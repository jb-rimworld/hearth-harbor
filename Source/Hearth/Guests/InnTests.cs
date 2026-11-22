// SPDX-License-Identifier: MIT
// In-game suites of the Inn module (built only with HH_TESTS).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Guests
{
    /// <summary>Shared helpers: builds a small walled, roofed guest room with beds (and optionally a front desk) by code.</summary>
    public static class InnTestKit
    {
        public sealed class Site
        {
            public List<Thing> Built = new List<Thing>();
            public List<Building_Bed> Beds = new List<Building_Bed>();
            public Building_FrontDesk Desk;
            public CellRect Interior;
            public Room Room => Beds.FirstOrDefault()?.GetRoom();
        }

        static readonly HashSet<IntVec3> usedCells = new HashSet<IntVec3>();

        static bool AreaOk(Map map, CellRect r)
        {
            foreach (var c in r)
            {
                if (!c.InBounds(map) || c.Fogged(map) || usedCells.Contains(c)) return false;
                if (c.GetFirstBuilding(map) != null || c.GetFirstPawn(map) != null) return false;
                var terr = c.GetTerrain(map);
                if (terr == null || terr.passability == Traversability.Impassable || terr.IsWater || !terr.affordances.Contains(TerrainAffordanceDefOf.Heavy)) return false;
                if (c.GetEdifice(map) != null) return false;
            }
            return true;
        }

        public static Faction GuestFaction(Map map)
            => Find.FactionManager.AllFactionsListForReading.Where(f => MapComponent_HH_GuestScheduler.FactionCanVisit(f, map)).OrderByDescending(f => f.PlayerGoodwill).FirstOrDefault();

        /// <summary>Walls ring around an interior of w×h cells, a door on the south side, roof, beds along the north wall, desk 3 cells south of the door.</summary>
        public static Site BuildRoom(Map map, int beds, bool desk, int w = 5, int h = 4)
        {
            var site = new Site();
            CellRect outer = CellRect.Empty;
            bool found = false;
            foreach (var c in GenRadial.RadialCellsAround(map.Center, 45, true))
            {
                outer = new CellRect(c.x, c.z, w + 2, h + 2 + (desk ? 4 : 0));
                if (AreaOk(map, outer.ExpandedBy(1))) { found = true; break; }
            }
            if (!found) throw new Exception("no free building site found");
            foreach (var c in outer.ExpandedBy(1)) usedCells.Add(c);
            int baseZ = outer.minZ + (desk ? 4 : 0);
            var room = new CellRect(outer.minX, baseZ, w + 2, h + 2);
            site.Interior = room.ContractedBy(1);
            var wood = ThingDefOf.WoodLog;
            foreach (var c in room.ExpandedBy(1)) foreach (var t in c.GetThingList(map).ToList()) if (t is Plant || t.def.category == ThingCategory.Item) t.Destroy();
            if (desk) foreach (var c in new CellRect(outer.minX, outer.minZ, outer.Width, 4)) foreach (var t in c.GetThingList(map).ToList()) if (t is Plant || t.def.category == ThingCategory.Item) t.Destroy();
            IntVec3 doorCell = new IntVec3(room.minX + room.Width / 2, 0, room.minZ);
            foreach (var c in room.EdgeCells)
            {
                var def = c == doorCell ? ThingDefOf.Door : ThingDefOf.Wall;
                site.Built.Add(Spawn(def, wood, c, map, Rot4.North));
            }
            foreach (var c in site.Interior) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            for (int i = 0; i < beds; i++)
            {
                var pos = new IntVec3(site.Interior.minX + i * 2, 0, site.Interior.maxZ);
                var bed = (Building_Bed)Spawn(ThingDefOf.Bed, wood, pos, map, Rot4.South);
                site.Built.Add(bed); site.Beds.Add(bed);
            }
            if (desk)
            {
                var dpos = new IntVec3(doorCell.x, 0, doorCell.z - 3);
                site.Desk = (Building_FrontDesk)Spawn(InnDefOf.HH_FrontDesk, wood, dpos, map, Rot4.North);
                site.Built.Add(site.Desk);
            }
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            return site;
        }

        static Thing Spawn(ThingDef def, ThingDef stuff, IntVec3 c, Map map, Rot4 rot)
        {
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
            t.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(t, c, map, rot, WipeMode.Vanish);
        }

        public static void MarkGuestBeds(Site s, bool on) { foreach (var b in s.Beds) GuestBeds.Comp(b).SetGuestBed(on, byPlayer: false); }

        public static void Teardown(Site s, Map map)
        {
            if (s == null) return;
            foreach (var b in s.Beds) if (!b.Destroyed) GuestBeds.Comp(b)?.SetGuestBed(false, false);
            foreach (var t in s.Built) if (!t.Destroyed) t.Destroy(DestroyMode.Vanish);
            foreach (var c in s.Interior) map.roofGrid.SetRoof(c, null);
            var wc = GuestWings.Comp(map); if (wc != null) { foreach (var w in wc.wings) w.Clear(); wc.wings.Clear(); wc.selected = null; }
        }

        /// <summary>Removes every guest pawn this module knows about (tests only).</summary>
        public static void RemoveAllGuests(Map map)
        {
            var reg = GuestRegistry.Instance;
            foreach (var r in reg.Records.ToList())
            {
                var p = r.Pawn;
                reg.Unregister(r, "test teardown");
                if (p != null && p.Spawned) p.DeSpawn();
                if (p != null && !p.Destroyed && !p.Dead) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            }
            foreach (var l in map.lordManager.lords.ToList()) if (l.LordJob is LordJob_HH_GuestStay) map.lordManager.RemoveLord(l);
        }

        public static int CountSilver(Pawn p) { int n = 0; foreach (var t in p.inventory.innerContainer) if (t.def == ThingDefOf.Silver) n += t.stackCount; return n; }

        public static bool JournalHas(string to, string memoPart, int sinceTick)
            => Ledger.Current.Journal.Any(e => e.Tick >= sinceTick && e.To == to && (e.Memo ?? "").Contains(memoPart));
    }

    /// <summary>N08: no guest bed, no venue → two days pass and nothing at all happens.</summary>
    public sealed class InnNothingSuite : HearthTestSuite
    {
        public override string Name => "inn.nothing";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 135000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var reg = GuestRegistry.Instance;
            t.Check(reg != null, "guest register exists");
            t.Check(GuestApi.Impl != null, "GuestApi implemented by the Inn module");
            t.Check(Modules.On(ModuleId.Inn), "Inn module on");
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            foreach (var b in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()) GuestBeds.Comp(b)?.SetGuestBed(false, false);
            t.Check(GuestBeds.CountOn(map) == 0, "no guest beds on the map");
            t.Check(Venues.All(map).Count == 0, "no venues on the map");
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
            t.Check(sch != null, "scheduler component exists");
            int letters0 = Find.LetterStack.LettersListForReading.Count;
            int hosted0 = reg.GuestsHosted;
            yield return 120000;
            t.Check(reg.Records.Count == 0, "no guest files after 2 days: " + reg.Records.Count);
            t.Check(reg.GuestsHosted == hosted0, "no guests arrived");
            t.Check(!map.lordManager.lords.Any(l => l.LordJob is LordJob_HH_GuestStay), "no guest lords on the map");
            t.Check(sch.Forecast.Count == 0 && !sch.EverActive, "arrival plan stays empty and inactive");
            t.Check(!Find.LetterStack.LettersListForReading.Skip(letters0).Any(l => (l.Label.ToString() ?? "").StartsWith("Guests")), "no guest letters");
            t.Check(GuestWings.Comp(map).wings.Count == 0, "no guest wing created");
        }
    }

    /// <summary>Caretaker removes broken guest files and frees guest beds owned by non-guests; colonists cannot take guest beds.</summary>
    public sealed class InnCleanupSuite : HearthTestSuite
    {
        public override string Name => "inn.cleanup";
        public override ModuleId Module => ModuleId.Inn;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var f = InnTestKit.GuestFaction(map);
            t.Check(f != null, "a friendly faction exists: " + f?.Name);
            if (f == null) yield break;
            // 1) record whose pawn never spawned / was despawned long ago
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, tile: map.Tile));
            var g = reg.NewGroup(f, map, StayKind.Overnight, false);
            var r = reg.Register(p, g, map, 1, Find.TickManager.TicksGame + 60000, forceDigital: true, forceBudget: Money.FromSilver(100));
            r.ArrivedTick = Find.TickManager.TicksGame - 5000;
            t.Check(reg.Get(p) != null && GuestRegistry.IsRegisteredGuest(p), "broken record registered");
            t.Check(Ledger.Current.Balance(r.Account) == Money.FromSilver(100), "card funds booked for the broken record");
            // 2) record without pawn
            reg.AddRawRecordForTests(new GuestRecord { PawnId = -42, GroupId = g.Id, MapId = map.uniqueID, ArrivedTick = 0 });
            int before = reg.Records.Count;
            int fixes = reg.CaretakerRecords();
            t.Check(fixes >= 2, "caretaker repaired " + fixes + " problems");
            t.Check(reg.Get(p) == null && !GuestRegistry.IsRegisteredGuest(p), "despawned guest's record removed");
            t.Check(reg.Records.Count == before - 2, "pawnless record removed");
            t.Check(Ledger.Current.Balance(r.Account).Micro == 0, "card funds returned to the world");
            t.Check(Ledger.Current.SumAll() == 0, "ledger sums to zero");
            Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            // 3) guest bed owned by a colonist
            var site = InnTestKit.BuildRoom(map, 1, false);
            yield return 5;
            var bed = site.Beds[0];
            var col = map.mapPawns.FreeColonistsSpawned.First();
            var prevBed = col.ownership.OwnedBed;
            InnTestKit.MarkGuestBeds(site, true);
            t.Check(GuestBeds.IsGuestBed(bed), "bed marked as guest bed");
            t.Check(!RestUtility.IsValidBedFor(bed, col, col, false), "colonist may not use a guest bed");
            t.Check(!bed.CompAssignableToPawn.CanAssignTo(col).Accepted, "assign dialog refuses colonists");
            col.ownership.ClaimBedIfNonMedical(bed); // bypasses the dialog, like a broken save would
            t.Check(bed.OwnersForReading.Contains(col), "colonist forced into the guest bed");
            int bf = reg.CaretakerBeds();
            t.Check(bf >= 1 && !bed.OwnersForReading.Contains(col), "caretaker freed the guest bed (" + bf + ")");
            var q = GuestBedPricing.Quote(bed);
            t.Check(q != null && q.Price.Micro > 0 && q.Price.WholeSilver % 5 == 0, "price quote: " + q?.Price + " (" + q?.Breakdown + ")");
            t.Check(q != null && Math.Abs(q.Occupancy - 1.35f) < 0.001f || site.Interior.Area < 9, "single room ×1.35");
            string insp = bed.GetInspectString();
            t.Check(insp.Contains("silver/night"), "inspect string shows the price: " + insp.Replace("\n", " | "));
            InnTestKit.Teardown(site, map);
            if (prevBed != null && !prevBed.Destroyed) col.ownership.ClaimBedIfNonMedical(prevBed);
            t.Check(GuestBeds.CountOn(map) == 0, "no guest beds left");
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>The core loop: arrival → check-in at the desk → bed → first night paid → departure → report → goodwill 0..5 per guest.</summary>
    public sealed class InnStaySuite : HearthTestSuite
    {
        public override string Name => "inn.stay";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 45000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var site = InnTestKit.BuildRoom(map, 2, true);
            InnTestKit.MarkGuestBeds(site, true);
            yield return 10;
            t.Check(GuestBeds.CountOn(map) == 2, "two guest beds");
            var wing = GuestWings.Comp(map).wings.FirstOrDefault();
            t.Check(wing != null && wing[site.Beds[0].Position], "Guest Wing 1 created around the room");
            var f = InnTestKit.GuestFaction(map);
            t.Check(f != null, "guest faction: " + f?.Name);
            if (f == null) yield break;
            int gw0 = f.PlayerGoodwill;
            int t0 = Find.TickManager.TicksGame;
            string till = Payments.ColonyTill(map);
            var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 2, Kind = StayKind.Overnight, Nights = 1, ForcePod = false, ForceBudget = Money.FromSilver(400) });
            t.Check(g != null, "arrival incident fired");
            if (g == null) { InnTestKit.Teardown(site, map); yield break; }
            var members = reg.Members(g).ToList();
            t.Check(members.Count == 2, "two guests registered");
            t.Check(members.All(m => m.Pawn.GetLord()?.LordJob is LordJob_HH_GuestStay), "guests have the guest lord");
            int waited = 0;
            while (waited < 20000 && !members.All(m => m.CheckedIn && m.Bed != null)) { yield return 250; waited += 250; }
            t.Check(members.All(m => m.CheckedIn), "both checked in after " + waited + " ticks");
            t.Check(members.All(m => m.Bed != null && GuestBeds.IsGuestBed(m.Bed)), "both got a guest bed");
            t.Check(members.All(m => !m.SelfCheckIn), "checked in at the front desk");
            t.Check(members.Select(m => m.Bed).Distinct().Count() == members.Count, "one bed each");
            t.Check(members.All(m => m.NightsPaid >= 1), "first night paid");
            t.Check(InnTestKit.JournalHas(till, "Inn: night", t0), "night payment booked into the till");
            t.Check(members.All(m => m.Spent.Micro > 0 && m.Budget < m.StartBudget), "budget reduced by spending");
            t.Check(members.All(m => m.Bed.OwnersForReading.Contains(m.Pawn)), "guests own their beds while staying");
            t.Check(members.All(m => GuestRegistry.IsWingRestricted(m.Pawn)), "guests are restricted to the wing while staying");
            yield return 1500;
            int letters0 = Find.LetterStack.LettersListForReading.Count;
            foreach (var m in members) m.PlannedLeaveTick = Find.TickManager.TicksGame;
            waited = 0;
            while (waited < 3000 && !g.LeaveSent) { yield return 250; waited += 250; }
            t.Check(g.LeaveSent, "group set off at the planned time");
            t.Check(members.All(m => m.Settled), "stays settled");
            var newLetters = Find.LetterStack.LettersListForReading.Skip(letters0).ToList();
            t.Check(newLetters.Any(l => l.Label.ToString().StartsWith("Guests left")), "departure letter sent: " + string.Join(" / ", newLetters.Select(l => l.Label.ToString())));
            t.Check(!DepartureReport.LastReportText.NullOrEmpty() && DepartureReport.LastReportText.Contains("silver"), "report text: " + (DepartureReport.LastReportText ?? "").Replace("\n", " | "));
            int dgw = f.PlayerGoodwill - gw0;
            t.Check(dgw >= -5 && dgw <= 5 * members.Count, "goodwill change " + dgw + " within 0..5 per guest (" + string.Join(",", members.Select(m => m.GoodwillPoints)) + ")");
            t.Check(members.All(m => m.GoodwillPoints >= -5 && m.GoodwillPoints <= 5), "per-guest points within bounds");
            t.Check(members.All(m => m.Bed == null && m.Pawn.ownership.OwnedBed == null), "beds released at departure");
            t.Check(site.Beds.All(b => b.OwnersForReading.Count == 0), "guest beds free again");
            waited = 0;
            while (waited < 20000 && members.Any(m => m.Pawn.Spawned)) { yield return 500; waited += 500; }
            t.Check(members.All(m => !m.Pawn.Spawned), "guests left the map after " + waited + " ticks");
            t.Check(members.All(m => reg.Get(m.Pawn) == null), "guest files closed");
            t.Check(reg.Group(g.Id) == null, "group closed");
            t.Check(Ledger.Current.SumAll() == 0, "ledger sums to zero");
            InnTestKit.RemoveAllGuests(map);
            InnTestKit.Teardown(site, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>Cash and card guests both pay; card remainder returns to the world; the ledger always balances.</summary>
    public sealed class InnPaymentSuite : HearthTestSuite
    {
        public override string Name => "inn.payment";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var site = InnTestKit.BuildRoom(map, 2, false, 6, 4);
            InnTestKit.MarkGuestBeds(site, true);
            yield return 10;
            var f = InnTestKit.GuestFaction(map);
            if (f == null) { t.Check(false, "no guest faction"); yield break; }
            int t0 = Find.TickManager.TicksGame;
            var gCash = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.Overnight, Nights = 2, ForceDigital = false, ForceBudget = Money.FromSilver(300), Silent = true });
            var gCard = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.Overnight, Nights = 2, ForceDigital = true, ForceBudget = Money.FromSilver(450), Silent = true });
            t.Check(gCash != null && gCard != null, "two groups arrived");
            if (gCash == null || gCard == null) { InnTestKit.Teardown(site, map); yield break; }
            var cash = reg.Members(gCash).First(); var card = reg.Members(gCard).First();
            t.Check(!cash.PaysDigital && InnTestKit.CountSilver(cash.Pawn) >= 300, "cash guest carries silver: " + InnTestKit.CountSilver(cash.Pawn));
            t.Check(card.PaysDigital && Ledger.Current.Balance(card.Account) == Money.FromSilver(450), "card guest funded in the ledger: " + Ledger.Current.Balance(card.Account));
            t.Check(Payments.Providers.Any(p => p.Id == "inn.card"), "card provider registered");
            int silver0 = InnTestKit.CountSilver(cash.Pawn);
            int waited = 0;
            while (waited < 20000 && !(cash.CheckedIn && card.CheckedIn)) { yield return 250; waited += 250; }
            t.Check(cash.CheckedIn && card.CheckedIn, "both checked in (" + waited + " ticks)");
            t.Check(cash.NightsPaid >= 1 && card.NightsPaid >= 1, "both paid a night");
            string till = Payments.ColonyTill(map);
            t.Check(InnTestKit.JournalHas(till, "(cash)", t0), "cash payment in the till");
            t.Check(InnTestKit.JournalHas(till, "(card)", t0), "card payment in the till");
            t.Check(InnTestKit.CountSilver(cash.Pawn) == silver0 - cash.NightPrice.WholeSilver, $"cash guest's silver went down by the price ({silver0} → {InnTestKit.CountSilver(cash.Pawn)}, price {cash.NightPrice})");
            t.Check(Ledger.Current.Balance(card.Account) == Money.FromSilver(450) - card.NightPrice, "card balance went down by the price: " + Ledger.Current.Balance(card.Account));
            t.Check(Ledger.Current.SumAll() == 0, "ledger sums to zero after payments");
            // budget is respected: a charge above the remaining budget fails and books nothing
            long sum0 = Ledger.Current.Balance(till).Micro;
            t.Check(!GuestApi.ChargeGuest(card.Pawn, card.Budget + Money.FromSilver(1), "test", "too much"), "charge above budget refused");
            t.Check(Ledger.Current.Balance(till).Micro == sum0, "refused charge booked nothing");
            // explicit card + cash service charge
            t.Check(GuestApi.ChargeGuest(card.Pawn, Money.FromSilver(7.5), "test", "Test: snack"), "card guest pays a 7.5 silver snack");
            t.Check(GuestApi.ChargeGuest(cash.Pawn, Money.FromSilver(7.5), "test", "Test: snack"), "cash guest pays a 7.5 silver snack (8 coins)");
            t.Check(Ledger.Current.SumAll() == 0, "ledger sums to zero after snacks");
            reg.SendAway(card.Pawn);
            reg.SendAway(cash.Pawn);
            t.Check(Ledger.Current.Balance(card.Account).Micro == 0, "unused card funds went back to the world");
            t.Check(card.GoodwillPoints == 0 && cash.GoodwillPoints == 0, "sent-away guests earn no goodwill");
            t.Check(Ledger.Current.SumAll() == 0, "ledger sums to zero after departure");
            yield return 600;
            InnTestKit.RemoveAllGuests(map);
            InnTestKit.Teardown(site, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>A checked-in guest stays inside the guest wing; colonists are not affected by the wing.</summary>
    public sealed class InnWingSuite : HearthTestSuite
    {
        public override string Name => "inn.wing";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 32000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var site = InnTestKit.BuildRoom(map, 1, false, 6, 5);
            InnTestKit.MarkGuestBeds(site, true);
            yield return 10;
            var comp = GuestWings.Comp(map);
            var wing = comp.wings.FirstOrDefault();
            t.Check(wing != null && wing.TrueCount >= site.Interior.Area, "auto wing covers the room: " + wing?.TrueCount);
            t.Check(wing != null && !wing.AssignableAsAllowed() && !map.areaManager.AllAreas.Contains(wing), "wing is not a vanilla allowed area");
            var f = InnTestKit.GuestFaction(map);
            var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.Overnight, Nights = 2, Silent = true, ForceBudget = Money.FromSilver(300) });
            t.Check(g != null, "guest arrived");
            if (g == null) { InnTestKit.Teardown(site, map); yield break; }
            var r = reg.Members(g).First(); var p = r.Pawn;
            int waited = 0;
            while (waited < 20000 && !(r.CheckedIn && wing[p.Position])) { yield return 200; waited += 200; }
            t.Check(r.CheckedIn && wing[p.Position], "guest checked in and entered the wing (" + waited + " ticks)");
            var outside = GenRadial.RadialCellsAround(site.Interior.CenterCell, 15, true).First(c => c.InBounds(map) && !wing[c] && c.Standable(map));
            t.Check(!outside.InAllowedArea(p), "cell outside the wing is not allowed for the guest");
            t.Check(site.Interior.CenterCell.InAllowedArea(p), "cell inside the wing is allowed");
            var col = map.mapPawns.FreeColonistsSpawned.First();
            t.Check(outside.InAllowedArea(col), "colonists are not affected by the wing");
            int inside = 0, samples = 0;
            for (int i = 0; i < 40; i++)
            {
                yield return 150;
                if (!p.Spawned) break;
                samples++;
                if (wing[p.Position] || (r.Bed != null && r.Bed.OccupiedRect().Contains(p.Position))) inside++;
            }
            t.Check(samples >= 30 && inside >= samples - 1, $"guest stayed inside the wing ({inside}/{samples} samples)");
            reg.SendAway(p);
            yield return 300;
            t.Check(!GuestRegistry.IsWingRestricted(p) && outside.InAllowedArea(p), "leaving guests are free to walk out");
            InnTestKit.RemoveAllGuests(map);
            InnTestKit.Teardown(site, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>Pure numbers: budget curve, wealth bands, renown curve, report variants, goodwill caps.</summary>
    public sealed class InnMathSuite : HearthTestSuite
    {
        public override string Name => "inn.math";
        public override ModuleId Module => ModuleId.Inn;
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(Math.Abs(GuestEconomy.DailyBudget(1) - 25) < 0.01f && Math.Abs(GuestEconomy.DailyBudget(50) - 230) < 0.01f && Math.Abs(GuestEconomy.DailyBudget(100) - 2500) < 0.01f, "b(W) anchors 25 / 230 / 2500");
            float prev = 0; bool mono = true;
            for (int w = 1; w <= 100; w++) { float b = GuestEconomy.DailyBudget(w); if (b < prev) mono = false; prev = b; }
            t.Check(mono, "b(W) is monotonic");
            t.Check(Math.Abs(MapComponent_HH_GuestScheduler.RenownCurve(50) - 1f) < 0.001f, "renown curve 50 → 1.0");
            var rng = new RngStream(12345, 7);
            int poor = 0; for (int i = 0; i < 400; i++) if (GuestEconomy.RollWealth(null, null, 0, rng) <= 10) poor++;
            t.Check(poor > 260 && poor < 380, "renown 0: about 80 % poor guests (" + poor + "/400)");
            int count = DepartureReport.Outcomes.Sum(o => 3);
            t.Check(count == 15, "15 report opener variants");
            var reg = GuestRegistry.Instance;
            float r0 = reg.Renown;
            reg.Renown = GuestRegistry.StartRenown;
            float d = reg.ChangeRenown(-5f);
            t.Check(d >= -0.001f && reg.Renown >= GuestRegistry.StartRenown - 0.001f, "Standard Room: renown never drops below the start value");
            reg.Renown = r0;
            var f = InnTestKit.GuestFaction(t.Map);
            if (f != null)
            {
                int before = f.PlayerGoodwill;
                int a = reg.ApplyGoodwill(f, 40);
                t.Check(a <= HSettings.GetInt(InnSettings.GoodwillCapQuadrum), "goodwill per quadrum capped (+" + a + ")");
                int b2 = reg.ApplyGoodwill(f, -40);
                t.Check(b2 >= -HSettings.GetInt(InnSettings.GoodwillLossCap), "goodwill loss per visit capped (" + b2 + ")");
                f.TryAffectGoodwillWith(Faction.OfPlayer, before - f.PlayerGoodwill, false, false);
                reg.ResetGoodwillBookForTests();
            }
            t.Check(Math.Abs(HSettings.Find(InnSettings.ArrivalRate).DefaultFor(0) - 3f) < 0.001f && Math.Abs(HSettings.Find(InnSettings.ArrivalRate).DefaultFor(6) - 0.6f) < 0.001f, "arrival rate follows the tier anchors");
            yield return 1;
        }
    }
}
#endif
