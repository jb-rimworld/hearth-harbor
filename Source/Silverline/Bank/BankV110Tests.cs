// SPDX-License-Identifier: MIT
// In-game suites of Hearth & Harbor 1.1.0 for the bank: net worth with investments (F22), the deposit slider (F23), the terms page and the windows (F19, F35), sounds (F31).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Testing;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    public sealed class BankNetWorthSuite : HearthTestSuite
    {
        public override string Name => "bank.networth";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); BT.Fund(Money.FromSilver(5000)); BT.ClearStockpileSilver(t);
            var g = FinanceGame.Current; g.EnsureInit();
            t.Check(g.initialized, "finance initialised");
            c.InvalidateCache();
            var inv0 = c.InvestmentsValue; var nw0 = c.NetWorth;
            t.Check(c.NetWorth == c.Balance + Money.FromSilver(c.CashAtHome) + inv0 - c.Debt, "net worth = account + cash + investments - debts: " + c.NetWorth.ToStringShort());

            // securities count at market value
            var buy = g.Buy("SL_Asset_Index", Money.FromSilver(1000)); t.Check(buy.Ok, "bought the index: " + buy.Message);
            c.InvalidateCache();
            double invSilver = (c.InvestmentsValue - inv0).Silver;
            t.Check(invSilver > 950 && invSilver <= 1000.5, "the position counts at market value: " + invSilver.ToString("0.00"));
            t.Check(Math.Abs((c.NetWorth - nw0).Silver) < 60, "buying only swaps cash for a position, the net worth stays: " + (c.NetWorth - nw0).ToStringShort());
            t.Check(c.InvestmentParts.Securities > 0, "securities part is filled");
            var a = g.Asset("SL_Asset_Index"); double oldPrice = a.price; a.price = oldPrice * 1.5; c.InvalidateCache();
            t.Check((c.NetWorth - nw0).Silver > 400, "a price rise of 50 % shows in the net worth at once: +" + (c.NetWorth - nw0).Silver.ToString("0.0"));
            a.price = oldPrice * 0.5; c.InvalidateCache();
            t.Check((c.NetWorth - nw0).Silver < -400, "a price fall shows too: " + (c.NetWorth - nw0).Silver.ToString("0.0"));
            a.price = oldPrice; c.InvalidateCache();

            // patience bonds
            var bondBefore = c.InvestmentParts.Bonds;
            var bond = g.OpenPatienceBond(30, Money.FromSilver(200)); t.Check(bond.Ok, "patience bond opened: " + bond.Message);
            c.InvalidateCache(); t.Check(new Money(c.InvestmentParts.Bonds - bondBefore).Silver > 150, "bond counts in the investments: " + new Money(c.InvestmentParts.Bonds).ToStringShort());

            // trades invalidate the cache (the lobby changes at once, not after 250 ticks)
            var invMid = c.InvestmentsValue; g.Buy("SL_Asset_Index", Money.FromSilver(100));
            t.Check((c.InvestmentsValue - invMid).Silver > 90, "the next trade shows without waiting for the cache");

            // deposits in transit are not lost
            BT.AddSilver(t, 300); map.resourceCounter.UpdateResourceCounts(); c.InvalidateCache();
            var before = c.NetWorth;
            bool ok = c.TryDeposit(map, 300, BankSite.Remote, out string why); t.Check(ok, "deposit by remote: " + why);
            map.resourceCounter.UpdateResourceCounts(); c.InvalidateCache();
            t.Check(c.InTransit.Micro > 0, "a deposit is on its way");
            t.Check(Math.Abs((c.NetWorth - before).Silver) < 1.01, "money in transit still counts: " + (c.NetWorth - before).ToStringShort());

            // credit limit counts only a share of the investments
            HSettings.Set("bank.loanPortfolioShare", 0.5f); c.InvalidateCache();
            double invS = c.InvestmentsValue.Silver;
            t.Check(Math.Abs(c.LoanNetWorth.Silver - (c.NetWorth.Silver - 0.5 * invS)) < 0.01, "loan net worth counts half of the investments");
            HSettings.Set("bank.loanPortfolioShare", 1f); c.InvalidateCache();
            t.Check(Math.Abs(c.LoanNetWorth.Silver - c.NetWorth.Silver) < 0.01, "share 100 %: all of it");
            HSettings.Reset(HSettings.Find("bank.loanPortfolioShare"));
            double basis = c.LoanBasis(); t.Check(basis > 0 && !double.IsNaN(basis), "loan basis is sane: " + basis.ToString("0"));

            // robustness: a failing provider never breaks the bank
            var prov = BankGameComp.InvestmentsProvider; BankGameComp.InvestmentsProvider = () => throw new InvalidOperationException("test failure");
            c.InvalidateCache(); int err0 = HLog.ErrorCount;
            var nwFail = c.NetWorth; t.Check(c.InvestmentsValue.Micro == 0, "a failing provider counts as zero");
            t.Check(nwFail == c.Balance + c.InTransit + Money.FromSilver(c.CashAtHome) - c.Debt, "net worth falls back to account + cash");
            t.Check(HLog.ErrorCount == err0, "no error logged for it (only a warning)");
            BankGameComp.InvestmentsProvider = prov; c.InvalidateCache();
            t.Check(c.InvestmentsValue.Micro > 0, "provider back: investments count again");

            // selling brings it back to cash
            g.SellAll("SL_Asset_Index"); g.ClosePatienceBond(g.bonds.Last(b => !b.closed).id, true); c.InvalidateCache();
            t.Check(c.InvestmentsValue.Silver - inv0.Silver < 5, "everything sold: investments are back at the start (" + (c.InvestmentsValue - inv0).ToStringShort() + ")");
            t.Check(BT.LedgerSum.Micro == 0, "ledger balanced");
            // leave the market accounts as we found them (the finance suites expect an empty account for the index)
            var led = Ledger.Current;
            foreach (var acc in new[] { FinanceAccounts.Market("SL_Asset_Index"), FinanceAccounts.Patience }) { var b = led.Balance(acc); if (b.Micro != 0) led.Post(b.Micro > 0 ? acc : "world", b.Micro > 0 ? "world" : acc, b.Abs, "test cleanup", true); }
            BT.ClearStockpileSilver(t); map.resourceCounter.UpdateResourceCounts();
            c.TestReset(true);
            yield break;
        }
    }

    public sealed class BankTransferSuite : HearthTestSuite
    {
        public override string Name => "bank.transfer";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); BT.ClearStockpileSilver(t);
            // F23: a deposit is not limited to 2,000 silver (the old slider cap bank.podMaxLoad)
            BT.AddSilver(t, 6000);
            int dep = BankSilver.DepositableCoins(map);
            t.Check(dep >= 1000, "the colony holds " + dep + " silver in storage");
            HSettings.Set("bank.podMaxLoad", 500f);
            t.Check(dep > HSettings.GetInt("bank.podMaxLoad"), "the pod load setting (" + HSettings.GetInt("bank.podMaxLoad") + ") is below the amount");
            t.Check(c.TryDeposit(map, dep, BankSite.Branch, out string why), "deposit of everything (" + dep + ") works: " + why);
            HSettings.Reset(HSettings.Find("bank.podMaxLoad"));
            t.Check(c.Balance >= Money.FromSilver(dep * 0.9), "booked: " + c.Balance.ToStringShort());
            t.Check(BankSilver.DepositableCoins(map) == 0, "storage is empty afterwards");

            // review fixes: pod plan never explodes, pods follow bank.podMaxLoad, withdrawals in transit stay in the net worth
            {
                var pl = BankSilver.PodPlan(1_000_000, 2000, BankSilver.MaxPodsPerWithdrawal);
                t.Check(pl.Count == BankSilver.MaxPodsPerWithdrawal && pl.Sum() == 1_000_000, "1M silver comes in at most " + BankSilver.MaxPodsPerWithdrawal + " pods: " + pl.Count);
                pl = BankSilver.PodPlan(5000, 2000, 10); t.Check(pl.Count == 3 && pl.Sum() == 5000 && pl.All(x => x <= 2000), "5000 at 2000 per pod = 3 pods: " + string.Join(",", pl));
                pl = BankSilver.PodPlan(700, 500, 10); t.Check(pl.Count == 2 && pl.Sum() == 700, "700 at 500 per pod = 2 pods");
                pl = BankSilver.PodPlan(1, 2000, 10); t.Check(pl.Count == 1 && pl[0] == 1, "one coin is one pod");
                t.Check(BankSilver.PodPlan(0, 2000, 10).Count == 0 && BankSilver.PodPlan(-5, 2000, 10).Count == 0, "nothing to drop = no pods");
                pl = BankSilver.PodPlan(int.MaxValue, 1, 10); t.Check(pl.Count == 10 && pl.Sum(x => (long)x) == int.MaxValue, "extreme amounts keep the sum without overflow");
                c.TestReset(true); BT.Fund(Money.FromSilver(3000)); c.InvalidateCache();
                var nwBefore = c.NetWorth;
                t.Check(c.TryWithdraw(map, 1000, BankSite.Remote, out string whyW), "withdraw by pod: " + whyW);
                c.InvalidateCache();
                t.Check(c.InTransit.Micro > 0, "the pod withdrawal counts as in transit: " + c.InTransit.ToStringShort());
                t.Check(Math.Abs((c.NetWorth - nwBefore).Silver) < 60, "net worth does not dip while the pod is on its way (only the fee): " + (c.NetWorth - nwBefore).ToStringShort());
                c.TestReset(true);
            }
            // F21: the account never pays more than Mattress Plus, whatever the sliders say
            {
                HSettings.Set("bank.profit", 3f); HSettings.Set("finance.profit", 1.2f);
                double accM = BankSettings.AccountMultiple(1.0);
                t.Check(accM <= BankSettings.AccountCeiling + 1e-9 && accM > 1.0, "bank M 3 against investment M 1.2: account " + accM.ToString("0.0000") + " <= Mattress " + BankSettings.AccountCeiling.ToString("0.0000"));
                HSettings.Set("bank.accountShare", 1f); accM = BankSettings.AccountMultiple(1.1);
                t.Check(accM <= BankSettings.AccountCeiling + 1e-9, "share 1 is capped as well: " + accM.ToString("0.0000"));
                HSettings.Reset(HSettings.Find("bank.accountShare")); HSettings.Reset(HSettings.Find("bank.profit")); HSettings.Reset(HSettings.Find("finance.profit"));
            }

            // withdrawal limit helper
            c.TestReset(true); BT.Fund(Money.FromSilver(1000));
            int max = c.MaxWithdrawCoins(BankSite.Branch);
            var fee = c.WithdrawFee(Money.FromSilver(max), BankSite.Branch);
            t.Check(max > 0 && c.Balance >= Money.FromSilver(max) + fee, "max withdrawal fits balance + fee: " + max + " + " + fee.ToStringShort());
            t.Check(c.Balance < Money.FromSilver(max + 1) + c.WithdrawFee(Money.FromSilver(max + 1), BankSite.Branch), "and one more coin would not");
            HSettings.Set("bank.atmDailyLimit", 300f);
            t.Check(c.MaxWithdrawCoins(BankSite.Atm) <= 300, "at an ATM the daily limit caps it: " + c.MaxWithdrawCoins(BankSite.Atm));
            HSettings.Reset(HSettings.Find("bank.atmDailyLimit"));
            c.TestReset(false); t.Check(c.MaxWithdrawCoins(BankSite.Branch) == 0, "without an account nothing can be withdrawn");
            c.TestReset(true);

            // the lobby draws with every kind of amount without errors
            int err0 = HLog.ErrorCount;
            var w = new Window_Bank(map, BankSite.Remote, null); Find.WindowStack.Add(w); yield return 20;
            var rect = new Rect(0, 0, w.InitialSize.x, w.InitialSize.y);
            BT.Fund(Money.FromSilver(500)); w.TabId = "lobby";
            foreach (bool wd in new[] { false, true })
                foreach (int amt in new[] { 0, 1, 50, 99999, 2_000_000_000 })
                { LobbyPage.WithdrawMode = wd; LobbyPage.Amount = amt; w.DoWindowContents(rect); yield return 2; }
            LobbyPage.WithdrawMode = false; LobbyPage.Amount = 100;
            t.Check(HLog.ErrorCount == err0, "lobby draws deposit and withdraw mode with extreme amounts without errors");
            w.Close(); c.TestReset(true);
            BT.ClearStockpileSilver(t);
        }
    }

    public sealed class BankWindowsSuite : HearthTestSuite
    {
        public override string Name => "bank.windows";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 9000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); BT.Fund(Money.FromSilver(3000)); BT.AddSilver(t, 400);
            int err0 = HLog.ErrorCount;
            var w = new Window_Bank(map, BankSite.Remote, null);
            var sz = w.InitialSize;
            t.Check(sz.x >= 900f && sz.y >= 620f, "the bank window is at least 900 x 620: " + sz);
            t.Check(sz.x <= UI.screenWidth && sz.y <= UI.screenHeight, "and fits on the screen " + UI.screenWidth + "x" + UI.screenHeight);
            Find.WindowStack.Add(w); yield return 20;
            t.Check(w.resizeable, "the bank window can be resized");
            // seven (or more) tabs, drawn at the smallest size, at the default and at a huge one
            foreach (var size in new[] { new Vector2(860f, 560f), sz, new Vector2(1700f, 1000f) })
            {
                var rect = new Rect(0, 0, size.x, size.y);
                foreach (var id in BankTabs.VisibleTabs.Select(x => x.Id).ToList())
                {
                    w.TabId = id;
                    if (id == "invest") foreach (int p in new[] { 0, 1, 2, 3 }) { InvestTab.TestSetPage(p); w.DoWindowContents(rect); }
                    else w.DoWindowContents(rect);
                    yield return 1;
                }
                InvestTab.TestSetPage(0);
            }
            t.Check(HLog.ErrorCount == err0, "every tab draws at 860x560, the default and 1700x1000 without errors (" + (HLog.ErrorCount - err0) + ")");
            // the tab order: useful pages first
            var ids = BankTabs.VisibleTabs.Select(x => x.Id).ToList();
            t.Check(ids.IndexOf("invest") < ids.IndexOf("history") && ids.IndexOf("lobby") == 0 && ids.IndexOf("terms") == ids.Count - 1, "tab order: lobby first, invest before history, terms last: " + string.Join(",", ids));
            // too many tabs: a "More" menu takes the rest and the window still draws
            for (int i = 0; i < 5; i++) BankTabs.Register(new BankTab { Id = "test.long" + i, LabelEn = "A very long tab label number " + i, Order = 500 + i, Draw = r => { } });
            w.TabId = "test.long4";
            w.DoWindowContents(new Rect(0, 0, 860f, 560f)); yield return 2;
            BankTabs.Tabs.RemoveAll(x => x.Id.StartsWith("test.long"));
            w.TabId = "lobby";
            t.Check(HLog.ErrorCount == err0, "twelve tabs at 860 px draw without errors (overflow menu)");
            // open-account page, change-bank dialog, comms window
            c.TestReset(false); w.DoWindowContents(new Rect(0, 0, 860f, 560f)); yield return 2; c.TestReset(true);
            var cb = new Window_ChooseBank(); t.Check(cb.InitialSize.x >= 620f && cb.InitialSize.y >= 480f, "choose-bank dialog size " + cb.InitialSize);
            Find.WindowStack.Add(cb); yield return 5; cb.DoWindowContents(new Rect(0, 0, 640f, 480f)); cb.DoWindowContents(new Rect(0, 0, cb.InitialSize.x, cb.InitialSize.y)); cb.Close();
            var sl = new Window_Silverline(); t.Check(sl.InitialSize.x >= 900f && sl.resizeable, "Silverline fallback window size " + sl.InitialSize);
            Find.WindowStack.Add(sl); yield return 5;
            foreach (var size in new[] { new Vector2(900f, 600f), sl.InitialSize }) { sl.DoWindowContents(new Rect(0, 0, size.x, size.y)); yield return 1; }
            sl.Close();
            t.Check(HLog.ErrorCount == err0, "the other bank windows draw without errors");
            t.Check(!Guard.Paused.Contains(ModuleId.Bank), "bank module not paused by the error barrier");
            w.Close(); c.TestReset(true); BT.ClearStockpileSilver(t);
        }
    }

    public sealed class BankTermsSuite : HearthTestSuite
    {
        public override string Name => "bank.terms";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 4000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); BT.Fund(Money.FromSilver(100));
            // F19: the terms are real settings
            foreach (var key in new[] { "bank.profit", "bank.accountShare", "bank.feeFactor", "bank.switchFee", "bank.directDepositFee", "bank.atmDailyLimit", "lottery.colonyPrice", "lottery.colonyQuote", "lottery.colonyInterval", "lottery.colonyFee", "finance.profit" })
                t.Check(HSettings.Find(key) != null, "setting registered: " + key);
            t.Check(HSettings.Find("bank.accountBeta") != null, "the old key stays registered (old profiles)");
            t.Check(HSettings.Find("bank.accountShare").Category == "Interest & profitability" && HSettings.Find("bank.directDepositFee").Category == "Fees", "settings are grouped: interest and fees");
            foreach (var key in new[] { "bank.accountShare", "bank.loanPortfolioShare", "bank.sounds" }) { var s = HSettings.Find(key); t.Check(s != null && !s.Label.NullOrEmpty() && !s.Desc.NullOrEmpty(), "label and description: " + key); }
            // changing a setting changes what the page shows (it reads the setting, there is no copy)
            HSettings.Set("bank.profit", 1.5f); t.Check(Math.Abs(BankSettings.ProfitM - 1.5) < 1e-6, "profitability read back");
            var rows = YieldCompare.Rows(c.Bank); t.Check(rows.Count >= 3 && rows[0].IsAccount, "comparison has the account first and the products after it: " + string.Join(" | ", rows.Select(r => r.Label + " " + r.YearlyPercent.ToString("0.0"))));
            HSettings.Set("bank.profit", 1.2f); HSettings.Set("finance.profit", 1.2f);
            rows = YieldCompare.Rows(c.Bank);
            var acc = rows[0]; var mattress = rows.FirstOrDefault(r => r.Label.Contains("Mattress")); var index = rows.FirstOrDefault(r => r.Label.Contains("Index") || r.Label.Contains("RimIndex"));
            t.Check(Math.Abs(acc.YearlyPercent - 4.0) < 0.05, "account +4 % at the default: " + acc.YearlyPercent.ToString("0.00"));
            t.Check(mattress.Label != null && mattress.YearlyPercent > acc.YearlyPercent + 2.5, "Mattress Plus clearly above the account: " + mattress.YearlyPercent.ToString("0.0"));
            t.Check(index.Label != null && index.YearlyPercent > mattress.YearlyPercent, "the index pays more than Mattress Plus");
            foreach (var b in DefDatabase<SL_BankDef>.AllDefs)
            {
                double p = FinanceMath.AccountYearlyPercent(1.2, 0.2, b.interestMod);
                t.Check(p < mattress.YearlyPercent, b.defName + " pays " + p.ToString("0.0") + " %, below Mattress Plus");
            }
            // the Terms and lobby pages draw
            int err0 = HLog.ErrorCount;
            var w = new Window_Bank(map, BankSite.Remote, null); Find.WindowStack.Add(w); yield return 10;
            foreach (var id in new[] { "terms", "lobby", "lottery", "invest" }) { w.TabId = id; w.DoWindowContents(new Rect(0, 0, w.InitialSize.x, w.InitialSize.y)); yield return 2; w.DoWindowContents(new Rect(0, 0, 860f, 560f)); yield return 2; }
            t.Check(HLog.ErrorCount == err0, "terms, lobby, lottery and invest pages draw without errors");
            w.Close();
            HSettings.Reset(HSettings.Find("bank.profit")); HSettings.Reset(HSettings.Find("finance.profit"));
            c.TestReset(true);
        }
    }

    public sealed class BankSoundSuite : HearthTestSuite
    {
        public override string Name => "bank.sounds";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            foreach (var n in new[] { BankSound.Click, BankSound.Tick, BankSound.Open, BankSound.CoinsIn, BankSound.CoinsOut, BankSound.Stamp, BankSound.Reject, BankSound.Ticket, BankSound.Ball, BankSound.Ratchet, BankSound.Win, BankSound.Lose, BankSound.Trade })
            {
                var def = DefDatabase<Verse.SoundDef>.GetNamedSilentFail("HH_Bank_" + n);
                t.Check(def != null, "SoundDef HH_Bank_" + n + " exists");
                t.Check(def != null && def.subSounds.Count > 0 && def.subSounds[0].grains.Count > 0, "it has a clip: " + n);
            }
            int p0 = BankSound.Played; BankSound.Play(BankSound.Stamp);
            t.Check(BankSound.Played == p0 + 1 && BankSound.Last == BankSound.Stamp, "a sound is counted when played");
            HSettings.Set("bank.sounds", 0f); int p1 = BankSound.Played; BankSound.Play(BankSound.Win);
            t.Check(BankSound.Played == p1, "the setting switches menu sounds off");
            HSettings.Reset(HSettings.Find("bank.sounds"));
            BankSound.Play("NoSuchSound"); t.Check(true, "an unknown sound name never throws");
            yield break;
        }
    }
}
#endif
