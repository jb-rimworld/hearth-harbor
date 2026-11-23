// SPDX-License-Identifier: MIT
// Screenshots of The Lucky Thrumbo (Workshop gallery): HH_TEST=showcase.thrumbo, see tools/showcase/run_thrumbo_shots.sh. Not a real test (ExplicitOnly).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Casino.PlayerHouse;
using Hearth.Casino.Tests;
using Hearth.CasinoMath;
using Hearth.Shell;
using Hearth.Shell.Tests;
using Hearth.Testing;
using Silverline;

namespace Hearth.Showcase
{
    public sealed class ThrumboShotsSuite : HearthTestSuite
    {
        public override string Name => "showcase.thrumbo";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 60000;

        object shotMode; FieldInfo shotField;
        const string OutDir = "/private/tmp/claude-501/hh_tests/thrumbo_shots";
        static string ShotDir => GenFilePaths.ScreenshotFolderPath;
        readonly List<(string file, string name)> renames = new List<(string, string)>();

        void Tidy()
        {
            try { Prefs.DevMode = false; } catch { }
            try { foreach (var w in Find.WindowStack.Windows.Where(w => w.GetType().Name.StartsWith("EditWindow")).ToList()) Find.WindowStack.TryRemove(w, false); } catch { }
            try { Messages.Clear(); } catch { }
            try { foreach (var l in Find.LetterStack.LettersListForReading.ToList()) Find.LetterStack.RemoveLetter(l); } catch { }
        }

        IEnumerable<int> Shot(TestContext T, string name)
        {
            Tidy(); yield return 60; Tidy();
            Directory.CreateDirectory(ShotDir);
            var before = new HashSet<string>(Directory.GetFiles(ShotDir, "*.png"));
            string found = null;
            for (int attempt = 0; attempt < 6 && found == null; attempt++)
            {
                ScreenshotTaker.TakeNonSteamShot();
                for (int i = 0; i < 12 && found == null; i++)
                {
                    yield return 15;
                    found = Directory.GetFiles(ShotDir, "*.png").FirstOrDefault(f => !before.Contains(f) && Path.GetFileName(f).StartsWith("screenshot"));
                }
                if (found == null) yield return 120;
            }
            if (found == null) { T.Check(false, "screenshot " + name + " was written"); yield break; }
            yield return 10;
            renames.Add((found, name));
            T.Check(new FileInfo(found).Length > 20000, "screenshot " + name);
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            string want = Environment.GetEnvironmentVariable("GAME_LANG");
            if (!string.IsNullOrEmpty(want) && LanguageDatabase.activeLanguage?.folderName != want)
            {
                // quicktest starts in English whatever Prefs.xml says: switch the loaded language data in place (keyed strings are all the windows need)
                var lang = LanguageDatabase.AllLoadedLanguages.FirstOrDefault(l => l.folderName == want);
                if (lang != null) { LanguageDatabase.activeLanguage = lang; Prefs.LangFolderName = want; lang.LoadData(); }
            }
            t.Note("active language: " + LanguageDatabase.activeLanguage?.folderName + " / prefs " + Prefs.LangFolderName + "; Thrumbo title = " + "HH.Thrumbo.Title".Translate());
            var map = t.Map; var col = Kit.Colonist(t);
            try { if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true); } catch { }
            var comp = ThrumboComponent.Get(); comp.Clear();
            var bank = BankGameComp.Current; bank.TestReset(true);
            Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(4250), "showcase"); bank.InvalidateCache();
            HSettings.Set("core.helpHint", 1f);
            Tidy();
            // some history so the window looks lived in
            var seat = new ThrumboSeat(map, col, PayFrom.Bank);
            foreach (var v in new[] { SlotVolatility.Medium, SlotVolatility.Low, SlotVolatility.High, SlotVolatility.Medium })
                ThrumboHouse.PlaySlots(seat, Money.FromSilver(10), v);
            for (int i = 0; i < 3; i++) ThrumboHouse.PlayRoulette(seat, Money.FromSilver(5), RouletteBet.Red());
            var d0 = ThrumboHouse.DealBlackjack(seat, Money.FromSilver(5)); if (!d0.Done) ThrumboHouse.Stand(seat);
            yield return 5;

            // card on the Casino page
            Find.CameraDriver.SetRootPosAndSize(col.Position.ToVector3Shifted(), 14f);
            var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
            Find.MainTabsRoot.SetCurrentTab(tabDef, false); yield return 10;
            MainTabWindow_HH.SelectedPage = "casino"; yield return 20;
            foreach (var w in Shot(t, "22_thrumbo_card")) yield return w;
            Find.MainTabsRoot.SetCurrentTab(tabDef, false); yield return 10;
            if (Find.WindowStack.WindowOfType<MainTabWindow_HH>() != null) { Find.MainTabsRoot.EscapeCurrentTab(false); yield return 10; }

            var win = UiTestUtil.NoPause(new Window_Thrumbo(col)); Find.WindowStack.Add(win); yield return 20;
            win.Game = ThrumboGame.Slots;
            foreach (var w in Shot(t, "22_thrumbo_slots_start")) yield return w;
            win.TestPlay("slots", "10"); yield return 10;
            foreach (var w in Shot(t, "22_thrumbo_slots")) yield return w;
            win.TestPlay("roulette", "5"); yield return 10;
            foreach (var w in Shot(t, "22_thrumbo_roulette")) yield return w;
            win.TestPlay("deal", "20"); yield return 10;
            foreach (var w in Shot(t, "22_thrumbo_blackjack")) yield return w;
            ThrumboHouse.StandIfOpen(seat);
            win.Close(false); yield return 10;

            foreach (var (file, name) in renames)
                try { string dest = Path.Combine(ShotDir, name + ".png"); if (File.Exists(dest)) File.Delete(dest); File.Move(file, dest); Directory.CreateDirectory(OutDir); File.Copy(dest, Path.Combine(OutDir, name + ".png"), true); } catch (Exception e) { t.Note("rename failed: " + e.Message); }
            comp.Clear(); bank.TestReset(true); HSettings.Set("core.helpHint", 0f);
        }
    }
}
#endif
