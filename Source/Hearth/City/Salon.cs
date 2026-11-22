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
    public enum SalonService { Cut = 0, CutAndDye = 1, CutAndTattoo = 2 }

    /// <summary>
    /// The salon chair: a vanilla styling station for colonists (Ideology gives the full style dialog) and a paid service for guests.
    /// A guest sits down (job HH_SalonSit), a colonist with the serving work type walks up behind (WorkGiver) and styles them.
    /// </summary>
    public class Building_SalonChair : Building_StylingStation
    {
        public Pawn Customer, Stylist;
        public int CustomerSince;
        public bool ServiceStarted, ServiceDone, Declined;
        public bool OpenToGuests = true;

        /// <summary>The customer is really sitting here (a stale reference after a save or a cancelled job does not count).</summary>
        public bool HasCustomer
        {
            get
            {
                if (Customer == null) return false;
                if (Customer.Dead || !Customer.Spawned || Customer.Downed) return false;
                var j = Customer.CurJob;
                return j != null && j.def == CityDefs.SalonSit && j.targetA.Thing == this;
            }
        }
        public bool WaitingForServer => HasCustomer && !ServiceStarted && !ServiceDone && !Declined;

        public bool CanTakeCustomer(Pawn p)
        {
            if (!Spawned || !OpenToGuests || this.IsForbidden(p) || this.IsBurning()) return false;
            if (HasCustomer) return false;
            return p.CanReserve(this);
        }

        public void Seat(Pawn p) { Customer = p; Stylist = null; CustomerSince = Find.TickManager.TicksGame; ServiceStarted = false; ServiceDone = false; Declined = false; }
        public void Release(Pawn p) { if (Customer == p) { Customer = null; Stylist = null; ServiceStarted = false; ServiceDone = false; Declined = false; } }

        public Venue Venue => Spawned ? Venues.At(Position, Map) : null;

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "HH.City.SalonGuests".TranslateOr("Open to guests"),
                defaultDesc = "HH.City.SalonGuestsDesc".TranslateOr("Guests may take this chair for a paid haircut (the room needs a beauty salon sign and someone to serve)."),
                icon = TexCommand.ForbidOff,
                isActive = () => OpenToGuests,
                toggleAction = () => OpenToGuests = !OpenToGuests,
            };
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var o in base.GetFloatMenuOptions(selPawn)) yield return o;
            // Without Ideology the vanilla station offers nothing, so the salon chair brings its own new-look option.
            if (ModLister.IdeologyInstalled || CityDefs.SalonSelf == null) yield break;
            if (!selPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly))
                yield return new FloatMenuOption("CannotUseReason".Translate("NoPath".Translate().CapitalizeFirst()), null);
            else
                yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("HH.City.NewHaircut".TranslateOr("Get a new haircut"),
                    () => selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(CityDefs.SalonSelf, this), JobTag.Misc)), selPawn, this);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            if (HasCustomer)
            {
                string line = ("HH.City.SalonCustomer".TranslateOr("Customer: {0}")).Replace("{0}", Customer.LabelShortCap);
                s = s.NullOrEmpty() ? line : s + "\n" + line;
            }
            return s;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref OpenToGuests, "openToGuests", true);
            // The customer and the stylist are transient: after loading, jobs are re-established or the chair is simply free.
        }
    }

    /// <summary>Picks and applies a new look. Hair always; colour with Biotech or Ideology; tattoos with Ideology.</summary>
    public static class SalonStyling
    {
        public static bool DyeAvailable => ModsConfig.BiotechActive || ModsConfig.IdeologyActive;
        public static bool TattooAvailable => ModsConfig.IdeologyActive;

        public static SalonService ChooseService(Pawn customer, Money budgetLeft, Money baseCut)
        {
            var svc = SalonService.Cut;
            if (TattooAvailable && customer.style != null && Rand.Chance(0.15f)) svc = SalonService.CutAndTattoo;
            else if (DyeAvailable && Rand.Chance(0.30f)) svc = SalonService.CutAndDye;
            while (svc != SalonService.Cut && PriceOf(svc, 1f) > budgetLeft) svc = SalonService.Cut;
            return svc;
        }

        public static Money PriceOf(SalonService svc, float factor)
        {
            var p = CitySettings.Silver(CitySettings.SalonPrice);
            if (svc == SalonService.CutAndDye) p += CitySettings.Silver(CitySettings.SalonDyePrice);
            else if (svc == SalonService.CutAndTattoo) p += CitySettings.Silver(CitySettings.SalonTattooPrice);
            return p * factor;
        }

        /// <summary>Applies the new look. Returns a short description of what changed, or null if nothing visible did.</summary>
        public static string Apply(Pawn p, SalonService svc)
        {
            if (p?.story == null) return null;
            var parts = new List<string>();
            var before = p.story.hairDef;
            for (int i = 0; i < 8; i++)
            {
                var h = PawnStyleItemChooser.RandomHairFor(p);
                if (h != null && h != p.story.hairDef) { p.story.hairDef = h; break; }
            }
            if (p.story.hairDef != before) parts.Add("hair");
            if (svc >= SalonService.CutAndDye && DyeAvailable)
            {
                try { p.story.HairColor = PawnHairColors.RandomHairColor(p, p.story.SkinColor, 20); parts.Add("colour"); }
                catch (Exception e) { HLog.Once("city.dye", LogLevel.Warn, "City", "Hair dye failed: " + e.Message); }
            }
            if (svc == SalonService.CutAndTattoo && TattooAvailable && p.style != null)
            {
                try
                {
                    var t = PawnStyleItemChooser.RandomTattooFor(p, TattooType.Face);
                    if (t != null && t != p.style.FaceTattoo) { p.style.FaceTattoo = t; parts.Add("tattoo"); }
                }
                catch (Exception e) { HLog.Once("city.tattoo", LogLevel.Warn, "City", "Tattoo failed: " + e.Message); }
            }
            p.style?.Notify_StyleItemChanged();
            p.Drawer?.renderer?.SetAllGraphicsDirty();
            if (before != p.story.hairDef && p.Spawned) p.style?.MakeHairFilth();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        public static void GiveFreshLook(Pawn p)
        {
            var def = CityDefs.Thought("HH_FreshLook");
            if (def != null && p.needs?.mood != null) p.needs.mood.thoughts.memories.TryGainMemory(def);
        }
    }

    /// <summary>The guest sits in the chair and waits until a stylist arrives and finishes.</summary>
    public class JobDriver_HHSalonSit : JobDriver
    {
        Building_SalonChair Chair => job.targetA.Thing as Building_SalonChair;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            var sit = Toils.Make("SalonSit");
            sit.initAction = () => { Chair.Seat(pawn); pawn.pather.StopDead(); };
            sit.defaultCompleteMode = ToilCompleteMode.Never;
            sit.handlingFacing = true;
            Toils.OnTick(sit, delta =>
            {
                var chair = Chair; if (chair == null) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker.FaceCell(pawn.Position + chair.Rotation.FacingCell);
                if (chair.ServiceDone || chair.Declined) { ReadyForNextToil(); return; }
                if (!chair.ServiceStarted && Toils.HashTick(pawn, 60, delta) && GuestApi.IsGuest(pawn) && GuestApi.Budget(pawn) < SalonStyling.PriceOf(SalonService.Cut, JobDriver_HHSalonStyle.SalonFactor(chair)))
                {
                    chair.Declined = true; // the purse ran empty while waiting: get up, no hard feelings
                    GuestApi.AddSatisfaction(pawn, -2 * CityPricing.PointsToSatisfaction, "Salon: could not pay");
                    ReadyForNextToil(); return;
                }
                if (!chair.ServiceStarted && Find.TickManager.TicksGame - chair.CustomerSince > CitySettings.Int(CitySettings.SalonWaitTicks))
                {
                    GuestApi.AddSatisfaction(pawn, -3 * CityPricing.PointsToSatisfaction, "Salon: nobody came to cut my hair");
                    HLog.Debug("City", pawn + " gave up waiting in the salon chair");
                    EndJobWith(JobCondition.Incompletable);
                }
            });
            sit.AddFinishAction(() => Chair?.Release(pawn));
            yield return sit;
        }
    }

    /// <summary>The stylist: walks up behind the chair, charges the guest, styles them.</summary>
    public class JobDriver_HHSalonStyle : JobDriver
    {
        Building_SalonChair Chair => job.targetA.Thing as Building_SalonChair;
        bool charged;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Chair.InteractionCell, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Chair == null || (!Chair.WaitingForServer && !Chair.ServiceStarted) || (Chair.ServiceStarted && Chair.Stylist != pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            SalonService svc = SalonService.Cut;
            Money price = Money.Zero;
            var begin = Toils.Make("SalonBegin");
            begin.initAction = () =>
            {
                var chair = Chair; var guest = chair.Customer;
                if (guest == null || !chair.WaitingForServer) { EndJobWith(JobCondition.Incompletable); return; }
                chair.Stylist = pawn; chair.ServiceStarted = true;
                if (GuestApi.IsGuest(guest))
                {
                    float factor = SalonFactor(chair);
                    var budget = GuestApi.Budget(guest);
                    svc = SalonStyling.ChooseService(guest, budget, SalonStyling.PriceOf(SalonService.Cut, factor));
                    price = SalonStyling.PriceOf(svc, factor);
                    if (!GuestApi.ChargeGuest(guest, price, "salon", "Beauty salon: " + svc))
                    {
                        HLog.Debug("City", guest + " could not pay " + price + " at the salon");
                        chair.ServiceStarted = false; chair.Stylist = null; chair.Declined = true;
                        GuestApi.AddSatisfaction(guest, -2 * CityPricing.PointsToSatisfaction, "Salon: could not pay");
                        EndJobWith(JobCondition.Incompletable); return;
                    }
                    charged = true;
                    CityGameComponent.Instance?.Record("salon", price);
                    CityGameComponent.Instance?.MarkVisit(guest, "salon");
                    CityPricing.ApplyPriceThought(guest, GuestApi.Info(guest), price, SalonStyling.PriceOf(svc, 1f), "Salon");
                }
            };
            yield return begin;

            int ticks = ServiceTicks(pawn);
            var work = Toils_General.Wait(ticks, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            var eff = EffecterDefOf.HairCutting;
            if (eff != null) work.WithEffect(eff, TargetIndex.A);
            work.FailOn(() => Chair?.Customer == null || !Chair.HasCustomer);
            yield return work;

            var done = Toils.Make("SalonDone");
            done.initAction = () =>
            {
                var chair = Chair; var guest = chair.Customer;
                if (guest == null) return;
                string changed = SalonStyling.Apply(guest, svc);
                SalonStyling.GiveFreshLook(guest);
                if (charged)
                {
                    // KONZEPT 6.9: "Fresh look" +4 (mood points, 0.02 satisfaction each)
                    GuestApi.AddSatisfaction(guest, 4 * CityPricing.PointsToSatisfaction, "Salon: fresh look" + (changed != null ? " (" + changed + ")" : ""));
                    pawn.skills?.Learn(SkillDefOf.Social, 40f);
                }
                chair.ServiceDone = true;
                HLog.Debug("City", $"{pawn} styled {guest} ({svc}, {price})");
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }

        public static float SalonFactor(Building_SalonChair chair)
        {
            var v = chair.Venue;
            return v == null ? 1f : v.Comp.priceFactor * v.Type.priceFactor;
        }

        public static int ServiceTicks(Pawn stylist)
        {
            int baseTicks = CitySettings.Int(CitySettings.SalonServiceTicks);
            int art = stylist.skills?.GetSkill(SkillDefOf.Artistic)?.Level ?? 6;
            return Mathf.Max(60, Mathf.RoundToInt(baseTicks * Mathf.Lerp(1.35f, 0.7f, Mathf.Clamp01(art / 20f))));
        }
    }

    /// <summary>A colonist gives themselves a new look in the chair (free). Used without Ideology; with it the vanilla dialog is offered too.</summary>
    public class JobDriver_HHSalonSelf : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            var work = Toils_General.WaitWith(TargetIndex.A, 300, true, false, false, TargetIndex.A);
            var eff = EffecterDefOf.HairCutting;
            if (eff != null) work.WithEffect(eff, TargetIndex.A);
            yield return work;
            yield return Toils_General.Do(() =>
            {
                SalonStyling.Apply(pawn, SalonService.Cut);
                SalonStyling.GiveFreshLook(pawn);
            });
        }
    }

    public class WorkGiver_HHSalonStyle : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(CityDefs.SalonChair);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!CityAccess.ModuleOn || CityDefs.SalonChair == null) return true;
            return pawn.Map.listerThings.ThingsOfDef(CityDefs.SalonChair).Count == 0;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var chair = t as Building_SalonChair;
            if (chair == null || !chair.WaitingForServer || chair.IsForbidden(pawn)) return false;
            return pawn.CanReserve(chair.InteractionCell, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(CityDefs.SalonStyle, t);
    }
}
