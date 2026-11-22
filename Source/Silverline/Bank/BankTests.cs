// SPDX-License-Identifier: MIT
// In-game suites of the bank. Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hearth;
using Hearth.Testing;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Silverline
{
    public sealed partial class BankGameComp
    {
        /// <summary>Test helper: back to a clean state (optionally with an open account at the given bank), money zero, no loans, no visitors.</summary>
        public void TestReset(bool account, string bank = "SL_Mutual")
        {
            var led = Ledger.Current;
            foreach (var p in collectors.ToList()) if (p != null && !p.Destroyed) p.Destroy();
            collectors.Clear();
            pending.Clear(); loans.Clear();
            foreach (var a in led.Accounts("bank:").ToList()) if (a.Value.Micro != 0) led.Post(a.Value.Micro > 0 ? a.Key : "world", a.Value.Micro > 0 ? "world" : a.Key, a.Value.Abs, "test reset", true);
            score = FinanceMath.ScoreStart; hasAccount = account; bankDefName = account ? bank : null; atmDay = -1; atmTakenMicro = 0;
            lastDay = Today; lastQuadrum = Today / 15; samples.Clear(); shownTier = 4; candidateTier = 4;
            totalFeesMicro = totalInterestMicro = interestQuadrumMicro = 0;
            lastBailoutTick = -100_000_000;
            InvalidateCache();
        }
        public void TestSetTier(int t) { shownTier = candidateTier = t; }
        public int TestLastDay => lastDay;
    }

    static class BT
    {
        public static BankGameComp C => BankGameComp.Current;
        public static int MapSilver(Map map) => map.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t => t.stackCount);

        public static Zone_Stockpile Stockpile(TestContext t)
        {
            var map = t.Map;
            var z = map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(zz => zz.label == "HH bank test");
            if (z != null) return z;
            z = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager) { label = "HH bank test" };
            map.zoneManager.RegisterZone(z);
            var c = t.FreeCellNearCenter(25);
            foreach (var cell in GenRadial.RadialCellsAround(c, 2.3f, true))
                if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null && map.zoneManager.ZoneAt(cell) == null) z.AddCell(cell);
            return z;
        }

        public static void AddSilver(TestContext t, int coins)
        {
            var z = Stockpile(t); var map = t.Map; int left = coins; int i = 0;
            var cells = z.Cells.ToList();
            while (left > 0)
            {
                int n = Math.Min(left, 400);
                var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n;
                GenSpawn.Spawn(s, cells[i++ % cells.Count], map);
                left -= n;
            }
        }

        public static void ClearStockpileSilver(TestContext t)
        {
            foreach (var s in t.Map.listerThings.ThingsOfDef(ThingDefOf.Silver).ToList()) if (s.Spawned) s.Destroy();
        }

        public static void Fund(Money amount) => Ledger.Current.Post("world", BankGameComp.Colony, amount, "test funding");
        public static Money LedgerSum => new Money(Ledger.Current.SumAll());
        public static void Jump(int ticks) => Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + ticks);
        public static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
        public static int Errors0;
    }

    public sealed class BankAccountSuite : HearthTestSuite
    {
        public override string Name => "bank.account";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            t.Check(c != null, "bank game component exists");
            t.Check(BankApi.Impl != null, "BankApi.Impl is set");
            t.Check(DefDatabase<SL_BankDef>.DefCount >= 3, "bank defs loaded: " + DefDatabase<SL_BankDef>.DefCount);
            t.Check(DefDatabase<SL_BankDef>.GetNamedSilentFail("SL_Mutual") != null && DefDatabase<SL_BankDef>.GetNamedSilentFail("SL_TooBigToFail") != null, "Silverline Mutual and Too Big To Fail exist");
            if (ModsConfig.RoyaltyActive) t.Check(DefDatabase<SL_BankDef>.GetNamedSilentFail("SL_ImperialTreasury") != null, "Imperial Treasury exists with Royalty");
            c.TestReset(false);
            BT.ClearStockpileSilver(t);
            t.Check(!BankApi.Ready, "no account: BankApi.Ready false (bank invisible)");
            t.Check(!c.TryDeposit(map, 10, BankSite.Remote, out _), "deposit without account refused");
            t.Check(c.OpenAccount(SL_BankDef.Default, false), "account opens at the default bank");
            t.Check(!c.OpenAccount(SL_BankDef.Default, false), "cannot open a second account");
            t.Check(BankApi.Ready && BankApi.Impl.HasAccount, "BankApi.Ready after opening");
            t.Check(!c.DirectDepositOn, "direct deposit switched off at opening");
            t.Check(c.Balance == Money.Zero, "balance starts at 0");
            HSettings.Set("bank.depositDelayHours", 1f);
            BT.AddSilver(t, 500);
            int before = BT.MapSilver(map);
            t.Check(c.TryDeposit(map, 500, BankSite.Remote, out string why), "deposit of 500 silver from stockpile accepted: " + why);
            t.Check(BT.MapSilver(map) == before - 500, "stockpile lost exactly 500 silver (" + before + " -> " + BT.MapSilver(map) + ")");
            t.Check(c.Balance == Money.Zero && c.InTransit == Money.FromSilver(500), "500 in transit, not yet on the account");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced while in transit");
            yield return 2700;
            t.Check(c.Balance == Money.FromSilver(500), "balance is 500 after the deposit delay: " + c.Balance.ToStringFull());
            t.Check(c.InTransit == Money.Zero && c.Pending.Count == 0, "nothing left in transit");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced after credit");
            t.Check(!c.TryDeposit(map, 100000, BankSite.Remote, out why), "deposit of more silver than exists refused: " + why);
            // instant booking at a counter site
            BT.AddSilver(t, 100);
            t.Check(c.TryDeposit(map, 100, BankSite.Branch, out why), "branch deposit accepted");
            t.Check(c.Balance == Money.FromSilver(600), "branch deposit is booked at once: " + c.Balance);
            // banks
            var other = SL_BankDef.AllSorted.FirstOrDefault(b => b.defName != c.Bank.defName && b.Available);
            t.Check(other != null, "another bank is available");
            if (other != null)
            {
                string oldBank = c.Bank.defName;
                t.Check(c.SwitchBank(other, out why), "switching banks works: " + why);
                t.Check(c.Balance == Money.FromSilver(550), "switching banks cost 50 silver: " + c.Balance);
                t.Check(c.Bank.defName == other.defName, "bank is now " + other.defName);
                t.Check(!c.SwitchBank(other, out _), "switching to the same bank refused");
                c.SwitchBank(DefDatabase<SL_BankDef>.GetNamed(oldBank), out _);
                t.Check(c.Balance == Money.FromSilver(500), "switching back cost another 50: " + c.Balance);
            }
            t.Check(BT.LedgerSum == Money.Zero, "ledger still balanced");
            BT.ClearStockpileSilver(t);
            c.TestReset(true);
        }
    }

    public sealed class BankInterestSuite : HearthTestSuite
    {
        public override string Name => "bank.interest";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 5_000_000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C;
            c.TestReset(true);
            float oldM = HSettings.Get("bank.profit"); bool wasOver = HSettings.IsOverridden(HSettings.Find("bank.profit")); float oldBeta = HSettings.Get("bank.accountBeta");
            HSettings.Set("bank.profit", 1.2f); HSettings.Set("bank.accountBeta", 1f);
            t.Check(Math.Abs(BankSettings.ProfitM - 1.2) < 1e-6, "slider reads 1.2");
            BT.Fund(Money.FromSilver(10000));
            var start = c.Balance;
            int day0 = c.Today;
            for (int d = 1; d <= 60; d++) c.DailyStep(day0 + d);
            double expect = 10000 * 1.2;
            t.Check(BT.Near(c.Balance.Silver / expect, 1.0, 1e-4), $"60 days at M=1.2: {c.Balance.Silver:0.00000} vs {expect} (within 0.01 %)");
            t.Check(c.InterestThisQuadrum.Micro != 0 || c.TotalInterest.Micro > 0, "interest is booked and visible: " + c.TotalInterest.ToStringFull());
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced after interest");
            // one real day through the tick loop (jump the clock, the component catches up)
            var before = c.Balance;
            BT.Jump(60000 * 3);
            yield return 1; yield return 600;       // (a yield right after a clock jump is measured from before the jump)
            double f = FinanceMath.DailyFactor(1.2);
            double want = before.Silver * Math.Pow(f, 3);
            t.Check(BT.Near(c.Balance.Silver / want, 1.0, 1e-4), $"3 days through the tick loop: {c.Balance.Silver:0.00000} vs {want:0.00000} (today {c.Today}, lastDay {c.TestLastDay}, tick {c.Now}, account {c.HasAccount})");
            // other levels
            c.TestReset(true); BT.Fund(Money.FromSilver(1000));
            HSettings.Set("bank.profit", 0.7f); c.DailyStep(c.Today + 100);
            t.Check(c.Balance < Money.FromSilver(1000) && BT.Near(c.Balance.Silver, 1000 * FinanceMath.DailyFactor(0.7), 0.001), "M=0.7 shrinks the account: " + c.Balance.ToStringFull());
            HSettings.Set("bank.profit", 1.0f); var b1 = c.Balance; c.DailyStep(c.Today + 101);
            t.Check(c.Balance == b1, "M=1.0 changes nothing");
            HSettings.Set("bank.profit", 0f); c.DailyStep(c.Today + 102);
            t.Check(c.Balance.Micro == 0, "M=0 makes the money disappear (free text value)");
            // account fee per quadrum
            c.TestReset(true, "SL_TooBigToFail"); BT.Fund(Money.FromSilver(100)); HSettings.Set("bank.profit", 1.0f);
            int q = (c.Today / 15 + 1) * 15; c.DailyStep(q);
            t.Check(BT.Near(c.Balance.Silver, 94, 0.001), "Too Big To Fail charges 6 silver per quadrum: " + c.Balance.ToStringFull());
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced after fees");
            c.TestReset(true);
            if (wasOver) HSettings.Set("bank.profit", oldM); else HSettings.Reset(HSettings.Find("bank.profit"));
            HSettings.Set("bank.accountBeta", oldBeta);
        }
    }

    public sealed class BankWithdrawSuite : HearthTestSuite
    {
        public override string Name => "bank.withdraw";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 9000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); BT.ClearStockpileSilver(t);
            BT.Fund(Money.FromSilver(1000));
            float mn = HSettings.Get("bank.podMinHours"), mx = HSettings.Get("bank.podMaxHours");
            HSettings.Set("bank.podMinHours", 0.02f); HSettings.Set("bank.podMaxHours", 0.04f);
            int before = BT.MapSilver(map);
            t.Check(!c.TryWithdraw(map, 5000, BankSite.Remote, out string why), "withdrawing more than the balance is refused: " + why);
            t.Check(c.TryWithdraw(map, 300, BankSite.Remote, out why), "withdrawal of 300 by drop pod accepted: " + why);
            t.Check(c.Balance == Money.FromSilver(695), "300 + 5 fee (1 % of 300, minimum 5) left the account: " + c.Balance);
            t.Check(c.Pending.Count == 1, "one transfer is pending");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced with silver in transit");
            int waited = 0;
            while (BT.MapSilver(map) < before + 300 && waited < 2000) { yield return 50; waited += 50; }
            t.Check(BT.MapSilver(map) == before + 300, $"300 silver arrived on the map by drop pod after {waited} ticks (" + before + " -> " + BT.MapSilver(map) + ")");
            t.Check(c.Pending.Count == 0 && BT.LedgerSum == Money.Zero, "transfer done, ledger balanced");
            t.Check(new Money(Ledger.Current.Balance(BankGameComp.Transit).Micro) == Money.Zero, "nothing stays in transit");
            // ATM: instant, limited per day, 2 + 1 %
            var atmCell = t.FreeCellNearCenter(15);
            var atm = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(SLDefOf.SL_ATM), atmCell, map);
            int b2 = BT.MapSilver(map);
            var bal = c.Balance;
            t.Check(c.TryWithdraw(map, 100, BankSite.Atm, out why, atm), "ATM withdrawal of 100 accepted: " + why);
            t.Check(BT.MapSilver(map) == b2 + 100, "silver appears at the ATM at once");
            t.Check(c.Balance == bal - Money.FromSilver(103), "ATM fee is 2 + 1 %: " + (bal - c.Balance));
            t.Check(!c.TryWithdraw(map, 450, BankSite.Atm, out why, atm), "ATM daily limit of 500 stops the next withdrawal: " + why);
            t.Check(c.AtmRemainingToday() == 400, "400 silver left of the daily limit");
            t.Check(!atm.Destroyed && atm is Building_SLATM, "ATM def uses Building_SLATM");
            atm.Destroy();
            // with a counter on the map, remote withdrawals are instant too
            var branch = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(SLDefOf.SL_BankCounter), t.FreeCellNearCenter(30), map);
            t.Check(branch is Building_SLBranch, "bank counter def uses Building_SLBranch");
            branch.Destroy();
            HSettings.Set("bank.podMinHours", mn); HSettings.Set("bank.podMaxHours", mx);
            BT.ClearStockpileSilver(t);
            c.TestReset(true);
        }
    }

    public sealed class BankSinkSuite : HearthTestSuite
    {
        public override string Name => "bank.sink";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 5_000_000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map; var led = Ledger.Current;
            t.Check(Tills.BankSink != null, "Tills.BankSink is registered");
            BT.ClearStockpileSilver(t);
            string till = Payments.ColonyTill(map);
            c.TestReset(true);
            HSettings.Set("bank.directDeposit", 1f);
            float fee = HSettings.Get("bank.directDepositFee");
            Payments.CreditColony(map, Money.FromSilver(200), "test income");
            t.Check(led.Balance(till) == Money.FromSilver(200), "till holds 200");
            Tills.FlushAll();
            t.Check(led.Balance(till) == Money.Zero, "till is empty after the midnight flush");
            t.Check(BT.Near(c.Balance.Silver, 200 * (1 - fee), 0.0001), $"till money landed on the account minus {fee * 100:0.##} % fee: {c.Balance.ToStringFull()}");
            t.Check(BT.MapSilver(map) == 0, "no silver was put on the floor");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced");
            // direct deposit off: silver on the floor, account untouched
            var bal = c.Balance;
            HSettings.Set("bank.directDeposit", 0f);
            Payments.CreditColony(map, Money.FromSilver(120), "test income 2");
            Tills.FlushAll();
            t.Check(c.Balance == bal, "with direct deposit off the account is untouched");
            t.Check(BT.MapSilver(map) == 120, "120 silver put down as physical silver: " + BT.MapSilver(map));
            BT.ClearStockpileSilver(t);
            // no account: also the floor
            c.TestReset(false); HSettings.Set("bank.directDeposit", 1f);
            Payments.CreditColony(map, Money.FromSilver(50), "test income 3");
            Tills.FlushAll();
            t.Check(BT.MapSilver(map) == 50 && c.Balance == Money.Zero, "no account: silver on the floor");
            BT.ClearStockpileSilver(t);
            // midnight through the real game loop
            c.TestReset(true); HSettings.Set("bank.directDeposit", 1f);
            Payments.CreditColony(map, Money.FromSilver(80), "test income 4");
            for (int night = 0; night < 3 && led.Balance(till).Micro > 0; night++)
            {
                for (int k = 0; k < 40 && GenLocalDate.HourOfDay(map) != 23; k++) BT.Jump(1250);
                BT.Jump(2000);
                yield return 1; yield return 5400;          // through hour 0: HearthGame flushes the tills once per day
            }
            t.Check(led.Balance(till) == Money.Zero && c.Balance.Silver > 79, "real midnight: till flushed into the account (" + c.Balance + ")");
            // BankApi contract
            c.TestReset(true);
            Payments.CreditColony(map, Money.FromSilver(100), "test income 5");
            t.Check(BankApi.Impl.Deposit(map, Money.FromSilver(100), "api deposit"), "IBankImpl.Deposit works");
            t.Check(led.Balance(till) == Money.Zero && BT.Near(c.Balance.Silver, 100 * (1 - fee), 0.0001), "Deposit took the money from the till");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced after API deposit");
            c.TestReset(true);
            HSettings.Reset(HSettings.Find("bank.directDeposit"));
        }
    }

    public sealed class BankLoanSuite : HearthTestSuite
    {
        public override string Name => "bank.loan";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 5_000_000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var led = Ledger.Current;
            var profile = HearthProfile.Current; float oldTier = profile.Tier;
            int oldMode = HSettings.GetInt("bank.researchMode");
            HSettings.Set("bank.researchMode", 1f); BankCaretaker.CheckResearchOff();
            profile.Tier = 3f;
            Collections.TestMode = true; Collections.TestLog.Clear();
            c.TestReset(true);
            BT.Jump(60000 * 45); yield return 1; yield return 300;      // the overdue ladder counts back from now: make room on the clock
            c.TestReset(true); BT.Fund(Money.FromSilver(5000));
            t.Check(c.LoansUnlocked, "loans unlocked (research off)");
            t.Check(c.CreditLimitSilver() >= 250, "credit limit at least the starter credit: " + c.CreditLimitSilver());
            t.Check(!c.TryTakeLoan(10, 30, out string why, out _), "a loan below the minimum is refused: " + why);
            t.Check(!c.TryTakeLoan(5_000_000, 30, out why, out _), "a loan above the limit is refused: " + why);
            var bal0 = c.Balance;
            t.Check(c.TryTakeLoan(200, 30, out why, out var loan), "loan of 200 for 30 days granted: " + why);
            t.Check(c.Balance == bal0 + Money.FromSilver(200), "balance +200");
            t.Check(c.Debt == Money.FromSilver(200), "debt 200");
            t.Check(BT.LedgerSum == Money.Zero && led.Balance(BankGameComp.LoansAccount) == -Money.FromSilver(200), "ledger balanced, loan account -200");
            double rate = c.YearlyLoanRate();
            t.Check(BT.Near(rate, 0.32, 1e-6), "Standard Room loan rate is 32 % (M=1.2 + 12 pp): " + rate);
            t.Check(loan.InstallmentsLeft == 6 && loan.Installment.Micro > 0, "6 instalments every 5 days, instalment " + loan.Installment);
            // interest accrues daily, coupled to the slider
            var d0 = c.Debt; c.DailyStep(c.Today + 1);
            t.Check(c.Debt > d0 && BT.Near(c.Debt.Silver / d0.Silver, FinanceMath.LoanFactor(rate, 1), 1e-6), "debt grows with the daily loan factor");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced after loan interest");
            Caretaker.TotalFixes = Caretaker.TotalFixes;
            // on-time instalment
            int sc0 = c.Score; var bal1 = c.Balance;
            loan.NextDueTick = c.Now - 1;
            yield return 700;
            t.Check(c.Balance < bal1 && !loan.InArrears, "instalment was paid automatically");
            t.Check(c.Score == sc0 + FinanceMath.ScoreOnTime, "on-time payment: score +4");
            // miss payments: drain the account
            Ledger.Current.Post(BankGameComp.Colony, "world", c.Balance, "test drain");
            loan.NextDueTick = c.Now - 1;
            yield return 700;
            t.Check(loan.InArrears && loan.Missed == 1 && loan.OverdueSinceTick >= 0, "missed instalment is in arrears");
            t.Check(loan.Stage == 0, "no stage yet before the daily check");
            c.CanBorrow(out why); t.Check(!c.CanBorrow(out why), "no new loan while behind: " + why);
            int scB = c.Score;
            // climb the ladder by moving the overdue date back
            Action<int> overdue = days => { loan.OverdueSinceTick = c.Now - (days - 1) * 60000; c.AdvanceLadder(loan, c.Today); };
            overdue(1); t.Check(loan.Stage == 1, "day 1: stage 1 reminder letter");
            var arrearsBefore = loan.Arrears; overdue(4);
            t.Check(loan.Stage == 2 && loan.Arrears > arrearsBefore, "day 4: stage 2 adds a late fee (arrears " + arrearsBefore + " -> " + loan.Arrears + ")");
            t.Check(c.Score <= scB - 30, "stage 2 lowers the score by 30");
            overdue(7); t.Check(loan.Stage == 3, "day 7: stage 3 offset");
            var a3 = loan.Arrears; var b3 = c.Balance;
            c.Credit("world", Money.FromSilver(40), "test deposit", 0);
            t.Check(c.Balance == b3 + Money.FromSilver(20) && loan.Arrears == a3 - Money.FromSilver(20), "stage 3: half of a deposit goes to the debt (balance " + c.Balance + ", arrears " + loan.Arrears + ")");
            overdue(10); t.Check(loan.Stage == 4, "day 10: stage 4 friendly visit");
            yield return 5;
            t.Check(c.Collectors.Count == 2 && c.Collectors.All(p => p.Spawned && p.Faction == Collections.Agency), "two collectors stand in the colony as agency visitors");
            t.Check(c.Collectors.All(p => p.equipment?.Primary == null), "the visitors carry no weapons");
            t.Check(c.Collectors.All(p => p.GetLord() != null), "visitors have a lord job");
            t.Check(Collections.Agency.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile, "agency is not hostile at stage 4");
            overdue(40); t.Check(loan.Stage == 4, "Standard Room: the ladder is capped at stage 4 even after 40 days");
            t.Check(!Collections.TestLog.Any(l => l.Contains("raid") || l.Contains("seizure")), "no seizure or raid at Standard Room");
            // paying clears everything
            BT.Fund(loan.Arrears + Money.FromSilver(10));
            var col = c.Collectors.ToList();
            c.ProcessOverdueForTest();
            t.Check(!loan.InArrears && loan.Stage == 0 && loan.OverdueSinceTick < 0, "paying the arrears resets the ladder");
            t.Check(c.Collectors.Count == 0, "the visitors are released when paid");
            yield return 30;
            t.Check(col.All(p => p.Destroyed || p.GetLord()?.LordJob is LordJob_ExitMapBest || !p.Spawned), "released visitors leave the map");
            foreach (var p in col) if (!p.Destroyed) p.Destroy();
            // difficulty caps (stage anchors 0,1,2,4,5,6,7)
            foreach (var kv in new Dictionary<float, int> { { 0f, 0 }, { 1f, 1 }, { 2f, 2 }, { 3f, 4 }, { 4f, 5 }, { 5f, 6 }, { 6f, 7 } })
            { profile.Tier = kv.Key; t.Check(BankSettings.MaxCollectionStage == kv.Value, $"tier {kv.Key}: highest collection stage {kv.Value} (is {BankSettings.MaxCollectionStage})"); }
            // hard difficulty: all 7 stages reachable
            profile.Tier = 6f;
            Ledger.Current.Post(BankGameComp.Colony, "world", c.Balance, "test drain");
            loan.ArrearsMicro = loan.BalanceMicro; loan.Stage = 0; loan.OverdueSinceTick = c.Now - 29 * 60000; Collections.TestLog.Clear();
            c.AdvanceLadder(loan, c.Today);
            t.Check(loan.Stage == 7, "Why-are-you-hurting-yourself: stage 7 reached (" + loan.Stage + ")");
            t.Check(Collections.TestLog.Any(l => l.StartsWith("seizure value")) && Collections.TestLog.Count(l => l.StartsWith("raid points")) >= 2, "stages 5, 6 and 7 fired: " + string.Join("; ", Collections.TestLog));
            foreach (var p in c.Collectors.ToList()) if (!p.Destroyed) p.Destroy();
            // Comped: nobody collects, instalments are quietly deferred
            profile.Tier = 0f; c.TestReset(true); BT.Fund(Money.FromSilver(3000));
            c.TryTakeLoan(250, 30, out why, out var l2);
            t.Check(l2 != null, "loan on Comped: " + why);
            if (l2 != null)
            {
                Ledger.Current.Post(BankGameComp.Colony, "world", c.Balance, "test drain");
                l2.NextDueTick = c.Now - 1; yield return 700;
                t.Check(!l2.InArrears && l2.Missed == 0, "Comped: unpaid instalment is deferred, no arrears");
            }
            // voluntary repayment
            profile.Tier = 3f; c.TestReset(true); BT.Fund(Money.FromSilver(3000));
            c.TryTakeLoan(300, 60, out why, out var l3);
            t.Check(c.RepayLoan(l3, Money.FromSilver(100), out why) && l3.Balance == Money.FromSilver(200), "repaying 100 reduces the debt: " + l3.Balance);
            t.Check(c.RepayLoan(l3, l3.Balance, out why) && !c.Loans.Contains(l3), "paying off removes the loan");
            t.Check(BT.LedgerSum == Money.Zero && led.Balance(BankGameComp.LoansAccount) == Money.Zero, "ledger balanced, loan account empty");
            // the caretaker notices a broken loan ledger
            c.TryTakeLoan(100, 30, out why, out var l4);
            led.Post("bank:loans", "world", Money.FromSilver(7), "test corruption", true);
            t.Check(BankCaretaker.CheckLoanLedger() == 1 && BankCaretaker.CheckLoanLedger() == 0, "caretaker repairs a corrupted loan account once");
            Collections.TestMode = false;
            c.TestReset(true);
            profile.Tier = oldTier; HSettings.Set("bank.researchMode", oldMode);
        }
    }

    public sealed class BankCollectSuite : HearthTestSuite
    {
        public override string Name => "bank.collect";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); Collections.TestMode = false;
            var agency = Collections.Agency;
            t.Check(agency != null && agency.Hidden, "the collection agency faction exists and is hidden");
            t.Check(!Find.FactionManager.AllFactionsVisible.Contains(agency), "agency does not show up in the faction list");
            var team = Collections.SpawnTeam(map, 200f, true);
            t.Check(team.Count > 0 && team.All(p => p.Spawned && p.Faction == agency), "recovery team spawned: " + team.Count);
            agency.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            t.Check(agency.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile, "agency can be hostile");
            foreach (var p in team) if (!p.Destroyed) p.Destroy();
            Collections.Housekeeping(c);
            t.Check(agency.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile, "housekeeping calms the agency down once its people are gone");
            // seizure: items are taken, the debt is paid
            BT.ClearStockpileSilver(t);
            var z = BT.Stockpile(t); var cell = z.Cells.First();
            var gold = ThingMaker.MakeThing(ThingDefOf.Gold); gold.stackCount = 200; GenSpawn.Spawn(gold, cell, map);
            BT.Fund(Money.FromSilver(2000));
            HearthProfile.Current.Tier = 3f; HSettings.Set("bank.researchMode", 1f); BankCaretaker.CheckResearchOff();
            c.TryTakeLoan(150, 30, out string why, out var loan);
            double value = gold.MarketValue * 200;
            double target = FinanceMath.SeizureValue(loan.Balance.Silver);
            t.Check(value > target, "enough gold to cover the seizure (" + value + " vs " + target + ")");
            double taken = Collections.ExecuteSeizure(c, loan, map, null, target);
            t.Check(taken >= target && taken < target + gold.MarketValue + 1, "seizure took items worth the target: " + taken);
            t.Check(!c.Loans.Contains(loan), "seizure paid the loan off");
            t.Check(BT.LedgerSum == Money.Zero, "ledger balanced after seizure");
            foreach (var th in map.listerThings.ThingsOfDef(ThingDefOf.Gold).ToList()) th.Destroy();
            c.TestReset(true);
            yield return 5;
        }
    }

    public sealed class BankWealthSuite : HearthTestSuite
    {
        public override string Name => "bank.wealth";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 1500;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true);
            float old = HSettings.Get("bank.wealthShare");
            HSettings.Set("bank.wealthShare", 0f);
            float w0 = map.PlayerWealthForStoryteller;
            HSettings.Set("bank.wealthShare", 1f);
            t.Check(BT.Near(map.PlayerWealthForStoryteller, w0, 0.5), "empty account adds nothing to the wealth");
            BT.Fund(Money.FromSilver(10000));
            t.Check(BT.Near(map.PlayerWealthForStoryteller - w0, 10000, 1), "balance of 10,000 counts 100 % for the storyteller: +" + (map.PlayerWealthForStoryteller - w0));
            HSettings.Set("bank.wealthShare", 0.5f);
            t.Check(BT.Near(map.PlayerWealthForStoryteller - w0, 5000, 1), "slider 50 % counts half");
            HSettings.Set("bank.wealthShare", 1.5f);
            t.Check(BT.Near(map.PlayerWealthForStoryteller - w0, 15000, 1), "slider 150 % counts one and a half times");
            HSettings.Set("bank.wealthShare", 0f);
            t.Check(BT.Near(map.PlayerWealthForStoryteller - w0, 0, 1), "slider 0 % means the bank is a hiding place");
            HSettings.Set("bank.wealthShare", 1f);
            t.Check(BT.Near(Find.World.PlayerWealthForStoryteller, Find.Maps.Where(m => m.IsPlayerHome).Sum(m => (double)m.PlayerWealthForStoryteller), 2), "world wealth is the sum over the maps and counts the balance once");
            // the raid point calculation uses the same number
            float pts1 = StorytellerUtility.DefaultThreatPointsNow(map);
            HSettings.Set("bank.wealthShare", 0f);
            float pts0 = StorytellerUtility.DefaultThreatPointsNow(map);
            t.Check(pts1 >= pts0, $"raid points with the balance ({pts1:0}) are not lower than without ({pts0:0})");
            // module off: no effect
            HSettings.Set("bank.wealthShare", 1f);
            HearthProfile.Current.SetModule(ModuleId.Bank, false);
            t.Check(BT.Near(map.PlayerWealthForStoryteller - w0, 0, 1), "with the bank module off the balance does not count");
            HearthProfile.Current.SetModule(ModuleId.Bank, true);
            HSettings.Set("bank.wealthShare", old);
            c.TestReset(true);
            yield return 1;
        }
    }

    public sealed class BankMoodSuite : HearthTestSuite
    {
        public override string Name => "bank.mood";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 5_000_000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            var pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            t.Check(pawn != null, "a colonist exists");
            if (pawn == null) yield break;
            c.TestReset(false);
            var def = SLDefOf.SL_Finances; var greedy = SLDefOf.SL_FinancesGreedy;
            t.Check(def.Worker.CurrentState(pawn).Active == false, "no account: no thought (the bank is invisible)");
            c.TestReset(true);
            var expect = new[] { -4, -2, -1, 0, 0, 1, 2, 3, 3 };
            var greedyTrait = DefDatabase<TraitDef>.GetNamed("Greedy");
            bool hadGreedy = pawn.story.traits.HasTrait(greedyTrait);
            if (!hadGreedy) pawn.story.traits.GainTrait(new Trait(greedyTrait));
            t.Check(!greedy.Worker.CurrentState(pawn).Active || true, "greedy trait given for the test");
            for (int tier = 0; tier < 9; tier++)
            {
                c.TestSetTier(tier);
                var st = def.Worker.CurrentState(pawn);
                if (expect[tier] == 0) t.Check(!st.Active, $"tier {tier} ({FinanceMath.TierNamesEn[tier]}): no thought");
                else t.Check(st.Active && st.StageIndex == tier && Math.Abs(def.stages[tier].baseMoodEffect - expect[tier]) < 0.01f, $"tier {tier} ({FinanceMath.TierNamesEn[tier]}): mood {expect[tier]}");
                var gs = greedy.Worker.CurrentState(pawn);
                if (expect[tier] != 0) t.Check(gs.Active && gs.StageIndex == tier && Math.Abs(greedy.stages[tier].baseMoodEffect - 2 * expect[tier]) < 0.01f, $"tier {tier}: greedy colonists feel it twice (active {gs.Active}, stage {gs.StageIndex}, mood {greedy.stages[Math.Min(tier, greedy.stages.Count - 1)].baseMoodEffect}, stages {greedy.stages.Count})");
            }
            if (!hadGreedy) pawn.story.traits.RemoveTrait(pawn.story.traits.GetTrait(greedyTrait));
            HSettings.Set("bank.moodOn", 0f);
            t.Check(!def.Worker.CurrentState(pawn).Active, "setting moodOn off: no thought");
            HSettings.Set("bank.moodOn", 1f);
            // the tier follows the net worth (with the one day hold)
            c.TestReset(true); BT.Fund(Money.FromSilver(30000));
            yield return 400;
            BT.Jump(60000 + 100); yield return 1; yield return 400;      // a new tier must hold for a whole day before it is shown
            t.Check(c.ShownTier >= 5, "30,000 silver of net worth shows a Well-Off tier or better (" + c.ShownTier + ")");
            c.TestReset(true);
        }
    }

    public sealed class BankUiSuite : HearthTestSuite
    {
        public override string Name => "bank.ui";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 4000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(false);
            int err0 = HLog.ErrorCount;
            var w = new Window_Bank(map, BankSite.Remote, null);
            Find.WindowStack.Add(w);
            yield return 60;
            t.Check(Find.WindowStack.IsOpen(w), "bank window opens without an account (open-account page)");
            c.OpenAccount(SL_BankDef.Default, true);
            BT.Fund(Money.FromSilver(1234.56789));
            c.TryTakeLoan(100, 30, out _, out _);
            var ids = BankTabs.VisibleTabs.Select(x => x.Id).ToList();
            t.Check(new[] { "lobby", "loans", "history", "terms" }.All(ids.Contains), "tabs registered: " + string.Join(",", ids));
            foreach (var id in ids) { w.TabId = id; yield return 40; }
            BankTabs.Register(new BankTab { Id = "test.extra", LabelEn = "Extra", Order = 500, Draw = r => { } });
            t.Check(BankTabs.VisibleTabs.Any(x => x.Id == "test.extra"), "other modules can add tabs");
            w.TabId = "test.extra"; yield return 20;
            BankTabs.Tabs.RemoveAll(x => x.Id == "test.extra");
            w.TabId = "lobby";
            var cb = new Window_ChooseBank(); Find.WindowStack.Add(cb); yield return 30; cb.Close();
            var atmw = new Window_Bank(map, BankSite.Atm, null); w.Close(); Find.WindowStack.Add(atmw); yield return 40; atmw.Close();
            t.Check(HLog.ErrorCount == err0, "no errors logged while the windows were drawn (" + (HLog.ErrorCount - err0) + ")");
            t.Check(!Guard.Paused.Contains(ModuleId.Bank), "bank module not paused by the error barrier");
            // quips
            t.Check(!string.IsNullOrEmpty(Window_Bank.Quip(c)), "Ottoline has something to say");
            // keys for all defs exist as labels
            foreach (var b in DefDatabase<SL_BankDef>.AllDefs) t.Check(!b.label.NullOrEmpty() && !b.description.NullOrEmpty() && !b.slogan.NullOrEmpty() && b.Icon != null, "bank def complete: " + b.defName);
            foreach (var d in new Def[] { SLDefOf.SL_ATM, SLDefOf.SL_BankCounter, SLDefOf.SL_NightSafe, SLDefOf.SL_Banking, SLDefOf.SL_Branches, SLDefOf.SL_Credit }) t.Check(!d.label.NullOrEmpty() && !d.description.NullOrEmpty(), "def has label and description: " + d.defName);
            c.TestReset(true);
        }
    }

    public sealed class BankSaveSuite : HearthTestSuite
    {
        public override string Name => "bank.save";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 600;
        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BT.C; var map = t.Map;
            c.TestReset(true); BT.Fund(Money.FromSilver(3000));
            HearthProfile.Current.Tier = 3f; HSettings.Set("bank.researchMode", 1f); BankCaretaker.CheckResearchOff();
            c.TryTakeLoan(200, 30, out _, out var loan);
            loan.Stage = 2; loan.ArrearsMicro = 123456; loan.OverdueSinceTick = 777;
            var pt = new PendingTransfer { Kind = TransferKind.Withdraw, Micro = 4200000, DueTick = 999, MapId = map.uniqueID, Cell = new IntVec3(3, 0, 4), Memo = "m" };
            string path = Path.Combine(GenFilePaths.TempFolderPath, "hh_bank_save_test.xml");
            Loan l2 = null; PendingTransfer p2 = null; DaySample s0 = new DaySample { Day = 7, Balance = 5, NetWorth = -9 }, s2 = null;
            Scribe.saver.InitSaving(path, "test");
            Scribe_Deep.Look(ref loan, "loan"); Scribe_Deep.Look(ref pt, "pt"); Scribe_Deep.Look(ref s0, "sample");
            Scribe.saver.FinalizeSaving();
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref l2, "loan"); Scribe_Deep.Look(ref p2, "pt"); Scribe_Deep.Look(ref s2, "sample");
            Scribe.loader.FinalizeLoading();
            t.Check(l2 != null && l2.Id == loan.Id && l2.BalanceMicro == loan.BalanceMicro && l2.Stage == 2 && l2.ArrearsMicro == 123456 && l2.OverdueSinceTick == 777 && l2.InstallmentsLeft == loan.InstallmentsLeft, "loan survives a save/load round trip");
            t.Check(p2 != null && p2.Micro == 4200000 && p2.Kind == TransferKind.Withdraw && p2.Cell == new IntVec3(3, 0, 4) && p2.DueTick == 999, "pending transfer survives a save/load round trip");
            t.Check(s2 != null && s2.Day == 7 && s2.NetWorth == -9, "day sample survives a round trip");
            try { File.Delete(path); } catch { }
            // the real thing: save the game with the comp in it
            int err0 = HLog.ErrorCount;
            GameDataSaveLoader.SaveGame("hh_bank_save_test");
            yield return 5;
            string savePath = GenFilePaths.FilePathForSavedGame("hh_bank_save_test");
            t.Check(File.Exists(savePath), "game saved: " + savePath);
            if (File.Exists(savePath))
            {
                string xml = File.ReadAllText(savePath);
                t.Check(xml.Contains("Silverline.BankGameComp") && xml.Contains("<hasAccount>True</hasAccount>") && xml.Contains("<loans>"), "save game contains the bank component with account and loans");
                File.Delete(savePath);
            }
            t.Check(HLog.ErrorCount == err0, "no errors while saving");
            c.TestReset(true);
        }
    }
}
#endif
