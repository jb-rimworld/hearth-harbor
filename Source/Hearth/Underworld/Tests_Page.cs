// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Testing;
using Hearth.Shell;

namespace Hearth.Underworld
{
    /// <summary>The Underworld tab (package D): rows for not built / waiting / working, the offer row with Accept and Decline, the Build button
    /// (selects the designator, closes the tab), the pit row's buttons, the cache, Cozy, and drawing at 1280x720 without errors.</summary>
    public sealed class UwPageSuite : HearthTestSuite
    {
        public override string Name => "underworld.page";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 12000;

        static UwPageRow Row(Map map, string id) => UnderworldPage.Row(map, id, refresh: true);

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var c = Underworld.Comp;
            int errors0 = HLog.ErrorCount;
            UwKit.Begin(ContentIntensity.Standard);
            FacKit.CloseOffers();
            Find.DesignatorManager.Deselect();
            FacilityStatus.MarkDirty();

            // ---- the page is there whenever the underworld is visible
            var page = HearthTabs.VisiblePages.FirstOrDefault(p => p.Id == "underworld");
            t.Check(page != null, "Standard: the Underworld page is in the main tab");

            // ---- not built: every allowed facility has a row in the page order, clinic only from Dark on
            var rows = UnderworldPage.Rows(map, refresh: true);
            string ids = string.Join(",", rows.Select(r => r.Id));
            t.Check(ids == "backroom,pit,fence,lounge,dock,gangs,hunt", "Standard rows in order: " + ids);
            t.Check(rows.All(r => r.State == FacilityState.NotBuilt), "fresh map: every row is grey (not built): " + string.Join(", ", rows.Where(r => r.State != FacilityState.NotBuilt).Select(r => r.Id + "=" + r.State)));
            t.Check(rows.All(r => !r.Name.NullOrEmpty() && !r.Line.NullOrEmpty() && !r.Name.StartsWith("HH.") && !r.Line.StartsWith("HH.")), "every row has a translated name and sentence");
            var pitRow = rows.First(r => r.Id == UnderworldPage.PitId);
            t.Check(pitRow.Line == UwPay.T("Page.Pit.NotBuilt") && pitRow.BuildDef == UwDefs.FightPit && pitRow.Pit == null, "pit row: " + pitRow.Line);
            t.Check(rows.First(r => r.Id == FacilityStatus.GangsId).BuildDef == null, "gangs: no Build button (not a building)");
            t.Check(rows.First(r => r.Id == FacilityStatus.LoungeId).BuildDef?.defName == "HH_VenueSign" || !Modules.On(ModuleId.ShopsFood), "lounge: Build picks the venue sign");
            foreach (var d in new[] { UwDefs.BackRoomDoor, UwDefs.FightPit, UwDefs.FenceCounter, UwDefs.SmugglerDock })
                t.Check(UwBuild.DesignatorFor(d, out string w) != null, d.defName + " can be built from the page without research (" + w + ")");
            if (!UwDefs.ResHunt.IsFinished)
            {
                t.Check(UwBuild.DesignatorFor(UwDefs.HuntingGround, out string why) == null && why == UwPay.T("Page.Build.Research", UwDefs.ResHunt.LabelCap.ToString()), "hunting ground: Build is disabled with the research it needs (" + why + ")");
            }
            else t.Note("the hunt is researched in this game: research reason not checked");
            var huntRow = rows.First(r => r.Id == FacilityStatus.HuntId);
            t.Check(huntRow.Line == (UwDefs.ResHunt.IsFinished ? UwPay.T("Hunt.Status.NotBuiltResearched", Hunts.MinCells, Hunts.MaxCells) : UwPay.T("Hunt.Status.NotBuilt", Hunts.MinCells)), "hunt row asks for research only while it is not done: " + huntRow.Line);
            t.Check(ReferenceEquals(UnderworldPage.Rows(map), UnderworldPage.Rows(map)), "rows are cached between frames");

            HearthProfile.Current.Content = ContentIntensity.Dark;
            t.Check(UnderworldPage.Rows(map, refresh: true).Any(r => r.Id == FacilityStatus.ClinicId), "Dark: the clinic row appears at once (no dirty flag, no ticks)");
            HearthProfile.Current.Content = ContentIntensity.Cozy; FacilityStatus.MarkDirty();
            t.Check(UnderworldPage.Rows(map, refresh: true).Count == 0 && !HearthTabs.VisiblePages.Any(p => p.Id == "underworld"), "Cozy: no rows and no page");
            HearthProfile.Current.Content = ContentIntensity.Standard; FacilityStatus.MarkDirty();

