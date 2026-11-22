// SPDX-License-Identifier: MIT
// In-game suites of the Inn integration round (built only with HH_TESTS).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.ShopsFood;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Guests
{
    static class InnIntKit
    {
        static readonly Dictionary<VenueTestKit.TestRoom, InnTestKit.Site> sites = new Dictionary<VenueTestKit.TestRoom, InnTestKit.Site>();

        /// <summary>A walled, roofed shop room built with the Inn's own kit (it never destroys motes or pawns on the site).</summary>
        public static VenueTestKit.TestRoom Shop(Map map, out Thing sign, out CompVenueSignHH comp, out Dictionary<Pawn, int> haulWas)
        {
            var site = InnTestKit.BuildRoom(map, 0, false, 5, 5);
            var room = new VenueTestKit.TestRoom { Inner = site.Interior, Outer = site.Interior.ExpandedBy(1) };
            room.Things.AddRange(site.Built);
            sites[room] = site;
            sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = true;
            var s = sign;
            var cells = room.Inner.Cells.Where(c => c != s.Position).Take(5).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            haulWas = map.mapPawns.FreeColonistsSpawned.ToDictionary(c => c, c => c.workSettings?.GetPriority(WorkTypeDefOf.Hauling) ?? 0);
            foreach (var c in haulWas.Keys) c.workSettings?.SetPriority(WorkTypeDefOf.Hauling, 0);
            var defs = new[] { ThingDefOf.Steel, ThingDefOf.Cloth, ThingDefOf.MedicineHerbal, ThingDefOf.WoodLog, ThingDefOf.ComponentIndustrial };
            for (int i = 0; i < defs.Length && i < cells.Count; i++) VenueTestKit.Stock(map, defs[i], 40, cells[i], room);
            Venues.MarkDirty(map);
            return room;
        }

        public static void EndShop(Map map, VenueTestKit.TestRoom room, Dictionary<Pawn, int> haulWas)
        {
            if (haulWas != null) foreach (var kv in haulWas) kv.Key.workSettings?.SetPriority(WorkTypeDefOf.Hauling, kv.Value);
            if (room == null) return;
            VenueTestKit.Teardown(map, room);
            if (sites.TryGetValue(room, out var site)) { InnTestKit.Teardown(site, map); sites.Remove(room); }
        }

        public static void ClearAll(Map map)
        {
            InnTestKit.RemoveAllGuests(map);
            VenueTestKit.ClearGuests(map);
        }
    }

    /// <summary>One brain per guest: vanilla visitors are driven by GuestFallback (Inn on and off), Inn guests only by their own think tree.</summary>
    public sealed class InnFallbackSuite : HearthTestSuite
    {
        public override string Name => "inn.fallback";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 50000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            InnIntKit.ClearAll(map);
            var factions = VenueTestKit.Friendly(1, false);
            t.Check(factions.Count > 0, "a friendly faction exists");
            if (factions.Count == 0) yield break;
            var f = factions[0];
            var room = InnIntKit.Shop(map, out var sign, out var comp, out var haulWas);
            bool innWasOn = Modules.On(ModuleId.Inn);
            try
            {
                yield return 30;
                t.Check(Modules.On(ModuleId.Inn) && Modules.On(ModuleId.ShopsFood), "Inn and Venues modules on");
                // an Inn day guest: driven by the Inn's think tree, never by the fallback
                var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.DayVisitor, Silent = true, IgnoreDoorPolicy = true, StayTicksOverride = 40000, ForceBudget = Money.FromSilver(300) });
                t.Check(g != null, "Inn day guest arrived");
                var innPawn = g != null ? reg.Members(g).First().Pawn : null;
                t.Check(innPawn != null && !GuestFallback.Drives(innPawn), "fallback does not drive the Inn guest");

                int j0 = GuestFallback.JobsStarted, sales0 = comp.salesCount;
                var visitors = VenueTestKit.SpawnVisitors(map, f, VenueTestKit.OutsideCell(map, room));
                t.Check(visitors.Count > 0, "vanilla visitor group spawned: " + visitors.Count);
                t.Check(visitors.All(v => GuestApi.IsGuest(v) && !GuestRegistry.IsRegisteredGuest(v)), "vanilla visitors count as day visitors with the Inn on, without an Inn file");
                int waited = 0;
                while (waited < 24000 && !(visitors.Any(v => GuestFallback.Driven.Contains(v.thingIDNumber)) && comp.salesCount > sales0)) { yield return 200; waited += 200; }
                t.Check(visitors.Any(v => GuestFallback.Driven.Contains(v.thingIDNumber)), $"fallback offered an activity to a vanilla visitor with the Inn on ({GuestFallback.JobsStarted - j0} jobs, {waited} ticks)");
                if (comp.salesCount <= sales0)
                    t.Note("no sale: last fallback job " + GuestFallback.LastJob?.def.defName + " on " + GuestFallback.LastJob?.targetA.Thing?.def.defName
                        + "; venues on map: " + string.Join(", ", Venues.All(map).Select(v => v.Type?.defName + (v.IsOpenNow ? "(open)" : "(closed)") + "@" + v.Sign?.Position))
                        + "; visitors: " + string.Join(", ", visitors.Select(v => v.LabelShort + ":" + (v.Spawned ? v.CurJob?.def.defName : "gone"))));
                t.Check(comp.salesCount > sales0, "the shop sold something (" + (comp.salesCount - sales0) + " sales)");
                t.Check(innPawn == null || !GuestFallback.Driven.Contains(innPawn.thingIDNumber), "the Inn guest was never offered a second activity by the fallback");

                // Inn off: the same fallback still drives vanilla visitors
                if (g != null) reg.SendAway(innPawn);
                InnTestKit.RemoveAllGuests(map);
                VenueTestKit.ClearGuests(map);
                HearthProfile.Current.SetModule(ModuleId.Inn, false);
                t.Check(!Modules.On(ModuleId.Inn), "Inn module switched off");
                GuestFallback.ResetOffers();
                var visitors2 = VenueTestKit.SpawnVisitors(map, f, VenueTestKit.OutsideCell(map, room));
                t.Check(visitors2.Count > 0 && visitors2.All(GuestApi.IsGuest), "visitors are guests without the Inn: " + visitors2.Count);
                waited = 0;
                while (waited < 15000 && !visitors2.Any(v => GuestFallback.Driven.Contains(v.thingIDNumber))) { yield return 200; waited += 200; }
                t.Check(visitors2.Any(v => GuestFallback.Driven.Contains(v.thingIDNumber)), "fallback drives vanilla visitors with the Inn off (" + waited + " ticks)");
            }
            finally
            {
                HearthProfile.Current.SetModule(ModuleId.Inn, innWasOn);
                InnIntKit.ClearAll(map);
                InnIntKit.EndShop(map, room, haulWas);
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }

    /// <summary>Door policy (5.9): filters work already at generation; the one-liners aim at the player.</summary>
    public sealed class InnDoorSuite : HearthTestSuite
    {
        public override string Name => "inn.door";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var door = reg.Door;
            door.Reset();
            try
            {
                t.Check(door.IsDefault && door.Quip() == "Woke, but with limits, huh?", "default: " + door.Quip());
                door.AllowAll = true;
                t.Check(door.Quip() == "Ugh. A hippie.", "allow all: " + door.Quip());
                door.AllowAll = false;
                var f = InnTestKit.GuestFaction(map);
                t.Check(f != null, "guest faction " + f?.Name);
                if (f == null) yield break;
                // gender, through the generation request
                door.GenderMode = FilterMode.OnlyThese; door.Gender = Gender.Female;
                t.Check(door.Quip().Contains("sexist"), "gender filter one-liner: " + door.Quip());
                var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 3, Kind = StayKind.DayVisitor, Silent = true, StayTicksOverride = 3000 });
                t.Check(g != null, "group generated under the gender filter");
                var pawns = g != null ? reg.Members(g).Select(r => r.Pawn).ToList() : new List<Pawn>();
                t.Check(pawns.Count > 0 && pawns.All(p => p.gender == Gender.Female), "only women came: " + string.Join(", ", pawns.Select(p => p.gender)));
                if (g != null) reg.SendAway(pawns[0]);
                InnTestKit.RemoveAllGuests(map);
                // age, by request range
                door.Reset(); door.MinAge = 40;
                g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 2, Kind = StayKind.DayVisitor, Silent = true, StayTicksOverride = 3000 });
                pawns = g != null ? reg.Members(g).Select(r => r.Pawn).ToList() : new List<Pawn>();
                t.Check(pawns.Count > 0 && pawns.All(p => p.ageTracker.AgeBiologicalYears >= 40), "age ≥ 40: " + string.Join(", ", pawns.Select(p => p.ageTracker.AgeBiologicalYears)));
                t.Check(door.Quip().StartsWith("Ageist"), "age one-liner: " + door.Quip());
                if (g != null) reg.SendAway(pawns[0]);
                InnTestKit.RemoveAllGuests(map);
                // wealth band when the guest's wealth is rolled
                door.Reset(); door.MinWealth = 70; door.MaxWealth = 100;
                g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 2, Kind = StayKind.DayVisitor, Silent = true, StayTicksOverride = 3000 });
                var recs = g != null ? reg.Members(g).ToList() : new List<GuestRecord>();
                t.Check(recs.Count > 0 && recs.All(r => r.Wealth >= 70), "wealth band 70–100: " + string.Join(", ", recs.Select(r => r.Wealth)));
                t.Check(recs.Count > 0 && recs.All(r => r.Vip), "wealth ≥ 60 → VIP flag");
                if (g != null) reg.SendAway(recs[0].Pawn);
                InnTestKit.RemoveAllGuests(map);
                // faction and stay kind: refused before anybody is generated
                door.Reset(); door.FactionMode = FilterMode.ExceptThese; door.Factions.Add(f);
                t.Check(GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.DayVisitor, Silent = true }) == null, "excluded faction does not come");
                t.Check(door.Quip() == "Holding a grudge? Classy.", "faction one-liner: " + door.Quip());
                t.Check(GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.DayVisitor, Silent = true, IgnoreDoorPolicy = true, StayTicksOverride = 2000 }) != null, "dev arrivals may ignore the policy");
                InnTestKit.RemoveAllGuests(map);
                door.Reset(); door.AllowDay = false;
                t.Check(GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.DayVisitor, Silent = true }) == null, "day visitors switched off");
                t.Check(door.Quip() == "Commitment issues, much?", "stay-kind one-liner");
                door.Reset();
                // the window: open, change, close → one-liner
                DoorPolicyHub.LastQuip = null;
                var dlg = new Dialog_HH_DoorPolicy(door);
                Find.WindowStack.Add(dlg);
                yield return 5;
                t.Check(Find.WindowStack.IsOpen(dlg), "door policy window opens");
                door.MinTech = TechLevel.Industrial;
                dlg.Close();
                yield return 2;
                t.Check(DoorPolicyHub.LastQuip == "Too primitive for your fine dining? Noted.", "closing the window shows the one-liner: " + DoorPolicyHub.LastQuip);
                t.Check(!door.FactionOk(Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.def.techLevel == TechLevel.Neolithic && !x.IsPlayer)) , "tech filter keeps neolithic factions out");
            }
            finally
            {
                door.Reset();
                InnTestKit.RemoveAllGuests(map);
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }

    /// <summary>Wing status line (N31) on the bed, the desk and in the overview.</summary>
    public sealed class InnWingStatusSuite : HearthTestSuite
    {
        public override string Name => "inn.wingstatus";
        public override ModuleId Module => ModuleId.Inn;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var site = InnTestKit.BuildRoom(map, 1, true, 5, 4);
            InnTestKit.MarkGuestBeds(site, true);
            yield return 10;
            WingStatus.ClearCache();
            var lines = WingStatus.Lines(map).ToList();
            t.Check(lines.Count >= 1, "one status line per wing: " + string.Join(" | ", lines));
            string line = lines.FirstOrDefault() ?? "";
            t.Check(line.StartsWith("Guest Wing") && line.Contains("Bedroom ✓"), "bedroom ok: " + line);
            t.Check(line.Contains("Kitchen") && line.Contains("Restaurant"), "kitchen and restaurant listed");
            t.Check(line.Contains("est. ★"), "star estimate shown");
            t.Check(site.Beds[0].GetInspectString().Contains("Guest Wing"), "guest bed inspect string carries the wing line");
            t.Check(site.Desk.GetInspectString().Contains("Bedroom ✓"), "front desk inspect string carries the wing line");
            // open the roof: the bedroom check fails
            foreach (var c in site.Interior) map.roofGrid.SetRoof(c, null);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            yield return 5;
            WingStatus.ClearCache();
            line = WingStatus.Lines(map).FirstOrDefault() ?? "";
            t.Check(line.Contains("Bedroom ✗"), "unroofed room fails the bedroom check: " + line);
            InnTestKit.Teardown(site, map);
            WingStatus.ClearCache();
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>Contracts with other modules: renown/whispers API, Underworld hooks, Hospitality import, UI probe, VIP, planned-leave changes.</summary>
    public sealed class InnContractsSuite : HearthTestSuite
    {
        public override string Name => "inn.contracts";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            float renown0 = reg.Renown;
            var site = InnTestKit.BuildRoom(map, 2, false, 6, 4);
            try
            {
                // renown / whispers read access
                t.Check(GuestApi.HasRenown, "GuestApi has a renown source");
                reg.Renown = 40f;
                t.Check(Math.Abs(GuestApi.Renown(map) - 40f) < 0.001f && GuestApi.RenownStars(map) == 3, "GuestApi.Renown = 40, 3 stars");
                float d = GuestApi.AddRenown(map, 2f, "test");
                t.Check(Math.Abs(d - 2f) < 0.001f && Math.Abs(reg.Renown - 42f) < 0.001f, "GuestApi.AddRenown +2");
                // Underworld scandal → renown (direct event)
                Hearth.Underworld.Underworld.RaiseRenown(map, -1.5f, "test scandal");
                t.Check(Math.Abs(reg.Renown - 40.5f) < 0.01f, "Underworld RenownDelta lowers renown: " + reg.Renown.ToString("0.00"));
                if (Modules.On(ModuleId.Underworld) && Hearth.Underworld.Underworld.Comp != null)
                {
                    float w0 = Hearth.Underworld.Underworld.Whispers;
                    reg.AddWhispers(3f, "test");
                    t.Check(Math.Abs(GuestApi.Whispers(map) - Hearth.Underworld.Underworld.Whispers) < 0.001f && Hearth.Underworld.Underworld.Whispers > w0, "whispers live in the Underworld module and are read through GuestApi");
                    Hearth.Underworld.Underworld.AddWhispers(-3f, "test undo");
                }
                else t.Note("Underworld module off: whispers stay in the Inn");
                // VIP flag
                t.Check(new GuestInfo { Wealth = 60 }.Vip && !new GuestInfo { Wealth = 59 }.Vip, "VIP from wealth 60");
                // Hospitality import: bed event → our guest bed
                var bed = site.Beds[0];
                t.Check(!GuestBeds.IsGuestBed(bed), "bed starts as a normal bed");
                Hearth.Compat.Hospitality.HospitalityImport.NotifyBedImported(bed, 25);
                yield return 5;
                t.Check(GuestBeds.IsGuestBed(bed), "imported Hospitality guest bed is adopted as guest bed");
                // UI probe
                t.Check(Hearth.Shell.HearthUI.GuestBedProbe != null && Hearth.Shell.HearthUI.GuestBedProbe(map), "HearthUI.GuestBedProbe sees the guest bed");
                // wing constructor the importer uses; a wing put into the vanilla area list moves into ours
                t.Check(typeof(Area_HH_GuestWing).GetConstructor(new[] { typeof(AreaManager) }) != null && typeof(Area_HH_GuestWing).GetConstructor(new[] { typeof(AreaManager), typeof(string) }) != null, "Area_HH_GuestWing has the importer's constructors");
                var foreign = new Area_HH_GuestWing(map.areaManager, "Imported guests");
                foreach (var c in site.Interior) foreign[c] = true;
                map.areaManager.AllAreas.Add(foreign);
                int adopted = GuestWings.Comp(map).AdoptForeignWings();
                t.Check(adopted == 1 && GuestWings.Comp(map).wings.Contains(foreign) && !map.areaManager.AllAreas.Contains(foreign), "imported wing adopted out of the vanilla area list");
                // shady stays are marked for the Underworld module
                var f = InnTestKit.GuestFaction(map);
                var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.DayVisitor, Silent = true, ForceShady = true, IgnoreDoorPolicy = true, StayTicksOverride = 20000 });
                t.Check(g != null, "shady guest arrived");
                var r = g != null ? reg.Members(g).First() : null;
                t.Check(r != null && r.Shady, "guest file marked shady");
                if (Modules.On(ModuleId.Underworld)) t.Check(r != null && Hearth.Underworld.Underworld.IsShady(r.Pawn), "Underworld.MarkShady called");
                // PlannedLeaveTick changed by another module (shrine bonus) is honoured both ways
                if (r != null)
                {
                    r.PlannedLeaveTick = Find.TickManager.TicksGame + 30000;
                    yield return 2600;
                    t.Check(!reg.Group(g.Id).LeaveSent, "extended stay: still here");
                    r.PlannedLeaveTick = Find.TickManager.TicksGame;
                    int waited = 0;
                    while (waited < 3000 && !(reg.Group(g.Id)?.LeaveSent ?? true)) { yield return 250; waited += 250; }
                    t.Check(reg.Group(g.Id) == null || reg.Group(g.Id).LeaveSent, "shortened stay: leaves at once");
                }
            }
            finally
            {
                reg.Renown = renown0;
                InnTestKit.RemoveAllGuests(map);
                InnTestKit.Teardown(site, map);
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }

    /// <summary>Day visitors (5.13) with venue opening hours: they come for an open venue, shop, and go at last orders.</summary>
    public sealed class InnDayVisitSuite : HearthTestSuite
    {
        public override string Name => "inn.dayvisit";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 40000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            InnIntKit.ClearAll(map);
            var room = InnIntKit.Shop(map, out var sign, out var comp, out var haulWas);
            try
            {
                yield return 30;
                // open now for the next 6 hours
                int h = GenLocalDate.HourOfDay(map);
                comp.alwaysOpen = false; comp.openHour = h; comp.closeHour = (h + 6) % 24;
                var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
                t.Check(sch.DayVisitsPossible && sch.Active, "a venue makes day visits possible (no guest bed needed)");
                sch.OpeningWindow(out int open, out int close);
                t.Check(open <= h && close >= h + 1, $"opening window {open}–{close} covers now ({h})");
                t.Check(GuestRegistry.AnyVenueOpenSoon(map), "venue open now and in 30 minutes");
                var f = InnTestKit.GuestFaction(map);
                int sales0 = comp.salesCount;
                var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 2, Kind = StayKind.DayVisitor, Silent = true, ForceBudget = Money.FromSilver(300) });
                t.Check(g != null, "day visitors arrived");
                if (g == null) yield break;
                var members = reg.Members(g).ToList();
                int closeTick = Find.TickManager.TicksGame + (int)((((comp.closeHour - GenLocalDate.HourFloat(map)) + 24f) % 24f) * 2500f);
                t.Check(members.All(m => m.PlannedLeaveTick <= closeTick), "planned to leave before closing time");
                t.Check(members.All(m => !m.Overnight && m.Bed == null), "no beds for day visitors");
                int waited = 0;
                while (waited < 20000 && comp.salesCount == sales0) { yield return 250; waited += 250; }
                t.Check(comp.salesCount > sales0, $"a day visitor shopped during opening hours ({waited} ticks, Inn brain)");
                t.Check(members.All(m => !GuestFallback.Driven.Contains(m.PawnId)), "Inn day visitors are not driven by the fallback");
                // the venue closes: last orders, the group leaves
                if (!g.LeaveSent)
                {
                    int now = GenLocalDate.HourOfDay(map);
                    comp.openHour = (now + 2) % 24; comp.closeHour = (now + 3) % 24;
                    t.Check(!GuestRegistry.AnyVenueOpenSoon(map), "venue closed now");
                    waited = 0;
                    while (waited < 3000 && !g.LeaveSent) { yield return 250; waited += 250; }
                    t.Check(g.LeaveSent && (g.LeaveReason == "closing" || g.LeaveReason == "planned"), "day visitors leave when the venue closes (" + g.LeaveReason + ", " + waited + " ticks)");
                }
                else t.Note("group left before the closing check (" + g.LeaveReason + ")");
            }
            finally
            {
                InnIntKit.ClearAll(map);
                InnIntKit.EndShop(map, room, haulWas);
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }

    /// <summary>5.13/5.14: honesty box, waiting thoughts, return visits and the gift on leaving.</summary>
    public sealed class InnReturnsSuite : HearthTestSuite
    {
        public override string Name => "inn.returns";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            var site = InnTestKit.BuildRoom(map, 1, true, 5, 4);
            InnTestKit.MarkGuestBeds(site, true);
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
            try
            {
                yield return 10;
                t.Check(Math.Abs(GuestReturns.ReturnDays(0.5f, 1f) - 5f) < 0.01f && Math.Abs(GuestReturns.ReturnDays(-0.5f, 0f) - 30f) < 0.01f && Math.Abs(GuestReturns.ReturnDays(0f, 0f) - 14f) < 0.01f,
                    "return days: 2·travel + Lerp(10, 3) / Lerp(14, 30)");
                t.Check(Math.Abs(GuestReturns.GiftChance(0f) - 0.2f) < 0.001f && Math.Abs(GuestReturns.GiftChance(1f) - 0.9f) < 0.001f, "gift chance Lerp(0.2, 0.9, V)");
                // waiting thoughts through the venues' satisfaction outcome
                var f = InnTestKit.GuestFaction(map);
                var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.Overnight, Nights = 1, Silent = true, ForceBudget = Money.FromSilver(400), IgnoreDoorPolicy = true });
                t.Check(g != null, "guest arrived");
                if (g == null) yield break;
                var r = reg.Members(g).First(); var p = r.Pawn;
                GuestApi.AddSatisfaction(p, -0.03f, "HH.Venue.NobodyCameBell".TranslateOr("Rang the bell. Nobody came."));
                t.Check(p.needs.mood.thoughts.memories.GetFirstMemoryOfDef(InnDefOf.HH_GuestBellNobody) != null, "\"Rang the bell. Nobody came.\" thought");
                GuestThoughts.Waited(p, bell: false, nobodyCame: true, room: null);
                t.Check(p.needs.mood.thoughts.memories.GetFirstMemoryOfDef(InnDefOf.HH_GuestWaited) != null, "waited-in-vain thought");
                // honesty box: desk closed, nobody on duty
                int waited = 0;
                while (waited < 20000 && !r.CheckedIn) { yield return 250; waited += 250; }
                t.Check(r.CheckedIn, "checked in (" + waited + " ticks)");
                bool honesty = r.Events.Any(e => e.Contains("honesty"));
                t.Note("desk " + (GuestThoughts.DeskOpenNow(map) ? "open" : "closed") + " at check-in; honesty box used: " + honesty);
                if (honesty) t.Check(r.Events.Any(e => e.Contains("honesty-box price")), "honesty-box price applied");
                // leaving: return visit planned, the pawn is kept as world pawn; gift chance forced via high value
                r.Value = 1f; r.MoodSum = 0.9f * Math.Max(1, r.MoodSamples); r.Expectation = 0.2f;
                int forecast0 = sch.Forecast.Count(x => x.IsReturn);
                int items0 = map.listerThings.AllThings.Count;
                foreach (var m in reg.Members(g)) m.PlannedLeaveTick = Find.TickManager.TicksGame;
                waited = 0;
                while (waited < 3000 && !g.LeaveSent) { yield return 250; waited += 250; }
                t.Check(g.LeaveSent, "group left");
                var back = sch.Forecast.FirstOrDefault(x => x.IsReturn && x.Returning.Contains(p));
                t.Check(back != null, "return visit planned: " + back?.Describe(map));
                t.Check(back == null || (back.ArrivalTick - Find.TickManager.TicksGame) / 60000f >= 2.9f, "return in ≥ 3 days");
                t.Check(Find.WorldPawns.ForcefullyKeptPawns.Contains(p), "returning guest is kept as world pawn");
                t.Note("gift: " + (r.Gift ?? "none (chance roll)"));
                // the return arrives with the same person
                waited = 0;
                while (waited < 15000 && p.Spawned) { yield return 500; waited += 500; }
                t.Check(!p.Spawned, "guest walked off the map");
                if (back != null && !p.Spawned)
                {
                    var req = new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.Overnight, Nights = 1, Silent = true, Returning = new List<Pawn>(back.Returning), IgnoreDoorPolicy = true };
                    t.Check(GuestReturns.CanReturn(p, f), "returning guest can come back");
                    var g2 = GuestArrival.TryFire(map, req);
                    t.Check(g2 != null && reg.Members(g2).Any(m => m.Pawn == p && m.Returner), "the same guest came back as returner");
                    GuestReturns.Unpin(back.Returning);
                }
            }
            finally
            {
                foreach (var x in sch.Forecast.Where(x => x.IsReturn).ToList()) GuestReturns.Unpin(x.Returning);
                InnTestKit.RemoveAllGuests(map);
                InnTestKit.Teardown(site, map);
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }
}
#endif
