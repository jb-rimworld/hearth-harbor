// SPDX-License-Identifier: MIT
// Shared hotel builder: the showcase and the long run (LongRunSuite) build the same hotel with it. Compiled only with HH_TESTS.
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
    /// <summary>Builds the demo hotel (lobby, restaurant, casino, rooms, spa, bank, power) on the current map, staffs it and sets up bank + insurance.</summary>
    public sealed class ShowcaseBuilder
    {
        public Map map; public TestContext T; public IntVec3 O;
        public readonly List<Thing> powered = new List<Thing>();
        public readonly List<Thing> fuelled = new List<Thing>();
        public CellRect casino, lobby, restaurant, spa, bank;
        public List<Pawn> Cols = new List<Pawn>();
        ThingDef wallStuff, doorStuff;

        // ---------------------------------------------------------------- helpers
        public IntVec3 P(int x, int z) => new IntVec3(O.x + x, 0, O.z + z);
        public CellRect R(int x0, int z0, int x1, int z1) => new CellRect(O.x + x0, O.z + z0, x1 - x0 + 1, z1 - z0 + 1);

        ThingDef D(string n)
        {
            var d = DefDatabase<ThingDef>.GetNamedSilentFail(n);
            if (d == null) T.Note("MISSING def " + n + " (skipped)");
            return d;
        }

        TerrainDef Terr(params string[] names)
        {
            foreach (var n in names)
            {
                var d = DefDatabase<TerrainDef>.GetNamedSilentFail(n);
                if (d != null) return d;
            }
            foreach (var n in names)
            {
                var d = DefDatabase<TerrainDef>.AllDefs.FirstOrDefault(x => x.defName.StartsWith(n));
                if (d != null) return d;
            }
            T.Note("MISSING terrain " + string.Join("/", names));
            return TerrainDefOf.Soil;
        }

        void Floor(CellRect r, TerrainDef d) { foreach (var c in r) if (c.InBounds(map)) map.terrainGrid.SetTerrain(c, d); }

        /// <summary>Spawns def so that its (rotated) footprint has its lower-left corner at (x,z) relative to the building origin.</summary>
        Thing Put(string defName, int x, int z, Rot4? rot = null, ThingDef stuff = null, bool power = false, QualityCategory? q = null)
        {
            var def = D(defName); if (def == null) return null;
            return Put(def, x, z, rot, stuff, power, q);
        }

        Thing Put(ThingDef def, int x, int z, Rot4? rot = null, ThingDef stuff = null, bool power = false, QualityCategory? q = null)
        {
            Rot4 r = rot ?? Rot4.North;
            // the footprint's lower-left corner must land on (x, z) for every rotation (even sizes shift differently when rotated south or west)
            var want = new IntVec3(O.x + x, 0, O.z + z);
            var probe = GenAdj.OccupiedRect(want, r, def.size);
            var center = want + (want - probe.Min);
            try
            {
                var th = ThingMaker.MakeThing(def, def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null);
                if (def.CanHaveFaction) th.SetFaction(Faction.OfPlayer);
                if (q.HasValue) th.TryGetComp<CompQuality>()?.SetQuality(q.Value, ArtGenerationContext.Colony);
                var spawned = GenSpawn.Spawn(th, center, map, r, WipeMode.Vanish);
                if (spawned == null || !spawned.Spawned) { T.Note("could not spawn " + def.defName + " at " + x + "," + z); return null; }
                if (spawned.TryGetComp<CompPowerTrader>() != null) { powered.Add(spawned); }
                if (spawned.TryGetComp<CompRefuelable>() != null) fuelled.Add(spawned);
                return spawned;
            }
            catch (Exception e) { T.Note("spawn " + def.defName + " failed: " + e.Message); return null; }
        }

        void Wall(int x, int z) { Put(ThingDefOf.Wall, x, z, null, wallStuff); Put(ThingDefOf.PowerConduit == null ? null : D("PowerConduit"), x, z); }
        void DoorAt(int x, int z) { Put(ThingDefOf.Door, x, z, null, doorStuff); Put("PowerConduit", x, z); }

        void Table(int x, int z, int chairsPerSide = 2)
        {
            Put("Table2x2c", x, z, null, ThingDefOf.WoodLog);
            Put("DiningChair", x - 1, z, Rot4.East, ThingDefOf.WoodLog); Put("DiningChair", x - 1, z + 1, Rot4.East, ThingDefOf.WoodLog);
            Put("DiningChair", x + 2, z, Rot4.West, ThingDefOf.WoodLog); Put("DiningChair", x + 2, z + 1, Rot4.West, ThingDefOf.WoodLog);
            if (chairsPerSide > 2) { Put("DiningChair", x, z - 1, Rot4.North, ThingDefOf.WoodLog); Put("DiningChair", x + 1, z + 2, Rot4.South, ThingDefOf.WoodLog); }
        }

        void Lamp(int x, int z) => Put("StandingLamp", x, z, null, null, true);
        void Pot(int x, int z) => Put("PlantPot", x, z);
        // ---------------------------------------------------------------- the build
        public IEnumerable<int> Build(TestContext t, bool noon = true, bool money = true)
        {
            T = t; map = t.Map;
            // --- noon, clear weather
            if (noon) try
            {
                float h = GenLocalDate.HourFloat(map);
                int delta = (int)(((11f - h + 24f) % 24f) * 2500f);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + delta);
                map.weatherManager.TransitionTo(WeatherDefOf.Clear);
                map.weatherManager.curWeatherAge = 0;
            }
            catch (Exception e) { t.Note("time/weather: " + e.Message); }

            // --- site
            var cols = Cols = map.mapPawns.FreeColonistsSpawned.ToList();
            var c0 = cols.Count > 0 ? cols[0].Position : map.Center;
            O = new IntVec3(Mathf.Clamp(c0.x - 21, 14, map.Size.x - 60), 0, Mathf.Clamp(c0.z - 6, 14, map.Size.z - 40));
            t.Note("building origin " + O + " on map " + map.Size);
            wallStuff = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksSandstone") ?? DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite") ?? ThingDefOf.WoodLog;
            doorStuff = ThingDefOf.WoodLog;

            var clear = R(-9, -12, 52, 34);
            foreach (var c in clear)
            {
                if (!c.InBounds(map)) continue;
                foreach (var th in c.GetThingList(map).ToList())
                    if (!(th is Pawn) && th.def.destroyable && !th.Destroyed) th.Destroy();
                map.roofGrid.SetRoof(c, null);
                map.fogGrid.Unfog(c);
                var tr = c.GetTerrain(map);
                if (tr.IsWater || tr.passability == Traversability.Impassable || tr.defName.Contains("Rough") || tr.defName.Contains("Rock")) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            }
            foreach (var c in R(-9, -12, 52, 34)) if (c.InBounds(map)) map.snowGrid.SetDepth(c, 0f);
            var pad = R(-4, -10, 45, 23);
            foreach (var c in R(-9, -12, 52, 34))
            {
                if (!c.InBounds(map) || pad.Contains(c)) continue;
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                try
                {
                    float r = Rand.Value;
                    string pd = r < 0.05f ? "Plant_TreeOak" : r < 0.09f ? "Plant_Bush" : r < 0.8f ? "Plant_Grass" : null;
                    var plantDef = pd == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(pd);
                    if (plantDef != null && c.GetFirstBuilding(map) == null)
                    { var pl = (Plant)ThingMaker.MakeThing(plantDef); pl.Growth = 0.5f + Rand.Value * 0.5f; GenSpawn.Spawn(pl, c, map); }
                }
                catch { }
            }

            var tile = Terr("TileSandstone"); var wood = Terr("WoodPlankFloor"); var marble = Terr("TileMarble", "TileGranite");
            var paved = Terr("PavedTile", "Concrete"); var flag = Terr("FlagstoneSandstone", "FlagstoneGranite"); var sterile = Terr("SterileTile");
            var carpetRed = Terr("Carpet_Red", "CarpetRed", "Carpet_"); var carpetBlue = DefDatabase<TerrainDef>.AllDefs.FirstOrDefault(d => d.defName.StartsWith("Carpet") && d.defName.Contains("Blue")) ?? carpetRed;
            var carpetTan = DefDatabase<TerrainDef>.AllDefs.FirstOrDefault(d => d.defName.StartsWith("Carpet") && (d.defName.Contains("Tan") || d.defName.Contains("Cream") || d.defName.Contains("White"))) ?? carpetBlue;
            t.Note("carpets: " + carpetRed.defName + " / " + carpetBlue.defName + " / " + carpetTan.defName);

            // plaza and apron
            Floor(R(-1, -1, 42, 22), paved);
            Floor(R(6, -8, 36, -2), flag);
            Floor(R(19, -8, 22, -1), paved);
            Floor(R(-3, -1, -2, 22), flag); Floor(R(43, -1, 44, 22), flag);

            // interiors
            lobby = R(15, 1, 26, 10); restaurant = R(1, 1, 13, 10); casino = R(28, 1, 40, 10); spa = R(19, 15, 28, 20); bank = R(30, 15, 40, 20);
            Floor(lobby, marble); Floor(restaurant, wood); Floor(R(1, 8, 13, 10), sterile); Floor(casino, carpetRed);
            Floor(R(1, 12, 40, 13), tile);
            foreach (var rx in new[] { 1, 7, 13 }) Floor(R(rx, 15, rx + 4, 20), carpetBlue);
            Floor(spa, tile); Floor(bank, carpetTan);

            // walls, doors
            for (int x = 0; x <= 41; x++) { Wall(x, 0); Wall(x, 21); Wall(x, 11); Wall(x, 14); }
            for (int z = 0; z <= 21; z++) { Wall(0, z); Wall(41, z); }
            for (int z = 1; z <= 10; z++) { Wall(14, z); Wall(27, z); }
            foreach (var x in new[] { 6, 12, 18, 29 }) for (int z = 15; z <= 20; z++) Wall(x, z);
            foreach (var (x, z) in new[] { (20, 0), (14, 5), (27, 5), (20, 11), (3, 14), (9, 14), (15, 14), (23, 14), (35, 14) })
            {
                foreach (var th in P(x, z).GetThingList(map).Where(q => q.def == ThingDefOf.Wall).ToList()) th.Destroy();
                DoorAt(x, z);
            }
            // roof over everything
            foreach (var c in R(0, 0, 41, 21)) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            // coolers in the outer walls (cold side inwards): a hot day must not send the guests home with heatstroke
            foreach (var x in new[] { 4, 10, 16, 24, 31, 37 }) Put("Cooler", x, 0, Rot4.North, null, true);      // front rooms, cold side north
            foreach (var x in new[] { 3, 9, 15, 23, 35 }) Put("Cooler", x, 21, Rot4.South, null, true);          // guest rooms, spa, bank, cold side south
            Put("Cooler", 0, 12, Rot4.East, null, true);                                                         // corridor (east end: batteries outside)

            // ---- lobby
            var desk = Put("HH_FrontDesk", 20, 8, Rot4.North, ThingDefOf.WoodLog);
            var lobbySign = Put("HH_VenueSign", 17, 8, Rot4.South, ThingDefOf.WoodLog);
            SetSign(lobbySign, "HH_Venue_Tavern", "The Harbor Light");
            Table(17, 4); Table(23, 4);
            Put("Armchair", 16, 7, Rot4.South); Put("Armchair", 25, 7, Rot4.South);
            Put("EndTable", 16, 8); Put("EndTable", 25, 8);
            Lamp(15, 10); Lamp(26, 10); Lamp(15, 1); Lamp(26, 1); Lamp(19, 10); Lamp(22, 10);
            Pot(16, 1); Pot(25, 1); Pot(18, 1); Pot(23, 1); Pot(15, 5); Pot(26, 5);
            Put("SculptureLarge", 20, 10, null, D("BlocksMarble") ?? D("BlocksSandstone"));
            Put("Shelf", 24, 10, null, ThingDefOf.WoodLog);

            // ---- restaurant (stations face north-rot so their interaction cell is the aisle at z=9, not the wall)
            var restSign = Put("HH_VenueSign", 12, 6, Rot4.South, ThingDefOf.WoodLog); SetSign(restSign, "HH_Venue_Restaurant", "Hearth Kitchen");
            Put("HH_Station_PrepCounter", 1, 10, Rot4.North); Put("HH_Station_StockPot", 3, 10, Rot4.North); Put("HH_Station_Grill", 4, 10, Rot4.North);
            Put("HH_Station_Oven", 6, 10, Rot4.North); Put("HH_Station_WokStation", 8, 10, Rot4.North); Put("HH_Station_DeepFryer", 9, 10, Rot4.North);
            Put("HH_Station_Smoker", 10, 10, Rot4.North); Put("HH_Station_Steamer", 11, 10, Rot4.North); Put("HH_Station_ColdStation", 12, 10, Rot4.North);
            Put("HH_Station_DoughBench", 1, 7, Rot4.West);
            Table(3, 3, 4); Table(8, 3, 4); Table(3, 6); Table(8, 6);
            Put("HH_ShopCounter", 12, 3, Rot4.West);
            var shelf = Put("Shelf", 12, 8, null, ThingDefOf.WoodLog);   // 2 wide: x 12-13 (13-14 would replace the wall)
            Lamp(1, 1); Lamp(13, 1); Lamp(13, 10 - 0 - 0 > 0 ? 7 : 7);  Pot(6, 1); Pot(7, 1);
            PlacePlates();

            // ---- casino
            var casSign = Put("HH_VenueSign", 29, 1, Rot4.South, ThingDefOf.WoodLog); SetSign(casSign, "HH_Venue_Casino", "The Golden Anchor");
            for (int z = 2; z <= 8; z += 2) Put("HH_SlotMachine", 40, z, Rot4.West, null, true);
            for (int x = 31; x <= 36; x += 1) if (x % 2 == 1) Put("HH_SlotMachine", x, 10, Rot4.South, null, true);
            Put("HH_RouletteTable", 30, 5, Rot4.North, null, true); Put("HH_BlackjackTable", 35, 5, Rot4.North, null, true);
            Put("HH_PokerTable", 33, 2, Rot4.North, null, true);
            Put("HH_CashierCage", 28, 9, Rot4.East, null, true);
            Lamp(28, 1); Lamp(39, 1); Lamp(39, 10); Lamp(30, 10); Lamp(33, 10); Lamp(37, 10);
            Pot(31, 1); Pot(38, 1); Put("Armchair", 38, 4, Rot4.West);

            // ---- guest rooms
            foreach (var a in new[] { 1, 7, 13 })
            {
                var bed = Put("DoubleBed", a + 1, 19, Rot4.North, null, false, QualityCategory.Excellent) as Building_Bed;
                Put("EndTable", a, 20); Put("EndTable", a + 3, 20, null, null, false);
                Put("Dresser", a + 3, 16, Rot4.West); Put("Armchair", a, 16, Rot4.East);
                Lamp(a + 4, 20); Pot(a, 19); Pot(a + 4, 15);
                if (bed != null) GuestBeds.Comp(bed)?.SetGuestBed(true, false);
            }

            // ---- spa
            var spaSign = Put("HH_VenueSign", 19, 15, Rot4.South, ThingDefOf.WoodLog); SetSign(spaSign, "HH_Venue_Spa", "Tidewater Spa");
            Put("HH_HotTub", 21, 18, Rot4.North, null, true); Put("HH_HotTub", 24, 18, Rot4.North, null, true);
            Put("HH_MassageTable", 26, 16, Rot4.North); Put("HH_Sauna", 28, 20, null);
            Lamp(19, 20); Lamp(28, 15); Pot(20, 20); Pot(27, 20); Pot(22, 16); Pot(24, 16);

            // ---- bank
            Put("SL_BankCounter", 33, 18, Rot4.South, null, true); Put("SL_ATM", 40, 17, Rot4.West, null, true); Put("SL_NightSafe", 40, 19, Rot4.North);   // not rotatable: always north
            Put("Armchair", 31, 16, Rot4.North); Put("Armchair", 32, 16, Rot4.North); Put("Armchair", 37, 16, Rot4.North);
            Lamp(30, 20); Lamp(40, 15); Pot(30, 15); Pot(38, 15); Pot(31, 20); Pot(39, 20);

            // ---- corridor decoration
            foreach (var x in new[] { 1, 8, 12, 17, 21, 26, 30, 34, 39 }) Pot(x, 12);
            foreach (var x in new[] { 5, 11, 22, 33 }) Lamp(x, 13);
            Put("Armchair", 17, 13, Rot4.South);

            // ---- front: lamps and pots, power plant
            foreach (var x in new[] { 17, 23 }) { Lamp(x, -1); }
            foreach (var x in new[] { 16, 24 }) Pot(x, -1);
            foreach (var x in new[] { 12, 28, 8, 32 }) Pot(x, -3);
            var gen1 = Put("WoodFiredGenerator", 42, 2, Rot4.North); var gen2 = Put("WoodFiredGenerator", 42, 5, Rot4.North);
            Put("Battery", 42, 8); Put("Battery", 42, 9); Put("Battery", 42, 10);
            Put("Battery", 42, 12); Put("Battery", 42, 13);
            Put("SolarGenerator", 42, 15, Rot4.North);
            Put("WoodFiredGenerator", 44, 2, Rot4.North); Put("WoodFiredGenerator", 44, 5, Rot4.North);   // the coolers need ~2.6 kW
            foreach (var th in fuelled) { var rf = th.TryGetComp<CompRefuelable>(); rf?.Refuel(rf.Props.fuelCapacity); }
            foreach (var th in powered) { var pt = th.TryGetComp<CompPowerTrader>(); if (pt != null) pt.PowerOn = true; }

            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            // the hotel opens at room temperature (the coolers keep it there; a fresh build on a hot map would start at the outdoor heat)
            var rooms = new HashSet<Room>();
            foreach (var c in R(1, 1, 40, 20)) { var rm = c.GetRoom(map); if (rm != null && !rm.PsychologicallyOutdoors && rooms.Add(rm)) rm.Temperature = 21f; }
            t.Note("rooms set to 21 C: " + rooms.Count + " (outdoors " + map.mapTemperature.OutdoorTemp.ToString("0") + " C)");
            foreach (var c in R(-2, -2, 44, 23)) map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Terrain | MapMeshFlagDefOf.Roofs | MapMeshFlagDefOf.Buildings);
            yield return 120;
            ConnectLoners();
            yield return 120;
            Venues.MarkDirty(map);
            foreach (var sg in new[] { lobbySign, restSign, casSign, spaSign })
            {
                if (sg == null) { t.Note("venue sign missing"); continue; }
                var vc = sg.TryGetComp<CompVenueSign>(); var rm = Venues.RoomOfSign(sg);
                t.Note($"sign {vc?.DisplayName}: type {vc?.type?.defName}, room {(rm == null ? "none" : rm.ID + (rm.PsychologicallyOutdoors ? " outdoors" : "") + (rm.IsDoorway ? " doorway" : "") + " cells " + rm.CellCount)}, module on {(vc?.type != null && Modules.On(vc.type.module))}");
            }
            // walls with holes make a room "outdoors" (and its venue disappear): list them
            var holes = new List<string>();
            foreach (var (x0, z0, x1, z1) in new[] { (0, 0, 41, 0), (0, 11, 41, 11), (0, 14, 41, 14), (0, 21, 41, 21), (0, 0, 0, 21), (41, 0, 41, 21), (14, 0, 14, 11), (27, 0, 27, 11) })
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
                {
                    var c = P(x, z); if (!c.InBounds(map)) continue;
                    var ed = c.GetEdifice(map);
                    if (ed == null || (ed.def.passability != Traversability.Impassable && !(ed is Building_Door))) holes.Add($"({x},{z}) {ed?.def.defName ?? "nothing"}");
                }
            if (holes.Count > 0) t.Note("holes in the walls: " + string.Join(", ", holes.Take(20)));
            t.Check(Venues.All(map).Count >= 4, "all four venues are recognised: " + string.Join(", ", Venues.All(map).Select(v => v.Comp.DisplayName)));
            int off = powered.Count(th => th.Spawned && th.TryGetComp<CompPowerTrader>() is CompPowerTrader p && !p.PowerOn);
            t.Check(powered.Count > 20, "hotel furniture spawned: " + powered.Count + " powered buildings, " + off + " without power");

            // --- staff
            var hosting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("HH_Hosting"); var serving = DefDatabase<WorkTypeDef>.GetNamedSilentFail("HH_Serving");
            int slot = 0;
            foreach (var p in cols)
            {
                try
                {
                    foreach (var w in new[] { hosting, serving, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking") })
                        if (w != null && !p.WorkTypeIsDisabled(w)) p.workSettings.SetPriority(w, 1);
                    p.Position = P(17 + slot * 2, 6); p.Notify_Teleported(false); slot++;
                    p.needs.food.CurLevelPercentage = 0.9f; p.needs.rest.CurLevelPercentage = 0.9f;
                }
                catch (Exception e) { t.Note("staff setup: " + e.Message); }
            }
            t.Check(cols.Count > 0, "colonists on the map: " + cols.Count);

            if (money)
            {
                yield return 5;
                SetupMoney();
                yield return 60;
            }
        }

        // ---------------------------------------------------------------- pieces
        public void RefuelAll()
        {
            foreach (var th in fuelled) { if (th.Spawned) { var rf = th.TryGetComp<CompRefuelable>(); if (rf != null && rf.FuelPercentOfMax < 0.5f) rf.Refuel(rf.Props.fuelCapacity); } }
            foreach (var p in map.mapPawns.FreeColonistsSpawned) { try { if (p.needs.food.CurLevelPercentage < 0.4f) p.needs.food.CurLevelPercentage = 0.8f; } catch { } }
            // presentation only: guests walking in across a hot (or freezing) map would otherwise catch heatstroke on the way and go home
            foreach (var p in map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsGuest).ToList())
                try
                {
                    foreach (var hd in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Heatstroke || h.def == HediffDefOf.Hypothermia).ToList()) p.health.RemoveHediff(hd);
                }
                catch { }
        }


        void SetSign(Thing sign, string typeDef, string name)
        {
            var comp = sign?.TryGetComp<CompVenueSignHH>(); if (comp == null) return;
            var type = DefDatabase<VenueTypeDef>.GetNamedSilentFail(typeDef);
            if (type == null) { T.Note("MISSING venue type " + typeDef); return; }
            comp.SetType(type); comp.alwaysOpen = true; comp.customName = name;
        }

        void PlacePlates()
        {
            try
            {
                var dishes = DefDatabase<DishDef>.AllDefsListForReading.Where(d => d.plateDef != null).ToList();
                var cells = new[] { P(13, 8), P(13, 9) };
                int i = 0;
                foreach (var d in dishes.Take(2))
                {
                    var th = ThingMaker.MakeThing(d.plateDef);
                    th.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
                    th.TryGetComp<CompDish>()?.SetDish(d, 0);
                    GenPlace.TryPlaceThing(th, cells[i++ % cells.Length], map, ThingPlaceMode.Near);
                }
                // a few on the dining tables
                foreach (var (x, z) in new[] { (3, 3), (8, 3), (3, 6) })
                {
                    var d = dishes.RandomElementWithFallback(); if (d == null) continue;
                    var th = ThingMaker.MakeThing(d.plateDef);
                    th.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
                    th.TryGetComp<CompDish>()?.SetDish(d, 0);
                    GenPlace.TryPlaceThing(th, P(x, z), map, ThingPlaceMode.Direct);
                }
            }
            catch (Exception e) { T.Note("plates: " + e.Message); }
        }

        /// <summary>Powered buildings that did not join the grid get a short conduit line to the nearest wall conduit.</summary>
        void ConnectLoners()
        {
            int fixedCount = 0;
            var conduit = D("PowerConduit"); if (conduit == null) return;
            foreach (var th in powered.ToList())
            {
                var pw = th.TryGetComp<CompPowerTrader>();
                if (!th.Spawned || pw == null || pw.PowerNet != null && pw.PowerNet.HasActivePowerSource) continue;
                // conduit under the building itself, then straight toward the closest wall cell
                var start = th.Position; var target = start; int best = 99;
                foreach (var c in GenRadial.RadialCellsAround(start, 12, false))
                    if (c.InBounds(map) && c.GetFirstThing(map, ThingDefOf.Wall) != null) { int d = Math.Abs(c.x - start.x) + Math.Abs(c.z - start.z); if (d < best) { best = d; target = c; } }
                var cur = start;
                var cells = new List<IntVec3> { cur };
                while (cur.x != target.x) { cur.x += Math.Sign(target.x - cur.x); cells.Add(cur); }
                while (cur.z != target.z) { cur.z += Math.Sign(target.z - cur.z); cells.Add(cur); }
                foreach (var c in cells)
                    if (c.GetFirstThing(map, conduit) == null && c.GetFirstThing(map, ThingDefOf.Wall) == null)
                    { var cd = ThingMaker.MakeThing(conduit); cd.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(cd, c, map); }
                fixedCount++;
            }
            T.Note("lone powered buildings re-wired: " + fixedCount);
            map.powerNetManager.UpdatePowerNetsAndConnections_First();
        }

        void SetupMoney()
        {
            try
            {
                var c = BankGameComp.Current;
                if (c != null)
                {
                    Ledger.Current.Post("world", Payments.ColonyTill(map), Money.FromSilver(1500), "Showcase: opening float");
                    if (!c.HasAccount) T.Check(c.OpenAccount(SL_BankDef.Default, true), "bank account opened");
                    Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(2480), "Showcase: opening deposit");
                    try { c.TryTakeLoan(300, 60, out _, out _); } catch (Exception e) { T.Note("loan: " + e.Message); }
                }
                else T.Note("no bank component");
            }
            catch (Exception e) { T.Note("bank setup failed: " + e); }
            try
            {
                var ins = InsuranceComponent.Instance;
                if (ins != null)
                {
                    // the showcase colony has the insurance research (policies are gated behind it)
                    var res0 = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("SL_Insurance");
                    if (res0 != null && !res0.IsFinished) Find.ResearchManager.FinishProject(res0, doCompletionDialog: false);
                    var col = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    foreach (var (name, tier) in new[] { ("SL_RoofOverHead", CoverTier.GoodEnough), ("SL_RimHealth", CoverTier.GoodEnough), ("SL_LegalShield", CoverTier.BareMinimum) })
                    {
                        var def = DefDatabase<SL_PolicyDef>.GetNamedSilentFail(name); if (def == null) { T.Note("MISSING policy " + name); continue; }
                        Pawn holder = def.kind == PolicyKind.Person ? col : null;
                        InsurancePricing.SumRange(def, map, holder, out double min, out double max);
                        var res = ins.Sign(def, tier, holder, min + (max - min) * 0.4, null, null);
                        T.Note("policy " + name + ": " + (res.Ok ? "signed" : "refused: " + res.error));
                    }
                    T.Check(ins.Active.Any(), "at least one insurance policy signed");
                }
            }
            catch (Exception e) { T.Note("insurance setup failed: " + e); }
        }
    }
}
#endif
