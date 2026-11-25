// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Testing;

namespace Hearth.Underworld
{
    /// <summary>Helpers for the in-game suites of the Underworld module.</summary>
    static class UwKit
    {
        public static readonly List<Thing> Spawned = new List<Thing>();
        static ContentIntensity oldContent; static float oldTier; static bool saved;

        /// <summary>Sets the content intensity and tier for a suite (1.2.0: no discovery, no warnings). Nested calls keep the first saved state.</summary>
        public static void Begin(ContentIntensity c, float tier = 3f)
        {
            var p = HearthProfile.Current;
            if (!saved) { oldContent = p.Content; oldTier = p.Tier; saved = true; }
            p.Content = c; p.Tier = tier;
            var comp = Underworld.Comp;
            comp.SetWhispers(0, Find.TickManager.TicksGame);
            comp.Orders.Clear();
            Underworld.MarkInUseDirty(null);
        }

        public static void End()
        {
            foreach (var t in Spawned.ToList()) if (t != null && !t.Destroyed) { try { t.Destroy(); } catch { } }
            Spawned.Clear();
            ShowColonySilver();
            var p = HearthProfile.Current; if (saved) { p.Content = oldContent; p.Tier = oldTier; saved = false; }
            FightBout.AbortAll("test end");
            Underworld.MarkInUseDirty(null);
        }

        public static Faction Friendly() => Find.FactionManager.AllFactionsVisible.FirstOrDefault(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && !f.HostileTo(Faction.OfPlayer) && !f.def.hidden);

        public static T Track<T>(T t) where T : Thing { Spawned.Add(t); return t; }

        /// <summary>A clear w×h rectangle (all cells standable, free, unfogged) near the map centre, at least <paramref name="skip"/> cells away from the centre in steps.</summary>
        public static bool TryFindRect(Map map, int w, int h, out CellRect rect, int startRadius = 4)
        {
            rect = default;
            var center = TestCells.Anchor(map);
            foreach (var c in GenRadial.RadialCellsAround(center, 78f, true).Where(c => c.DistanceTo(center) >= startRadius))
            {
                var r = CellRect.CenteredOn(c, w, h);
                if (!r.InBounds(map)) continue;
                bool ok = true;
                foreach (var cell in r.ExpandedBy(1)) { if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetFirstBuilding(map) != null || cell.Fogged(map) || cell.GetTerrain(map).passability == Traversability.Impassable
                    || cell.GetTerrain(map).affordances?.Contains(TerrainAffordanceDefOf.Light) != true) { ok = false; break; } }   // light buildings (fence, marker) must be placeable: no marsh or mud
                // a free patch is no use when it sits in a pocket the colonists and the visitors cannot walk to (water, cliffs, rock on a random map)
                if (ok && !TestCells.Reachable(map, center, r.CenterCell)) ok = false;
                if (ok) { rect = r; return true; }
            }
            return false;
        }

        static ThingDef FenceDef => DefDatabase<ThingDef>.GetNamedSilentFail("Fence") ?? ThingDefOf.Wall;
        public static void BuildFence(Map map, CellRect outer)
        {
            foreach (var c in outer.EdgeCells) { var f = ThingMaker.MakeThing(FenceDef, ThingDefOf.WoodLog); f.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(f, c, map); Track(f); }
        }

