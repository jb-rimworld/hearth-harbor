// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.City
{
    /// <summary>Common frame: module/research gate, per-guest cooldown, interest scale. Desire must stay cheap.</summary>
    public abstract class CityActivity : IGuestActivity
    {
        public abstract string Id { get; }
        public ModuleId Module => ModuleId.CityLife;
        protected abstract string Kind { get; }
        protected abstract int CooldownTicks { get; }
        protected virtual bool Unlocked => CityAccess.Level1;

        protected abstract float DesireCore(Pawn guest, GuestInfo info);
        public abstract Job TryGiveJob(Pawn guest, GuestInfo info);

        public float Desire(Pawn guest, GuestInfo info)
        {
            if (guest?.Map == null || info == null || !Unlocked) return 0f;
            var data = CityGameComponent.Instance;
            if (data != null && data.OnCooldown(guest, Kind, CooldownTicks)) return 0f;
            float scale = CitySettings.F(CitySettings.DesireScale);
            if (scale <= 0f) return 0f;
            return DesireCore(guest, info) * scale;
        }
    }

    // ===== Salon ==============================================================================================

    public sealed class SalonActivity : CityActivity
    {
        public override string Id => "city.salon";
        protected override string Kind => "salon";
        protected override int CooldownTicks => CitySettings.HoursToTicks(CitySettings.SalonCooldownHours);
        public static readonly Money Reference = Money.FromSilver(25);

        protected override float DesireCore(Pawn guest, GuestInfo info)
        {
            float best = 0f;
            foreach (var s in CityScan.Salons(guest.Map))
            {
                if (!CityScan.IsOpenForBusiness(s.Venue) || !s.Chairs.Any(c => c.Spawned && c.OpenToGuests && !c.HasCustomer)) continue;
                var price = SalonStyling.PriceOf(SalonService.Cut, s.Venue.Comp.priceFactor * s.Venue.Type.priceFactor);
                if (GuestApi.Budget(guest) < price) continue;
                double buy = CityPricing.BuyChance(guest, info, price, Reference, "salon");
                float dist = guest.Position.DistanceTo(s.Chairs[0].Position);
                best = Mathf.Max(best, 0.45f * (float)buy / (1f + dist / 40f));
            }
            return best;
        }

        public override Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            if (!CityAccess.Level1 || !CityScan.AnyServer(guest.Map)) return null;
            foreach (var s in CityScan.Salons(guest.Map))
            {
                if (!CityScan.IsOpenForBusiness(s.Venue)) continue;
                var price = SalonStyling.PriceOf(SalonService.Cut, s.Venue.Comp.priceFactor * s.Venue.Type.priceFactor);
                if (GuestApi.Budget(guest) < price) continue;
                foreach (var c in s.Chairs.OrderBy(x => guest.Position.DistanceToSquared(x.Position)))
                {
                    if (!c.CanTakeCustomer(guest) || !guest.CanReach(c, PathEndMode.OnCell, Danger.None)) continue;
                    var job = JobMaker.MakeJob(CityDefs.SalonSit, c);
                    job.locomotionUrgency = LocomotionUrgency.Walk;
                    return job;
                }
            }
            return null;
        }
    }

    // ===== Gallery ============================================================================================

    public sealed class GalleryActivity : CityActivity
    {
        public override string Id => "city.gallery";
        protected override string Kind => "gallery";
        protected override int CooldownTicks => CitySettings.HoursToTicks(CitySettings.GalleryCooldownHours);
        public static readonly Money Reference = Money.FromSilver(10);

        public static Money Fee(GallerySpot g) => CitySettings.Silver(CitySettings.GalleryFee) * (g.Venue.Comp.priceFactor * g.Venue.Type.priceFactor);
        public static bool Usable(GallerySpot g) => g.Art.Count >= CitySettings.Int(CitySettings.GalleryMinArt) && CityScan.IsOpenForBusiness(g.Venue);
        /// <summary>Quality perception 0.7 to 1.5 from the average beauty of the pieces (vanilla sculptures: about 100 when good, 250 as masterworks).</summary>
        public static double Quality(GallerySpot g) => 0.7 + Math.Min(0.8, g.AverageBeauty / 150.0);

        protected override float DesireCore(Pawn guest, GuestInfo info)
        {
            float best = 0f;
            foreach (var g in CityScan.Galleries(guest.Map))
            {
                if (!Usable(g)) continue;
                var fee = Fee(g);
                if (GuestApi.Budget(guest) < fee) continue;
                double buy = CityPricing.BuyChance(guest, info, fee, Reference, "gallery", Quality(g));
                float dist = guest.Position.DistanceTo(g.Venue.Sign.Position);
                best = Mathf.Max(best, 0.55f * (float)buy / (1f + dist / 40f));
            }
            return best;
        }

        public override Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            if (!CityAccess.Level1) return null;
            foreach (var g in CityScan.Galleries(guest.Map).OrderBy(x => guest.Position.DistanceToSquared(x.Venue.Sign.Position)))
            {
                if (!Usable(g) || GuestApi.Budget(guest) < Fee(g)) continue;
                int max = CitySettings.Int(CitySettings.GalleryMaxViews);
                // the best pieces first, but with some variety between visits
                var pieces = g.Art.Where(a => a.Spawned && !a.IsForbidden(guest) && guest.CanReach(a, PathEndMode.Touch, Danger.None))
                    .OrderByDescending(a => a.GetStatValue(StatDefOf.Beauty) * Rand.Range(0.6f, 1f)).Take(max).ToList();
                if (pieces.Count == 0) continue;
                var job = JobMaker.MakeJob(CityDefs.GalleryVisit, g.Venue.Sign);
                job.targetQueueB = new List<LocalTargetInfo>(pieces.Select(p => new LocalTargetInfo(p)));
                job.locomotionUrgency = LocomotionUrgency.Walk;
                return job;
            }
            return null;
        }
    }

    /// <summary>Walk through a gallery: pay at the first piece, stand in front of each in turn, leave a little happier.</summary>
    public class JobDriver_HHGalleryVisit : JobDriver
    {
        float beautySum; int viewed; bool paid;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.B);
            Toil goTo = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch).FailOnDespawnedOrNull(TargetIndex.B);

            Toil start = Toils.Make("Start");
            start.initAction = () => { if (job.targetQueueB == null || job.targetQueueB.Count == 0) EndJobWith(JobCondition.Incompletable); };
            yield return start;
            yield return extract;
            yield return goTo;

            Toil look = Toils.Make("Look");
            look.initAction = () =>
            {
                if (!paid) { if (!Pay()) { EndJobWith(JobCondition.Incompletable); return; } paid = true; }
            };
            yield return look;

            Toil view = Toils_General.Wait(CitySettings.Int(CitySettings.GalleryViewTicks), TargetIndex.B).WithProgressBarToilDelay(TargetIndex.B);
            view.FailOnDespawnedOrNull(TargetIndex.B);
            view.AddFinishAction(() =>
            {
                var art = job.GetTarget(TargetIndex.B).Thing;
                if (art == null) return;
                beautySum += Mathf.Max(0f, art.GetStatValue(StatDefOf.Beauty)); viewed++;
            });
            yield return view;
            yield return Toils_Jump.JumpIfHaveTargetInQueue(TargetIndex.B, extract);

            Toil leave = Toils.Make("Leave");
            leave.initAction = () => Finish();
            yield return leave;
        }

        GallerySpot Spot()
        {
            var sign = job.GetTarget(TargetIndex.A).Thing;
            return sign == null ? null : CityScan.Galleries(pawn.Map).FirstOrDefault(g => g.Venue.Sign == sign);
        }

        bool Pay()
        {
            var g = Spot(); var info = GuestApi.Info(pawn);
            if (g == null) return false;
            var fee = GalleryActivity.Fee(g);
            if (!GuestApi.ChargeGuest(pawn, fee, "gallery", "Gallery entry"))
            {
                GuestApi.AddSatisfaction(pawn, -2 * CityPricing.PointsToSatisfaction, "Gallery: could not pay the entry fee");
                return false;
            }
            CityGameComponent.Instance?.Record("gallery", fee);
            CityGameComponent.Instance?.MarkVisit(pawn, "gallery");
            CityPricing.ApplyPriceThought(pawn, info, fee, GalleryActivity.Reference, "Gallery", GalleryActivity.Quality(g));
            return true;
        }

        void Finish()
        {
            if (!paid || viewed == 0) return;
            float avg = beautySum / viewed;
            // vanilla beauty of the pieces is what counts: a plain statue gives little, a masterwork a lot
            float gain = Mathf.Clamp(0.04f + 0.14f * Mathf.Clamp01(avg / 250f) + 0.01f * Mathf.Min(viewed, 5), 0.04f, 0.25f);
            GuestApi.AddSatisfaction(pawn, gain, $"Gallery: admired {viewed} piece(s)");
            var def = CityDefs.Thought("HH_AdmiredArt");
            if (def != null && pawn.needs?.mood != null) pawn.needs.mood.thoughts.memories.TryGainMemory(def);
            CityGameComponent.Instance?.Record("galleryViews", Money.Zero);
            HLog.Debug("City", $"{pawn} walked through the gallery: {viewed} pieces, avg beauty {avg:0.0}, satisfaction +{gain:0.00}");
        }
    }

    // ===== Park bench / viewpoint ==============================================================================

    public sealed class ParkActivity : CityActivity
    {
        public override string Id => "city.park";
        protected override string Kind => "park";
        protected override int CooldownTicks => CitySettings.HoursToTicks(CitySettings.ParkCooldownHours);
        protected override bool Unlocked => CityAccess.ModuleOn; // park is free (KONZEPT 7.1): benches need no research

        protected override float DesireCore(Pawn guest, GuestInfo info)
        {
            var def = CityDefs.ParkBench; if (def == null) return 0f;
            var list = guest.Map.listerThings.ThingsOfDef(def);
            if (list.Count == 0) return 0f;
            float best = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b.Faction != Faction.OfPlayer || b.IsForbidden(guest)) continue;
                best = Mathf.Max(best, 0.35f / (1f + guest.Position.DistanceTo(b.Position) / 40f));
            }
            return best;
        }

        public override Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            var def = CityDefs.ParkBench; if (def == null) return null;
            foreach (var b in guest.Map.listerThings.ThingsOfDef(def).OrderBy(x => guest.Position.DistanceToSquared(x.Position)))
            {
                if (b.Faction != Faction.OfPlayer || b.IsForbidden(guest)) continue;
                var cell = ParkUtility.FreeSeat(b, guest);
                if (!cell.IsValid || !guest.CanReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                var job = JobMaker.MakeJob(CityDefs.ParkRest, b, cell);
                job.locomotionUrgency = LocomotionUrgency.Walk;
                job.ignoreJoyTimeAssignment = true;
                return job;
            }
            return null;
        }
    }

    public static class ParkUtility
    {
        public static IntVec3 FreeSeat(Thing bench, Pawn p)
        {
            foreach (var c in bench.OccupiedRect())
                if (c.InBounds(bench.Map) && p.CanReserveSittableOrSpot(c)) return c;
            return IntVec3.Invalid;
        }

        /// <summary>0 to 1: how nice the view from a bench is (vanilla beauty around it, a bonus for open sky).</summary>
        public static float Scenery(Thing bench)
        {
            var map = bench.Map; if (map == null) return 0f;
            float beauty = BeautyUtility.AverageBeautyPerceptible(bench.Position, map);
            float s = Mathf.Clamp01((beauty + 1f) / 8f);
            if (!bench.Position.Roofed(map)) s = Mathf.Min(1f, s + 0.15f);
            return s;
        }
    }

    /// <summary>Sit on a bench for a while. Guests: free, a little satisfaction (more with a nice view). Colonists: recreation.</summary>
    public class JobDriver_HHParkRest : JobDriver
    {
        bool guest;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, Math.Max(1, job.def.joyMaxParticipants), 0, null, errorOnFailed)) return false;
            return pawn.ReserveSittableOrSpot(job.targetB.Cell, job, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.EndOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            guest = pawn.Faction != Faction.OfPlayer && GuestApi.IsGuest(pawn);
            var rest = Toils.Make("Rest");
            rest.defaultCompleteMode = ToilCompleteMode.Delay;
            rest.defaultDuration = CitySettings.Int(CitySettings.ParkRestTicks);
            rest.handlingFacing = false;
            Toils.OnTick(rest, delta =>
            {
                Toils.Comfort(pawn, delta);
                if (!guest) Toils.JoyTick(pawn, delta, JoyTickFullJoyAction.EndJob, TargetThingA as Building);
            });
            rest.AddFinishAction(() =>
            {
                if (guest)
                {
                    float scenery = ParkUtility.Scenery(TargetThingA);
                    GuestApi.AddSatisfaction(pawn, 0.03f + 0.05f * scenery, "Park bench: a quiet sit");
                    CityGameComponent.Instance?.MarkVisit(pawn, "park");
                    CityGameComponent.Instance?.Record("park", Money.Zero);
                }
                var def = CityDefs.Thought("HH_ParkRested");
                if (def != null && pawn.needs?.mood != null) pawn.needs.mood.thoughts.memories.TryGainMemory(def);
            });
            yield return rest;
        }
    }

    /// <summary>Colonists sit on benches in the open when they want recreation (the tame cousin of the telescope).</summary>
    public class JoyGiver_ParkBench : JoyGiver_InteractBuilding
    {
        protected override Job TryGivePlayJob(Pawn pawn, Thing t)
        {
            var cell = ParkUtility.FreeSeat(t, pawn);
            if (!cell.IsValid) return null;
            return JobMaker.MakeJob(def.jobDef, t, cell);
        }
    }
}
