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
