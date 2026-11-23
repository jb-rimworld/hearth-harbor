// SPDX-License-Identifier: MIT
// In-game suite of the night bell call (F06/F16). Built only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.ShopsFood
{
    public sealed class NightBellSuite : HearthTestSuite
    {
        public override string Name => "venues.nightbell";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 16000;

        static readonly Dictionary<Pawn, IntVec3> stations = new Dictionary<Pawn, IntVec3>();

        /// <summary>Colonists with no job wander off (or go cleaning); the hearing checks depend on where they stand, so each check puts them back at their station first.</summary>
        internal static void Place(Pawn p)
        {
            if (p == null || p.Destroyed || !p.Spawned || !stations.TryGetValue(p, out var at) || p.Position == at) return;
            p.jobs?.StopAll();
            p.Position = at;
            p.Notify_Teleported(false);
        }

        internal static Pawn NewColonist(Map map, IntVec3 at, List<Pawn> made)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                var np = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, fixedBiologicalAge: 28f, fixedChronologicalAge: 28f));
                if (np.workSettings == null || np.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving)) { np.Destroy(); continue; }
                // a random colonist may come with old-age hearing loss, an illness or an injury: they would not hear the bell or would be sent to rest, which has nothing to do with the bell
                foreach (var hd in np.health.hediffSet.hediffs.ToList()) np.health.RemoveHediff(hd);
                GenSpawn.Spawn(np, at, map);
                np.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                // no other work: a test colonist who goes off cleaning or hauling is not where the checks expect them
                foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading) if (!np.WorkTypeIsDisabled(wt)) np.workSettings.SetPriority(wt, 0);
                stations[np] = at;
                if (np.needs?.rest != null) np.needs.rest.CurLevel = 0.9f;
                if (np.needs?.food != null) np.needs.food.CurLevel = 0.9f;
                made.Add(np);
                return np;
            }
            return null;
        }

        internal static void Sleep(Pawn p)
        {
            Place(p);
            p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, p.Position), JobCondition.InterruptForced);
            if (p.jobs.curDriver != null) p.jobs.curDriver.asleep = true;
            if (p.needs?.rest != null) p.needs.rest.CurLevel = 0.9f;
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var serving = HHVenueDefOf.HH_Serving;
            // ---- defaults
            t.Check(HSettings.GetEnum<BellWakes>(NightBellSettings.Wakes) == BellWakes.WakeVolunteers && HSettings.GetEnum<BellWho>(NightBellSettings.Who) == BellWho.ServingOnly, "defaults: wake volunteers, Serving work only");
            t.Check(HSettings.GetInt(NightBellSettings.Range) == 28 && HSettings.GetInt(NightBellSettings.Responders) == 1 && HSettings.GetInt(NightBellSettings.WakeCooldownHours) == 12, "defaults: range 28, one responder, 12 hours sleep protection");
            t.Check(HHBellDefOf.HH_NightBell != null && HHBellDefOf.HH_BellAnswered != null && HHBellDefOf.HH_Mote_Bell != null && HHBellDefOf.HH_Mote_RingArc != null && HHBellDefOf.HH_WokenByBell != null, "sound, mote and thought defs resolved");
            foreach (var k in new[] { "HH.Enum.BellWakes.Off", "HH.Enum.BellWakes.AwakeOnly", "HH.Enum.BellWakes.WakeVolunteers", "HH.Enum.BellWho.ServingOnly", "HH.Enum.BellWho.AnyColonist" })
                t.Check(k.CanTranslate(), "dropdown label exists: " + k);
            t.Check(HHBellDefOf.HH_Mote_Bell.thingClass == typeof(MoteBell), "the bell mote swings (own mote class)");

            // ---- a closed tavern and some colonists we control
            var savedPrio = new Dictionary<Pawn, int>();
            foreach (var c in map.mapPawns.FreeColonistsSpawned) if (c.workSettings != null) { savedPrio[c] = c.workSettings.GetPriority(serving); c.workSettings.SetPriority(serving, 0); }
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            // 1.1.0 (F12): a venue that is not set up is not ready and nobody tends it, so the tavern gets what the checklist requires (counter, table, seats)
            V11Kit.FurnishTavern(map, room);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 8) % 24; comp.closeHour = (h + 11) % 24;
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null && !VenueApi.InHours(venue), "a tavern that is closed now");
            t.Check(venue != null && VenueSetup.IsReady(venue), "and ready to open (counter, table and seats stand in it)");
            var made = new List<Pawn>();
            // the colonists stand just outside the door, in a row, so the hearing checks do not depend on where the random room landed
            var outside = new IntVec3(room.Door.x, 0, room.Door.z - 2);
            var awake = NewColonist(map, outside, made);
            var sleeper = NewColonist(map, outside + new IntVec3(-2, 0, -1), made);
            var far = NewColonist(map, new IntVec3(6, 0, 6), made);
            var mover = NewColonist(map, outside + new IntVec3(2, 0, -1), made);
            t.Check(awake != null && sleeper != null && far != null && mover != null, "four test colonists");
            if (awake == null || sleeper == null || far == null || mover == null) { Cleanup(map, made, savedPrio, room); yield break; }
            var oldWakes = HSettings.Get(NightBellSettings.Wakes); var oldWho = HSettings.Get(NightBellSettings.Who);
            VenueState.RingBell(sign, 12000);
            try
            {
                foreach (var c in made) Place(c);
                awake.workSettings.SetPriority(serving, 1);
                far.workSettings.SetPriority(serving, 1);
                float farDist = (far.Position - sign.Position).LengthHorizontal;
                // ---- who can hear it
                var list = NightBell.Candidates(venue, BellWakes.AwakeOnly);
                var ca = list.FirstOrDefault(x => x.Pawn == awake);
                t.Check(ca != null && ca.Chance > 0.5f && !ca.Sleeping, "an awake colonist with Serving at the venue could hear it: " + (ca == null ? "-" : ca.Chance.ToString("0.00") + " at " + ca.Distance.ToString("0") + " cells"));
                t.Check(list.All(x => x.Pawn != mover), "a colonist with the Serving work switched off is not called");
                t.Check(farDist < 36f || list.All(x => x.Pawn != far), $"a colonist {farDist:0} cells away does not hear it");
                NightBell.HearingOverride = p => p == awake ? 0f : 1f;
                t.Check(NightBell.Candidates(venue, BellWakes.AwakeOnly).All(x => x.Pawn != awake), "a deaf colonist never hears it");
                NightBell.HearingOverride = p => p == awake ? 0.3f : 1f;
                var weak = NightBell.Candidates(venue, BellWakes.AwakeOnly).FirstOrDefault(x => x.Pawn == awake);
                t.Check(weak == null || weak.Chance < ca.Chance, "poor hearing lowers the chance (" + (weak == null ? "out of range" : weak.Chance.ToString("0.00")) + " vs " + ca.Chance.ToString("0.00") + ")");
                NightBell.HearingOverride = null;
                float straight = (outside - sign.Position).LengthHorizontal;
                float muffled = NightBell.EffectiveDistance(map, outside, sign.Position);
                t.Check(muffled >= straight + 2.9f, $"walls and doors muffle the sound: {straight:0.0} cells straight, {muffled:0.0} as the sound travels");
                t.Check(NightBell.EffectiveDistance(map, room.Inner.CenterCell, sign.Position) <= (room.Inner.CenterCell - sign.Position).LengthHorizontal + 0.01f, "inside the same room nothing muffles it");

                // ---- a call: the awake colonist hurries to the venue
                NightBell.ForceHear = true;
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.AwakeOnly);
                int n = NightBell.Alert(venue, null);
                t.Check(n == 1, "one colonist answers the call (responders = 1)");
                t.Check(awake.CurJob != null && awake.CurJob.def == HHVenueDefOf.HH_TendVenue && awake.CurJob.targetA.Thing == sign, "the colonist has the venue job: " + awake.CurJobDef?.defName);
                t.Check(awake.CurJob != null && awake.CurJob.locomotionUrgency == LocomotionUrgency.Jog, "and hurries (jog)");
                int waited = 0;
                while (waited < 3500 && VenueState.StaffOn(sign) != awake) { VenueState.RingBell(sign, 12000); yield return 50; waited += 50; }
                t.Check(VenueState.StaffOn(sign) == awake, "the colonist reached the post and tends the venue after " + waited + " ticks");
                var mc = map.GetComponent<MapComponent_HH_NightBell>();
                yield return 120;
                t.Check(mc != null && mc.Pending.All(a => a.Pawn != awake), "the call is marked as answered");
                t.Check(NightBell.Alert(venue, null) == 0, "nobody else is called while the venue is tended");
                awake.jobs.StopAll();
                yield return 130;   // the tender's staff ping expires

                // ---- sleepers: only with the setting, only volunteers, never twice
                awake.workSettings.SetPriority(serving, 0); far.workSettings.SetPriority(serving, 0);
                sleeper.workSettings.SetPriority(serving, 1);
                Sleep(sleeper);
                t.Check(!sleeper.Awake(), "the test colonist is asleep");
                t.Check(NightBell.Candidates(venue, BellWakes.AwakeOnly).All(x => x.Pawn != sleeper) && NightBell.Alert(venue, null) == 0 && !sleeper.Awake(), "with 'awake only' a sleeper is left alone");
                var sc = NightBell.Candidates(venue, BellWakes.WakeVolunteers).FirstOrDefault(x => x.Pawn == sleeper);
                t.Check(sc != null && sc.Sleeping && sc.Chance > 0f, "with 'wake volunteers' the sleeper is a candidate (chance " + (sc?.Chance.ToString("0.00") ?? "-") + (sc != null ? "" : $"; hearing {sleeper.health.capacities.GetLevel(PawnCapacityDefOf.Hearing):0.00}, distance {NightBell.EffectiveDistance(map, sleeper.Position, sign.Position):0}, rest {sleeper.needs.rest?.CurLevel:0.00}, medical rest {HealthAIUtility.ShouldSeekMedicalRest(sleeper)}, danger {map.dangerWatcher.DangerRating}, age {sleeper.ageTracker.AgeBiologicalYears}") + ")");
                sleeper.jobs.StopAll();
                var sa = NightBell.Candidates(venue, BellWakes.AwakeOnly).FirstOrDefault(x => x.Pawn == sleeper);
                t.Check(sc != null && sa != null && sc.Chance < sa.Chance, $"the same colonist hears it less easily asleep ({sc?.Chance:0.00}) than awake ({sa?.Chance:0.00})");
                Sleep(sleeper);
                sleeper.workSettings.SetPriority(serving, 3);
                bool manual = Find.PlaySettings.useWorkPriorities;
                if (manual) t.Check(NightBell.Candidates(venue, BellWakes.WakeVolunteers).All(x => x.Pawn != sleeper), "a sleeper with Serving only at priority 3 is not a volunteer");
                sleeper.workSettings.SetPriority(serving, 1);
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.WakeVolunteers);
                var lastWake = GameComponent_HH_NightBell.Instance;
                t.Check(lastWake != null && lastWake.CanWake(sleeper), "nobody has been woken yet");
                n = NightBell.Alert(venue, null);
                t.Check(n == 1 && sleeper.CurJob?.def == HHVenueDefOf.HH_TendVenue, "the sleeping volunteer is woken and sent to the venue (job " + sleeper.CurJobDef?.defName + ")");
                t.Check(sleeper.needs.mood.thoughts.memories.GetFirstMemoryOfDef(HHBellDefOf.HH_WokenByBell) != null, "and remembers being woken");
                t.Check(!lastWake.CanWake(sleeper), "who was woken is protected from the next wake-up (12 hours)");
                sleeper.jobs.StopAll();
                yield return 130;
                Sleep(sleeper);
                t.Check(NightBell.Candidates(venue, BellWakes.WakeVolunteers).All(x => x.Pawn != sleeper) && NightBell.Alert(venue, null) == 0, "asleep again, the colonist is not woken a second time");
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.Off);
                sleeper.jobs.StopAll();
                t.Check(NightBell.Alert(venue, null) == 0, "setting 'off': the bell calls nobody");

                // ---- who may answer
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.AwakeOnly);
                HSettings.Set(NightBellSettings.Who, (float)(int)BellWho.AnyColonist);
                sleeper.jobs.StopAll(); awake.workSettings.SetPriority(serving, 0);
                Place(awake); Place(mover); Place(sleeper);
                var any = NightBell.Candidates(venue, BellWakes.AwakeOnly);
                t.Check(any.Any(x => x.Pawn == mover) && any.Any(x => x.Pawn == awake), "'any colonist' also calls colonists whose Serving work is switched off");
                // review fix: a sleeper whose Serving work is off is never woken, not even with 'any colonist'
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.WakeVolunteers);
                Sleep(mover);
                t.Check(NightBell.Candidates(venue, BellWakes.WakeVolunteers).All(x => x.Pawn != mover), "a sleeper with Serving switched off is no volunteer, even with 'any colonist'");
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.AwakeOnly);
                HSettings.Set(NightBellSettings.Who, (float)(int)BellWho.ServingOnly);

                // ---- one ring, one call: a second ring a moment later only refreshes
                Place(awake);
                awake.workSettings.SetPriority(serving, 1); sleeper.workSettings.SetPriority(serving, 0);
                var rs = mc.StateOf(sign);
                rs.LastAlert = -99999; rs.LastSound = -99999; rs.AnyResponder = false;
                NightBell.ForceHear = false;
                NightBell.Ring(venue, null);
                int alert1 = rs.LastAlert, sound1 = rs.LastSound;
                t.Check(alert1 > 0 && !rs.AnyResponder, "ringing makes a call; nobody heard it this time");
                yield return 40;
                NightBell.Ring(venue, null);
                t.Check(rs.LastAlert == alert1 && rs.LastSound == sound1, "a second ring right after only refreshes: no second call, no second sound");
                NightBell.ForceHear = true;
                yield return 600;
                NightBell.Ring(venue, null);
                t.Check(rs.LastAlert > alert1 && awake.CurJobDef == HHVenueDefOf.HH_TendVenue, "when nobody heard the first call, a later ring tries again (job " + awake.CurJobDef?.defName + ")");
                t.Check(sign.TryGetComp<CompVenueSignHH>().CompInspectStringExtra().Contains("Night bell"), "the sign's text mentions the ringing bell: " + sign.TryGetComp<CompVenueSignHH>().CompInspectStringExtra().Replace("\n", " | "));
                awake.jobs.StopAll();
            }
            finally
            {
                NightBell.ForceHear = null; NightBell.HearingOverride = null;
                HSettings.Set(NightBellSettings.Wakes, oldWakes); HSettings.Set(NightBellSettings.Who, oldWho);
                Cleanup(map, made, savedPrio, room);
            }
        }

        internal static void Cleanup(Map map, List<Pawn> made, Dictionary<Pawn, int> savedPrio, VenueTestKit.TestRoom room)
        {
            foreach (var p in made) { if (p != null && !p.Destroyed) { p.jobs?.StopAll(); p.Destroy(); } stations.Remove(p); }
            foreach (var kv in savedPrio) if (kv.Key != null && !kv.Key.Dead && !kv.Key.Destroyed && kv.Key.workSettings != null) kv.Key.workSettings.SetPriority(HHVenueDefOf.HH_Serving, kv.Value);
            VenueTestKit.Teardown(map, room);
        }
    }
    /// <summary>
    /// F06/F16 acceptance: a lone guest whose first ring was not heard rings again until somebody comes; an awake Serving colonist within earshot
    /// always hears it; a colonist whose venue job fails on the way is replaced (the call is opened again at once).
    /// </summary>
    public sealed class BellRetrySuite : HearthTestSuite
    {
        public override string Name => "venues.bellretry";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 14000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var serving = HHVenueDefOf.HH_Serving;
            VenueTestKit.ClearGuests(map);
            var savedPrio = new Dictionary<Pawn, int>();
            foreach (var c in map.mapPawns.FreeColonistsSpawned) if (c.workSettings != null) { savedPrio[c] = c.workSettings.GetPriority(serving); c.workSettings.SetPriority(serving, 0); }
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            V11Kit.FurnishTavern(map, room);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 8) % 24; comp.closeHour = (h + 11) % 24;
            yield return 30;
            Venues.MarkDirty(map);
            var venue = VenueApi.VenueOfSign(map, sign);
            t.Check(venue != null && !VenueApi.InHours(venue) && VenueSetup.IsReady(venue), "a closed, ready tavern");
            var made = new List<Pawn>();
            var outside = new IntVec3(room.Door.x, 0, room.Door.z - 2);
            var awake = NightBellSuite.NewColonist(map, outside, made);
            var sleeper = NightBellSuite.NewColonist(map, outside + new IntVec3(-2, 0, -1), made);
            List<Pawn> guests = new List<Pawn>();
            var oldWakes = HSettings.Get(NightBellSettings.Wakes); var oldWho = HSettings.Get(NightBellSettings.Who);
            bool oldBell = VS.B(VS.NightBell);
            try
            {
                t.Check(awake != null && sleeper != null && venue != null, "two test colonists");
                if (awake == null || sleeper == null || venue == null) yield break;
                HSettings.Set(VS.NightBell, 1f);
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.WakeVolunteers);
                awake.workSettings.SetPriority(serving, 1);
                NightBellSuite.Place(awake); NightBellSuite.Place(sleeper);
                VenueState.RingBell(sign, 600);
                // ---- an awake colonist within earshot always hears it; a sleeper only by chance; poor hearing stays a chance
                var ca = NightBell.Candidates(venue, BellWakes.AwakeOnly).FirstOrDefault(x => x.Pawn == awake);
                t.Check(ca != null && !ca.Sleeping && Math.Abs(ca.Chance - 1f) < 1e-4f, "awake Serving colonist in range hears it for sure: " + (ca == null ? "-" : ca.Chance.ToString("0.00")));
                NightBell.HearingOverride = p => p == awake ? 0.25f : 1f;
                var weak = NightBell.Candidates(venue, BellWakes.AwakeOnly).FirstOrDefault(x => x.Pawn == awake);
                t.Check(weak == null || weak.Chance < 1f, "with very poor hearing (0.25) it stays a chance: " + (weak == null ? "out of range" : weak.Chance.ToString("0.00")));
                NightBell.HearingOverride = null;
                sleeper.workSettings.SetPriority(serving, 1);
                NightBellSuite.Sleep(sleeper);
                var sc = NightBell.Candidates(venue, BellWakes.WakeVolunteers).FirstOrDefault(x => x.Pawn == sleeper);
                t.Check(sc == null || (sc.Sleeping && sc.Chance < 1f), "a sleeper only hears it by chance: " + (sc == null ? "not a candidate" : sc.Chance.ToString("0.00")));
                sleeper.workSettings.SetPriority(serving, 0);
                sleeper.jobs.StopAll();
                // from here on only the bell may send the colonist: Serving is switched off (so the work giver never sends them on its own)
                // and 'any colonist' lets the bell call them anyway
                awake.workSettings.SetPriority(serving, 0);
                HSettings.Set(NightBellSettings.Who, (float)(int)BellWho.AnyColonist);
                HSettings.Set(NightBellSettings.Wakes, (float)(int)BellWakes.AwakeOnly);

                // ---- one guest rings; the first ring is not heard (forced), the colonist does not come
                guests = Hearth.Casino.Tests.Kit.Visitors(t, 1, 300, VenueTestKit.OutsideCell(map, room));
                t.Check(guests.Count == 1, "one guest");
                if (guests.Count == 0) yield break;
                var guest = guests[0];
                var mc = map.GetComponent<MapComponent_HH_NightBell>();
                var rs = mc.StateOf(sign);
                rs.LastAlert = -99999; rs.LastSound = -99999; rs.LastRing = -99999; rs.AnyResponder = false;
                NightBell.ForceHear = false;
                guest.jobs.StartJob(VenueShopping.WaitJob(venue, true), JobCondition.InterruptForced);
                int waited = 0;
                while (waited < 2500 && rs.LastRing < 0) { yield return 20; waited += 20; }
                t.Check(rs.LastRing > 0 && NightBell.IsWaiting(guest, sign), "the guest rang and waits (" + guest.CurJobDef?.defName + ", after " + waited + " ticks)");
                int firstRing = rs.LastRing, firstAlert = rs.LastAlert;
                t.Check(awake.CurJobDef != HHVenueDefOf.HH_TendVenue && !rs.AnyResponder, "the first ring was not heard: nobody comes (" + awake.CurJobDef?.defName + ")");
                yield return 60;
                NightBell.ForceHear = null;   // from now on the normal rule: awake and in range = hears it
                // ---- the guest rings again (every ~600 ticks) and a colonist answers ('any colonist': the colony's own people may be the best choice)
                Pawn Sent() => mc.Pending.FirstOrDefault(x => x.Sign == sign && x.Pawn != null && x.Pawn.CurJobDef == HHVenueDefOf.HH_TendVenue)?.Pawn;
                waited = 0;
                while (waited < 1600 && Sent() == null)
                {
                    if (waited % 100 == 0) NightBellSuite.Place(awake);
                    yield return 10; waited += 10;
                }
                var resp = Sent();
                t.Check(rs.LastRing > firstRing && rs.LastAlert > firstAlert, $"the waiting guest rang again ({rs.LastRing - firstRing} ticks after the first ring)");
                t.Check(resp != null && resp.CurJob.targetA.Thing == sign, "the second ring brings a colonist (" + resp?.LabelShort + ", " + resp?.CurJobDef?.defName + ")");
                t.Check(NightBell.SomeoneComing(sign), "the sign knows somebody is on the way");
                if (resp == null) yield break;

                // ---- the colonist's job fails on the way: the call is opened again at once and somebody is sent again
                int reopened0 = NightBell.Reopened;
                var hold = JobMaker.MakeJob(JobDefOf.Wait, 2000);
                resp.jobs.StartJob(hold, JobCondition.InterruptForced);
                waited = 0;
                while (waited < 300 && (NightBell.Reopened == reopened0 || Sent() == null)) { yield return 10; waited += 10; }
                var resp2 = Sent();
                t.Check(NightBell.Reopened > reopened0, "a failed answer opens the call again (after " + waited + " ticks)");
                t.Check(resp2 != null, "and a colonist is sent again right away (" + resp2?.LabelShort + ", " + resp2?.CurJobDef?.defName + ")");
                t.Check(waited <= 120, "quickly, not after 4000 ticks: " + waited + " ticks");
                if (resp2 == null) yield break;
                t.Check(!mc.Resting(resp, Find.TickManager.TicksGame), "one dropped call is forgiven at once (the colonist may be sent again)");
                // ---- a call that outlives PendingTimeout while the colonist is still walking stays theirs: nobody is sent alongside
                var entry = mc.Pending.FirstOrDefault(x => x.Sign == sign && x.Pawn == resp2);
                if (entry != null && resp2.pather != null && resp2.pather.Moving)
                {
                    entry.Since -= NightBell.PendingTimeout + 100;
                    yield return 1;
                    bool kept = mc.Pending.Contains(entry) || VenueState.StaffOn(sign) == resp2;
                    t.Check(kept, "a colonist still walking there keeps the call after the timeout");
                    int coming = map.mapPawns.FreeColonistsSpawned.Count(c => c.CurJob?.def == HHVenueDefOf.HH_TendVenue && c.CurJob.targetA.Thing == sign);
                    t.Check(coming <= 1, "and no second colonist walks to the same sign (" + coming + ")");
                    rs.LastAlert = -99999;
                    t.Check(NightBell.Alert(venue, guest) == 0, "a new call sends nobody while somebody is on the way");
                }
                else t.Note("the colonist was already there: timeout check skipped");

                // ---- the colonist arrives, the guest is served (or at least no longer waits for the bell)
                waited = 0;
                while (waited < 4000 && VenueState.StaffOn(sign) != resp2) { yield return 50; waited += 50; }
                t.Check(VenueState.StaffOn(sign) == resp2, "the colonist reached the post after " + waited + " ticks");
                yield return 60;
                t.Check(!NightBell.IsWaiting(guest, sign), "the guest stops waiting once the venue is staffed (" + guest.CurJobDef?.defName + ")");
                t.Check(mc.Pending.All(a => a.Sign != sign), "the call is answered");
            }
            finally
            {
                NightBell.ForceHear = null; NightBell.HearingOverride = null;
                HSettings.Set(NightBellSettings.Wakes, oldWakes); HSettings.Set(NightBellSettings.Who, oldWho);
                HSettings.Set(VS.NightBell, oldBell ? 1f : 0f);
                foreach (var g in guests) if (g != null && !g.Destroyed) g.Destroy();
                NightBellSuite.Cleanup(map, made, savedPrio, room);
            }
        }
    }
}
#endif
