// SPDX-License-Identifier: MIT
// Presentation run: builds a good-looking hotel by code, fills it with guests and takes screenshots of the mod in action.
// Not a real test (ExplicitOnly): HH_TEST=showcase, see tools/showcase/run_showcase.sh. Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Guests;
using Hearth.Insurance;
using Hearth.Kitchen;
using Hearth.Shell;
using Hearth.Shell.Tests;
using Hearth.ShopsFood;
using Hearth.Testing;
using Silverline;

namespace Hearth.Showcase
{
    public sealed class ShowcaseSuite : HearthTestSuite
    {
        public override string Name => "showcase";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 400000;

        ShowcaseBuilder b = new ShowcaseBuilder();
        Map map => b.map; TestContext T => b.T; IntVec3 O => b.O;
        CellRect casino => b.casino;
        CellRect lobby => b.lobby;
        CellRect restaurant => b.restaurant;
        CellRect spa => b.spa;
        CellRect bank => b.bank;
        List<Thing> powered => b.powered;
        IntVec3 P(int x, int z) => b.P(x, z);
        void RefuelAll() => b.RefuelAll();
        readonly List<string> shots = new List<string>();
        readonly List<(string file, string name)> renames = new List<(string, string)>();
        object shotMode; FieldInfo shotField;

        // ---------------------------------------------------------------- screenshot plumbing
        void HookScreenshotMode()
        {
            try
            {
                var root = Find.UIRoot;
                foreach (var f in root.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (f.FieldType.Name == "ScreenshotModeHandler") { shotMode = f.GetValue(root); break; }
                if (shotMode != null)
                {
                    shotField = shotMode.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(bool) && f.Name.ToLower().Contains("active"))
                                ?? shotMode.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(bool));
                }
                T.Note("screenshot-mode handler: " + (shotMode != null) + ", field " + shotField?.Name);
            }
            catch (Exception e) { T.Note("screenshot mode hook failed: " + e.Message); }
        }

        void SetShotMode(bool on) { try { shotField?.SetValue(shotMode, on); } catch { } }

        void Tidy()
        {
            try { Prefs.DevMode = false; } catch { }
            try { foreach (var w in Find.WindowStack.Windows.Where(w => w.GetType().Name.StartsWith("EditWindow")).ToList()) Find.WindowStack.TryRemove(w, false); } catch { }
            try { Messages.Clear(); } catch { }
            try { foreach (var l in Find.LetterStack.LettersListForReading.ToList()) Find.LetterStack.RemoveLetter(l); } catch { }
        }

        void Cam(float x, float z, float size)
        {
            var v = new Vector3(O.x + x + 0.5f, 0f, O.z + z + 0.5f);
            Find.CameraDriver.SetRootPosAndSize(v, size);
        }

        const string OutDir = "/private/tmp/claude-501/hh_tests/showcase_shots";
        static string ShotDir => GenFilePaths.ScreenshotFolderPath;

