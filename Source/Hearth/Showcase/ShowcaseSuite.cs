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

        Map map; TestContext T; IntVec3 O;
        readonly List<Thing> powered = new List<Thing>();
        readonly List<Thing> fuelled = new List<Thing>();
        readonly List<string> shots = new List<string>();
        readonly List<(string file, string name)> renames = new List<(string, string)>();
        CellRect casino, lobby, restaurant, spa, bank;
        ThingDef wallStuff, doorStuff;
        object shotMode; FieldInfo shotField;

        // ---------------------------------------------------------------- helpers
        IntVec3 P(int x, int z) => new IntVec3(O.x + x, 0, O.z + z);
        CellRect R(int x0, int z0, int x1, int z1) => new CellRect(O.x + x0, O.z + z0, x1 - x0 + 1, z1 - z0 + 1);

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
            int w = r.IsHorizontal ? def.size.z : def.size.x, h = r.IsHorizontal ? def.size.x : def.size.z;
            var center = new IntVec3(O.x + x + (w - 1) / 2, 0, O.z + z + (h - 1) / 2);
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
            T = t; map = t.Map;
            try { if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true); } catch { }
            Tidy();
            HookScreenshotMode();
            try { Find.CameraDriver.config.dollyRateKeys = 0; } catch { }

            // --- noon, clear weather
            try
            {
                float h = GenLocalDate.HourFloat(map);
                int delta = (int)(((11f - h + 24f) % 24f) * 2500f);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + delta);
                map.weatherManager.TransitionTo(WeatherDefOf.Clear);
                map.weatherManager.curWeatherAge = 0;
            }
            catch (Exception e) { t.Note("time/weather: " + e.Message); }

            // --- site
            var cols = map.mapPawns.FreeColonistsSpawned.ToList();
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

            // ---- restaurant
            var restSign = Put("HH_VenueSign", 12, 6, Rot4.South, ThingDefOf.WoodLog); SetSign(restSign, "HH_Venue_Restaurant", "Hearth Kitchen");
            Put("HH_Station_PrepCounter", 1, 10, Rot4.South); Put("HH_Station_StockPot", 3, 10, Rot4.South); Put("HH_Station_Grill", 4, 10, Rot4.South);
            Put("HH_Station_Oven", 6, 10, Rot4.South); Put("HH_Station_WokStation", 8, 10, Rot4.South); Put("HH_Station_DeepFryer", 9, 10, Rot4.South);
            Put("HH_Station_Smoker", 10, 10, Rot4.South); Put("HH_Station_Steamer", 11, 10, Rot4.South); Put("HH_Station_ColdStation", 12, 10, Rot4.South);
            Put("HH_Station_DoughBench", 1, 8, Rot4.East);
            Table(3, 3, 4); Table(8, 3, 4); Table(3, 6); Table(8, 6);
            Put("HH_ShopCounter", 12, 3, Rot4.West);
            var shelf = Put("Shelf", 13, 8, null, ThingDefOf.WoodLog);
            Lamp(1, 1); Lamp(13, 1); Lamp(13, 10 - 0 - 0 > 0 ? 7 : 7); Pot(1, 6); Pot(1, 5); Pot(6, 1); Pot(7, 1);
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
            Put("SL_BankCounter", 33, 18, Rot4.South, null, true); Put("SL_ATM", 40, 17, Rot4.West, null, true); Put("SL_NightSafe", 40, 19, Rot4.West);
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
            foreach (var th in fuelled) { var rf = th.TryGetComp<CompRefuelable>(); rf?.Refuel(rf.Props.fuelCapacity); }
            foreach (var th in powered) { var pt = th.TryGetComp<CompPowerTrader>(); if (pt != null) pt.PowerOn = true; }

            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            foreach (var c in R(-2, -2, 44, 23)) map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Terrain | MapMeshFlagDefOf.Roofs | MapMeshFlagDefOf.Buildings);
            yield return 120;
            ConnectLoners();
            yield return 120;
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

            // --- money: bank account and insurance
            yield return 5;
            SetupMoney();
            yield return 60;

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
            var guest = guests.FirstOrDefault(p => p.Spawned && !p.Dead && reg.Get(p)?.CheckedIn == true) ?? guests.FirstOrDefault(p => p.Spawned && !p.Dead);
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

        // ---------------------------------------------------------------- pieces
        void RefuelAll()
        {
            foreach (var th in fuelled) { if (th.Spawned) { var rf = th.TryGetComp<CompRefuelable>(); if (rf != null && rf.FuelPercentOfMax < 0.5f) rf.Refuel(rf.Props.fuelCapacity); } }
            foreach (var p in map.mapPawns.FreeColonistsSpawned) { try { if (p.needs.food.CurLevelPercentage < 0.4f) p.needs.food.CurLevelPercentage = 0.8f; } catch { } }
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
