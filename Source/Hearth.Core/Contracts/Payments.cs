// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth
{
    public enum PaymentMethod { None, Cash, Card, BankTransfer, Prepaid, Tab, Barter, Chips }

    /// <summary>A way a pawn can pay. Providers are asked in descending Priority; the first that succeeds wins.</summary>
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
    /// Single entry point for every purchase in Hearth &amp; Harbor (8.1). Modules never move silver by hand:
    /// they call Payments.TryCharge(payer, price, Payments.ColonyTill(map), "Inn: night in room 3", out method).
    /// Money for the colony first lands in the till account of the map; the till is emptied at midnight into the bank
    /// (if Silverline has an account and direct deposit is on) or as physical silver next to a till/front desk.
    /// </summary>
    public static class Payments
    {
        static readonly List<IPaymentProvider> providers = new List<IPaymentProvider> { new InventorySilverProvider() };
        public static IReadOnlyList<IPaymentProvider> Providers => providers;

        public static void Register(IPaymentProvider p)
        {
            providers.RemoveAll(x => x.Id == p.Id);
            providers.Add(p);
            providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static string ColonyTill(Map map) => "colony:till:" + (map?.uniqueID ?? 0);

        public static Money Available(Pawn payer)
        {
            var sum = Money.Zero;
            foreach (var p in providers) sum += Guard.Run(ModuleId.Core, "Payments.Available." + p.Id, () => p.Available(payer), Money.Zero);
            return sum;
        }

        public static bool TryCharge(Pawn payer, Money amount, string toAccount, string memo, out PaymentMethod method)
        {
            method = PaymentMethod.None;
            if (payer == null || amount.Micro < 0) return false;
            if (amount.Micro == 0) { method = PaymentMethod.Cash; return true; }
            foreach (var p in providers)
            {
                bool ok = Guard.Run(ModuleId.Core, "Payments.TryPay." + p.Id, () => p.Available(payer) >= amount && p.TryPay(payer, amount, toAccount, memo), false);
                if (ok) { method = p.Method; Paid?.Invoke(payer, amount, toAccount, memo, method); return true; }
            }
            return false;
        }

        /// <summary>Raised after every successful payment (reports, renown, achievements).</summary>
        public static event Action<Pawn, Money, string, string, PaymentMethod> Paid;

        /// <summary>Income for the colony that is not paid by a pawn (e.g. a booking fee): world → till.</summary>
        public static void CreditColony(Map map, Money amount, string memo) => Ledger.Current?.Post("world", ColonyTill(map), amount, memo);
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
            int coins = (int)Math.Ceiling(amount.Silver - 1e-9);
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
                if (BankSink != null && Guard.Run(ModuleId.Bank, "Tills.BankSink", () => BankSink.TryTake(map, bal, "Nightly deposit"), false)) continue;
                int coins = bal.WholeSilver; if (coins <= 0) continue;
                var spot = TillSpot(map);
                var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = coins;
                if (GenPlace.TryPlaceThing(silver, spot, map, ThingPlaceMode.Near))
                {
                    l.Post(acc, "world", Money.FromSilver(coins), "Till emptied as silver");
                    HLog.Debug("Core", $"Till {map} emptied: {coins} silver at {spot}");
                }
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
