// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    // ======================================================================= think nodes of the HH_GuestStay duty

    /// <summary>Go to the front desk and check in; without a desk, check in on the spot (self check-in).</summary>
    public class JobGiver_HH_CheckIn : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var reg = GuestRegistry.Instance; var r = reg?.Get(pawn);
            if (r == null || r.CheckedIn || r.Settled) return null;
            var desk = reg.FindDesk(pawn.Map);
            if (desk == null || r.CheckInFailures >= 2 || !pawn.CanReach(desk.InteractionCell, PathEndMode.OnCell, Danger.Some))
            {
                // self check-in: walk to the guest rooms first, then take a bed (5.4)
                var target = GuestArrival.TargetSpot(pawn.Map);
                if (r.CheckInFailures < 4 && target.IsValid && !pawn.Position.InHorDistOf(target, 9f) && pawn.CanReach(target, PathEndMode.Touch, Danger.Some))
                {
                    var go = JobMaker.MakeJob(JobDefOf.Goto, target);
                    go.locomotionUrgency = LocomotionUrgency.Walk;
                    go.expiryInterval = 4000;
                    go.checkOverrideOnExpire = true;
                    r.CheckInFailures++;
                    return go;
                }
                reg.CheckIn(pawn, null);
                return null;
            }
            var job = JobMaker.MakeJob(InnDefOf.HH_CheckIn, desk);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 5000;
            return job;
        }
    }

    /// <summary>Sleep in the booked guest bed at night (or when exhausted).</summary>
    public class JobGiver_HH_GuestSleep : ThinkNode_JobGiver
    {
        public override float GetPriority(Pawn pawn) => 8f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            var r = GuestRegistry.Instance?.Get(pawn);
            if (r == null || !r.CheckedIn || r.Settled || !r.Overnight) return null;
            var rest = pawn.needs?.rest; if (rest == null) return null;
            if (r.Bed == null || r.Bed.Destroyed || !r.Bed.Spawned) { r.Bed = null; GuestRegistry.Instance.TryReassignBed(r); if (r.Bed == null) return null; }
            float hour = GenLocalDate.HourFloat(pawn.Map);
            bool night = hour >= 22f || hour < 6.5f;
            if (!(night && rest.CurLevel < 0.95f) && rest.CurLevel > 0.22f) return null;
            if (!RestUtility.CanUseBedNow(r.Bed, pawn, checkSocialProperness: false)) return null;
            if (!pawn.CanReserveAndReach(r.Bed, PathEndMode.OnCell, Danger.Some, r.Bed.SleepingSlotsCount, 0)) return null;
            return JobMaker.MakeJob(JobDefOf.LayDown, r.Bed);
        }
    }

    /// <summary>Guests eat what they brought (the colony's freezer is not theirs; venues sell food through activities). Only an emergency:
    /// hungry guests first look for a restaurant or tavern (guest activities), see Inn_AI.xml.</summary>
    public class JobGiver_HH_EatPacked : ThinkNode_JobGiver
    {
        /// <summary>Hunger below which the packed food comes out (B8): 0.15 in the needs block (emergency), 0.3 after the venues had their turn.</summary>
        public float threshold = 0.15f;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            var c = (JobGiver_HH_EatPacked)base.DeepCopy(resolve);
            c.threshold = threshold;
            return c;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            var food = pawn.needs?.food;
            if (food == null || food.CurLevelPercentage > threshold) return null;
            var t = FoodUtility.BestFoodInInventory(pawn, pawn, FoodPreferability.DesperateOnly, FoodPreferability.MealLavish);
            if (t == null) return null;
            float n = FoodUtility.GetNutrition(pawn, t, t.def);
            var job = JobMaker.MakeJob(JobDefOf.Ingest, t);
            job.count = Math.Max(1, FoodUtility.WillIngestStackCountOf(pawn, t.def, n));
            return job;
        }
    }

    /// <summary>Asks the activities registered by all modules (shops, food, casino …) via GuestActivities.TryPick.</summary>
    public class JobGiver_HH_GuestActivity : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var r = GuestRegistry.Instance?.Get(pawn);
            if (r == null || r.Settled || (!r.CheckedIn && r.Overnight)) return null;
            if (GuestActivities.All.Count == 0) return null;
            int now = Find.TickManager.TicksGame;
            if (now - r.LastActivityTick < 600) return null;
            r.LastActivityTick = now;
            var job = GuestActivities.TryPick(pawn, r);
            // some job defs have no label ("Went to ."): name the target instead, or say nothing
            string what = job == null ? null : !job.def.label.NullOrEmpty() ? job.def.label : job.targetA.Thing?.LabelNoCount;
            if (!what.NullOrEmpty()) r.Log("HH.Inn.Event.Activity".TranslateOr("Went to {0}.").Replace("{0}", what), 2);
            return job;
        }
    }

    /// <summary>Vanilla joy givers that only use buildings (TV, chess, telescope …). Nothing that eats the colony's beer.</summary>
    public class JobGiver_HH_GuestJoy : JobGiver_GetJoy
    {
        static readonly HashSet<string> allowedWorkers = new HashSet<string>
        {
            "JoyGiver_InteractBuilding", "JoyGiver_InteractBuildingInteractionCell", "JoyGiver_InteractBuildingSitAdjacent",
            "JoyGiver_WatchBuilding", "JoyGiver_Skygaze", "JoyGiver_ViewArt", "JoyGiver_VisitGrave", "JoyGiver_Meditate",
        };

        protected override bool JoyGiverAllowed(JoyGiverDef def)
        {
            if (def?.giverClass == null) return false;
            for (var t = def.giverClass; t != null && t != typeof(object); t = t.BaseType)
                if (allowedWorkers.Contains(t.Name)) return true;
            return false;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.needs?.joy == null) return null;
            var r = GuestRegistry.Instance?.Get(pawn);
            if (r == null || r.Settled || (!r.CheckedIn && r.Overnight)) return null;
            if (pawn.needs.joy.CurLevel > 0.7f) return null;
            Job j = null;
            try { j = base.TryGiveJob(pawn); }
            catch (Exception e) { HLog.Once("inn.joy." + e.GetType().Name, LogLevel.Warn, "Inn", "Guest joy giver failed: " + e.Message); }
            return j;
        }
    }

    /// <summary>Wander inside the guest wing (or near the duty spot when there is no wing).</summary>
    public class JobGiver_HH_WanderWing : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var map = pawn.Map; if (map == null) return null;
            var r = GuestRegistry.Instance?.Get(pawn);
            bool wait = pawn.mindState.nextMoveOrderIsWait;
            pawn.mindState.nextMoveOrderIsWait = !wait;
            if (wait)
            {
                var w = JobMaker.MakeJob(JobDefOf.Wait_Wander);
                w.expiryInterval = Rand.Range(300, 900);
                return w;
            }
            IntVec3 dest = IntVec3.Invalid;
            var wing = r != null && r.CheckedIn ? GuestWings.WingFor(pawn) : null;
            if (wing != null) dest = wing.RandomCellFor(pawn);
            if (!dest.IsValid)
            {
                IntVec3 root = pawn.mindState.duty?.focus.Cell ?? pawn.Position;
                if (r?.Bed != null && r.Bed.Spawned) root = r.Bed.Position;
                if (!root.IsValid) root = pawn.Position;
                RCellFinder.TryFindRandomCellNearWith(root, c => c.Standable(map) && !c.IsForbidden(pawn) && pawn.CanReach(c, PathEndMode.OnCell, Danger.None), map, out dest, 5, 12);
            }
            if (!dest.IsValid || dest == pawn.Position)
            {
                var w = JobMaker.MakeJob(JobDefOf.Wait_Wander);
                w.expiryInterval = Rand.Range(300, 600);
                return w;
            }
            var job = JobMaker.MakeJob(JobDefOf.GotoWander, dest);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 2500;
            return job;
        }
    }

    // ======================================================================= job drivers

    /// <summary>Guest walks to the front desk, waits a moment (shorter if a host stands by) and checks in.</summary>
    public class JobDriver_HH_CheckIn : JobDriver
    {
        Building_FrontDesk Desk => job.GetTarget(TargetIndex.A).Thing as Building_FrontDesk;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            AddFinishAction(cond =>
            {
                if (cond != JobCondition.Succeeded)
                {
                    var r = GuestRegistry.Instance?.Get(pawn);
                    if (r != null && !r.CheckedIn) r.CheckInFailures++;
                }
            });
            yield return Toils_Goto.GotoCell(Desk.InteractionCell, PathEndMode.OnCell);
            var wait = ToilMaker.MakeToil("HH_CheckInWait");
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.defaultDuration = 150;
            wait.initAction = () => { if (Desk.HostNearby()) wait.actor.jobs.curDriver.ticksLeftThisToil = 60; };
            Hearth.City.Toils.OnTick(wait, delta => pawn.rotationTracker.FaceTarget(Desk));
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            var done = ToilMaker.MakeToil("HH_CheckInDone");
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            done.initAction = () => GuestRegistry.Instance?.CheckIn(pawn, Desk);
            yield return done;
        }
    }

    /// <summary>A Hosting colonist keeps a guest company (Accompany) or talks about staying (Recruit), 5.10.</summary>
    public class JobDriver_HH_AccompanyGuest : JobDriver
    {
        Pawn Guest => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Guest, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !GuestRegistry.Instance?.IsGuest(Guest) ?? true);
            this.FailOn(() => Guest.InBed() || !Guest.Awake());
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Interpersonal.WaitToBeAbleToInteract(pawn);
            var talk = ToilMaker.MakeToil("HH_Accompany");
            Hearth.City.Toils.OnTick(talk, delta =>
            {
                pawn.rotationTracker.FaceCell(Guest.Position);
                if (Hearth.City.Toils.HashTick(pawn, 300, delta) && pawn.interactions.CanInteractNowWith(Guest))
                    pawn.interactions.TryInteractWith(Guest, Rand.Value < 0.75f ? InteractionDefOf.Chitchat : InteractionDefOf.DeepTalk);
                if (!pawn.Position.InHorDistOf(Guest.Position, 3f)) ReadyForNextToil();
            });
            talk.handlingFacing = true;
            talk.socialMode = RandomSocialMode.Off;
            talk.defaultCompleteMode = ToilCompleteMode.Delay;
            talk.defaultDuration = 700;
            talk.AddFinishAction(() => { if (ticksLeftThisToil <= 1) GuestRegistry.Instance?.Notify_Talk(pawn, Guest); });
            yield return talk;
        }
    }

    /// <summary>Hosting work: keep guests with handling Accompany/Recruit company, at most every few hours per guest.</summary>
    public class WorkGiver_HH_AccompanyGuest : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var reg = GuestRegistry.Instance;
            if (reg == null || !Modules.On(ModuleId.Inn)) return Enumerable.Empty<Thing>();
            return reg.GuestsOn(pawn.Map).Where(p => reg.Get(p)?.Handling != HandlingMode.Basics).Cast<Thing>();
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            var reg = GuestRegistry.Instance;
            if (reg == null || !Modules.On(ModuleId.Inn) || reg.Records.Count == 0) return true;
            return !VersionCompat.CanInitiateInteraction(pawn);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn g)) return false;
            var r = GuestRegistry.Instance?.Get(g);
            if (r == null || r.Settled || r.Handling == HandlingMode.Basics || !r.CheckedIn) return false;
            int cooldown = r.Handling == HandlingMode.Recruit || r.Handling == HandlingMode.Convert ? 5000 : 7500;
            if (!forced && Find.TickManager.TicksGame - r.LastAccompanyTick < cooldown) return false;
            if (g.InBed() || !g.Awake() || g.Downed || g.InMentalState) return false;
            return pawn.CanReserveAndReach(g, PathEndMode.Touch, Danger.None);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(InnDefOf.HH_AccompanyGuest, t);
    }

    /// <summary>
    /// C7: Hosting work: stand behind the front desk while guests are arriving during desk hours. A host on duty welcomes every guest
    /// in person (service bonus, social XP) and keeps the desk from turning into an honesty box. Work Tab sees it like any work giver.
    /// </summary>
    public class WorkGiver_HH_StaffFrontDesk : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.OnCell;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(InnDefOf.HH_FrontDesk);

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            var reg = GuestRegistry.Instance;
            if (reg == null || !Modules.On(ModuleId.Inn) || reg.Records.Count == 0 || pawn.Map == null) return true;
            return !forced && !GuestThoughts.DeskOpenNow(pawn.Map);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_FrontDesk desk) || !desk.Spawned || desk.Faction != Faction.OfPlayer) return false;
            if (t.IsForbidden(pawn) || !desk.GuestsArriving()) return false;
            var host = desk.Host();
            if (host != null && host != pawn) return false;   // somebody already stands there
            return desk.StaffCell(pawn).IsValid;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var desk = (Building_FrontDesk)t;
            var cell = desk.StaffCell(pawn);
            if (!cell.IsValid) return null;
            var job = JobMaker.MakeJob(InnDefOf.HH_StaffFrontDesk, desk, cell);
            job.expiryInterval = 2500;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    /// <summary>Walk behind the desk and stay there while guests are still arriving (at most an hour per job).</summary>
    public class JobDriver_HH_StaffFrontDesk : JobDriver
    {
        Building_FrontDesk Desk => job.GetTarget(TargetIndex.A).Thing as Building_FrontDesk;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var stand = ToilMaker.MakeToil("HH_StaffDesk");
            stand.defaultCompleteMode = ToilCompleteMode.Delay;
            stand.defaultDuration = 2500;
            stand.handlingFacing = true;
            Hearth.City.Toils.OnTick(stand, delta =>
            {
                pawn.rotationTracker.FaceTarget(Desk);
                if (Hearth.City.Toils.HashTick(pawn, 250, delta) && (Desk == null || !Desk.GuestsArriving())) ReadyForNextToil();
            });
            stand.socialMode = RandomSocialMode.Normal;
            yield return stand;
        }
    }

    // ======================================================================= the front desk

    /// <summary>Front desk (6.2): where guests check in; also the till spot where the nightly takings are put down.</summary>
    public class Building_FrontDesk : Building
    {
        int checkInsToday, checkInDay = -1, checkInsTotal;

        public void Notify_CheckIn(Pawn p)
        {
            int day = GenLocalDate.DayOfYear(Map);
            if (day != checkInDay) { checkInDay = day; checkInsToday = 0; }
            checkInsToday++; checkInsTotal++;
        }

        static WorkTypeDef hostingWork;
        static WorkTypeDef HostingWork => hostingWork ?? (hostingWork = DefDatabase<WorkTypeDef>.GetNamedSilentFail("HH_Hosting"));

        public bool HostNearby() => Host() != null;

        /// <summary>A colonist with Hosting work standing next to the desk (the one staffing it, if any).</summary>
        public Pawn Host()
        {
            if (!Spawned) return null;
            var wt = HostingWork; if (wt == null) return null;
            foreach (var c in GenAdj.CellsAdjacent8Way(this))
            {
                if (!c.InBounds(Map)) continue;
                var list = c.GetThingList(Map);
                for (int i = 0; i < list.Count; i++)
                    if (list[i] is Pawn p && p.IsColonist && !p.Downed && p.workSettings != null && p.workSettings.WorkIsActive(wt)) return p;
            }
            return null;
        }

        /// <summary>Where a host stands while staffing the desk: behind it (opposite the guests' interaction cell), else any free adjacent cell.</summary>
        public IntVec3 StaffCell(Pawn forPawn)
        {
            if (!Spawned) return IntVec3.Invalid;
            var behind = Position + IntVec3.North.RotatedBy(Rotation);
            if (Ok(behind, forPawn)) return behind;
            foreach (var c in GenAdj.CellsAdjacent8Way(this)) if (c != InteractionCell && Ok(c, forPawn)) return c;
            return IntVec3.Invalid;
        }

        bool Ok(IntVec3 c, Pawn p) => c.InBounds(Map) && c.Standable(Map) && c != InteractionCell && (p == null || (p.CanReserve(c) && p.CanReach(c, PathEndMode.OnCell, Danger.Some)));

        /// <summary>Is someone waiting to check in here right now (arriving overnight guests, not yet checked in)?</summary>
        public bool GuestsArriving()
        {
            var reg = GuestRegistry.Instance; if (reg == null || !Spawned) return false;
            foreach (var r in reg.OnMap(Map))
                if (!r.CheckedIn && !r.Settled && r.Pawn != null && r.Pawn.Spawned && r.Pawn.Map == Map) return true;
            return false;
        }

        public override string GetInspectString()
        {
            var s = base.GetInspectString();
            if (!Modules.On(ModuleId.Inn)) return s;
            int day = Spawned ? GenLocalDate.DayOfYear(Map) : -2;
            string line = "HH.Inn.Desk.Inspect".TranslateOr("Check-ins today: {0} · all time: {1}")
                .Replace("{0}", (day == checkInDay ? checkInsToday : 0).ToString()).Replace("{1}", checkInsTotal.ToString());
            int beds = Spawned ? GuestBeds.CountOn(Map) : 0;
            if (beds == 0) line += "\n" + "HH.Inn.Desk.NoBeds".TranslateOr("No guest beds yet: mark a bed as guest bed to receive overnight guests.");
            else
            {
                var sch = Map.GetComponent<MapComponent_HH_GuestScheduler>();
                var next = sch?.NextArrival();
                if (next != null) line += "\n" + "HH.Inn.Desk.Next".TranslateOr("Next guests: {0}").Replace("{0}", next.Describe(Map));
                foreach (var w in WingStatus.Lines(Map)) line += "\n" + w;
            }
            var door = DoorPolicyHub.Current;
            if (door != null && !door.IsDefault) line += "\n" + "HH.Inn.Desk.Door".TranslateOr("Door policy: {0}").Replace("{0}", door.AllowAll ? "HH.Inn.Door.Everyone".TranslateOr("Everyone") : string.Join(", ", door.Restricting()));
            return s.NullOrEmpty() ? line : s + "\n" + line;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            if (!Modules.On(ModuleId.Inn) || Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HH.Inn.Door.Gizmo".TranslateOr("Door policy"),
                defaultDesc = "HH.Inn.Door.GizmoDesc".TranslateOr("Choose who may stay: factions, xenotypes, gender, age, royal titles, tech level, wealth and kind of stay. Filters apply when guests are planned and generated."),
                icon = InnTex.DoorPolicy,
                action = DoorPolicyHub.Open,
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref checkInsToday, "hhCheckInsToday");
            Scribe_Values.Look(ref checkInDay, "hhCheckInDay", -1);
            Scribe_Values.Look(ref checkInsTotal, "hhCheckInsTotal");
        }
    }
}