            // ---- Build button: selects the designator and closes the main tab
            var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
            MainTabWindow_HH.SelectedPage = "underworld";
            Find.MainTabsRoot.SetCurrentTab(tabDef, false);
            yield return 5;
            t.Check(Find.MainTabsRoot.OpenTab == tabDef, "main tab open on the Underworld page");
            bool built = UnderworldPage.StartBuilding(UwDefs.FenceCounter);
            yield return 2;
            var sel = Find.DesignatorManager.SelectedDesignator as Designator_Build;
            t.Check(built && sel != null && sel.PlacingDef == UwDefs.FenceCounter, "Build selects the fence counter designator (" + sel?.PlacingDef?.defName + ")");
            t.Check(Find.MainTabsRoot.OpenTab == null, "Build closes the main tab");
            Find.DesignatorManager.Deselect();

            // ---- waiting: a pit marker without a fence, an empty fence counter
            var cell = UwKit.TryFindRect(map, 3, 3, out var pr) ? pr.CenterCell : t.FreeCellNearCenter();
            var lonePit = UwKit.SpawnBuilding(UwDefs.FightPit, cell, map).TryGetComp<CompFightPit>();
            var fence = FacKit.Fence(map, t); FacKit.Refresh(map);
            var r1 = Row(map, UnderworldPage.PitId);
            t.Check(r1 != null && r1.State == FacilityState.Waiting && r1.Pit == lonePit && r1.Line == lonePit.StatusLine(), "pit without an arena: amber, the marker's own sentence: " + r1?.Line);
            t.Check(r1 != null && r1.Sub.Count >= 1 && r1.Sub[0] == lonePit.ReadyLine(), "pit row: the ready line below");
            var rf = Row(map, FacilityStatus.FenceId);
            var fi = FacilityStatus.Get(map, FacilityStatus.FenceId);
            // amber until its first deal; a game in which an earlier suite already traded shows the stock line in green
            t.Check(rf != null && rf.State != FacilityState.NotBuilt && rf.State == fi.State && rf.Line == fi.Line && (rf.State == FacilityState.Waiting || fi.Last != null), "fence counter: " + rf?.State + ", its own sentence: " + rf?.Line);
            var rg = Row(map, FacilityStatus.GangsId);
            t.Check(rg != null && rg.State != FacilityState.NotBuilt, "gangs: no longer grey once a fence counter exists (" + rg?.State + ", next: " + string.Join(" | ", rg?.Sub ?? new List<string>()) + ")");
            UwKit.Spawned.Remove(lonePit.parent); lonePit.parent.Destroy();

