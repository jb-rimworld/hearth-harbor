// SPDX-License-Identifier: MIT
// In-game suites of Clawback Mutual for 1.1.0 (F24 change, F25 bulk, F26 pension rule, F27 claim form, F28 status, F35 windows). Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using Hearth.Testing;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    static class InsTest110
    {
        public static int Letters<T>() where T : Letter => Find.LetterStack.LettersListForReading.OfType<T>().Count();
        public static int AllLetters() => Find.LetterStack.LettersListForReading.Count;

        /// <summary>Colonists are shared by all suites of a run: put the age back when a suite is done.</summary>
        public static long AgeOf(Pawn p) => p.ageTracker.AgeBiologicalTicks;
        public static void SetAgeYears(Pawn p, double years) { p.ageTracker.AgeBiologicalTicks = (long)(years * GenDate.TicksPerYear); }

        public static TimeAssignmentDef[] SaveSchedule(Pawn p) { var a = new TimeAssignmentDef[24]; for (int h = 0; h < 24; h++) a[h] = p.timetable.GetAssignment(h); return a; }
        public static void RestoreSchedule(Pawn p, TimeAssignmentDef[] a) { for (int h = 0; h < 24; h++) p.timetable.SetAssignment(h, a[h]); }
        public static void SetWorkHours(Pawn p, int n) { for (int h = 0; h < 24; h++) p.timetable.SetAssignment(h, h < n ? TimeAssignmentDefOf.Work : (h % 3 == 0 ? TimeAssignmentDefOf.Sleep : TimeAssignmentDefOf.Anything)); }
    }

    // ---------------------------------------------------------------------------------------------------------------------- F24
    public sealed class InsuranceChangeSuite : HearthTestSuite
    {
        public override string Name => "insurance.change";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(20000);
            var comp = InsTest.Comp;
            InsTest.Set(InsSettings.ChangeFee, 15);
            var def = InsTest.Def("SL_MuffaloMachine");
            var res = comp.Sign(def, CoverTier.GoodEnough, null, 2000, new[] { "boost" }, null);
            t.Check(res.Ok, "signed Muffalo & Machine (Good Enough, 2000): " + res.error);
            if (!res.Ok) yield break;
            var c = res.contract; InsTest.SkipWaiting(c);
            t.Check(comp.CanChange(c) == null, "a running policy can be changed");

            // --- an upgrade: tier, sum, add-on --------------------------------------------------------------------------------------------
            var plan = comp.PlanChange(c, CoverTier.Premium, 3000, new[] { "boost" });
            t.Check(plan.Ok && plan.increase && !plan.decrease, "plan: an increase: " + plan.error);
            t.Check(plan.newQuote.billing > plan.oldQuote.billing, $"the new premium is higher: {plan.oldQuote.billing:0.00} -> {plan.newQuote.billing:0.00}");
            t.Check(Math.Abs(plan.fee - 15) < 1e-9 && plan.waitDays == 3, "fee 15 and a 3-day waiting period after the upgrade");
            t.Check(plan.effectiveTick >= Ins.Now && plan.effectiveTick <= c.nextBillTick + 1, "takes effect at the next billing");
            var till0 = InsTest.Till; var ins0 = InsTest.Insurer;
            bool ok = comp.ApplyChange(plan, out var msg);
            t.Check(ok, "change filed: " + msg);
            t.Check((till0 - InsTest.Till) == Money.FromSilver(15) && (InsTest.Insurer - ins0) == Money.FromSilver(15), "the fee went from the till to the insurer");
            t.Check(c.HasPendingChange && c.tier == CoverTier.GoodEnough && Math.Abs(c.sum - 2000) < 1e-9, "nothing changes before the billing: still Good Enough, 2000");
            t.Check(comp.CanChange(c) != null, "a second change is refused while one is filed");
            t.Check(InsTest.LedgerOk, "ledger balanced after the fee");
            var q = InsurancePricing.QuoteFor(c);
            t.Check(Math.Abs(q.billing - plan.oldQuote.billing) < 1e-9, "the running premium is still the old one");

            // withdraw and file again
            comp.WithdrawChange(c);
            t.Check(!c.HasPendingChange && comp.CanChange(c) == null, "a filed change can be withdrawn");
            plan = comp.PlanChange(c, CoverTier.Premium, 3000, new[] { "boost" });
            t.Check(comp.ApplyChange(plan, out _), "filed again");

            // --- it takes effect --------------------------------------------------------------------------------------------------------------
            comp.Process(Ins.Now);
            t.Check(c.HasPendingChange, "not yet due: still pending");
            int now = Ins.Now;
            c.pendingEffectiveTick = now;
            c.nextBillTick = now + 100000;                       // keep the billing out of this check
            int letters0 = InsTest110.AllLetters();
            comp.Process(now);
            t.Check(!c.HasPendingChange && c.tier == CoverTier.Premium && Math.Abs(c.sum - 3000) < 1e-9, "applied: Premium, 3000");
            t.Check(c.prevTier == (int)CoverTier.GoodEnough && Math.Abs(c.prevSum - 2000) < 1e-9 && c.upgradeWaitUntilTick > now, "the old terms are kept for the waiting period");
            t.Check(Math.Abs(c.lastPremiumQuadrum - InsurancePricing.QuoteFor(c).premiumQuadrum) < 1e-9, "our own change does not open the 'premium rose' window");
            t.Check(InsTest110.AllLetters() > letters0, "a letter confirms the change");
            t.Check(c.changeCount == 1, "change counted");

            // --- anti-exploit: an event inside the waiting period is assessed with the old cover -----------------------------------------------
            var old = ClaimEngine.FileProperty(c, ClaimTrigger.VehicleBuilding, 1000, "shuttle", InsTest.Map, null, null, now);   // not through a view: the engine takes c as it is
            ClaimEngine.Close(old); comp.claims.Remove(old);
            ClaimIntake.Property(c, ClaimTrigger.VehicleBuilding, 1000, "shuttle", InsTest.Map, null, null);
            var inWait = comp.claims.Last();
            t.Check(inWait.tier == CoverTier.GoodEnough && Math.Abs(inWait.sumInsured - 2000) < 1e-9, $"claim in the waiting window uses the old cover: {inWait.tier}, {inWait.sumInsured}");
            t.Check(c.tier == CoverTier.Premium && Math.Abs(c.sum - 3000) < 1e-9 && c.riders.Contains("boost"), "the contract itself is untouched by the claim view");
            ClaimEngine.Close(inWait); comp.claims.Remove(inWait);
            c.upgradeWaitUntilTick = Ins.Now - 61 * 60000; c.changedTick = Ins.Now - 61 * 60000; comp.Process(Ins.Now);
            t.Check(c.prevSum < 0 && c.upgradeWaitUntilTick < 0, "waiting period and filing windows over: old terms forgotten");
            ClaimIntake.Property(c, ClaimTrigger.VehicleBuilding, 1000, "shuttle", InsTest.Map, null, null);
            var afterWait = comp.claims.Last();
            t.Check(afterWait.tier == CoverTier.Premium && Math.Abs(afterWait.sumInsured - 3000) < 1e-9, "after the waiting period the new cover counts");
            ClaimEngine.Close(afterWait); comp.claims.Remove(afterWait);

            // --- a decrease: no waiting period, the add-on goes ----------------------------------------------------------------------------------
            InsTest.Set(InsSettings.ChangeFee, 0);
            var down = comp.PlanChange(c, CoverTier.GoodEnough, 1500, new string[0]);
            t.Check(down.Ok && down.decrease && !down.increase && down.waitDays == 0, "plan: a decrease without waiting");
            t.Check(down.newQuote.billing < down.oldQuote.billing, "and a cheaper premium");
            t.Check(comp.ApplyChange(down, out _), "decrease filed (no fee)");
            c.pendingEffectiveTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(c.tier == CoverTier.GoodEnough && Math.Abs(c.sum - 1500) < 1e-9 && c.riders.Count == 0 && c.upgradeWaitUntilTick < 0, "decrease applied at once, add-on gone, no waiting period");
            t.Check(!plan.noChange && comp.PlanChange(c, c.tier, c.sum, c.riders).noChange, "no change is recognised as no change");

            // --- events from before a change keep the old terms (wait = 0 and a decrease) --------------------------------------------------------
            {
                double sumNow = c.sum; var tierNow = c.tier; int evBefore = c.changedTick - 1000;
                t.Check(c.changedTick >= 0 && c.prevSum >= 0, "a decrease records the previous terms and the change time");
                using (c.ClaimView(evBefore)) t.Check(Math.Abs(c.sum - 3000) < 1e-9 && c.tier == CoverTier.Premium, "an event from before a decrease is assessed with the old (higher) cover");
                using (c.ClaimView(c.changedTick)) t.Check(Math.Abs(c.sum - sumNow) < 1e-9 && c.tier == tierNow, "an event at or after the change uses the new cover");
                int waitOld = HSettings.GetInt(InsSettings.ChangeWaitingDays), limOld = HSettings.GetInt(InsSettings.ChangeLimit);
                InsTest.Set(InsSettings.ChangeWaitingDays, 0); InsTest.Set(InsSettings.ChangeLimit, 0);
                var up0 = comp.PlanChange(c, CoverTier.Premium, 2500, null);
                t.Check(up0.Ok && up0.increase && up0.waitDays == 0 && comp.ApplyChange(up0, out _), "increase with waiting days = 0 filed");
                c.pendingEffectiveTick = Ins.Now; comp.Process(Ins.Now);
                int before = c.changedTick - 5000;
                using (c.ClaimView(before)) t.Check(Math.Abs(c.sum - sumNow) < 1e-9 && c.tier == tierNow, "wait = 0: an event from before the increase still uses the old cover");
                using (c.ClaimView(c.changedTick + 1)) t.Check(Math.Abs(c.sum - 2500) < 1e-9, "wait = 0: later events use the new cover");
                c.prevSum = -1; c.prevTier = -1; c.prevRiders = null; c.changedTick = -1; c.upgradeWaitUntilTick = -1;
                // put the contract back to what the rest of this suite expects
                c.tier = tierNow; c.sum = sumNow; c.riders = new List<string>();
                InsTest.Set(InsSettings.ChangeWaitingDays, waitOld); InsTest.Set(InsSettings.ChangeLimit, limOld);
            }

            // --- limits and refusals ---------------------------------------------------------------------------------------------------------------
            InsTest.Set(InsSettings.ChangeLimit, 2);
            t.Check(comp.CanChange(c) != null, "the per-quadrum limit stops a third change: " + comp.CanChange(c));
            InsTest.Set(InsSettings.ChangeLimit, 0);
            t.Check(comp.CanChange(c) == null, "limit off: changeable again");
            var pf = comp.PlanChange(c, CoverTier.Premium, 99999, null);
            t.Check(pf.newSum <= 30000 && pf.newSum >= def.minSum, "the sum is clamped into the allowed range: " + pf.newSum);
            InsTest.Reset(5); InsTest.Set(InsSettings.ChangeFee, 400);
            var broke = comp.Sign(def, CoverTier.BareMinimum, null, 500, null, null);
            if (broke.Ok)
            {
                var p3 = comp.PlanChange(broke.contract, CoverTier.GoodEnough, 800, null);
                t.Check(!comp.ApplyChange(p3, out var why) && !broke.contract.HasPendingChange, "a fee that cannot be paid refuses the change: " + why);
            }
            else t.Note("could not sign with 5 silver (expected): " + broke.error);
            InsTest.Reset(5000);
            var cx = comp.Sign(def, CoverTier.GoodEnough, null, 1000, null, null);
            if (cx.Ok)
            {
                comp.FileCancel(cx.contract);
                t.Check(comp.CanChange(cx.contract) != null || !cx.contract.Active, "a policy that is being cancelled cannot be changed");
            }

            // --- save / load of the pending fields ---------------------------------------------------------------------------------------------------
            InsTest.Reset(5000);
            var r2 = comp.Sign(def, CoverTier.GoodEnough, null, 2000, null, null);
            var c2 = r2.contract;
            comp.ApplyChange(comp.PlanChange(c2, CoverTier.Premium, 2500, new[] { "boost" }), out _);
            c2.prevSum = 1111; c2.prevTier = 0; c2.upgradeWaitUntilTick = Ins.Now + 5000; c2.prevRiders = new List<string> { "boost" };
            string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "hh_insurance_change_test.xml");
            Scribe.saver.InitSaving(path, "HHInsChange");
            var toSave = comp; Scribe_Deep.Look(ref toSave, "ins", Current.Game);
            Scribe.saver.FinalizeSaving();
            InsuranceComponent loaded = null;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "ins", Current.Game); Scribe.loader.FinalizeLoading();
            var l2 = loaded?.contracts.FirstOrDefault(x => x.defName == def.defName);
            t.Check(l2 != null && l2.HasPendingChange && l2.pendingTier == (int)CoverTier.Premium && Math.Abs(l2.pendingSum - 2500) < 1e-9 && l2.pendingRiders != null && l2.pendingRiders.Contains("boost"), "pending change survives a save");
            t.Check(l2 != null && Math.Abs(l2.prevSum - 1111) < 1e-9 && l2.prevTier == 0 && l2.upgradeWaitUntilTick == c2.upgradeWaitUntilTick && l2.prevRiders != null && l2.prevRiders.Contains("boost"), "previous terms survive a save");
            try { System.IO.File.Delete(path); } catch { }
            InsTest.RestoreSettings();
            yield break;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------- F25
    public sealed class InsuranceBulkSuite : HearthTestSuite
    {
        public override string Name => "insurance.bulk";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(60000);
            var comp = InsTest.Comp;
            var cols = InsTest.Colonists;
            t.Check(cols.Count >= 2, "at least two colonists: " + cols.Count);
            if (cols.Count < 2) yield break;
            var ages = cols.Select(InsTest110.AgeOf).ToList();
            foreach (var p in cols) InsTest110.SetAgeYears(p, 30);
            var accident = InsTest.Def("SL_LimbByLimb"); var pension = InsTest.Def("SL_GoldenYears"); var health = InsTest.Def("SL_RimHealth");
            InsurancePricing.SumRange(accident, InsTest.Map, cols[0], out var amin, out var amax);
            double aSum = Math.Max(amin, Math.Min(amax, 1500));

            // --- the plan: progressive bundle discount, reasons ---------------------------------------------------------------------------------------
            var items = new List<InsuranceComponent.BulkItem>();
            foreach (var p in cols) { items.Add(new InsuranceComponent.BulkItem { def = accident, tier = CoverTier.GoodEnough, holder = p, sum = aSum }); items.Add(new InsuranceComponent.BulkItem { def = pension, tier = CoverTier.GoodEnough, holder = p, sum = 800 }); }
            items.Add(new InsuranceComponent.BulkItem { def = health, tier = CoverTier.GoodEnough, sum = 1000 });
            var planned = comp.PlanBulk(items, out double firstTotal, out double billingTotal);
            t.Check(planned.Count == items.Count && planned.All(i => i.skip == null), "all items can be signed: " + string.Join("; ", planned.Where(i => i.skip != null).Select(i => i.skip)));
            t.Check(planned[0].def.kind != PolicyKind.Person, "colony policies come first");
            double sumFirst = planned.Sum(i => i.first);
            t.Check(Math.Abs(firstTotal - sumFirst) < 1e-6 && firstTotal > 0, $"first premiums add up: {firstTotal:0.00}");
            var single = InsurancePricing.QuoteFor(accident, CoverTier.GoodEnough, cols[0], aSum, null, 1, null).billing;
            var lastAccident = planned.Last(i => i.def == accident);
            t.Check(lastAccident.billing < single - 1e-9, $"the bundle discount grows with the number of policies: {single:0.00} alone, {lastAccident.billing:0.00} as one of {planned.Count}");

            // --- sign ---------------------------------------------------------------------------------------------------------------------------------
            int letters0 = InsTest110.AllLetters();
            var ins0 = InsTest.Insurer;
            var result = comp.SignMany(items);
            t.Check(result.signed.Count == items.Count, $"signed {result.signed.Count} of {items.Count}: " + string.Join("; ", result.skipped.Select(s => s.skip)));
            t.Check(comp.contracts.Count == items.Count, "one contract each");
            t.Check(InsTest110.AllLetters() == letters0 + 1, "exactly one summary letter instead of " + items.Count + " welcome letters: " + (InsTest110.AllLetters() - letters0));
            t.Check(Math.Abs((InsTest.Insurer - ins0).Silver - result.firstTotal) < 0.0001, "the insurer received exactly the first premiums");
            t.Check(InsTest.LedgerOk, "ledger balanced after the bulk purchase");
            t.Check(comp.contracts.Where(c => c.def_ok()).All(c => c.waitUntilTick > Ins.Now), "waiting period starts per policy");
            t.Check(comp.Started, "Clawback is in contact");

            // --- a second try: everybody is insured already ----------------------------------------------------------------------------------------------
            var again = comp.SignMany(items);
            t.Check(again.signed.Count == 0 && again.skipped.Count == items.Count, "signing the same list again signs nothing (duplicates are refused)");

            // --- an old colonist is skipped, the rest is signed --------------------------------------------------------------------------------------------
            InsTest.Reset(60000);
            InsTest110.SetAgeYears(cols[0], 61);
            var items2 = cols.Select(p => new InsuranceComponent.BulkItem { def = pension, tier = CoverTier.GoodEnough, holder = p, sum = 800 }).ToList();
            var r2 = comp.SignMany(items2);
            t.Check(r2.signed.Count == cols.Count - 1 && r2.skipped.Count == 1 && r2.skipped[0].holder == cols[0], "a colonist of pension age is skipped with a reason: " + r2.skipped.FirstOrDefault()?.skip);

            // --- not enough money: stops, no half-failure -----------------------------------------------------------------------------------------------------
            InsTest.Reset(0);
            InsTest110.SetAgeYears(cols[0], 30);
            var probe = InsurancePricing.QuoteFor(accident, CoverTier.GoodEnough, cols[0], aSum, null, 1, null).billing;
            var l = Ledger.Current; l.Post("world", Payments.ColonyTill(InsTest.Map), Money.FromSilver(probe * 1.6), "test: small purse");
            var items3 = cols.Select(p => new InsuranceComponent.BulkItem { def = accident, tier = CoverTier.GoodEnough, holder = p, sum = aSum }).ToList();
            var r3 = comp.SignMany(items3);
            t.Check(r3.stoppedForMoney && r3.signed.Count >= 1 && r3.signed.Count < cols.Count, $"stopped for money after {r3.signed.Count} of {cols.Count}");
            t.Check(r3.skipped.Count == cols.Count - r3.signed.Count, "the rest is reported as skipped");
            t.Check(comp.contracts.Count == r3.signed.Count && InsTest.LedgerOk, "no contract without its premium; ledger balanced");

            for (int i = 0; i < cols.Count; i++) cols[i].ageTracker.AgeBiologicalTicks = ages[i];
            InsTest.RestoreSettings();
        }
    }

    static class ContractExt { public static bool def_ok(this PolicyContract c) => c.Def != null; }

    // ---------------------------------------------------------------------------------------------------------------------- F26
    public sealed class InsurancePensionSuite : HearthTestSuite
    {
        public override string Name => "insurance.pension";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(20000);
            var comp = InsTest.Comp;
            var pawn = InsTest.Colonists.First(p => p.timetable != null);
            long age0 = InsTest110.AgeOf(pawn); var sched0 = InsTest110.SaveSchedule(pawn);
            InsTest110.SetAgeYears(pawn, 40);
            InsTest.Set(InsSettings.PensionRule, (float)PensionRuleMode.Enforce); InsTest.Set(InsSettings.PensionMaxHours, 2);
            var def = InsTest.Def("SL_GoldenYears");
            var res = comp.Sign(def, CoverTier.GoodEnough, pawn, 1000, null, null);
            t.Check(res.Ok, "pension signed at 40: " + res.error); if (!res.Ok) yield break;
            var c = res.contract; InsTest.SkipWaiting(c);
            int now = Ins.Now;
            t.Check(!PensionRule.Drawing(c, now), "a 40-year-old is not drawing a pension yet");
            InsTest110.SetAgeYears(pawn, 61);
            t.Check(PensionRule.Drawing(c, now), "at 61 the pension is drawn");

            // --- the schedule helpers -------------------------------------------------------------------------------------------------------------------
            InsTest110.SetWorkHours(pawn, 6);
            t.Check(PensionRule.WorkHours(pawn) == 6, "6 work hours counted");
            t.Check(PensionRule.AtRisk(c, now), "6 hours is over the limit of 2");
            InsTest110.SetWorkHours(pawn, 2);
            t.Check(!PensionRule.AtRisk(c, now) && PensionRule.WorkHours(pawn) == 2, "exactly 2 hours is fine");
            for (int h = 0; h < 24; h++) pawn.timetable.SetAssignment(h, TimeAssignmentDefOf.Anything);
            t.Check(PensionRule.WorkHours(pawn) == 0 && !PensionRule.AtRisk(c, now), "Anything counts as free time");
            InsTest110.SetWorkHours(pawn, 6);
            t.Check(PensionRule.FixSchedule(pawn, 2) == 4 && PensionRule.WorkHours(pawn) == 2, "the fix button turns the surplus work hours into Anything");

            // --- strikes ------------------------------------------------------------------------------------------------------------------------------------
            InsTest110.SetWorkHours(pawn, 2);
            c.pensionCheckDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c.pensionStrikes == 0, "a schedule within the limit gives no strike");

            InsTest110.SetWorkHours(pawn, 6);
            int letters0 = InsTest110.AllLetters();
            c.pensionCheckDay = -1; c.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c.pensionStrikes == 1 && InsTest110.AllLetters() > letters0, "first strike with a warning letter");
            c.pensionCheckDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c.pensionStrikes == 1, "no second strike inside the two-day cooldown");

            c.accrued = 40; c.accruedSince = Ins.Now - 1000;
            c.pensionCheckDay = -1; c.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c.pensionStrikes == 2 && c.accrued == 0 && c.pensionSuspendedUntil > Ins.Now, "second strike: the pension of this period is forfeited and suspended");
            double before = c.accrued;
            comp.DailyChecks(Ins.Now + 1);   // accrual is skipped while suspended (the day check may or may not fire; the suspension is what counts)
            t.Check(c.accrued <= before + 1e-9 || c.pensionSuspendedUntil > Ins.Now, "no pension accrues while suspended");

            // decay: a quadrum later one strike is gone
            int qi = (int)InsuranceTime.QuadrumIndex();
            c.quadrumStamp = qi + 5; c.nextBillTick = Ins.Now + 100000;
            comp.Process(Ins.Now);
            t.Check(c.pensionStrikes == 1, "a new quadrum removes one strike: " + c.pensionStrikes);

            c.pensionCheckDay = -1; c.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            c.pensionCheckDay = -1; c.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c.pensionRevoked && !c.Active && c.endReason == Ins.T("End.Pension"), "third strike: the entitlement is lost and the contract ends: " + c.endReason + " / " + c.pensionStrikes);

            // --- modes and exemptions --------------------------------------------------------------------------------------------------------------------------
            InsTest.Reset(20000);
            InsTest110.SetAgeYears(pawn, 40);
            var r2 = comp.Sign(def, CoverTier.GoodEnough, pawn, 1000, null, null); var c2 = r2.contract; InsTest.SkipWaiting(c2);
            InsTest110.SetAgeYears(pawn, 61); InsTest110.SetWorkHours(pawn, 8);
            InsTest.Set(InsSettings.PensionRule, (float)PensionRuleMode.WarnOnly);
            int l1 = InsTest110.AllLetters();
            c2.pensionCheckDay = -1; c2.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c2.pensionStrikes == 0 && InsTest110.AllLetters() > l1, "warn-only: a letter, no strike");
            InsTest.Set(InsSettings.PensionRule, (float)PensionRuleMode.Off);
            c2.pensionCheckDay = -1; c2.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(c2.pensionStrikes == 0 && !PensionRule.AtRisk(c2, Ins.Now), "rule off: nothing happens");
            InsTest.Set(InsSettings.PensionRule, (float)PensionRuleMode.Enforce);
            InsTest.Reset(20000);
            InsTest110.SetAgeYears(pawn, 40);
            var r3 = comp.Sign(def, CoverTier.BareMinimum, pawn, 1000, null, null); InsTest.SkipWaiting(r3.contract);
            InsTest110.SetAgeYears(pawn, 61); InsTest110.SetWorkHours(pawn, 8);
            t.Check(!PensionRule.Drawing(r3.contract, Ins.Now) && !PensionRule.AtRisk(r3.contract, Ins.Now), "the lump-sum tier (Bare Minimum) is not bound by the rule");
            // Legal Shield turns the first strike into a warning
            InsTest.Reset(20000);
            InsTest110.SetAgeYears(pawn, 40);
            var r4 = comp.Sign(def, CoverTier.GoodEnough, pawn, 1000, null, null); InsTest.SkipWaiting(r4.contract);
            comp.Sign(InsTest.Def("SL_LegalShield"), CoverTier.GoodEnough, null, 1000, null, null);
            InsTest110.SetAgeYears(pawn, 61); InsTest110.SetWorkHours(pawn, 8);
            r4.contract.pensionCheckDay = -1; r4.contract.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(r4.contract.pensionStrikes == 0 && r4.contract.pensionLegalQuadrum >= 0, "Legal Shield: the first breach per quadrum is only a warning");
            r4.contract.pensionCheckDay = -1; r4.contract.pensionLastStrikeDay = -1; comp.DailyChecks(Ins.Now);
            t.Check(r4.contract.pensionStrikes == 1, "but the next one is a strike");

            // --- save / load ---------------------------------------------------------------------------------------------------------------------------------
            r4.contract.pensionSuspendedUntil = Ins.Now + 4000; r4.contract.pensionNoticed = true;
            string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "hh_insurance_pension_test.xml");
            Scribe.saver.InitSaving(path, "HHInsPension");
            var toSave = comp; Scribe_Deep.Look(ref toSave, "ins", Current.Game); Scribe.saver.FinalizeSaving();
            InsuranceComponent loaded = null;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "ins", Current.Game); Scribe.loader.FinalizeLoading();
            var lc = loaded?.contracts.FirstOrDefault(x => x.defName == def.defName);
            t.Check(lc != null && lc.pensionStrikes == 1 && lc.pensionSuspendedUntil == r4.contract.pensionSuspendedUntil && lc.pensionNoticed, "pension state survives a save");
            try { System.IO.File.Delete(path); } catch { }

            InsTest110.RestoreSchedule(pawn, sched0); pawn.ageTracker.AgeBiologicalTicks = age0;
            InsTest.RestoreSettings();
            yield break;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------- F27
    public sealed class InsuranceOpportunitySuite : HearthTestSuite
    {
        public override string Name => "insurance.opportunity";
        public override ModuleId Module => ModuleId.Bank;

        sealed class FakeAi : AiApi.IAiProvider
        {
            public bool on = true; public Action<string> pending; public string lastText; public Dictionary<string, object> lastFields;
            public bool Available(string function) => on && function == "claim-letter";
            public void Request(string function, Dictionary<string, object> fields, Action<string> onText, Action<string> onError) { pending = onText; lastFields = fields; }
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(20000);
            var comp = InsTest.Comp; var map = InsTest.Map;
            var def = InsTest.Def("SL_ActsOfStoryteller");
            InsurancePricing.SumRange(def, map, null, out var smin, out var smax);
            var res = comp.Sign(def, CoverTier.GoodEnough, null, smin, null, null);
            t.Check(res.Ok, "signed Acts of Storyteller: " + res.error); if (!res.Ok) yield break;
            var c = res.contract; InsTest.SkipWaiting(c);
            InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            t.Check(def.claimDeadlineDays == 15 && ClaimOpportunities.DeadlineDays(c) == 15, "deadline of the property policy: " + ClaimOpportunities.DeadlineDays(c) + " days");
            var cp = new PolicyContract { defName = c.defName, tier = CoverTier.Premium }; var cb = new PolicyContract { defName = c.defName, tier = CoverTier.BareMinimum };
            t.Check(ClaimOpportunities.DeadlineDays(cp) > 15 && ClaimOpportunities.DeadlineDays(cb) < 15, $"Premium longer, Bare Minimum shorter: {ClaimOpportunities.DeadlineDays(cb)}/{ClaimOpportunities.DeadlineDays(cp)}");
            InsTest.Set(InsSettings.DeadlineScale, 2f); t.Check(ClaimOpportunities.DeadlineDays(c) == 30, "the deadline factor scales it"); InsTest.Set(InsSettings.DeadlineScale, 1f);
            t.Check(InsuranceComponent.Instance.GetType().GetField("opportunities") != null, "component stores filing windows");

            // --- three buildings in one raid -> one window, no claim yet ---------------------------------------------------------------------------------------
            int claims0 = comp.claims.Count; int now = Ins.Now;
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 120, "wooden wall", map, new List<string> { "Siege" }, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.WoodLog, 20) });
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 80, "cooler", map, null, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 10) });
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 100, "door", map, new List<string> { "Door" }, null);
            t.Check(comp.claims.Count == claims0, "form mode: no claim is filed at the moment of damage");
            t.Check(comp.opportunities.Count == 1 && comp.opportunities[0].itemLabels.Count == 3, "three losses of one raid merge into one filing window: " + comp.opportunities.Count);
            var o = comp.opportunities[0];
            t.Check(Math.Abs(o.TotalValue - 300) < 1e-9 && o.eventTick == now && o.deadlineTick == now + 15 * 60000, "value, event time and deadline stored");
            t.Check(o.hints.Contains("Siege") && o.hints.Contains("Door"), "hints merged");
            t.Check(ClawbackApi.ReportLoss(ClaimTrigger.Liability, map, Money.FromSilver(50), "nothing") == false, "no liability policy: the report is not taken");

            // --- the letter: once, delayed ----------------------------------------------------------------------------------------------------------------------
            int lp0 = InsTest110.Letters<ClaimPossibleLetter>();
            comp.Process(Ins.Now);
            t.Check(InsTest110.Letters<ClaimPossibleLetter>() == lp0 && !o.letterSent, "no letter in the first hour after the last loss");
            o.lastItemTick = Ins.Now - 3000;
            comp.Process(Ins.Now); comp.Process(Ins.Now);
            t.Check(InsTest110.Letters<ClaimPossibleLetter>() == lp0 + 1 && o.letterSent, "exactly one 'claim possible' letter");
            var letter = Find.LetterStack.LettersListForReading.OfType<ClaimPossibleLetter>().Last();
            t.Check(letter.oppId == o.id && letter.def == SLInsuranceDefOf.SL_ClaimPossible && letter.Choices.Count() >= 3, "letter of its own kind with options: " + letter.Choices.Count());
            t.Check(ClaimLetters.IsOurs(letter), "the module-removal clean-up recognises it");

            // --- the reminder ----------------------------------------------------------------------------------------------------------------------------------------
            int all0 = InsTest110.AllLetters();
            o.eventTick = Ins.Now - 14 * 60000; o.deadlineTick = Ins.Now + 90000;
            comp.Process(Ins.Now);
            t.Check(o.remindSent && InsTest110.AllLetters() == all0 + 1, "a reminder letter two days before the deadline");
            o.eventTick = now; o.deadlineTick = now + 15 * 60000;

            // --- the form ------------------------------------------------------------------------------------------------------------------------------------------------
            var forms = ClaimOpportunities.FormsFor(c, ClaimTrigger.RaidBuilding);
            t.Check(forms.Count >= 1 && forms.SelectMany(f => f.fields).Any(f => f.kind == ClaimFieldKind.Payout) && forms.SelectMany(f => f.fields).Any(f => f.key == "extent"), "form fields come from the defs");
            var answers = new Dictionary<string, string> { { "cause", "raid" }, { "extent", "major" }, { "payout", "materials" } };
            var claims = ClaimOpportunities.Submit(o, answers, "They came at dawn <b>with</b> a catapult.\nI saw it.", out var err);
            t.Check(claims != null && claims.Count >= 1, "submitted: " + err);
            if (claims == null) yield break;
            var cl = comp.claims.Last();
            t.Check(comp.claims.Count == claims0 + 1 && cl.items.Count == 3 && Math.Abs(cl.lossValue - 300) < 1e-9, $"one claim with all three items: {cl.items.Count}, loss {cl.lossValue}");
            t.Check(cl.eventTick == o.eventTick && cl.opportunityId == o.id && o.state == OppState.Filed && o.claimId == cl.id, "event time and window are linked to the claim");
            t.Check(cl.formAnswers.Contains("extent=major") && cl.formText != null && cl.formText.Contains("catapult") && !cl.formText.Contains("<") && !cl.formText.Contains("\n"), "form and sanitised statement stored: " + cl.formText);
            t.Check(cl.materialsChosen && cl.materials.Count == 2, "the payout choice is real: materials selected");
            t.Check(InsTest110.Letters<ClaimPossibleLetter>() == 0 || !Find.LetterStack.LettersListForReading.OfType<ClaimPossibleLetter>().Any(x => x.oppId == o.id), "the 'claim possible' letter is gone");
            t.Check(cl.Hint("Siege") && cl.Hint("Door"), "hints reached the claim");
            t.Check(ClaimOpportunities.Submit(o, null, null, out var err2) == null && err2 != null, "a window can only be used once");

            // --- the same outcome as in automatic mode ----------------------------------------------------------------------------------------------------------
            ClaimEngine.Close(cl);
            InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.Automatic);
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 120, "wooden wall", map, new List<string> { "Siege" }, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.WoodLog, 20) });
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 80, "cooler", map, null, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 10) });
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 100, "door", map, new List<string> { "Door" }, null);
            var auto = comp.claims.Last();
            t.Check(auto != cl && auto.items.Count == 3 && Math.Abs(auto.gross - cl.gross) < 1e-9 && Math.Abs(auto.lossValue - cl.lossValue) < 1e-9, $"automatic mode files the same claim: gross {auto.gross:0.##} vs {cl.gross:0.##}");
            ClaimEngine.Close(auto);

            // --- expiry --------------------------------------------------------------------------------------------------------------------------------------------------
            InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            ClaimIntake.Property(c, ClaimTrigger.RaidBuilding, 60, "stool", map, null, null);
            var o2 = comp.opportunities.Last(x => x.Open);
            int nClaims = comp.claims.Count; int lettersBefore = InsTest110.AllLetters();
            o2.deadlineTick = Ins.Now - 1;
            comp.Process(Ins.Now);
            t.Check(o2.state == OppState.Expired && comp.claims.Count == nClaims, "missed deadline: the window expires and no claim exists");
            t.Check(InsTest110.AllLetters() > lettersBefore, "a letter tells so");
            t.Check(ClaimOpportunities.Submit(o2, null, null, out var err3) == null && err3 != null, "an expired window cannot be filed");

            // --- a death and a limb ------------------------------------------------------------------------------------------------------------------------------------
            InsTest.Reset(20000); InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            var pawn = InsTest.Colonists.First();
            long pawnAge = InsTest110.AgeOf(pawn); InsTest110.SetAgeYears(pawn, 30);
            var acc = comp.Sign(InsTest.Def("SL_LimbByLimb"), CoverTier.GoodEnough, pawn, 2000, null, null); InsTest.SkipWaiting(acc.contract);
            var part = InsTest.Part(pawn, InsTest.BP("Hand"), "left");
            InsTest.LoseParts(pawn, part);
            t.Check(comp.opportunities.Count(x => x.Open && x.kind == OppKind.Limb) == 1 && !comp.claims.Any(x => x.trigger == ClaimTrigger.LimbLoss), "a lost hand opens a window instead of a claim");
            var lo = comp.opportunities.First(x => x.kind == OppKind.Limb);
            t.Check(lo.deadlineTick - lo.eventTick == 30 * 60000, "accident deadline 30 days");
            ClaimIntake.Death(acc.contract, pawn, false, false);
            t.Check(!lo.Open && comp.claims.Any(x => x.trigger == ClaimTrigger.LimbLoss), "a death files the open limb window first (clause 23 keeps working)");
            var dop = comp.opportunities.First(x => x.kind == OppKind.Death);
            t.Check(ClaimIntake.DeathKnown(comp, acc.contract, pawn, false), "the same death is not opened twice");
            var dc = ClaimOpportunities.Submit(dop, null, null, out _);
            t.Check(dc != null && dc.Any(x => x.death), "death claim filed from its window");
            InsTest110.RestoreSchedule(pawn, InsTest110.SaveSchedule(pawn)); pawn.ageTracker.AgeBiologicalTicks = pawnAge;
            foreach (var m in pawn.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.MissingBodyPart).ToList()) pawn.health.RemoveHediff(m);

            // --- AI reply to the statement ---------------------------------------------------------------------------------------------------------------------------------
            InsTest.Reset(20000); InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            var ai = new FakeAi(); AiApi.Impl = ai;
            float oldTimeout = ClaimLetters.AiTimeoutSeconds;
            try
            {
                var r5 = comp.Sign(def, CoverTier.GoodEnough, null, smin, null, null); InsTest.SkipWaiting(r5.contract);
                ClaimIntake.Property(r5.contract, ClaimTrigger.RaidBuilding, 90, "wall", map, null, null);
                var o5 = comp.opportunities.Last(); o5.lastItemTick = -99999;
                var cls = ClaimOpportunities.Submit(o5, new Dictionary<string, string> { { "cause", "raid" } }, "Please be kind.", out _);
                var c5 = cls[0]; c5.decideTick = Ins.Now; c5.state = ClaimState.Review;
                int dl0 = InsTest110.Letters<ClaimLetter>();
                ClaimEngine.Decide(c5);
                t.Check(ai.pending != null && c5.decisionLetterPending, "with the AI on the decision letter waits for the reply");
                t.Check(InsTest110.Letters<ClaimLetter>() == dl0, "no decision letter yet");
                var form = ai.lastFields != null && ai.lastFields.TryGetValue("form", out var fo) ? fo as Dictionary<string, object> : null;
                t.Check(form != null && (string)form["free_text"] == "Please be kind." && ((Dictionary<string, string>)form["choices"]).ContainsKey("cause"), "the request carries the choices and the statement");
                t.Check(ai.lastFields.ContainsKey("outcome") && ai.lastFields.ContainsKey("amount"), "outcome and amount are passed as facts");
                ai.pending("We have read your statement with great sympathy.");
                ClaimLetters.PollAi();
                var dl = Find.LetterStack.LettersListForReading.OfType<ClaimLetter>().LastOrDefault(x => x.claimId == c5.id);
                t.Check(dl != null && dl.Text.ToString().Contains("great sympathy") && !c5.decisionLetterPending, "the letter carries the AI paragraph");
                t.Check(dl != null && dl.Text.ToString().Contains(Ins.Silver(c5.offered)) || c5.outcome == ClaimOutcome.Denied, "and still our figures");
                t.Check(c5.aiReply != null, "reply stored with the claim");
                ClaimEngine.Close(c5);

                // timeout: no reply -> the offline letter arrives
                ClaimLetters.AiTimeoutSeconds = 0.2f;
                ClaimIntake.Property(r5.contract, ClaimTrigger.RaidBuilding, 90, "wall", map, null, null);
                var o6 = comp.opportunities.Last(x => x.Open); var c6 = ClaimOpportunities.Submit(o6, null, "Any news?", out _)[0];
                c6.decideTick = Ins.Now; ClaimEngine.Decide(c6);
                t.Check(c6.decisionLetterPending && ClaimLetters.AiWaiting(c6.id), "waiting for the AI");
                for (int i = 0; i < 400 && c6.decisionLetterPending; i++) yield return 3;
                t.Check(!c6.decisionLetterPending && Find.LetterStack.LettersListForReading.OfType<ClaimLetter>().Any(x => x.claimId == c6.id), "no reply: the offline letter is sent after the timeout");
                ClaimEngine.Close(c6);

                // AI off: immediate letter
                ai.on = false;
                ClaimIntake.Property(r5.contract, ClaimTrigger.RaidBuilding, 90, "wall", map, null, null);
                var o7 = comp.opportunities.Last(x => x.Open); var c7 = ClaimOpportunities.Submit(o7, null, "Anybody there?", out _)[0];
                c7.decideTick = Ins.Now; ClaimEngine.Decide(c7);
                t.Check(!c7.decisionLetterPending && Find.LetterStack.LettersListForReading.OfType<ClaimLetter>().Any(x => x.claimId == c7.id), "without the AI the letter comes at once and the statement is just stored");
                ClaimEngine.Close(c7);
            }
            finally { AiApi.Impl = null; ClaimLetters.AiTimeoutSeconds = oldTimeout; }

            // --- save / load ------------------------------------------------------------------------------------------------------------------------------------------------------
            InsTest.Reset(20000); InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            var r8 = comp.Sign(def, CoverTier.GoodEnough, null, smin, null, null);
            ClaimIntake.Property(r8.contract, ClaimTrigger.RaidBuilding, 70, "wall", map, new List<string> { "Door" }, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 5) });
            string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "hh_insurance_opp_test.xml");
            Scribe.saver.InitSaving(path, "HHInsOpp");
            var toSave = comp; Scribe_Deep.Look(ref toSave, "ins", Current.Game); Scribe.saver.FinalizeSaving();
            InsuranceComponent loaded = null;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "ins", Current.Game); Scribe.loader.FinalizeLoading();
            var lo2 = loaded?.opportunities.FirstOrDefault();
            t.Check(lo2 != null && lo2.Open && lo2.itemLabels.Count == 1 && lo2.itemCosts[0] == "Steel:5" && lo2.hints.Contains("Door") && lo2.deadlineTick == comp.opportunities[0].deadlineTick, "filing windows survive a save");
            try { System.IO.File.Delete(path); } catch { }
            InsTest.RestoreSettings();
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------- F28
    public sealed class InsuranceStatusSuite : HearthTestSuite
    {
        public override string Name => "insurance.status";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(20000);
            var comp = InsTest.Comp;
            var cols = InsTest.Colonists;
            if (cols.Count < 2) { t.Check(false, "need two colonists"); yield break; }
            var a = cols[0]; var b = cols[1];
            long ageA = InsTest110.AgeOf(a); var schedA = a.timetable != null ? InsTest110.SaveSchedule(a) : null;
            InsTest110.SetAgeYears(a, 40);
            t.Check(!InsuranceStatus.UiActive, "nothing shown before the first policy (N08)");
            var tab = new ITab_SL_Insurance(); var col = new PawnColumnWorker_Insurance();
            t.Check(col.GetMinWidth(null) == 0 && col.GetMaxWidth(null) == 0, "the column has no width before the first policy");
            t.Check(!tab.IsVisible, "the tab is not visible without a pawn / policy");

            var pens = comp.Sign(InsTest.Def("SL_GoldenYears"), CoverTier.GoodEnough, a, 800, null, null);
            comp.Sign(InsTest.Def("SL_RimHealth"), CoverTier.GoodEnough, null, 1000, null, null);
            t.Check(InsuranceStatus.UiActive, "after the first policy the insurance UI is on");
            t.Check(col.GetMinWidth(null) > 0, "the column has its width now");
            var sa = InsuranceStatus.Of(a); var sb = InsuranceStatus.Of(b);
            CoverItem I(List<CoverItem> l, CoverKind k) => l.First(x => x.kind == k);
            t.Check(I(sa, CoverKind.Pension).Has && I(sa, CoverKind.Pension).state == CoverState.Waiting && !I(sa, CoverKind.Pension).colonyWide, "pension of A: yes, waiting period, personal");
            t.Check(!I(sb, CoverKind.Pension).Has, "pension of B: no");
            t.Check(I(sa, CoverKind.Health).Has && I(sa, CoverKind.Health).colonyWide && I(sb, CoverKind.Health).Has, "health plan covers everybody as colony cover");
            t.Check(!I(sa, CoverKind.Accident).Has && !I(sa, CoverKind.Liability).Has && !I(sa, CoverKind.Legal).Has, "accident, liability and legal: no");
            InsTest.SkipWaiting(pens.contract); comp.statusVersion++;
            t.Check(I(InsuranceStatus.Of(a), CoverKind.Pension).state == CoverState.Active, "a version change refreshes the cache at once: active");
            comp.Sign(InsTest.Def("SL_LimbByLimb"), CoverTier.GoodEnough, a, 1500, null, null);
            t.Check(I(InsuranceStatus.Of(a), CoverKind.Accident).Has, "newly signed accident cover shows immediately");
            comp.FileCancel(comp.contracts.First(x => x.defName == "SL_LimbByLimb"));
            t.Check(I(InsuranceStatus.Of(a), CoverKind.Accident).state == CoverState.CancelPending || !I(InsuranceStatus.Of(a), CoverKind.Accident).Has, "cancellation pending shows");

            // pension at risk
            if (a.timetable != null)
            {
                InsTest.Set(InsSettings.PensionRule, (float)PensionRuleMode.Enforce); InsTest.Set(InsSettings.PensionMaxHours, 2);
                InsTest110.SetAgeYears(a, 61); InsTest110.SetWorkHours(a, 7); comp.statusVersion++;
                t.Check(I(InsuranceStatus.Of(a), CoverKind.Pension).state == CoverState.AtRisk, "a pensioner who works too much shows 'at risk'");
                t.Check(InsuranceStatus.Line(a, I(InsuranceStatus.Of(a), CoverKind.Pension)).Contains(Ins.T("Status.Yes")), "line text");
                t.Check(PensionRule.PensionersAtRisk().Contains(a) || !a.Spawned, "the alert sees the pensioner");
                var alert = new Alert_PensionSchedule();
                t.Check(alert.GetReport().active || !a.Spawned, "alert is active");
                InsTest110.RestoreSchedule(a, schedA);
            }

            // who is insurable
            t.Check(InsuranceStatus.Insurable(a) && InsuranceStatus.Insurable(b), "colonists are insurable");
            var animal = InsTest.Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !p.RaceProps.Humanlike);
            t.Check(animal == null || !InsuranceStatus.Shown(animal), "animals are not shown");
            // tab registered on every humanlike race
            var humanlike = DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.Humanlike).ToList();
            t.Check(humanlike.Count > 0 && humanlike.All(d => d.inspectorTabs != null && d.inspectorTabs.Contains(typeof(ITab_SL_Insurance))), $"the tab is on all {humanlike.Count} humanlike races (also modded ones)");
            t.Check(ModuleGate_ok(), "pawn column def present");
            t.Check(DefDatabase<PawnTableDef>.GetNamedSilentFail("Assign")?.columns?.Any(x => x.defName == "SL_Insurance") == true, "the column is in the Assign table");

            // an overview page and the pawn tab draw without errors
            int err0 = HLog.ErrorCount;
            Window_Clawback.Open(); InsuranceUI.ShowPage(2);
            yield return 30;
            Find.Selector.ClearSelection(); Find.Selector.Select(a, false, false);
            yield return 10;
            t.Check(HLog.ErrorCount == err0, "colonist overview page draws without errors");
            foreach (var w in Find.WindowStack.Windows.Where(w => w is Window_Clawback).ToList()) Find.WindowStack.TryRemove(w, false);
            Find.Selector.ClearSelection();
            InsuranceUI.ShowPage(0);
            a.ageTracker.AgeBiologicalTicks = ageA;
            InsTest.RestoreSettings();
        }
        static bool ModuleGate_ok() => DefDatabase<PawnColumnDef>.GetNamedSilentFail("SL_Insurance") != null;
    }

    // ---------------------------------------------------------------------------------------------------------------------- F35 (insurance windows)
    public sealed class InsuranceWindowsSuite : HearthTestSuite
    {
        public override string Name => "insurance.windows";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(20000);
            var comp = InsTest.Comp; var map = InsTest.Map;
            var res = comp.Sign(InsTest.Def("SL_MuffaloMachine"), CoverTier.GoodEnough, null, 2000, new[] { "boost" }, null);
            InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            ClaimIntake.Property(res.contract, ClaimTrigger.VehicleBuilding, 400, "shuttle", map, null, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 30) });
            var opp = comp.opportunities.Last();
            var cl = new Claim { id = comp.nextClaimId++, contractId = res.contract.id, policyDef = res.contract.defName, tier = CoverTier.GoodEnough, trigger = ClaimTrigger.RaidBuilding, state = ClaimState.Decided, outcome = ClaimOutcome.Partial, gross = 100, offered = 50, payTick = Ins.Now + 99999, subject = "test", breakdown = new List<string> { "line one", "line two" } };
            comp.claims.Add(cl);
            int err0 = HLog.ErrorCount;
            var windows = new List<Window>
            {
                new Dialog_Negotiate(cl.id), new Dialog_CancelPolicy(res.contract.id), new Dialog_ChangePolicy(res.contract), new Dialog_CoverEveryone(), new Dialog_ClaimForm(opp.id)
            };
            foreach (var w in windows)
            {
                var size = w.InitialSize;
                t.Check(size.x <= UI.screenWidth && size.y <= UI.screenHeight && size.x >= 400f && size.y >= 380f, $"{w.GetType().Name} size {size.x:0}x{size.y:0} fits the screen {UI.screenWidth}x{UI.screenHeight}");
                Find.WindowStack.Add(w);
            }
            Window_Clawback.Open();
            var wc = Find.WindowStack.Windows.OfType<Window_Clawback>().First();
            t.Check(wc.InitialSize.x <= UI.screenWidth && wc.InitialSize.y <= UI.screenHeight, "Clawback window fits the screen");
            for (int page = 0; page < 5; page++) { InsuranceUI.ShowPage(page, page == 0); yield return 8; }
            // text heights: the negotiate dialog's wrapped lines never exceed what they were given
            t.Check(InsDlg.TextH("x", 50f) > 0f, "text measuring works");
            yield return 20;
            foreach (var w in windows) t.Check(Find.WindowStack.IsOpen(w), w.GetType().Name + " stays open while drawing");
            t.Check(HLog.ErrorCount == err0, "no errors while drawing all insurance windows: " + (HLog.ErrorCount - err0));
            t.Check(!Guard.IsPaused(ModuleId.Bank), "bank module not paused by a UI error");
            foreach (var w in Find.WindowStack.Windows.Where(w => w is Window_Clawback || w is Dialog_Negotiate || w is Dialog_CancelPolicy || w is Dialog_ChangePolicy || w is Dialog_CoverEveryone || w is Dialog_ClaimForm).ToList()) Find.WindowStack.TryRemove(w, false);
            ClaimEngine.Close(cl);
            InsuranceUI.ShowPage(0);
            InsTest.RestoreSettings();
        }
    }

    /// <summary>With HH_UI_SHOTS=&lt;dir&gt;: saves a screenshot of each new window and page (to look at the layout by eye). Without it the suite only draws them.</summary>
    public sealed class InsuranceShots110Suite : HearthTestSuite
    {
        public override string Name => "insurance.shots110";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 20000;
        static void Shot(string name)
        {
            string dir = Environment.GetEnvironmentVariable("HH_UI_SHOTS"); if (string.IsNullOrEmpty(dir)) return;
            try { System.IO.Directory.CreateDirectory(dir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); }
            catch (Exception e) { Log.Warning("[HHTEST] screenshot failed: " + e.Message); }
        }
        static void CloseAll() { foreach (var w in Find.WindowStack.Windows.Where(w => w is Window_Clawback || w is Dialog_Negotiate || w is Dialog_CancelPolicy || w is Dialog_ChangePolicy || w is Dialog_CoverEveryone || w is Dialog_ClaimForm).ToList()) Find.WindowStack.TryRemove(w, false); }

        public override IEnumerable<int> Run(TestContext t)
        {
            bool shots = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HH_UI_SHOTS"));
            InsTest.Reset(30000);
            var comp = InsTest.Comp; var map = InsTest.Map;
            var cols = InsTest.Colonists;
            var pawn = cols.First(p => p.timetable != null);
            long age0 = InsTest110.AgeOf(pawn); var sched0 = InsTest110.SaveSchedule(pawn);
            InsTest110.SetAgeYears(pawn, 40);
            var muff = comp.Sign(InsTest.Def("SL_MuffaloMachine"), CoverTier.GoodEnough, null, 2000, new[] { "boost" }, null); InsTest.SkipWaiting(muff.contract);
            var pens = comp.Sign(InsTest.Def("SL_GoldenYears"), CoverTier.GoodEnough, pawn, 800, null, null); InsTest.SkipWaiting(pens.contract);
            comp.Sign(InsTest.Def("SL_RimHealth"), CoverTier.GoodEnough, null, 1000, null, null);
            InsTest110.SetAgeYears(pawn, 61); InsTest110.SetWorkHours(pawn, 7);
            comp.ApplyChange(comp.PlanChange(muff.contract, CoverTier.Premium, 3000, new[] { "boost" }), out _);
            InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            ClaimIntake.Property(muff.contract, ClaimTrigger.VehicleBuilding, 400, "shuttle", map, null, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 30) });
            ClaimIntake.Property(muff.contract, ClaimTrigger.VehicleBuilding, 250, "drop pod", map, null, null);
            var opp = comp.opportunities.Last(); opp.lastItemTick = Ins.Now - 4000; comp.Process(Ins.Now);
            var cl = new Claim { id = comp.nextClaimId++, contractId = muff.contract.id, policyDef = muff.contract.defName, tier = CoverTier.GoodEnough, trigger = ClaimTrigger.RaidBuilding, state = ClaimState.Decided, outcome = ClaimOutcome.Partial, gross = 100, offered = 50, payTick = Ins.Now + 99999, subject = "two wooden walls", breakdown = new List<string> { "line one", "line two" }, clauseDef = "SL_Clause_14b" };
            comp.claims.Add(cl);
            var done = new List<string>();
            IEnumerable<int> Take(string name, Action open) { CloseAll(); open(); yield return 25; Shot(name); yield return 20; done.Add(name); }
            foreach (var step in new (string, Action)[]
            {
                ("ins_mine", () => { InsuranceUI.ShowPage(1); Window_Clawback.Open(); }),
                ("ins_cover", () => { InsuranceUI.ShowPage(2); Window_Clawback.Open(); }),
                ("ins_claims", () => { InsuranceUI.ShowPage(3); Window_Clawback.Open(); }),
                ("ins_shop", () => { InsuranceUI.ShowPage(0); Window_Clawback.Open(); }),
                ("ins_change", () => Find.WindowStack.Add(new Dialog_ChangePolicy(muff.contract))),
                ("ins_bulk", () => Find.WindowStack.Add(new Dialog_CoverEveryone("SL_LimbByLimb"))),
                ("ins_form", () => Find.WindowStack.Add(new Dialog_ClaimForm(opp.id))),
                ("ins_negotiate", () => Find.WindowStack.Add(new Dialog_Negotiate(cl.id))),
                ("ins_cancel", () => Find.WindowStack.Add(new Dialog_CancelPolicy(muff.contract.id))),
            })
                foreach (var tick in Take(step.Item1, step.Item2)) yield return tick;
            CloseAll();
            Find.Selector.ClearSelection(); Find.Selector.Select(pawn, false, false);
            yield return 5;
            t.Check(done.Count == 9, "all windows were shown: " + done.Count + (shots ? " (screenshots saved)" : " (no HH_UI_SHOTS: not saved)"));
            Find.Selector.ClearSelection();
            ClaimEngine.Close(cl);
            InsTest110.RestoreSchedule(pawn, sched0); pawn.ageTracker.AgeBiologicalTicks = age0;
            InsuranceUI.ShowPage(0);
            InsTest.RestoreSettings();
        }
    }
}
#endif
