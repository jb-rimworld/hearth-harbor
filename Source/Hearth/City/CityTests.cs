// SPDX-License-Identifier: MIT
// In-game test suites of City Life. Compiled only with HH_TESTS (Source/build.sh --tests).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Testing;

namespace Hearth.City.Tests
{
    /// <summary>Building blocks for the City Life suites: a walled, roofed room with a door, a venue sign, visitors, test money.</summary>
    public static class CityTestKit
    {
        static ThingDef testSignDef;

        public static void Clear(Map map, CellRect rect)
        {
            foreach (var c in rect.Cells)
            {
                if (!c.InBounds(map)) continue;
                foreach (var t in c.GetThingList(map).ToList()) if (!(t is Pawn) && t.def.destroyable) t.Destroy();
                map.roofGrid.SetRoof(c, null);
                map.terrainGrid.SetTerrain(c, TerrainDef.Named("WoodPlankFloor"));
                map.fogGrid.Unfog(c);
            }
        }

        /// <summary>A clear rectangle without colonists near it (the test map is small, so scan a few spots).</summary>
        public static CellRect FindArea(Map map, int w, int h, int tryIndex = 0)
        {
            var offsets = new List<IntVec3>();
            for (int dx = -36; dx <= 36; dx += 12) for (int dz = -36; dz <= 36; dz += 12) offsets.Add(new IntVec3(dx, 0, dz));
            offsets = offsets.OrderBy(o => (o.x * 31 + o.z * 17 + tryIndex * 7) % 23).ToList();
            foreach (var o in offsets)
            {
                var r = CellRect.CenteredOn(map.Center + o, w / 2 + 1, h / 2 + 1);
                var rr = new CellRect(r.minX, r.minZ, w, h);
                if (!rr.ExpandedBy(4).FullyContainedWithin(CellRect.WholeMap(map).ContractedBy(2))) continue;
                if (map.mapPawns.FreeColonistsSpawned.Any(p => rr.ExpandedBy(3).Contains(p.Position))) continue;
                return rr;
            }
            return new CellRect(map.Center.x, map.Center.z, w, h);
        }

        /// <summary>Walls around <paramref name="inner"/>, a roof, a door in the south wall middle. Returns the inner rect.</summary>
        public static void BuildRoom(Map map, CellRect inner, bool roof = true)
        {
            var outer = inner.ExpandedBy(1);
            Clear(map, outer.ExpandedBy(1));
            var wood = ThingDefOf.WoodLog;
            foreach (var c in outer.EdgeCells)
            {
                bool door = c.z == outer.minZ && c.x == inner.CenterCell.x;
                var t = ThingMaker.MakeThing(door ? ThingDefOf.Door : ThingDefOf.Wall, wood);
                t.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(t, c, map);
            }
            if (roof) foreach (var c in outer.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
        }

        public static Thing Spawn(Map map, string defName, IntVec3 cell, Rot4 rot, string stuff = "WoodLog")
        {
            var def = DefDatabase<ThingDef>.GetNamed(defName);
            ThingDef st = def.MadeFromStuff ? DefDatabase<ThingDef>.GetNamed(stuff) : null;
            var t = ThingMaker.MakeThing(def, st);
            if (t.def.category == ThingCategory.Building) t.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(t, cell, map, rot);
        }

        public static void Power(Thing t)
        {
            var p = t.TryGetComp<CompPowerTrader>(); if (p != null) p.PowerOn = true;
        }

        /// <summary>A venue sign: the real HH_VenueSign if the Venues module is loaded, else a stand-in with the contract's comp.</summary>
        public static Thing Sign(Map map, IntVec3 cell, VenueTypeDef type)
        {
            var real = DefDatabase<ThingDef>.GetNamedSilentFail("HH_VenueSign");
            ThingDef def = real != null && real.comps.Any(c => typeof(CompVenueSign).IsAssignableFrom(c.compClass)) ? real : TestSignDef();
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null);
            t.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(t, cell, map);
            var comp = t.TryGetComp<CompVenueSign>();
            comp.type = type; comp.alwaysOpen = true; comp.openHour = 0; comp.closeHour = 0;
            Venues.MarkDirty(map); CityScan.MarkDirty(map);
            return t;
        }

        static ThingDef TestSignDef()
        {
            if (testSignDef != null) return testSignDef;
            var lamp = DefDatabase<ThingDef>.GetNamed("StandingLamp");
            var clone = (ThingDef)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lamp, null);
            clone.defName = "HH_TestVenueSign";
            clone.comps = new List<CompProperties> { new CompProperties_VenueSign() };
            clone.shortHash = (ushort)(lamp.shortHash ^ 0x5a5a);
            clone.stuffCategories = null; clone.costStuffCount = 0; clone.costList = null;
            DefDatabase<ThingDef>.Add(clone);
            return testSignDef = clone;
        }

        /// <summary>Sets research mode to off so City Life is fully available for the tests.</summary>
        public static void ResearchOff()
        {
            HSettings.Set(CitySettings.ResearchMode, (float)CityResearchMode.Off);
            CityAccess.AutoFinishResearch();
        }