            // ---- working: a back room, a pit with a running bout
            if (FacKit.MakeBackRoom(map, 4, 4, out _, out var door, out _))
            {
                var rb = Row(map, FacilityStatus.BackRoomId);
                t.Check(rb != null && rb.State == FacilityState.Working && rb.Line == FacilityStatus.Get(map, FacilityStatus.BackRoomId).Line, "back room behind a back room door: green: " + rb?.Line);
            }
            else t.Note("no room for a back room: working back room not checked");
            var pit = PitKit.Arena(map, out var inner, out var outside);
            if (pit != null)
            {
                pit.allowAnimals = true;
                var a = UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(inner.CenterCell, map, 1), "Boar", "Muffalo");
                var b = UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(inner.CenterCell, map, 1), "Boar", "Muffalo");
                pit.Invalidate();
                var rp = Row(map, UnderworldPage.PitId);
                t.Check(rp != null && rp.Pit == pit && rp.State == FacilityState.Waiting && rp.Line == pit.StatusLine(), "closed arena, no guests: amber: " + rp?.Line);
                var bout = FightBout.Start(pit, a, b, true, 60, manual: true);
                t.Check(bout != null, "an animal bout starts");
                rp = Row(map, UnderworldPage.PitId);
                t.Check(rp != null && rp.State == FacilityState.Working && rp.Line == UwPay.T("Pit.Status.Running", a.LabelShortCap, b.LabelShortCap), "bout running: green: " + rp?.Line);
                FightBout.AbortAll("page test");
                yield return 30;
                pit.lastA = "Alpha"; pit.lastB = "Beta"; pit.lastWinner = 0; pit.lastTick = Find.TickManager.TicksGame; pit.lastPoolMicro = 0; pit.lastHouseMicro = 0;
                rp = Row(map, UnderworldPage.PitId);
                t.Check(rp != null && rp.Sub.Contains(pit.LastBoutLine()), "the last bout line below the pit row");
                pit.lastTick = -1; pit.lastA = pit.lastB = null;
                // Fighters… and Show
                var w = Window_PitFighters.Open(pit);
                yield return 3;
                t.Check(Find.WindowStack.IsOpen<Window_PitFighters>(), "Fighters… opens the Fighters window");
                w?.Close(false); Find.WindowStack.TryRemove(typeof(Window_PitFighters), false);
                Find.MainTabsRoot.SetCurrentTab(tabDef, false);
                yield return 3;
                UnderworldPage.JumpTo(pit.parent);
                yield return 2;
                t.Check(Find.MainTabsRoot.OpenTab == null && Find.Selector.IsSelected(pit.parent), "Show closes the tab and selects the marker");
                Find.Selector.ClearSelection();
            }
            else t.Note("no room for an arena: pit working state not checked");

            // ---- open offer: a row with Accept and Decline
            var dock = FacKit.Dock(map, t); FacKit.Refresh(map);
            Payments.CreditColony(map, Money.FromSilver(3000), "test");
            t.Check(Smuggler.Fire(map, force: true), "a boat lands");
            var offers = UnderworldPage.Offers(map, refresh: true);
            var o = offers.FirstOrDefault();
            t.Check(offers.Count == 1 && o != null && o.kind == UwOfferKind.Smuggler, "the page lists the open offer");
            if (o != null)
            {
                t.Check(UnderworldPage.AcceptLabel(o) == UwPay.T("Smuggler.Buy", o.price.ToStringShort()) && UnderworldPage.DeclineLabel(o) == UwPay.T("Smuggler.Decline"), "Accept and Decline carry the dock's button texts");
                t.Check(FacilityStatus.OfferLine(o).Contains(Offers.TimeLeft(o)), "the offer line says the time left");
                t.Check(Row(map, FacilityStatus.DockId)?.State == FacilityState.Working, "the dock row is green while a boat waits");
                var keep = UwKit.Till(map); Ledger.Current.Post(Underworld.Till(map), "world", keep, "test drain");
                t.Check(!FacilityStatus.CanAccept(o, out string why) && why == UwPay.T("Smuggler.NoCash"), "empty till: Accept disabled, the reason is the tooltip (" + why + ")");
                t.Check(!UnderworldPage.AcceptOffer(o) && Offers.IsOpen(o), "a click on the disabled Accept does nothing");
                Ledger.Current.Post("world", Underworld.Till(map), keep, "test refill");
                UnderworldPage.DeclineOffer(o);
                t.Check(!Offers.IsOpen(o) && UnderworldPage.Offers(map).Count == 0, "Decline closes the offer and the row goes away");
                Smuggler.Fire(map, force: true);
                o = UnderworldPage.Offers(map, refresh: true).FirstOrDefault();
                if (o != null)
                {
                    var till0 = UwKit.Till(map);
                    t.Check(UnderworldPage.AcceptOffer(o) && till0 - UwKit.Till(map) == o.price && !Offers.IsOpen(o), "Accept buys the crate and closes the offer");
                }
                Smuggler.Fire(map, force: true);
                t.Check(UnderworldPage.Offers(map, refresh: true).Count == 1, "a new boat for the drawing test");
            }

            // ---- draw the page at 1280x720 with everything on it
            HUI.TestScreen = new Vector2(1280f, 720f);
            try
            {
                MainTabWindow_HH.SelectedPage = "underworld";
                Find.MainTabsRoot.SetCurrentTab(tabDef, false);
                yield return 30;
                Hearth.Shell.Tests.UiTestUtil.Shot("uw_page_1280");
                yield return 10;
                t.Check(Find.MainTabsRoot.OpenTab == tabDef && MainTabWindow_HH.SelectedPage == "underworld", "the page draws (tab still open on it)");
                Find.MainTabsRoot.EscapeCurrentTab(false);
            }
            finally { HUI.TestScreen = null; }
            FacKit.CloseOffers();
            t.Check(HLog.ErrorCount == errors0, "no errors of ours while building and drawing the page (" + (HLog.ErrorCount - errors0) + ")");
            UwKit.End();
            UnderworldPage.Invalidate();
        }
    }

    /// <summary>The guide topic "The back room": unlocked while the underworld is visible, every step ticks from game state, the jump opens the page.</summary>
    public sealed class UwGuideSuite : HearthTestSuite
    {
        public override string Name => "underworld.guide";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 6000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var c = Underworld.Comp;
            UwKit.Begin(ContentIntensity.Standard);
            var def = DefDatabase<GuideTopicDef>.GetNamedSilentFail("HH_Guide_BackRoom");
            t.Check(def != null && def.module == ModuleId.Underworld && def.unlockedBy == UwGuide.Visible && def.steps.Count == 5, "topic HH_Guide_BackRoom exists (module Underworld, unlocked by underworldVisible, 5 steps)");
            if (def == null) { UwKit.End(); yield break; }
            t.Check(def.steps.Select(s => s.check).SequenceEqual(new[] { UwGuide.PitArena, UwGuide.BoutSettled, UwGuide.BackRoomBuilt, UwGuide.GangVisit, UwGuide.FenceBuilt }), "steps in order: fence + marker, first bout, back room, gang visit, fence counter");
            t.Check(def.steps.All(s => s.module == ModuleId.Underworld && s.jump == "tab:underworld"), "every step jumps to the Underworld page");

            // the sticky state the test touches, restored at the end
            string settledKey = "uw.pitSettled." + map.uniqueID, doneKey = "done." + def.defName;
            bool hadSettled = UiState.HasValue(settledKey); float settled0 = UiState.Value(settledKey, 0f);
            bool hadDone = UiState.HasValue(doneKey);
            int visits0 = c.GangVisits;
            try
            {
                t.Check(GuideChecks.Eval(UwGuide.Visible), "underworldVisible at Standard");
                HearthProfile.Current.Content = ContentIntensity.Cozy;
                t.Check(!GuideChecks.Eval(UwGuide.Visible), "not at Cozy");
                HearthProfile.Current.Content = ContentIntensity.Standard;

                UiState.Store_Remove(settledKey); c.GangVisits = 0; UiState.Store_Remove(doneKey);
                bool anyPit = CompFightPit.All.Any(p => p.Map == map && p.ArenaValid), anyRoom = BackRoom.HasBackRoom(map), anyFence = FenceTrade.FindCounter(map) != null;
                if (!anyPit) t.Check(!GuideChecks.Eval(UwGuide.PitArena), "no arena yet: step 1 open");
                t.Check(!GuideChecks.Eval(UwGuide.BoutSettled), "no bout settled: step 2 open");
                if (!anyRoom) t.Check(!GuideChecks.Eval(UwGuide.BackRoomBuilt), "no back room: step 3 open");
                t.Check(!GuideChecks.Eval(UwGuide.GangVisit), "no gang visit: step 4 open");
                if (!anyFence) t.Check(!GuideChecks.Eval(UwGuide.FenceBuilt), "no fence counter: step 5 open");

                GuideState.Update();
                var v = GuideState.Views.FirstOrDefault(x => x.Def == def);
                t.Check(v != null && v.Unlocked && v.Steps.Count == 5 && !v.Done, "the topic shows in the guide, not done");

                var pit = PitKit.Arena(map, out _, out _);
                if (pit != null) t.Check(GuideChecks.Eval(UwGuide.PitArena), "fence + marker: step 1 ticks");
                else t.Note("no room for an arena");
                PitRecords.NoteSettled(map);
                t.Check(GuideChecks.Eval(UwGuide.BoutSettled), "a settled bout: step 2 ticks");
                if (FacKit.MakeBackRoom(map, 4, 4, out _, out _, out _)) t.Check(GuideChecks.Eval(UwGuide.BackRoomBuilt), "back room: step 3 ticks");
                else t.Note("no room for a back room");
                c.GangVisits = 1;
                t.Check(GuideChecks.Eval(UwGuide.GangVisit), "a gang came: step 4 ticks");
                FacKit.Fence(map, t);
                t.Check(GuideChecks.Eval(UwGuide.FenceBuilt), "fence counter: step 5 ticks");
                GuideState.Update();
                v = GuideState.Views.FirstOrDefault(x => x.Def == def);
                t.Check(v != null && (pit == null || v.Steps.All(s => s.done)), "all steps done: " + (v == null ? "-" : string.Join(", ", v.Steps.Select(s => s.step.id + "=" + s.done))));

                // the jump opens the page
                GuidePanel.Jump("tab:underworld");
                yield return 3;
                t.Check(Find.MainTabsRoot.OpenTab?.defName == "HH_MainTab" && MainTabWindow_HH.SelectedPage == "underworld", "a step's jump opens the Underworld page");
                Find.MainTabsRoot.EscapeCurrentTab(false);
            }
            finally
            {
                if (hadSettled) UiState.SetValue(settledKey, settled0); else UiState.Store_Remove(settledKey);
                if (!hadDone) UiState.Store_Remove(doneKey);
                c.GangVisits = visits0;
                UwKit.End();
            }
        }
    }
}
#endif
