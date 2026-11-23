// SPDX-License-Identifier: MIT
// Gallery shots for 1.1.0, bank and insurance part (explicit only): HH_TEST=gallery.bank | gallery.insurance.
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
using System.IO;

namespace Silverline
{
    public static class GalShotS
    {
        public const string OutDir = "/private/tmp/claude-501/hh_tests/gallery_v110";
        public static void Tidy()
        {
            try { Prefs.DevMode = false; } catch { }
            try { Messages.Clear(); } catch { }
            try { foreach (var l in Find.LetterStack.LettersListForReading.ToList()) Find.LetterStack.RemoveLetter(l); } catch { }
            try { foreach (var w in Find.WindowStack.Windows.Where(w => w.GetType().Name.StartsWith("EditWindow")).ToList()) Find.WindowStack.TryRemove(w, false); } catch { }
        }
        public static IEnumerable<int> Wait(float seconds) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < seconds) yield return 1; }
        public static void Init()
        {
            Directory.CreateDirectory(OutDir);
            HSettings.Set("core.guide", 0f);
            HSettings.Set("core.helpHint", 0f);
        }
        public static IEnumerable<int> Take(Hearth.Testing.TestContext t, string name, float pre = 0.8f)
        {
            Tidy();
            foreach (var w in Wait(pre)) yield return w;
            Tidy();
            string path = Path.Combine(OutDir, name + ".png");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            bool found = false;
            for (int attempt = 0; attempt < 3 && !found; attempt++)
            {
                try { ScreenCapture.CaptureScreenshot(path); } catch (Exception e) { t.Note("capture failed: " + e.Message); }
                float t0 = Time.realtimeSinceStartup;
                while (!found && Time.realtimeSinceStartup - t0 < 3f) { yield return 1; if (File.Exists(path)) found = true; }
            }
            foreach (var w in Wait(0.4f)) yield return w;
            t.Note((found ? "shot " : "NO shot ") + name);
        }
        public static void CloseAllWindows(Type keep = null)
        {
            foreach (var w in Find.WindowStack.Windows.ToList())
                if (w.GetType().Name.StartsWith("EditWindow") || w.GetType().Name.Contains("Messages")) continue;
                else Find.WindowStack.TryRemove(w, false);
        }
    }

    // ============================================================================================ bank
    public sealed class GalleryBankSuite : HearthTestSuite
    {
        public override string Name => "gallery.bank";
        public override ModuleId Module => ModuleId.Bank;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 5_000_000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var c = BankGameComp.Current; var map = t.Map;
            GalShotS.Init();
            HearthProfile.Current.Tier = 3f; HSettings.Set("bank.researchMode", 1f); BankCaretaker.CheckResearchOff();
            c.TestReset(false);
            BT.Jump(60000 * 45); yield return 1; yield return 60;
            c.OpenAccount(SL_BankDef.Default, true);
            c.TestReset(true, "SL_TooBigToFail");
            { int today = c.Today; var rnd = new System.Random(7); double running = 600;
              for (int d = today - 32; d < today; d++)
              { double move = rnd.Next(-120, 220); BT.Fund(Money.FromSilver(Math.Max(-running, move))); running = Math.Max(0, running + move); c.DailyStep(d); } }
            // securities
            var g = FinanceGame.Current; g.EnsureInit(); FinTest.Fund(Money.FromSilver(3500));
            g.Buy("SL_Asset_Index", Money.FromSilver(1500)); g.Buy("SL_Asset_RimCoin", Money.FromSilver(200)); g.OpenPatienceBond(60, Money.FromSilver(300));
            FinTest.Hours(g, 30);
            var lot = LotteryGame.Current; lot.EnsureInit(); lot.QuickPick(14); lot.DrawGlobal(Find.TickManager.TicksGame); lot.QuickPick(9); lot.QuickPick(3);
            c.TryTakeLoan(300, 60, out _, out _);
            BT.AddSilver(t, 800);
            var w0 = new Window_Bank(map, BankSite.Remote, null); w0.forcePause = false; w0.absorbInputAroundWindow = false;
            Find.WindowStack.Add(w0); yield return 40;
            foreach (var id in new[] { "lobby", "invest", "lottery" })
            {
                w0.TabId = id; yield return 40;
                if (id == "invest") InvestTab.TestSetPage(0);
                foreach (var w in GalShotS.Take(t, "bank_" + id, 1.0f)) yield return w;
            }
            w0.Close(false); yield return 3;
            c.TestReset(true);
            t.Check(true, "shots taken");
        }
    }

    // ============================================================================================ insurance
    public sealed class GalleryInsuranceSuite : HearthTestSuite
    {
        public override string Name => "gallery.insurance";
        public override ModuleId Module => ModuleId.Bank;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 900000;

        public override IEnumerable<int> Run(TestContext t)
        {
            GalShotS.Init();
            InsTest.Reset(30000);
            var comp = InsTest.Comp; var map = InsTest.Map; var cols = InsTest.Colonists;
            var pawn = cols.First(p => p.timetable != null);
            long age0 = InsTest110.AgeOf(pawn); var sched0 = InsTest110.SaveSchedule(pawn);
            InsTest110.SetAgeYears(pawn, 40);
            var muff = comp.Sign(InsTest.Def("SL_MuffaloMachine"), CoverTier.GoodEnough, null, 2000, new[] { "boost" }, null); InsTest.SkipWaiting(muff.contract);
            var pens = comp.Sign(InsTest.Def("SL_GoldenYears"), CoverTier.GoodEnough, pawn, 800, null, null); InsTest.SkipWaiting(pens.contract);
            comp.Sign(InsTest.Def("SL_RimHealth"), CoverTier.GoodEnough, null, 1000, null, null);
            InsTest110.SetAgeYears(pawn, 61); InsTest110.SetWorkHours(pawn, 7);
            // the overview of the colonists
            Window_Clawback.Open(); yield return 5; InsuranceUI.ShowPage(4); yield return 40;
            foreach (var w in GalShotS.Take(t, "insurance_overview", 1.0f)) yield return w;
            GalShotS.CloseAllWindows();
            InsTest.Set(InsSettings.ClaimModeKey, (float)ClaimMode.ByForm);
            ClaimIntake.Property(muff.contract, ClaimTrigger.VehicleBuilding, 400, "shuttle", map, null, new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 30) });
            ClaimIntake.Property(muff.contract, ClaimTrigger.VehicleBuilding, 250, "drop pod", map, null, null);
            var opp = comp.opportunities.Last(); opp.lastItemTick = Ins.Now - 4000; comp.Process(Ins.Now);
            Window_Clawback.Open(); yield return 5; InsuranceUI.ShowPage(2); yield return 40;
            Find.WindowStack.Add(new Dialog_ClaimForm(opp.id)); yield return 40;
            foreach (var w in GalShotS.Take(t, "insurance_claimform", 1.0f)) yield return w;
            GalShotS.CloseAllWindows();
            InsuranceUI.ShowPage(0);
            InsTest110.RestoreSchedule(pawn, sched0); pawn.ageTracker.AgeBiologicalTicks = age0;
            InsTest.RestoreSettings();
            t.Check(true, "shots taken");
        }
    }
}
#endif
