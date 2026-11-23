// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// Work giver of the Serving work type (6.5): during opening hours (or while the night bell is ringing) a colonist
    /// goes to the venue's counter or sign and tends it. Only venues that need staff, or have a counter, are offered.
    /// </summary>
    public class WorkGiver_ServeVenue : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool Prioritized => true;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!Modules.On(ModuleId.ShopsFood)) return true;
            var map = pawn.Map; if (map == null) return true;
            return Venues.All(map).Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var map = pawn.Map; if (map == null) yield break;
            var all = Venues.All(map);
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (WantsTender(v)) yield return v.Sign;
            }
        }

        /// <summary>A venue wants a tender if it is (or is being rung) open and nobody tends it, and tending makes sense.</summary>
        public static bool WantsTender(Venue v)
        {
            if (!Modules.On(ModuleId.ShopsFood) || v?.Type == null) return false;
            if (!VenueSetup.IsReady(v)) return false;   // nothing to tend in a room that is not set up (F12)
            if (VenueHonor.IsOn(v)) return false;       // honor system (F15): the room runs unattended on purpose
            bool needs = VenueApi.NeedsStaff(v);
            if (!needs && VenueApi.CounterOf(v) == null) return false;
            bool inHours = VenueApi.InHours(v);
            if (!inHours && !(needs && VenueApi.BellActive(v))) return false;
            return !VenueApi.IsStaffed(v);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t == null || t.Map != pawn.Map || t.IsForbidden(pawn)) return null;
            var v = VenueApi.VenueOfSign(pawn.Map, t);
            if (v == null || !WantsTender(v)) return null;
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return null;
            var post = VenueApi.PostCell(v);
            if (!pawn.CanReach(post, PathEndMode.OnCell, Danger.None)) return null;
            if (pawn.Faction != Faction.OfPlayer) return null;
            var job = JobMaker.MakeJob(HHVenueDefOf.HH_TendVenue, t, post);
            // re-check needs (food, rest) every so often instead of standing for the whole shift
            job.expiryInterval = 1500;
            job.checkOverrideOnExpire = true;
            return job;
        }

        public override float GetPriority(Pawn pawn, TargetInfo t)
        {
            // nearer posts first; a ringing bell comes first of all
            float d = (pawn.Position - t.Cell).LengthHorizontal;
            float p = 100f - Math.Min(d, 90f);
            var v = VenueApi.VenueOfSign(pawn.Map, t.Thing);
            if (v != null && !VenueApi.InHours(v)) p += 50f;
            return p;
        }
    }

    /// <summary>Stand at the post of a venue, keep it open and learn Social. Ends after a shift or when the venue closes.</summary>
    public class JobDriver_TendVenue : JobDriver
    {
        Thing Sign => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        bool StillWanted()
        {
            if (Sign == null || !Sign.Spawned || !Modules.On(ModuleId.ShopsFood)) return false;
            var v = VenueApi.VenueOfSign(pawn.Map, Sign);
            if (v == null) return false;
            // switched to the honor system or no longer set up (F12/F15): the tender goes back to other work
            if (VenueHonor.IsOn(v) || !VenueSetup.IsReady(v)) return false;
            bool needs = VenueApi.NeedsStaff(v);
            if (VenueApi.InHours(v)) return true;
            return needs && VenueApi.BellActive(v);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !StillWanted());
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var tend = ToilMaker.MakeToil("TendVenue");
            tend.defaultCompleteMode = ToilCompleteMode.Delay;
            tend.defaultDuration = Math.Max(1, VS.I(VS.TendHours)) * GenDate.TicksPerHour;
            tend.initAction = () =>
            {
                VenueState.PingStaff(Sign, pawn);
                VenueService.BeginShift(pawn, Sign);
                pawn.pather.StopDead();
                // the bell was answered: the business stays open for a service window, so the guest who rang is served even if the bell was rung long ago
                var v = VenueApi.VenueOfSign(pawn.Map, Sign);
                if (v != null && !VenueApi.InHours(v) && VenueApi.BellActive(v)) VenueState.RingBell(Sign, VenueState.BellServiceTicks);
            };
            tend.tickAction = () =>
            {
                if (pawn.IsHashIntervalTick(30)) VenueState.PingStaff(Sign, pawn);
                if (pawn.IsHashIntervalTick(250)) Guard.Run(ModuleId.ShopsFood, "Venues.Shift", () => VenueService.TendTick(pawn));
                // a quiet counter teaches less than a busy one (F14)
                pawn.skills?.Learn(SkillDefOf.Social, VenueService.ServedThisShift(pawn) > 0 ? 0.05f : 0.03f, false);
                // face the sign so the tender looks at customers
                if (pawn.IsHashIntervalTick(120)) pawn.rotationTracker.FaceTarget(Sign);
            };
            tend.socialMode = RandomSocialMode.Off;
            yield return tend;
        }
    }

    /// <summary>Wait near a venue for service: for an open but unstaffed venue, or after ringing the night bell (5.13).</summary>
    public class JobDriver_WaitAtVenue : JobDriver
    {
        Thing Sign => job.targetA.Thing;
        bool InLounge => Sign != null && job.targetB.IsValid && Venues.At(job.targetB.Cell, pawn.Map)?.Type == HHVenueDefOf.HH_Venue_Lounge;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        Venue VenueNow() => VenueApi.VenueOfSign(pawn.Map, Sign);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var ring = ToilMaker.MakeToil("RingBell");
            ring.defaultCompleteMode = ToilCompleteMode.Instant;
            ring.initAction = () =>
            {
                var v = VenueNow(); if (v == null) return;
                if (!VenueApi.InHours(v) && VS.B(VS.NightBell))
                {
                    VenueState.RingBell(Sign, VS.I(VS.NightBellWaitHours) * GenDate.TicksPerHour);
                    MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "HH.Venue.BellRung".TranslateOr("*ding*"), 3f);
                }
            };
            yield return ring;
            var wait = ToilMaker.MakeToil("WaitForService");
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            bool bell = job.count == 1;
            wait.defaultDuration = bell ? VS.I(VS.NightBellWaitHours) * GenDate.TicksPerHour : VS.I(VS.WaitForStaffTicks);
            wait.handlingFacing = true;
            wait.tickAction = () =>
            {
                if (pawn.IsHashIntervalTick(20))
                {
                    var v = VenueNow();
                    if (v == null) { EndJobWith(JobCondition.Incompletable); return; }
                    if (VenueApi.Gate(v) == VenueGate.Open) { EndJobWith(JobCondition.Succeeded); return; }
                    pawn.rotationTracker.FaceTarget(Sign);
                }
            };
            yield return wait;
            // reached only when nobody came
            var gaveUp = ToilMaker.MakeToil("NobodyCame");
            gaveUp.defaultCompleteMode = ToilCompleteMode.Instant;
            gaveUp.initAction = () =>
            {
                bool lounge = InLounge;
                float penalty = job.count == 1 ? -0.03f : -0.02f;
                if (lounge) penalty *= 0.5f;
                string why = job.count == 1 ? "HH.Venue.NobodyCameBell".TranslateOr("Rang the bell. Nobody came.") : "HH.Venue.NobodyCameStaff".TranslateOr("Waited for service. Nobody came.");
                GuestApi.AddSatisfaction(pawn, penalty, why);
                GuestMemory.SetNoService(pawn, 6000);
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, why, 4f);
            };
            yield return gaveUp;
        }
    }
}
