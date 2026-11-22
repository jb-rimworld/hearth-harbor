// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.City
{
    /// <summary>Guest activity "city.cinema": buy a ticket, sit down in front of a powered projector screen, watch a film.</summary>
    public sealed class CinemaActivity : CityActivity
    {
        public override string Id => "city.cinema";
        protected override string Kind => "cinema";
        protected override int CooldownTicks => CitySettings.HoursToTicks(CitySettings.CinemaCooldownHours);
        public static readonly Money Reference = Money.FromSilver(12);

        protected override bool Unlocked => CityAccess.Level1;

        public static Money TicketPrice(CinemaSpot s) => CitySettings.Silver(CitySettings.CinemaTicket) * (s.Venue.Comp.priceFactor * s.Venue.Type.priceFactor);

        public static bool Usable(CinemaSpot s)
            => s.Screen.Spawned && s.Comp.CanJoin(0.4f) && s.Seats >= CitySettings.Int(CitySettings.CinemaMinSeats) && CityScan.IsOpenForBusiness(s.Venue);

        protected override float DesireCore(Pawn guest, GuestInfo info)
        {
            var map = guest.Map; float best = 0f;
            foreach (var s in CityScan.Cinemas(map))
            {
                if (!Usable(s)) continue;
                var price = TicketPrice(s);
                if (GuestApi.Budget(guest) < price) continue;
                double buy = CityPricing.BuyChance(guest, info, price, Reference, "cinema");
                float dist = guest.Position.DistanceTo(s.Screen.Position);
                float w = 0.8f * (float)buy / (1f + dist / 40f);
                if (s.Comp.ShowActive) w *= 1.2f; // a film already running is a crowd puller
                best = Mathf.Max(best, w);
            }
            return best;
        }

        public override Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            if (!CityAccess.Level1) return null;
            var spots = CityScan.Cinemas(guest.Map).Where(s => Usable(s) && GuestApi.Budget(guest) >= TicketPrice(s))
                .OrderBy(s => guest.Position.DistanceToSquared(s.Screen.Position));
            foreach (var s in spots)
            {
                if (!guest.CanReserveAndReach(s.Screen, PathEndMode.Touch, Danger.None, 24, 0)) continue;
                if (!WatchBuildingUtility.TryFindBestWatchCell(s.Screen, guest, true, out var cell, out var chair)) continue;
                var job = JobMaker.MakeJob(CityDefs.WatchScreening, s.Screen, cell, chair);
                job.ignoreJoyTimeAssignment = true;
                job.locomotionUrgency = LocomotionUrgency.Walk;
                return job;
            }
            return null;
        }
    }

    /// <summary>Watching a film: guests (paid ticket) and colonists (free, joy). Shared by movie nights, joy giver and the activity.</summary>
    public class JobDriver_HHWatchScreening : JobDriver
    {
        Thing Screen => job.targetA.Thing;
        CompProjectorScreen Comp => Screen?.TryGetComp<CompProjectorScreen>();
        int watched;
        bool joined, boughtTicket;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, Math.Max(1, job.def.joyMaxParticipants), 0, null, errorOnFailed)) return false;
            if (!pawn.ReserveSittableOrSpot(job.targetB.Cell, job, errorOnFailed)) return false;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.EndOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Comp == null || !Comp.Powered);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            var sit = Toils.Make("SitDown");
            sit.initAction = () =>
            {
                var comp = Comp;
                if (comp == null) { EndJobWith(JobCondition.Incompletable); return; }
                if (GuestApi.IsGuest(pawn) && pawn.Faction != Faction.OfPlayer)
                {
                    if (!BuyTicket()) { EndJobWith(JobCondition.Incompletable); return; }
                }
                comp.Join(pawn); joined = true;
                if (!comp.ShowActive) comp.StartShow(CitySettings.Int(CitySettings.CinemaShowTicks));
            };
            yield return sit;

            var watch = Toils.Make("Watch");
            watch.defaultCompleteMode = ToilCompleteMode.Never;
            watch.handlingFacing = true;
            bool guest = GuestApi.IsGuest(pawn) && pawn.Faction != Faction.OfPlayer;
            Toils.OnTick(watch, delta =>
            {
                var comp = Comp; if (comp == null) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker.FaceCell(Screen.Position);
                Toils.Comfort(pawn, delta);
                if (!comp.ShowActive) { ReadyForNextToil(); return; }
                watched += delta;
                if (guest)
                    pawn.needs?.joy?.GainJoy(job.def.joyGainRate * 0.36f / 2500f * delta, job.def.joyKind); // visitors may lack the joy need: no vanilla joy check for them
                else
                    Toils.JoyTick(pawn, delta, JoyTickFullJoyAction.GoToNextToil, Screen as Building);
            });
            watch.AddFinishAction(() => { if (joined) Comp?.Leave(pawn); });
            yield return watch;

            var done = Toils.Make("FilmOver");
            done.initAction = () => FilmOver();
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }

        bool BuyTicket()
        {
            var comp = Comp; var info = GuestApi.Info(pawn);
            var venue = Venues.At(Screen.Position, Screen.Map);
            float f = venue == null ? 1f : venue.Comp.priceFactor * venue.Type.priceFactor;
            var price = CitySettings.Silver(CitySettings.CinemaTicket) * f;
            if (!GuestApi.ChargeGuest(pawn, price, "cinema", "Cinema ticket: " + (comp.FilmTitle ?? "next film")))
            {
                GuestApi.AddSatisfaction(pawn, -2 * CityPricing.PointsToSatisfaction, "Cinema: could not pay the ticket");
                return false;
            }
            boughtTicket = true;
            CityGameComponent.Instance?.Record("cinema", price);
            CityGameComponent.Instance?.MarkVisit(pawn, "cinema");
            CityPricing.ApplyPriceThought(pawn, info, price, CinemaActivity.Reference, "Cinema");
            return true;
        }

        void FilmOver()
        {
            var comp = Comp; int len = Math.Max(1, comp?.ShowLength ?? 1);
            bool enough = watched >= 0.5f * Math.Min(len, CitySettings.Int(CitySettings.CinemaShowTicks));
            if (!enough) return;
            if (boughtTicket)
            {
                float q = 0.10f + 0.04f * Mathf.Clamp01(CityScan.CountSeats(Screen) / 10f);
                GuestApi.AddSatisfaction(pawn, q, "Watched \"" + (comp?.FilmTitle ?? "a film") + "\"");
            }
            else
            {
                var def = CityDefs.Thought("HH_WatchedMovie");
                if (def != null && pawn.needs?.mood != null) pawn.needs.mood.thoughts.memories.TryGainMemory(def);
                JoyUtility.TryGainRecRoomThought(pawn);
            }
        }
    }

    /// <summary>Colonists watch a film on their own when they want recreation and a powered screen with a seat exists.</summary>
    public class JoyGiver_WatchScreening : JoyGiver_WatchBuilding
    {
        protected override bool CanInteractWith(Pawn pawn, Thing t, bool inBed)
        {
            if (!CityAccess.Level1 || inBed) return false;
            var comp = t.TryGetComp<CompProjectorScreen>();
            if (comp == null || !comp.Powered) return false;
            if (comp.ShowActive && comp.FractionLeft < 0.25f) return false;
            return base.CanInteractWith(pawn, t, inBed);
        }

        protected override Job TryGivePlayJob(Pawn pawn, Thing t)
        {
            var job = base.TryGivePlayJob(pawn, t);
            return job;
        }
    }

    // ===== Movie night (a joinable gathering around a screen) ================================================

    public class GatheringWorker_Screening : GatheringWorker
    {
        Thing foundScreen;

        protected override bool TryFindGatherSpot(Pawn organizer, out IntVec3 spot)
        {
            spot = IntVec3.Invalid; foundScreen = null;
            if (!CityAccess.Level1 || organizer?.Map == null) return false;
            var map = organizer.Map;
            int minSeats = CitySettings.Int(CitySettings.CinemaMinSeats);
            var def = CityDefs.ProjectorScreen; if (def == null) return false;
            Thing best = null; int bestSeats = 0;
            foreach (var t in map.listerThings.ThingsOfDef(def))
            {
                var comp = t.TryGetComp<CompProjectorScreen>();
                if (comp == null || t.Faction != Faction.OfPlayer || !comp.Powered || comp.ShowActive) continue;
                if (!organizer.CanReach(t, PathEndMode.Touch, Danger.None)) continue;
                int seats = CityScan.CountSeats(t);
                if (seats < minSeats || seats <= bestSeats) continue;
                best = t; bestSeats = seats;
            }
            if (best == null) return false;
            // the spot is a watch cell in front of the screen; the whole room is the gathering area
            IntVec3 cell = IntVec3.Invalid;
            foreach (var c in WatchBuildingUtility.CalculateWatchCells(best.def, best.Position, best.Rotation, map))
            {
                if (!c.Standable(map) || c.IsForbidden(organizer)) continue;
                cell = c; break;
            }
            if (!cell.IsValid) return false;
            spot = cell; foundScreen = best;
            return true;
        }

        protected override LordJob CreateLordJob(IntVec3 spot, Pawn organizer) => new LordJob_Joinable_Screening(spot, organizer, def, foundScreen);
    }

    public class LordJob_Joinable_Screening : LordJob_Joinable_Party
    {
        Thing screen;
        public Thing Screen => screen;

        public LordJob_Joinable_Screening() { }

        public LordJob_Joinable_Screening(IntVec3 spot, Pawn organizer, GatheringDef gatheringDef, Thing screen) : base(spot, organizer, gatheringDef)
        {
            this.screen = screen;
            durationTicks = CitySettings.Int(CitySettings.CinemaShowTicks) + 500;
        }

        protected override ThoughtDef AttendeeThought => CityDefs.Thought("HH_ScreeningAttended") ?? ThoughtDefOf.AttendedParty;
        protected override ThoughtDef OrganizerThought => CityDefs.Thought("HH_ScreeningAttended") ?? ThoughtDefOf.AttendedParty;

        protected override LordToil CreateGatheringToil(IntVec3 spot, Pawn organizer, GatheringDef gatheringDef) => new LordToil_Screening(spot, gatheringDef);

        public override string GetReport(Pawn pawn) => "HH.City.LordReport".TranslateOr("watching a film with the colony");

        protected override bool ShouldBeCalledOff()
        {
            if (base.ShouldBeCalledOff()) return true;
            var comp = screen?.TryGetComp<CompProjectorScreen>();
            return screen == null || !screen.Spawned || comp == null || !comp.Powered;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref screen, "screen");
        }
    }

    public class LordToil_Screening : LordToil_Party
    {
        public LordToil_Screening(IntVec3 spot, GatheringDef gatheringDef) : base(spot, gatheringDef, 0f) { }

        public override void Init()
        {
            base.Init();
            var job = lord.LordJob as LordJob_Joinable_Screening;
            var comp = job?.Screen?.TryGetComp<CompProjectorScreen>();
            comp?.StartShow(CitySettings.Int(CitySettings.CinemaShowTicks));
        }
    }

    /// <summary>The duty of a movie-night guest: take a seat in front of the screen and watch.</summary>
    public class JobGiver_ScreeningSeat : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var duty = pawn.mindState?.duty; if (duty == null) return null;
            var spot = duty.focus.Cell;
            var map = pawn.Map;
            foreach (var screen in CityScan.ScreensNear(map, spot, 14f).OrderBy(t => t.Position.DistanceToSquared(spot)))
            {
                var comp = screen.TryGetComp<CompProjectorScreen>();
                if (comp == null || !comp.Powered) continue;
                if (!WatchBuildingUtility.TryFindBestWatchCell(screen, pawn, true, out var cell, out var chair)
                    && !WatchBuildingUtility.TryFindBestWatchCell(screen, pawn, false, out cell, out chair)) continue;
                var job = JobMaker.MakeJob(CityDefs.WatchScreening, screen, cell, chair);
                job.doUntilGatheringEnded = true;
                job.ignoreJoyTimeAssignment = true;
                return job;
            }
            return null;
        }
    }
}
