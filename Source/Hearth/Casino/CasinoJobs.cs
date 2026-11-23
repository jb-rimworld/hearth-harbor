// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    public static class CasinoJobs
    {
        /// <summary>The closest usable device this pawn can reach and sit/stand at, or null.</summary>
        public static CompCasinoGame FindDeviceFor(Pawn pawn, bool guest, bool ignoreJoyRules = false, Func<CompCasinoGame, double> weight = null)
        {
            var map = pawn.Map; if (map == null) return null;
            var c = CasinoComponent.Get(); if (c == null) return null;
            var candidates = new List<(CompCasinoGame comp, double w)>();
            foreach (var comp in CasinoBank.AllDevices(map))
            {
                var t = comp.parent;
                if (!comp.Usable(guest, out _)) continue;
                if (comp.Seated >= comp.Props.seats) continue;
                if (t.IsForbidden(pawn) && !ignoreJoyRules) continue;
                if (guest && comp.NeedsCroupier && !comp.Staffed) continue;
                if (!guest && !t.IsSociallyProper(pawn)) continue;
                var existing = c.SessionsAt(t); if (existing.Count > 0 && existing[0].Kind != (guest ? SessionKind.Guest : SessionKind.Colonist)) continue;
                if (!pawn.CanReserve(t, comp.Props.seats)) continue;
                if (!CasinoSeats.SeatCells(comp).Any(cell => pawn.CanReserveSittableOrSpot(cell) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))) continue;   // F17: the planned guest seats
                double w = weight?.Invoke(comp) ?? 1.0;
                if (w <= 0) continue;
                w /= 1.0 + 0.04 * (pawn.Position - t.Position).LengthManhattan;      // nearer is better
                if (comp.Seated > 0) w *= 1.3;                                       // people like a table that already has players
                candidates.Add((comp, w));
            }
            if (candidates.Count == 0) return null;
            double roll = Rand.Value * candidates.Sum(x => x.w);
            foreach (var cd in candidates) { roll -= cd.w; if (roll <= 0) return cd.comp; }
            return candidates[candidates.Count - 1].comp;
        }

        public static Job MakeJob(Pawn pawn, CompCasinoGame comp)
        {
            IntVec3 cell = IntVec3.Invalid;
            // F17: only the planned seats (the ones the table shows); chairs come first in the plan
            var cells = CasinoSeats.SeatCells(comp).Where(x => pawn.CanReserveSittableOrSpot(x) && pawn.CanReach(x, PathEndMode.OnCell, Danger.Some)).ToList();
            if (cells.Count == 0) return null;
            // prefer a cell with a chair, otherwise the nearest
            foreach (var x in cells)
            {
                var e = x.GetEdifice(pawn.Map);
                if (e != null && e.def.building != null && e.def.building.isSittable) { cell = x; break; }
            }
            if (!cell.IsValid) cell = cells.MinBy(x => (x - pawn.Position).LengthManhattan);
            var job = JobMaker.MakeJob(CasinoDefOf.HH_PlayCasino, comp.parent, cell);
            return job;
        }
    }

    /// <summary>Plays at a machine or table: walks there, sits/stands, plays until the session ends (guests: bankroll rules; colonists: joy or the clock).</summary>
    public class JobDriver_PlayCasino : JobDriver
    {
        Thing Device => job.GetTarget(TargetIndex.A).Thing;
        CompCasinoGame Comp => Device?.TryGetComp<CompCasinoGame>();
        bool failedStart;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            int seats = Comp?.Props.seats ?? 1;
            if (!pawn.Reserve(job.targetA, job, seats, 0, null, errorOnFailed)) return false;
            return pawn.ReserveSittableOrSpot(job.targetB.Cell, job, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Comp == null || !Comp.Usable(IsGuest, out _));
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var play = ToilMaker.MakeToil("PlayCasino");
            play.initAction = StartSession;
            Action<int> tick = delta => PlayTick(delta);
#if RW17
            Hearth.City.Toils.OnTick(play, tick);
#else
            play.tickAction = () => tick(1);
#endif
            play.handlingFacing = true;
            play.defaultCompleteMode = ToilCompleteMode.Delay;
            play.defaultDuration = IsGuest ? 6000 : Math.Max(1000, job.def.joyDuration);
            play.AddFinishAction(() => { var s = CasinoComponent.Get()?.SessionOf(pawn); if (s != null) CasinoEngine.EndSession(s, "left the table"); });
            yield return play;
        }

        bool IsGuest => GuestApi.IsGuest(pawn);

        void StartSession()
        {
            var comp = Comp; if (comp == null) { EndJobWith(JobCondition.Incompletable); return; }
            var s = CasinoEngine.Begin(pawn, comp, IsGuest ? SessionKind.Guest : SessionKind.Colonist, pawn.MentalState?.def == CasinoDefOf.HH_GamblingSpree, out string why);
            if (s == null) { HLog.Debug("Casino", $"{pawn.LabelShort} could not start playing at {Device}: {why}"); failedStart = true; EndJobWith(JobCondition.Incompletable); }
        }

        void PlayTick(int delta)
        {
            var comp = Comp; if (comp == null || failedStart) return;
            pawn.rotationTracker.FaceTarget(Device);
            try { Hearth.City.Toils.Comfort(pawn, delta); } catch (Exception) { }
            var c = CasinoComponent.Get();
            var s = c?.SessionOf(pawn);
            if (s == null)
            {
                // after a reload the session is restored from the save; if it is gone (ended by the engine) the job is over
                if (pawn.IsHashIntervalTick(30)) EndJobWith(JobCondition.Succeeded);
                return;
            }
            if (s.Done) { EndJobWith(JobCondition.Succeeded); return; }
            if (s.Kind == SessionKind.Colonist)
            {
                var joy = pawn.needs?.joy; float before = joy?.CurLevel ?? 0f;
                bool ended = false;
                if (pawn.CurJob != null)
                {
                    ended = Hearth.City.Toils.JoyTick(pawn, delta, JoyTickFullJoyAction.EndJob, CS.F(CS.JoyGain) * Hearth.Dlc.LuckApi.JoyFactor(pawn), Device as Building);
                    // gambling builds tolerance slower than vanilla play (casino.toleranceFactor): hand back the difference
                    if (joy != null)
                    {
                        float gained = joy.CurLevel - before;
                        if (gained > 0f) joy.tolerances.Notify_JoyGained(-gained * (1f - CS.F(CS.ToleranceFactor)), CasinoDefOf.HH_Gambling);
                    }
                }
                if (ended) return;
            }
            else if (pawn.IsHashIntervalTick(60) && !comp.Usable(true, out _)) EndJobWith(JobCondition.Incompletable);
        }
    }

    /// <summary>The croupier: stands at the table so that it runs for guests.</summary>
    public class JobDriver_RunCasinoTable : JobDriver
    {
        Thing Table => job.GetTarget(TargetIndex.A).Thing;
        CompCasinoGame Comp => Table?.TryGetComp<CompCasinoGame>();
        int idle;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var work = ToilMaker.MakeToil("RunTable");
            Action<int> tick = delta =>
            {
                var comp = Comp; if (comp == null) { EndJobWith(JobCondition.Incompletable); return; }
                comp.Notify_CroupierPresent(pawn);
                pawn.rotationTracker.FaceTarget(Table);
                pawn.skills?.Learn(SkillDefOf.Social, 0.02f * delta);
                if (comp.WantsCroupier) idle = 0; else idle += delta;
                if (idle > 600) EndJobWith(JobCondition.Succeeded);
            };
#if RW17
            Hearth.City.Toils.OnTick(work, tick);
#else
            work.tickAction = () => tick(1);
#endif
            work.handlingFacing = true;
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = 20000;   // ends earlier when nobody waits any more (idle > 600 ticks)
            work.activeSkill = () => SkillDefOf.Social;
            yield return work;
        }
    }

    /// <summary>Work giver of the Serving work type: staff a table that guests want to play at.</summary>
    public class WorkGiver_RunCasinoTable : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.OnCell;
        public override bool Prioritized => false;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !Modules.On(ModuleId.Casino) || !CS.B(CS.Croupier);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var map = pawn.Map; if (map == null) yield break;
            foreach (var comp in CasinoBank.AllDevices(map)) if (comp.Props.needsCroupier) yield return comp.parent;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var comp = t.TryGetComp<CompCasinoGame>();
            if (comp == null || !comp.Props.needsCroupier || !comp.WantsCroupier || comp.Staffed) return false;
            if (t.IsForbidden(pawn)) return false;
            var cell = comp.CroupierCell;
            return pawn.CanReserve(cell) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;
            var comp = t.TryGetComp<CompCasinoGame>();
            return JobMaker.MakeJob(CasinoDefOf.HH_RunCasinoTable, t, comp.CroupierCell);
        }
    }

    /// <summary>Joy giver for colonists: gamble with house chips at any machine or table (a Hearth &amp; Harbor "drug").</summary>
    public class JoyGiver_Gambling : JoyGiver_InteractBuilding
    {
        public override float GetChance(Pawn pawn)
        {
            float c = def.baseChance;
            var h = Gambling.AddictionOf(pawn);
            if (h != null && h.Need != null) c *= 1f + 5f * (1f - h.Need.CurLevelPercentage) * h.Stage;   // addicts pick gambling first
            return c;
        }

        public override bool CanBeGivenTo(Pawn pawn)
        {
            if (!Modules.On(ModuleId.Casino) || !base.CanBeGivenTo(pawn)) return false;
            if (!Gambling.IsColonist(pawn) || !Gambling.IsAdult(pawn)) return false;
            if (!Hearth.Dlc.LuckApi.WillGamble(pawn)) return false;      // gambling-averse: never plays
            var h = Gambling.AddictionOf(pawn);
            if (h != null) return Gambling.PolicyAllowsAddiction(pawn);
            return Gambling.PolicyAllowsJoy(pawn, out _);
        }

        protected override bool CanInteractWith(Pawn pawn, Thing t, bool inBed)
        {
            if (!base.CanInteractWith(pawn, t, inBed)) return false;
            var comp = t.TryGetComp<CompCasinoGame>(); if (comp == null) return false;
            if (!comp.Usable(false, out _) || comp.Seated >= comp.Props.seats) return false;
            var existing = CasinoComponent.Get()?.SessionsAt(t);
            if (existing != null && existing.Count > 0 && existing[0].Kind != SessionKind.Colonist) return false;
            return true;
        }

        protected override Job TryGivePlayJob(Pawn pawn, Thing bestGame)
        {
            var comp = bestGame.TryGetComp<CompCasinoGame>(); if (comp == null) return null;
            return CasinoJobs.MakeJob(pawn, comp);
        }
    }
}
