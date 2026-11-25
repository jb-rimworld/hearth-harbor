// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Money helpers of the module. All silver goes through Payments and the Ledger (rule 5).</summary>
    public static class UwPay
    {
        public static string T(string key, params NamedArgument[] args) => ("HH.Underworld." + key).Translate(args).ToString();

        /// <summary>True when the Inn module books this pawn as a guest (it then owns the budget).</summary>
        static bool InnBooks(Pawn p) => p != null && GuestApi.Impl != null && Modules.On(ModuleId.Inn) && GuestApi.Impl.Info(p) != null;

        /// <summary>Spending budget of a pawn for underworld services. A shady pawn the Inn does not book (a gang member) spends the silver he carries
        /// (same source as Payments); everyone else has the guest budget of <see cref="GuestApi"/> (the Inn's, or the fallback of a vanilla visitor,
        /// who may pay "virtually" in <see cref="Charge"/>).</summary>
        public static Money Budget(Pawn p)
        {
            if (p == null) return Money.Zero;
            return !InnBooks(p) && Underworld.IsShady(p) ? Payments.Available(p) : GuestApi.Budget(p);
        }

        /// <summary>Takes money from a guest and books it to an account (not necessarily the till). Mirrors GuestApi.ChargeGuest, which only knows the till.</summary>
        public static bool Charge(Pawn guest, Money price, string toAccount, string category, string memo, bool ignoreBudget = false)
        {
            if (guest == null || price.Micro <= 0) return price.Micro == 0;
            GuestApi.EnsureFunds(guest, price);
            if (!ignoreBudget && Budget(guest) < price) return false;
            bool inn = GuestApi.Impl != null && Modules.On(ModuleId.Inn);
            bool booked = InnBooks(guest);
            if (!Payments.TryCharge(guest, price, toAccount, memo, out _))
            {
                if (inn) return false;
                // vanilla visitors without silver pay "virtually" in fallback mode
                if (!Ledger.Current.Post("world", toAccount, price, memo + " (visitor)")) return false;
            }
            // a fee forced past the budget must not eat the budget a second time (only the Inn's own record keeps its Spent total)
            if (!ignoreBudget || booked) GuestApi.Spend(guest, price, category);
            return true;
        }

        /// <summary>Pays a guest out of an account: books it to the world and puts the silver into the guest's inventory (whole coins only).</summary>
        public static Money PayOut(Pawn guest, Money amount, string fromAccount, string memo)
        {
            int coins = Math.Max(0, amount.WholeSilver);
            if (coins <= 0 || guest == null) return Money.Zero;
            if (!Ledger.Current.Post(fromAccount, "world", Money.FromSilver(coins), memo)) return Money.Zero;
            GiveSilver(guest, coins);
            return Money.FromSilver(coins);
        }

        public static void GiveSilver(Pawn p, int coins)
        {
            if (p?.inventory == null || coins <= 0) return;
            var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = coins;
            if (!p.inventory.innerContainer.TryAdd(s)) s.Destroy();
        }

        public static void Comfort(Pawn p)
        {
#if RW16PLUS
            p.GainComfortFromCellIfPossible(1);
#else
            p.GainComfortFromCellIfPossible();
#endif
        }

        public static Money Balance(string acc) => Ledger.Current?.Balance(acc) ?? Money.Zero;

        // ---- the colony pays (pit opponent fee, smuggler crates, protection money) -------------------------------------------
        static int fundsFrame = -999, fundsMap = -1; static Money fundsCache;

        /// <summary>All the colony could pay with right now: the till, then stock silver on the map (not forbidden, in a stockpile or the home area,
        /// <see cref="Hearth.Dlc.ColonySilver.CountStock"/>), then the Silverline account. The silver count and the bank balance are cached for 30 frames
        /// (gizmos and the tab ask every frame); the till is read fresh.</summary>
        public static Money ColonyFunds(Map map)
        {
            if (map == null || Ledger.Current == null) return Money.Zero;
            int f = UnityEngine.Time.frameCount;
            if (fundsMap != map.uniqueID || f - fundsFrame >= 30 || f < fundsFrame)
            {
                fundsCache = Money.FromSilver(Hearth.Dlc.ColonySilver.CountStock(map)) + Silverline.BankApi.ColonyBalance; fundsMap = map.uniqueID; fundsFrame = f;
            }
            return Money.Max(Money.Zero, Balance(Payments.ColonyTill(map))) + fundsCache;
        }

        /// <summary>Drops the cached silver count (after silver was taken, forbidden or added outside a payment).</summary>
        public static void ForgetFunds() => fundsFrame = -999;

        /// <summary>The reason shown when the colony cannot pay: "the colony has 12 of 40 silver".</summary>
        public static string ShortOf(Map map, Money price)
        {
            var have = ColonyFunds(map);
            return T("Pay.Short", Math.Floor(have.Silver + 1e-9).ToString("0"), PitFighters.Silver(price));
        }

        /// <summary>Pays <paramref name="amount"/> for the colony into <paramref name="toAccount"/>: the till first, then stock silver, then the bank.
        /// All-or-nothing: false and nothing moved when everything together is not enough. Coins are whole: the change of the last coin goes to the till.</summary>
        public static bool PayFromColony(Map map, Money amount, string toAccount, string memo)
        {
            var l = Ledger.Current;
            if (map == null || l == null || amount.Micro < 0) return false;
            if (amount.Micro == 0) return true;
            ForgetFunds();
            string till = Payments.ColonyTill(map);
            Money tillHas = Money.Max(Money.Zero, l.Balance(till)), bank = Silverline.BankApi.ColonyBalance;
            int coins = Hearth.Dlc.ColonySilver.CountStock(map);
            Money fromTill = Money.Min(tillHas, amount), left = amount - fromTill;
            Money fromBank = Money.Zero; int fromCoins = 0;
            if (left.Micro > 0)
            {
                int need = left.CeilSilver;
                if (coins >= need) fromCoins = need;
                else { fromCoins = coins; fromBank = left - Money.FromSilver(coins); }
                if (fromBank > bank) return false;
            }
            if (fromTill.Micro > 0 && !l.Post(till, toAccount, fromTill, memo)) return false;
            if (fromCoins > 0)
            {
                if (!Hearth.Dlc.ColonySilver.TakeStock(map, fromCoins))
                {
                    if (fromTill.Micro > 0) l.Post(toAccount, till, fromTill, memo + " (undone)", allowNegative: true);
                    return false;
                }
                // the coins leave the map as things and enter the ledger as money; what the last coin paid too much goes to the till
                var coinMoney = Money.FromSilver(fromCoins);
                var due = left - fromBank;
                l.Post("world", toAccount, coinMoney, memo + " (colony silver)");
                if (coinMoney > due) l.Post(toAccount, till, coinMoney - due, "Change from " + memo, allowNegative: true);
            }
            if (fromBank.Micro > 0 && !Silverline.BankApi.TryPay(toAccount, fromBank, memo + " (bank)"))
            {
                // the coins are gone: book their value back into the till rather than spawn silver
                var back = fromTill + (fromCoins > 0 ? left - fromBank : Money.Zero);
                if (back.Micro > 0) l.Post(toAccount, till, back, memo + " (undone)", allowNegative: true);
                return false;
            }
            ForgetFunds();
            return true;
        }

        /// <summary>Moves what is left on an account into the colony till.</summary>
        public static void Sweep(string from, Map map, string memo)
        {
            var l = Ledger.Current; if (l == null) return;
            var b = l.Balance(from);
            if (b.Micro > 0) l.Post(from, Payments.ColonyTill(map), b, memo);
        }

        /// <summary>Picks a random market-value-weighted item def for loot, crates and bounties.</summary>
        public static Thing MakeGoods(float minValue, float maxValue, Func<ThingDef, bool> filter = null)
        {
            List<ThingDef> cands;
            cands = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Item && !d.IsCorpse && d.tradeability != Tradeability.None
                && d.BaseMarketValue >= minValue && d.BaseMarketValue <= maxValue && (d.IsWeapon || d.IsApparel || d.IsMedicine || d.IsDrug)
                && (filter == null || filter(d))).ToList();
            if (cands.Count == 0) return null;
            var def = cands.RandomElement();
            var stuff = def.MadeFromStuff ? GenStuff.RandomStuffFor(def) : null;
            var t = ThingMaker.MakeThing(def, stuff);
            if (t.def.stackLimit > 1) t.stackCount = Math.Max(1, Math.Min(t.def.stackLimit, (int)(maxValue / Math.Max(1f, t.MarketValue) * Rand.Range(0.3f, 1f))));
            return t;
        }
    }
}
