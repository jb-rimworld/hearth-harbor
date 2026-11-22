// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth
{
    public enum PaymentMethod { None, Cash, Card, BankTransfer, Prepaid, Tab, Barter, Chips }

    /// <summary>A way a pawn can pay. Providers are asked in descending Priority; the first that can cover the whole amount wins.</summary>
    public interface IPaymentProvider
    {
        int Priority { get; }
        string Id { get; }
        /// <summary>How much this provider could pay for the pawn right now.</summary>
        Money Available(Pawn payer);
        /// <summary>Take the money from the payer and post it to <paramref name="toAccount"/> in the ledger. Must be all-or-nothing.</summary>
        bool TryPay(Pawn payer, Money amount, string toAccount, string memo);
        PaymentMethod Method { get; }
    }

    /// <summary>
    /// Optional: a payment/payout provider may also implement this to belong to a module. Payments then skips it while the
    /// module is switched off or paused and runs it inside that module's error barrier. Providers without it count as Core.
    /// </summary>
    public interface IModuleScoped { ModuleId Module { get; } }

    /// <summary>A way to hand money to a pawn (winnings, insurance claims, refunds, fence payments). First provider that succeeds wins.</summary>
    public interface IPayoutProvider
    {
        int Priority { get; }
        string Id { get; }
        /// <summary>Move <paramref name="amount"/> from the ledger account to the recipient. All-or-nothing.</summary>
        bool TryPayout(Pawn recipient, Money amount, string fromAccount, string memo, bool allowOverdraft);
    }

    /// <summary>
    /// Single entry point for every purchase and payout in Hearth &amp; Harbor (8.1). Modules never move silver by hand:
    /// they call Payments.TryCharge(payer, price, Payments.ColonyTill(map), "Inn: night in room 3", out method) and
    /// Payments.TryPayout(winner, prize, houseAccount, "Slots: jackpot").
    /// Money for the colony first lands in the till account of the map; the till is emptied at midnight into the bank
    /// (if Silverline has an account and direct deposit is on) or as physical silver next to a till/front desk.
    /// </summary>
    public static class Payments
    {
        static readonly List<IPaymentProvider> providers = new List<IPaymentProvider> { new InventorySilverProvider() };
        static readonly List<IPayoutProvider> payoutProviders = new List<IPayoutProvider> { new InventorySilverPayout() };
        public static IReadOnlyList<IPaymentProvider> Providers => providers;
        public static IReadOnlyList<IPayoutProvider> PayoutProviders => payoutProviders;

        /// <summary>Report category of the charge/payout that is running right now (the Ledger picks it up for posts that name none).</summary>
        internal static string ActiveCategory;

        public static void Register(IPaymentProvider p)
        {
            providers.RemoveAll(x => x.Id == p.Id);
            providers.Add(p);
            providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static void RegisterPayout(IPayoutProvider p)
        {
            payoutProviders.RemoveAll(x => x.Id == p.Id);
            payoutProviders.Add(p);
            payoutProviders.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static string ColonyTill(Map map) => "colony:till:" + (map?.uniqueID ?? 0);

        static ModuleId ModuleOf(object provider) => (provider as IModuleScoped)?.Module ?? ModuleId.Core;

        /// <summary>Everything the pawn could pay right now, summed over the providers of enabled modules.</summary>
        public static Money Available(Pawn payer)
        {
            var sum = Money.Zero;
            for (int i = 0; i < providers.Count; i++)
            {
                var p = providers[i]; var mod = ModuleOf(p);
                if (!Modules.On(mod)) continue;
                sum += Guard.Run(mod, "Payments.Available." + p.Id, () => p.Available(payer), Money.Zero);
            }
            return sum;
        }

        /// <summary>
        /// Charges the payer. One provider (priority order) is used if it can cover the whole price; otherwise the price is split
        /// across providers in priority order. All-or-nothing: if everything together is not enough, nothing is charged.
        /// <paramref name="category"/> (optional) feeds the daily income report (Ledger.DayIncome).
        /// </summary>
        public static bool TryCharge(Pawn payer, Money amount, string toAccount, string memo, out PaymentMethod method, string category = null)
        {
            method = PaymentMethod.None;
            if (payer == null || amount.Micro < 0) return false;
            if (amount.Micro == 0) { method = PaymentMethod.Cash; return true; }
            string prevCat = ActiveCategory; ActiveCategory = category ?? prevCat;
            try
            {
                var avail = new List<KeyValuePair<IPaymentProvider, Money>>(providers.Count);
                var total = Money.Zero;
                for (int i = 0; i < providers.Count; i++)
                {
                    var p = providers[i]; var mod = ModuleOf(p);
                    if (!Modules.On(mod)) continue;
                    var a = Guard.Run(mod, "Payments.Available." + p.Id, () => p.Available(payer), Money.Zero);
                    if (a.Micro <= 0) continue;
                    avail.Add(new KeyValuePair<IPaymentProvider, Money>(p, a)); total += a;
                    if (a >= amount && TryOne(p, mod, payer, amount, toAccount, memo))
                    { method = p.Method; RaisePaid(payer, amount, toAccount, memo, method); return true; }
                }
                if (total < amount) return false;
                // split across providers, highest priority first
                var left = amount; PaymentMethod first = PaymentMethod.None; var paid = Money.Zero;
                for (int i = 0; i < avail.Count && left.Micro > 0; i++)
                {
                    var p = avail[i].Key; var take = Money.Min(avail[i].Value, left);
                    if (!TryOne(p, ModuleOf(p), payer, take, toAccount, memo + " (part)")) continue;
                    if (first == PaymentMethod.None) first = p.Method;
                    paid += take; left -= take;
                }
                if (left.Micro > 0)
                {
                    HLog.Warn("Payments", $"Split payment of {amount} for '{memo}' fell short by {left}; refunding {paid}");
                    if (paid.Micro > 0 && !TryPayout(payer, paid, toAccount, memo + " (refund)", null, true))
                        HLog.Error("Payments", $"Could not refund {paid} for '{memo}' after a failed split payment");
                    return false;
                }
                method = first; RaisePaid(payer, amount, toAccount, memo, method);
                return true;
            }
            finally { ActiveCategory = prevCat; }
        }

        static bool TryOne(IPaymentProvider p, ModuleId mod, Pawn payer, Money amount, string toAccount, string memo)
            => Guard.Run(mod, "Payments.TryPay." + p.Id, () => p.TryPay(payer, amount, toAccount, memo), false);

        /// <summary>
        /// Hands money to a pawn: the amount leaves <paramref name="fromAccount"/> and is given to the recipient (default: whole silver
        /// coins into the inventory; a fraction below one coin stays on the account). Returns false if nothing could be paid
        /// (account lacks funds, recipient unusable). <paramref name="allowOverdraft"/> lets the account go negative (refunds of money already banked).
        /// </summary>
        public static bool TryPayout(Pawn recipient, Money amount, string fromAccount, string memo, string category = null, bool allowOverdraft = false)
        {
            if (recipient == null || amount.Micro < 0 || string.IsNullOrEmpty(fromAccount)) return false;
            if (amount.Micro == 0) return true;
            string prevCat = ActiveCategory; ActiveCategory = category ?? prevCat;
            try
            {
                for (int i = 0; i < payoutProviders.Count; i++)
                {
                    var p = payoutProviders[i]; var mod = ModuleOf(p);
                    if (!Modules.On(mod)) continue;
                    if (Guard.Run(mod, "Payments.TryPayout." + p.Id, () => p.TryPayout(recipient, amount, fromAccount, memo, allowOverdraft), false)) return true;
                }
                return false;
            }
            finally { ActiveCategory = prevCat; }
        }

        /// <summary>Raised after every successful payment (reports, renown, achievements).</summary>
        public static event Action<Pawn, Money, string, string, PaymentMethod> Paid;

        /// <summary>Raises <see cref="Paid"/>; each subscriber is called on its own so a throwing one cannot break the payment.</summary>
        internal static void RaisePaid(Pawn payer, Money amount, string toAccount, string memo, PaymentMethod method)
        {
            var h = Paid; if (h == null) return;
            foreach (var d in h.GetInvocationList())
            {
                try { ((Action<Pawn, Money, string, string, PaymentMethod>)d)(payer, amount, toAccount, memo, method); }
                catch (Exception e) { HLog.Error("Payments", "Subscriber of Payments.Paid threw (ignored)", e); }
            }
        }

        /// <summary>Income for the colony that is not paid by a pawn (e.g. a booking fee): world → till.</summary>
        public static void CreditColony(Map map, Money amount, string memo, string category = null)
            => Ledger.Current?.Post("world", ColonyTill(map), amount, memo, false, category ?? ActiveCategory);
    }

    /// <summary>Default provider: physical silver in the pawn's inventory (and wallet). Fractions of a coin are rounded up.</summary>
    public sealed class InventorySilverProvider : IPaymentProvider
    {
        public int Priority => 0;
        public string Id => "core.inventorySilver";
        public PaymentMethod Method => PaymentMethod.Cash;

        public Money Available(Pawn payer)
        {
            if (payer?.inventory?.innerContainer == null) return Money.Zero;
            int n = 0;
            foreach (var t in payer.inventory.innerContainer) if (t.def == ThingDefOf.Silver) n += t.stackCount;
            return Money.FromSilver(n);
        }

        public bool TryPay(Pawn payer, Money amount, string toAccount, string memo)
        {
            int coins = amount.CeilSilver; // integer ceiling: 1.00001 silver = 2 coins, 1.0 = 1 coin
            if (coins <= 0) return true;
            if (Available(payer).WholeSilver < coins) return false;
            int left = coins;
            foreach (var t in payer.inventory.innerContainer.ToList())
            {
                if (left <= 0) break;
                if (t.def != ThingDefOf.Silver) continue;
                int take = Math.Min(left, t.stackCount);
                t.SplitOff(take).Destroy();
                left -= take;
            }
            // coins leave the world as physical objects and enter the ledger as money
            Ledger.Current?.Post("world", toAccount, Money.FromSilver(coins), memo + " (cash)");
            return true;
        }
    }

    /// <summary>Default payout: whole silver coins into the recipient's inventory (dropped next to them if it cannot carry them); the fraction stays on the account.</summary>
    public sealed class InventorySilverPayout : IPayoutProvider
    {
        public int Priority => 0;
        public string Id => "core.inventorySilverPayout";

        public bool TryPayout(Pawn recipient, Money amount, string fromAccount, string memo, bool allowOverdraft)
        {
            int coins = amount.WholeSilver;
            if (coins <= 0) return true; // less than one coin: nothing physical to hand over, the fraction stays on the account
            var l = Ledger.Current; if (l == null) return false;
            var paid = Money.FromSilver(coins);
            if (!l.Post(fromAccount, "world", paid, memo + " (cash)", allowOverdraft)) return false;
            int stackMax = Math.Max(1, ThingDefOf.Silver.stackLimit);
            bool ok = true; int delivered = 0;
            for (int left = coins; left > 0 && ok;)
            {
                int n = Math.Min(left, stackMax);
                var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = n;
                bool given = recipient.inventory != null && recipient.inventory.innerContainer.TryAdd(silver, true);
                if (!given && !silver.Destroyed && silver.stackCount > 0)
                {
                    // inventory refused: drop it next to the pawn
                    var map = recipient.MapHeld;
                    if (map == null || !GenPlace.TryPlaceThing(silver, recipient.PositionHeld, map, ThingPlaceMode.Near)) { silver.Destroy(); ok = false; }
                }
                if (ok) delivered += n;
                left -= n;
            }
            if (!ok)
            {
                // book back exactly what did not arrive
                int lost = coins - delivered;
                HLog.Error("Payments", $"Payout to {recipient}: {lost} of {coins} silver could not be placed; booking it back to {fromAccount}");
                l.Post("world", fromAccount, Money.FromSilver(lost), memo + " (payout failed)", true);
                return delivered > 0;
            }
            return true;
        }
    }

    /// <summary>
    /// Empties till accounts once per day (simplified cash, D24). A sink (Silverline) may take the money first;
    /// otherwise silver is spawned at a till spot: a building with CompTillSpot, else the colony's trade spot, else near the map's colonists.
    /// </summary>
    public static class Tills
    {
        public interface ISink { bool TryTake(Map map, Money amount, string memo); }
        public static ISink BankSink;

        public static void FlushAll()
        {
            var l = Ledger.Current; if (l == null) return;
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
            {
                string acc = Payments.ColonyTill(map);
                var bal = l.Balance(acc);
                if (bal.Micro <= 0) continue;
                var sink = BankSink;
                if (sink != null && Modules.On(ModuleId.Bank) && Guard.Run(ModuleId.Bank, "Tills.BankSink", () => sink.TryTake(map, bal, "Nightly deposit"), false)) continue;
                int coins = bal.WholeSilver; if (coins <= 0) continue;
                var spot = TillSpot(map);
                int stack = Math.Max(1, ThingDefOf.Silver.stackLimit), placed = 0;
                // place in stack-limit chunks and book every chunk that really landed, so a failure half way never duplicates silver
                while (coins > 0)
                {
                    int n = Math.Min(coins, stack);
                    var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = n;
                    if (!GenPlace.TryPlaceThing(silver, spot, map, ThingPlaceMode.Near))
                    {
                        HLog.Warn("Core", $"Till {map}: could not place {n} silver at {spot}; {coins} silver stay in the till");
                        break;
                    }
                    l.Post(acc, "world", Money.FromSilver(n), "Till emptied as silver");
                    placed += n; coins -= n;
                }
                if (placed > 0) HLog.Debug("Core", $"Till {map} emptied: {placed} silver at {spot}");
            }
        }

        public static IntVec3 TillSpot(Map map)
        {
            var b = map.listerBuildings.allBuildingsColonist.FirstOrDefault(x => x.TryGetComp<CompTillSpot>() != null);
            if (b != null) return b.InteractionCell.IsValid ? b.InteractionCell : b.Position;
            var col = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (col != null) return col.Position;
            return map.Center;
        }
    }

    /// <summary>Marks a building where nightly till money is put down as silver (front desk, cash register).</summary>
    public sealed class CompProperties_TillSpot : CompProperties { public CompProperties_TillSpot() { compClass = typeof(CompTillSpot); } }
    public sealed class CompTillSpot : ThingComp { }
}
