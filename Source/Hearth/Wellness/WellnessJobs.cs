// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Wellness
{
    /// <summary>Hot tub and sauna: walk to a cell, pay (guests), soak for the session length, gain recreation, get the rewards at the end. A = facility, B = cell.</summary>
    public class JobDriver_UseWellness : JobDriver
    {
        int soaked; bool paid, rewarded;
        Thing Facility => job.GetTarget(TargetIndex.A).Thing;
        CompWellnessFacility Comp => Facility?.TryGetComp<CompWellnessFacility>();
        WellnessKind Kind => Comp?.Kind ?? WellnessKind.HotTub;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref soaked, "soaked");
            Scribe_Values.Look(ref paid, "paid");
            Scribe_Values.Look(ref rewarded, "rewarded");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, Math.Max(1, job.def.joyMaxParticipants), 0, null, errorOnFailed)
               && pawn.Reserve(job.targetB, job, 1, 0, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Modules.On(ModuleId.Wellness));
            this.FailOn(() => { var c = Comp; return c == null || !c.UsableNow(out _, slack: true); });
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            var pay = ToilMaker.MakeToil("Pay");
            pay.defaultCompleteMode = ToilCompleteMode.Instant;
            pay.initAction = () =>
            {
                if (paid) return;
                paid = true;
                if (GuestApi.IsGuest(pawn) && !WellnessBilling.ChargeSession(pawn, Facility, Kind))
                    EndJobWith(JobCondition.Incompletable);
            };
            yield return pay;

            var soak = ToilMaker.MakeToil("Soak");
            soak.defaultCompleteMode = ToilCompleteMode.Never;
            soak.handlingFacing = true;
            Hearth.City.Toils.AddPreTick(soak, delta =>
            {
                soaked += delta;
                Hearth.City.Toils.Comfort(pawn, delta);
                if (Facility != null) pawn.rotationTracker.FaceTarget(Facility);
                WellnessEffects.GainJoy(pawn, delta, Facility, job.def.joyKind);
                if (pawn.needs?.joy != null && !pawn.GetTimeAssignment().allowJoy) { EndJobWith(JobCondition.InterruptForced); return; }
                int need = WellnessSettings.SessionTicks(Kind);
                bool joyFull = pawn.needs?.joy != null && pawn.needs.joy.CurLevel > 0.9999f && soaked > need / 3;
                if (soaked >= need || joyFull)
                {
                    if (!rewarded) { rewarded = true; WellnessEffects.Complete(pawn, Kind, Facility); }
                    ReadyForNextToil();
                }
            });
            soak.WithEffect(() => EffecterDefOf.Clean, TargetIndex.A);
            yield return soak;
        }

        public override object[] TaleParameters() => new object[] { pawn, Facility?.def };
    }

    /// <summary>The client on a massage table. Waits for a masseur, lies down while the massage runs. A = table, B = cell on the table.</summary>
    public class JobDriver_ReceiveMassage : JobDriver
    {
        int waited;
        Thing Table => job.GetTarget(TargetIndex.A).Thing;
        CompWellnessFacility Comp => Table?.TryGetComp<CompWellnessFacility>();

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref waited, "waited"); }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var c = Comp;
            if (c == null) return false;
            if (c.recipient != null && c.recipient != pawn && !c.recipient.Dead && c.recipient.CurJob?.def == WellnessDefOf.HH_ReceiveMassage) return false;
            if (!pawn.Reserve(job.targetA, job, 2, 0, null, errorOnFailed)) return false;
            if (!pawn.Reserve(job.targetB, job, 1, 0, null, errorOnFailed)) return false;
            c.ResetSession();
            c.recipient = pawn;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Modules.On(ModuleId.Wellness));
            globalFinishActions.Add(cond =>
            {
                var c = Comp;
                if (c != null && c.recipient == pawn) c.ResetSession();
                if (pawn.jobs != null && pawn.jobs.posture != PawnPosture.Standing) { pawn.jobs.posture = PawnPosture.Standing; PortraitsCache.SetDirty(pawn); }
            });
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            var lie = ToilMaker.MakeToil("LieOnTable");
            lie.defaultCompleteMode = ToilCompleteMode.Never;
            lie.handlingFacing = true;
            lie.initAction = () =>
            {
                pawn.jobs.posture = PawnPosture.LayingOnGroundFaceUp;
                PortraitsCache.SetDirty(pawn);
                var c = Comp; if (c != null) { c.recipient = pawn; c.recipientReady = true; }
            };
            Hearth.City.Toils.AddPreTick(lie, delta =>
            {
                var c = Comp; if (c == null) { EndJobWith(JobCondition.Incompletable); return; }
                if (c.sessionDenied) { EndJobWith(JobCondition.Incompletable); return; }
                if (c.sessionDone) { EndJobWith(JobCondition.Succeeded); return; }
                if (c.sessionActive)
                {
                    Hearth.City.Toils.Comfort(pawn, delta);
                    WellnessEffects.GainJoy(pawn, delta, Table, job.def.joyKind);
                    return;
                }
                waited += delta;
                if (waited > Math.Max(60, (int)(HSettings.Get(WellnessSettings.MassageWait) * GenDate.TicksPerHour)))
                {
                    if (GuestApi.IsGuest(pawn)) GuestApi.AddSatisfaction(pawn, -0.05f, "Waited for a masseur, nobody came");
                    EndJobWith(JobCondition.Incompletable);
                }
            });
            yield return lie;
        }
    }

    /// <summary>The masseur. A = table, B = client. Pays out XP; the client is charged when the massage starts.</summary>
    public class JobDriver_GiveMassage : JobDriver
    {
        int progress; bool started;
        Thing Table => job.GetTarget(TargetIndex.A).Thing;
        Pawn Client => job.GetTarget(TargetIndex.B).Pawn;
        CompWellnessFacility Comp => Table?.TryGetComp<CompWellnessFacility>();

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref progress, "progress"); Scribe_Values.Look(ref started, "started"); }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var c = Comp;
            if (c == null || (c.masseur != null && c.masseur != pawn && !c.masseur.Dead && c.masseur.CurJob?.def == WellnessDefOf.HH_GiveMassage)) return false;
            if (!pawn.Reserve(job.targetA, job, 2, 0, null, errorOnFailed)) return false;
            c.masseur = pawn;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => !Modules.On(ModuleId.Wellness));
            globalFinishActions.Add(cond =>
            {
                var c = Comp;
                if (c == null) return;
                if (c.masseur == pawn) c.masseur = null;
                if (c.sessionActive && !c.sessionDone) { c.sessionActive = false; c.sessionTicks = 0; } // interrupted: client keeps waiting
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            var work = ToilMaker.MakeToil("Massage");
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.handlingFacing = true;
            work.activeSkill = () => SkillDefOf.Social;
            int waitedForClient = 0;
            Hearth.City.Toils.AddPreTick(work, delta =>
            {
                var c = Comp; var client = Client;
                if (c == null || client == null || c.recipient != client) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker.FaceTarget(client);
                if (!c.recipientReady || !Table.OccupiedRect().Contains(client.Position))
                {
                    waitedForClient += delta;
                    if (waitedForClient > 900) EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!started)
                {
                    started = true;
                    if (GuestApi.IsGuest(client) && !WellnessBilling.ChargeSession(client, Table, WellnessKind.Massage))
                    { c.sessionDenied = true; EndJobWith(JobCondition.Incompletable); return; }
                    c.sessionActive = true; c.sessionTicks = 0;
                }
                progress += delta; c.sessionTicks = progress;
                int need = WellnessSettings.SessionTicks(WellnessKind.Massage);
                float xp = HSettings.Get(WellnessSettings.MassageXp) / need * delta;
                if (pawn.skills != null && xp > 0f)
                {
                    pawn.skills.Learn(SkillDefOf.Social, xp, false);
                    pawn.skills.Learn(SkillDefOf.Medicine, xp * 0.7f, false);
                }
                if (progress >= need)
                {
                    float skill = ((pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5) + (pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 5)) / 40f;
                    float quality = Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(skill)) * Mathf.Clamp(Hearth.Dlc.DlcApi.CareProgressFactor(pawn), 0.5f, 1.5f);   // caregiver x1.15
                    WellnessEffects.Complete(client, WellnessKind.Massage, Table, quality);
                    c.sessionDone = true; c.sessionActive = false;
                    ReadyForNextToil();
                }
            });
            yield return work;
        }
    }

    /// <summary>Colonists with the Serving work type pick up massage requests (client lying on a free table).</summary>
    public class WorkGiver_GiveMassage : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(WellnessDefOf.HH_MassageTable);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
            => !Modules.On(ModuleId.Wellness) || WellnessDefOf.HH_MassageTable == null || !pawn.Map.listerThings.ThingsOfDef(WellnessDefOf.HH_MassageTable).Any();

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var c = t.TryGetComp<CompWellnessFacility>();
            if (c == null || c.recipient == null || !c.recipientReady || c.sessionActive || c.sessionDone) return null;
            if (c.recipient == pawn || c.recipient.Dead) return null;
            if (c.masseur != null && c.masseur != pawn && !c.masseur.Dead && c.masseur.CurJob?.def == WellnessDefOf.HH_GiveMassage) return null;
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 2, 0) || !pawn.CanReserve(c.recipient)) return null;
            return JobMaker.MakeJob(WellnessDefOf.HH_GiveMassage, t, c.recipient);
        }
    }

    public static class WellnessStaff
    {
        /// <summary>Is a colonist other than <paramref name="except"/> able and allowed to give a massage?</summary>
        public static bool MasseurAvailable(Map map, Pawn except)
        {
            if (map == null || WellnessDefOf.HH_Serving == null) return false;
            foreach (var c in map.mapPawns.FreeColonistsSpawned)
            {
                if (c == except || c.Downed || c.Drafted || !c.Awake()) continue;
                if (c.WorkTypeIsDisabled(WellnessDefOf.HH_Serving) || c.workSettings == null || !c.workSettings.WorkIsActive(WellnessDefOf.HH_Serving)) continue;
                if (c.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) return true;
            }
            return false;
        }

        /// <summary>Builds the job that sends <paramref name="pawn"/> to use the facility, or null (no cell, unreachable, busy).</summary>
        public static Job MakeUseJob(Pawn pawn, Thing facility)
        {
            var comp = facility.TryGetComp<CompWellnessFacility>();
            if (comp == null) return null;
            // the facility itself must have room for one more user (a tub holds 2): otherwise the job fails at reservation time and logs an error
            int cap = comp.Kind == WellnessKind.HotTub ? Math.Max(1, WellnessDefOf.HH_UseHotTub?.joyMaxParticipants ?? 2) : 2;
            if (!pawn.CanReserve(facility, cap, 0)) return null;
            switch (comp.Kind)
            {
                case WellnessKind.HotTub:
                    foreach (var cell in facility.OccupiedRect().Cells)
                        if (pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) return JobMaker.MakeJob(WellnessDefOf.HH_UseHotTub, facility, cell);
                    return null;
                case WellnessKind.Sauna:
                    var spot = SaunaUtil.FindSpot(pawn, facility);
                    return spot.HasValue ? JobMaker.MakeJob(WellnessDefOf.HH_UseSauna, facility, spot.Value) : null;
                default:
                    if (comp.recipient != null && !comp.recipient.Dead && comp.recipient.CurJob?.def == WellnessDefOf.HH_ReceiveMassage) return null;
                    if (!MasseurAvailable(facility.Map, pawn)) return null;
                    var cell0 = facility.OccupiedRect().Cells.First();
                    return pawn.CanReserveAndReach(cell0, PathEndMode.OnCell, Danger.None) ? JobMaker.MakeJob(WellnessDefOf.HH_ReceiveMassage, facility, cell0) : null;
            }
        }
    }

    /// <summary>Recreation for colonists (and, with RimWard, patients in a medical bed).</summary>
    public class JoyGiver_UseWellness : JoyGiver_InteractBuilding
    {
        protected override bool CanInteractWith(Pawn pawn, Thing t, bool inBed)
        {
            if (!Modules.On(ModuleId.Wellness)) return false;
            bool rwPatient = RehabBridge.IsRimWardWalkingPatient(pawn);
            if (!rwPatient && (pawn.Faction != Faction.OfPlayer || GuestApi.IsGuest(pawn))) return false;
            if (!base.CanInteractWith(pawn, t, inBed)) return false;
            var c = t.TryGetComp<CompWellnessFacility>();
            if (c == null || !c.AllowsUser(pawn) || !c.UsableNow(out _)) return false;
            if (rwPatient || inBed || HealthAIUtility.ShouldSeekMedicalRest(pawn))
            {
                if (!RehabBridge.MayUse(pawn, c.Kind, out _)) return false;
            }
            return true;
        }

        protected override Job TryGivePlayJob(Pawn pawn, Thing bestGame) => WellnessStaff.MakeUseJob(pawn, bestGame);
        protected override Job TryGivePlayJobWhileInBed(Pawn pawn, Thing bestGame) => WellnessStaff.MakeUseJob(pawn, bestGame);
    }

    /// <summary>A sauna is hot on purpose: no heatstroke and no "too hot" thought while sitting in it (the session is short and capped by the stove).</summary>
    [HarmonyPatch(typeof(HediffGiver_Heat), nameof(HediffGiver_Heat.OnIntervalPassed))]
    static class Patch_NoHeatstrokeInSauna
    {
        static bool Prefix(Pawn pawn)
        {
            var d = pawn?.jobs?.curJob?.def;
            if (d == null || d != WellnessDefOf.HH_UseSauna) return true;
            return !Modules.On(ModuleId.Wellness);
        }
    }

    [HarmonyPatch(typeof(ThoughtWorker_Hot), "CurrentStateInternal")]
    static class Patch_NoHotThoughtInSauna
    {
        static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (!__result.Active) return;
            var d = p?.jobs?.curJob?.def;
            if (d != null && d == WellnessDefOf.HH_UseSauna && Modules.On(ModuleId.Wellness)) __result = ThoughtState.Inactive;
        }
    }

    /// <summary>Guests in a Spa venue use tub, sauna and massage (activity "wellness.spa").</summary>
    public sealed class SpaGuestActivity : IGuestActivity
    {
        public string Id => "wellness.spa";
        public ModuleId Module => ModuleId.Wellness;

        struct Offer { public Venue Venue; public Thing Facility; public WellnessKind Kind; public Money Price; }

        static void Collect(Pawn guest, GuestInfo info, List<Offer> into)
        {
            var map = guest.Map; if (map == null) return;
            foreach (var v in Venues.All(map))
            {
                if (!WellnessBilling.IsSpaVenueType(v.Type) || !v.IsOpenNow) continue;
                foreach (var def in new[] { WellnessDefOf.HH_HotTub, WellnessDefOf.HH_Sauna, WellnessDefOf.HH_MassageTable })
                {
                    if (def == null) continue;
                    foreach (var t in map.listerThings.ThingsOfDef(def))
                    {
                        if (t.GetRoom() != v.Room) continue;
                        var c = t.TryGetComp<CompWellnessFacility>();
                        if (c == null || !c.guestsMayUse || !c.UsableNow(out _)) continue;
                        if (c.Kind == WellnessKind.Massage && !WellnessStaff.MasseurAvailable(map, guest)) continue;
                        var price = WellnessBilling.Price(t, c.Kind);
                        var state = WellnessState.Current;
                        bool pass = c.Kind != WellnessKind.Massage && state != null && state.HasPass(guest, v.Sign.thingIDNumber);
                        if (!pass && info.Budget < price) continue;
                        into.Add(new Offer { Venue = v, Facility = t, Kind = c.Kind, Price = pass ? Money.Zero : price });
                    }
                }
            }
        }

        public float Desire(Pawn guest, GuestInfo info)
        {
            if (guest?.Map == null || info == null) return 0f;
            float weight = HSettings.Get(WellnessSettings.GuestWeight);
            if (weight <= 0f) return 0f;
            var state = WellnessState.Current;
            if (state != null && state.TicksSinceLastSession(guest) < HSettings.Get(WellnessSettings.GuestCooldown) * GenDate.TicksPerHour) return 0f;
            var offers = new List<Offer>(); Collect(guest, info, offers);
            if (offers.Count == 0) return 0f;
            float d = weight * (0.8f + 0.2f * offers.Count);
            if (info.Kind == StayKind.Overnight || info.Kind == StayKind.Traveler) d *= 1.3f;
            return d * (1f + Mathf.Clamp(info.Satisfaction, -1f, 1f) * 0.2f);
        }

        public Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            var offers = new List<Offer>(); Collect(guest, info, offers);
            while (offers.Count > 0)
            {
                var o = offers.RandomElement(); offers.Remove(o);
                var job = WellnessStaff.MakeUseJob(guest, o.Facility);
                if (job != null) return job;
            }
            return null;
        }
    }
}
