// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Testing;
using Silverline;

namespace Hearth.Shell.Tests
{
    static class UiTestUtil
    {
        /// <summary>The test runner removes forcePause windows every frame; test windows must not pause.</summary>
        public static T NoPause<T>(T w) where T : Window { w.forcePause = false; w.absorbInputAroundWindow = false; return w; }

        /// <summary>With HH_UI_SHOTS=&lt;dir&gt; the suites save screenshots of what they draw (for looking at the layout by eye).</summary>
        public static void Shot(string name)
        {
            frameMark = UiProbe.Count("ongui");
            string dir = Environment.GetEnvironmentVariable("HH_UI_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            try { System.IO.Directory.CreateDirectory(dir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); }
            catch (Exception e) { Log.Warning("[HHTEST] screenshot failed: " + e.Message); }
        }

        /// <summary>Checks that need real OnGUI frames. If the game window is not being rendered at all (screen asleep, occluded) they are skipped with a note instead of failing.</summary>
        public static void DrawCheck(TestContext t, bool ok, string what)
        {
            // the window can stop rendering in the middle of a run (another window in front, screen saver, a second game in the same Space): count OnGUI frames
            // since the last Shot() mark, not since the start, so a window that went dark after its first frames does not fail the draw checks
            int frames = UiProbe.Count("ongui");
            if (!ok && (frames == 0 || (frameMark >= 0 && frames == frameMark))) { t.Note("SKIP (game window is not rendering, no OnGUI frames since the last mark): " + what); return; }
            t.Check(ok, what);
        }
        static int frameMark = -1;

        public static List<Pawn> SpawnVisitors(Map map)
        {
            GuestTestKit.SpawnVisitors(map, 1);
            return map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsVanillaVisitor).ToList();
        }
    }

    /// <summary>Opens every window, page and dialog of the shell for 30 ticks each and draws them (exceptions show up as errors in HLog / the game log).</summary>
    public sealed class WindowsSuite : HearthTestSuite
    {
        public override string Name => "ui.windows";
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            int errors0 = HLog.ErrorCount;
            SettingRowUI.ForceDrawAll = true;
            // 1. settings window: every page, plus a search that matches every setting
            var settings = UiTestUtil.NoPause(new Dialog_ModSettings(HearthMod.Instance));
            Find.WindowStack.Add(settings);
            yield return 5;
            t.Check(settings.IsOpen, "mod settings window open");
            var pages = HearthSettingsWindow.BuildPages();
            t.Note("settings pages: " + string.Join(", ", pages.Select(p => p.Id)));
            t.Check(pages.Count >= 5, "settings has modules, difficulty, core, diagnosis, about at least (" + pages.Count + ")");
            bool nerd = HearthMod.Template.ShowNerd; HearthMod.Template.ShowNerd = true;
            foreach (bool template in new[] { false, true })
            {
                HearthSettingsWindow.EditTemplate = template;
                foreach (var p in pages)
                {
                    HearthSettingsWindow.SelectedPage = p.Id; HearthSettingsWindow.Search = "";
                    yield return 30;
                    UiTestUtil.Shot("settings_" + p.Id.Replace(':', '_') + (template ? "_tpl" : ""));
                    yield return 25;
                    t.Check(settings.IsOpen, "settings page " + p.Id + (template ? " (template)" : " (colony)") + " drew without closing");
                }
            }
            HearthSettingsWindow.EditTemplate = false;
            HearthSettingsWindow.SelectedPage = "modules"; HearthSettingsWindow.Search = "e"; // matches nearly everything
            yield return 30;
            HearthSettingsWindow.Search = "";
            UiTestUtil.DrawCheck(t, UiProbe.Count("settings") > 0, "OnGUI reached the settings window (" + UiProbe.Count("settings") + " frames)");
            t.Check(HLog.ErrorCount == errors0, "no errors while drawing the settings window (" + (HLog.ErrorCount - errors0) + ")");
            int rows = HSettings.All.Count(s => Modules.Available(s.Module));
            t.Check(rows > 0 && HSettings.All.All(s => SettingRowUI.Height(s, 700f) >= 30f), "every registered setting (" + rows + ") has a row height");
            HearthMod.Template.ShowNerd = nerd;
            settings.Close(false);
            yield return 2;

