// SPDX-License-Identifier: MIT
// Integration suites of the bank: the tabs of insurance/investments/lottery inside the bank window, products paying through BankApi, trade income.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using Hearth.Testing;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Silverline
{
    public sealed class BankTabsSuite : HearthTestSuite
    {
        public override string Name => "bank.tabs";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map; var led = Ledger.Current;
            InsTest.Reset(0);   // closes any account and clears insurance state
            c.TestReset(true);
            BT.Fund(Money.FromSilver(5000));
            var fg = FinanceGame.Current; fg.EnsureInit(); var lot = LotteryGame.Current; lot.EnsureInit();

            // products pay through the bank account (BankApi helpers)
            var b0 = BankApi.ColonyBalance;
            t.Check(BankApi.Ready && b0 == Money.FromSilver(5000), "bank ready with 5000: " + b0.ToStringShort());
            t.Check(!BankApi.TryPay("insurer:test", Money.FromSilver(6000), "too much") && BankApi.ColonyBalance == b0, "TryPay refuses more than the account holds");
            t.Check(BankApi.TryPay("insurer:test", Money.FromSilver(100), "test premium") && BankApi.ColonyBalance == b0 - Money.FromSilver(100), "TryPay moves money from the account");
            t.Check(BankApi.Receive("insurer:test", Money.FromSilver(40), "test benefit") && BankApi.ColonyBalance == b0 - Money.FromSilver(60), "Receive books money onto the account");
            t.Check(BankApi.Receive("insurer:test", Money.FromSilver(500), "overdrawn benefit") && led.Balance("insurer:test").Micro < 0, "the source of a benefit may go negative");
            led.Post("world", "insurer:test", led.Balance("insurer:test").Abs, "test cleanup"); led.Post("insurer:test", "world", led.Balance("insurer:test"), "test cleanup");
            c.TestReset(true); BT.Fund(Money.FromSilver(5000)); InsTest.Reset(0); c.TestReset(true); BT.Fund(Money.FromSilver(5000));

            var comp = InsTest.Comp;
            var res = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.GoodEnough, null, 2000, null, null);
            t.Check(res.Ok && res.contract != null, "policy signed: " + res.error); if (!res.Ok || res.contract == null) yield break;
            t.Check(BankApi.ColonyBalance < Money.FromSilver(5000) && InsTest.Insurer.Micro > 0, "premium came out of the bank account: " + BankApi.ColonyBalance.ToStringShort() + " (insurer " + InsTest.Insurer.ToStringShort() + ")");
            t.Check(InsTest.Till == Money.Zero, "the till was not touched");
            var before = BankApi.ColonyBalance;
            string how = InsurerBooks.Payout(map, Money.FromSilver(250), "test benefit");
            t.Check(BankApi.ColonyBalance == before + Money.FromSilver(250) && how == Ins.T("Source.PaidBank"), "benefit paid onto the bank account: " + how);
            t.Check(BT.LedgerSum.Micro == 0, "ledger balanced");

            // the window hosts all tabs
            int err0 = HLog.ErrorCount;
            var w = new Window_Bank(map, BankSite.Remote, null);
            Find.WindowStack.Add(w); yield return 30;
            var ids = BankTabs.VisibleTabs.Select(x => x.Id).ToList();
            t.Check(new[] { "lobby", "loans", "history", "terms", "invest", "lottery", "insurance" }.All(ids.Contains), "all tabs visible: " + string.Join(",", ids));
            var orders = BankTabs.VisibleTabs.Select(x => x.Order).ToList();
            t.Check(orders.Distinct().Count() == orders.Count && orders.SequenceEqual(orders.OrderBy(o => o)), "tab orders are unique and sorted: " + string.Join(",", orders));
            var rect = new Rect(0, 0, w.InitialSize.x, w.InitialSize.y);
            int drawn = 0;
            foreach (var id in ids)
            {
                w.TabId = id;
                if (id == "insurance") { for (int p = 0; p < 4; p++) { InsuranceUI.ShowPage(p, p == 0); w.DoWindowContents(rect); drawn++; } InsuranceUI.ShowPage(0); }
                else if (id == "invest") { for (int p = 0; p < 4; p++) { InvestTab.TestSetPage(p); w.DoWindowContents(rect); drawn++; } InvestTab.TestSetPage(0); }
                else { w.DoWindowContents(rect); drawn++; }
                yield return 5;
            }
            t.Check(HLog.ErrorCount == err0, $"{drawn} tab draws inside the bank window without errors: " + (HLog.ErrorCount - err0));
            t.Check(!Guard.Paused.Contains(ModuleId.Bank), "bank module not paused by the error barrier");
            t.Check(InsuranceUI.Blue.b > 0.6f, "insurance colours go back to Clawback blue outside the bank window");
            t.Check(!Guard.Paused.Contains(ModuleId.Bank), "still not paused");
            w.Close();
            c.TestReset(true);
            InsTest.RestoreSettings();
        }
    }

    public sealed class BankTradeSuite : HearthTestSuite
    {
        public override string Name => "bank.trade";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 2000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            var colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (colonist == null) { t.Note("no colonist"); yield break; }
            HSettings.Set("bank.directDeposit", 1f); HSettings.Set("bank.tradeDeposit", 1f); HSettings.Set("bank.directDepositFee", 0.005f);
            Tradeable Offer(int coins)
            {
                var tr = new Tradeable(); var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = coins;
                tr.thingsTrader.Add(silver); tr.ForceToSource(coins); return tr;
            }
            TradeSession.trader = colonist; TradeSession.playerNegotiator = colonist; TradeSession.giftMode = false;
            try
            {
                c.TestReset(false);
                var off0 = Offer(500);
                t.Check(off0.ActionToDo == TradeAction.PlayerBuys && off0.CountToTransferToSource == 500, "test offer: the colony takes 500 silver (" + off0.ActionToDo + ")");
                t.Check(!BankTrade.ShouldDivert(off0), "no account: silver arrives as coins");
                c.TestReset(true);
                HSettings.Set("bank.tradeDeposit", 0f);
                t.Check(!BankTrade.ShouldDivert(Offer(500)), "setting off: silver arrives as coins");
                HSettings.Set("bank.tradeDeposit", 1f); HSettings.Set("bank.directDeposit", 0f);
                t.Check(!BankTrade.ShouldDivert(Offer(500)), "direct deposit off: silver arrives as coins");
                HSettings.Set("bank.directDeposit", 1f);
                c.TestReset(true);
                var off = Offer(500);
                t.Check(BankTrade.ShouldDivert(off), "account + direct deposit: the silver is diverted");
                int mapSilver = BT.MapSilver(map); var ledSum = BT.LedgerSum;
                var silverThing = off.thingsTrader[0];
                off.ResolveTrade();
                t.Check(BankApi.ColonyBalance == Money.FromSilver(497.5), "500 silver minus the 0.5 % deposit fee reached the account: " + BankApi.ColonyBalance.ToStringFull());
                t.Check(silverThing.Destroyed && off.thingsTrader.Count == 0, "the trader's coins are gone");
                t.Check(BT.MapSilver(map) == mapSilver, "no coins dropped on the map");
                t.Check(BT.LedgerSum == ledSum, "ledger balanced");
                // partial: the trader holds 800, the deal takes 300
                var tr2 = new Tradeable(); var big = ThingMaker.MakeThing(ThingDefOf.Silver); big.stackCount = 800; tr2.thingsTrader.Add(big); tr2.ForceToSource(300);
                var b1 = BankApi.ColonyBalance; tr2.ResolveTrade();
                t.Check(big.stackCount == 500 && BankApi.ColonyBalance == b1 + Money.FromSilver(298.5), "partial deals leave the rest with the trader: " + big.stackCount);
                // the player paying (selling direction) is vanilla
                var pay = new Tradeable(); var mine = ThingMaker.MakeThing(ThingDefOf.Silver); mine.stackCount = 100; pay.thingsColony.Add(mine); pay.ForceToDestination(100);
                t.Check(!BankTrade.ShouldDivert(pay), "paying the trader is left to vanilla");
                // a caravan negotiator (not on a home map) keeps coins
                var away = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                TradeSession.playerNegotiator = away;
                t.Check(!BankTrade.ShouldDivert(Offer(500)), "negotiator off the home map (caravan): silver arrives as coins");
                Find.WorldPawns.PassToWorld(away, PawnDiscardDecideMode.Discard);
            }
            finally { TradeSession.Close(); TradeSession.playerNegotiator = null; c.TestReset(true); HSettings.Reset(HSettings.Find("bank.tradeDeposit")); }
        }
    }
}
#endif
