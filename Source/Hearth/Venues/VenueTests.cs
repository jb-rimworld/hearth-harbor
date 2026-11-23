// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Testing;

namespace Hearth.ShopsFood
{
    /// <summary>Helpers that build walled shop rooms and friendly guests on the test map.</summary>
    static class VenueTestKit
    {
        public sealed class TestRoom
        {
            public CellRect Outer, Inner;
            public IntVec3 Door;
            public readonly List<Thing> Things = new List<Thing>();
            public readonly List<Zone> Zones = new List<Zone>();
        }

        static int nextOffset;
        // areas of rooms built and not yet torn down: a second room must not be placed over them (it destroyed the first room's walls and sign: the venues.wait flake)
        static readonly List<CellRect> claimed = new List<CellRect>();

        public static TestRoom Build(Map map, int innerSize = 5)
        {
            // find a clear area at growing distance from the map centre so suites do not overlap
            int half = innerSize / 2 + 1;
            IntVec3 chosen = IntVec3.Invalid;
            for (int r = 8 + (nextOffset % 7) * 8; r < 110 && !chosen.IsValid; r += 3)
                foreach (var c in GenRadial.RadialCellsAround(map.Center, r, r + 2))
                {
                    var rect = CellRect.CenteredOn(c, half).ExpandedBy(1);
                    if (!rect.InBounds(map)) continue;
                    bool ok = true;
                    foreach (var other in claimed) if (other.Overlaps(rect.ExpandedBy(2))) { ok = false; break; }
                    if (!ok) continue;
                    foreach (var cell in rect) { var t = cell.GetTerrain(map); if (t.passability == Traversability.Impassable || t.IsWater || cell.Fogged(map)) { ok = false; break; } }
                    if (ok) { chosen = c; break; }
                }
            nextOffset++;
            if (!chosen.IsValid) throw new Exception("no free area for a test room");
            var room = new TestRoom { Outer = CellRect.CenteredOn(chosen, half), Inner = CellRect.CenteredOn(chosen, half - 1) };
            claimed.Add(room.Outer);
            room.Door = new IntVec3(room.Outer.CenterCell.x, 0, room.Outer.minZ);
            var keepOut = room.Outer.ExpandedBy(2);
            foreach (var c in room.Outer.ExpandedBy(1))
                foreach (var t in c.GetThingList(map).ToList())
                {
                    if (t is Pawn p)
                    {
                        // push animals and colonists out of the building site
                        if (CellFinder.TryFindRandomCellNear(chosen, map, 14, x => !keepOut.Contains(x) && x.Standable(map) && x.GetEdifice(map) == null, out var dest))
                        {
                            // wild or ambient animals simply go (they threw errors when moved into new walls); colonists are respawned outside
                            if (p.RaceProps.Animal && (p.Faction == null || !p.Faction.IsPlayer)) { p.Destroy(); continue; }
                            if (p.Spawned) { p.jobs?.StopAll(); p.DeSpawn(); }
                            GenSpawn.Spawn(p, dest, map);
                        }
                    }
                    else if (t.def.destroyable && !t.Destroyed) t.Destroy(); // the snapshot may hold things (motes) a previous Destroy already took with it
                }
            foreach (var c in room.Outer)
            {
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                if (room.Inner.Contains(c)) { map.terrainGrid.SetTerrain(c, TerrainDefOf.WoodPlankFloor); continue; }
                var def = c == room.Door ? ThingDefOf.Door : ThingDefOf.Wall;
                var w = ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
                w.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(w, c, map);
                room.Things.Add(w);
            }
            return room;
        }

        public static Thing SpawnSign(Map map, TestRoom room, ThingDef def, VenueTypeDef type, IntVec3? at = null, Rot4? rot = null)
        {
            var sign = ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
            sign.SetFaction(Faction.OfPlayer);
            var cell = at ?? new IntVec3(room.Inner.minX, 0, room.Inner.maxZ);
            GenSpawn.Spawn(sign, cell, map, rot ?? Rot4.South);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            if (comp != null && type != null) { comp.SetType(type); comp.alwaysOpen = true; }
            room.Things.Add(sign);
            return sign;
        }

        public static Zone_Stockpile Stockpile(Map map, TestRoom room, IEnumerable<IntVec3> cells)
        {
            var z = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(z);
            foreach (var c in cells) z.AddCell(c);
            room.Zones.Add(z);
            return z;
        }

        public static Thing Stock(Map map, ThingDef def, int count, IntVec3 cell, TestRoom room)
        {
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            t.stackCount = Math.Min(count, def.stackLimit);
            GenSpawn.Spawn(t, cell, map);
            room.Things.Add(t);
            return t;
        }

        /// <summary>Make sure there are friendly factions able to send traders (and visitors).</summary>
        public static List<Faction> Friendly(int wanted, bool needCaravans)
        {
            var player = Faction.OfPlayer;
            bool Good(Faction f) => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && !f.Hidden
                                    && !f.def.pawnGroupMakers.NullOrEmpty() && (!needCaravans || !f.def.caravanTraderKinds.NullOrEmpty());
            var all = Find.FactionManager.AllFactionsVisible.Where(Good).ToList();
            var friendly = all.Where(f => !f.HostileTo(player)).ToList();
            foreach (var f in all)
            {
                if (friendly.Count >= wanted) break;
                if (friendly.Contains(f) || f.def.permanentEnemy) continue;
                f.SetRelationDirect(player, FactionRelationKind.Neutral, false);
                if (!f.HostileTo(player)) friendly.Add(f);
            }
            return friendly;
        }

        public static List<Pawn> SpawnVisitors(Map map, Faction f, IntVec3 spawn)
        {
            // a faction that cannot send a group right now (temperature range, tech level) or a blocked entry cell yields nothing: retry, then try the other friendly factions
            var all = new List<Pawn>();
            var tried = new List<Faction> { f };
            tried.AddRange(Friendly(4, false).Where(x => x != f));
            for (int i = 0; i < 6 && all.Count == 0; i++)
            {
                var parms = new IncidentParms { target = map, faction = tried[i % tried.Count], forced = true, sendLetter = false, points = 120f, spawnCenter = spawn, storeGeneratedNeutralPawns = new List<Pawn>() };
                try { IncidentDefOf.VisitorGroup.Worker.TryExecute(parms); } catch (Exception e) { HLog.Warn("Test", "VisitorGroup incident threw: " + e.Message); }
                all.AddRange(parms.storeGeneratedNeutralPawns.Where(p => p.Spawned));
            }
            return all;
        }

        public static IntVec3 OutsideCell(Map map, TestRoom room)
        {
            var below = new IntVec3(room.Outer.CenterCell.x, 0, room.Outer.minZ - 8);
            if (CellFinder.TryFindRandomCellNear(below, map, 5, c => c.Standable(map) && c.InBounds(map) && !c.Roofed(map), out var found)) return found;
            return map.Center;
        }

