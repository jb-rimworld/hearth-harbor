// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Silverline
{
    /// <summary>Common base of the ATM and the branch counter: a colonist walks to the interaction cell and the bank window opens there.</summary>
    public abstract class Building_SLTerminal : Building
    {
        CompPowerTrader power;
        public abstract BankSite Site { get; }
        protected abstract string UseLabel { get; }

        public bool Powered => Spawned && (power == null || power.PowerOn) && !Map.gameConditionManager.ElectricityDisabled(Map);

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn myPawn)
        {
            foreach (var o in base.GetFloatMenuOptions(myPawn)) yield return o;
            if (!Modules.On(ModuleId.Bank)) yield break;
            if (!myPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Some)) { yield return new FloatMenuOption("CannotUseNoPath".Translate(), null); yield break; }
            if (!Powered) { yield return new FloatMenuOption("CannotUseNoPower".Translate(), null); yield break; }
            if (!myPawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking) && !myPawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            { yield return new FloatMenuOption("CannotUseReason".Translate("IncapableOfCapacity".Translate(PawnCapacityDefOf.Manipulation.label, myPawn.Named("PAWN"))), null); yield break; }
            var self = this;
            yield return new FloatMenuOption(UseLabel, () =>
            {
                var job = JobMaker.MakeJob(SLDefOf.SL_UseBank, self);
                myPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            if (BankApi.Impl != null && Modules.On(ModuleId.Bank))
            {
                var c = BankGameComp.Current;
                string line = c != null && c.HasAccount ? L.T("Building.Account", "{0}: balance {1}", c.BankName, c.Balance.ToStringShort()) : L.T("Building.NoAccount", "No account yet. Open one here or at a comms console.");
                s = s.NullOrEmpty() ? line : s + "\n" + line;
            }
            return s;
        }

        /// <summary>Opens the bank window as if the colonist stood here.</summary>
        public void OpenWindowFor(Pawn user)
        {
            Find.WindowStack.Add(new Window_Bank(Map, Site, this));
        }
    }

    public class Building_SLATM : Building_SLTerminal
    {
        public override BankSite Site => BankSite.Atm;
        protected override string UseLabel => L.T("Building.UseAtm", "Use the ATM");
    }

    public class Building_SLBranch : Building_SLTerminal
    {
        public override BankSite Site => BankSite.Branch;
        protected override string UseLabel => L.T("Building.UseBranch", "Go to the counter");
    }

    /// <summary>The night safe: the nightly till sink spot. Offers a switch for direct deposit.</summary>
    public class Building_SLNightSafe : Building
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            if (!Modules.On(ModuleId.Bank) || BankApi.Impl == null || !BankApi.Impl.HasAccount) yield break;
            // F19: bank terms are changed in the mod settings only; the gizmo shows the state and opens them
            bool on = BankApi.Impl.DirectDepositOn;
            yield return new Command_Action
            {
                defaultLabel = L.T("Building.OpenSettings", "Open settings (Bank)") + "\n" + L.T("Building.DirectState", "Direct deposit: {0}", on ? L.T("Lobby.On", "on") : L.T("Lobby.Off", "off")),
                defaultDesc = L.T("Building.DirectDepositDesc", "On: the tills of your businesses are emptied into your bank account at midnight. Off: the takings are put down as silver next to the safe.")
                              + "\n\n" + L.T("Building.OpenSettingsDesc", "This is a term of your bank and is changed in the mod settings only (Bank > Account). Click to open them."),
                icon = ContentFinder<Texture2D>.Get("UI/HH/Bank/DirectDeposit", true),
                action = () => HearthMod.OpenSettings("mod:Bank"),
            };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string line = BankApi.Impl != null && BankApi.Impl.HasAccount
                ? (BankApi.Impl.DirectDepositOn ? L.T("Building.SafeDirect", "Takings go straight to the account at midnight.") : L.T("Building.SafeFloor", "Takings are put down as silver at midnight."))
                : L.T("Building.SafeNoAccount", "No bank account: takings are put down as silver at midnight.");
            return s.NullOrEmpty() ? line : s + "\n" + line;
        }
    }

    public class JobDriver_UseBank : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell);
            var open = ToilMaker.MakeToil("OpenBank");
            open.initAction = () =>
            {
                if (job.GetTarget(TargetIndex.A).Thing is Building_SLTerminal t && t.Spawned) t.OpenWindowFor(pawn);
            };
            open.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return open;
        }
    }

    [DefOf]
    public static class SLDefOf
    {
        public static JobDef SL_UseBank;
        public static ThingDef SL_ATM, SL_BankCounter, SL_NightSafe;
        public static ResearchProjectDef SL_Banking, SL_Branches, SL_Credit;
        public static ThoughtDef SL_Finances, SL_FinancesGreedy, SL_CollectorsNearby;
        public static FactionDef SL_CollectionsAgency;
        static SLDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(SLDefOf)); }
    }
}