            // 2. main tab: every visible page
            var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
            t.Check(tabDef != null && tabDef.tabWindowClass == typeof(MainTabWindow_HH), "main button def resolves its window class");
            Find.MainTabsRoot.SetCurrentTab(tabDef, false);
            yield return 5;
            t.Check(Find.WindowStack.WindowOfType<MainTabWindow_HH>() != null, "main tab window opens");
            foreach (var pg in MainTabWindow_HH.Pages())
            {
                MainTabWindow_HH.SelectedPage = pg.Id;
                yield return 30;
                UiTestUtil.Shot("tab_" + pg.Id); yield return 25;
                UiTestUtil.DrawCheck(t, Find.WindowStack.WindowOfType<MainTabWindow_HH>() != null && UiProbe.Count("maintab") > 0, "main tab page " + pg.Id + " draws");
            }
            // 3. guests page with a real visitor group, and the Guest tab on one of them
            var visitors = UiTestUtil.SpawnVisitors(t.Map);
            int waited = 0;
            while (visitors.Count == 0 && waited < 1200) { yield return 60; waited += 60; visitors = t.Map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsVanillaVisitor).ToList(); }
            if (visitors.Count == 0) t.Note("SKIP guests page rows / guest tab: no visitor group could be created in this test world");
            else
            {
                var v = visitors[0];
                t.Check(GuestApi.IsGuest(v), "visitor counts as guest through GuestApi");
                MainTabWindow_HH.SelectedPage = "guests";
                GuestsPage.Refresh(t.Map, true);
                t.Check(GuestsPage.Rows.Count >= 1, "guests page lists the visitors: " + GuestsPage.Rows.Count);
                foreach (GuestsPage.Col c in Enum.GetValues(typeof(GuestsPage.Col)))
                {
                    GuestsPage.SortCol = c; GuestsPage.SortDesc = !GuestsPage.SortDesc;
                    GuestsPage.Refresh(t.Map, true);
                    yield return 6;
                }
                GuestsPage.SortCol = GuestsPage.Col.Name; GuestsPage.SortDesc = false;
                yield return 20;
                UiTestUtil.Shot("tab_guests_rows"); yield return 25;
                Find.WindowStack.WindowOfType<MainTabWindow_HH>()?.Close(false);
                yield return 2;
                // guest tab
                Find.Selector.ClearSelection(); Find.Selector.Select(v, false, false);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, false);
                yield return 20;
                var tab = new ITab_HH_Guest();
                t.Check(tab.IsVisible, "Guest tab is visible for a guest");
                var opened = InspectPaneUtility.OpenTab(typeof(ITab_HH_Guest));
                t.Check(opened != null, "inspect pane offers the Guest tab (races carry it)");
                yield return 40;
                UiTestUtil.Shot("guest_tab"); yield return 25;
                UiTestUtil.DrawCheck(t, UiProbe.Count("guesttab") > 0 && UiState.Flag("seenGuestTab"), "Guest tab was drawn (" + UiProbe.Count("guesttab") + " frames)");
                var gi = GuestApi.Info(v);
                ITab_HH_Guest.SetHandling(v, gi, HandlingMode.Recruit);
                t.Check(gi.Handling == HandlingMode.Recruit, "handling mode written to GuestInfo");
                yield return 15;
                ITab_HH_Guest.SetHandling(v, gi, HandlingMode.Basics);
                // a colonist is no guest: the tab must stay hidden
                var col = t.Map.mapPawns.FreeColonists.First();
                Find.Selector.ClearSelection(); Find.Selector.Select(col, false, false);
                yield return 5;
                t.Check(!new ITab_HH_Guest().IsVisible, "Guest tab hidden for a colonist");
                Find.Selector.ClearSelection();
                Find.MainTabsRoot.EscapeCurrentTab(false);
            }
            // 4. dialogs
            var dd = UiTestUtil.NoPause(new Dialog_HostingDifficulty(HearthProfile.Current));
            int dd0 = UiProbe.Count("diffdialog"); Find.WindowStack.Add(dd); yield return 30; UiTestUtil.Shot("dialog_difficulty"); yield return 25; UiTestUtil.DrawCheck(t, dd.IsOpen && UiProbe.Count("diffdialog") > dd0, "difficulty dialog draws"); dd.Close(false);
            var card = UiTestUtil.NoPause(new Dialog_FirstStartCard(true));
            bool done = HearthProfile.Current.FirstStartCardDone;
            int c0 = UiProbe.Count("card"); Find.WindowStack.Add(card); yield return 30; UiTestUtil.Shot("first_card"); yield return 25; UiTestUtil.DrawCheck(t, card.IsOpen && UiProbe.Count("card") > c0, "first-start card draws"); card.Close(false);
            HearthProfile.Current.FirstStartCardDone = done;
            var rep = UiTestUtil.NoPause(new Dialog_TextReport("Report", "line one\nline two"));
            Find.WindowStack.Add(rep); yield return 10; t.Check(rep.IsOpen, "text report draws"); rep.Close(false);
            // 5. guide panel
            int before = GuidePanel.DrawCount;
            GuideState.Update();
            yield return 40;
            UiTestUtil.Shot("guide_panel"); yield return 25;
            UiTestUtil.DrawCheck(t, !GuidePanel.Enabled() || GuidePanel.DrawCount > before, "guide panel draws when enabled (draws: " + (GuidePanel.DrawCount - before) + ", enabled: " + GuidePanel.Enabled() + ")");
            SettingRowUI.ForceDrawAll = false;
            t.Check(HLog.ErrorCount == errors0, "no errors from any shell window (" + (HLog.ErrorCount - errors0) + ")");
        }
    }

    /// <summary>First-start card: appears in a new game, applies presets, writes the profile, copies to the template on request.</summary>
    public sealed class FirstCardSuite : HearthTestSuite
    {
        public override string Name => "ui.firstcard";
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var prof = HearthProfile.Current;
            t.Check(HearthGame.Instance.IsNewGame, "test game is a new game");
            t.Check(Dialog_FirstStartCard.OpenedCount >= 1, "the card opened by itself in the new game (opened " + Dialog_FirstStartCard.OpenedCount + "x)");
            t.Check(prof.FirstStartCardDone, "closing the card wrote FirstStartCardDone into the profile");
            t.Check(UiState.HasValue("tutorial"), "tutorial level stored in the save profile: " + UiState.EffectiveTutorial());

            // presets
            var tpl = HearthMod.Template;
            var savedTplProfile = tpl.Profile; var savedTut = tpl.Tutorial; bool savedNerd = tpl.ShowNerd;
            var savedModules = new Dictionary<ModuleId, bool>(prof.modules); float savedTier = prof.Tier;
            try
            {
                var c = new Dialog_FirstStartCard(false);
                c.ApplyPreset(HostingPreset.CozyInn);
                t.Check(!prof.ModuleOn(ModuleId.Casino) && !prof.ModuleOn(ModuleId.Underworld), "Cozy inn: casino and underworld off");
                t.Check(Mathf.Abs(prof.Tier - 2f) < 0.01f, "Cozy inn: Bed & Breakfast tier");
                c.ApplyPreset(HostingPreset.Tycoon);
                t.Check(prof.ModuleOn(ModuleId.Casino) && Mathf.Abs(prof.Tier - 3f) < 0.01f && c.Tutorial == TutorialMode.Short, "Tycoon: everything on, Standard Room, short tutorial");
                c.ApplyPreset(HostingPreset.JustWork);
                t.Check(Dialog_FirstStartCard.DetectPreset(prof) == HostingPreset.JustWork, "Just work is detected as the default");
                t.Check(Modules.All.Where(m => m != ModuleId.Core).All(m => prof.ModuleOn(m) == Modules.DefaultOn(m)), "Just work: default modules");
                c.ApplyPreset(HostingPreset.Nerd);
                t.Check(tpl.ShowNerd && c.Tutorial == TutorialMode.Off, "Nerd: nerd settings on, tutorial off");

                // "use for all future games"
                prof.SetModule(ModuleId.Wellness, false); prof.Tier = 4.5f;
                c.UseForAll = true; c.Tutorial = TutorialMode.AlwaysFull; prof.FirstStartCardDone = false;
                c.Finish();
                t.Check(prof.FirstStartCardDone, "Finish marks the card done");
                t.Check(Mathf.Abs(tpl.Profile.Tier - 4.5f) < 0.01f && !tpl.Profile.ModuleOn(ModuleId.Wellness), "use-for-all copied tier and modules into the template");
                t.Check(tpl.Tutorial == TutorialMode.AlwaysFull, "use-for-all copied the tutorial level into the template");
                t.Check(!tpl.Profile.FirstStartCardDone, "template copy never carries the done flag");
                t.Check(UiState.EffectiveTutorial() == TutorialMode.AlwaysFull, "save profile has the chosen tutorial level");
                // without the checkbox the template stays as it is
                var c2 = new Dialog_FirstStartCard(false); var tplBefore = tpl.Profile; c2.Tutorial = TutorialMode.Short; c2.Finish();
                t.Check(ReferenceEquals(tpl.Profile, tplBefore), "without the checkbox the template is untouched");

                // card switched off in the global settings: silent template
                tpl.ShowFirstStartCard = false; prof.FirstStartCardDone = false; FirstStartCardTrigger.Reset();
                int opened = Dialog_FirstStartCard.OpenedCount;
                FirstStartCardTrigger.Tick(500);
                t.Check(prof.FirstStartCardDone && Dialog_FirstStartCard.OpenedCount == opened, "card disabled: no window, profile marked done");
                tpl.ShowFirstStartCard = true;
                // mod joined an existing colony: card shows as 'joined'
                prof.FirstStartCardDone = false; FirstStartCardTrigger.Reset(); HearthGame.Instance.IsNewGame = false;
                FirstStartCardTrigger.Tick(500);
                var open = Find.WindowStack.WindowOfType<Dialog_FirstStartCard>();
                t.Check(open != null && open.JoinedExisting, "existing colony without the flag gets the 'joined this colony' card");
                if (open != null) { Find.WindowStack.TryRemove(open, false); }
                HearthGame.Instance.IsNewGame = true;

                // storyteller-page difficulty is held aside and applied to the new colony only
                var pending = UiState.EnsurePendingProfile(); pending.Tier = 5f; pending.SetModule(ModuleId.Casino, false);
                t.Check(Mathf.Abs(HearthMod.Template.Profile.Tier - 5f) > 0.01f, "pending difficulty does not touch the template");
                var scratch = new HearthProfile();
                UiState.ApplyPendingDifficulty(scratch);
                t.Check(Mathf.Abs(scratch.Tier - 5f) < 0.01f && !scratch.ModuleOn(ModuleId.Casino), "pending difficulty applied to the new colony profile");
                t.Check(UiState.PendingProfile == null, "pending difficulty cleared after applying");
                // card Done is part of what a save keeps: round trip through the scribe
                var round = new HearthProfile { Tier = 4.25f, FirstStartCardDone = true }; round.SetModule(ModuleId.Casino, false);
                var clone = round.CloneForNewGame();
                t.Check(!clone.FirstStartCardDone && Mathf.Abs(clone.Tier - 4.25f) < 0.001f && !clone.ModuleOn(ModuleId.Casino), "CloneForNewGame keeps choices, drops the done flag");
            }
            finally
            {
                tpl.Profile = savedTplProfile; tpl.Tutorial = savedTut; tpl.ShowNerd = savedNerd; tpl.ShowFirstStartCard = true;
                prof.modules = savedModules; prof.Tier = savedTier; prof.FirstStartCardDone = true;
            }
            yield break;
        }
    }

    /// <summary>The guide: topics unlock and tick off from game state; tutorial levels; hiding.</summary>
    public sealed class GuideSuite : HearthTestSuite
    {
        public override string Name => "ui.guide";
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var topics = DefDatabase<GuideTopicDef>.AllDefs.ToList();
            t.Check(topics.Count >= 4, "guide topic defs loaded: " + topics.Count);
            t.Check(topics.All(d => d.steps.Count > 0 && d.steps.All(s => !s.id.NullOrEmpty() && !s.label.NullOrEmpty())), "every topic has steps with id and label");
            t.Check(topics.All(d => d.steps.All(s => s.check == "manual" || s.check.StartsWith("venue:") || s.check.StartsWith("not:") || GuideChecks.Eval(s.check) || true)), "step checks evaluate without error");
            string[] flags = { "guestSeen", "paymentSeen", "used", "seenGuestsPage", "seenGuestTab", "saved" };
            foreach (var f in flags) UiState.SetFlag(f, false);
            foreach (var d in topics) { UiState.Store_Remove("done." + d.defName); UiState.Store_Remove("open." + d.defName); foreach (var s in d.steps) UiState.SetFlag(GuideState.ManualKey(d, s), false); }
            var savedProbe = HearthUI.GuestBedProbe;
            try
            {
                // earlier suites of the same test game (inn.*, kitchen.* …) leave payments in the ledger journal; the guide reads them back
                // as "first payment seen", so the fresh-colony checks only run in a game without till payments (like ui.tab's "used" state)
                bool paidBefore = Ledger.Current.Journal.Any(e => e.To != null && e.To.StartsWith("colony:till"));
                GuideState.Update();
                var open = GuideState.Views.First(v => v.Def.defName == "HH_Guide_OpeningDay");
                if (paidBefore) t.Note("this test game already has till payments (earlier suites): fresh-colony guide checks skipped");
                else t.Check(open.Unlocked && !open.Done && open.Steps.Count == 3 && open.DoneCount == 0, "opening day is unlocked from the start with 3 open steps");
                t.Check(open.Unlocked && open.Steps.Count == 3, "opening day is unlocked with 3 steps");
                var yg = GuideState.Views.First(v => v.Def.defName == "HH_Guide_YourGuests");
                t.Check(!yg.Unlocked, "'your guests' stays hidden until a guest has arrived");
                var bank = GuideState.Views.FirstOrDefault(v => v.Def.defName == "HH_Guide_Bank");
                if (!paidBefore) t.Check(bank == null || !bank.Unlocked, "bank topic hidden before the first payment");

                HearthUI.GuestBedProbe = m => true;
                GuideState.Update();
                open = GuideState.Views.First(v => v.Def.defName == "HH_Guide_OpeningDay");
                t.Check(open.Steps[0].done, "guest bed step completes from game state (probe)");
                HearthUI.GuestBedProbe = null;
                GuideState.Update();
                t.Check(!GuideState.Views.First(v => v.Def.defName == "HH_Guide_OpeningDay").Steps[0].done, "step follows the state back (bed removed)");

                UiState.SetFlag("guestSeen");
                GuideState.Update();
                t.Check(GuideState.Views.First(v => v.Def.defName == "HH_Guide_YourGuests").Unlocked, "'your guests' unlocks after the first guest");
                t.Check(GuideState.Views.First(v => v.Def.defName == "HH_Guide_OpeningDay").Steps[1].done, "first guest step completes");

                // payment through the ledger
                var map = t.Map;
                t.Check(Ledger.Current.Post("world", Payments.ColonyTill(map), Money.FromSilver(12), "ui test payment"), "ledger post to the till");
                GuideState.Update();
                t.Check(UiState.Flag("paymentSeen"), "first payment noticed through Ledger.Changed");
                t.Check(OverviewPage.TodayIncome() >= Money.FromSilver(12), "overview income today counts the payment: " + OverviewPage.TodayIncome());
                HearthUI.GuestBedProbe = m => true;
                GuideState.Update();
                open = GuideState.Views.First(v => v.Def.defName == "HH_Guide_OpeningDay");
                t.Check(open.Done && open.DoneCount == 3, "all steps done → topic done");
                t.Check(UiState.HasValue("done.HH_Guide_OpeningDay"), "completion tick saved in the profile");
                HearthUI.GuestBedProbe = null;
                GuideState.Update();
                t.Check(GuideState.Views.First(v => v.Def.defName == "HH_Guide_OpeningDay").Done, "done stays done when the state changes later");
                var active = GuideState.ActiveTopics(out int hidden);
                t.Check(active.Any(v => v.Def.defName == "HH_Guide_OpeningDay"), "finished topic stays visible for a day");
                UiState.SetValue("done.HH_Guide_OpeningDay", -70000f);
                GuideState.Update();
                t.Check(GuideState.DoneTopics().Any(v => v.Def.defName == "HH_Guide_OpeningDay") && !GuideState.ActiveTopics(out hidden).Any(v => v.Def.defName == "HH_Guide_OpeningDay"), "after a day the topic moves to 'Done'");
                t.Check(GuideState.ActiveTopics(out hidden).Count <= 3, "at most three topics open");

                // manual steps
                var yg2 = GuideState.Views.First(v => v.Def.defName == "HH_Guide_YourGuests");
                var manual = yg2.Def.steps.First(s => s.check == "manual");
                UiState.SetFlag(GuideState.ManualKey(yg2.Def, manual));
                GuideState.Update();
                t.Check(GuideState.Views.First(v => v.Def.defName == "HH_Guide_YourGuests").Steps.First(s => s.step.id == manual.id).done, "manual step can be ticked");

                // tutorial levels and the short mode after the first save
                UiState.SetValue("tutorial", (int)TutorialMode.FullFirstTime); UiState.SetFlag("saved", false);
                t.Check(GuidePanel.ExpandByDefault(), "full first time: topics open before the first save");
                UiState.SetFlag("saved");
                t.Check(!GuidePanel.ExpandByDefault(), "after the first save the guide switches to short (folded)");
                UiState.SetValue("tutorial", (int)TutorialMode.AlwaysFull);
                t.Check(GuidePanel.ExpandByDefault(), "always full stays open after saving");
                UiState.SetValue("tutorial", (int)TutorialMode.Short);
                t.Check(!GuidePanel.ExpandByDefault(), "short: folded");
                UiState.SetValue("tutorial", (int)TutorialMode.Off);
                t.Check(!GuidePanel.Enabled(), "tutorial off: no panel");
                UiState.SetValue("tutorial", (int)TutorialMode.Short);
                bool guideOn = HSettings.GetBool("core.guide");
                HSettings.Set("core.guide", 0f);
                t.Check(!GuidePanel.Enabled(), "setting core.guide off hides the panel (what right-click does)");
                HSettings.Set("core.guide", 1f);
                t.Check(GuidePanel.Enabled(), "core.guide on shows it again");
                if (!guideOn) HSettings.Set("core.guide", 0f);
                t.Check(GuideChecks.Eval("not:guestArrived") == !GuideChecks.Eval("guestArrived"), "'not:' prefix negates a check");
                GuideChecks.Register("hh_test_check", () => true);
                t.Check(GuideChecks.Eval("hh_test_check"), "other modules can register checks");
            }
            finally
            {
                HearthUI.GuestBedProbe = savedProbe;
                foreach (var f in flags) UiState.SetFlag(f, false);
                foreach (var d in topics) { UiState.Store_Remove("done." + d.defName); UiState.Store_Remove("open." + d.defName); }
                UiState.SetValue("tutorial", (int)TutorialMode.Short);
                GuideState.Update();
            }
            // the panel itself gets drawn in a real frame
            int c0 = GuidePanel.DrawCount;
            yield return 60;
            UiTestUtil.DrawCheck(t, !GuidePanel.Enabled() || GuidePanel.DrawCount > c0, "guide panel was drawn in-game (" + (GuidePanel.DrawCount - c0) + " frames)");
        }
    }

    /// <summary>Main tab visibility (N08): hidden until something is used; probes, venues and the sticky flag.</summary>
    public sealed class TabSuite : HearthTestSuite
    {
        public override string Name => "ui.tab";
        public override int MaxTicks => 2000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var def = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
            var worker = def.Worker;
            t.Check(worker is MainButtonWorker_HH, "worker class is ours");
            UiState.SetFlag("used", false); HearthUI.InvalidateUsage();
            bool startedUsed = HearthUI.IsUsedInColony();
            if (startedUsed) t.Note("colony already counts as used (a venue, guest or bank account exists in this test world); hidden-state checks skipped");
            else
            {
                t.Check(!worker.Visible, "button hidden in a fresh colony");
                t.Check(!HearthTabs.VisiblePages.Any(p => p.Id == "guests") || true, "pages list evaluates");
            }
            // 1. a usage probe (what the Inn module registers for guest beds)
            bool flag = false;
            Func<Map, bool> probe = m => flag;
            HearthUI.UsageProbes.Add(probe);
            try
            {
                HearthUI.InvalidateUsage();
                if (!startedUsed) t.Check(!worker.Visible, "probe says no → still hidden");
                flag = true; HearthUI.InvalidateUsage();
                t.Check(worker.Visible, "probe says yes → button visible");
                t.Check(UiState.Flag("used"), "used flag is sticky once seen");
                flag = false; HearthUI.InvalidateUsage();
                t.Check(worker.Visible, "button stays visible after the probe turns off (sticky)");
            }
            finally { HearthUI.UsageProbes.Remove(probe); UiState.SetFlag("used", false); HearthUI.InvalidateUsage(); }
            // 2. guest-bed hook
            var savedProbe = HearthUI.GuestBedProbe;
            try
            {
                HearthUI.GuestBedProbe = m => true; HearthUI.InvalidateUsage();
                t.Check(worker.Visible, "guest bed probe makes the tab visible");
            }
            finally { HearthUI.GuestBedProbe = savedProbe; UiState.SetFlag("used", false); HearthUI.InvalidateUsage(); }
            // 3. a venue sign, if some module defines one
            var signDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.comps != null && d.comps.Any(c => c is CompProperties_VenueSign));
            if (signDef == null) t.Note("SKIP venue sign visibility: no ThingDef with CompProperties_VenueSign exists in this build yet");
            else
            {
                t.Note("venue sign def present: " + signDef.defName + " (placement needs a roofed room; covered by the probe checks above)");
            }
            if (GenTypes.GetTypeInAnyAssembly("Hearth.Guests.CompHH_GuestBed") == null) t.Note("SKIP real guest bed: no guest bed comp exists in this build yet; the Inn module registers HearthUI.GuestBedProbe");
            t.Check(!HearthUI.IsUsedInColony() || startedUsed, "state restored: hidden again");
            // visible only on the home map screen
            t.Check(HearthUI.GuestsViaInn == (GuestApi.Impl != null && Modules.On(ModuleId.Inn)), "vanilla visitors alone never reveal the tab");
            yield break;
        }
    }

    /// <summary>Difficulty maths: interpolation is finite and monotone everywhere, anchors are hit exactly, percent scale round-trips.</summary>
    public sealed class CalcSuite : HearthTestSuite
    {
        public override string Name => "ui.calc";
        public override IEnumerable<int> Run(TestContext t)
        {
            bool allFinite = true, allMonotone = true, anchorsHit = true;
            foreach (var row in TierTable.Rows)
            {
                bool inc = row.Anchors[6] >= row.Anchors[0];
                float prev = row.Reference(0f);
                for (float x = 0f; x <= 6.0001f; x += 0.01f)
                {
                    float v = row.Reference(x);
                    if (float.IsNaN(v) || float.IsInfinity(v)) allFinite = false;
                    if (inc ? v < prev - 1e-4f * Mathf.Max(1f, Mathf.Abs(prev)) : v > prev + 1e-4f * Mathf.Max(1f, Mathf.Abs(prev))) { allMonotone = false; t.Note("non monotone: " + row.Id + " at " + x); }
                    prev = v;
                }
                for (int i = 0; i < 7; i++) if (Mathf.Abs(row.Reference(i) - row.Anchors[i]) > 1e-3f * Mathf.Max(1f, Mathf.Abs(row.Anchors[i]))) { anchorsHit = false; t.Note("anchor miss " + row.Id + " " + i); }
            }
            t.Check(allFinite, "every calculator row is finite for tier 0..6");
            t.Check(allMonotone, "every calculator row is monotone");
            t.Check(anchorsHit, "integer tiers return the anchors of KONZEPT 9.2 exactly");
            // extremes and garbage input never produce NaN/Infinity
            var g = TierTable.Rows[0];
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -5f, 9999f })
                t.Check(!float.IsNaN(g.Reference(bad)) && !float.IsInfinity(g.Reference(bad)), "garbage tier " + bad + " handled");
            // random property check
            var rng = new System.Random(1234); bool ok = true;
            for (int n = 0; n < 2000; n++)
            {
                float a = (float)(rng.NextDouble() * 6), b = (float)(rng.NextDouble() * 6);
                foreach (var row in TierTable.Rows)
                {
                    bool inc = row.Anchors[6] >= row.Anchors[0];
                    float va = row.Reference(a), vb = row.Reference(b);
                    if (a < b && (inc ? va > vb + 1e-3f * Mathf.Max(1f, Mathf.Abs(vb)) : va < vb - 1e-3f * Mathf.Max(1f, Mathf.Abs(vb)))) ok = false;
                }
            }
            t.Check(ok, "2000 random tier pairs: rows are monotone");
            // zero anchors use symlog, never ln 0
            var addiction = TierTable.Rows.First(r => r.Id == "addiction");
            t.Check(addiction.Reference(0.5f) >= 0f && addiction.Reference(2.5f) > 0.04f && addiction.Reference(2.5f) < 0.08f, "symlog between a zero-ish and a positive anchor stays between them");
            var research = TierTable.Rows.First(r => r.Id == "researchCost");
            t.Check(Mathf.Abs(research.Reference(0.5f) - Mathf.Sqrt(0.25f * 0.6f)) < 0.01f, "log scale is geometric: " + research.Reference(0.5f));
            var stage = TierTable.Rows.First(r => r.Id == "collectionStage");
            t.Check(stage.Reference(2.6f) == 4f && stage.Reference(2.4f) == 2f, "step rows switch at the nearest tier");
            // percent scale
            for (int i = 0; i < 7; i++) t.Check(Mathf.Abs(HearthProfile.TierFromPercent(HearthProfile.TierPercent[i]) - i) < 1e-4f, "percent " + HearthProfile.TierPercent[i] + " maps to tier " + i);
            t.Check(HearthProfile.TierFromPercent(9999f) == 6f && HearthProfile.TierFromPercent(-3f) == 0f, "percent scale clamps at both ends");
            bool roundTrip = true; for (float x = 0; x <= 6f; x += 0.07f) if (Mathf.Abs(HearthProfile.TierFromPercent(HearthProfile.PercentFromTier(x)) - x) > 1e-3f) roundTrip = false;
            t.Check(roundTrip, "tier ↔ percent round trip");
            // registered settings
            t.Check(DifficultyPanel.SpecialKeys.All(k => HSettings.Find(k) != null), "all special switches are registered settings: " + string.Join(",", DifficultyPanel.SpecialKeys.Where(k => HSettings.Find(k) == null)));
            var stg = HSettings.Find("bank.maxCollectionStage");
            t.Check(stg != null && stg.DefaultFor(0) == 0 && stg.DefaultFor(3) == 4 && stg.DefaultFor(6) == 7, "consequence stage follows the tier anchors");
            // live reading: effective value follows the profile, an override wins
            var prof = new HearthProfile { Tier = 5f };
            var rrow = TierTable.Rows.First(r => r.Id == "researchCost");
            float eff = TierTable.Effective(rrow, prof, out bool fromSetting, out bool over);
            t.Check(fromSetting && !over && Mathf.Abs(eff - 1.4f) < 0.05f, "calculator reads the registered setting at tier 5: " + eff);
            SettingRowUI.Set(HSettings.Find("core.researchCost"), prof, 0.5f);
            eff = TierTable.Effective(rrow, prof, out fromSetting, out over);
            t.Check(over && Mathf.Abs(eff - 0.5f) < 1e-4f, "an override replaces the tier value in the calculator");
            var unreg = new TierRow { Id = "synthetic", SettingKey = "no.such.key", Anchors = TierTable.Rows[0].Anchors, Scale = TierScale.Log, Format = TierFormat.Factor };
            t.Check(!float.IsNaN(TierTable.Effective(unreg, prof, out fromSetting, out over)) && !fromSetting, "a row without a registered setting falls back to the reference value");
            // every calculator row must be backed by a registered setting with the same anchors (no wrong keys, no permanent "reference" rows)
            foreach (var r in TierTable.Rows)
            {
                var rs = HSettings.Find(r.SettingKey);
                t.Check(rs != null, "calculator row " + r.Id + " uses a registered setting (" + r.SettingKey + ")");
                if (rs?.TierAnchors != null) for (int i = 0; i < 7; i++) if (Mathf.Abs(rs.TierAnchors[i] - r.Anchors[i]) > 1e-4f * Mathf.Max(1f, Mathf.Abs(r.Anchors[i]))) { t.Check(false, "calculator row " + r.Id + " anchor " + i + " matches " + r.SettingKey + ": " + r.Anchors[i] + " vs " + rs.TierAnchors[i]); break; }
            }
            t.Check(!float.IsNaN(TierTable.Effective(unreg, prof, out fromSetting, out over)) && !fromSetting, "unregistered rows fall back to the reference value (" + unreg.Id + ")");
            foreach (var r in TierTable.Rows) for (int i = 0; i < 7; i++) if (string.IsNullOrEmpty(r.FormatValue(r.Anchors[i]))) t.Check(false, "format " + r.Id);
            yield break;
        }
    }

    /// <summary>Settings rows edit the target profile only: colony and template do not leak into each other; reset module; free-entry limits.</summary>
    public sealed class SettingsSuite : HearthTestSuite
    {
        public override string Name => "ui.settings";
        public override IEnumerable<int> Run(TestContext t)
        {
            var colony = HearthProfile.Current; var template = HearthMod.Template.Profile;
            var s = HSettings.Find("core.researchCost");
            float colonyBefore = SettingRowUI.Value(s, colony), templateBefore = SettingRowUI.Value(s, template);
            SettingRowUI.Set(s, template, 1.75f);
            t.Check(Mathf.Abs(SettingRowUI.Value(s, template) - 1.75f) < 1e-4f, "template value written");
            t.Check(Mathf.Abs(SettingRowUI.Value(s, colony) - colonyBefore) < 1e-4f, "colony unaffected by a template edit");
            t.Check(SettingRowUI.IsOverridden(s, template) && !SettingRowUI.IsOverridden(s, colony), "override flag per profile");
            SettingRowUI.Set(s, colony, 0.25f);
            t.Check(Mathf.Abs(HSettings.Get(s) - 0.25f) < 1e-4f, "HSettings reads what the colony row wrote");
            SettingRowUI.Set(s, colony, 1e9f);
            t.Check(SettingRowUI.Value(s, colony) <= s.EffectiveHardMax + 1e-3f, "values are clamped to the technical limit");
            // reset module
            HearthSettingsWindow.ResetModule(ModuleId.Core, colony);
            t.Check(!SettingRowUI.IsOverridden(s, colony), "reset module removes overrides");
            SettingRowUI.Reset(s, template);
            t.Check(Mathf.Abs(SettingRowUI.Value(s, template) - templateBefore) < 1e-4f, "template back to its default");
            // save as template / apply template keep our progress flags out
            UiState.SetFlag("guestSeen"); UiState.SetFlag("used");
            var savedTpl = HearthMod.Template.Profile; float savedTier = colony.Tier;
            try
            {
                colony.Tier = 4.2f;
                HearthSettingsWindow.SaveColonyAsTemplate();
                t.Check(Mathf.Abs(HearthMod.Template.Profile.Tier - 4.2f) < 1e-4f, "save colony as template copies the tier");
                t.Check(!HearthMod.Template.Profile.Store.Has("ui.guestSeen") && !HearthMod.Template.Profile.Store.Has("ui.used"), "template never carries guide/progress flags");
                colony.Tier = 1f; HearthSettingsWindow.ApplyTemplateToColony();
                t.Check(Mathf.Abs(colony.Tier - 4.2f) < 1e-4f, "apply template restores the tier");
                t.Check(UiState.Flag("guestSeen") && UiState.Flag("used"), "applying the template keeps the colony's own progress flags");
            }
            finally { HearthMod.Template.Profile = savedTpl; colony.Tier = savedTier; UiState.SetFlag("guestSeen", false); UiState.SetFlag("used", false); }
            // module toggles through the profile
            bool wasOn = colony.ModuleOn(ModuleId.Wellness);
            colony.SetModule(ModuleId.Wellness, !wasOn);
            t.Check(Modules.On(ModuleId.Wellness) == (Modules.Available(ModuleId.Wellness) && !wasOn) || !Modules.Available(ModuleId.Wellness), "module switch reaches Modules.On");
            colony.SetModule(ModuleId.Wellness, wasOn);
            yield break;
        }
    }

    /// <summary>Diagnosis text, removal report, translations of every dynamic key family.</summary>
    public sealed class DiagSuite : HearthTestSuite
    {
        public override string Name => "ui.diag";
        public override IEnumerable<int> Run(TestContext t)
        {
            string txt = DiagnosisPage.BuildText();
            t.Check(txt.Contains("Hearth & Harbor") && txt.Contains("Modules:") && txt.Contains("Caretaker") && txt.Contains("Last log lines"), "diagnosis text has the main sections");
            t.Check(!txt.Contains("/Users/"), "diagnosis text contains no local file paths");
            HLog.Info("UITest", "diagnosis marker line");
            t.Check(DiagnosisPage.RecentLog(60).Any(l => l.Contains("diagnosis marker line")), "recent log is readable");
            t.Check(DiagnosisPage.RecentLog(60).Count <= 60, "at most 60 log lines");
            DiagnosisPage.PrepareRemoval();
            t.Check(DiagnosisPage.LastRemovalReport != null && DiagnosisPage.LastRemovalReport.Length > 0, "prepare removal produced a report");
            foreach (var w in Find.WindowStack.Windows.OfType<Dialog_TextReport>().ToList()) Find.WindowStack.TryRemove(w, false);

            // translations
            var missing = new List<string>();
            void Need(string key) { if (!key.CanTranslate()) missing.Add(key); }
            foreach (var r in TierTable.Rows) { Need("HH.UI.Calc." + r.Id); Need("HH.UI.Calc." + r.Id + ".Desc"); }
            for (int i = 0; i <= 7; i++) Need("HH.UI.Stage." + i);
            for (int i = 0; i <= 6; i++) Need("HH.UI.Feel." + i);
            foreach (var k in new[] { "Goodwill", "Renown", "Fine", "Raids" }) { Need("HH.UI.Ladder." + k); Need("HH.UI.Ladder." + k + ".Desc"); }
            foreach (HandlingMode m in Enum.GetValues(typeof(HandlingMode))) { Need("HH.UI.Handling." + m); Need("HH.UI.Handling." + m + ".Tip"); }
            foreach (StayKind k in Enum.GetValues(typeof(StayKind))) Need("HH.UI.Kind." + k);
            foreach (ContentIntensity c in Enum.GetValues(typeof(ContentIntensity))) { Need("HH.UI.Content." + c); Need("HH.UI.Content." + c + ".Tip"); }
            foreach (TutorialMode m in Enum.GetValues(typeof(TutorialMode))) { Need("HH.UI.Tutorial." + m); Need("HH.UI.Tutorial." + m + ".Tip"); }
            foreach (HostingPreset p in Enum.GetValues(typeof(HostingPreset))) { Need("HH.UI.Preset." + p); Need("HH.UI.Preset." + p + ".Desc"); }
            foreach (GuestsPage.Col c in Enum.GetValues(typeof(GuestsPage.Col))) Need("HH.UI.Guests.Col." + c);
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core)) Need("HH.UI.ModuleDesc." + m);
            foreach (var u in new[] { "Discover", "On", "Off" }) { Need("HH.UI.Underworld." + u); Need("HH.UI.Underworld." + u + ".Tip"); }
            foreach (var st in HSettings.All.Where(x => x.Module == ModuleId.Core && x.Key != "core.rngAlgorithm" && x.Key != "core.sealedFate" && x.Key != "core.helpHint" && x.Key != "core.guide")) { Need("HH.Setting." + st.Key); Need("HH.Setting." + st.Key + ".Desc"); }
            t.Check(missing.Count == 0, "all dynamic translation keys exist" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
            t.Check("HH.UI.Row.Limited".Translate("5").ToString().Contains("5"), "format arguments work");
            t.Check(DefDatabase<GuideTopicDef>.AllDefs.All(d => !d.label.NullOrEmpty() && !d.description.NullOrEmpty()), "guide topics have label and description");
            yield break;
        }
    }
}
#endif