        public static Thing SpawnBuilding(ThingDef def, IntVec3 c, Map map, Rot4? rot = null)
        {
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null); t.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(t, c, map, rot ?? Rot4.North); return Track(t);
        }

        public static List<Pawn> Guests(Map map, int n, Faction f, bool shady = false)
        {
            var list = new List<Pawn>();
            for (int i = 0; i < n; i++)
            {
                var kind = f.RandomPawnKind() ?? PawnKindDefOf.Villager;
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, f, PawnGenerationContext.NonPlayer, map.Tile, canGeneratePawnRelations: false));
                if (!p.DevelopmentalStage.Adult()) { p.ageTracker.AgeBiologicalTicks = 25L * 3600000L; p.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(TestCells.Anchor(map), map, 12), map); Track(p);
                list.Add(p);
            }
            LordMaker.MakeNewLord(f, new LordJob_VisitColony(f, TestCells.Anchor(map), 60000), map, list);
            foreach (var p in list) { if (shady) Underworld.MarkShady(p); UwPay.GiveSilver(p, 600); var gi = GuestApi.Info(p); if (gi != null) gi.Budget = Money.FromSilver(600); }
            return list;
        }

        public static int Silver(Pawn p) { int n = 0; foreach (var t in p.inventory.innerContainer) if (t.def == ThingDefOf.Silver) n += t.stackCount; return n; }
        public static Money Till(Map map) => Ledger.Current.Balance(Underworld.Till(map));

        static readonly List<Thing> hiddenSilver = new List<Thing>();
        /// <summary>Forbids the colony's silver on the map, so a check of "the colony cannot pay" sees only the till (and the bank, if any). Undone by <see cref="ShowColonySilver"/> or <see cref="End"/>.</summary>
        public static void HideColonySilver(Map map)
        {
            foreach (var s in map.listerThings.ThingsOfDef(ThingDefOf.Silver).ToList()) if (!s.IsForbidden(Faction.OfPlayer)) { s.SetForbidden(true, false); hiddenSilver.Add(s); }
            UwPay.ForgetFunds();
        }
        /// <summary>Puts <paramref name="n"/> silver near <paramref name="near"/> as colony stock (home area, not forbidden) and tracks it for <see cref="End"/>.</summary>
        public static Thing StockSilver(Map map, IntVec3 near, int n)
        {
            var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n;
            if (!GenPlace.TryPlaceThing(s, near, map, ThingPlaceMode.Near, out Thing placed)) return null;
            placed = placed ?? s;
            map.areaManager.Home[placed.Position] = true; placed.SetForbidden(false, false);
            Track(placed); UwPay.ForgetFunds();
            return placed;
        }
        public static void ShowColonySilver()
        {
            foreach (var s in hiddenSilver) if (s != null && !s.Destroyed) s.SetForbidden(false, false);
            hiddenSilver.Clear(); UwPay.ForgetFunds();
        }
        public static PawnKindDef Animal(params string[] names) { foreach (var n in names) { var k = DefDatabase<PawnKindDef>.GetNamedSilentFail(n); if (k != null) return k; } return PawnKindDefOf.Muffalo; }
        public static Pawn TameAnimal(Map map, IntVec3 c, params string[] kinds)
        {
            var p = PawnGenerator.GeneratePawn(Animal(kinds), Faction.OfPlayer); GenSpawn.Spawn(p, c, map); return Track(p);
        }
    }

    public sealed class UwHiddenSuite : HearthTestSuite
    {
        public override string Name => "underworld.hidden";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Cozy);
            t.Check(DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Underworld_1") != null && UwDefs.BackRoomDoor != null, "defs present (HH_Underworld_1 kept for old saves)");
            t.Check(!Underworld.Visible && !Underworld.Unlocked, "Cozy: underworld not visible");
            t.Check(UwDefs.ResHunt.IsHidden && UwDefs.ResClinic.IsHidden, "Cozy: research hidden");
            t.Check(!UwDefs.ResHunt.CanStartNow, "Cozy: research cannot start");
            foreach (var d in UwDefs.GatedBuildings) t.Check(d.GetModExtension<UwGateExtension>() != null && !UwDefs.Open(d.GetModExtension<UwGateExtension>()), "Cozy: " + d.defName + " gate shut");
            var door = UwDefs.BackRoomDoor;
            t.Check(!new PlaceWorker_UnderworldGate().IsBuildDesignatorVisible(door), "Cozy: designator hidden");
            if (f != null) UwKit.Guests(map, 2, f);
            t.Check(!Gangs.Possible(map) && !Smuggler.Possible(map) && !Hunts.Possible(map), "Cozy: no gang, smuggler or hunt");
            var lettersBefore = new HashSet<Letter>(Find.LetterStack.LettersListForReading); int bouts = Underworld.Comp.Bouts;
            yield return 3000;
            // only letters of the underworld count: in the shared test game the storyteller and other suites' guests may send their own
            var uwTitles = new HashSet<string>(new[] { "Gang.Demand.Title", "Gang.Title", "Hunt.DeathTitle", "Hunt.FreedTitle", "Hunt.Title", "Organ.OrderTitle", "Smuggler.InspectTitle", "Smuggler.Title", "Tour.IntelTitle" }.Select(k => UwPay.T(k)));
            foreach (ScandalKind k in Enum.GetValues(typeof(ScandalKind))) uwTitles.Add(("HH.Underworld.Scandal." + k + ".Title").Translate());
            var newLetters = Find.LetterStack.LettersListForReading.Where(l => !lettersBefore.Contains(l)).ToList();
            foreach (var l in newLetters) t.Note("new letter (not ours unless flagged): " + l.Label);
            t.Check(!newLetters.Any(l => uwTitles.Contains(l.Label.ToString())), "Cozy: no underworld letter in 3000 ticks (" + newLetters.Count + " other letters)");
            t.Check(Underworld.Comp.Bouts == bouts, "Cozy: nothing happened");
            t.Check(!Find.WindowStack.IsOpen<Dialog_NodeTree>(), "Cozy: no dialog");
            // module switch
            UwKit.Begin(ContentIntensity.Standard);
            t.Check(Underworld.Unlocked, "Standard: unlocked");
            HearthProfile.Current.SetModule(ModuleId.Underworld, false);
            t.Check(!Underworld.Visible && !Underworld.Unlocked, "module off: closed");
            HearthProfile.Current.SetModule(ModuleId.Underworld, true);
            // Dark applies at once (no warning dialog since 1.2.0)
            UwKit.Begin(ContentIntensity.Dark);
            t.Check(Underworld.Effective == ContentIntensity.Dark && Underworld.Allows(UwFeature.OrganClinic) && Underworld.Allows(UwFeature.HuntPrisoners) && !Underworld.Allows(UwFeature.HuntPrisonersNoPrecept), "Dark: organs and prisoner hunt at once, no Grim extras");
            UwKit.End();
        }
    }

    /// <summary>1.2.0 package A: the underworld just works. Fresh Standard game, Dark and Grim without dialogs, old saves, Cozy, the N08 guard.</summary>
    public sealed class UwGatesSuite : HearthTestSuite
    {
        public override string Name => "underworld.gates";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var pw = new PlaceWorker_UnderworldGate();
            var basics = new[] { UwDefs.BackRoomDoor, UwDefs.FightPit, UwDefs.FenceCounter, UwDefs.SmugglerDock };
            HearthTabPage Tab() => HearthTabs.Pages.FirstOrDefault(p => p.Id == "underworld");
            bool TabShown() => HearthTabs.VisiblePages.Contains(Tab());
            // a fresh profile is Standard with the module on
            var fresh = new HearthProfile();
            t.Check(fresh.Content == ContentIntensity.Standard && fresh.ModuleOn(ModuleId.Underworld) && Modules.DefaultOn(ModuleId.Underworld), "a new profile: Standard content, Underworld module on");
            UwKit.Begin(ContentIntensity.Standard);
            HearthProfile.Current.SetModule(ModuleId.Underworld, true);
            t.Check(Underworld.Visible && Underworld.Unlocked, "fresh Standard game: the underworld is open");
            foreach (var d in basics)
            {
                t.Check(d != null && pw.IsBuildDesignatorVisible(d), d?.defName + ": designator visible");
                t.Check(d != null && d.IsResearchFinished, d?.defName + ": needs no research");
                t.Check(d != null && pw.AllowsPlacing(d, IntVec3.Zero, Rot4.North, map).Accepted, d?.defName + ": the gate allows placing");
            }
            t.Check(UwDefs.LoungeVenue != null && UwDefs.LoungeVenue.researchPrerequisite == null && UwDefs.Open(UwDefs.LoungeVenue.GetModExtension<UwGateExtension>()), "drug lounge venue type: no research, gate open");
            t.Check(Tab() != null && TabShown(), "Underworld tab visible");
            var resMain = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Underworld_1"); var resKey = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Underworld_Discovery");
            t.Check(resMain != null && resKey != null && resMain.IsHidden && resKey.IsHidden, "research tab shows neither HH_Underworld_1 nor the old key project");
            foreach (var r in new[] { UwDefs.ResHunt, UwDefs.ResClinic })
                t.Check((r.prerequisites ?? new List<ResearchProjectDef>()).Concat(r.hiddenPrerequisites ?? new List<ResearchProjectDef>()).All(q => q != resMain && q != resKey), r.defName + " no longer needs HH_Underworld_1");
            t.Check(!UwDefs.ResHunt.IsHidden && UwDefs.ResClinic.IsHidden, "Standard: the hunt research visible, the clinic research hidden");
            t.Check(DefDatabase<ThingDef>.AllDefs.All(d => d.researchPrerequisites == null || !d.researchPrerequisites.Contains(resMain)), "no building needs HH_Underworld_1");
            t.Check(LanguageDatabase.defaultLanguage.HaveTextForKey("HH.Underworld.Page.ContentLevel") && !LanguageDatabase.activeLanguage.HaveTextForKey("HH.Underworld.Page.Content") && !LanguageDatabase.defaultLanguage.HaveTextForKey("HH.Underworld.Page.Content") && !UwPay.T("Page.ContentLevel", "HH.UI.Content.Standard".Translate().Resolve()).Contains("{"), "Underworld tab: one content line, no second 'applies' level");
            t.Check(HSettings.Find("underworld.mode") == null && HSettings.Find("underworld.strangerMeanDays") == null && !HSettings.All.Any(x => x.Category == "Discovery"), "discovery settings are gone");
            t.Check(DefDatabase<IncidentDef>.GetNamedSilentFail("HH_UnderworldStranger") == null, "the stranger incident is gone");
            // old save: the flag says "not discovered" → still open; stored values of removed settings stay silent
            var prof = HearthProfile.Current; bool oldFlag = prof.UnderworldDiscovered; int errs0 = HLog.ErrorCount;
            prof.UnderworldDiscovered = false;
            prof.Store.Set("underworld.mode", 0f); prof.Store.Set("underworld.strangerMaxAsks", 3f);
            t.Check(Underworld.Unlocked && pw.IsBuildDesignatorVisible(UwDefs.FightPit) && TabShown(), "old save with underworldDiscovered=false: unlocked");
            prof.Store.Remove("underworld.mode"); prof.Store.Remove("underworld.strangerMaxAsks"); prof.UnderworldDiscovered = oldFlag;
            t.Check(HLog.ErrorCount == errs0, "stored values of removed settings cause no error");
            // N08 guard: nothing reaches a colony without a facility
            UwKit.Begin(ContentIntensity.Standard);
            bool anyFacility = UwDefs.Facilities.Any(d => map.listerThings.ThingsOfDef(d).Any(x => x.Faction == Faction.OfPlayer)) || Venues.OfType(map, UwDefs.LoungeVenue).Any();
            if (anyFacility) t.Note("the test map already has an underworld facility; the 'not in use' checks are skipped");
            else
            {
                t.Check(!Underworld.InUse(map), "no facility: not in use");
                t.Check(!Gangs.Possible(map) && !Smuggler.Possible(map), "no facility: no gang, no smuggler");
                var f = UwKit.Friendly();
                if (f != null)
                {
                    Underworld.Comp.SetWhispers(60f, Find.TickManager.TicksGame);
                    float w = 1f + Math.Max(0, f.PlayerGoodwill) / 25f;
                    t.Check(Math.Abs(Hearth.Guests.MapComponent_HH_GuestScheduler.FactionWeight(f, map) - w) < 1e-4f, "no facility: whispers do not change arrival weights");
                    Underworld.Comp.SetWhispers(0f, Find.TickManager.TicksGame);
                }
                var cell = UwKit.TryFindRect(map, 3, 3, out var rr) ? rr.CenterCell : t.FreeCellNearCenter();
                var pit = UwKit.SpawnBuilding(UwDefs.FightPit, cell, map);
                t.Check(Underworld.InUse(map), "a fight pit marker puts the underworld in use at once");
                if (f != null)
                {
                    Underworld.Comp.SetWhispers(60f, Find.TickManager.TicksGame);
                    float w = 1f + Math.Max(0, f.PlayerGoodwill) / 25f;
                    t.Check(Math.Abs(Hearth.Guests.MapComponent_HH_GuestScheduler.FactionWeight(f, map) - w) > 1e-3f, "with a facility: whispers change arrival weights");
                    Underworld.Comp.SetWhispers(0f, Find.TickManager.TicksGame);
                }
                pit.Destroy(); UwKit.Spawned.Remove(pit);
                t.Check(!Underworld.InUse(map), "removing it: not in use again");
                HearthProfile.Current.Content = ContentIntensity.Cozy; Underworld.MarkInUseDirty(map);
                var pit2 = UwKit.SpawnBuilding(UwDefs.FightPit, cell, map);
                t.Check(!Underworld.InUse(map), "Cozy: never in use, even with a facility");
                pit2.Destroy(); UwKit.Spawned.Remove(pit2);
                HearthProfile.Current.Content = ContentIntensity.Standard;
            }
            // Dark and Grim: open at once, no dialog opens on its own
            Find.WindowStack.TryRemove(typeof(Dialog_NodeTree), false);
            foreach (var c in new[] { ContentIntensity.Dark, ContentIntensity.Grim })
            {
                UwKit.Begin(c);
                t.Check(Underworld.Effective == c, c + ": applies at once (effective " + Underworld.Effective + ")");
                t.Check(Underworld.Allows(UwFeature.OrganClinic) && Underworld.Allows(UwFeature.PitToDeath) && Underworld.Allows(UwFeature.HuntPrisoners), c + ": dark features open");
                t.Check(pw.IsBuildDesignatorVisible(UwDefs.OrganClinic) && pw.AllowsPlacing(UwDefs.OrganClinic, IntVec3.Zero, Rot4.North, map).Accepted, c + ": clinic placeable");
                t.Check(!UwDefs.ResClinic.IsHidden, c + ": clinic research visible");
                t.Check(Underworld.Allows(UwFeature.HuntPrisonersNoPrecept) == (c == ContentIntensity.Grim), c + ": hunt without precept only in Grim");
                t.Check(Underworld.Allows(UwFeature.OrganOrders) == (c == ContentIntensity.Grim), c + ": organ orders only in Grim");
                yield return 3000;
                t.Check(!Find.WindowStack.IsOpen<Dialog_NodeTree>(), c + ": no dialog opened on its own within 3000 ticks");
                Find.WindowStack.TryRemove(typeof(Dialog_NodeTree), false);
            }
            // Cozy hides everything
            UwKit.Begin(ContentIntensity.Cozy);
            t.Check(!Underworld.Visible && !Underworld.Unlocked && !Underworld.InUse(map), "Cozy: closed");
            foreach (var d in UwDefs.GatedBuildings) t.Check(!pw.IsBuildDesignatorVisible(d), "Cozy: " + d.defName + " hidden");
            t.Check(!TabShown(), "Cozy: no Underworld tab");
            t.Check(UwDefs.ResHunt.IsHidden && UwDefs.ResClinic.IsHidden, "Cozy: research hidden");
            t.Check(!UwDefs.Open(UwDefs.LoungeVenue.GetModExtension<UwGateExtension>()), "Cozy: lounge venue gated");
            t.Check(Underworld.IsMinor(null), "null pawn counts as minor (safe default)");
            UwKit.End();
        }
    }

    public sealed class UwWhispersSuite : HearthTestSuite
    {
        public override string Name => "underworld.whispers";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var comp = Underworld.Comp; int now = Find.TickManager.TicksGame;
            UwKit.Begin(ContentIntensity.Standard, tier: 3f);
            comp.SetWhispers(50f, now);
            t.Check(Math.Abs(Underworld.Whispers - 50f) < 0.05f, "stored value read back: " + Underworld.Whispers);
            float d15 = comp.WhispersAt(now + 15 * 60000), d30 = comp.WhispersAt(now + 30 * 60000);
            t.Check(Math.Abs(d15 - 49f) < 0.02f, "2 % decay after 15 days: " + d15);
            t.Check(Math.Abs(d30 - 50f * 0.98f * 0.98f) < 0.03f, "decay compounds: " + d30);
            comp.AddWhispers(70f, "test");
            t.Check(Underworld.Whispers <= 100.001f && Underworld.Whispers > 99f, "clamped at 100: " + Underworld.Whispers);
            comp.AddWhispers(-500f, "test");
            t.Check(Underworld.Whispers >= 0f && Underworld.Whispers < 0.01f, "clamped at 0: " + Underworld.Whispers);
            comp.SetWhispers(40f, now);
            t.Check(Math.Abs(Underworld.RespectableArrivalFactor - (float)Math.Exp(-1.0)) < 0.01f, "respectable arrival weight exp(-N/40)");
            t.Check(Math.Abs(Underworld.OutlawArrivalFactor - 1.0f) < 0.01f, "outlaw arrival weight 0.2 + N/50");
            // scandal changes whispers and renown
            comp.SetWhispers(10f, now);
            var got = new List<(float d, string r)>();
            Action<Map, float, string> listener = (m, d, r) => got.Add((d, r));
            Underworld.RenownDelta += listener;
            comp.NoteScandal(map, -9_999_999);
            var res = Scandals.Raise(ScandalKind.BackRoomFound, map);
            t.Check(res.Raised, "scandal raised");
            t.Check(Math.Abs(Underworld.Whispers - 14f) < 0.1f, "back room found: whispers +4 -> " + Underworld.Whispers);
            t.Check(got.Count == 1 && Math.Abs(got[0].d + 3f) < 0.01f, "renown -4 capped to -3 on Standard Room: " + (got.Count > 0 ? got[0].d.ToString() : "none"));
            var again = Scandals.Raise(ScandalKind.BackRoomFound, map);
            t.Check(!again.Raised && again.Why == "cooldown", "second scandal within 10 days refused");
            HearthProfile.Current.Tier = 0f;
            var free = Scandals.Raise(ScandalKind.LeakedLedger, map, force: true);
            t.Check(got.Count == 1 || Math.Abs(got[got.Count - 1].d) < 0.001f, "Comped tier: negative renown capped to 0");
            HearthProfile.Current.Tier = 6f;
            Scandals.Raise(ScandalKind.LeakedLedger, map, force: true);
            t.Check(got.Last().d <= -4.99f, "Why? tier: full -5 renown, got " + got.Last().d);
            HearthProfile.Current.Tier = 3f;
            int before = got.Count;
            Scandals.Raise(ScandalKind.SecretCasinoRumour, map, force: true);
            t.Check(Underworld.HighRollersActive, "positive scandal: high rollers active");
            t.Check(got.Count == before || got.Last().d >= 0f, "positive scandal never costs renown");
            Underworld.RenownDelta -= listener;
            t.Check(Ledger.Current.SumAll() == 0, "ledger untouched and balanced");
            // save/load of the component values is covered by Scribe; check module hooks
            t.Check(Underworld.IsShady(null) == false, "IsShady(null) is false");
            UwKit.End();
            yield return 1;
        }
    }

    public sealed class UwBackRoomSuite : HearthTestSuite
    {
        public override string Name => "underworld.backroom";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (!UwKit.TryFindRect(map, 5, 5, out var inner)) { t.Note("no free 5x5 area, skipped"); UwKit.End(); yield break; }
            var outer = inner.ExpandedBy(1);
            foreach (var c in outer.EdgeCells) { var w = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); w.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(w, c, map); UwKit.Track(w); }
            foreach (var c in inner) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            // replace one wall cell with the back room door
            var doorCell = new IntVec3(outer.CenterCell.x, 0, outer.minZ);
            doorCell.GetEdifice(map)?.Destroy();
            var door = (Building_BackRoomDoor)UwKit.SpawnBuilding(UwDefs.BackRoomDoor, doorCell, map);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            yield return 2;
            t.Check(BackRoom.HasDoor(map), "door registered");
            t.Check(BackRoom.IsBackRoomCell(inner.CenterCell, map), "room behind only back room doors is a back room");
            t.Check(BackRoom.IsBackRoom(inner.CenterCell.GetRoom(map)), "IsBackRoom(room)");
            if (f != null)
            {
                var guests = UwKit.Guests(map, 1, f); var g = guests[0];
                var shady = UwKit.Guests(map, 1, f, shady: true)[0];
                t.Check(!door.PawnCanOpen(g), "ordinary guest cannot open the door");
                t.Check(door.PawnCanOpen(shady), "shady guest can");
                var colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                if (colonist != null) t.Check(door.PawnCanOpen(colonist), "colonist can");
                t.Check(BackRoom.MayEnter(g) == false && BackRoom.MayEnter(shady), "MayEnter matches");
                HearthProfile.Current.Content = ContentIntensity.Cozy;
                t.Check(BackRoom.MayEnter(g), "switched off (Cozy): a plain door");
                HearthProfile.Current.Content = ContentIntensity.Standard;
            }
            // a second, ordinary door makes the room an ordinary room again
            var other = new IntVec3(outer.CenterCell.x, 0, outer.maxZ);
            other.GetEdifice(map)?.Destroy();
            var plain = UwKit.SpawnBuilding(ThingDefOf.Door, other, map);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            BackRoom.MarkDirty(map);
            yield return 2;
            t.Check(!BackRoom.IsBackRoomCell(inner.CenterCell, map), "with a normal door too: not a back room");
            plain.Destroy(); var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); wall.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(wall, other, map); UwKit.Track(wall);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms(); BackRoom.MarkDirty(map);
            yield return 2;
            t.Check(BackRoom.IsBackRoomCell(inner.CenterCell, map), "back again after the normal door is gone");
            t.Check(BackRoom.TryFindCell(map, null, out var cell) && inner.Contains(cell), "a free cell inside the back room is found");
            foreach (var c in inner) map.roofGrid.SetRoof(c, null);
            UwKit.End();
        }
    }

    public sealed class UwFightPitSuite : HearthTestSuite
    {
        public override string Name => "underworld.fightpit";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null || !UwKit.TryFindRect(map, 7, 7, out var inner)) { t.Note("no friendly faction or free area; skipped"); UwKit.End(); yield break; }
            UwKit.BuildFence(map, inner.ExpandedBy(1));
            var marker = UwKit.SpawnBuilding(UwDefs.FightPit, inner.CenterCell, map);
            var pit = marker.TryGetComp<CompFightPit>();
            t.Check(pit != null && pit.ArenaValid, "closed fence makes a valid arena (" + pit?.ArenaInfo()?.Cells.Count + " cells)");
            t.Check(pit.ArenaInfo().Cells.Count == 49, "arena has 49 cells");
            var cells = inner.Cells.ToList();
            var a = UwKit.TameAnimal(map, cells[10], "Boar", "Muffalo"); var b = UwKit.TameAnimal(map, cells[38], "Boar", "Muffalo");
            var guests = UwKit.Guests(map, 4, f);
            var ledger = Ledger.Current; var till0 = UwKit.Till(map);
            // 1: deterministic settlement with forced winner
            var bout1 = FightBout.Start(pit, a, b, true, 0);
            t.Check(bout1.NonLethal, "Standard: bouts are non-lethal");
            t.Check(bout1.TryBet(guests[0], 0, 50) && bout1.TryBet(guests[1], 0, 50) && bout1.TryBet(guests[2], 1, 100), "three bets accepted");
            t.Check(!bout1.TryBet(guests[0], 1, 10), "a guest bets only once");
            t.Check(bout1.Pool == Money.FromSilver(200), "pool is 200 silver");
            t.Check(ledger.Balance(Underworld.PitAccount(map)) == Money.FromSilver(200), "pit account holds the stakes");
            int s0 = UwKit.Silver(guests[0]), s2 = UwKit.Silver(guests[2]);
            bout1.Finish(0);
            t.Check(bout1.Finished && bout1.Result != null, "bout settled");
            t.Check(bout1.Result.CutMicro == Money.FromSilver(20).Micro, "house cut is exactly 10 %");
            t.Check(UwKit.Silver(guests[0]) == s0 + 90 && UwKit.Silver(guests[1]) >= 600 - 50 + 90, "winners got 90 silver each");
            t.Check(UwKit.Silver(guests[2]) == s2, "loser got nothing back");
            t.Check(ledger.Balance(Underworld.PitAccount(map)).Micro == 0, "pit account empty after settlement");
            t.Check(UwKit.Till(map) - till0 == Money.FromSilver(20), "house take of 20 silver in the till");
            t.Check(ledger.SumAll() == 0, "ledger balanced");
            // 2: a draw refunds everything
            var till1 = UwKit.Till(map); int g3 = UwKit.Silver(guests[3]);
            var bout2 = FightBout.Start(pit, a, b, true, 0);
            bout2.TryBet(guests[3], 1, 40);
            bout2.Finish(-1);
            t.Check(UwKit.Silver(guests[3]) == g3, "draw: stake returned in full");
            t.Check(UwKit.Till(map) == till1 && ledger.SumAll() == 0, "draw: house takes nothing, ledger balanced");
            // 3: an abort (module removal etc.) refunds everything too
            var bout3 = FightBout.Start(pit, a, b, true, 0);
            bout3.TryBet(guests[0], 0, 30);
            int g0 = UwKit.Silver(guests[0]);
            bout3.Abort("test");
            t.Check(UwKit.Silver(guests[0]) == g0 + 30 && ledger.Balance(Underworld.PitAccount(map)).Micro == 0, "abort: stake returned");
            // 4: lethal rules
            pit.lethal = true;
            var lethalStd = FightBout.Start(pit, a, b, true, 0); t.Check(lethalStd.NonLethal, "Standard: lethal switch has no effect"); lethalStd.Abort("test");
            HearthProfile.Current.Content = ContentIntensity.Dark;
            var lethalDark = FightBout.Start(pit, a, b, true, 0); t.Check(!lethalDark.NonLethal, "Dark confirmed: animals may fight to the death"); lethalDark.Abort("test");
            HearthProfile.Current.Content = ContentIntensity.Standard; pit.lethal = false;
            t.Check(FightBout.ActiveCount == 0, "no bouts left");
            // 5: a real fight to the end
            var till2 = UwKit.Till(map);
            var real = FightBout.Start(pit, a, b, true, 0);
            real.TryBet(guests[0], 0, 60); real.TryBet(guests[1], 1, 60); real.TryBet(guests[2], 0, 30);
            real.TryBet(guests[3], 1, 30);
            int waited = 0;
            while (!real.Finished && waited < 9000) { waited += 60; yield return 60; }
            t.Check(real.Finished, "the fight ended on its own after " + waited + " ticks");
            t.Check(real.Result != null && real.Result.PoolMicro == Money.FromSilver(180).Micro, "pool 180 settled");
            t.Check(real.Draw || real.Result.HouseMicro >= real.Result.CutMicro, "house got at least its cut");
            t.Check(real.Draw || real.Result.HouseMicro - real.Result.CutMicro < Money.FromSilver(3).Micro, "rounding leftovers are tiny");
            t.Check(ledger.Balance(Underworld.PitAccount(map)).Micro == 0 && ledger.SumAll() == 0, "ledger balanced after a real bout");
            t.Check(UwKit.Till(map) - till2 == new Money(real.Result.HouseMicro), "till got exactly the house take");
            t.Check(!a.Dead && !b.Dead, "nobody died in a non-lethal bout");
            t.Check(Underworld.Comp.Bouts >= 5 && Underworld.Whispers > 0f, "bouts counted, whispers up");
            // 5b: the damage guard lets damage through for fit fighters and nulls it for beaten ones (without cancelling PreApplyDamage)
            var c1 = UwKit.TameAnimal(map, cells[3], "Boar", "Muffalo"); var c2 = UwKit.TameAnimal(map, cells[45], "Boar", "Muffalo");
            var guardBout = FightBout.Start(pit, c1, c2, true, 0);
            c1.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 0f, -1f, c2));
            t.Check(c1.health.hediffSet.hediffs.Count > 0, "a fit fighter still takes damage in a bout");
            HealthUtility.DamageUntilDowned(c1);
            if (!c1.Dead)
            {
                t.Check(guardBout.ShouldAbsorb(c1), "a downed fighter of a non-lethal bout is beaten");
                int injuries = c1.health.hediffSet.hediffs.Count; float hp = c1.health.summaryHealth.SummaryHealthPercent;
                c1.TakeDamage(new DamageInfo(DamageDefOf.Cut, 25f, 0f, -1f, c2));
                c1.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 25f, 0f, -1f, c2));
                t.Check(!c1.Dead && c1.health.hediffSet.hediffs.Count == injuries && c1.health.summaryHealth.SummaryHealthPercent >= hp - 1e-4f, "no further damage once beaten (injuries " + injuries + " -> " + c1.health.hediffSet.hediffs.Count + ")");
            }
            else t.Note("fighter died while being downed; absorb check skipped");
            guardBout.Abort("test");
            if (!c1.Dead) { var before = c1.health.hediffSet.hediffs.Count; c1.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5f, 0f, -1f, c2)); t.Check(c1.Dead || c1.health.hediffSet.hediffs.Count >= before, "outside a bout damage is normal again"); }
            // 6: broken arena
            var gap = new IntVec3(inner.CenterCell.x, 0, inner.ExpandedBy(1).minZ); gap.GetEdifice(map)?.Destroy(); pit.Invalidate();
            t.Check(!pit.ArenaValid, "a gap in the fence invalidates the arena");
            // 7: gates
            UwKit.Begin(ContentIntensity.Cozy);
            t.Check(!FightBout.TryStartFor(pit, true, out _), "Cozy: no bout can start");
            UwKit.End();
        }
    }

    public sealed class UwFenceSuite : HearthTestSuite
    {
        public override string Name => "underworld.fence";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null) { t.Note("no friendly faction; skipped"); UwKit.End(); yield break; }
            var cell = UwKit.TryFindRect(map, 3, 3, out var r) ? r.CenterCell : t.FreeCellNearCenter();
            var counter = UwKit.SpawnBuilding(UwDefs.FenceCounter, cell, map);
            var fs = counter.TryGetComp<CompFenceStock>();
            t.Check(fs != null && FenceTrade.FindCounter(map) == fs, "counter found on the map");
            var ledger = Ledger.Current;
            Payments.CreditColony(map, Money.FromSilver(1000), "test income");
            var till0 = UwKit.Till(map);
            var seller = UwKit.Guests(map, 1, f, shady: true)[0]; var buyer = UwKit.Guests(map, 1, f, shady: true)[0];
            var item = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); item.stackCount = 10;
            double mv = item.MarketValue * 10;
            seller.inventory.innerContainer.TryAdd(item, false);
            Underworld.Comp.MarkStolen(item, f);
            t.Check(new FenceActivity_Probe().Desire(seller) > 0f, "shady seller with loot wants to visit the fence");
            var ord = UwKit.Guests(map, 1, f)[0];
            t.Check(new FenceActivity_Probe().Desire(ord) == 0f, "ordinary guest does not");
            float w0 = Underworld.Whispers; int s0 = UwKit.Silver(seller); int db0 = Underworld.Comp.DealsBought, ds0 = Underworld.Comp.DealsSold;
            t.Check(FenceTrade.BuyFrom(fs, seller, item, out string why), "fence buys the loot (" + why + ")");
            int expect = (int)Math.Floor(mv * 0.4 + 1e-6);
            int paid = UwKit.Silver(seller) - s0;
            t.Check(paid == expect, $"seller got 40 % = {expect} silver, got {paid}");
            t.Check(fs.Stock.Count == 1 && fs.Stock.First().def == ThingDefOf.MedicineIndustrial, "goods are in stock");
            t.Check(Underworld.Whispers > w0 + 0.9f, "whispers +1 per deal");
            t.Check(ledger.SumAll() == 0, "ledger balanced after buying");
            var stocked = fs.Stock.First();
            var salePrice = FenceTrade.SellPrice(stocked); var buyPrice = FenceTrade.BuyPrice(stocked);
            t.Check(salePrice > buyPrice && Math.Abs(salePrice.Silver - mv * 0.7) < 0.01, "sell price is 70 %: " + salePrice);
            int b0 = UwKit.Silver(buyer);
            t.Check(FenceTrade.SellTo(fs, buyer, stocked, out why), "fence sells to a shady buyer (" + why + ")");
            t.Check(b0 - UwKit.Silver(buyer) == (int)Math.Ceiling(salePrice.Silver - 1e-9), "buyer paid 70 %");
            t.Check(buyer.inventory.innerContainer.Any(x => x.def == ThingDefOf.MedicineIndustrial), "buyer carries the goods away");
            t.Check(fs.Stock.Count == 0, "stock empty");
            t.Check(Underworld.Comp.DealsBought == db0 + 1 && Underworld.Comp.DealsSold == ds0 + 1, "deal counters");
            t.Check(ledger.SumAll() == 0, "ledger balanced after selling");
            t.Check(Underworld.Comp.FenceProfitMicro > 0, "profit tracked: " + new Money(Underworld.Comp.FenceProfitMicro));
            // cash box cap: surplus goes back to the till
            ledger.Post("world", fs.Account, Money.FromSilver(2000), "test float");
            var item2 = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); item2.stackCount = 5; buyer.inventory.innerContainer.TryAdd(item2, false); Underworld.Comp.MarkStolen(item2, f);
            FenceTrade.BuyFrom(fs, buyer, item2, out _);
            FenceTrade.SellTo(fs, seller, fs.Stock.First(), out _);
            t.Check(ledger.Balance(fs.Account) <= Money.FromSilver(HSettings.Get("underworld.fence.capital")), "cash box capped at the setting: " + ledger.Balance(fs.Account));
            ledger.Post(fs.Account, "world", ledger.Balance(fs.Account), "test cleanup");
            // no cash: deal declined
            var item3 = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); item3.stackCount = 5; seller.inventory.innerContainer.TryAdd(item3, false);
            ledger.Post(Underworld.Till(map), "world", UwKit.Till(map), "test drain");
            t.Check(!FenceTrade.BuyFrom(fs, seller, item3, out why), "empty till and empty cash box: the deal is declined");
            // exposure
            var fac = Find.FactionManager.AllFactionsVisible.FirstOrDefault(x => !x.IsPlayer && !x.defeated && x.def.humanlikeFaction && !x.def.hidden);
            int gw0 = fac.PlayerGoodwill; float w1 = Underworld.Whispers;
            t.Check(FenceTrade.MaybeExposed(map, fac, force: true), "owner finds out (forced)");
            t.Check(fac.PlayerGoodwill <= gw0 && fac.PlayerGoodwill >= gw0 - 7, $"goodwill drop stays small on Standard Room (cap 5 plus vanilla rounding): {gw0} -> {fac.PlayerGoodwill}");
            t.Check(Underworld.Whispers > w1 + 5f, "leaked ledger raises whispers");
            // gates
            UwKit.Begin(ContentIntensity.Cozy);
            t.Check(!FenceTrade.BuyFrom(fs, seller, item3, out _), "Cozy: no deals");
            UwKit.End();
        }

        sealed class FenceActivity_Probe { public float Desire(Pawn p) => new Activities.FenceActivity().Desire(p, GuestApi.Info(p)); }
    }

    public sealed class UwGangSuite : HearthTestSuite
    {
        public override string Name => "underworld.gang";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null || !UwKit.TryFindRect(map, 5, 5, out var inner)) { t.Note("no friendly faction or free area; skipped"); UwKit.End(); yield break; }
            var outer = inner.ExpandedBy(1);
            foreach (var c in outer.EdgeCells) { var w = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); w.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(w, c, map); UwKit.Track(w); }
            foreach (var c in inner) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            var dc = new IntVec3(outer.CenterCell.x, 0, outer.minZ); dc.GetEdifice(map)?.Destroy();
            UwKit.SpawnBuilding(UwDefs.BackRoomDoor, dc, map);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms(); BackRoom.MarkDirty(map);
            yield return 2;
            t.Check(Gangs.Possible(map), "gang visits are possible with a back room");
            float w0 = Underworld.Whispers;
            var ok = Gangs.Fire(map, out var pawns);
            t.Check(ok && pawns != null && pawns.Count >= 2, "a gang arrived: " + pawns?.Count);
            if (!ok) { UwKit.End(); yield break; }
            foreach (var p in pawns) UwKit.Track(p);
            t.Check(pawns.All(p => Underworld.IsShady(p) && UwKit.Silver(p) > 0 && p.GetLord() != null), "all members are shady, carry silver and have a lord");
            t.Check(Underworld.Whispers > w0 + 1.5f, "gang visit raises whispers");
            // use the back room directly
            var member = pawns[0]; var till0 = UwKit.Till(map);
            t.Check(BackRoom.TryFindCell(map, member, out var cell), "a back room cell is free for the member: " + cell);
            if (!cell.IsValid) { UwKit.End(); yield break; }
            member.Position = cell; member.Notify_Teleported(true, true);
            member.jobs.StartJob(JobMaker.MakeJob(UwDefs.JobBackRoom, cell), JobCondition.InterruptForced);
            yield return 120;
            t.Check(Underworld.Comp.PaidFee(member), "member paid the back room fee");
            t.Check(UwKit.Till(map) - till0 >= Money.FromSilver(HSettings.Get("underworld.gang.feeMin")) && UwKit.Till(map) - till0 <= Money.FromSilver(HSettings.Get("underworld.gang.feeMax")), "fee within the set range: " + (UwKit.Till(map) - till0));
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            // protection money
            Payments.CreditColony(map, Money.FromSilver(500), "test");
            var fee = HSettings.GetSilver("underworld.gang.protectionFee"); var tillP = UwKit.Till(map); float wP = Underworld.Whispers;
            t.Check(Gangs.PayProtection(map, f, fee) && tillP - UwKit.Till(map) == fee, "paying protection costs exactly the fee");
            int gw = f.PlayerGoodwill; Gangs.RefuseProtection(map, f);
            t.Check(f.PlayerGoodwill <= gw && f.PlayerGoodwill >= gw - 10, "refusing costs goodwill (capped)");
            // interrogation: closed by default
            t.Check(!Interrogation.Allowed(), "holding cells tour is off by default");
            HSettings.Set("underworld.gang.prisonAccess", 1f);
            t.Check(Interrogation.Allowed(), "opt-in switch enables the tour");
            var prisoner = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, f);
            if (!prisoner.DevelopmentalStage.Adult()) { prisoner.ageTracker.AgeBiologicalTicks = 25L * 3600000L; prisoner.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
            GenSpawn.Spawn(prisoner, CellFinder.RandomClosewalkCellNear(TestCells.Anchor(map), map, 10), map); UwKit.Track(prisoner);
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            int tr0 = Underworld.Comp.Tours;
            Interrogation.Apply(member, prisoner);
            t.Check(prisoner.needs.mood.thoughts.memories.GetFirstMemoryOfDef(UwDefs.ThoughtInterrogated) != null, "prisoner remembers the questioning");
            t.Check(!prisoner.health.hediffSet.hediffs.Any(h => h.def == UwDefs.Bruise), "Standard: no bruises");
            t.Check(!prisoner.Dead && prisoner.health.summaryHealth.SummaryHealthPercent > 0.9f, "prisoner unharmed");
            t.Check(Underworld.Comp.Tours == tr0 + 1, "tour counted");
            HSettings.Set("underworld.gang.prisonAccess", 0f);
            // a child is never questioned
            var child = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, fixedBiologicalAge: 9f, fixedChronologicalAge: 9f));
            GenSpawn.Spawn(child, prisoner.Position, map); UwKit.Track(child);
            t.Check(Underworld.IsMinor(child), "a nine-year-old is a minor");
            int tours = Underworld.Comp.Tours; Interrogation.Apply(member, child);
            t.Check(Underworld.Comp.Tours == tours, "children are never questioned");
            foreach (var c in inner) map.roofGrid.SetRoof(c, null);
            UwKit.End();
        }
    }

    public sealed class UwLoungeSuite : HearthTestSuite
    {
        public override string Name => "underworld.lounge";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null) { t.Note("no friendly faction; skipped"); UwKit.End(); yield break; }
            var g = UwKit.Guests(map, 1, f)[0];
            t.Check(Lounge.AllowedLevel(g) >= 0, "ideology level computed");
            var child = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, fixedBiologicalAge: 9f, fixedChronologicalAge: 9f));
            t.Check(Lounge.AllowedLevel(child) == 0, "children never get drugs");
            t.Check(new Activities.LoungeActivity().Desire(g, GuestApi.Info(g)) == 0f, "without a lounge venue no desire");
            var drug = DefDatabase<ThingDef>.GetNamedSilentFail("SmokeleafJoint");
            if (drug != null && Lounge.AllowedLevel(g) > 0)
            {
                var till0 = UwKit.Till(map); int s0 = UwKit.Silver(g); float w0 = Underworld.Whispers;
                t.Check(Lounge.Session(g, drug), "a session works for a paying guest");
                t.Check(UwKit.Till(map) - till0 == HSettings.GetSilver("underworld.lounge.price"), "the guest paid the session price");
                t.Check(UwKit.Silver(g) < s0, "silver left the guest");
                t.Check(Underworld.Whispers > w0 + 0.4f, "whispers +0.5");
                t.Check(g.health.hediffSet.hediffs.Any(h => h.def.defName.Contains("Smokeleaf")), "the drug took effect");
            }
            else t.Note("ideology of the test guest forbids drugs; session skipped");
            var broke = UwKit.Guests(map, 1, f)[0]; foreach (var s in broke.inventory.innerContainer.ToList()) if (s.def == ThingDefOf.Silver) s.Destroy(); GuestApi.Info(broke).Budget = Money.Zero;
            if (drug != null) t.Check(!Lounge.Session(broke, drug), "a guest without money gets nothing");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            UwKit.End();
            yield return 1;
        }
    }

    public sealed class UwHuntSuite : HearthTestSuite
    {
        public override string Name => "underworld.hunt";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            t.Check(!Hunts.HumanQuarryAllowed(out string whyStd), "Standard: no human quarry (" + whyStd + ")");
            HearthProfile.Current.Content = ContentIntensity.Grim;
            t.Check(Hunts.HumanQuarryAllowed(out _), "Grim: human quarry allowed without precept");
            HearthProfile.Current.Content = ContentIntensity.Standard;
            if (f == null || !UwKit.TryFindRect(map, 11, 11, out var inner)) { t.Note("no friendly faction or free area; skipped"); UwKit.End(); yield break; }
            UwKit.BuildFence(map, inner.ExpandedBy(1));
            var marker = UwKit.SpawnBuilding(UwDefs.HuntingGround, inner.CenterCell, map);
            var ground = marker.TryGetComp<CompHuntingGround>();
            t.Check(ground.Valid, "a closed fence makes a hunting ground (" + ground.ArenaInfo().Cells.Count + " tiles)");
            t.Check(Hunts.Possible(map), "hunt parties are possible");
            Payments.CreditColony(map, Money.FromSilver(10), "test");
            var till0 = UwKit.Till(map); float w0 = Underworld.Whispers;
            var ok = Hunts.Fire(map, out var hunters);
            t.Check(ok && hunters != null && hunters.Count >= 1, "a hunting party arrived: " + hunters?.Count);
            if (!ok) { UwKit.End(); yield break; }
            foreach (var h in hunters) UwKit.Track(h);
            var inside = map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal && ground.ArenaInfo().Cells.Contains(p.Position)).ToList();
            foreach (var a in inside) UwKit.Track(a);
            t.Check(inside.Count >= HSettings.GetInt("underworld.hunt.quarryCount"), "quarry released into the ground: " + inside.Count + " (a wild animal of the map may stand inside the fence as well)");
            var fee = UwKit.Till(map) - till0;
            t.Check(fee >= HSettings.GetSilver("underworld.hunt.feeMin") && fee <= HSettings.GetSilver("underworld.hunt.feeMax"), "fee in the set range: " + fee);
            t.Check(Underworld.Whispers >= w0 + 9.9f, "whispers +10");
            t.Check(Underworld.Comp.IsHunter(hunters[0]) && !Underworld.IsShady(hunters[0]), "hunters are marked, not shady");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            // one hunter shoots one animal
            var hunter = hunters[0]; var quarry = inside[0];
            t.Check(hunter.equipment.Primary != null, "hunter is armed");
            var spot = CellFinder.RandomClosewalkCellNear(inner.ExpandedBy(3).Cells.First(c => !inner.ExpandedBy(1).Contains(c) && c.Standable(map)), map, 2);
            hunter.Position = spot; hunter.Notify_Teleported(true, true);
            var job = Hunts.PickHunter(hunter);
            t.Check(job != null && job.def == UwDefs.JobHunt, "the director gives the hunter a hunting job");
            hunter.jobs.debugLog = true;
            t.Note($"DIAG hunter {hunter.LabelShort} weapon {hunter.equipment.Primary?.def.defName} verb {hunter.TryGetAttackVerb(quarry, true)?.GetType().Name} faction {hunter.Faction?.Name} lord {hunter.GetLord()?.LordJob?.GetType().Name} toil {hunter.GetLord()?.CurLordToil?.GetType().Name} quarry {quarry.def.defName}");
            hunter.jobs.StartJob(JobMaker.MakeJob(UwDefs.JobHunt, quarry), JobCondition.InterruptForced);
            int waited = 0;
            while (!inside.Any(q => q.Dead) && waited < 9000) { waited += 100; yield return 100; if (!quarry.Dead && hunter.Spawned && !hunter.Downed && hunter.CurJob?.def != UwDefs.JobHunt && waited % 300 == 0) { var alive = inside.Where(q => !q.Dead && q.Spawned).ToList(); if (alive.Count > 0) quarry = alive[(waited / 300) % alive.Count]; hunter.jobs.StartJob(JobMaker.MakeJob(UwDefs.JobHunt, quarry), JobCondition.InterruptForced); } /* some small quarry cannot be targeted from every spot (the job ends Incompletable at the cast position): try the next animal. the director re-issues the hunt job in the real game (its job ends when the quarry flees out of reach) */ if (waited % 1000 == 0) t.Note($"hunter job={hunter.CurJobDef?.defName} pos={hunter.Position} quarry={quarry.Position} health={quarry.health.summaryHealth.SummaryHealthPercent:0.00} verb={hunter.CurJob?.verbToUse?.GetType().Name} downed={hunter.Downed}"); }
            t.Check(inside.Any(q => q.Dead), "a quarry animal was shot after " + waited + " ticks");
            // gates
            UwKit.Begin(ContentIntensity.Cozy);
            t.Check(!Hunts.Possible(map), "Cozy: no hunt");
            UwKit.End();
        }
    }

    public sealed class UwOrgansSuite : HearthTestSuite
    {
        public override string Name => "underworld.organs";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            UwKit.Begin(ContentIntensity.Dark);
            var cell = UwKit.TryFindRect(map, 3, 3, out var r) ? r.CenterCell : t.FreeCellNearCenter();
            var clinic = UwKit.SpawnBuilding(UwDefs.OrganClinic, cell, map);
            var comp = clinic.TryGetComp<CompOrganClinic>();
            t.Check(comp != null, "clinic counter has its comp");
            var kidney = DefDatabase<ThingDef>.GetNamedSilentFail("Kidney");
            t.Check(kidney != null && Organs_.IsOrgan(ThingMaker.MakeThing(kidney)), "a kidney counts as an organ");
            var o = ThingMaker.MakeThing(kidney); GenSpawn.Spawn(o, cell, map); UwKit.Track(o);
            t.Check(comp.Organs().Contains(o), "organ on the counter is found");
            var till0 = UwKit.Till(map); float w0 = Underworld.Whispers;
            double expect = o.MarketValue * HSettings.Get("underworld.organ.priceFactor");
            t.Check(Organs_.Collect(comp) == 1, "one organ sold");
            t.Check(Math.Abs((UwKit.Till(map) - till0).Silver - expect) < 0.01, $"paid market value x factor: {(UwKit.Till(map) - till0)} vs {expect:0.00}");
            t.Check(o.Destroyed, "organ is gone");
            t.Check(Underworld.Whispers > w0 + 4.9f, "whispers +5 per organ");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            t.Check(Underworld.Comp.OrgansSold == 1, "counter");
            // Standard: counter does nothing by itself
            UwKit.Begin(ContentIntensity.Standard);
            var o2 = ThingMaker.MakeThing(kidney); GenSpawn.Spawn(o2, cell, map); UwKit.Track(o2);
            comp.CompTickRare();
            t.Check(!o2.Destroyed, "Standard: no organ trade");
            // Grim: orders pay a premium
            UwKit.Begin(ContentIntensity.Grim);
            Underworld.Comp.Orders.Add(new OrganOrder { organ = kidney, deadlineTick = Find.TickManager.TicksGame + 60000, premium = 2f });
            var price = Organs_.Price(o2, out var order);
            t.Check(order != null && Math.Abs(price.Silver - o2.MarketValue * HSettings.Get("underworld.organ.priceFactor") * 2.0) < 0.01, "Grim: a matching order doubles the price");
            Organs_.Collect(comp);
            t.Check(order.done, "the order is marked done");
            Underworld.Comp.Orders.Add(new OrganOrder { organ = kidney, deadlineTick = Find.TickManager.TicksGame - 10 });
            Organs_.TickOrders(Find.TickManager.TicksGame);
            t.Check(Underworld.Comp.Orders.All(x => x.done || x.deadlineTick > Find.TickManager.TicksGame), "expired orders are closed");
            UwKit.End();
            yield return 1;
        }
    }

    public sealed class UwSmugglerSuite : HearthTestSuite
    {
        public override string Name => "underworld.smuggler";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            UwKit.Begin(ContentIntensity.Standard);
            t.Check(!Smuggler.Possible(map), "no dock, no landing");
            var cell = UwKit.TryFindRect(map, 3, 3, out var r) ? r.CenterCell : t.FreeCellNearCenter();
            var dock = UwKit.SpawnBuilding(UwDefs.SmugglerDock, cell, map);
            t.Check(Smuggler.Possible(map), "dock makes landings possible");
            var offer = Smuggler.MakeOffer(map);
            t.Check(offer.goods.Count > 0 && offer.price < offer.marketValue, "an offer with goods below market value: " + offer.price + " for " + offer.marketValue);
            t.Check(Math.Abs(offer.price.Silver - offer.marketValue.Silver * HSettings.Get("underworld.smuggler.priceFactor")) < 0.01, "price is the set share of market value");
            Payments.CreditColony(map, offer.price + Money.FromSilver(50), "test");
            var till0 = UwKit.Till(map);
            t.Check(Smuggler.Buy(map, offer), "buying works");
            t.Check(till0 - UwKit.Till(map) == offer.price, "the price left the till");
            yield return 600;
            // look for the ordered goods themselves (landed, merged into a stack nearby, or still in an incoming pod)
            var goodDefs = new HashSet<ThingDef>(offer.goods.Select(g => g.def));
            bool landed = map.listerThings.AllThings.Any(x => goodDefs.Contains(x.def) && x.Spawned && x.Position.InHorDistOf(dock.Position, 30f))
                          || map.listerThings.ThingsOfDef(ThingDefOf.ActiveDropPod).Count > 0 || map.listerThings.ThingsOfDef(ThingDefOf.DropPodIncoming).Count > 0;
            t.Check(landed, "crate dropped near the dock");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            var poor = Smuggler.MakeOffer(map); Ledger.Current.Post(Underworld.Till(map), "world", UwKit.Till(map), "drain");
            UwKit.HideColonySilver(map);
            if (Silverline.BankApi.ColonyBalance.Micro <= 0) t.Check(!Smuggler.Buy(map, poor), "no cash, no crate");
            UwKit.ShowColonySilver();
            UwKit.End();
        }
    }
    public sealed class UwPitFlowSuite : HearthTestSuite
    {
        public override string Name => "underworld.pitflow";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 24000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null || !UwKit.TryFindRect(map, 7, 7, out var inner)) { t.Note("no friendly faction or free area; skipped"); UwKit.End(); yield break; }
            // the gate goes on a side the colony can walk to (PitKit.Arena); before 1.2.0 it was always the south side, which a random map can wall off
            var pit = PitKit.Arena(map, out inner, out var outside);
            pit.allowAnimals = true;
            t.Check(pit.ArenaValid && pit.ArenaInfo().Cells.Count == 49, "arena with a gate is valid and the gate is not part of it");
            var a = UwKit.TameAnimal(map, outside, "Boar", "Muffalo"); var b = UwKit.TameAnimal(map, outside + new IntVec3(1, 0, 0), "Boar", "Muffalo");
            var guests = UwKit.Guests(map, 3, f);
            var till0 = UwKit.Till(map); long take0 = Underworld.Comp.HouseTakeMicro;
            t.Check(FightBout.TryStartFor(pit, true, out string why), "a bout starts through the normal path (" + why + ")");
            var bout = FightBout.OfPit(pit);
            t.Check(bout != null && bout.phase == FightBout.Phase.Assemble, "bout is assembling");
            int waited = 0; bool gaveJobs = false;
            while (bout != null && !bout.Finished && waited < 14000)
            {
                yield return 120; waited += 120;
                if (bout.BettingOpen && !gaveJobs)
                {
                    gaveJobs = true;
                    foreach (var g in guests)
                    {
                        // the guests start up to 12 cells from the map centre and the betting window is limited: bring them to the gate so the walk (and the random map between) does not decide whether they bet
                        g.Position = CellFinder.RandomClosewalkCellNear(outside, map, 4); g.Notify_Teleported(true, true);
                        var act = new Activities.PitActivity(); float d = act.Desire(g, GuestApi.Info(g));
                        var job = d > 0 ? act.TryGiveJob(g, GuestApi.Info(g)) : null;
                        if (job != null) g.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                    t.Note("spectators sent");
                }
                if (waited % 2400 == 0) t.Note($"phase={bout.phase} A in arena={pit.ArenaInfo().Cells.Contains(a.Position)} B={pit.ArenaInfo().Cells.Contains(b.Position)} bets={bout.Bets.Count}");
            }
            t.Check(bout.Finished, "bout finished by itself after " + waited + " ticks");
            t.Check(bout.WinnerSide != -2, "outcome recorded: side " + bout.WinnerSide);
            t.Check(bout.Bets.Count >= 1, "spectators placed bets on arrival: " + bout.Bets.Count);
            t.Check(Ledger.Current.Balance(Underworld.PitAccount(map)).Micro == 0 && Ledger.Current.SumAll() == 0, "ledger balanced, pit account empty");
            if (bout.Result != null) t.Check(Underworld.Comp.HouseTakeMicro - take0 == bout.Result.HouseMicro, "house take booked: " + new Money(bout.Result.HouseMicro));
            foreach (var fp in new[] { a, b }) if (fp.Dead) t.Note("DEAD " + fp.LabelShort + " " + fp.kindDef.defName + ": " + string.Join(", ", fp.health.hediffSet.hediffs.Select(h => h.def.defName + (h.Part != null ? "@" + h.Part.Label : "") + " " + h.Severity.ToString("0.0"))) + "; nonLethal=" + bout.NonLethal + " winner=" + bout.WinnerSide);
            t.Check(!a.Dead && !b.Dead, "both fighters alive");
            UwKit.End();
        }
    }

    public sealed class UwDirectorSuite : HearthTestSuite
    {
        public override string Name => "underworld.director";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null || !UwKit.TryFindRect(map, 5, 5, out var inner)) { t.Note("no friendly faction or free area; skipped"); UwKit.End(); yield break; }
            var outer = inner.ExpandedBy(1);
            foreach (var c in outer.EdgeCells) { var w = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); w.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(w, c, map); UwKit.Track(w); }
            foreach (var c in inner) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            var dc = new IntVec3(outer.CenterCell.x, 0, outer.minZ); dc.GetEdifice(map)?.Destroy();
            UwKit.SpawnBuilding(UwDefs.BackRoomDoor, dc, map);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms(); BackRoom.MarkDirty(map);
            var till0 = UwKit.Till(map);
            Gangs.Fire(map, out var pawns, force: true);
            t.Check(pawns != null && pawns.Count > 0, "gang arrived");
            if (pawns == null) { UwKit.End(); yield break; }
            foreach (var p in pawns) UwKit.Track(p);
            int waited = 0; bool paid = false;
            while (waited < 15000 && !paid)
            {
                yield return 300; waited += 300;
                paid = pawns.Any(p => Underworld.Comp.PaidFee(p));
                if (waited % 3000 == 0) t.Note("jobs: " + string.Join(", ", pawns.Select(p => p.CurJobDef?.defName ?? "none")));
            }
            t.Check(paid, "without any help the director sent a gang member into the back room and he paid (after " + waited + " ticks)");
            t.Check(UwKit.Till(map) > till0, "fee reached the till");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            foreach (var c in inner) map.roofGrid.SetRoof(c, null);
            UwKit.End();
        }
    }
    public sealed class UwJobsSuite : HearthTestSuite
    {
        public override string Name => "underworld.jobs";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null) { t.Note("no friendly faction; skipped"); UwKit.End(); yield break; }
            var spot = UwKit.TryFindRect(map, 5, 5, out var r) ? r.CenterCell : t.FreeCellNearCenter();
            // fence deal as a real job
            var counter = UwKit.SpawnBuilding(UwDefs.FenceCounter, spot, map);
            var fs = counter.TryGetComp<CompFenceStock>();
            Payments.CreditColony(map, Money.FromSilver(800), "test");
            var seller = UwKit.Guests(map, 1, f, shady: true)[0];
            var item = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); item.stackCount = 8; seller.inventory.innerContainer.TryAdd(item, false); Underworld.Comp.MarkStolen(item, f);
            seller.Position = counter.InteractionCell; seller.Notify_Teleported(true, true);
            int bought0 = Underworld.Comp.DealsBought;
            var act = new Activities.FenceActivity();
            var job = act.TryGiveJob(seller, GuestApi.Info(seller));
            t.Check(job != null && job.def == UwDefs.JobFenceDeal, "fence activity offers a fence deal job");
            seller.jobs.StartJob(job, JobCondition.InterruptForced);
            yield return 500;
            t.Check(Underworld.Comp.DealsBought == bought0 + 1 && fs.Stock.Count == 1, "the job ran: loot bought and stocked");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            // lounge job
            var guest = UwKit.Guests(map, 10, f).FirstOrDefault(g => Lounge.AllowedLevel(g) > 0) ?? UwKit.Guests(map, 1, f)[0];
            if (Lounge.AllowedLevel(guest) > 0 && DefDatabase<ThingDef>.GetNamedSilentFail("SmokeleafJoint") != null)
            {
                var cell = t.FreeCellNearCenter(); guest.Position = cell; guest.Notify_Teleported(true, true);
                int ls0 = Underworld.Comp.LoungeSessions;
                guest.jobs.StartJob(JobMaker.MakeJob(UwDefs.JobLounge, cell), JobCondition.InterruptForced);
                yield return 1400;
                t.Check(Underworld.Comp.LoungeSessions == ls0 + 1, "the lounge job completed a session (guest job " + (guest.CurJob?.def.defName ?? "none") + " at " + guest.Position + " of " + cell + ", budget " + GuestApi.Info(guest)?.Budget + ", spawned " + guest.Spawned + ", lord toil " + guest.GetLord()?.CurLordToil?.GetType().Name + ", level " + Lounge.AllowedLevel(guest) + ")");
            }
            else t.Note("test guest cannot use drugs; lounge job skipped");
            // holding cells tour as a real job
            HSettings.Set("underworld.gang.prisonAccess", 1f);
            var visitor = UwKit.Guests(map, 1, f, shady: true)[0];
            var prisoner = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, f);
            if (!prisoner.DevelopmentalStage.Adult()) { prisoner.ageTracker.AgeBiologicalTicks = 25L * 3600000L; prisoner.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
            GenSpawn.Spawn(prisoner, CellFinder.RandomClosewalkCellNear(TestCells.Anchor(map), map, 10), map); UwKit.Track(prisoner);
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            // a walkable cell next to the prisoner: two cells east can be rock, a tree or water on a random map and the visitor then cannot reach anybody
            visitor.Position = CellFinder.RandomClosewalkCellNear(prisoner.Position, map, 3); visitor.Notify_Teleported(true, true);
            int tours0 = Underworld.Comp.Tours;
            var tj = Interrogation.MakeJob(visitor);
            t.Check(tj != null, "the tour job is offered when allowed");
            if (tj != null) { visitor.jobs.StartJob(tj, JobCondition.InterruptForced); yield return 3200; t.Check(Underworld.Comp.Tours == tours0 + 1, "the tour job ran to the end"); }
            HSettings.Set("underworld.gang.prisonAccess", 0f);
            // smuggler offer dialog
            var dockCell = UwKit.TryFindRect(map, 3, 3, out var dr) ? dr.CenterCell : t.FreeCellNearCenter();
            UwKit.SpawnBuilding(UwDefs.SmugglerDock, dockCell, map);
            Find.WindowStack.TryRemove(typeof(Dialog_NodeTree), false);
            t.Check(Smuggler.Fire(map), "a smuggler landing fires");
            t.Check(!Find.WindowStack.IsOpen<Dialog_NodeTree>() && Offers.OpenOf(UwOfferKind.Smuggler, map) != null, "1.2.0: no dialog, an open offer instead");
            var so = Offers.OpenOf(UwOfferKind.Smuggler, map); if (so != null) Offers.Close(so);
            UwKit.End();
        }
    }
    public sealed class UwSaveLoadSuite : HearthTestSuite
    {
        public override string Name => "underworld.saveload";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var comp = Underworld.Comp; int now = Find.TickManager.TicksGame;
            UwKit.Begin(ContentIntensity.Standard);
            comp.SetWhispers(33.5f, now); comp.NeverAsk = true; comp.AskCount = 2; comp.HuntWarningAck = true; comp.OrganWarningAck = false; comp.ContentWarningAck = true;
            comp.Bouts = 7; comp.DealsSold = 3; comp.HouseTakeMicro = 123456789L; comp.FenceProfitMicro = 4242L;
            var fakeShady = 987654; comp.NoteScandal(t.Map, now - 500);
            var kidney = DefDatabase<ThingDef>.GetNamedSilentFail("Kidney");
            comp.Orders.Clear(); comp.Orders.Add(new OrganOrder { organ = kidney, deadlineTick = now + 1234, premium = 1.9f });
            var pawn = UwKit.Guests(t.Map, 1, UwKit.Friendly() ?? Find.FactionManager.OfPlayer, true)[0];
            comp.MarkHunter(pawn); var loot = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); comp.MarkStolen(loot, UwKit.Friendly());
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hh_uw_saveload.xml");
            UnderworldComponent copy = null; bool ok = true; string err = null;
            try
            {
                Scribe.saver.InitSaving(path, "UwTest");
                Scribe_Deep.Look(ref comp, "uw", Current.Game);
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe.EnterNode("UwTest");
                Scribe_Deep.Look(ref copy, "uw", Current.Game);
                Scribe.ExitNode();
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception e) { ok = false; err = e.Message; try { Scribe.ForceStop(); } catch { } }
            t.Check(ok, "component can be written and read back " + err);
            if (ok && copy != null)
            {
                t.Check(Math.Abs(copy.WhispersAt(now) - 33.5f) < 0.01f, "whispers survived");
                t.Check(copy.NeverAsk && copy.AskCount == 2 && copy.HuntWarningAck && !copy.OrganWarningAck && copy.ContentWarningAck, "legacy flags of old saves are still read (and ignored)");
                t.Check(copy.Bouts == 7 && copy.DealsSold == 3 && copy.HouseTakeMicro == 123456789L && copy.FenceProfitMicro == 4242L, "statistics survived");
                t.Check(copy.Orders.Count == 1 && copy.Orders[0].organ == kidney && Math.Abs(copy.Orders[0].premium - 1.9f) < 0.001f, "order book survived");
                t.Check(copy.IsHunter(pawn) && copy.IsShady(pawn), "pawn marks survived");
                t.Check(copy.IsStolen(loot, out _), "loot marks survived");
                t.Check(copy.ScandalTickOn(t.Map) == now - 500, "scandal cooldown survived");
            }
            try { System.IO.File.Delete(path); } catch { }
            UwKit.End();
            yield return 1;
        }
    }
}
#endif
