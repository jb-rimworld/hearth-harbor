// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Guests
{
    /// <summary>
    /// Lord of one guest group (5.1): stay (check-in → needs → activities → wander in the wing) → leave.
    /// Side states: take the wounded home, leave because of dangerous temperatures, leave defending themselves when hostile.
    /// The register decides when the planned stay is over and sends the memo <see cref="MemoLeave"/>.
    /// </summary>
    public class LordJob_HH_GuestStay : LordJob
    {
        public const string MemoLeave = "HH_Leave";
        public Faction faction;
        public IntVec3 spot;
        public int groupId = -1;

        public LordJob_HH_GuestStay() { }
        public LordJob_HH_GuestStay(Faction faction, IntVec3 spot, int groupId) { this.faction = faction; this.spot = spot; this.groupId = groupId; }

        public override bool AddFleeToil => true;

        public override StateGraph CreateGraph()
        {
            var g = new StateGraph();
            var stay = new LordToil_HH_Stay(spot);
            g.StartingToil = stay;
            var leave = new LordToil_ExitMap(LocomotionUrgency.Walk, canDig: false, interruptCurrentJob: true);
            g.AddToil(leave);
            var wounded = new LordToil_TakeWoundedGuest();
            g.AddToil(wounded);
            var hostile = new LordToil_ExitMapAndDefendSelf();
            g.AddToil(hostile);

            var tLeave = new Transition(stay, leave);
            tLeave.AddTrigger(new Trigger_Memo(MemoLeave));
            tLeave.AddPostAction(new TransitionAction_WakeAll());
            tLeave.AddPostAction(new TransitionAction_EndAllJobs());
            g.AddTransition(tLeave);

            var tTemp = new Transition(stay, leave);
            tTemp.AddTrigger(new Trigger_PawnExperiencingDangerousTemperatures());
            tTemp.AddPreAction(new TransitionAction_HH_Settle { reason = "temperature" });
            if (faction != null) tTemp.AddPreAction(new TransitionAction_Message("HH.Inn.Msg.LeavingTemperature".TranslateOr("Your guests from {0} are leaving: it is dangerously hot or cold.").Replace("{0}", faction.Name)));
            tTemp.AddPostAction(new TransitionAction_EndAllJobs());
            g.AddTransition(tTemp);

            var tWounded = new Transition(stay, wounded);
            tWounded.AddTrigger(new Trigger_WoundedGuestPresent());
            tWounded.AddPreAction(new TransitionAction_HH_Settle { reason = "wounded" });
            if (faction != null) tWounded.AddPreAction(new TransitionAction_Message("HH.Inn.Msg.LeavingWounded".TranslateOr("Your guests from {0} are taking their wounded home.").Replace("{0}", faction.Name)));
            g.AddTransition(tWounded);

            var tHostile = new Transition(stay, hostile);
            tHostile.AddSources(leave, wounded);
            tHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            tHostile.AddPreAction(new TransitionAction_HH_Settle { reason = "hostile" });
            tHostile.AddPostAction(new TransitionAction_WakeAll());
            tHostile.AddPostAction(new TransitionAction_EndAllJobs());
            g.AddTransition(tHostile);
            return g;
        }

        public override void Notify_PawnLost(Pawn p, PawnLostCondition condition)
        {
            base.Notify_PawnLost(p, condition);
            Guard.Run(ModuleId.Inn, "Inn.Lord.PawnLost", () => GuestRegistry.Instance?.Notify_PawnLost(p, condition));
        }

        public override IEnumerable<Gizmo> GetPawnGizmos(Pawn p)
        {
            if (GuestRegistry.Instance == null) yield break;
            foreach (var g in GuestRegistry.Instance.GizmosFor(p)) yield return g;
        }

        public override string GetReport(Pawn pawn) => GuestRegistry.Instance?.ReportFor(pawn) ?? base.GetReport(pawn);

        public override void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref spot, "spot");
            Scribe_Values.Look(ref groupId, "groupId", -1);
        }
    }

    public class LordToil_HH_Stay : LordToil
    {
        readonly IntVec3 spot;
        public LordToil_HH_Stay(IntVec3 spot) { this.spot = spot; }
        public override bool AllowSatisfyLongNeeds => true;
        public override bool AllowRestingInBed => true;
        public override IntVec3 FlagLoc => spot;

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                var p = lord.ownedPawns[i];
                if (p?.mindState == null) continue;
                p.mindState.duty = new PawnDuty(InnDefOf.HH_GuestStay, spot) { radius = 18f };
            }
        }
    }

    /// <summary>Settles the stay when the group leaves for reasons the register did not plan (danger, wounded, hostility).</summary>
    public class TransitionAction_HH_Settle : TransitionAction
    {
        public string reason;
        public override void DoAction(Transition trans)
        {
            var lord = trans.target?.lord;
            Guard.Run(ModuleId.Inn, "Inn.Lord.Settle", () => GuestRegistry.Instance?.Notify_LordLeaving(lord, reason));
        }
    }
}
