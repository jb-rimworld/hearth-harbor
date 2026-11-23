// SPDX-License-Identifier: MIT
// In-game suites for the build-1 review fixes of the Inn module (A1, A2, A5, B1–B9, C1–C10). Built only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Hearth.Shell;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Guests
{
    /// <summary>Two rooms side by side with a door between them: A (guest beds, door to the outside) and B (for a venue sign).</summary>
    public static class InnUxKit
    {
        public sealed class Pair
        {
            public List<Thing> Built = new List<Thing>();
            public CellRect A, B; public IntVec3 InnerDoor, OuterDoor;
            public List<Building_Bed> Beds = new List<Building_Bed>();
            public Building_FrontDesk Desk;
        }

        static bool Free(Map map, CellRect r)
        {
            foreach (var c in r)
            {
                if (!c.InBounds(map) || c.Fogged(map) || c.GetFirstBuilding(map) != null || c.GetFirstPawn(map) != null || c.GetEdifice(map) != null) return false;
                var terr = c.GetTerrain(map);
                if (terr == null || terr.passability == Traversability.Impassable || terr.IsWater || !terr.affordances.Contains(TerrainAffordanceDefOf.Heavy)) return false;
            }
            return true;
        }

        static Thing Spawn(ThingDef def, IntVec3 c, Map map, Rot4 rot, ThingDef stuff = null)
        {
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? (stuff ?? ThingDefOf.WoodLog) : null);
            if (def.CanHaveFaction) t.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(t, c, map, rot, WipeMode.Vanish);
        }

        /// <summary>Outer box 13×6 (+4 south for a desk): rooms A (x 1–5) and B (x 7–11), wall at x 6 with a door, outer door south of A.</summary>
        public static Pair Build(Map map, ThingDef bedDef, int beds, bool desk)
        {
            var p = new Pair();
            CellRect outer = CellRect.Empty; bool found = false;
            foreach (var c in GenRadial.RadialCellsAround(map.Center + new IntVec3(-20, 0, 20), 40, true))
            {
                outer = new CellRect(c.x, c.z, 13, 6 + (desk ? 4 : 0));
                if (Free(map, outer.ExpandedBy(1))) { found = true; break; }
            }
            if (!found) throw new Exception("no free building site found");
            foreach (var c in outer.ExpandedBy(1)) foreach (var t in c.GetThingList(map).ToList()) if (t is Plant || t.def.category == ThingCategory.Item) t.Destroy();
            int z0 = outer.minZ + (desk ? 4 : 0);
            var box = new CellRect(outer.minX, z0, 13, 6);
            p.A = new CellRect(box.minX + 1, box.minZ + 1, 5, 4); p.B = new CellRect(box.minX + 7, box.minZ + 1, 5, 4);
            p.InnerDoor = new IntVec3(box.minX + 6, 0, box.minZ + 2); p.OuterDoor = new IntVec3(box.minX + 3, 0, box.minZ);
            foreach (var c in box.EdgeCells) p.Built.Add(Spawn(c == p.OuterDoor ? ThingDefOf.Door : ThingDefOf.Wall, c, map, Rot4.North));
            for (int z = box.minZ + 1; z < box.maxZ; z++) { var c = new IntVec3(box.minX + 6, 0, z); p.Built.Add(Spawn(c == p.InnerDoor ? ThingDefOf.Door : ThingDefOf.Wall, c, map, Rot4.North)); }
            foreach (var c in box.ContractedBy(1)) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            for (int i = 0; i < beds; i++)
            {
                var bed = (Building_Bed)Spawn(bedDef, new IntVec3(p.A.minX + i * 2, 0, p.A.maxZ - (bedDef.size.z - 1)), map, Rot4.North);
                p.Built.Add(bed); p.Beds.Add(bed);
            }
            if (desk)
            {
                p.Desk = (Building_FrontDesk)Spawn(InnDefOf.HH_FrontDesk, new IntVec3(p.OuterDoor.x, 0, p.OuterDoor.z - 3), map, Rot4.North);
                p.Built.Add(p.Desk);
            }
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            return p;
        }

        public static Thing PlaceSign(Map map, IntVec3 c, string type, string name)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("HH_VenueSign"); if (def == null) return null;
            var sign = Spawn(def, c, map, Rot4.South);
            var comp = sign.TryGetComp<Hearth.ShopsFood.CompVenueSignHH>();
            var vt = DefDatabase<VenueTypeDef>.GetNamedSilentFail(type);
            if (comp != null && vt != null) { comp.SetType(vt); comp.alwaysOpen = true; comp.customName = name; }
            Venues.MarkDirty(map);
            return sign;
        }

        public static void Teardown(Pair p, Map map)
        {
            if (p == null) return;
            foreach (var b in p.Beds) if (!b.Destroyed) GuestBeds.Comp(b)?.SetGuestBed(false, false);
            foreach (var t in p.Built) if (!t.Destroyed) t.Destroy(DestroyMode.Vanish);
            foreach (var c in p.A.ExpandedBy(1).Cells.Concat(p.B.ExpandedBy(1).Cells)) map.roofGrid.SetRoof(c, null);
            var wc = GuestWings.Comp(map); if (wc != null) { foreach (var w in wc.wings) w.Clear(); wc.wings.Clear(); wc.selected = null; wc.Version++; }
            Venues.MarkDirty(map);
        }
    }

    /// <summary>A1 + A2 + B6: the allowed-area check is O(1) and sees venue rooms; wings grow towards new venue signs; IsValidBedFor rules.</summary>
    public sealed class InnAllowedSuite : HearthTestSuite
    {
        public override string Name => "inn.allowed";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var site = InnUxKit.Build(map, ThingDefOf.Bed, 1, false);
            foreach (var b in site.Beds) GuestBeds.Comp(b).SetGuestBed(true, byPlayer: false);
            yield return 10;
            var comp = GuestWings.Comp(map);
            var wing = comp.wings.FirstOrDefault(w => w.TrueCount > 0);
            t.Check(wing != null && wing[site.A.CenterCell], "Guest Wing 1 around room A");
            t.Check(wing != null && !wing[site.B.CenterCell] && !wing[site.InnerDoor], "room B and the door between are not in the wing yet");
            var f = InnTestKit.GuestFaction(map);
            var g = f == null ? null : GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.Overnight, Nights = 2, Silent = true, ForceBudget = Money.FromSilver(300) });
            t.Check(g != null, "a guest arrived");
            if (g == null) { InnUxKit.Teardown(site, map); MapComponent_HH_GuestScheduler.SuspendedForTests = false; yield break; }
            var r = reg.Members(g).First(); var p = r.Pawn;
            int waited = 0;
            while (waited < 15000 && !(r.CheckedIn && r.Bed != null)) { yield return 200; waited += 200; }
            t.Check(r.CheckedIn && GuestRegistry.IsWingRestricted(p), "guest checked in and is wing-restricted (" + waited + " ticks)");

            // A2: IsValidBedFor
            var bed = site.Beds[0];
            t.Check(r.Bed == bed && RestUtility.IsValidBedFor(bed, p, p, false), "the guest may use the booked guest bed (bed of another faction is allowed for them)");
            var col = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => !c.Downed);
            if (col != null) t.Check(!RestUtility.IsValidBedFor(bed, col, col, false), "a colonist may not use the guest bed");
            var postfix = AccessTools.Method(typeof(Patch_RestUtility_IsValidBedFor), "Postfix");
            object[] args = { bed, null, p, false, false, false, null, true };
            bool threw = false;
            try { postfix.Invoke(null, args); } catch (Exception e) { threw = true; t.Note("postfix threw: " + e.InnerException?.Message); }
            t.Check(!threw && (bool)args[7], "null sleeper: postfix leaves the result alone");
            args = new object[] { bed, col ?? p, col ?? p, false, false, false, null, false };
            postfix.Invoke(null, args);
            t.Check(!(bool)args[7], "a vanilla 'no' for a non-guest stays 'no'");

            // A1: venue room cells come from a grid, not a room lookup per cell
            var inB = site.B.CenterCell;
            t.Check(!inB.InAllowedArea(p), "room B is forbidden while it is no venue");
            var sign = InnUxKit.PlaceSign(map, new IntVec3(site.B.maxX, 0, site.B.maxZ), "HH_Venue_Shop", "Test Shop");
            t.Check(sign != null, "venue sign placed in room B");
            yield return 5;
            comp.MarkVenuesDirty();
            t.Check(comp.RefreshVenueGrid(force: true) || comp.IsVenueCell(inB), "venue grid rebuilt for the new sign");
            t.Check(comp.IsVenueCell(inB) && inB.InAllowedArea(p), "a cell in the venue room is allowed for the guest");
            var far = GenRadial.RadialCellsAround(site.A.CenterCell, 25, true).Last(c => c.InBounds(map) && !wing[c] && !comp.IsVenueCell(c) && c.Standable(map));
            t.Check(!far.InAllowedArea(p), "a cell far outside stays forbidden");
            // timing: 200 000 calls (half inside the wing, half outside) must be cheap (no room lookups, no venue list scans)
            var sw = Stopwatch.StartNew();
            int allowed = 0;
            for (int i = 0; i < 100000; i++) { if (GuestWings.AllowedFor(p, far)) allowed++; if (GuestWings.AllowedFor(p, inB)) allowed++; }
            sw.Stop();
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / 200000.0;
            t.Note($"AllowedFor: {us:0.000} µs per call");
            t.Check(allowed == 100000 && us < 2.0, $"AllowedFor stays O(1): {us:0.000} µs per call (limit 2 µs)");

            // B6: the wing grows towards the new venue (room B and the door between)
            int grown = comp.GrowTowardsNewVenues();
            t.Check(grown > 0 && wing[inB] && wing[site.InnerDoor], "wing grew towards the venue: " + grown + " cells, door and room B included");
            t.Check(comp.GrowTowardsNewVenues() == 0, "a known venue does not grow the wing again");
            // C5: selecting the bed draws the wing outline (smoke: no exception in the update path)
            Find.Selector.ClearSelection(); Find.Selector.Select(bed);
            bool drew = true; try { comp.MapComponentUpdate(); } catch (Exception e) { drew = false; t.Note(e.ToString()); }
            Find.Selector.ClearSelection();
            t.Check(drew, "wing outline draws for a selected guest bed");

            reg.SendAway(p);
            yield return 300;
            InnTestKit.RemoveAllGuests(map);
            if (sign != null && !sign.Destroyed) sign.Destroy();
            InnUxKit.Teardown(site, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>A5: returning guests are pinned as world pawns only while a return is planned; cap, removal, map loss and caretaker release them.</summary>
    public sealed class InnReturnPinsSuite : HearthTestSuite
    {
        public override string Name => "inn.returnpins";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 2000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var f = InnTestKit.GuestFaction(map);
            t.Check(f != null, "guest faction");
            if (f == null) yield break;
            GuestReturns.ReleaseAll();
            var made = new List<Pawn>();
            Pawn Make() { var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, tile: map.Tile)); Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Decide); made.Add(p); return p; }
            float cap0 = HSettings.Get(InnSettings.ReturnPinCap);
            try
            {
                var a = new List<Pawn> { Make(), Make() };
                sch.AddReturn(f, a, StayKind.Overnight, 1, Find.TickManager.TicksGame + 60000 * 5);
                GuestReturns.Pin(a);
                t.Check(a.All(x => Find.WorldPawns.ForcefullyKeptPawns.Contains(x) && reg.PinnedReturners.Contains(x)), "planned returners are pinned and booked");
                // cap: 3 → pinning two more drops the oldest plan first
                HSettings.Set(InnSettings.ReturnPinCap, 3f);
                var b = new List<Pawn> { Make(), Make() };
                GuestReturns.MakeRoomForTests(b.Count);
                sch.AddReturn(f, b, StayKind.Overnight, 1, Find.TickManager.TicksGame + 60000 * 6);
                GuestReturns.Pin(b);
                t.Check(reg.PinnedReturners.Count <= 3, "pin cap respected: " + reg.PinnedReturners.Count);
                t.Check(a.All(x => !Find.WorldPawns.ForcefullyKeptPawns.Contains(x)), "the oldest planned return was dropped and unpinned");
                t.Check(!sch.Forecast.Any(g => g.IsReturn && g.Returning.Intersect(a).Any()), "its plan is gone");
                // caretaker: a pin without a plan is released
                var stray = Make();
                GuestReturns.Pin(new[] { stray });
                int fixes = GuestReturns.CaretakerPins();
                t.Check(fixes >= 1 && !Find.WorldPawns.ForcefullyKeptPawns.Contains(stray) && !reg.PinnedReturners.Contains(stray), "caretaker released a pin without a planned return");
                t.Check(b.All(x => Find.WorldPawns.ForcefullyKeptPawns.Contains(x)), "pins with a plan stay");
                // a pin someone else set is never released by us
                var foreign = Make();
                Find.WorldPawns.ForcefullyKeptPawns.Add(foreign);
                GuestReturns.Unpin(new[] { foreign });
                t.Check(Find.WorldPawns.ForcefullyKeptPawns.Contains(foreign), "a foreign pin is left alone");
                Find.WorldPawns.ForcefullyKeptPawns.Remove(foreign);
                // map loss: the scheduler releases its pins
                sch.MapRemoved();
                t.Check(b.All(x => !Find.WorldPawns.ForcefullyKeptPawns.Contains(x)) && !sch.Forecast.Any(), "a removed map releases its returners");
                // removal: everything goes
                var c = new List<Pawn> { Make() };
                sch.AddReturn(f, c, StayKind.Overnight, 1, Find.TickManager.TicksGame + 60000 * 4);
                GuestReturns.Pin(c);
                var report = new List<string>();
                HearthMod.ModuleObjects.FirstOrDefault(m => m.Id == ModuleId.Inn)?.PrepareRemoval(report);
                t.Check(reg.PinnedReturners.Count == 0 && !Find.WorldPawns.ForcefullyKeptPawns.Contains(c[0]), "module removal releases every pin: " + string.Join(" / ", report));
            }
            finally
            {
                HSettings.Set(InnSettings.ReturnPinCap, cap0);
                GuestReturns.ReleaseAll();
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
            yield break;
        }
    }

    /// <summary>
    /// B1/C1, B3/C10, B5.4, B7, B9, C2, C3, C4, C7, C8: one Guests page, couples share a double bed, the front desk is staffed by Hosting work,
    /// leaving guests keep a read-only Guest tab, recruitment progress and actionable complaints, price rules per bed, wing labels.
    /// </summary>
    public sealed class InnUxSuite : HearthTestSuite
    {
        public override string Name => "inn.ux";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 40000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            // B1: one Guests page
            t.Check(!HearthTabs.Pages.Any(pg => pg.Id == "inn.overview"), "no second guests page");
            t.Check(HearthTabs.Pages.Count(pg => pg.LabelEn == "Guests") == 1, "exactly one page called Guests");
            t.Check(HearthUI.GuestsHeader != null && HearthUI.GuestStatus != null && HearthUI.TopComplaint != null, "the Inn fills the shell's guest hooks");
            // B7 / B9 defaults
            t.Check(HSettings.Get(InnSettings.FirstGuestMinDays) <= 0.5f + 1e-4 && HSettings.Get(InnSettings.FirstGuestMaxDays) <= 1.2f + 1e-4, "first guests within 0.5–1.2 days");
            t.Check(Math.Abs(HSettings.Get(InnSettings.PriceBase) - 12f) < 0.01f, "base price 12 silver");
            t.Check(GuestBedPricing.Classes.Select(c => c.mult).SequenceEqual(new[] { 7f, 4f, 2.5f, 1.5f, 1f }), "room classes ×1 / 1.5 / 2.5 / 4 / 7");
            t.Check(Math.Abs(GuestEconomy.PodBudgetFactor - 1.15f) < 1e-4, "drop-pod guests bring ×1.15");

            var dbl = DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed") ?? ThingDefOf.Bed;
            var site = InnUxKit.Build(map, dbl, 1, desk: true);
            var bed = site.Beds[0];
            GuestBeds.Comp(bed).SetGuestBed(true, byPlayer: false);
            yield return 10;
            // C8: price rules
            var bc = GuestBeds.Comp(bed);
            var auto = GuestBedPricing.Quote(bed).Price;
            bc.PriceMode = BedPriceMode.Free; var free = GuestBedPricing.Quote(bed).Price;
            bc.PriceMode = BedPriceMode.Discount; var disc = GuestBedPricing.Quote(bed).Price;
            bc.PriceMode = BedPriceMode.Premium; var prem = GuestBedPricing.Quote(bed).Price;
            bc.PriceMode = BedPriceMode.Manual; bc.ManualPrice = 37; var man = GuestBedPricing.Quote(bed).Price;
            bc.PriceMode = BedPriceMode.Auto; bc.ManualPrice = 0;
            t.Check(man.WholeSilver == 37, "manual price mode gives the stored fixed price: " + man);
            t.Check(free.Micro == 0 && disc <= auto && prem >= auto && prem.WholeSilver % 5 == 0, $"bed price rules: auto {auto}, free {free}, −25 % {disc}, +25 % {prem}");
            var bedGizmos = bed.GetGizmos().ToList();
            t.Check(bedGizmos.OfType<Command_HH_BedUse>().Count() == 1 && !bedGizmos.OfType<Command_Action>().Any(gz => !(gz is Command_HH_BedUse) && gz.defaultLabel != null && gz.defaultLabel.Contains("Price")),
                "the guest bed shows exactly one Hearth & Harbor button (role + price chip): " + string.Join(", ", bedGizmos.OfType<Command_HH_BedUse>().Select(g => g.defaultLabel)));
            // B7: next arrival on the bed
            var f = InnTestKit.GuestFaction(map);
            if (f == null) { t.Check(false, "no guest faction"); InnUxKit.Teardown(site, map); yield break; }
            var soon = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, tile: map.Tile));
            Find.WorldPawns.PassToWorld(soon, RimWorld.Planet.PawnDiscardDecideMode.Decide);
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
            sch.AddReturn(f, new List<Pawn> { soon }, StayKind.Overnight, 1, Find.TickManager.TicksGame + 30000);
            string insp = bed.GetInspectString();
            t.Check(insp.Contains("Next guests") && insp.Contains(" in "), "guest bed shows the next arrival countdown: " + insp.Replace("\n", " | "));
            sch.ClearAllReturns();
            // C7: a host staffs the front desk
            var hosting = DefDatabase<WorkTypeDef>.GetNamed("HH_Hosting");
            var host = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => !c.Downed && !c.WorkTypeIsDisabled(hosting) && !c.InMentalState);
            var wg = new WorkGiver_HH_StaffFrontDesk { def = DefDatabase<WorkGiverDef>.GetNamed("HH_StaffFrontDesk") };
            float o0 = HSettings.Get(InnSettings.DeskOpenHour), c0 = HSettings.Get(InnSettings.DeskCloseHour);
            HSettings.Set(InnSettings.DeskOpenHour, 0f); HSettings.Set(InnSettings.DeskCloseHour, 0f); // open around the clock
            if (host != null)
            {
                host.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                host.workSettings.SetPriority(hosting, 1);
                t.Check(!wg.HasJobOnThing(host, site.Desk), "nothing to staff while nobody arrives");
            }
            var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 2, Kind = StayKind.Overnight, Nights = 2, Silent = true, ForceBudget = Money.FromSilver(600) });
            t.Check(g != null, "two guests arrived");
            if (g == null) { HSettings.Set(InnSettings.DeskOpenHour, o0); HSettings.Set(InnSettings.DeskCloseHour, c0); InnUxKit.Teardown(site, map); yield break; }
            var ms = reg.Members(g).ToList();
            var a = ms[0]; var b = ms.Count > 1 ? ms[1] : null;
            // C4: make them a couple before they check in
            // spouses: every ideology lets spouses share a bed
            if (b != null && !LovePartnerRelationUtility.LovePartnerRelationExists(a.Pawn, b.Pawn)) a.Pawn.relations.AddDirectRelation(PawnRelationDefOf.Spouse, b.Pawn);
            if (host != null)
            {
                t.Check(wg.HasJobOnThing(host, site.Desk), "arriving guests: the host has front desk work");
                var job = wg.JobOnThing(host, site.Desk);
                t.Check(job != null && job.def == InnDefOf.HH_StaffFrontDesk, "staff job created");
                // stand there for the test (drafted so other work does not pull the host away)
                var cell = site.Desk.StaffCell(host);
                if (host.drafter != null) host.drafter.Drafted = true;
                host.jobs.StopAll(); host.Position = cell; host.Notify_Teleported(false);
                t.Check(site.Desk.Host() == host, "the desk sees its host");
            }
            int waited = 0;
            while (waited < 20000 && !ms.All(m => m.CheckedIn)) { yield return 250; waited += 250; }
            t.Check(ms.All(m => m.CheckedIn), "both checked in (" + waited + " ticks)");
            if (host != null)
            {
                t.Check(ms.Any(m => m.Events.Any(e => e.Contains("welcomed"))), "a guest was welcomed in person by the host");
                if (host.drafter != null) host.drafter.Drafted = false;
            }
            bool ideoForbids = b != null && bed.CompAssignableToPawn != null && ModsConfig.IdeologyActive
                && (!BedUtility.WillingToShareBed(a.Pawn, b.Pawn) || !BedUtility.WillingToShareBed(b.Pawn, a.Pawn));
            if (ideoForbids) t.Note("the guests' ideology forbids sharing a bed even for spouses: couple checks skipped");
            if (b != null && !ideoForbids)
            {
                t.Check(a.Bed != null && a.Bed == b.Bed && a.Bed.SleepingSlotsCount >= 2, "the couple shares a double bed");
                if (a.Bed != null) bed = a.Bed;
                var quote = GuestBedPricing.Quote(a.Bed);
                t.Check(Math.Abs((a.NightPrice + b.NightPrice).Silver - quote.Price.Silver) < 0.01 && Math.Abs(a.NightPrice.Silver - b.NightPrice.Silver) <= 1.01,
                    $"the couple pays the price of the double bed together, half each (no surcharge): {a.NightPrice} + {b.NightPrice} = {quote.Price}");
                t.Check(bed.OwnersForReading.Contains(a.Pawn) && bed.OwnersForReading.Contains(b.Pawn), "both own the bed");
                bool valid = RestUtility.IsValidBedFor(bed, b.Pawn, b.Pawn, false);
                if (!valid) t.Note($"partner bed check: canUseNow={RestUtility.CanUseBedNow(bed, b.Pawn, false)} reach={b.Pawn.CanReach(bed, PathEndMode.OnCell, Danger.Some)} reserve={b.Pawn.HasReserved(bed) || b.Pawn.CanReserve(bed, bed.SleepingSlotsCount, 0)} forbidden={bed.IsForbidden(b.Pawn)} job={b.Pawn.CurJobDef?.defName} inBed={b.Pawn.InBed()}");
                t.Check(valid, "the partner may sleep in the shared bed");
            }
            // B5.4: wing label, C2: handling cell
            var row = GuestsPage.MakeRow(a.Pawn, a);
            t.Check(row.Wing.StartsWith("Guest Wing") && !row.Wing.StartsWith("wing"), "guests table shows the wing's name: " + row.Wing);
            t.Check(row.Handled, "handling can be changed from the table");
            var h0 = a.Handling;
            var h1 = GuestsPage.CycleHandling(a.Pawn, a); var h2 = GuestsPage.CycleHandling(a.Pawn, a); var h3 = GuestsPage.CycleHandling(a.Pawn, a);
            t.Check(h1 == HandlingRules.Next(a.Pawn, h0) && a.Handling == h3 && HandlingRules.All(a.Pawn).Contains(h1) && HandlingRules.All(a.Pawn).Contains(h2) && HandlingRules.All(a.Pawn).Contains(h3),
                "handling cycles through the offered modes: " + h0 + " → " + h1 + " → " + h2 + " → " + h3);
            for (int cyc = 0; cyc < 5 && a.Handling != h0; cyc++) GuestsPage.CycleHandling(a.Pawn, a);   // back to where it started
            // C3: recruitment progress
            ITab_HH_Guest.SetHandling(a.Pawn, a, HandlingMode.Recruit);
            var st = HearthUI.GuestStatus(a.Pawn);
            t.Check(st != null && st.RecruitProgress >= 0f && (st.RecruitLine ?? "").Contains("Bond"), "recruitment progress in the guest tab: " + st?.RecruitLine);
            float y = 0f;
            bool drawn = true; try { y = ITab_HH_Guest.DrawBody(400f, a.Pawn, a); } catch (Exception e) { drawn = false; t.Note(e.ToString()); }
            t.Check(drawn && y > 100f, "guest tab body draws");
            ITab_HH_Guest.SetHandling(a.Pawn, a, HandlingMode.Basics);
            // C10: actionable complaints
            a.Pawn.needs.food.CurLevelPercentage = 0.1f;
            st = HearthUI.GuestStatus(a.Pawn);
            t.Check(st.Problems.Any(pr => pr.Contains("Hungry")), "a hungry guest says what would help: " + string.Join(" / ", st.Problems));
            string top = HearthUI.TopComplaint(map);
            t.Check(!top.NullOrEmpty(), "the concierge names the top complaint: " + top);
            a.Pawn.needs.food.CurLevelPercentage = 0.9f;
            // B2: income by category
            var cats = OverviewPage.IncomeByCategory(7);
            t.Check(cats.Any(c => c.cat == "inn.room" && c.today.Micro > 0), "overview: room income today by category: " + string.Join(", ", cats.Select(c => c.cat + "=" + c.today)));
            t.Check(OverviewPage.DailyIncome(7).Count == 7, "seven daily bars");
            // B3: a guest sent away keeps a read-only Guest tab while still on the map
            reg.SendAway(a.Pawn);
            t.Check(!GuestApi.IsGuest(a.Pawn) && ITab_HH_Guest.Visible(a.Pawn), "the guest tab stays visible for a leaving guest");
            st = HearthUI.GuestStatus(a.Pawn);
            t.Check(st != null && st.ReadOnly && (st.StateLine ?? "").Contains("Sent away"), "read-only state line: " + st?.StateLine);
            var before = a.Handling;
            ITab_HH_Guest.SetHandling(a.Pawn, a, HandlingMode.Recruit);
            t.Check(a.Handling == before, "handling cannot change after the stay is settled");
            drawn = true; try { ITab_HH_Guest.DrawBody(400f, a.Pawn, a); } catch (Exception e) { drawn = false; t.Note(e.ToString()); }
            t.Check(drawn, "read-only guest tab draws");
            HSettings.Set(InnSettings.DeskOpenHour, o0); HSettings.Set(InnSettings.DeskCloseHour, c0);
            yield return 300;
            InnTestKit.RemoveAllGuests(map);
            InnUxKit.Teardown(site, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }
}
#endif
