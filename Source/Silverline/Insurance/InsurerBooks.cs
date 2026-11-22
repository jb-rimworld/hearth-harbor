// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>
    /// All money between the colony and Clawback Mutual. The insurer is the ledger account "insurer:clawback":
    /// premiums go in (from the bank account, else the till, else silver in storage), benefits come out (into the bank
    /// account, else as silver in a drop capsule plus the coin fraction in the till). Every movement is a ledger post.
    /// </summary>
    public static class InsurerBooks
    {
        public const string Account = "insurer:clawback";

        public static Map HomeMap => Find.CurrentMap != null && Find.CurrentMap.IsPlayerHome ? Find.CurrentMap : Find.AnyPlayerHomeMap;

        /// <summary>Everything the colony could pay with right now: bank balance (if a bank account exists), till, silver in storage.</summary>
        public static Money Spendable(Map map)
        {
            var l = Ledger.Current; var sum = Money.Zero;
            if (BankApi.Ready) sum += BankApi.ColonyBalance;
            if (l != null && map != null) sum += l.Balance(Payments.ColonyTill(map));
            if (map != null) sum += Money.FromSilver(StorageSilver(map));
            return sum;
        }

        public static string SourceLabel(Map map)
        {
            if (BankApi.Ready) return Ins.T("Source.Bank");
            return Ins.T("Source.Till");
        }

        /// <summary>Silver coins lying in storage or the home area (not forbidden): what a nightly till would also use.</summary>
        public static int StorageSilver(Map map)
        {
            int n = 0;
            foreach (var t in SilverThings(map)) n += t.stackCount;
            return n;
        }

        static IEnumerable<Thing> SilverThings(Map map)
        {
            var list = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (!t.Spawned || t.IsForbidden(Faction.OfPlayer)) continue;
                if (t.IsInAnyStorage() || map.areaManager.Home[t.Position]) yield return t;
            }
        }

        static bool TakeSilver(Map map, int coins)
        {
            if (StorageSilver(map) < coins) return false;
            int left = coins;
            foreach (var t in SilverThings(map).ToList())
            {
                if (left <= 0) break;
                int take = Math.Min(left, t.stackCount);
                t.SplitOff(take).Destroy();
                left -= take;
            }
            return left <= 0;
        }

        /// <summary>Debits a premium. Returns false and books nothing if the colony cannot pay.</summary>
        public static bool TryPay(Map map, Money amount, string memo, out string source)
        {
            source = null;
            var l = Ledger.Current; if (l == null) return false;
            if (amount.Micro <= 0) { source = "free"; return true; }
            if (BankApi.TryPay(Account, amount, memo)) { source = Ins.T("Source.Bank"); return true; }
            if (map == null) return false;
            string till = Payments.ColonyTill(map);
            if (l.Balance(till) >= amount && l.Post(till, Account, amount, memo)) { source = Ins.T("Source.Till"); return true; }
            int coins = (int)Math.Ceiling(amount.Silver - 1e-9);
            if (StorageSilver(map) >= coins && TakeSilver(map, coins))
            {
                l.Post("world", Account, Money.FromSilver(coins), memo + " (cash)");
                var change = Money.FromSilver(coins) - amount;
                if (change.IsPositive) l.Post(Account, till, change, "Change from " + memo, allowNegative: true);
                source = Ins.T("Source.Silver");
                return true;
            }
            return false;
        }

        /// <summary>Pays out a benefit. Bank account if there is one, else silver in a drop capsule (+ the coin fraction in the till).</summary>
        public static string Payout(Map map, Money amount, string memo)
        {
            var l = Ledger.Current; if (l == null || amount.Micro <= 0) return null;
            if (BankApi.Receive(Account, amount, memo)) return Ins.T("Source.PaidBank");
            map = map ?? HomeMap;
            if (map == null) { l.Post(Account, "colony:till:0", amount, memo, allowNegative: true); return Ins.T("Source.PaidTill"); }
            string till = Payments.ColonyTill(map);
            int coins = amount.WholeSilver;
            var frac = amount - Money.FromSilver(coins);
            string how = Ins.T("Source.PaidTill");
            if (coins > 0)
            {
                var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = coins;
                try
                {
                    IntVec3 spot = DropCellFinder.TradeDropSpot(map);
                    TradeUtility.SpawnDropPod(spot, map, silver);
                    l.Post(Account, "world", Money.FromSilver(coins), memo + " (capsule)", allowNegative: true);
                    how = Ins.T("Source.PaidCapsule", coins);
                }
                catch (Exception e)
                {
                    HLog.Warn("Bank", "Insurance capsule failed, paying into the till: " + e.Message);
                    l.Post(Account, till, Money.FromSilver(coins), memo, allowNegative: true);
                }
            }
            if (frac.IsPositive) l.Post(Account, till, frac, memo + " (fraction)", allowNegative: true);
            return how;
        }

        /// <summary>A refund (e.g. courtesy credit): same path as a benefit.</summary>
        public static string Refund(Map map, Money amount, string memo) => Payout(map, amount, memo);

        public static Money InsurerBalance => Ledger.Current?.Balance(Account) ?? Money.Zero;
    }
}
