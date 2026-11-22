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
                    foreach (var cell in rect) { var t = cell.GetTerrain(map); if (t.passability == Traversability.Impassable || t.IsWater || cell.Fogged(map)) { ok = false; break; } }
                    if (ok) { chosen = c; break; }
                }
            nextOffset++;
            if (!chosen.IsValid) throw new Exception("no free area for a test room");
            var room = new TestRoom { Outer = CellRect.CenteredOn(chosen, half), Inner = CellRect.CenteredOn(chosen, half - 1) };
            room.Door = new IntVec3(room.Outer.CenterCell.x, 0, room.Outer.minZ);
            var keepOut = room.Outer.ExpandedBy(2);
            foreach (var c in room.Outer.ExpandedBy(1))
                foreach (var t in c.GetThingList(map).ToList())
                {
                    if (t is Pawn p)
                    {
                        // push animals and colonists out of the building site
                        if (CellFinder.TryFindRandomCellNear(chosen, map, 14, x => !keepOut.Contains(x) && x.Standable(map) && x.GetEdifice(map) == null, out var dest))
                        { p.Position = dest; p.pather?.StopDead(); p.Notify_Teleported(true, true); }
                    }
                    else if (t.def.destroyable) t.Destroy();
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
            var parms = new IncidentParms { target = map, faction = f, forced = true, sendLetter = false, points = 120f, spawnCenter = spawn, storeGeneratedNeutralPawns = new List<Pawn>() };
            IncidentDefOf.VisitorGroup.Worker.TryExecute(parms);
            return parms.storeGeneratedNeutralPawns;
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
            var pick = colonists.FirstOrDefault();
            if (pick == null) return null;
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) c.workSettings?.SetPriority(HHVenueDefOf.HH_Serving, 0);
            pick.workSettings.SetPriority(HHVenueDefOf.HH_Serving, 1);
            if (pick.needs?.rest != null) pick.needs.rest.CurLevel = 1f;
            return pick;
        }

        public static void Teardown(Map map, TestRoom room)
        {
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
            t.Check(wv != null, "wall-mounted sign is found (contract workaround)");
            t.Check(wv != null && wv.Contains(room.Inner.CenterCell), "wall sign belongs to the room it faces out of");

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
            while (waited < 22000 && comp.salesCount == 0) { yield return 200; waited += 200; }
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
            t.Check(pawn.skills.GetSkill(SkillDefOf.Social).xpSinceLastLevel >= 0f, "social skill intact");
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
            var guests = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            t.Check(guests.Count > 0, "guests spawned: " + guests.Count);
            waited = 0;
            while (waited < 22000 && comp.salesCount == 0) { yield return 200; waited += 200; }
            t.Check(comp.salesCount > 0, "a guest ordered a drink after " + waited + " ticks");
            t.Check(VenueTestKit.Till(map) > till0, "till increased by drinks: " + till0 + " -> " + VenueTestKit.Till(map));
            yield return 900;
            t.Check(beer.Destroyed || beer.stackCount < beer0, "beer left the shelf: " + beer0 + " -> " + (beer.Destroyed ? 0 : beer.stackCount));
            var drinker = GuestApi.GuestsOn(map).FirstOrDefault(g => GuestMemory.LastDrink(g) > 0);
            t.Check(drinker != null, "a guest is marked as having drunk");
            foreach (var g in GuestApi.GuestsOn(map)) t.Note(g.LabelShort + ": job " + (g.CurJobDef?.defName ?? "none") + ", alcohol " + (g.health?.hediffSet.HasHediff(HediffDefOf.AlcoholHigh) == true) + ", bag " + string.Join("/", g.inventory.innerContainer.Where(x => x.def.defName == "Beer").Select(x => x.stackCount.ToString())));
            t.Check(GuestApi.GuestsOn(map).Any(g => g.health != null && g.health.hediffSet.HasHediff(HediffDefOf.AlcoholHigh)) || GuestApi.GuestsOn(map).Any(g => g.jobs?.curJob?.def == JobDefOf.Ingest || (g.inventory != null && g.inventory.innerContainer.Any(x => x.def.defName == "Beer"))),
                "the drink was drunk or is being drunk or sits in the guest's bag");
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
                yield return 100; waited += 100;
                sawWait = guests.Any(g => g.CurJobDef == HHVenueDefOf.HH_WaitAtVenue);
            }
            t.Check(sawWait, "a guest waits for service after " + waited + " ticks");
            t.Check(comp.salesCount == 0, "nothing sold without staff");
            waited = 0;
            while (waited < 4000 && !guests.Any(g => GuestMemory.InNoServiceCooldown(g))) { yield return 100; waited += 100; }
            var annoyed = guests.FirstOrDefault(g => GuestMemory.InNoServiceCooldown(g));
            t.Check(annoyed != null, "a guest gave up waiting after " + waited + " ticks");
            t.Check(annoyed != null && GuestApi.Info(annoyed).Satisfaction < 0f, "the waiting guest is annoyed: " + (annoyed != null ? GuestApi.Info(annoyed).Satisfaction.ToString("0.00") : "-"));

            // --- closed: new guests ring the night bell; a colonist answers; the surcharge is paid
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 8) % 24; comp.closeHour = (h + 11) % 24;
            var group2 = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            waited = 0;
            while (waited < 12000 && !VenueApi.BellActive(venue)) { yield return 100; waited += 100; }
            t.Check(VenueApi.BellActive(venue), "a guest rang the bell after " + waited + " ticks");
            t.Check(!VenueApi.InHours(venue), "still outside hours while the bell is rung");
            foreach (var g in group2) t.Note(g.LabelShort + ": job " + (g.CurJobDef?.defName ?? "none"));
            t.Check(GuestApi.GuestsOn(map).Any(g => g.CurJobDef == HHVenueDefOf.HH_WaitAtVenue || g.CurJobDef == HHVenueDefOf.HH_BuyDrink), "bell ringer waits for the answer or is being served");
            var worker = VenueTestKit.Worker(map);
            waited = 0;
            while (waited < 6000 && comp.salesCount == 0)
            {
                yield return 100; waited += 100;
                if (worker == null || worker.Downed || worker.Dead) worker = VenueTestKit.Worker(map); // colonists get hurt in a live colony
                if (waited % 1000 == 0) t.Note($"worker {worker?.LabelShort}: job {worker?.CurJobDef?.defName} at {worker?.Position}, wants tender {WorkGiver_ServeVenue.WantsTender(VenueApi.VenueOfSign(map, sign))}, bell {VenueApi.BellActive(venue)}, gate {VenueApi.Gate(venue)}, drafted {worker?.Drafted}, downed {worker?.Downed}, prio {worker?.workSettings?.GetPriority(HHVenueDefOf.HH_Serving)}");
            }
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
            var buyer = VenueTestKit.SpawnVisitors(map, factions[0], VenueTestKit.OutsideCell(map, room));
            var till0 = VenueTestKit.Till(map);
            int meal0 = meal.stackCount;
            int waited = 0;
            while (waited < 22000 && VenueTestKit.Till(map) <= till0)
            {
                power.PowerOn = true; // no power net in the test: keep the machine on
                yield return 200; waited += 200;
            }
            t.Check(VenueTestKit.Till(map) > till0, "a guest paid the vending machine after " + waited + " ticks: " + till0 + " -> " + VenueTestKit.Till(map));
            yield return 400;
            t.Check(meal.Destroyed || meal.stackCount < meal0, "meal left the machine: " + meal0 + " -> " + (meal.Destroyed ? 0 : meal.stackCount));
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
            t.Check(VenueMarket.CanHaveMarket(map), "a shop makes a market possible");
            int lordsBefore = map.lordManager.lords.Count(l => l.LordJob is LordJob_TradeWithColony);
            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            var res = game.ForceMarket(map);
            t.Check(res.Caravans >= 2, "market day sent at least 2 caravans: " + res.Caravans + " (visitor groups " + res.VisitorGroups + ")");
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
            game.Schedule(Find.TickManager.TicksGame);
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
}
#endif
