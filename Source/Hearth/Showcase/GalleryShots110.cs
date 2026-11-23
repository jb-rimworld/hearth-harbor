// SPDX-License-Identifier: MIT
// Steam Workshop gallery shots for 1.1.0 (explicit only): HH_TEST=gallery.casino | gallery.inn | gallery.bank | gallery.insurance. Files go to /private/tmp/claude-501/hh_tests/gallery_v110.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;
using Hearth.Casino.Anim;
using Hearth.Casino.PlayerHouse;
using Hearth.Casino.Tests;
using Hearth.Guests;
using Hearth.Shell;
using Hearth.Shell.Tests;
using Hearth.Testing;
using Hearth.Ai;
using Hearth.Ai.Tests;

namespace Hearth.Showcase
{
    public static class GalleryShot
    {
        public const string OutDir = "/private/tmp/claude-501/hh_tests/gallery_v110";
        public static void Tidy()
        {
            try { Prefs.DevMode = false; } catch { }
            try { Messages.Clear(); } catch { }
            try { foreach (var l in Find.LetterStack.LettersListForReading.ToList()) Find.LetterStack.RemoveLetter(l); } catch { }
            try { foreach (var w in Find.WindowStack.Windows.Where(w => w.GetType().Name.StartsWith("EditWindow")).ToList()) Find.WindowStack.TryRemove(w, false); } catch { }
        }
        public static Action Tick;
        public static IEnumerable<int> Wait(float seconds) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < seconds) { Tick?.Invoke(); yield return 1; } }
        public static void Init()
        {
            Directory.CreateDirectory(OutDir);
            HSettings.Set("core.guide", 0f);
            HSettings.Set("core.helpHint", 0f);
        }
        public static IEnumerable<int> Take(TestContext t, string name, float pre = 0.8f)
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
                while (!found && Time.realtimeSinceStartup - t0 < 3f) { Tick?.Invoke(); yield return 1; if (File.Exists(path)) found = true; }
            }
            foreach (var w in Wait(0.4f)) yield return w;
            t.Note((found ? "shot " : "NO shot ") + name);
        }
        public static void Freeze(TableAnim a, float at) { if (a == null) return; a.Elapsed = Mathf.Min(a.Duration, at); }
        public static void CloseAllWindows(Type keep = null)
        {
            foreach (var w in Find.WindowStack.Windows.ToList())
                if (w.GetType().Name.StartsWith("EditWindow") || w.GetType().Name.Contains("Messages")) continue;
                else Find.WindowStack.TryRemove(w, false);
        }
    }

    // ============================================================================================ casino
    public sealed class GalleryCasinoSuite : HearthTestSuite
    {
        public override string Name => "gallery.casino";
        public override ModuleId Module => ModuleId.Casino;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 900000;
        static float clock = 500f;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t);
            GalleryShot.Init();
            var comp = ThrumboComponent.Get(); comp.Clear();
            var bank = BankGameComp.Current; bank.TestReset(true);
            Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(12000), "shots"); bank.InvalidateCache();
            AnimPrefs.ForceOn = true;
            AnimClock.Override = () => clock;
            var seat = new ThrumboSeat(map, col, PayFrom.Bank);
            for (int i = 0; i < 3; i++) ThrumboHouse.PlaySlots(seat, Money.FromSilver(10), SlotVolatility.Medium);
            var win = UiTestUtil.NoPause(new Window_Thrumbo(col)); win.HowToOpen = false; Find.WindowStack.Add(win); yield return 20;

            // slots mid-spin: reels blurred
            win.Game = ThrumboGame.Slots; HSettings.Set("core.guide", 0f);
            win.TestPlay("slots", "25", animate: true);
            GalleryShot.Freeze(win.Slots.Anim, 0.95f);
            foreach (var w in GalleryShot.Take(t, "casino_slots_spinning")) yield return w;
            win.SkipAll(); yield return 3;
            // a real win: spin until one pays well, freeze when the plate shows
            ThrumboResult r = null; int tries = 0;
            for (; tries < 200; tries++)
            {
                win.SkipAll(); yield return 1;
                r = win.TestPlay("slots", "25", animate: true);
                if (r != null && r.Ok && r.Mult >= 6) break;
            }
            t.Note("win after " + tries + " tries: mult " + (r?.Mult ?? 0) + " paid " + (r?.Paid.ToString() ?? "-"));
            AnimClock.Override = null;
            GalleryShot.Freeze(win.Slots.Anim, win.Slots.Anim.WinAt + 0.55f);
            foreach (var w in GalleryShot.Wait(0.5f)) yield return w;
            foreach (var w in GalleryShot.Take(t, "casino_slots_win", 0.3f)) yield return w;
            AnimClock.Override = () => clock; win.SkipAll(); yield return 3;

            // roulette: ball in the wheel
            win.Game = ThrumboGame.Roulette; yield return 4;
            win.TestPlay("roulette", "5", animate: true);
            GalleryShot.Freeze(win.Wheel.Anim, RouletteSpinAnim.SpinSeconds * 0.7f);
            foreach (var w in GalleryShot.Take(t, "casino_roulette_ball")) yield return w;
            GalleryShot.Freeze(win.Wheel.Anim, RouletteSpinAnim.SpinSeconds * 0.45f);
            foreach (var w in GalleryShot.Take(t, "casino_roulette_ball_b")) yield return w;
            win.SkipAll(); yield return 3;

            // blackjack dealing
            win.Game = ThrumboGame.Blackjack; yield return 4;
            win.TestPlay("deal", "20", animate: true);
            var f = win.Cards.GetType().GetField("anim", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (float at in new[] { 0.45f, 0.7f, 1.0f })
            {
                GalleryShot.Freeze(f?.GetValue(win.Cards) as TableAnim, at);
                foreach (var w in GalleryShot.Take(t, "casino_blackjack_deal_" + (int)(at * 100))) yield return w;
            }
            win.SkipAll(); yield return 4;
            foreach (var w in GalleryShot.Take(t, "casino_blackjack_open")) yield return w;
            AnimClock.Override = null; AnimPrefs.ForceOn = null; comp.Clear(); bank.TestReset(true);
            win.Close(false);
            t.Check(true, "shots taken");
        }
    }

    // ============================================================================================ inn / ui / content
    public sealed class GalleryInnSuite : HearthTestSuite
    {
        public override string Name => "gallery.inn";
        public override ModuleId Module => ModuleId.Inn;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 900000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            GalleryShot.Init();
            // ---- the bed button with its menu on a double guest bed
            InnTestKit.FastTime();
            var site = InnUxKit.Build(map, ThingDef.Named("DoubleBed"), 1, desk: false);
            var bed = site.Beds[0]; yield return 10;
            GuestBeds.Comp(bed).SetGuestBed(true, false);
            Command_HH_BedUse.ClearCache();
            foreach (var c in site.A.ExpandedBy(6)) if (c.InBounds(map)) map.fogGrid.Unfog(c);
            var camPos = bed.DrawPos + new Vector3(0f, 0f, -2.5f);
            GalleryShot.Tick = () => Find.CameraDriver.SetRootPosAndSize(camPos, 8f);
            Find.Selector.ClearSelection(); Find.Selector.Select(bed, false, false);
            yield return 40;
            foreach (var w in GalleryShot.Take(t, "bed_guest_selected", 1.2f)) yield return w;
            // open the menu next to the button
            var gz = bed.GetGizmos().OfType<Command_HH_BedUse>().First();
            var fm = new FloatMenu(gz.MenuOptions()) { vanishIfMouseDistant = false };
            Find.WindowStack.Add(fm);
            yield return 5;
            fm.windowRect.x = 560f; fm.windowRect.y = UI.screenHeight - 215f - fm.windowRect.height;
            t.Note("float menu rect " + fm.windowRect + " screen " + UI.screenWidth + "x" + UI.screenHeight);
            yield return 5;
            foreach (var w in GalleryShot.Take(t, "bed_use_menu", 0.6f)) yield return w;
            fm.Close(false); yield return 3;
            Find.Selector.ClearSelection(); GalleryShot.Tick = null;
            InnUxKit.Teardown(site, map);

            // ---- storyteller page
            var hetty = DefDatabase<StorytellerDef>.GetNamedSilentFail("HH_Hetty");
            var pg = UiTestUtil.NoPause(new Page_SelectStoryteller());
            var fld = AccessTools.Field(typeof(Page_SelectStoryteller), "storyteller");
            fld?.SetValue(pg, hetty);
            UiState.ClearPendingDifficulty(); HostingLine.ClearCache(); UiState.EnsurePendingProfile().Tier = 4f;
            Find.WindowStack.Add(pg); yield return 30;
            fld?.SetValue(pg, hetty);
            foreach (var w in GalleryShot.Take(t, "storyteller_hetty", 1.0f)) yield return w;
            pg.Close(false); UiState.ClearPendingDifficulty(); yield return 3;

            // ---- scenario page
            var sp = UiTestUtil.NoPause(new Page_SelectScenario());
            Find.WindowStack.Add(sp); yield return 30;
            foreach (var f2 in typeof(Page_SelectScenario).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) t.Note("scenario page field: " + f2.FieldType.Name + " " + f2.Name);
            var scen = DefDatabase<ScenarioDef>.GetNamedSilentFail("HH_Scen_HarborTown")?.scenario;
            var cur = AccessTools.Field(typeof(Page_SelectScenario), "curScen");
            if (cur != null && scen != null) cur.SetValue(sp, scen);
            AccessTools.Field(typeof(Page_SelectScenario), "scenariosScrollPosition")?.SetValue(sp, new Vector2(0f, 5000f));
            yield return 20;
            foreach (var w in GalleryShot.Take(t, "scenarios_list", 1.0f)) yield return w;
            var scen2 = DefDatabase<ScenarioDef>.GetNamedSilentFail("HH_Scen_BackRoom")?.scenario;
            if (cur != null && scen2 != null) cur.SetValue(sp, scen2);
            yield return 20;
            foreach (var w in GalleryShot.Take(t, "scenarios_backroom", 1.0f)) yield return w;
            sp.Close(false); yield return 3;

            // ---- settings window
            var st = UiTestUtil.NoPause(new Dialog_ModSettings(HearthMod.Instance));
            Find.WindowStack.Add(st); yield return 5;
            HearthSettingsWindow.EditTemplate = false; HearthSettingsWindow.Search = "";
            foreach (var p in HearthSettingsWindow.BuildPages()) t.Note("settings page " + p.Id);
            yield return 30;
            foreach (var w in GalleryShot.Take(t, "settings_default", 1.0f)) yield return w;
            HearthSettingsWindow.SelectedPage = "mod:" + ModuleId.Casino; yield return 30;
            foreach (var w in GalleryShot.Take(t, "settings_casino", 1.0f)) yield return w;
            HearthSettingsWindow.SelectedPage = "mod:" + ModuleId.Inn; yield return 30;
            foreach (var w in GalleryShot.Take(t, "settings_inn", 1.0f)) yield return w;
            st.Close(false); yield return 3;

            // ---- Ask Ottoline (offline fallback)
            {
                var c = BankGameComp.Current; HearthProfile.Current.Tier = 3f; c.TestReset(true); Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(28170), "shots"); c.InvalidateCache();
                using (var k = new AiKit())
                {
                    AiChat.Reset(); AiChat.Quick("net"); AiChat.Quick("loans");
                    var wb = new Window_Bank(map, BankSite.Remote, null) { TabId = "ottoline" }; wb.forcePause = false; wb.absorbInputAroundWindow = false;
                    Find.WindowStack.Add(wb); yield return 40;
                    foreach (var w in GalleryShot.Take(t, "bank_ottoline", 1.0f)) yield return w;
                    wb.Close(false); yield return 3;
                }
            }

            // ---- main tab (with a few guests and a venue so the numbers are not zero)
            InnTestKit.FastTime();
            var site2 = InnUxKit.Build(map, ThingDefOf.Bed, 2, desk: true);
            foreach (var b in site2.Beds) GuestBeds.Comp(b)?.SetGuestBed(true, false);
            InnUxKit.PlaceSign(map, site2.A.CenterCell + new IntVec3(0, 0, 1), "HH_Venue_Tavern", "The Weary Muffalo");
            yield return 10;
            try { var gg = V110Kit.Arrive(map, 3, 2, 400, false); if (gg != null) V110Kit.CheckInAll(GuestRegistry.Instance.Members(gg).ToList(), site2.Desk); }
            catch (Exception e) { t.Note("arrive failed: " + e.Message); }
            Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(4210), "shots");
            var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
            Find.MainTabsRoot.SetCurrentTab(tabDef, false); yield return 5;
            foreach (var pgId in new[] { "overview", "guests" })
            {
                MainTabWindow_HH.SelectedPage = pgId; yield return 40;
                foreach (var w in GalleryShot.Take(t, "maintab_" + pgId, 1.0f)) yield return w;
            }
            Find.WindowStack.WindowOfType<MainTabWindow_HH>()?.Close(false);
            t.Check(true, "shots taken");
        }
    }

}
#endif