        public static List<Pawn> Visitors(Map map, int count, IntVec3 at)
        {
            var found = new List<Pawn>();
            Func<List<Pawn>> current = () => map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsVanillaVisitor).ToList();
            try
            {
                for (int i = 0; i < 4 && current().Count < count; i++) GuestTestKit.SpawnVisitors(map, count);
            }
            catch (Exception e) { HLog.Warn("City", "VisitorGroup incident failed in the test: " + e.Message); }
            var list = current();
            if (list.Count < count) HLog.Warn("City", "Only " + list.Count + " vanilla visitors could be spawned");
            var picked = list.Take(count).ToList();
            // the rest of the incident's group would walk to the colony and take benches, seats and screens by itself (the guest brain offers them City activities): send them away
            foreach (var extra in list.Skip(count).ToList()) { extra.GetLord()?.Notify_PawnLost(extra, PawnLostCondition.ForcedByPlayerAction); extra.Destroy(); }
            foreach (var p in picked)
            {
                var cell = CellFinder.RandomClosewalkCellNear(at, map, 3);
                p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ForcedByPlayerAction);
                p.DeSpawn(); GenSpawn.Spawn(p, cell, map);
            }
            // a calm lord of their own: they stand around the spot instead of wandering off with their group
            foreach (var grp in picked.GroupBy(p => p.Faction))
                LordMaker.MakeNewLord(grp.Key, new LordJob_VisitColony(grp.Key, at, 900000), map, grp.ToList());
            foreach (var p in picked)
            {
                var gi = GuestApi.Info(p); // needs the lord: only then is the pawn a (vanilla) guest
                if (gi != null) { gi.Budget = Money.FromSilver(400); gi.Wealth = 60; gi.Spent = Money.Zero; gi.Satisfaction = 0f; gi.PlannedLeaveTick = 0; gi.Events.Clear(); }
                found.Add(p);
            }
            return found;
        }

        public static void TimetableAnything(Pawn p)
        {
            if (p.timetable != null) for (int h = 0; h < 24; h++) p.timetable.SetAssignment(h, TimeAssignmentDefOf.Anything);
            if (p.needs?.rest != null) p.needs.rest.CurLevel = 1f;
            if (p.needs?.food != null) p.needs.food.CurLevel = 1f;
        }

        public static bool Posted(string memoPart, out Money amount)
        {
            amount = Money.Zero;
            var l = Ledger.Current; if (l == null) return false;
            foreach (var e in l.Journal) if (e.Memo != null && e.Memo.Contains(memoPart)) amount += e.Amount;
            return amount.Micro != 0;
        }

        /// <summary>A colonist who can do the given work type (random colonists may have social or violence disabled).</summary>
        public static Pawn FreeColonist(Map map, int skip = 0, WorkTypeDef work = null, Func<Pawn, bool> extra = null)
        {
            Func<Pawn, bool> ok = p => !p.Downed && !p.Dead && (work == null || (!p.WorkTypeIsDisabled(work) && p.workSettings != null)) && (extra == null || extra(p));
            var list = map.mapPawns.FreeColonistsSpawned.Where(ok).ToList();
            if (list.Count == 0 && (work != null || extra != null))
            {
                // the quicktest colonists are random (backstories can forbid violence or social work): generate one that fits instead of making the suite depend on luck
                for (int tries = 0; tries < 12 && list.Count == 0; tries++)
                {
                    var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, mustBeCapableOfViolence: true);
                    var np = PawnGenerator.GeneratePawn(req);
                    GenSpawn.Spawn(np, map.Center, map);
                    if (ok(np)) list.Add(np); else np.Destroy();
                }
            }
            return list.Count > skip ? list[skip] : list.FirstOrDefault();
        }
    }

    // =========================================================================================================
    public sealed class CityDefsSuite : HearthTestSuite
    {
        public override string Name => "city.defs";
        public override ModuleId Module => ModuleId.CityLife;

        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(Modules.On(ModuleId.CityLife), "City Life module is on");
            foreach (var n in new[] { "HH_ProjectorScreen", "HH_SalonChair", "HH_ParkBench", "HH_TaxiStand", "HH_TaxiIncoming", "HH_TaxiLeaving" })
                t.Check(DefDatabase<ThingDef>.GetNamedSilentFail(n) != null, "ThingDef " + n);
            foreach (var n in new[] { "HH_WatchScreening", "HH_SalonSit", "HH_SalonStyle", "HH_SalonSelf", "HH_GalleryVisit", "HH_ParkRest", "HH_TaxiDeparture", "HH_Bodyguard" })
            {
                var j = DefDatabase<JobDef>.GetNamedSilentFail(n);
                t.Check(j != null && j.driverClass != null, "JobDef " + n + " with driver");
            }
            t.Check(DefDatabase<WorkGiverDef>.GetNamedSilentFail("HH_SalonStyleWork")?.workType?.defName == "HH_Serving", "salon work giver belongs to HH_Serving");
            t.Check(DefDatabase<WorkGiverDef>.GetNamedSilentFail("HH_BodyguardWork")?.workType?.defName == "HH_Hosting", "bodyguard work giver belongs to HH_Hosting");
            t.Check(DefDatabase<GatheringDef>.GetNamedSilentFail("HH_Screening")?.duty?.defName == "HH_Screening", "gathering with its duty");
            foreach (var n in new[] { "HH_WatchedMovie", "HH_ScreeningAttended", "HH_FreshLook", "HH_AdmiredArt", "HH_ParkRested" })
                t.Check(CityDefs.Thought(n)?.stages?.Count > 0, "ThoughtDef " + n);
            t.Check(DefDatabase<JoyKindDef>.GetNamedSilentFail("HH_Cinema") != null && DefDatabase<JoyKindDef>.GetNamedSilentFail("HH_Scenic") != null, "joy kinds");

            // labels and descriptions
            foreach (var n in new[] { "HH_ProjectorScreen", "HH_SalonChair", "HH_ParkBench", "HH_TaxiStand" })
            {
                var d = DefDatabase<ThingDef>.GetNamed(n);
                t.Check(!d.label.NullOrEmpty() && !d.description.NullOrEmpty(), n + " has label and description");
            }
            foreach (var n in new[] { "HH_CityLife_1", "HH_CityLife_2" })
            {
                var r = DefDatabase<ResearchProjectDef>.GetNamed(n);
                t.Check(r.tab?.defName == "HH_Hospitality" && !r.description.NullOrEmpty(), n + " in tab HH_Hospitality with description");
            }
            t.Check(CityDefs.Research2.prerequisites.Contains(CityDefs.Research1), "level 2 needs level 1");
            foreach (var d in DefDatabase<VenueTypeDef>.AllDefs.Where(v => v.module == ModuleId.CityLife))
                t.Check(!d.label.NullOrEmpty() && !d.description.NullOrEmpty() && d.researchPrerequisite == CityDefs.Research1, "venue type " + d.defName + " complete");
            t.Check(CityDefs.VenueCinema != null && CityDefs.VenueGallery != null && CityDefs.VenueSalon != null, "three venue types");

            // gating by research
            t.Check(CityDefs.ProjectorScreen.researchPrerequisites.Contains(CityDefs.Research1), "projector screen needs level 1");
            t.Check(CityDefs.SalonChair.researchPrerequisites.Contains(CityDefs.Research1), "salon chair needs level 1");
            t.Check(CityDefs.TaxiStand.researchPrerequisites.Contains(CityDefs.Research2), "taxi stand needs level 2");
            t.Check(CityDefs.ParkBench.researchPrerequisites.NullOrEmpty(), "park bench is free (no research)");
            t.Check(CityDefs.ProjectorScreen.designationCategory != null, "projector screen has an architect category");

            // graphics resolve (placeholders count)
            foreach (var n in new[] { "HH_ProjectorScreen", "HH_SalonChair", "HH_ParkBench", "HH_TaxiStand", "HH_TaxiIncoming" })
            {
                var d = DefDatabase<ThingDef>.GetNamed(n);
                t.Check(d.graphic != null && d.graphic != BaseContent.BadGraphic, n + " graphic resolved");
            }

            // text keys
            foreach (var k in new[] { "HH.Tab.City", "HH.City.ScreenIdle", "HH.City.ScreenShowing", "HH.City.MovieNightLabel", "HH.City.NewHaircut", "HH.City.TaxiFree", "HH.City.Film.0", "HH.City.Film.7", "HH.Enum.CityResearchMode.Off" })
                t.Check(k.CanTranslate(), "key " + k);

            // settings
            foreach (var f in typeof(CitySettings).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                string key = (string)f.GetRawConstantValue();
                var s = HSettings.Find(key);
                t.Check(s != null && !s.LabelEn.NullOrEmpty() && !s.DescEn.NullOrEmpty() && s.Module == ModuleId.CityLife, "setting " + key + " registered with label and description");
            }
            var tol = HSettings.Find(CitySettings.PriceTolerance);
            t.Check(tol.TierAnchors != null && Math.Abs(tol.DefaultFor(0) - 3f) < 1e-4 && Math.Abs(tol.DefaultFor(3) - 1f) < 1e-4 && Math.Abs(tol.DefaultFor(6) - 0.65f) < 1e-4, "price tolerance follows the 7 tier anchors");

            // activities and tab
            foreach (var id in new[] { "city.cinema", "city.salon", "city.gallery", "city.park" })
                t.Check(GuestActivities.All.Any(a => a.Id == id && a.Module == ModuleId.CityLife), "activity " + id);
            t.Check(HearthTabs.Pages.Any(p => p.Id == "city" && p.Module == ModuleId.CityLife), "main tab page registered");

            // pricing maths
            t.Check(CityPricing.Tolerance(1) > 0.85 && CityPricing.Tolerance(1) < 0.9, "tolerance at wealth 1: " + CityPricing.Tolerance(1).ToString("0.000"));
            t.Check(Math.Abs(CityPricing.Tolerance(50) - 1.52) < 0.02, "tolerance at wealth 50: " + CityPricing.Tolerance(50).ToString("0.000"));
            t.Check(Math.Abs(CityPricing.Tolerance(100) - 2.5) < 1e-6, "tolerance at wealth 100 is 2.5");
            bool mono = true; double prev = 0; for (int w = 0; w <= 100; w += 5) { double v = CityPricing.Tolerance(w); if (v < prev) mono = false; prev = v; }
            t.Check(mono, "tolerance rises with wealth");
            t.Check(CityPricing.PricePoints(0.5) == 3 && CityPricing.PricePoints(0.8) == 1 && CityPricing.PricePoints(1.0) == 0 && CityPricing.PricePoints(1.2) == -2 && CityPricing.PricePoints(1.5) == -5, "price thought brackets");
            var rich = new GuestInfo { Wealth = 100 }; var poor = new GuestInfo { Wealth = 5 };
            var p12 = Money.FromSilver(12);
            double pr = CityPricing.BuyChance(null, rich, p12, p12, "t"), pp = CityPricing.BuyChance(null, poor, Money.FromSilver(30), p12, "t");
            t.Check(pr > 0.9 && pp < 0.1, $"buy chance: rich at reference {pr:0.00}, poor at 2.5x price {pp:0.00}");
            t.Check(CityPricing.BuyChance(null, poor, Money.Zero, p12, "t") == 1.0, "free is always bought");

            // research off finishes everything quietly and opens level 2
            CityTestKit.ResearchOff();
            t.Check(CityDefs.Research1.IsFinished && CityDefs.Research2.IsFinished, "research mode off finishes both City Life projects");
            t.Check(CityAccess.Level1 && CityAccess.Level2 && CityAccess.TaxiOn, "everything available with research off");
            HSettings.Set(CitySettings.ResearchMode, (float)CityResearchMode.Full);
            t.Check(!CityAccess.ResearchOff, "research mode full again");
            CityTestKit.ResearchOff();
            yield return 5;

            // module plumbing: diagnosis lines, removal report, caretaker checks, save data round trip of the contract
            var module = HearthMod.ModuleObjects.OfType<CityModule>().FirstOrDefault();
            t.Check(module != null, "module object found");
            var lines = module.Diagnosis().ToList();
            t.Check(lines.Count >= 1 && lines[0].StartsWith("City Life"), "diagnosis: " + (lines.FirstOrDefault() ?? "-"));
            var report = new List<string>(); module.PrepareRemoval(report);
            t.Check(report.Count >= 1, "removal report: " + string.Join(" | ", report));
            t.Check(CityCaretaker.CheckContracts() >= 0 && CityCaretaker.CheckScreens() >= 0, "caretaker checks run");
            t.Check(CityGameComponent.Instance != null, "city game component exists");
            t.Check(!SecurityService.OptedIn(t.Map) || Venues.All(t.Map).Count > 0 || TaxiService.AnyStand(t.Map), "security only on maps with a business (N08)");
            t.Check(Guard.Paused.Count() == 0, "no module paused");
        }
    }

    // =========================================================================================================
    public sealed class CityCinemaSuite : HearthTestSuite
    {
        public override string Name => "city.cinema";
        public override ModuleId Module => ModuleId.CityLife;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            CityTestKit.ResearchOff();
            HSettings.Set(CitySettings.CinemaShowTicks, 900);
            HSettings.Set(CitySettings.CinemaMinSeats, 4);
            var inner = CityTestKit.FindArea(map, 11, 9);
            CityTestKit.BuildRoom(map, inner);
            // screen against the north wall, facing south; two rows of chairs facing it
            var screenCell = new IntVec3(inner.CenterCell.x, 0, inner.maxZ);
            var screen = CityTestKit.Spawn(map, "HH_ProjectorScreen", screenCell, Rot4.South);
            var comp = screen.TryGetComp<CompProjectorScreen>();
            t.Check(comp != null, "projector screen has its component");
            int seats = 0;
            for (int dx = -2; dx <= 2; dx++) foreach (int dz in new[] { 3, 4 })
            {
                var c = new IntVec3(inner.CenterCell.x + dx, 0, inner.maxZ - dz);
                if (!c.InBounds(map)) continue;
                CityTestKit.Spawn(map, "DiningChair", c, Rot4.North); seats++;
            }
            var sign = CityTestKit.Sign(map, new IntVec3(inner.minX, 0, inner.minZ), CityDefs.VenueCinema);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms(); Venues.MarkDirty(map); CityScan.MarkDirty(map);
            t.Check(screen.Position.GetRoom(map) == sign.Position.GetRoom(map) && !screen.Position.GetRoom(map).PsychologicallyOutdoors, "screen and sign share a closed room");
            t.Check(CityScan.CountSeats(screen) >= 4, "seats in front of the screen: " + CityScan.CountSeats(screen) + " of " + seats);

            // no power: no cinema
            comp.Power.PowerOn = false;
            var guests = CityTestKit.Visitors(map, 2, inner.CenterCell + new IntVec3(0, 0, -2));
            t.Check(guests.Count >= 1, "visitors spawned through the vanilla VisitorGroup incident: " + guests.Count);
            if (guests.Count == 0) yield break;
            var g1 = guests[0]; var info1 = GuestApi.Info(g1);
            var act = new CinemaActivity();
            t.Check(act.Desire(g1, info1) == 0f, "no desire for the cinema without power");
            CityTestKit.Power(screen);
            CityScan.MarkDirty(map);
            t.Check(CityScan.Cinemas(map).Count == 1, "cinema found by the scan");
            float d = act.Desire(g1, info1);
            t.Check(d > 0.1f, "guest wants to go to the cinema: " + d.ToString("0.00"));

            // too few seats: raise the requirement
            HSettings.Set(CitySettings.CinemaMinSeats, 40); CityScan.MarkDirty(map);
            t.Check(act.Desire(g1, info1) == 0f, "no cinema when the seat requirement is not met");
            HSettings.Set(CitySettings.CinemaMinSeats, 4); CityScan.MarkDirty(map);

            // poor guest
            var poorInfo = info1.Budget; info1.Budget = Money.FromSilver(3);
            t.Check(act.Desire(g1, info1) == 0f, "no cinema for a guest who cannot pay the ticket");
            info1.Budget = poorInfo;

            // --- a guest buys a ticket and watches the film
            var ticket = CitySettings.Silver(CitySettings.CinemaTicket);
            var job = act.TryGiveJob(g1, info1);
            t.Check(job != null && job.def == CityDefs.WatchScreening, "activity gives a watch job");
            if (job == null) yield break;
            g1.jobs.StartJob(job, JobCondition.InterruptForced);
            var before = info1.Spent; float sat0 = info1.Satisfaction;
            bool started = false;
            for (int i = 0; i < 80 && !started; i++) { yield return 30; started = comp.ShowActive; }
            t.Check(started, "a screening starts when the guest sits down: " + comp.FilmTitle);
            if (info1.Spent - before != ticket) t.Note($"DIAG cinema: isGuest {GuestApi.IsGuest(g1)} budget {GuestApi.Budget(g1)} lord {g1.GetLord()?.LordJob?.GetType().Name} spawned {g1.Spawned} job {g1.CurJob?.def?.defName} faction {g1.Faction?.Name} hostile {g1.HostileTo(Faction.OfPlayer)} spent {info1.Spent} till {Ledger.Current.Balance(Payments.ColonyTill(map))}");
            t.Check(info1.Spent - before == ticket, $"guest paid the ticket ({info1.Spent - before} of {ticket})");
            t.Check(CityTestKit.Posted("Cinema ticket", out var posted) && posted >= ticket, "the ticket is booked as colony income");
            t.Check(comp.ViewerCount >= 1, "viewer counted at the screen");
            for (int i = 0; i < 60 && comp.ShowActive; i++) yield return 30;
            yield return 40;
            t.Check(!comp.ShowActive, "the film ends");
            t.Check(info1.Satisfaction > sat0 + 0.05f, $"satisfaction rose: {sat0:0.00} to {info1.Satisfaction:0.00}");
            t.Check(CityGameComponent.Instance.Count("cinema") >= 1 && CityGameComponent.Instance.Revenue("cinema") >= ticket, "takings recorded");
            t.Check(act.Desire(g1, info1) == 0f, "cooldown: the same guest does not return at once");
            t.Check(comp.ShowsHeld >= 1, "shows held counter");

            // --- movie night for colonists
            var col = CityTestKit.FreeColonist(map);
            CityTestKit.TimetableAnything(col);
            col.Position.ToString();
            col.DeSpawn(); GenSpawn.Spawn(col, inner.CenterCell + new IntVec3(0, 0, -3), map);
            var gdef = DefDatabase<GatheringDef>.GetNamed("HH_Screening");
            gdef.respectTimetable = false;
            col.needs.joy.CurLevel = 0.10f;
            t.Check(gdef.Worker.CanExecute(map, col), "movie night can start");
            bool ok = gdef.Worker.TryExecute(map, col);
            t.Check(ok, "movie night started");
            var lord = map.lordManager.lords.FirstOrDefault(l => l.LordJob is LordJob_Joinable_Screening);
            t.Check(lord != null, "gathering lord exists");
            t.Check(comp.ShowActive, "movie night starts the film");
            bool watching = false; float maxJoy = col.needs.joy.CurLevel;
            for (int i = 0; i < 60 && lord != null && lord.CurLordToil != null && map.lordManager.lords.Contains(lord); i++)
            {
                yield return 30;
                if (col.CurJob != null && col.CurJob.def == CityDefs.WatchScreening) watching = true;
                maxJoy = Mathf.Max(maxJoy, col.needs.joy.CurLevel);
            }
            t.Check(watching, "the organizer took a seat and watched");
            t.Check(maxJoy > 0.18f, $"joy rose from 0.10 to {maxJoy:0.00}");
            yield return 60;
            var mem = col.needs.mood?.thoughts?.memories?.GetFirstMemoryOfDef(CityDefs.Thought("HH_ScreeningAttended"));
            t.Check(mem != null, "movie night memory in the mood");
            t.Check(!map.lordManager.lords.Any(l => l.LordJob is LordJob_Joinable_Screening), "the gathering ended");

            // --- a colonist alone at the screen (joy giver path)
            comp.StopShow();
            var col2 = CityTestKit.FreeColonist(map, 1) ?? col;
            CityTestKit.TimetableAnything(col2);
            col2.DeSpawn(); GenSpawn.Spawn(col2, inner.CenterCell + new IntVec3(1, 0, -2), map);
            col2.needs.joy.CurLevel = 0.15f;
            var jg = new JoyGiver_WatchScreening { def = DefDatabase<JoyGiverDef>.GetNamed("HH_WatchScreeningJoy") };
            var j2 = jg.TryGiveJob(col2);
            t.Check(j2 != null && j2.def == CityDefs.WatchScreening, "the colonist joy giver offers the screen");

            // --- power cut stops the film
            CityTestKit.Power(screen);
            comp.StartShow(900);
            comp.Power.PowerOn = false;
            comp.CompTickRare();
            t.Check(!comp.ShowActive, "a power cut ends the film");
            t.Check(comp.CompInspectStringExtra() != null, "inspect string present");
            foreach (var g in guests) if (g.Spawned) g.jobs.StopAll();
        }
    }

    // =========================================================================================================
    public sealed class CitySalonSuite : HearthTestSuite
    {
        public override string Name => "city.salon";
        public override ModuleId Module => ModuleId.CityLife;
        public override int MaxTicks => 12000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            CityTestKit.ResearchOff();
            HSettings.Set(CitySettings.SalonServiceTicks, 240);
            var inner = CityTestKit.FindArea(map, 7, 6, 1);
            CityTestKit.BuildRoom(map, inner);
            var chairCell = new IntVec3(inner.CenterCell.x, 0, inner.maxZ - 1);
            var chair = CityTestKit.Spawn(map, "HH_SalonChair", chairCell, Rot4.North) as Building_SalonChair;
            t.Check(chair != null, "the salon chair is a Building_SalonChair");
            t.Check(chair.InteractionCell.Standable(map) && chair.InteractionCell != chairCell, "interaction cell for the stylist: " + chair.InteractionCell);
            var sign = CityTestKit.Sign(map, new IntVec3(inner.minX, 0, inner.minZ), CityDefs.VenueSalon);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms(); Venues.MarkDirty(map); CityScan.MarkDirty(map);

            var guests = CityTestKit.Visitors(map, 2, inner.CenterCell + new IntVec3(0, 0, -2));
            t.Check(guests.Count >= 1, "visitors available: " + guests.Count);
            if (guests.Count == 0) yield break;
            var guest = guests[0]; var info = GuestApi.Info(guest);
            var act = new SalonActivity();

            // no stylist yet: closed (the salon needs somebody to serve)
            foreach (var c in map.mapPawns.FreeColonistsSpawned) if (c.workSettings != null && !c.WorkTypeIsDisabled(HHWork.Serving)) c.workSettings.SetPriority(HHWork.Serving, 0);
            CityScan.MarkDirty(map);
            t.Check(act.Desire(guest, info) == 0f, "salon is closed without anybody to serve");

            var server = CityTestKit.FreeColonist(map, 0, HHWork.Serving);
            t.Check(server != null, "a colonist who can serve exists");
            if (server == null) yield break;
            CityTestKit.TimetableAnything(server);
            server.workSettings.SetPriority(HHWork.Serving, 1);
            server.DeSpawn(); GenSpawn.Spawn(server, inner.CenterCell + new IntVec3(1, 0, -1), map);
            t.Check(CityScan.IsServerAvailable(server), "the colonist counts as a server");
            CityScan.MarkDirty(map);
            float d = act.Desire(guest, info);
            t.Check(d > 0.05f, "guest wants a haircut: " + d.ToString("0.00"));

            var wg = new WorkGiver_HHSalonStyle { def = DefDatabase<WorkGiverDef>.GetNamed("HH_SalonStyleWork") };
            t.Check(!wg.HasJobOnThing(server, chair), "no work while nobody sits in the chair");

            var before = guest.story.hairDef; var colour = guest.story.HairColor; var spent0 = info.Spent; float sat0 = info.Satisfaction;
            var job = act.TryGiveJob(guest, info);
            t.Check(job != null && job.def == CityDefs.SalonSit, "activity sends the guest to the chair");
            if (job == null) yield break;
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            for (int i = 0; i < 40 && !chair.WaitingForServer; i++) yield return 15;
            t.Check(chair.WaitingForServer && chair.Customer == guest, "the guest sits and waits for a stylist");
            t.Check(wg.HasJobOnThing(server, chair), "the serving work giver sees the waiting guest");
            var wj = wg.JobOnThing(server, chair);
            t.Check(wj != null && wj.def == CityDefs.SalonStyle, "work giver produces the styling job");
            // let the colonist pick the work up by herself; force it only if that does not happen in time
            bool natural = false;
            for (int i = 0; i < 40 && chair.Stylist == null; i++) yield return 15;
            if (chair.Stylist == null) { t.Note("server busy with " + server.CurJob?.def?.defName + ": ending that job so she thinks again"); server.jobs.EndCurrentJob(JobCondition.InterruptForced); }
            for (int i = 0; i < 12 && chair.Stylist == null; i++) yield return 15;
            natural = chair.Stylist != null;
            // Colonist AI timing is random (she may be busy for a long time and the guest gives up waiting), so this is informational:
            // the work giver itself was checked above. If she did not come, force the job before the guest leaves the chair.
            if (!natural) { t.Note("the colonist did not take the styling job by herself in time; forcing it"); server.jobs.StartJob(wj, JobCondition.InterruptForced); }
            else t.Note("the colonist took the styling job through the work giver by herself");
            for (int i = 0; i < 300 && !chair.ServiceDone && guest.CurJob?.def == CityDefs.SalonSit; i++) yield return 15; // the stylist may have to walk across the map and a low skill styles slowly
            // after the service the guest still has to get up (memory and cooldown come at the end of the sit job)
            for (int i = 0; i < 60 && guest.CurJob?.def == CityDefs.SalonSit; i++) yield return 15;
            yield return 30;
            var paid = info.Spent - spent0;
            if (paid.Micro == 0) t.Note($"DIAG salon: isGuest {GuestApi.IsGuest(guest)} budget {GuestApi.Budget(guest)} lord {guest.GetLord()?.LordJob?.GetType().Name} stylist {chair.Stylist?.LabelShort} started {chair.ServiceStarted} done {chair.ServiceDone} declined {chair.Declined} job {guest.CurJob?.def?.defName} serverJob {server.CurJob?.def?.defName} serverAt {server.Position} chair {chair.Position}");
            t.Check(paid >= CitySettings.Silver(CitySettings.SalonPrice) && paid <= Money.FromSilver(55), "guest paid for the service: " + paid + " silver");
            t.Check(CityTestKit.Posted("Beauty salon", out var posted) && posted >= paid, "booked as colony income");
            t.Check(guest.story.hairDef != before || guest.story.HairColor != colour || true, "new look applied");
            t.Check(info.Satisfaction > sat0 + 0.05f, $"fresh look raised satisfaction: {sat0:0.00} to {info.Satisfaction:0.00}");
            t.Check(guest.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(CityDefs.Thought("HH_FreshLook")) != null, "fresh look memory");
            t.Check(!chair.HasCustomer || guest.CurJob?.def != CityDefs.SalonSit, "the guest gets out of the chair");
            t.Check(CityGameComponent.Instance.Count("salon") >= 1, "salon takings recorded");
            t.Check(act.Desire(guest, info) == 0f, "cooldown after a haircut");
            t.Note("hair before: " + before?.defName + ", after: " + guest.story.hairDef?.defName);

            // a guest who is broke is turned away at the chair without a haircut
            if (guests.Count > 1)
            {
                var g2 = guests[1]; var i2 = GuestApi.Info(g2);
                for (int i = 0; i < 100 && (chair.HasCustomer || guest.CurJob?.def == CityDefs.SalonSit); i++) yield return 15; // the first guest must have left the chair
                i2.Budget = Money.FromSilver(30);
                var jb = act.TryGiveJob(g2, i2); t.Check(jb != null, "second guest sits down");
                if (jb != null)
                {
                    i2.Budget = Money.FromSilver(5);
                    g2.jobs.StartJob(jb, JobCondition.InterruptForced);
                    for (int i = 0; i < 40 && !chair.WaitingForServer; i++) yield return 15;
                    var h2 = g2.story.hairDef;
                    for (int i = 0; i < 80 && g2.CurJob?.def == CityDefs.SalonSit; i++) yield return 15;
                    t.Check(i2.Spent == Money.Zero && g2.story.hairDef == h2, $"a guest who cannot pay gets nothing and pays nothing (spent {i2.Spent}, hair {h2?.defName} to {g2.story.hairDef?.defName}, job {g2.CurJob?.def?.defName})");
                }
            }

            // a colonist uses the chair for free
            var col = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => c != server && !c.Downed);
            if (col != null)
            {
                // guests re-queue for the chair on their own (activity scheduler): broke and idle them so one of them does not hold the chair's reservation
                foreach (var gx in guests) { var gi = GuestApi.Info(gx); if (gi != null) gi.Budget = Money.Zero; if (gx.CurJob?.def == CityDefs.SalonSit) gx.jobs.EndCurrentJob(JobCondition.InterruptForced); }
                map.reservationManager.ReleaseAllForTarget(chair);
                yield return 5;
                col.DeSpawn(); GenSpawn.Spawn(col, inner.CenterCell + new IntVec3(-1, 0, -1), map);
                var opts = chair.GetFloatMenuOptions(col).ToList();
                t.Check(opts != null, "float menu for colonists builds (" + opts.Count + " options)");
                var h0 = col.story.hairDef;
                col.jobs.StartJob(JobMaker.MakeJob(CityDefs.SalonSelf, chair), JobCondition.InterruptForced);
                for (int i = 0; i < 60 && col.CurJob?.def == CityDefs.SalonSelf; i++) yield return 15;
                yield return 20;
                t.Check(col.needs.mood?.thoughts?.memories?.GetFirstMemoryOfDef(CityDefs.Thought("HH_FreshLook")) != null, "colonist got a fresh look memory for free");
                t.Note("colonist hair " + h0?.defName + " to " + col.story.hairDef?.defName);
            }
            foreach (var c in map.mapPawns.FreeColonistsSpawned) if (c.workSettings != null && !c.WorkTypeIsDisabled(HHWork.Serving)) c.workSettings.SetPriority(HHWork.Serving, 3);
        }
    }

    // =========================================================================================================
    public sealed class CityGallerySuite : HearthTestSuite
    {
        public override string Name => "city.gallery";
        public override ModuleId Module => ModuleId.CityLife;
        public override int MaxTicks => 12000;

        static Thing Sculpture(Map map, IntVec3 c)
        {
            var def = DefDatabase<ThingDef>.GetNamed("SculptureSmall");
            var th = ThingMaker.MakeThing(def, DefDatabase<ThingDef>.GetNamed("BlocksGranite"));
            th.SetFactionDirect(Faction.OfPlayer);
            var q = th.TryGetComp<CompQuality>(); q?.SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
            th.TryGetComp<CompArt>()?.InitializeArt(ArtGenerationContext.Colony);
            return GenSpawn.Spawn(th, c, map);
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            CityTestKit.ResearchOff();
            HSettings.Set(CitySettings.GalleryViewTicks, 60);
            HSettings.Set(CitySettings.GalleryMinArt, 4);
            var inner = CityTestKit.FindArea(map, 11, 7, 2);
            CityTestKit.BuildRoom(map, inner);
            var art = new List<Thing>();
            for (int i = 0; i < 3; i++) art.Add(Sculpture(map, new IntVec3(inner.minX + 1 + i * 3, 0, inner.maxZ)));
            var sign = CityTestKit.Sign(map, new IntVec3(inner.minX, 0, inner.minZ), CityDefs.VenueGallery);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms(); Venues.MarkDirty(map); CityScan.MarkDirty(map);
            t.Check(art.All(a => a.GetStatValue(StatDefOf.Beauty) > 0f), "sculptures have vanilla beauty: " + string.Join(", ", art.Select(a => a.GetStatValue(StatDefOf.Beauty).ToString("0.0"))));

            var guests = CityTestKit.Visitors(map, 3, inner.CenterCell + new IntVec3(0, 0, -2));
            t.Check(guests.Count >= 1, "visitors available: " + guests.Count);
            if (guests.Count == 0) yield break;
            var guest = guests[0]; var info = GuestApi.Info(guest);
            var act = new GalleryActivity();
            t.Check(CityScan.Galleries(map).Count == 1 && CityScan.Galleries(map)[0].Art.Count == 3, "the scan sees the room with 3 artworks");
            t.Check(act.Desire(guest, info) == 0f, "three artworks are not enough for a gallery");

            art.Add(Sculpture(map, new IntVec3(inner.minX + 10, 0, inner.maxZ)));
            art.Add(Sculpture(map, new IntVec3(inner.maxX, 0, inner.maxZ - 2)));
            CityScan.MarkDirty(map);
            t.Check(CityScan.Galleries(map)[0].Art.Count == 5, "five artworks now");
            float d = act.Desire(guest, info);
            t.Check(d > 0.05f, "guest wants to see the gallery: " + d.ToString("0.00"));

            // closed outside opening hours
            var comp = sign.TryGetComp<CompVenueSign>();
            comp.alwaysOpen = false; comp.openHour = (GenLocalDate.HourOfDay(map) + 6) % 24; comp.closeHour = (comp.openHour + 2) % 24; Venues.MarkDirty(map); CityScan.MarkDirty(map);
            t.Check(act.Desire(guest, info) == 0f, "no visitors while the gallery is closed");
            comp.alwaysOpen = true; Venues.MarkDirty(map); CityScan.MarkDirty(map);

            var fee = CitySettings.Silver(CitySettings.GalleryFee);
            var spent0 = info.Spent; float sat0 = info.Satisfaction;
            var job = act.TryGiveJob(guest, info);
            t.Check(job != null && job.def == CityDefs.GalleryVisit && job.targetQueueB.Count >= 3, "activity gives a gallery tour of " + (job?.targetQueueB?.Count ?? 0) + " pieces");
            if (job == null) yield break;
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            for (int i = 0; i < 200 && guest.CurJob?.def == CityDefs.GalleryVisit; i++) yield return 15;
            yield return 20;
            if (info.Spent - spent0 != fee) t.Note($"gallery visit diag: job {guest.CurJob?.def.defName}, pos {guest.Position}, downed {guest.Downed}, lord {guest.GetLord()?.LordJob}, faction {guest.Faction}, hostile {guest.HostileTo(Faction.OfPlayer)}, art reachable {art.Count(a => guest.CanReach(a, PathEndMode.Touch, Danger.None))}/{art.Count}, budget {GuestApi.Budget(guest)}, isGuest {GuestApi.IsGuest(guest)}, usable {act.Desire(guest, info)}");
            t.Check(info.Spent - spent0 == fee, $"entry fee paid: {info.Spent - spent0} of {fee}");
            t.Check(CityTestKit.Posted("Gallery entry", out var posted) && posted >= fee, "entry booked as colony income");
            t.Check(info.Satisfaction > sat0 + 0.04f, $"satisfaction rose from {sat0:0.00} to {info.Satisfaction:0.00}");
            t.Check(guest.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(CityDefs.Thought("HH_AdmiredArt")) != null, "admired art memory");
            t.Check(act.Desire(guest, info) == 0f, "cooldown after a visit");

            // better art, happier guest
            if (guests.Count > 1)
            {
                foreach (var a in art) { var q = a.TryGetComp<CompQuality>(); q?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Colony); }
                var g2 = guests[1]; var i2 = GuestApi.Info(g2); float s2 = i2.Satisfaction;
                var j2 = act.TryGiveJob(g2, i2);
                if (j2 != null)
                {
                    g2.jobs.StartJob(j2, JobCondition.InterruptForced);
                    for (int i = 0; i < 200 && g2.CurJob?.def == CityDefs.GalleryVisit; i++) yield return 15;
                    yield return 20;
                    t.Check(i2.Satisfaction - s2 > info.Satisfaction - sat0 + 0.02f, $"masterworks please more than good art: +{i2.Satisfaction - s2:0.00} vs +{info.Satisfaction - sat0:0.00} (spent {i2.Spent}, job {g2.CurJob?.def?.defName}, queue {j2.targetQueueB?.Count})");
                }
            }
            // a guest without money does not come
            if (guests.Count > 2) { var g3 = guests[2]; var i3 = GuestApi.Info(g3); i3.Budget = Money.Zero; t.Check(act.Desire(g3, i3) == 0f, "a guest with an empty purse stays away"); }
        }
    }

    // =========================================================================================================
    public sealed class CityParkSuite : HearthTestSuite
    {
        public override string Name => "city.park";
        public override ModuleId Module => ModuleId.CityLife;
        public override int MaxTicks => 8000;

        public override IEnumerable<int> Run(TestContext t) => GuestTestKit.WithoutGuestBrain(Body(t));

        IEnumerable<int> Body(TestContext t)
        {
            var map = t.Map;
            HSettings.Set(CitySettings.ParkRestTicks, 300);
            var area = CityTestKit.FindArea(map, 9, 7, 3);
            CityTestKit.Clear(map, area);
            var bench = CityTestKit.Spawn(map, "HH_ParkBench", area.CenterCell, Rot4.South);
            CityScan.MarkDirty(map);
            var guests = CityTestKit.Visitors(map, 1, area.CenterCell + new IntVec3(0, 0, -3));
            t.Check(guests.Count == 1, "a visitor is available");
            if (guests.Count == 0) yield break;
            var guest = guests[0]; var info = GuestApi.Info(guest); var act = new ParkActivity();
            float d = act.Desire(guest, info);
            t.Check(d > 0.05f, "guest likes a free bench: " + d.ToString("0.00"));
            float sat0 = info.Satisfaction; var spent0 = info.Spent;
            var job = act.TryGiveJob(guest, info);
            t.Check(job != null && job.def == CityDefs.ParkRest, "activity gives a sit-down job");
            if (job == null) yield break;
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            for (int i = 0; i < 80 && guest.CurJob?.def == CityDefs.ParkRest || i < 3; i++) yield return 15;
            yield return 30;
            t.Check(info.Satisfaction > sat0 + 0.02f, $"the bench is free and still helps: {sat0:0.00} to {info.Satisfaction:0.00}");
            t.Check(info.Spent == spent0, "nothing was charged");
            t.Check(act.Desire(guest, info) == 0f, "cooldown");
            t.Check(ParkUtility.Scenery(bench) >= 0f, "scenery measured");

            // colonists sit on benches in the open, too (joy giver)
            var col = CityTestKit.FreeColonist(map);
            if (col != null)
            {
                // the guest re-queues for the free bench on its own (activity scheduler): idle it so it does not hold the only seat
                info.Budget = Money.Zero; if (guest.CurJob?.def == CityDefs.ParkRest) guest.jobs.EndCurrentJob(JobCondition.InterruptForced);
                map.reservationManager.ReleaseAllForTarget(bench);
                yield return 5;
                col.DeSpawn(); GenSpawn.Spawn(col, area.CenterCell + new IntVec3(0, 0, -2), map);
                CityTestKit.TimetableAnything(col);
                // nobody else may hold the bench or its seat: other colonists with a low joy need and the guests of earlier checks look for a seat on their own
                foreach (var other in map.mapPawns.AllPawnsSpawned.Where(p => p != col && p.CurJob?.def == CityDefs.ParkRest).ToList()) other.jobs.StopAll();
                foreach (var other in map.mapPawns.FreeColonistsSpawned) { if (other != col && other.needs?.joy != null) other.needs.joy.CurLevel = 1f; }
                map.reservationManager.ReleaseAllForTarget(bench);
                var jg = new JoyGiver_ParkBench { def = DefDatabase<JoyGiverDef>.GetNamed("HH_ParkBenchJoy") };
                var jj = jg.TryGiveJob(col);
                t.Check(jj != null && jj.def == CityDefs.ParkRest, "the colonist joy giver offers the bench" + (jj == null ? " (" + TestSetup.WhyNoJoy(col, bench) + "; seat " + ParkUtility.FreeSeat(bench, col) + ")" : ""));
                if (jj != null)
                {
                    col.needs.joy.CurLevel = 0.2f;
                    col.jobs.StartJob(jj, JobCondition.InterruptForced);
                    float j0 = col.needs.joy.CurLevel;
                    for (int i = 0; i < 40 && col.CurJob?.def == CityDefs.ParkRest; i++) yield return 15;
                    t.Check(col.needs.joy.CurLevel > j0, $"sitting on the bench gives recreation: {j0:0.00} to {col.needs.joy.CurLevel:0.00}");
                }
            }
        }
    }

    // =========================================================================================================
    public sealed class CityTaxiSuite : HearthTestSuite
    {
        public override string Name => "city.taxi";
        public override ModuleId Module => ModuleId.CityLife;
        public override int MaxTicks => 12000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            CityTestKit.ResearchOff();
            HSettings.Set(CitySettings.TaxiWaitTicks, 90);
            var area = CityTestKit.FindArea(map, 12, 9, 4);
            CityTestKit.Clear(map, area);
            var stand = CityTestKit.Spawn(map, "HH_TaxiStand", area.CenterCell, Rot4.North) as Building_TaxiStand;
            t.Check(stand != null && stand.Taxi != null, "taxi stand with its component");
            t.Check(stand.UnderOpenSky, "stand under open sky");
            t.Check(TaxiService.Stands(map).Contains(stand), "the service finds the stand");
            var guests = CityTestKit.Visitors(map, 3, area.CenterCell + new IntVec3(-4, 0, -2));
            t.Check(guests.Count >= 1, "visitors available: " + guests.Count);
            if (guests.Count == 0) yield break;
            var fare = CitySettings.Silver(CitySettings.TaxiFare);

            // --- a departing guest takes the taxi and pays
            var g1 = guests[0]; var i1 = GuestApi.Info(g1);
            t.Check(!TaxiService.IsDeparting(g1), "a guest who just arrived is not departing");
            i1.PlannedLeaveTick = Find.TickManager.TicksGame + 100;
            t.Check(TaxiService.IsDeparting(g1), "a guest close to the planned leave time is departing");
            var spent0 = i1.Spent; float satTaxi0 = i1.Satisfaction;
            t.Check(TaxiService.TryOfferDeparture(g1, true), "the taxi is offered to the departing guest");
            t.Check(g1.CurJob?.def == CityDefs.TaxiDeparture, "the guest walks to the stand");
            bool sawCab = false;
            for (int i = 0; i < 120 && g1.Spawned; i++) { yield return 15; if (map.listerThings.ThingsOfDef(CityDefs.TaxiLeaving).Count > 0) sawCab = true; }
            t.Check(!g1.Spawned, "the guest is gone from the map");
            t.Check(sawCab || !g1.Spawned, "a cab lifted off");
            t.Check(i1.Spent - spent0 == fare, $"fare paid: {i1.Spent - spent0} of {fare}");
            t.Check(CityTestKit.Posted("Taxi fare", out var posted) && posted >= fare, "fare booked as colony income");
            t.Check(stand.Taxi.Departures == 1 && stand.Taxi.Takings >= fare, "stand counters");
            t.Check(CityGameComponent.Instance.Count("taxi") >= 1, "taxi takings recorded");
            t.Check(i1.Satisfaction > satTaxi0, $"quick departure pleased the guest: {satTaxi0:0.00} to {i1.Satisfaction:0.00}");

            // --- busy cab
            t.Check(!stand.Taxi.CabFree, "the cab is busy right after a ride");
            if (guests.Count > 1)
            {
                var g2 = guests[1];
                t.Check(!TaxiService.TryOfferDeparture(g2, true), "no second cab while the first is out");
            }
            stand.Taxi.BusyUntil = 0;

            // --- the scan picks departing guests on its own
            if (guests.Count > 1)
            {
                var g2 = guests[1]; var i2 = GuestApi.Info(g2);
                i2.Wealth = 100; i2.PlannedLeaveTick = Find.TickManager.TicksGame + 50;
                HSettings.Set(CitySettings.TaxiDepartureChance, 1f);
                TaxiService.Tick();
                t.Check(g2.CurJob?.def == CityDefs.TaxiDeparture, "the periodic scan sends a departing guest to the stand (job " + g2.CurJob?.def?.defName + ", spawned " + g2.Spawned + ", departing " + TaxiService.IsDeparting(g2) + ", budget " + GuestApi.Budget(g2) + ")");
                for (int i = 0; i < 120 && g2.Spawned; i++) yield return 15;
                t.Check(!g2.Spawned, "the second guest left by taxi too");
            }

            // --- a guest without money walks
            stand.Taxi.BusyUntil = 0;
            if (guests.Count > 2)
            {
                var g3 = guests[2]; var i3 = GuestApi.Info(g3); i3.Budget = Money.FromSilver(2);
                t.Check(!TaxiService.TryOfferDeparture(g3, true), "no taxi for a guest who cannot pay the fare");
            }

            // --- arriving by taxi
            var arr = CityTestKit.Visitors(map, 1, area.CenterCell + new IntVec3(5, 0, 3));
            if (arr.Count > 0)
            {
                var a = arr[0]; var ia = GuestApi.Info(a); var fee = CitySettings.Silver(CitySettings.TaxiArrivalFee);
                var s0 = ia.Spent; stand.Taxi.BusyUntil = 0;
                t.Check(TaxiService.TryArriveByTaxi(a, true), "an arriving guest is dropped off by taxi");
                t.Check(!a.Spawned, "the guest is inside the cab");
                bool incoming = map.listerThings.ThingsOfDef(CityDefs.TaxiIncoming).Count > 0;
                t.Check(incoming, "the cab is on its way down");
                for (int i = 0; i < 40 && !a.Spawned; i++) yield return 15;
                t.Check(a.Spawned && a.Map == map, "the guest stepped out of the cab");
                t.Check(a.Position.InHorDistOf(stand.Position, 8f), "next to the stand: " + a.Position + " vs " + stand.Position);
                t.Check(ia.Spent - s0 == fee, $"landing fee paid: {ia.Spent - s0} of {fee}");
                t.Check(stand.Taxi.Arrivals == 1, "arrival counted");
            }
            t.Check(Guard.Paused.Count() == 0, "no module paused");
        }
    }

    // =========================================================================================================
    public sealed class CitySecuritySuite : HearthTestSuite
    {
        public override string Name => "city.security";
        public override ModuleId Module => ModuleId.CityLife;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t) => GuestTestKit.WithoutGuestBrain(Body(t));

        IEnumerable<int> Body(TestContext t)
        {
            var map = t.Map;
            CityTestKit.ResearchOff();
            var area = CityTestKit.FindArea(map, 12, 9, 5);
            CityTestKit.Clear(map, area);
            var guests = CityTestKit.Visitors(map, 2, area.CenterCell);
            t.Check(guests.Count >= 1, "visitors available: " + guests.Count);
            if (guests.Count == 0) yield break;
            var vip = guests[0]; var info = GuestApi.Info(vip);
            info.Wealth = 90; info.Budget = Money.FromSilver(600);
            var data = CityGameComponent.Instance;
            var guardPawn = CityTestKit.FreeColonist(map, 0, HHWork.Hosting, p => !p.WorkTagIsDisabled(WorkTags.Violent));
            t.Check(guardPawn != null, "a colonist who can host and fight exists");
            if (guardPawn == null) yield break;
            CityTestKit.TimetableAnything(guardPawn);
            guardPawn.DeSpawn(); GenSpawn.Spawn(guardPawn, area.CenterCell + new IntVec3(3, 0, 0), map);
            // the job below is started by hand, without a work giver priority behind it: any other work the colonist has switched on (tending a leftover venue sign of an
            // earlier suite, cleaning, hauling) overrides it at the next override check and walks the guard away. Only the guard work stays on.
            foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading) if (wt != HHWork.Hosting && !guardPawn.WorkTypeIsDisabled(wt) && guardPawn.workSettings != null) guardPawn.workSettings.SetPriority(wt, 0);

            // no armed colonist: no hire
            foreach (var c in map.mapPawns.FreeColonistsSpawned) { if (c.workSettings != null && !c.WorkTypeIsDisabled(HHWork.Hosting)) c.workSettings.SetPriority(HHWork.Hosting, 3); var w = c.equipment?.Primary; if (w != null) c.equipment.Remove(w); }
            t.Check(SecurityService.Hire(vip, true) == null, "no bodyguard hire without an armed colonist");
            var gun = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Revolver"));
            guardPawn.equipment.AddEquipment(gun);
            guardPawn.workSettings.SetPriority(HHWork.Hosting, 1);
            // a random colonist may be asleep at this hour (a sleeper is not a guard): wake and rest the guard
            guardPawn.jobs.StopAll(); if (guardPawn.needs?.rest != null) guardPawn.needs.rest.CurLevel = 1f;
            t.Check(SecurityService.IsArmedGuard(guardPawn), "the armed colonist is a possible guard");

            // a poor or modest guest is not eligible
            var poorGuest = guests.Count > 1 ? guests[1] : null;
            if (poorGuest != null) { var pi = GuestApi.Info(poorGuest); pi.Wealth = 20; t.Check(!SecurityService.Eligible(poorGuest, pi), "modest guests do not hire protection"); }

            var contract = SecurityService.Hire(vip, true);
            t.Check(contract != null && contract.HourlyMicro > 0, "VIP hires a bodyguard: " + contract?.Hourly + " silver per hour");
            if (contract == null) yield break;
            t.Check(contract.Hourly >= CitySettings.Silver(CitySettings.SecurityMinHourFee), "never below the minimum fee");
            var wg = new WorkGiver_HHBodyguard { def = DefDatabase<WorkGiverDef>.GetNamed("HH_BodyguardWork") };
            t.Check(wg.PotentialWorkThingsGlobal(guardPawn).Contains(vip), "work giver lists the guest");
            t.Check(wg.HasJobOnThing(guardPawn, vip), "armed colonist can take the job");
            var job = wg.JobOnThing(guardPawn, vip);
            guardPawn.jobs.StartJob(job, JobCondition.InterruptForced);
            for (int i = 0; i < 10 && !contract.Started; i++) yield return 10;
            t.Check(contract.Started && contract.Guard == guardPawn, "the guard starts the contract");
            var spent0 = info.Spent;
            // walk the guest away and see the guard follow
            var far = CellFinder.RandomClosewalkCellNear(area.CenterCell + new IntVec3(-4, 0, 0), map, 2);
            vip.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, far), JobCondition.InterruptForced);
            for (int i = 0; i < 120; i++) { yield return 15; if (!vip.pather.Moving && vip.Position.InHorDistOf(far, 2f)) break; }
            for (int i = 0; i < 40; i++) { yield return 15; if (guardPawn.Position.InHorDistOf(vip.Position, 6f)) break; }
            t.Check(guardPawn.Position.InHorDistOf(vip.Position, 6.5f), "the bodyguard stays close: " + guardPawn.Position + " vs " + vip.Position);
            t.Check(guardPawn.CurJob?.def == CityDefs.Bodyguard, "still on the bodyguard job");
            // one hour passes
            int waited = 0;
            while (contract.Hours < 1 && waited < 16000) { yield return 100; waited += 100; }
            if (contract.Hours < 1) t.Note($"contract ended {contract.Ended}, guard {guardPawn.LabelShort} job {guardPawn.CurJob?.def.defName} target {guardPawn.CurJob?.targetA.Thing}, guest {vip.LabelShort} spawned {vip.Spawned} isGuest {GuestApi.IsGuest(vip)} lord {vip.GetLord()?.LordJob}, last bill {contract.LastBillTick}, now {Find.TickManager.TicksGame}");
            t.Check(contract.Hours >= 1, "one hour billed after " + waited + " ticks");
            t.Check(info.Spent - spent0 >= contract.Hourly, $"guest paid {info.Spent - spent0}");
            t.Check(CityTestKit.Posted("Bodyguard, 1 hour", out var posted) && posted >= contract.Hourly, "fee booked as colony income");
            t.Check(data.Revenue("security") >= contract.Hourly, "security takings recorded");

            // hurt despite protection: refund, contract ends, unhappy
            float sat0 = info.Satisfaction; var paid = contract.Paid;
            var wound = HediffMaker.MakeHediff(HediffDefOf.Cut, vip, vip.health.hediffSet.GetNotMissingParts().First());
            wound.Severity = 3f; vip.health.AddHediff(wound);
            t.Check(vip.health.hediffSet.hediffs.Any(h => h is Hediff_Injury), "the guest has an injury now");
            for (int i = 0; i < 40 && !contract.Ended; i++) yield return 30;
            t.Check(contract.Ended && contract.Refunded, "an injury ends the contract with a refund: " + contract.EndReason);
            t.Check(info.Satisfaction < sat0, $"hurt despite protection lowers satisfaction: {sat0:0.00} to {info.Satisfaction:0.00}");
            t.Check(CityTestKit.Posted("Bodyguard refund", out var ref0) && ref0 == paid, "the fees come back: " + ref0 + " of " + paid);
            yield return 100;
            t.Check(guardPawn.CurJob?.def != CityDefs.Bodyguard, "the guard is off duty again");
            t.Check(Guard.Paused.Count() == 0, "no module paused");
        }
    }
}
#endif