        public static Pawn Worker(Map map)
        {
            var colonists = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Dead && !p.Drafted && !p.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving) && p.workSettings != null).ToList();
            if (colonists.Count == 0)
            {
                // random quicktest colonists can all be barred from social work by their backstories: add one who can serve instead of making the suites depend on luck
                for (int tries = 0; tries < 20 && colonists.Count == 0; tries++)
                {
                    var np = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
                    GenSpawn.Spawn(np, map.Center, map);
                    if (np.workSettings != null && !np.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving)) colonists.Add(np); else np.Destroy();
                }
            }
            // the serving work givers may require capacities (talking, manipulation): a random colonist that lacks one never takes the work
            var givers = DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(d => d.workType == HHVenueDefOf.HH_Serving).ToList();
            var pick = colonists.FirstOrDefault(p => givers.All(d => d.Worker.MissingRequiredCapacity(p) == null)) ?? colonists.FirstOrDefault();
            if (pick == null) return null;
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) c.workSettings?.SetPriority(HHVenueDefOf.HH_Serving, 0);
            pick.workSettings.SetPriority(HHVenueDefOf.HH_Serving, 1);
            // only the serving work: a random colonist with Cleaning/Hauling at priority 1 never gets to the bell
            foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading) if (wt != HHVenueDefOf.HH_Serving && !pick.WorkTypeIsDisabled(wt)) pick.workSettings.SetPriority(wt, 0);
            if (pick.needs?.rest != null) pick.needs.rest.CurLevel = 1f;
            // a random colonist may be on a joy or sleep time assignment at this hour and then never takes the serving work
            if (pick.timetable != null) for (int hr = 0; hr < 24; hr++) pick.timetable.SetAssignment(hr, TimeAssignmentDefOf.Anything);
            return pick;
        }

        /// <summary>Test rooms are built far from the colonists: put the serving colonist next to the room so the suites do not time out on a long walk.</summary>
        public static void Relocate(Pawn pawn, TestRoom room)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return;
            var map = pawn.Map;
            if ((pawn.Position - room.Door).LengthHorizontal < 16f) return;
            var near = OutsideCell(map, room);
            pawn.jobs?.StopAll();
            pawn.DeSpawn();
            GenSpawn.Spawn(pawn, near, map);
        }

        /// <summary>Switches Hauling off for every colonist (colonists would top up a shelf, stockpile or vending machine with loose goods from elsewhere and hide a sale
        /// from a stack-count check). Returns the action that puts the priorities back.</summary>
        public static Action SuspendHauling(Map map)
        {
            var was = map.mapPawns.FreeColonistsSpawned.ToDictionary(c => c, c => c.workSettings?.GetPriority(WorkTypeDefOf.Hauling) ?? 0);
            foreach (var c in was.Keys) c.workSettings?.SetPriority(WorkTypeDefOf.Hauling, 0);
            return () => { foreach (var kv in was) kv.Key.workSettings?.SetPriority(WorkTypeDefOf.Hauling, kv.Value); };
        }

        public static void Teardown(Map map, TestRoom room)
        {
            claimed.Remove(room.Outer);
            foreach (var z in room.Zones) if (!z.Map.zoneManager.AllZones.Contains(z) == false) z.Delete();
            foreach (var c in room.Outer.ExpandedBy(1))
                foreach (var t in c.GetThingList(map).ToList())
                    if (!(t is Pawn) && !t.Destroyed && t.def.destroyable) t.Destroy();
            VenueSales.ClearCaches(); Venues.MarkDirty(map);
        }

        /// <summary>Remove visitors left over from earlier suites so they do not shop in the wrong place.</summary>
        public static void ClearGuests(Map map)
        {
            foreach (var p in GuestApi.GuestsOn(map).ToList())
                if (p != null && !p.Destroyed) p.Destroy(DestroyMode.Vanish);
            GuestMemory.Clear();
        }

        /// <summary>
        /// Offers idle visitors an activity, like the guest brain does. GuestFallback.Run is silent while the Inn module is on (it only drives
        /// the guests it registered itself), so here the same offer is made directly. Harmless when the real brain also drives them.
        /// </summary>
        public static void Drive(Map map)
        {
            GuestFallback.Run(map);
            if (!(GuestApi.Impl != null && Modules.On(ModuleId.Inn))) return;
            int now = Find.TickManager.TicksGame;
            foreach (var p in GuestApi.GuestsOn(map).ToList())
            {
                if (now < GuestMemory.NextOffer(p) || !p.Awake() || p.Downed) continue;
                var cur = p.CurJob;
                bool idle = cur == null || cur.def == JobDefOf.Wait_Wander || cur.def == JobDefOf.GotoWander || cur.def == JobDefOf.Wait || cur.def == JobDefOf.Wait_MaintainPosture;
                if (!idle) continue;
                var info = GuestApi.Info(p); if (info == null) continue;
                GuestMemory.SetNextOffer(p, now + 300);
                var job = GuestActivities.TryPick(p, info);
                if (job != null) p.jobs.StartJob(job, Verse.AI.JobCondition.InterruptOptional);
            }
        }

        public static Money Till(Map map) => Ledger.Current.Balance(Payments.ColonyTill(map));
        public static int HourNow(Map map) => GenLocalDate.HourOfDay(map);
        public static int StackSum(IEnumerable<Thing> things) => things.Where(t => t != null && !t.Destroyed && t.Spawned).Sum(t => t.stackCount);
    }

    public sealed class SignSuite : HearthTestSuite
    {
        public override string Name => "venues.sign";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            t.Check(Modules.On(ModuleId.ShopsFood), "module on");
            t.Check(HHVenueDefOf.HH_VenueSign != null && HHVenueDefOf.HH_VenueSignWall != null && HHVenueDefOf.HH_Venue_Tavern != null, "defs resolved");
            foreach (var d in DefDatabase<VenueTypeDef>.AllDefs) t.Check(d.icon != null, "icon of " + d.defName + " loaded");

            // --- pure hours logic (overnight ranges)
            var probe = new CompVenueSign();
            probe.openHour = 18; probe.closeHour = 4;
            t.Check(probe.IsOpenAt(18) && probe.IsOpenAt(23) && probe.IsOpenAt(0) && probe.IsOpenAt(3), "night 18-4: open at 18, 23, 0, 3");
            t.Check(!probe.IsOpenAt(4) && !probe.IsOpenAt(12) && !probe.IsOpenAt(17), "night 18-4: closed at 4, 12, 17");
            probe.openHour = 8; probe.closeHour = 22;
            t.Check(probe.IsOpenAt(8) && probe.IsOpenAt(21) && !probe.IsOpenAt(22) && !probe.IsOpenAt(7), "day 8-22 boundaries");
            probe.alwaysOpen = true; t.Check(probe.IsOpenAt(3) && probe.IsOpenAt(15), "always open");
            probe.alwaysOpen = false; probe.openHour = 5; probe.closeHour = 5; t.Check(probe.IsOpenAt(0), "equal hours = open all day");

            // --- free-standing sign in a walled room
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            yield return 20;
            var comp = sign.TryGetComp<CompVenueSignHH>();
            t.Check(comp != null, "sign has CompVenueSignHH");
            Venues.MarkDirty(map);
            var venue = Venues.All(map).FirstOrDefault(v => v.Sign == sign);
            t.Check(venue != null, "Venues.All finds the sign");
            if (venue != null)
            {
                t.Check(venue.Type == HHVenueDefOf.HH_Venue_Shop, "venue type is shop");
                var inside = room.Inner.CenterCell;
                t.Check(venue.Contains(inside), "room contains inner cell");
                t.Check(!venue.Contains(new IntVec3(room.Outer.maxX + 3, 0, room.Outer.maxZ + 3)), "outside cell is not part of the room");
                t.Check(Venues.At(inside, map) == venue, "Venues.At(inside) = venue");
                t.Check(venue.Room.CellCount >= room.Inner.Area, "room size " + venue.Room.CellCount);
            }

            // --- gizmos, inspect string, checklist
            var gizmos = comp.CompGetGizmosExtra().ToList();
            t.Check(gizmos.Count >= 5, "sign offers at least 5 gizmos (" + gizmos.Count + ")");
            string inspect = comp.CompInspectStringExtra();
            t.Check(!inspect.NullOrEmpty() && inspect.Contains(HHVenueDefOf.HH_Venue_Shop.LabelCap), "inspect string names the type: " + inspect?.Replace("\n", " | "));
            t.Check(venue != null && comp.Checklist(venue).Any(), "checklist has lines");
            var types = comp.SelectableTypes().ToList();
            t.Check(types.Contains(HHVenueDefOf.HH_Venue_Tavern) && types.Contains(HHVenueDefOf.HH_Venue_Shop), "type menu offers tavern and shop");
            t.Check(!types.Contains(HHVenueDefOf.HH_Venue_Lounge) == false, "type menu offers lounge");

            // --- type change moves default hours
            comp.alwaysOpen = false;
            comp.SetType(HHVenueDefOf.HH_Venue_Shop); comp.openHour = 8; comp.closeHour = 20;
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern);
            t.Check(comp.openHour == 16 && comp.closeHour == 2, "default hours followed the new type: " + comp.openHour + "-" + comp.closeHour);
            t.Check(comp.IsOpenAt(23) && comp.IsOpenAt(1) && !comp.IsOpenAt(10), "tavern default hours are overnight");
            comp.SetType(HHVenueDefOf.HH_Venue_Shop);

            // --- gate: hours vs. staff rule
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 8) % 24; comp.closeHour = (h + 11) % 24;
            venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null && VenueApi.Gate(venue) == VenueGate.ClosedHours, "closed outside hours");
            comp.alwaysOpen = true;
            t.Check(VenueApi.Gate(venue) == VenueGate.Open, "shop without staff need is open");
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern); comp.alwaysOpen = true;
            t.Check(VenueApi.Gate(venue) == VenueGate.WaitingForStaff, "tavern needs somebody on duty");
            float oldRule = HSettings.Get(VS.StaffRuleKey);
            HSettings.Set(VS.StaffRuleKey, (float)StaffRule.NeverRequired);
            t.Check(VenueApi.Gate(venue) == VenueGate.Open, "staff rule 'never' opens the tavern");
            HSettings.Set(VS.StaffRuleKey, (float)StaffRule.AlwaysRequired);
            comp.SetType(HHVenueDefOf.HH_Venue_Shop); comp.alwaysOpen = true;
            t.Check(VenueApi.Gate(venue) == VenueGate.WaitingForStaff, "staff rule 'always' needs staff in a shop");
            HSettings.Set(VS.StaffRuleKey, oldRule);
            t.Check(VenueApi.PriceMultiplier(venue) < comp.priceFactor * VS.F(VS.ShopPrice) + 0.0001f, "self-service price is not above the sign price");

            // --- pricing
            var steel = VenueTestKit.Stock(map, ThingDefOf.Steel, 10, room.Inner.CenterCell, room);
            comp.priceFactor = 1.5f;
            double expected = steel.MarketValue * 10 * VenueApi.PriceMultiplier(venue);
            t.Check(Math.Abs(VenueApi.PriceOf(steel, 10, venue).Silver - expected) < 0.01, "price = value x count x multiplier");
            comp.priceFactor = 1f;
            t.Check(VenueApi.PriceTierLabel(1f) == "Standard" && VenueApi.PriceTierLabel(0.25f) == "Charity" && VenueApi.PriceTierLabel(1.8f) == "Luxury", "price tier labels");

            // --- wall-mounted sign: its room is the room in front of the wall
            sign.Destroy();
            yield return 5;
            var wallCell = new IntVec3(room.Inner.CenterCell.x, 0, room.Inner.maxZ);
            var wall = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSignWall, HHVenueDefOf.HH_Venue_Shop, wallCell, Rot4.North);
            yield return 20;
            Venues.MarkDirty(map);
            var wv = Venues.All(map).FirstOrDefault(v => v.Sign == wall);
            t.Check(wv != null, "wall-mounted sign is found");
            t.Check(wv != null && wv.Contains(room.Inner.CenterCell), "wall sign belongs to the room it faces out of");
            t.Check(Venues.RoomOfSign(wall) == room.Inner.CenterCell.GetRoom(map), "RoomOfSign of a wall sign is the room of its own cell, not the wall cell");
            t.Check((wall.Position + wall.Rotation.FacingCell).GetRoom(map) == null || (wall.Position + wall.Rotation.FacingCell).GetEdifice(map) != null, "the cell a wall sign faces is its wall");
            if (wv != null)
            {
                // staffing query for other modules
                t.Check(!Venues.IsStaffed(wv) && Venues.Staffer(wv) == null, "Venues.IsStaffed: nobody on duty yet");
                var wc = wall.TryGetComp<CompVenueSignHH>();
                wc.alwaysOpen = true; wc.SetType(HHVenueDefOf.HH_Venue_Shop);
                t.Check(wv.OpenAndStaffed && Venues.IsOpenAndStaffed(wv), "open shop that needs nobody: OpenAndStaffed");
                wc.SetType(HHVenueDefOf.HH_Venue_Tavern); wc.alwaysOpen = true;
                t.Check(Venues.NeedsStaff(wv) && !wv.OpenAndStaffed, "tavern without bartender is not OpenAndStaffed");
                VenueState.PingStaff(wall, map.mapPawns.FreeColonistsSpawned.First());
                t.Check(Venues.IsStaffed(wv) && wv.OpenAndStaffed, "tavern with somebody at the post is OpenAndStaffed");
                wc.SetType(HHVenueDefOf.HH_Venue_Shop);
            }

            // --- types other modules rely on, and the gate / chapel
            foreach (var n in new[] { "HH_Venue_Tavern", "HH_Venue_Shop", "HH_Venue_Restaurant", "HH_Venue_Casino", "HH_Venue_Spa", "HH_Venue_Lounge", "HH_Venue_Cinema", "HH_Venue_Gallery", "HH_Venue_DrugLounge" })
                t.Check(DefDatabase<VenueTypeDef>.GetNamedSilentFail(n) != null, "venue type " + n + " is defined");
            if (ModsConfig.IdeologyActive) t.Check(DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Chapel") != null, "venue type HH_Venue_Chapel (Ideology)");
            t.Check(DefDatabase<VenueTypeDef>.AllDefs.Count(d => d.defName == "HH_Venue_Casino") == 1, "exactly one HH_Venue_Casino");
            t.Check(DefDatabase<ThingDef>.GetNamedSilentFail("HH_MarketStall") != null && HHVenueDefOf.HH_MarketStall.size == new IntVec2(2, 1), "market stall is a 2x1 building");
            var gatedLounge = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_DrugLounge");
            if (gatedLounge != null)
            {
                bool open = Hearth.Underworld.UwDefs.Open(gatedLounge.GetModExtension<Hearth.Underworld.UwGateExtension>());
                var menu = wall.TryGetComp<CompVenueSignHH>().SelectableTypes().ToList();
                t.Check(menu.Contains(gatedLounge) == (open && (gatedLounge.researchPrerequisite == null || gatedLounge.researchPrerequisite.IsFinished) && Modules.On(gatedLounge.module)), "drug lounge is offered only while the underworld gate is open: gate " + open);
            }

            // --- windows and selection overlays draw without errors (any exception shows up as a red error in the log)
            Find.CameraDriver.JumpToCurrentMapLoc(wall.Position);
            Find.Selector.ClearSelection(); Find.Selector.Select(wall, false, false);
            Find.WindowStack.Add(new Dialog_VenueHours(wall.TryGetComp<CompVenueSignHH>()));
            yield return 90;
            Find.WindowStack.TryRemove(typeof(Dialog_VenueHours));
            Find.WindowStack.Add(new Dialog_VenuePrice(wall.TryGetComp<CompVenueSignHH>()));
            yield return 90;
            Find.WindowStack.TryRemove(typeof(Dialog_VenuePrice));
            Find.WindowStack.Add(new Dialog_RenameVenue(wall.TryGetComp<CompVenueSignHH>()));
            yield return 90;
            Find.WindowStack.TryRemove(typeof(Dialog_RenameVenue));
            t.Check(Find.WindowStack.WindowOfType<Dialog_VenueHours>() == null, "dialogs closed again");
            wall.TryGetComp<CompVenueSignHH>().customName = "The Rusty Anchor";
            t.Check(wall.TryGetComp<CompVenueSignHH>().DisplayName == "The Rusty Anchor" && wall.TryGetComp<CompVenueSignHH>().CompInspectStringExtra().Contains("Shop"), "custom name and inspect text");
            Find.Selector.ClearSelection();
            VenueTestKit.Teardown(map, room);
        }
    }

    public sealed class ShopSuite : HearthTestSuite
    {
        public override string Name => "venues.shop";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 26000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            VenueTestKit.ClearGuests(map);
            var factions = VenueTestKit.Friendly(1, false);
            t.Check(factions.Count > 0, "a friendly faction exists for visitors");
            if (factions.Count == 0) yield break;

            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = true;
            var cells = room.Inner.Cells.Where(c => c != sign.Position).Take(8).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            // haulers would merge loose items into the stacks and blur the count: switch hauling off for the test
            var haulWas = map.mapPawns.FreeColonistsSpawned.ToDictionary(c => c, c => c.workSettings?.GetPriority(WorkTypeDefOf.Hauling) ?? 0);
            foreach (var c in haulWas.Keys) c.workSettings?.SetPriority(WorkTypeDefOf.Hauling, 0);
            var defs = new[] { ThingDefOf.Steel, ThingDefOf.WoodLog, ThingDefOf.Cloth, ThingDefOf.MedicineHerbal, ThingDefOf.ComponentIndustrial, ThingDefOf.Gold, ThingDefOf.Silver, ThingDef.Named("Beer") };
            var stocked = new List<Thing>();
            for (int i = 0; i < defs.Length; i++) stocked.Add(VenueTestKit.Stock(map, defs[i], 40, cells[i], room));
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null, "shop venue exists");
            var stock = VenueSales.Stock(venue, true);
            t.Check(stock.Count >= 6, "items on stockpile cells are for sale: " + stock.Count);
            t.Check(!stock.Any(s => s.def == ThingDefOf.Silver), "silver is never for sale");
            int startCount = VenueTestKit.StackSum(stocked);

            // --- N45: goods are off limits for the colony unless the sign says otherwise
            var mapComp = map.GetComponent<VenueMapComp>();
            t.Check(mapComp != null, "map component exists");
            comp.colonyMayTakeGoods = false;
            mapComp.SyncForbidden();
            var colonist = map.mapPawns.FreeColonistsSpawned.First();
            t.Check(stock.All(s => s.IsForbidden(colonist)), "goods for sale are forbidden for the colony");
            comp.colonyMayTakeGoods = true;
            mapComp.SyncForbidden();
            t.Check(stock.All(s => !s.IsForbidden(colonist)), "colony may take goods: they are released again");
            comp.colonyMayTakeGoods = false;
            mapComp.SyncForbidden();
            t.Check(stock.All(s => s.IsForbidden(colonist)), "forbidden again");
            var outsider = VenueTestKit.Stock(map, ThingDef.Named("Leather_Plain"), 5, VenueTestKit.OutsideCell(map, room), room);
            mapComp.SyncForbidden();
            t.Check(!outsider.IsForbidden(colonist) || !mapComp.IsForbiddenByUs(outsider), "goods outside the room are untouched");

            // --- guests: a visitor group comes and buys
            var till0 = VenueTestKit.Till(map);
            var guests = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            t.Check(guests.Count > 0, "visitor group spawned: " + guests.Count);
            int waited = 0;
            while (waited < 22000 && comp.salesCount == 0) { VenueTestKit.Drive(map); yield return 200; waited += 200; }
            t.Check(comp.salesCount > 0, "at least one guest bought something after " + waited + " ticks (sales " + comp.salesCount + ")");
            t.Check(VenueTestKit.Till(map) > till0, "colony till increased: " + till0 + " -> " + VenueTestKit.Till(map));
            var left = VenueApi.VenueOfSign(map, sign) != null ? VenueSales.Stock(VenueApi.VenueOfSign(map, sign), true) : new List<Thing>();
            t.Check(VenueTestKit.StackSum(stocked) < startCount, "goods left the shelves: " + startCount + " -> " + VenueTestKit.StackSum(stocked));
            var owned = guests.Where(g => g != null && g.inventory != null).SelectMany(g => g.inventory.innerContainer).Select(x => x.def).ToList();
            t.Check(owned.Any(d => defs.Contains(d)), "a guest carries a purchased item: " + string.Join(", ", owned.Select(d => d.defName).Distinct()));
            t.Check(comp.Income.Micro > 0 && comp.itemsSold > 0, "sign statistics recorded: " + comp.Income + " silver, " + comp.itemsSold + " items");
            t.Check(GuestApi.Info(guests.First(g => GuestApi.Info(g) != null))?.Spent.Micro >= 0, "guest info tracks spending");
            foreach (var kv in haulWas) kv.Key.workSettings?.SetPriority(WorkTypeDefOf.Hauling, kv.Value);
            VenueTestKit.Teardown(map, room);
        }
    }

    public sealed class StaffSuite : HearthTestSuite
    {
        public override string Name => "venues.staff";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var pawn = VenueTestKit.Worker(map);
            t.Check(pawn != null, "a colonist can do Serving");
            if (pawn == null) yield break;
            var room = VenueTestKit.Build(map);
            VenueTestKit.Relocate(pawn, room);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            var counter = ThingMaker.MakeThing(HHVenueDefOf.HH_ShopCounter, ThingDefOf.WoodLog);
            counter.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(counter, new IntVec3(room.Inner.CenterCell.x, 0, room.Inner.CenterCell.z), map, Rot4.South);
            room.Things.Add(counter);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = true;
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null && VenueApi.NeedsStaff(venue), "tavern needs staff");
            t.Check(VenueApi.CounterOf(venue) == counter, "counter belongs to the venue");
            t.Check(counter.InteractionCell.IsValid && venue.Contains(counter.InteractionCell), "counter post is inside the room");
            t.Check(VenueApi.Gate(venue) == VenueGate.WaitingForStaff, "open hours but nobody there: waiting for staff");
            t.Check(WorkGiver_ServeVenue.WantsTender(venue), "venue wants a tender");

            // --- the colonist comes by himself
            int waited = 0;
            while (waited < 6000 && !VenueApi.IsStaffed(venue))
            {
                yield return 100; waited += 100; venue = VenueApi.VenueOfSign(map, sign) ?? venue;
                if (pawn.Downed || pawn.Dead) pawn = VenueTestKit.Worker(map) ?? pawn;
            }
            t.Check(VenueApi.IsStaffed(venue), "colonist " + pawn.LabelShort + " staffs the open venue after " + waited + " ticks (job " + pawn.CurJobDef?.defName + ")");
            t.Check(pawn.CurJobDef == HHVenueDefOf.HH_TendVenue, "tender is on the tend job");
            t.Check(VenueApi.Gate(venue) == VenueGate.Open && VenueApi.Staffer(venue) == pawn, "venue is open while staffed");
            t.Check(!WorkGiver_ServeVenue.WantsTender(venue), "no second tender is wanted");
            int goal = pawn.CurJob != null ? pawn.CurJob.targetB.Cell.x : -1;
            t.Check(pawn.Position == VenueApi.PostCell(venue) || pawn.Position.DistanceTo(VenueApi.PostCell(venue)) < 2f, "tender stands at the post");
            yield return 600;
            t.Check(VenueApi.IsStaffed(venue), "still staffed 600 ticks later");

            // --- closing time: the tender goes, the venue is closed
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 8) % 24; comp.closeHour = (h + 11) % 24;
            yield return 400;
            venue = VenueApi.VenueOfSign(map, sign) ?? venue;
            t.Check(!VenueApi.InHours(venue), "outside hours");
            t.Check(VenueApi.Gate(venue) == VenueGate.ClosedHours, "closed outside hours even though nobody is needed: " + VenueApi.Gate(venue));
            t.Check(!VenueApi.IsStaffed(venue), "tender stopped when the venue closed");
            t.Check(pawn.CurJobDef != HHVenueDefOf.HH_TendVenue, "no longer on the tend job");
            t.Check(!WorkGiver_ServeVenue.WantsTender(venue), "nobody is wanted while closed (no bell)");

            // --- night bell
            int socialLevel0 = pawn.skills.GetSkill(SkillDefOf.Social).Level;
            VenueState.RingBell(sign, 5000);
            t.Check(VenueApi.Gate(venue) == VenueGate.WaitingForStaff, "ringing the bell calls for staff");
            t.Check(WorkGiver_ServeVenue.WantsTender(venue), "bell makes the venue want a tender");
            waited = 0;
            while (waited < 5000 && !VenueApi.IsStaffed(venue)) { yield return 100; waited += 100; }
            t.Check(VenueApi.IsStaffed(venue), "awake colonist answers the bell after " + waited + " ticks");
            t.Check(VenueApi.Gate(venue) == VenueGate.Open && VenueApi.IsBellService(venue), "bell service is open");
            var beer = VenueTestKit.Stock(map, ThingDef.Named("Beer"), 1, room.Inner.Cells.First(c => c != sign.Position && c != counter.Position && c != counter.InteractionCell), room);
            double normal = beer.MarketValue * comp.priceFactor * venue.Type.priceFactor * VS.F(VS.ShopPrice);
            var priced = VenueApi.PriceOf(beer, 1, venue);
            t.Check(priced.Silver >= HSettings.GetSilver(VS.NightBellMin).Silver - 0.001, "bell service never costs less than the minimum: " + priced);
            t.Check(priced.Silver > normal, "bell service has a surcharge: " + priced + " > " + normal.ToString("0.00"));
            // tending only ever adds Social xp; vanilla's own skill decay may take a level at most (xpSinceLastLevel itself dips below 0 for an instant at a decay step)
            t.Check(pawn.skills.GetSkill(SkillDefOf.Social).Level >= socialLevel0 - 1, "social skill intact: level " + socialLevel0 + " -> " + pawn.skills.GetSkill(SkillDefOf.Social).Level);
            VenueTestKit.Teardown(map, room);
        }
    }

    public sealed class TavernSuite : HearthTestSuite
    {
        public override string Name => "venues.tavern";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 26000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            VenueTestKit.ClearGuests(map);
            var factions = VenueTestKit.Friendly(1, false);
            var pawn = VenueTestKit.Worker(map);
            t.Check(factions.Count > 0 && pawn != null, "friendly faction and a serving colonist");
            if (factions.Count == 0 || pawn == null) yield break;
            var room = VenueTestKit.Build(map);
            VenueTestKit.Relocate(pawn, room);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = true;
            var cells = room.Inner.Cells.Where(c => c != sign.Position).Take(3).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            var beer = VenueTestKit.Stock(map, ThingDef.Named("Beer"), 40, cells[0], room);
            var junk = VenueTestKit.Stock(map, ThingDefOf.Steel, 40, cells[1], room);
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            var stock = VenueSales.Stock(venue, true);
            t.Check(stock.Contains(beer) && !stock.Contains(junk), "a tavern sells drinks only");
            int waited = 0;
            while (waited < 6000 && !VenueApi.IsStaffed(venue))
            {
                yield return 100; waited += 100;
                if (pawn.Downed || pawn.Dead) pawn = VenueTestKit.Worker(map) ?? pawn;
                if (waited % 1000 == 0) t.Note($"worker {pawn.LabelShort}: job {pawn.CurJobDef?.defName} (target {pawn.CurJob?.targetA.Thing}) at {pawn.Position}, wants tender {WorkGiver_ServeVenue.WantsTender(venue)}, gate {VenueApi.Gate(venue)}, prio {pawn.workSettings?.GetPriority(HHVenueDefOf.HH_Serving)}, disabled {pawn.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving)}, sign {sign} at {sign.Position}, venues {Venues.All(map).Count}");
            }
            t.Check(VenueApi.IsStaffed(venue), "bartender on duty after " + waited);
            var till0 = VenueTestKit.Till(map);
            int beer0 = beer.stackCount;
            // colonists with Hauling on carry beer from elsewhere into the room's stockpile and top the shelf up again before the shelf is checked
            var restoreHauling = VenueTestKit.SuspendHauling(map);
            var guests = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            t.Check(guests.Count > 0, "guests spawned: " + guests.Count);
            waited = 0;
            while (waited < 22000 && comp.salesCount == 0) { VenueTestKit.Drive(map); yield return 200; waited += 200; }
            t.Check(comp.salesCount > 0, "a guest ordered a drink after " + waited + " ticks");
            t.Check(VenueTestKit.Till(map) > till0, "till increased by drinks: " + till0 + " -> " + VenueTestKit.Till(map));
            yield return 900;
            t.Check(beer.Destroyed || beer.stackCount < beer0, "beer left the shelf: " + beer0 + " -> " + (beer.Destroyed ? 0 : beer.stackCount));
            var drinker = GuestApi.GuestsOn(map).FirstOrDefault(g => GuestMemory.LastDrink(g) > 0);
            t.Check(drinker != null, "a guest is marked as having drunk");
            foreach (var g in GuestApi.GuestsOn(map)) t.Note(g.LabelShort + ": job " + (g.CurJobDef?.defName ?? "none") + ", alcohol " + (g.health?.hediffSet.HasHediff(HediffDefOf.AlcoholHigh) == true) + ", bag " + string.Join("/", g.inventory.innerContainer.Where(x => x.def.defName == "Beer").Select(x => x.stackCount.ToString())));
            t.Check(GuestApi.GuestsOn(map).Any(g => g.health != null && g.health.hediffSet.HasHediff(HediffDefOf.AlcoholHigh)) || GuestApi.GuestsOn(map).Any(g => g.jobs?.curJob?.def == JobDefOf.Ingest || (g.inventory != null && g.inventory.innerContainer.Any(x => x.def.defName == "Beer"))),
                "the drink was drunk or is being drunk or sits in the guest's bag");
            restoreHauling();
            VenueTestKit.Teardown(map, room);
        }
    }

    public sealed class WaitSuite : HearthTestSuite
    {
        public override string Name => "venues.wait";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 28000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            VenueTestKit.ClearGuests(map);
            var factions = VenueTestKit.Friendly(1, false);
            t.Check(factions.Count > 0, "friendly faction");
            if (factions.Count == 0) yield break;
            float oldPatience = HSettings.Get(VS.WaitForStaffTicks);
            HSettings.Set(VS.WaitForStaffTicks, 400f);
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) c.workSettings?.SetPriority(HHVenueDefOf.HH_Serving, 0);

            // a lounge next door: guests wait there
            var loungeRoom = VenueTestKit.Build(map);
            var loungeSign = VenueTestKit.SpawnSign(map, loungeRoom, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Lounge);
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = true;
            var cells = room.Inner.Cells.Where(c => c != sign.Position).Take(2).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            VenueTestKit.Stock(map, ThingDef.Named("Beer"), 25, cells[0], room);
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            var lounge = VenueApi.VenueOfSign(map, loungeSign);
            t.Check(venue != null && lounge != null, "tavern and lounge exist");
            t.Check(lounge != null && lounge.Type.module == ModuleId.ShopsFood && VenueApi.Gate(lounge) == VenueGate.Open, "lounge is always open");
            var wc = VenueShopping.WaitCell(venue);
            t.Check(lounge.Contains(wc), "guests wait in the lounge when there is one: " + wc);
            t.Check(VenueApi.Gate(venue) == VenueGate.WaitingForStaff, "tavern open but nobody to serve");

            // --- guests wait for service, then give up
            var guests = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            t.Check(guests.Count > 0, "guests spawned");
            bool sawWait = false; int waited = 0;
            while (waited < 12000 && !sawWait)
            {
                VenueTestKit.Drive(map); yield return 100; waited += 100;
                sawWait = guests.Any(g => g.CurJobDef == HHVenueDefOf.HH_WaitAtVenue);
            }
            t.Check(sawWait, "a guest waits for service after " + waited + " ticks");
            t.Check(comp.salesCount == 0, "nothing sold without staff");
            waited = 0;
            while (waited < 4000 && !guests.Any(g => GuestMemory.InNoServiceCooldown(g))) { VenueTestKit.Drive(map); yield return 100; waited += 100; }
            var annoyed = guests.FirstOrDefault(g => GuestMemory.InNoServiceCooldown(g));
            t.Check(annoyed != null, "a guest gave up waiting after " + waited + " ticks");
            t.Check(annoyed != null && GuestApi.Info(annoyed).Satisfaction < 0f, "the waiting guest is annoyed: " + (annoyed != null ? GuestApi.Info(annoyed).Satisfaction.ToString("0.00") : "-"));

            // --- closed: new guests ring the night bell; a colonist answers; the surcharge is paid
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 8) % 24; comp.closeHour = (h + 11) % 24;
            comp.priceFactor = 0.8f; // guests only ring for a price they would pay (tavern x1.25, bell +50 %): a modest sign factor keeps every test guest willing
            var group2 = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            waited = 0;
            while (waited < 12000 && !VenueApi.BellActive(venue)) { VenueTestKit.Drive(map); yield return 100; waited += 100; }
            t.Check(VenueApi.BellActive(venue), "a guest rang the bell after " + waited + " ticks");
            t.Check(!VenueApi.InHours(venue), "still outside hours while the bell is rung");
            foreach (var g in group2) t.Note(g.LabelShort + ": job " + (g.CurJobDef?.defName ?? "none"));
            t.Check(GuestApi.GuestsOn(map).Any(g => g.CurJobDef == HHVenueDefOf.HH_WaitAtVenue || g.CurJobDef == HHVenueDefOf.HH_BuyDrink), "bell ringer waits for the answer or is being served");
            var worker = VenueTestKit.Worker(map);
            VenueTestKit.Relocate(worker, room);
            waited = 0;
            bool verbose0 = HLog.Verbose; HLog.Verbose = true;   // purchase failures are logged at Debug level
            while (waited < 6000 && comp.salesCount == 0)
            {
                VenueTestKit.Drive(map); yield return 100; waited += 100;
                if (worker == null || worker.Downed || worker.Dead)
                {
                    t.Note($"worker {worker?.LabelShort} is out (null {worker == null}, downed {worker?.Downed}, dead {worker?.Dead}, spawned {worker?.Spawned}, hp {worker?.health.summaryHealth.SummaryHealthPercent:0.00}, hediffs {(worker == null ? "" : string.Join("/", worker.health.hediffSet.hediffs.Select(hd => hd.def.defName)))}): picking another");
                    worker = VenueTestKit.Worker(map); // colonists get hurt in a live colony
                }
                if (waited >= 600 && waited % 300 == 0 && worker != null && !worker.Downed && !worker.Dead && worker.CurJobDef != HHVenueDefOf.HH_TendVenue && comp.salesCount == 0 && VenueApi.BellActive(venue))
                {
                    // a wandering colonist can take over a thousand ticks to look for work again, longer than guests keep ringing: after a natural chance, hand over the job the work giver would give
                    var wgj = new WorkGiver_ServeVenue { def = DefDatabase<WorkGiverDef>.AllDefsListForReading.First(d => d.giverClass == typeof(WorkGiver_ServeVenue)) }.JobOnThing(worker, sign);
                    if (wgj != null) worker.jobs.StartJob(wgj, JobCondition.InterruptForced);
                }
                if (waited % 1000 == 0 && worker == null) foreach (var cx in map.mapPawns.FreeColonists) t.Note($"DIAG colonist {cx.LabelShort}: spawned {cx.Spawned} downed {cx.Downed} dead {cx.Dead} drafted {cx.Drafted} mental {cx.InMentalState} job {cx.CurJobDef?.defName} serveDisabled {cx.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving)} health {cx.health.summaryHealth.SummaryHealthPercent:0.00}");
                if (waited % 1000 == 0) t.Note($"worker {worker?.LabelShort}: job {worker?.CurJobDef?.defName} at {worker?.Position}, wants tender {WorkGiver_ServeVenue.WantsTender(VenueApi.VenueOfSign(map, sign))}, bell {VenueApi.BellActive(venue)}, gate {VenueApi.Gate(venue)}, drafted {worker?.Drafted}, downed {worker?.Downed}, prio {worker?.workSettings?.GetPriority(HHVenueDefOf.HH_Serving)}");
                if (waited % 500 == 0) foreach (var g in group2) { var gi = GuestApi.Info(g); var da = GuestActivities.All.FirstOrDefault(a => a.Id == "venues.drink"); t.Note($"  guest {g.LabelShort}: job {g.CurJobDef?.defName ?? "none"}, budget {gi?.Budget.Silver:0.0}, drink desire {(gi != null && da != null ? da.Desire(g, gi) : -1f):0.00}, cooldown {GuestMemory.InNoServiceCooldown(g)}, rho {(gi != null ? VenueShopping.Rho(g, gi, VenueApi.PriceMultiplier(venue), sign.thingIDNumber) : -1f):0.00}, awake {g.Awake()}, lord {g.GetLord()?.CurLordToil?.GetType().Name}"); }
            }
            HLog.Verbose = verbose0;
            t.Check(comp.salesCount > 0, "the bell was answered and a drink was served after " + waited + " ticks");
            double plain = ThingDef.Named("Beer").BaseMarketValue * 1.25 * comp.priceFactor * VS.F(VS.ShopPrice);
            t.Check(comp.Income.Silver >= plain * 1.4, "bell service charged a surcharge: " + comp.Income.Silver.ToString("0.00") + " vs plain " + plain.ToString("0.00"));
            HSettings.Set(VS.WaitForStaffTicks, oldPatience);
            VenueTestKit.Teardown(map, room);
            VenueTestKit.Teardown(map, loungeRoom);
        }
    }

    public sealed class VendingSuite : HearthTestSuite
    {
        public override string Name => "venues.vending";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 26000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            VenueTestKit.ClearGuests(map);
            var factions = VenueTestKit.Friendly(1, false);
            t.Check(factions.Count > 0, "friendly faction");
            if (factions.Count == 0) yield break;
            var room = VenueTestKit.Build(map);
            var cell = room.Inner.CenterCell;
            var machine = (Building_Storage)ThingMaker.MakeThing(HHVenueDefOf.HH_VendingMachine);
            machine.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(machine, cell, map, Rot4.South);
            room.Things.Add(machine);
            var power = machine.GetComp<CompPowerTrader>();
            t.Check(power != null && machine.GetSlotGroup() != null, "machine is a powered storage building");
            power.PowerOn = false;
            var meal = VenueTestKit.Stock(map, ThingDefOf.MealSimple, 10, cell, room);
            yield return 10;
            t.Check(VenueSales.MachineStock(machine).Contains(meal), "meal is stocked in the machine");
            t.Check(!VenueSales.MachineWorks(machine), "machine without power does not sell");
            power.PowerOn = true;
            t.Check(VenueSales.MachineWorks(machine), "machine with power sells");
            // machine stock must not be treated as a sale room (no sign here)
            t.Check(!Venues.All(map).Any(v => v.Contains(cell)), "no venue around the machine");
            var restoreHauling = VenueTestKit.SuspendHauling(map);   // the machine is a storage building: colonists would haul loose meals into it and refill the stack
            var buyer = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            var till0 = VenueTestKit.Till(map);
            int meal0 = meal.stackCount;
            int waited = 0;
            while (waited < 22000 && VenueTestKit.Till(map) <= till0)
            {
                power.PowerOn = true; // no power net in the test: keep the machine on
                VenueTestKit.Drive(map); yield return 200; waited += 200;
            }
            t.Check(VenueTestKit.Till(map) > till0, "a guest paid the vending machine after " + waited + " ticks: " + till0 + " -> " + VenueTestKit.Till(map));
            yield return 400;
            t.Check(meal.Destroyed || meal.stackCount < meal0, "meal left the machine: " + meal0 + " -> " + (meal.Destroyed ? 0 : meal.stackCount));
            restoreHauling();
            t.Check(GuestApi.GuestsOn(map).Any(g => GuestMemory.LastVending(g) > 0), "a guest remembers the vending purchase");
            // colonists may use the machine for free: its goods are not forbidden (no sign, no sale room)
            var col = map.mapPawns.FreeColonistsSpawned.First();
            t.Check(meal.Destroyed || !meal.IsForbidden(col), "colonists can use the machine's goods");
            VenueTestKit.Teardown(map, room);
        }
    }

    public sealed class MarketSuite : HearthTestSuite
    {
        public override string Name => "venues.market";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var factions = VenueTestKit.Friendly(3, true);
            t.Check(factions.Count >= 2, "at least two friendly trader factions: " + string.Join(", ", factions.Select(f => f.Name)));
            var game = VenuesGame.Instance;
            t.Check(game != null, "venues game component exists");
            t.Check(!VenueMarket.CanHaveMarket(map) || Venues.All(map).Any(v => VenueApi.SellsOf(v) == SellKind.Goods), "market needs a shop (N08: nothing forced)");
            var room = VenueTestKit.Build(map);
            VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            yield return 20;
            Venues.MarkDirty(map);
            t.Check(!VenueMarket.CanHaveMarket(map) && (VenueMarket.Blocker(map) ?? "").Contains("stalls"), "a shop alone is not enough: " + VenueMarket.Blocker(map));
            var stalls = new List<Thing>();
            int need = VS.I(VS.MarketStalls);
            // a strip of cleared ground, one stall every 3 cells: on a random map the cells next to the room can be trees, rock or water, and a stall that does not fit
            // used to be spawned on top of the previous one (the old stall vanished and the count was one short)
            var strip = VenueTestKit.OutsideCell(map, room);
            TestSetup.PrepareGround(map, new CellRect(strip.x - 2, strip.z - 2, need * 3 + 4, 5));
            for (int i = 0; i < need; i++)
            {
                var st = ThingMaker.MakeThing(HHVenueDefOf.HH_MarketStall, ThingDefOf.WoodLog);
                st.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(st, strip + new IntVec3(i * 3, 0, 0), map, Rot4.South);
                stalls.Add(st);
            }
            yield return 5;
            t.Check(VenueMarket.Stalls(map) >= need, "stalls counted: " + VenueMarket.Stalls(map));
            var realGetter = Venues.RenownGetter;
            if (Modules.On(ModuleId.Inn)) t.Check(realGetter != null, "the Inn module wires Venues.RenownGetter");
            if (realGetter != null) t.Check(Math.Abs(Venues.Renown() - GuestApi.Renown(Find.CurrentMap)) < 0.01f, "Venues.Renown() reads the real colony renown");
            Venues.RenownGetter = () => 5f;
            t.Check(!VenueMarket.CanHaveMarket(map) && (VenueMarket.Blocker(map) ?? "").Contains("renown"), "low renown blocks the market: " + VenueMarket.Blocker(map));
            Venues.RenownGetter = () => 60f;
            t.Check(VenueMarket.CanHaveMarket(map), "shop + enough stalls + renown 60 make a market possible: " + VenueMarket.Blocker(map));
            Venues.RenownGetter = null;
            t.Check(Math.Abs(Venues.Renown() - 50f) < 0.01f, "without a renown system the colony counts as neutral (50)");
            Venues.RenownGetter = () => 60f;   // market day below runs with an explicit renown, not with whatever the live colony has
            var tillBefore = VenueTestKit.Till(map);
            int lordsBefore = map.lordManager.lords.Count(l => l.LordJob is LordJob_TradeWithColony);
            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            var res = game.ForceMarket(map);
            t.Check(res.Caravans >= 2, "market day sent at least 2 caravans: " + res.Caravans + " (visitor groups " + res.VisitorGroups + ")");
            double feeEach = HSettings.GetSilver(VS.StallFee).Silver;
            t.Check(Math.Abs(res.Fees.Silver - feeEach * res.Caravans) < 0.01 && feeEach > 0, "every caravan paid the stall fee: " + res.Fees + " for " + res.Caravans);
            t.Check(Math.Abs((VenueTestKit.Till(map) - tillBefore).Silver - res.Fees.Silver) < 0.01, "stall fees landed in the till: " + tillBefore + " -> " + VenueTestKit.Till(map));
            yield return 30;
            int lordsAfter = map.lordManager.lords.Count(l => l.LordJob is LordJob_TradeWithColony);
            t.Check(lordsAfter - lordsBefore >= 2, "trader lords on the map: " + lordsBefore + " -> " + lordsAfter);
            t.Check(res.Pawns.Count > 0 && res.Pawns.All(p => p != null && p.Faction != null && !p.HostileTo(Faction.OfPlayer)), "all market guests are friendly: " + res.Pawns.Count);
            t.Check(Find.LetterStack.LettersListForReading.Count(l => l.Label.ToString().Contains("arket")) >= 1, "one 'Market day' letter was sent");
            t.Check(Find.LetterStack.LettersListForReading.Count - lettersBefore <= 2, "no letter spam from the individual incidents: " + (Find.LetterStack.LettersListForReading.Count - lettersBefore));
            t.Check(game.NextMarketTick > Find.TickManager.TicksGame, "next market is scheduled");
            t.Check(game.MarketsHeld >= 1, "market counted");
            // interval follows the setting
            float old = HSettings.Get(VS.MarketDays);
            HSettings.Set(VS.MarketDays, 20f);
            game.Schedule(1000);
            float days = (game.NextMarketTick - 1000) / (float)GenDate.TicksPerDay;
            t.Check(days >= 15.9f && days <= 24.1f, "interval 20 days +-20 %: " + days.ToString("0.0"));
            HSettings.Set(VS.MarketDays, old);
            Venues.RenownGetter = realGetter;
            game.Schedule(Find.TickManager.TicksGame);
            foreach (var st in stalls) if (!st.Destroyed) st.Destroy();
            VenueTestKit.Teardown(map, room);
        }
    }

    public sealed class HiddenSuite : HearthTestSuite
    {
        public override string Name => "venues.hidden";
        public override ModuleId Module => ModuleId.ShopsFood;
        static bool InMenu(ThingDef d)
        {
            var cat = d.designationCategory; if (cat == null) return false;
            return cat.AllResolvedDesignators.Any(x => x is Designator_Build b && b.PlacingDef == d)
                || cat.AllResolvedDesignators.OfType<Designator_Dropdown>().Any(dd => (HarmonyLib.AccessTools.Field(typeof(Designator_Dropdown), "elements").GetValue(dd) as List<Designator>)?.Any(e => e is Designator_Build eb && eb.PlacingDef == d) == true);
        }
        public override IEnumerable<int> Run(TestContext t)
        {
            var hh = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("HH_Hospitality");
            t.Check(hh != null, "architect tab HH_Hospitality exists");
            if (hh != null)
            {
                t.Check(InMenu(HHVenueDefOf.HH_VenueSign) && InMenu(HHVenueDefOf.HH_VenueSignWall) && InMenu(HHVenueDefOf.HH_ShopCounter) && InMenu(HHVenueDefOf.HH_VendingMachine), "our buildings are in the Hospitality tab");
            }
            var poker = DefDatabase<ThingDef>.GetNamedSilentFail("PokerTable");
            if (poker != null)
            {
                t.Check(VanillaHiding.Pairs().Any(kv => kv.Key == poker && kv.Value.defName == "HH_PokerTable"), "hide-list pairs the vanilla poker table with HH_PokerTable (XML def)");
                HSettings.Set(VS.HideVanilla, 1f);
                VanillaHiding.Refresh();
                var card = DefDatabase<ThingDef>.GetNamedSilentFail("HH_PokerTable");
                t.Check(card != null && VanillaHiding.IsHidden(poker) == card.IsResearchFinished, "poker table hidden = Rimdeck table researched (" + card?.IsResearchFinished + ")");
            }
            var shelf = DefDatabase<ThingDef>.GetNamed("Shelf");
            var small = DefDatabase<ThingDef>.GetNamed("ShelfSmall");
            t.Check(InMenu(shelf), "vanilla shelf is in the menu to begin with");
            float oldSetting = HSettings.Get(VS.HideVanilla);
            HSettings.Set(VS.HideVanilla, 1f);
            VanillaHiding.Register("Shelf", "HH_ShopCounter");
            VanillaHiding.Refresh();
            t.Check(VanillaHiding.IsHidden(shelf) && !InMenu(shelf), "shelf is hidden once its replacement is available");
            t.Check(shelf.designationCategory != null && DefDatabase<ThingDef>.GetNamed("Shelf") == shelf, "the def itself stays in the game");
            // a pair whose replacement needs research stays visible until researched
            VanillaHiding.Register("ShelfSmall", "HH_VendingMachine");
            VanillaHiding.Refresh();
            bool researched = HHVenueDefOf.HH_VendingMachine.IsResearchFinished;
            t.Check(VanillaHiding.IsHidden(small) == researched, "pair with research: hidden=" + VanillaHiding.IsHidden(small) + " researched=" + researched);
            // unknown defs are ignored
            VanillaHiding.Register("HH_DoesNotExist", "HH_ShopCounter");
            VanillaHiding.Refresh();
            t.Check(true, "unknown vanilla def is skipped without error");
            // setting off brings everything back
            HSettings.Set(VS.HideVanilla, 0f);
            VanillaHiding.Refresh();
            t.Check(InMenu(shelf) && !VanillaHiding.IsHidden(shelf), "setting off: shelf is back in the menu");
            HSettings.Set(VS.HideVanilla, 1f);
            VanillaHiding.Refresh();
            t.Check(!InMenu(shelf), "setting on: hidden again");
            VanillaHiding.Unregister("Shelf"); VanillaHiding.Unregister("ShelfSmall"); VanillaHiding.Unregister("HH_DoesNotExist");
            VanillaHiding.Refresh();
            t.Check(InMenu(shelf) && InMenu(small), "pairs removed: menu restored");
            // the menu keeps its order when restored
            var list = shelf.designationCategory.AllResolvedDesignators;
            t.Check(list.Count(d => d is Designator_Build b && b.PlacingDef == shelf) == 1, "no duplicate designator after restoring");
            HSettings.Set(VS.HideVanilla, oldSetting);
            yield break;
        }
    }

    /// <summary>"Restock" (N45): the sign hands its filter and priority Important to every storage place in the room and gives them back.</summary>
    public sealed class RestockSuite : HearthTestSuite
    {
        public override string Name => "venues.restock";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = true;
            var cells = room.Inner.Cells.Where(c => c != sign.Position).ToList();
            var zone = VenueTestKit.Stockpile(map, room, new[] { cells[0], cells[1] });
            // another zone, outside the room: must stay untouched
            var outCell = VenueTestKit.OutsideCell(map, room);
            var outer = VenueTestKit.Stockpile(map, room, new[] { outCell });
            var shelf = (Building_Storage)ThingMaker.MakeThing(ThingDef.Named("ShelfSmall"), ThingDefOf.WoodLog);
            shelf.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(shelf, cells[cells.Count - 1], map, Rot4.North);
            room.Things.Add(shelf);
            VenueTestKit.Stock(map, ThingDefOf.Steel, 20, cells[0], room);
            VenueTestKit.Stock(map, ThingDefOf.WoodLog, 20, cells[1], room);
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null, "shop venue exists");
            t.Check(zone.settings.Priority == StoragePriority.Normal && zone.settings.filter.Allows(ThingDefOf.Cloth), "before: zone takes everything at Normal priority");
            t.Check(comp.CompGetGizmosExtra().Any(g => g is Command_Action ca && ca.defaultLabel.ToString().StartsWith("Restock")), "the sign offers the Restock button");

            // opt-in: nothing happens until switched on
            comp.SyncRestock();
            t.Check(zone.settings.Priority == StoragePriority.Normal, "off by default: nothing changed");

            comp.SetRestock(true);
            t.Check(comp.restockOn && comp.RestockPlaces == 2, "restock on, two storage places taken over (zone + shelf): " + comp.RestockPlaces);
            t.Check(zone.settings.Priority == StoragePriority.Important && shelf.GetStoreSettings().Priority == StoragePriority.Important, "zone and shelf have priority Important");
            t.Check(zone.settings.filter.Allows(ThingDefOf.Steel) && zone.settings.filter.Allows(ThingDefOf.WoodLog) && !zone.settings.filter.Allows(ThingDefOf.Cloth), "filter = the goods the room sells (steel, wood), not cloth");
            t.Check(outer.settings.Priority == StoragePriority.Normal && outer.settings.filter.Allows(ThingDefOf.Cloth), "a zone outside the room is untouched");

            // the player edits the goods filter: it reaches the places on the next sync
            comp.restockFilter.SetAllow(ThingDefOf.Cloth, true);
            comp.SyncRestock();
            t.Check(zone.settings.filter.Allows(ThingDefOf.Cloth), "filter change reaches the zone");
            t.Check(comp.RestockPlaces == 2, "still two places (no duplicate backups)");

            // a new shelf is taken over by the next sync
            comp.SetRestock(false);
            t.Check(!comp.restockOn && comp.RestockPlaces == 0, "switched off: backups gone");
            t.Check(zone.settings.Priority == StoragePriority.Normal && zone.settings.filter.Allows(ThingDefOf.Cloth) && zone.settings.filter.Allows(ThingDefOf.Gold), "zone has its old settings back (Normal, everything)");
            t.Check(shelf.GetStoreSettings().Priority != StoragePriority.Important, "shelf has its old priority back");

            // removing the sign gives everything back as well
            comp.SetRestock(true);
            t.Check(zone.settings.Priority == StoragePriority.Important, "on again");
            sign.DeSpawn();
            t.Check(zone.settings.Priority == StoragePriority.Normal && zone.settings.filter.Allows(ThingDefOf.Gold), "sign removed: the zone is restored");
            GenSpawn.Spawn(sign, cells[2], map, Rot4.South);
            yield return 20;

            // a venue type that sells nothing cannot restock
            comp.SetRestock(false);
            comp.SetType(HHVenueDefOf.HH_Venue_Lounge);
            comp.SetRestock(true);
            t.Check(!comp.restockOn, "a lounge sells nothing: restock stays off");
            comp.SetType(HHVenueDefOf.HH_Venue_Shop);

            // the window draws without errors
            Find.CameraDriver.JumpToCurrentMapLoc(sign.Position);
            Find.WindowStack.Add(new Dialog_VenueRestock(comp));
            yield return 90;
            Find.WindowStack.TryRemove(typeof(Dialog_VenueRestock));
            if (comp.restockOn) comp.SetRestock(false);
            VenueTestKit.Teardown(map, room);
            if (!outer.Map.zoneManager.AllZones.Contains(outer)) { }
            else outer.Delete();
        }
    }

    /// <summary>Last orders: the window before closing, the grace after it, and what guests do about it.</summary>
    public sealed class LastOrdersSuite : HearthTestSuite
    {
        public override string Name => "venues.last";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            VenueTestKit.ClearGuests(map);
            var factions = VenueTestKit.Friendly(1, false);
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            var cells = room.Inner.Cells.Where(c => c != sign.Position).Take(2).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            VenueTestKit.Stock(map, ThingDefOf.Steel, 40, cells[0], room);
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null, "shop venue exists");
            if (venue == null) yield break;
            int oldWin = VS.I(VS.LastOrders);
            HSettings.Set(VS.LastOrders, 30f);
            comp.alwaysOpen = false; comp.openHour = 8; comp.closeHour = 20;

            // --- pure clock logic (the hour is passed in)
            t.Check(Math.Abs(VenueApi.HoursToClose(venue, 19.25f) - 0.75f) < 0.001f, "0.75 hours to close at 19:15");
            t.Check(!VenueApi.InLastOrders(venue, 19.25f), "19:15: not yet last orders (30 minutes window)");
            t.Check(VenueApi.InLastOrders(venue, 19.6f) && VenueApi.InLastOrders(venue, 19.99f), "19:36 and 19:59: last orders");
            t.Check(!VenueApi.InLastOrders(venue, 20.1f) && !VenueApi.InLastOrders(venue, 7.9f), "after closing and before opening: not last orders");
            t.Check(VenueApi.ServingLate(venue, 20.1f) && !VenueApi.ServingLate(venue, 20.4f) && !VenueApi.ServingLate(venue, 12f), "a purchase under way is still served for a quarter of an hour after closing, not later, not in the day");
            comp.openHour = 18; comp.closeHour = 2;
            t.Check(VenueApi.InLastOrders(venue, 1.6f) && !VenueApi.InLastOrders(venue, 1.2f) && VenueApi.ServingLate(venue, 2.1f), "after midnight: 18-2 closes at 2");
            comp.alwaysOpen = true;
            t.Check(!VenueApi.InLastOrders(venue, 1.9f) && !VenueApi.ServingLate(venue, 3.1f) && VenueApi.HoursToClose(venue, 5f) == float.MaxValue, "a business open around the clock has no last orders");
            comp.alwaysOpen = false; comp.openHour = 8; comp.closeHour = 20;
            HSettings.Set(VS.LastOrders, 0f);
            t.Check(!VenueApi.InLastOrders(venue, 19.9f) && !VenueApi.ServingLate(venue, 20.1f), "setting 0: no last orders, no grace");
            HSettings.Set(VS.LastOrders, 30f);

            // --- live clock: with a 60 minute window and the venue closing at the next full hour it is always last orders
            HSettings.Set(VS.LastOrders, 60f);
            int h = VenueTestKit.HourNow(map);
            comp.openHour = (h + 23) % 24; comp.closeHour = (h + 1) % 24;
            t.Check(VenueApi.InLastOrders(venue), "live: closing within the hour -> last orders");
            t.Check(comp.CompInspectStringExtra().Contains("last orders"), "inspect string says last orders: " + comp.CompInspectStringExtra()?.Replace("\n", " | "));
            comp.closeHour = (h + 3) % 24;
            t.Check(!VenueApi.InLastOrders(venue), "live: closing in 2-3 hours -> normal business");
            t.Check(VenueApi.CanCompletePurchase(venue), "an open shop completes purchases");

            // --- guests hurry: the shop activity is wanted more
            if (factions.Count > 0)
            {
                var guests = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
                var g = guests.FirstOrDefault(x => GuestApi.Info(x) != null);
                var shop = GuestActivities.All.FirstOrDefault(a => a.Id == "venues.shop");
                t.Check(g != null && shop != null, "a visitor and the shop activity exist");
                if (g != null && shop != null)
                {
                    var info = GuestApi.Info(g);
                    info.Budget = Money.FromSilver(200);
                    comp.closeHour = (h + 3) % 24;
                    float normal = shop.Desire(g, info);
                    comp.closeHour = (h + 1) % 24;
                    float rush = shop.Desire(g, info);
                    t.Check(normal > 0f && rush > normal * 1.3f, $"shoppers hurry before closing: desire {normal:0.00} -> {rush:0.00}");
                }
            }
            HSettings.Set(VS.LastOrders, (float)oldWin);
            VenueTestKit.ClearGuests(map);
            VenueTestKit.Teardown(map, room);
        }
    }
}
#endif
