// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Wellness.Tests
{
    static class WT
    {
        /// <summary>A healthy adult colonist who can take part in joy (see TestSetup.Colonist); a sick or hurt one is sent to bed rest instead of a tub, table or sauna.</summary>
        public static Pawn Colonist(Map map, int skip = 0)
            => Hearth.Testing.TestSetup.Colonist(map, skip) ?? map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Dead).Skip(skip).FirstOrDefault();

        /// <summary>Clears a square patch (things, roofs) and lays soil so it is safe to build on.</summary>
        public static void Clear(Map map, IntVec3 center, int r)
        {
            foreach (var c in CellRect.CenteredOn(center, r).Cells.ToList())
            {
                if (!c.InBounds(map)) continue;
                foreach (var th in c.GetThingList(map).ToList()) if (!(th is Pawn) && th.def.destroyable) th.Destroy();
                map.roofGrid.SetRoof(c, null);
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                map.fogGrid.Unfog(c);
            }
        }

        public static Thing Spawn(Map map, ThingDef def, IntVec3 c, Rot4? rot = null, bool player = true)
        {
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            if (player && def.CanHaveFaction) t.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(t, c, map, rot ?? Rot4.North);
            return t;
        }

        public static void Power(Thing t) { var p = t.TryGetComp<CompPowerTrader>(); if (p != null) p.PowerOn = true; }

        /// <summary>Walled, roofed room of inner size n x n around center; returns the inner rect. Door on the south wall.</summary>
        public static CellRect Room(Map map, IntVec3 center, int n)
        {
            var inner = CellRect.CenteredOn(center, n / 2);
            var outer = inner.ExpandedBy(1);
            foreach (var c in outer.Cells)
            {
                if (inner.Contains(c)) continue;
                if (c.z == outer.minZ && c.x == center.x) Spawn(map, ThingDefOf.Door, c);
                else Spawn(map, ThingDefOf.Wall, c);
            }
            foreach (var c in outer.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            return inner;
        }

        public static void Teleport(Pawn p, IntVec3 c) { p.Position = c; p.Notify_Teleported(false); }

        public static IEnumerable<int> WaitUntil(Func<bool> cond, int maxTicks, int step = 30)
        {
            for (int i = 0; i < maxTicks && !cond(); i += step) yield return step;
        }

        public static void ClearJob(Pawn p) { p.jobs.StopAll(); p.jobs.ClearQueuedJobs(); }
    }

    public sealed class DefsSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "wellness.defs";
        public override ModuleId Module => ModuleId.Wellness;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            foreach (var n in new[] { "HH_HotTub", "HH_Sauna", "HH_MassageTable" })
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(n);
                t.Check(d != null, n + " exists");
                if (d == null) continue;
                t.Check(!d.description.NullOrEmpty() && !d.label.NullOrEmpty(), n + " has label and description");
                var c = d.GetCompProperties<CompProperties_WellnessFacility>();
                t.Check(c != null, n + " has the facility comp");
                t.Check(d.building?.joyKind == WellnessDefOf.HH_Relaxation, n + " joyKind is HH_Relaxation");
                t.Check(d.designationCategory != null && d.designationCategory.defName == "HH_Hospitality", n + " in the HH_Hospitality menu");
                t.Check(d.graphicData != null && d.graphicData.texPath.StartsWith("Things/HH/Wellness/"), n + " texture path under Things/HH/Wellness");
            }
            t.Check(WellnessDefOf.HH_HotTub.size == new IntVec2(2, 2), "hot tub is 2x2");
            t.Check(WellnessDefOf.HH_Sauna.size == new IntVec2(1, 1), "stove is 1x1");
            t.Check(WellnessDefOf.HH_MassageTable.size == new IntVec2(2, 1), "table is 2x1");
            t.Check(WellnessDefOf.HH_Sauna.GetCompProperties<CompProperties_Refuelable>() != null, "stove burns fuel");
            t.Check(WellnessDefOf.HH_Sauna.GetCompProperties<CompProperties_HeatPusher>()?.heatPushMaxTemperature > 45f, "stove can push the room above 45 C");
            t.Check(WellnessDefOf.HH_HotTub.GetCompProperties<CompProperties_Power>() != null, "tub needs power");
            t.Check(WellnessDefOf.HH_HotTub.GetCompProperties<CompProperties_HeatPusher>() != null, "tub gives a little heat");
            foreach (var j in new[] { WellnessDefOf.HH_UseHotTub, WellnessDefOf.HH_UseSauna, WellnessDefOf.HH_ReceiveMassage })
                t.Check(j.joyKind == WellnessDefOf.HH_Relaxation, j.defName + " has joyKind");
            foreach (var g in DefDatabase<JoyGiverDef>.AllDefs.Where(g => g.defName.StartsWith("HH_Joy_")))
                t.Check(!g.ConfigErrors().Any(), g.defName + " has no config errors");
            t.Check(DefDatabase<WorkGiverDef>.GetNamedSilentFail("HH_GiveMassage")?.workType == WellnessDefOf.HH_Serving, "massage work giver is under Serving");
            t.Check(WellnessDefOf.HH_Relaxed.stages.Count == 3, "Relaxed has one stage per kind");
            t.Check(DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Bathhouse") != null && DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Wellness") != null, "research projects exist");
            t.Check(GuestActivities.All.Any(a => a.Id == "wellness.spa"), "guest activity wellness.spa registered");
            int s = 0;
            foreach (var st in HSettings.All.Where(x => x.Module == ModuleId.Wellness))
            {
                s++;
                t.Check(("HH.Setting." + st.Key).CanTranslate() && ("HH.Setting." + st.Key + ".Desc").CanTranslate(), "setting text in Keyed: " + st.Key);
            }
            t.Check(s >= 15, "wellness settings registered: " + s);
            foreach (var k in new[] { "HH.Wellness.Inspect.SaunaReady", "HH.Wellness.Inspect.Users", "HH.Wellness.Gizmo.Colonists", "HH.Wellness.Gizmo.Guests" })
                t.Check(k.CanTranslate(), "keyed " + k);
            t.Check(WellnessSettings.SessionTicks(WellnessKind.HotTub) > 1000, "session length derived from hours");
            t.Check(HSettings.Get(WellnessSettings.SaunaMinTemp) == 45f, "sauna minimum default 45");
            yield break;
        }
    }

    public sealed class ColonistSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "wellness.colonist";
        public override ModuleId Module => ModuleId.Wellness;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            var map = t.Map; var pawn = WT.Colonist(map);
            t.Check(pawn != null, "have a colonist");
            var area = map.Center + new IntVec3(12, 0, 10);
            WT.Clear(map, area, 6);
            // vanilla's joy givers refuse outdoor play in rain/cold/heat, and the weather of the test game is random: put a roof over the tub
            foreach (var c in CellRect.CenteredOn(area, 6).Cells) if (c.InBounds(map)) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            var tub = WT.Spawn(map, WellnessDefOf.HH_HotTub, area);
            var comp = tub.TryGetComp<CompWellnessFacility>();
            t.Check(comp != null, "tub spawned with comp");
            string why;
            t.Check(!comp.UsableNow(out why) || tub.TryGetComp<CompPowerTrader>().PowerOn, "unpowered tub is not usable (" + why + ")");
            WT.Power(tub);
            t.Check(comp.UsableNow(out why), "powered tub is usable " + why);
            t.Check(comp.AllowsUser(pawn), "colonists allowed by default");

            WT.ClearJob(pawn);
            WT.Teleport(pawn, area + new IntVec3(0, 0, -3));
            pawn.needs.joy.CurLevel = 0.1f;
            float restBefore = pawn.needs.rest.CurLevel = 0.4f;
            var giver = DefDatabase<JoyGiverDef>.GetNamed("HH_Joy_HotTub").Worker;
            var job = giver.TryGiveJob(pawn);
            if (job == null) t.Note($"no offer: {Hearth.Testing.TestSetup.WhyNoJoy(pawn, tub)}; medical rest {HealthAIUtility.ShouldSeekMedicalRest(pawn)}, hediffs {string.Join("/", pawn.health.hediffSet.hediffs.Select(h => h.def.defName))}; pawn {pawn.LabelShort} at {pawn.Position}, joy {pawn.needs.joy.CurLevel:0.00}, usable {comp.UsableNow(out var w0)} ({w0}), allows {comp.AllowsUser(pawn)}, reach {pawn.CanReach(tub, PathEndMode.Touch, Danger.None)}, forbidden {tub.IsForbidden(pawn)}, weather {map.weatherManager.curWeather.defName}, temp {map.mapTemperature.OutdoorTemp:0}, inBed {pawn.InBed()}, mental {pawn.InMentalState}");
            t.Check(job != null && job.def == WellnessDefOf.HH_UseHotTub, "joy giver offers the hot tub");
            if (job == null) yield break;
            comp.colonistsMayUse = false;
            t.Check(giver.TryGiveJob(pawn) == null, "tub switched off for colonists is not offered");
            comp.colonistsMayUse = true;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            yield return 120;
            t.Check(pawn.CurJob?.def == WellnessDefOf.HH_UseHotTub, "pawn is using the tub");
            t.Check(tub.OccupiedRect().Contains(pawn.Position) || pawn.pather.Moving, "pawn goes into the tub");
            yield return 600;
            t.Check(pawn.needs.joy.CurLevel > 0.1f, "joy rises while soaking: " + pawn.needs.joy.CurLevel.ToString("0.00"));
            foreach (var x in WT.WaitUntil(() => pawn.CurJob?.def != WellnessDefOf.HH_UseHotTub, 5000)) yield return x;
            t.Check(pawn.CurJob?.def != WellnessDefOf.HH_UseHotTub, "session ended");
            if (pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(WellnessDefOf.HH_Relaxed) == null)
                t.Note("session aborted: " + pawn.LabelShort + " job " + pawn.CurJob?.def.defName + " downed " + pawn.Downed + " mental " + pawn.InMentalState + " pos " + pawn.Position + " hp " + pawn.health.summaryHealth.SummaryHealthPercent.ToString("0.00") + " hediffs " + string.Join("/", pawn.health.hediffSet.hediffs.Select(h => h.def.defName)) + " temp " + pawn.AmbientTemperature.ToString("0") + " weather " + map.weatherManager.curWeather.defName);
            t.Check(pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(WellnessDefOf.HH_Relaxed) != null, "got the Relaxed memory");
            t.Check(pawn.needs.rest.CurLevel > restBefore, "rest bonus applied: " + pawn.needs.rest.CurLevel.ToString("0.00"));
            t.Check(pawn.health.hediffSet.GetFirstHediffOfDef(WellnessDefOf.HH_Soothed) != null, "soothed hediff applied");
            t.Check(WellnessState.Current.sessionsHotTub >= 1, "session counted");
            // power cut ends a session / blocks new ones
            tub.TryGetComp<CompPowerTrader>().PowerOn = false;
            t.Check(giver.TryGiveJob(pawn) == null, "no power: no offer");
            // modifying the module off hides the giver
            WT.ClearJob(pawn);
            tub.Destroy();
        }
    }

    public sealed class SaunaSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "wellness.sauna";
        public override ModuleId Module => ModuleId.Wellness;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            var map = t.Map; var pawn = WT.Colonist(map, 1) ?? WT.Colonist(map);
            var center = map.Center + new IntVec3(-14, 0, 10);
            WT.Clear(map, center, 8);
            var inner = WT.Room(map, center, 5);
            var stove = WT.Spawn(map, WellnessDefOf.HH_Sauna, center + new IntVec3(-1, 0, 1));
            var comp = stove.TryGetComp<CompWellnessFacility>();
            var fuel = stove.TryGetComp<CompRefuelable>();
            yield return 5;
            var room = SaunaUtil.RoomOf(stove);
            t.Check(room != null, "stove stands in a closed, roofed room");
            string why;
            t.Check(!comp.UsableNow(out why), "no fuel: not usable (" + why + ")");
            fuel.Refuel(25f);
            float outdoor = map.mapTemperature.OutdoorTemp;
            room.Temperature = 20f;
            t.Check(!comp.UsableNow(out why), "fuel but 20 C: not a sauna (" + why + ")");
            room.Temperature = 44.5f;
            t.Check(!SaunaUtil.IsHotEnough(stove, 0f, out _), "44.5 C is below 45");
            room.Temperature = 45f;
            t.Check(SaunaUtil.IsHotEnough(stove, 0f, out _) && comp.UsableNow(out why), "45 C counts: " + why);
            room.Temperature = 41f;
            t.Check(!comp.UsableNow(out _) && comp.UsableNow(out _, slack: true), "a running session tolerates a few degrees of cooling");
            HSettings.Set(WellnessSettings.SaunaMinTemp, 55f);
            room.Temperature = 50f;
            t.Check(!comp.UsableNow(out _), "setting raises the minimum to 55");
            HSettings.Set(WellnessSettings.SaunaMinTemp, 45f);
            t.Check(SaunaUtil.WorkingSaunas(map) == 1, "working sauna counted");
            fuel.ConsumeFuel(fuel.Fuel);
            t.Check(!comp.UsableNow(out why) && why == "no fuel", "empty stove: " + why);
            fuel.Refuel(25f);

            // heat physics: the stove really warms a closed room
            room.Temperature = Math.Min(outdoor, 20f);
            float start = room.Temperature;
            yield return 1500;
            float after = room.Temperature;
            t.Note($"room {start:0.0} C -> {after:0.0} C after 1500 ticks (outdoor {outdoor:0} C, {room.CellCount} cells)");
            t.Check(after > start + 5f, "stove warms the room");
            foreach (var x in WT.WaitUntil(() => room.Temperature >= 45f, 6000, 100)) yield return x;
            t.Check(room.Temperature >= 45f, "the room reaches the 45 C sauna minimum: " + room.Temperature.ToString("0.0"));

            // a colonist uses it, takes no heatstroke, gets the thought
            room.Temperature = 55f;
            WT.ClearJob(pawn);
            WT.Teleport(pawn, center + new IntVec3(0, 0, -1));
            pawn.needs.joy.CurLevel = 0.1f;
            var job = DefDatabase<JoyGiverDef>.GetNamed("HH_Joy_Sauna").Worker.TryGiveJob(pawn);
            t.Check(job != null && job.def == WellnessDefOf.HH_UseSauna, "joy giver offers the sauna" + (job == null ? " (" + Hearth.Testing.TestSetup.WhyNoJoy(pawn, stove) + "; usable " + comp.UsableNow(out var whyNo) + " " + whyNo + ", room " + room.Temperature.ToString("0") + " C, fuel " + fuel.Fuel.ToString("0.0") + ", spot " + SaunaUtil.FindSpot(pawn, stove) + ")" : ""));
            if (job != null)
            {
                t.Check(room.ContainsCell(job.targetB.Cell), "sauna spot is inside the room");
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                foreach (var x in WT.WaitUntil(() => pawn.CurJob?.def == WellnessDefOf.HH_UseSauna && pawn.Position == job.targetB.Cell, 1500, 20)) yield return x;
                t.Check(pawn.CurJob?.def == WellnessDefOf.HH_UseSauna, "pawn sits in the sauna");
                room.Temperature = 58f;
                yield return 900;
                t.Check(pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Heatstroke) == null, "no heatstroke inside the sauna at " + pawn.AmbientTemperature.ToString("0") + " C");
                foreach (var x in WT.WaitUntil(() => pawn.CurJob?.def != WellnessDefOf.HH_UseSauna, 5000)) yield return x;
                t.Check(pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(WellnessDefOf.HH_Relaxed) != null, "sauna Relaxed memory");
                t.Check(WellnessState.Current.sessionsSauna >= 1, "sauna session counted");
            }
            WT.ClearJob(pawn);
            stove.Destroy();
        }
    }

    public sealed class MassageSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "wellness.massage";
        public override ModuleId Module => ModuleId.Wellness;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            var map = t.Map;
            // the masseur must be able to do Serving work at all: the test colonists are random pawns and some backstories disable it
            // healthy ones: a hurt or sick colonist is sent to bed rest and drops the table job half way
            var masseur = Hearth.Testing.TestSetup.Colonist(map, 0, c => !c.WorkTypeIsDisabled(WellnessDefOf.HH_Serving))
                ?? map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => !c.Downed && !c.Dead && !c.WorkTypeIsDisabled(WellnessDefOf.HH_Serving) && c.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
            var client = Hearth.Testing.TestSetup.Colonist(map, 0, c => c != masseur) ?? map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => c != masseur && !c.Downed && !c.Dead);
            if (client == null || masseur == null || client == masseur)
                t.Note("colonists: " + string.Join("; ", map.mapPawns.AllPawns.Where(p => p.IsColonist).Select(p => p.LabelShort + " spawned " + p.Spawned + " dead " + p.Dead + " downed " + p.Downed + " job " + p.CurJob?.def.defName + " hp " + p.health.summaryHealth.SummaryHealthPercent.ToString("0.00") + " hediffs " + string.Join("/", p.health.hediffSet.hediffs.Select(h => h.def.defName)))));
            t.Check(client != null && masseur != null && client != masseur, "two colonists available");
            if (client == null || masseur == null || client == masseur) yield break;
            var area = map.Center + new IntVec3(0, 0, 16);
            WT.Clear(map, area, 5);
            var table = WT.Spawn(map, WellnessDefOf.HH_MassageTable, area);
            var comp = table.TryGetComp<CompWellnessFacility>();
            WT.ClearJob(client); WT.ClearJob(masseur);
            masseur.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            masseur.workSettings.SetPriority(WellnessDefOf.HH_Serving, 1);
            t.Check(WellnessStaff.MasseurAvailable(map, client), "a masseur is available");
            // "Nobody has Serving on" must be true for every colonist: with the Inn, shops and casino active the other colonists of the test
            // colony have Serving switched on by default (and earlier suites' work settings stay), so remember and switch them all off.
            var saved = new List<KeyValuePair<Pawn, int>>();
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                if (c.workSettings == null) continue;
                c.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                saved.Add(new KeyValuePair<Pawn, int>(c, c.workSettings.GetPriority(WellnessDefOf.HH_Serving)));
                c.workSettings.SetPriority(WellnessDefOf.HH_Serving, 0);
            }
            t.Check(!WellnessStaff.MasseurAvailable(map, client), "no masseur when nobody has Serving on");
            var giver = DefDatabase<JoyGiverDef>.GetNamed("HH_Joy_Massage").Worker;
            client.needs.joy.CurLevel = 0.1f;
            t.Check(giver.TryGiveJob(client) == null, "no masseur: table not offered");
            masseur.workSettings.SetPriority(WellnessDefOf.HH_Serving, 1);
            WT.Teleport(client, area + new IntVec3(0, 0, -3));
            WT.Teleport(masseur, area + new IntVec3(-3, 0, -3));
            var job = giver.TryGiveJob(client);
            t.Check(job != null && job.def == WellnessDefOf.HH_ReceiveMassage, "client is offered the table" + (job == null ? " (" + Hearth.Testing.TestSetup.WhyNoJoy(client, table) + "; usable " + comp.UsableNow(out var whyNot) + " " + whyNot + ", allows " + comp.AllowsUser(client)
                + ", masseur available " + WellnessStaff.MasseurAvailable(map, client) + " (" + masseur.LabelShort + " awake " + masseur.Awake() + " drafted " + masseur.Drafted + " job " + masseur.CurJob?.def.defName + "), seeks medical rest " + HealthAIUtility.ShouldSeekMedicalRest(client)
                + ", recipient " + comp.recipient?.LabelShort + ")" : ""));
            if (job == null) yield break;
            float xp0 = masseur.skills.GetSkill(SkillDefOf.Social).xpSinceLastLevel + masseur.skills.GetSkill(SkillDefOf.Social).Level * 1000f;
            client.jobs.StartJob(job, JobCondition.InterruptForced);
            foreach (var x in WT.WaitUntil(() => comp.recipientReady, 1500, 20)) yield return x;
            t.Check(comp.recipientReady && comp.recipient == client, "client lies on the table and waits" + (comp.recipientReady ? "" : " (client job " + (client.CurJob?.def.defName ?? "none") + " at " + client.Position + ", table at " + table.Position + ", rest " + client.needs?.rest?.CurLevel.ToString("0.00") + ", medical rest " + HealthAIUtility.ShouldSeekMedicalRest(client) + ", mental " + client.InMentalState + ", drafted " + client.Drafted + ")"));
            t.Check(client.jobs.posture != PawnPosture.Standing, "client lies down");
            // the Serving work giver finds the request
            var wg = (WorkGiver_GiveMassage)DefDatabase<WorkGiverDef>.GetNamed("HH_GiveMassage").Worker;
            var mj = wg.JobOnThing(masseur, table);
            t.Check(mj != null && mj.def == WellnessDefOf.HH_GiveMassage, "work giver offers the massage to the masseur");
            if (mj == null) yield break;
            masseur.jobs.StartJob(mj, JobCondition.InterruptForced);
            foreach (var x in WT.WaitUntil(() => comp.sessionActive, 1500, 20)) yield return x;
            t.Check(comp.sessionActive, "massage is running");
            foreach (var x in WT.WaitUntil(() => comp.sessionDone || client.CurJob?.def != WellnessDefOf.HH_ReceiveMassage, 6000, 50)) yield return x;
            yield return 60;
            t.Check(client.CurJob?.def != WellnessDefOf.HH_ReceiveMassage, "client done");
            t.Check(client.needs.mood.thoughts.memories.GetFirstMemoryOfDef(WellnessDefOf.HH_Relaxed) != null, "client is relaxed");
            var sk = masseur.skills.GetSkill(SkillDefOf.Social);
            t.Check(sk.xpSinceLastLevel + sk.Level * 1000f > xp0 || sk.Level >= 20 || sk.passion == Passion.None, "masseur earned Social xp");
            t.Check(WellnessState.Current.sessionsMassage >= 1, "massage counted");
            t.Check(comp.recipient == null, "table is free again");
            // nobody comes: client gives up
            client.needs.joy.CurLevel = 0.1f;
            masseur.workSettings.SetPriority(WellnessDefOf.HH_Serving, 0);
            WT.Teleport(client, area + new IntVec3(0, 0, -2));
            HSettings.Set(WellnessSettings.MassageWait, 0.1f);
            var j2 = WellnessStaff.MakeUseJob(client, table);
            t.Check(j2 == null, "no masseur: no job");
            masseur.workSettings.SetPriority(WellnessDefOf.HH_Serving, 1);
            j2 = WellnessStaff.MakeUseJob(client, table);
            client.jobs.StartJob(j2, JobCondition.InterruptForced);
            // the masseur was only enabled to get a job object: nobody comes now, or an available masseur would simply massage the client (flaky)
            masseur.workSettings.SetPriority(WellnessDefOf.HH_Serving, 0); if (masseur.CurJob?.def == WellnessDefOf.HH_GiveMassage) masseur.jobs.EndCurrentJob(JobCondition.InterruptForced);
            foreach (var x in WT.WaitUntil(() => client.CurJob?.def != WellnessDefOf.HH_ReceiveMassage, 2500, 50)) yield return x;
            t.Check(client.CurJob?.def != WellnessDefOf.HH_ReceiveMassage, "client gives up waiting");
            t.Check(comp.recipient == null, "table freed after giving up");
            HSettings.Set(WellnessSettings.MassageWait, 0.7f);
            foreach (var kv in saved) if (kv.Key.workSettings != null) kv.Key.workSettings.SetPriority(WellnessDefOf.HH_Serving, kv.Value);
            WT.ClearJob(client); WT.ClearJob(masseur);
            table.Destroy();
        }
    }

    public sealed class GuestSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "wellness.guest";
        public override ModuleId Module => ModuleId.Wellness;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            var map = t.Map;
            var center = map.Center + new IntVec3(14, 0, -12);
            WT.Clear(map, center, 9);
            var inner = WT.Room(map, center, 7);
            var tub = WT.Spawn(map, WellnessDefOf.HH_HotTub, center + new IntVec3(1, 0, 1));
            WT.Power(tub);
            var stove = WT.Spawn(map, WellnessDefOf.HH_Sauna, center + new IntVec3(-2, 0, 2));
            // the stove stays cold: a fuelled stove warms its small room by 10+ C per 1000 ticks, and a room above the safe temperature range is a danger region that
            // visitors will not path into (Danger.None), so whether the guest got a tub job depended on how long the visitor took to walk in. Billing needs no fuel.

            // venue: the real HH_Venue_Spa with a real sign if the Venues module exists, else a test-only sign + type
            var type = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Spa");
            if (type == null)
            {
                type = new VenueTypeDef { defName = "HH_TestOnly_Venue_Spa", label = "test spa", module = ModuleId.Wellness, priceFactor = 1f, defaultOpenHour = 0, defaultCloseHour = 0 };
                DefDatabase<VenueTypeDef>.Add(type);
                t.Note("HH_Venue_Spa missing: using a test-only venue type");
            }
            var signDef = DefDatabase<ThingDef>.GetNamedSilentFail("HH_VenueSign") ?? DefDatabase<ThingDef>.GetNamed("HH_TestOnly_VenueSign");
            var sign = WT.Spawn(map, signDef, center + new IntVec3(-3, 0, -3));
            var sc = sign.TryGetComp<CompVenueSign>();
            sc.type = type; sc.alwaysOpen = true; Venues.MarkDirty(map);
            yield return 5;
            var venue = Venues.All(map).FirstOrDefault(v => v.Sign == sign);
            t.Check(venue != null, "spa venue registered");
            t.Check(WellnessBilling.IsSpaVenueType(venue?.Type), "venue counts as a spa");
            t.Check(tub.GetRoom() == venue?.Room && stove.GetRoom() == venue?.Room, "tub and stove are in the venue room");
            stove.GetRoom().Temperature = 24f; // below the sauna minimum: only the tub is on offer, and the room is safe to walk into

            // visitors through the vanilla incident
            t.Note("visitors spawned: " + Hearth.Testing.GuestTestKit.SpawnVisitors(map, 1).Count);
            Pawn guest = null;
            foreach (var x in WT.WaitUntil(() => (guest = map.mapPawns.AllPawnsSpawned.FirstOrDefault(GuestApi.IsGuest)) != null, 900, 30)) yield return x;
            t.Check(guest != null, "a visitor counts as a guest");
            if (guest == null) { tub.Destroy(); stove.Destroy(); yield break; }
            var info = GuestApi.Info(guest);
            info.Budget = Money.FromSilver(500); info.Wealth = 60;
            WellnessState.Current.lastSession.Clear();
            var act = GuestActivities.All.First(a => a.Id == "wellness.spa");
            float d = act.Desire(guest, info);
            t.Check(d > 0f, "guest wants the spa: desire " + d.ToString("0.00"));
            HSettings.Set(WellnessSettings.GuestWeight, 0f);
            t.Check(act.Desire(guest, info) == 0f, "weight 0 switches the activity off");
            HSettings.Set(WellnessSettings.GuestWeight, 1f);
            tub.TryGetComp<CompWellnessFacility>().guestsMayUse = false;
            stove.TryGetComp<CompWellnessFacility>().guestsMayUse = false;
            t.Check(act.Desire(guest, info) == 0f, "guests switched off on all facilities: no desire");
            tub.TryGetComp<CompWellnessFacility>().guestsMayUse = true;
            stove.TryGetComp<CompWellnessFacility>().guestsMayUse = true;

            // price and charging
            var tubPrice = WellnessBilling.Price(tub, WellnessKind.HotTub);
            t.Check(tubPrice > Money.Zero && tubPrice <= HSettings.GetSilver(WellnessSettings.PriceHotTub), "unstaffed tub price has the self-service discount: " + tubPrice);
            var till = Payments.ColonyTill(map);
            var tillBefore = Ledger.Current.Balance(till);
            float satBefore = info.Satisfaction; var budgetBefore = info.Budget;
            // pick the tub explicitly (sauna would also be fine)
            tub.GetRoom().Temperature = 24f;
            var job = WellnessStaff.MakeUseJob(guest, tub);
            t.Check(job != null && job.def == WellnessDefOf.HH_UseHotTub, "tub job for the guest" + (job == null ? " (guest at " + guest.Position + " spawned " + guest.Spawned + ", reserve " + guest.CanReserve(tub, 2, 0) + ", reach " + guest.CanReach(tub, PathEndMode.OnCell, Danger.None)
                + ", room " + tub.GetRoom()?.Temperature.ToString("0") + " C, outdoor " + map.mapTemperature.OutdoorTemp.ToString("0") + " C, safe range " + guest.SafeTemperatureRange() + ")" : ""));
            WT.ClearJob(guest);
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            // the visitor may walk across the map first, and earlier suites already counted soaks: wait for THIS guest's finished session
            foreach (var x in WT.WaitUntil(() => WellnessState.Current.lastSession.ContainsKey(guest.thingIDNumber) || guest.CurJob?.def != WellnessDefOf.HH_UseHotTub, 14000, 50)) yield return x;
            t.Check(WellnessState.Current.sessionsHotTub >= 1, "guest finished a soak");
            t.Check(Ledger.Current.Balance(till) > tillBefore, "colony till earned money: " + (Ledger.Current.Balance(till) - tillBefore));
            t.Check(info.Budget < budgetBefore, "guest budget reduced");
            t.Check(info.Satisfaction > satBefore, "satisfaction rose " + satBefore.ToString("0.00") + " -> " + info.Satisfaction.ToString("0.00"));
            t.Check(WellnessState.Current.revenueMicro > 0, "revenue counted");
            t.Check(act.Desire(guest, info) == 0f, "cooldown after a session");

            // day pass: wealthy guest buys it once, later sessions are free
            WellnessState.Current.lastSession.Clear();
            var passPrice = WellnessBilling.PassPrice(tub);
            var sauna = stove;
            var b0 = info.Budget;
            t.Check(WellnessBilling.ChargeSession(guest, sauna, WellnessKind.Sauna), "charge sauna");
            bool boughtPass = WellnessState.Current.HasPass(guest, venue.Sign.thingIDNumber);
            t.Note("day pass bought=" + boughtPass + " (pass " + passPrice + ", single " + WellnessBilling.Price(sauna, WellnessKind.Sauna) + ")");
            if (boughtPass)
            {
                var b1 = info.Budget;
                t.Check(WellnessBilling.ChargeSession(guest, tub, WellnessKind.HotTub) && info.Budget == b1, "second session free with the pass");
            }
            WellnessState.Current.passExpiry.Clear(); WellnessState.Current.passVenue.Clear();
            // broke guest: not charged, no desire
            info.Budget = Money.FromSilver(1);
            t.Check(!WellnessBilling.ChargeSession(guest, tub, WellnessKind.HotTub), "broke guest cannot pay");
            t.Check(act.Desire(guest, info) == 0f, "broke guest has no desire");
            // closed venue
            info.Budget = Money.FromSilver(500);
            sc.alwaysOpen = false; sc.openHour = (GenLocalDate.HourOfDay(map) + 6) % 24; sc.closeHour = (sc.openHour + 2) % 24;
            t.Check(!venue.IsOpenNow && act.Desire(guest, info) == 0f, "closed spa: no desire");
            sc.alwaysOpen = true;
            WT.ClearJob(guest);
            tub.Destroy(); stove.Destroy();
        }
    }

    public sealed class RehabSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "wellness.rehab";
        public override ModuleId Module => ModuleId.Wellness;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            var map = t.Map; var pawn = WT.Colonist(map, 2) ?? WT.Colonist(map);
            var area = map.Center + new IntVec3(-14, 0, -14);
            WT.Clear(map, area, 6);
            var bed = (Building_Bed)WT.Spawn(map, ThingDefOf.Bed, area + new IntVec3(-3, 0, 0));
            bed.Medical = true;
            var tub = WT.Spawn(map, WellnessDefOf.HH_HotTub, area + new IntVec3(2, 0, 2));
            WT.Power(tub);
            var giver = DefDatabase<JoyGiverDef>.GetNamed("HH_Joy_HotTub").Worker;
            bool prevForce = RehabBridge.ForceActive;
            try
            {
                t.Check(!RehabBridge.MayUse(pawn, WellnessKind.HotTub, out string why) && why != null, "not in a bed: " + why);
                WT.ClearJob(pawn);
                // a tended cut keeps a patient in bed (vanilla medical rest)
                var wound = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, pawn.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "Torso"));
                wound.Severity = 3f; pawn.health.AddHediff(wound);
                wound.Tended(0.8f, 0.8f, 1);
                WT.Teleport(pawn, bed.Position);
                pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, bed), JobCondition.InterruptForced);
                foreach (var x in WT.WaitUntil(() => pawn.InBed(), 600, 20)) yield return x;
                t.Check(pawn.InBed(), "patient lies in the medical bed");
                RehabBridge.ForceActive = false;
                if (!ModsConfig.IsActive(RehabBridge.RimWardPackageId))
                    t.Check(!RehabBridge.MayUse(pawn, WellnessKind.HotTub, out why), "without RimWard: " + why);
                RehabBridge.ForceActive = true;
                t.Check(RehabBridge.MayUse(pawn, WellnessKind.HotTub, out why), "with RimWard: bed patient may soak " + why);
                t.Check(RehabBridge.MayUse(pawn, WellnessKind.Sauna, out why), "with RimWard: healthy bed patient may use the sauna " + why);
                HSettings.Set(WellnessSettings.RehabJoy, 0f);
                t.Check(!RehabBridge.MayUse(pawn, WellnessKind.HotTub, out why), "setting off: " + why);
                HSettings.Set(WellnessSettings.RehabJoy, 1f);
                pawn.needs.joy.CurLevel = 0.1f;
                var job = giver.TryGiveJobWhileInBed(pawn);
                t.Check(job != null && job.def == WellnessDefOf.HH_UseHotTub, "in-bed joy offers the tub to the patient");
                // fresh surgery: no sauna
                var cut = HediffMaker.MakeHediff(HediffDefOf.SurgicalCut, pawn, pawn.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "Torso"));
                pawn.health.AddHediff(cut);
                t.Check(!RehabBridge.MayUse(pawn, WellnessKind.Sauna, out why) , "fresh surgery blocks the sauna: " + why);
                pawn.health.RemoveHediff(cut);
                // not for strangers / other faction beds
                bed.SetFaction(null);
                t.Check(!RehabBridge.MayUse(pawn, WellnessKind.HotTub, out why), "bed not owned by the colony: " + why);
                bed.SetFaction(Faction.OfPlayer);
                pawn.health.RemoveHediff(wound);
                // free colonist (no bed): the bridge does not apply, plain joy rules do
                WT.ClearJob(pawn);
                yield return 30;
            }
            finally { RehabBridge.ForceActive = prevForce; }
            WT.ClearJob(pawn);
            tub.Destroy(); bed.Destroy();
        }
    }
}
#endif