        /// <summary>Moves nothing: waits, takes a screenshot, renames the new file to name.png.</summary>
        IEnumerable<int> Shot(string name, bool hideUi)
        {
            Tidy();
            SetShotMode(hideUi);
            yield return 110;
            Tidy();
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
                if (found == null) { T.Note("screenshot " + name + " attempt " + (attempt + 1) + " produced no file, retrying"); yield return 120; }
            }
            SetShotMode(false);
            if (found == null) { T.Check(false, "screenshot " + name + " was written; folder has: " + string.Join(",", Directory.GetFiles(ShotDir).Select(Path.GetFileName))); yield break; }
            yield return 10;
            renames.Add((found, name));
            shots.Add(name);
            T.Check(File.Exists(found) && new FileInfo(found).Length > 20000, "screenshot " + name + " = " + Path.GetFileName(found) + " (" + new FileInfo(found).Length / 1024 + " kB)");
            Tidy();
        }

        // ---------------------------------------------------------------- the suite
        public override IEnumerable<int> Run(TestContext t)
        {
            b.T = t; b.map = t.Map;
            try { if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true); } catch { }
            Tidy();
            HookScreenshotMode();
            try { Find.CameraDriver.config.dollyRateKeys = 0; } catch { }

            foreach (var w in b.Build(t)) yield return w;
            var cols = b.Cols;

            // --- guests
            var f = InnTestKit.GuestFaction(map);
            t.Check(f != null, "friendly faction for guests: " + f?.Name);
            var groups = new List<GuestGroup>();
            if (f != null)
            {
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
                foreach (var (size, kind, nights, silver, pod) in new[] { (2, StayKind.Overnight, 2, 700, true), (3, StayKind.Overnight, 2, 900, true), (3, StayKind.DayVisitor, 1, 500, true) })
                {
                    var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = size, Kind = kind, Nights = nights, ForcePod = pod, ForceBudget = Money.FromSilver(silver), Silent = true });
                    if (g != null) groups.Add(g);
                    yield return 30;
                }
                t.Check(groups.Count > 0, "guest groups arrived by incident: " + groups.Count);
                // a vanilla visitor group too
                try
                {
                    var parms = new IncidentParms { target = map, faction = f, forced = true, sendLetter = false, points = 120f };
                    IncidentDefOf.VisitorGroup.Worker.TryExecute(parms);
                }
                catch (Exception e) { t.Note("vanilla visitors: " + e.Message); }
            }
            Tidy();

            // shots of the empty-ish hotel first (guests are still dropping in)
            Cam(21, 6, 19.5f);
            foreach (var w in Shot("01_hotel_overview_arrival", true)) yield return w;

            // --- let the game run until guests are inside
            int waited = 0;
            var reg = GuestRegistry.Instance;
            while (waited < 16000)
            {
                yield return 500; waited += 500;
                RefuelAll();
                var members = groups.SelectMany(g => reg.Members(g)).ToList();
                if (members.Count > 0 && members.Count(m => m.CheckedIn) >= Math.Min(3, members.Count) && waited >= 5000) break;
            }
            t.Note("waited " + waited + " ticks for guests; checked in: " + groups.SelectMany(g => reg.Members(g)).Count(m => m.CheckedIn));
            // let them start spending (casino/restaurant) a little more
            for (int i = 0; i < 6; i++) { yield return 400; RefuelAll(); }

            var guests = map.mapPawns.AllPawnsSpawned.Where(p => GuestApi.IsGuest(p) && !p.Dead).ToList();
            t.Check(guests.Count >= 3, "guests present on the map: " + guests.Count);

            // --- map shots
            Cam(21, 6, 19.5f);
            foreach (var w in Shot("02_hotel_overview", true)) yield return w;

            Cam(21, 12.5f, 12.5f);
            foreach (var w in Shot("03_guest_wing_spa_bank", true)) yield return w;

            Cam(20.5f, 5.5f, 6.6f);
            foreach (var w in Shot("04_lobby", true)) yield return w;

            // casino: make sure guests are playing around the machines
            foreach (var g in Place(guests.Where(p => !InRect(p, casino)).Take(3), casino, new[] { P(38, 5), P(34, 4), P(32, 8) })) yield return g;
            Cam(34, 5.5f, 6.4f);
            foreach (var w in Shot("05_casino", true)) yield return w;

            foreach (var g in Place(guests.Where(p => !InRect(p, restaurant)).Take(3), restaurant, new[] { P(5, 5), P(9, 5), P(6, 2) })) yield return g;
            Cam(7, 5.5f, 6.4f);
            foreach (var w in Shot("06_restaurant", true)) yield return w;

            Cam(9, 17.5f, 5.8f);
            foreach (var w in Shot("07_guest_rooms", true)) yield return w;

            // a selected guest bed: the wing outline and the bed's inspect string (price, wing status, next arrivals)
            var gbed = GuestBeds.On(map).FirstOrDefault();
            if (gbed != null)
            {
                Find.Selector.ClearSelection(); Find.Selector.Select(gbed);
                Cam(14, 15f, 9.5f);
                yield return 20;
                foreach (var w in Shot("51_guest_bed_wing", false)) yield return w;
                Find.Selector.ClearSelection();
            }

            Cam(24, 17.5f, 5.2f);
            foreach (var w in Shot("08_spa", true)) yield return w;

            Cam(35, 17.5f, 5.2f);
            foreach (var w in Shot("09_bank_corner", true)) yield return w;

            Cam(21, 1, 9f);
            foreach (var w in Shot("10_entrance", true)) yield return w;

            // --- windows (UI visible, no screenshot mode)
            Cam(21, 8, 20f);
            var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
            Find.MainTabsRoot.SetCurrentTab(tabDef, false);
            yield return 10;
            foreach (var id in new[] { "overview", "guests" })
            {
                MainTabWindow_HH.SelectedPage = id;
                foreach (var w in Shot("20_main_tab_" + id, false)) yield return w;
            }
            foreach (var pg in MainTabWindow_HH.Pages().Where(p => p.Id != "overview" && p.Id != "guests" && (p.Id == "kitchen" || p.Id == "casino")))
            {
                MainTabWindow_HH.SelectedPage = pg.Id;
                foreach (var w in Shot("21_main_tab_" + pg.Id, false)) yield return w;
            }
            Find.MainTabsRoot.SetCurrentTab(tabDef, false); // toggles closed
            yield return 10;
            if (Find.WindowStack.WindowOfType<MainTabWindow_HH>() != null) { Find.MainTabsRoot.EscapeCurrentTab(false); yield return 10; }

            var bankWin = UiTestUtil.NoPause(new Window_Bank(map, BankSite.Remote, null));
            Find.WindowStack.Add(bankWin);
            foreach (var tab in new[] { "lobby", "insurance", "invest" })
            {
                bankWin.TabId = tab;
                yield return 20;
                foreach (var w in Shot("30_bank_" + tab, false)) yield return w;
            }
            bankWin.Close(false);
            yield return 10;

            var settings = UiTestUtil.NoPause(new Dialog_ModSettings(HearthMod.Instance));
            Find.WindowStack.Add(settings);
            HearthSettingsWindow.SelectedPage = "modules"; HearthSettingsWindow.Search = "";
            yield return 20;
            foreach (var w in Shot("40_mod_settings", false)) yield return w;
            settings.Close(false);
            yield return 10;

            var card = UiTestUtil.NoPause(new Dialog_FirstStartCard(false));
            Find.WindowStack.Add(card);
            yield return 20;
            foreach (var w in Shot("41_first_start_card", false)) yield return w;
            card.Close(false);
            yield return 10;

            // guest inspect pane
            var guest = guests.FirstOrDefault(p => p.Spawned && !p.Dead && reg.Get(p)?.CheckedIn == true && !reg.Get(p).Settled && reg.Get(p).Bed != null)
                        ?? guests.FirstOrDefault(p => p.Spawned && !p.Dead && reg.Get(p)?.Settled == false) ?? guests.FirstOrDefault(p => p.Spawned && !p.Dead);
            if (guest != null)
            {
                guest.Position = P(19, 6); guest.Notify_Teleported(false); guest.jobs.StopAll();
                Cam(20.5f, 5.5f, 9.5f);
                Find.Selector.ClearSelection(); Find.Selector.Select(guest);
                yield return 20;
                try { var insp = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow; insp.OpenTabType = typeof(ITab_HH_Guest); } catch (Exception e) { t.Note("open guest tab: " + e.Message); }
                yield return 20;
                foreach (var w in Shot("50_guest_inspect", false)) yield return w;
                Find.Selector.ClearSelection();
            }
            else t.Note("no guest left to inspect");

            // evening exterior
            try { float h = GenLocalDate.HourFloat(map); Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + (int)(((20.8f - h + 24f) % 24f) * 2500f)); } catch { }
            RefuelAll();
            Cam(21, 6, 19.5f);
            yield return 200;
            foreach (var w in Shot("11_hotel_at_night", true)) yield return w;

            t.Note("shots: " + string.Join(", ", shots));
            t.Note("screenshot folder: " + ShotDir);
            foreach (var (file, name) in renames)
                try { string dest = Path.Combine(ShotDir, name + ".png"); if (File.Exists(dest)) File.Delete(dest); File.Move(file, dest); Directory.CreateDirectory(OutDir); File.Copy(dest, Path.Combine(OutDir, name + ".png"), true); } catch (Exception e) { t.Note("rename failed: " + e.Message); }
            Tidy();
        }

        static bool InRect(Pawn p, CellRect r) => p.Spawned && r.Contains(p.Position);

        IEnumerable<int> Place(IEnumerable<Pawn> pawns, CellRect room, IntVec3[] spots)
        {
            int i = 0;
            foreach (var p in pawns.ToList())
            {
                if (i >= spots.Length) break;
                try
                {
                    var cell = spots[i++];
                    if (!cell.Standable(map)) cell = CellFinder.RandomClosewalkCellNear(cell, map, 2);
                    p.Position = cell; p.Notify_Teleported(false);
                    p.jobs.StopAll();
                }
                catch (Exception e) { T.Note("place guest: " + e.Message); }
            }
            yield return 2;
        }
    }
}
#endif
