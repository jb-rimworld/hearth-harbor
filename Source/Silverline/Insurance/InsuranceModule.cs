// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>
    /// Entry point of Clawback Mutual (part of the Bank module). A second module object with <see cref="ModuleId.Bank"/>
    /// next to the bank's own: it registers the insurance settings, the "Insurance" tab of the bank window and the caretaker checks.
    /// </summary>
    public sealed class InsuranceModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Bank;

        public override void RegisterSettings() => InsSettings.Register();

        public override void OnDefsLoaded()
        {
            LimbSchedule.LoadDefaults();
            foreach (var d in DefDatabase<SL_LimbScheduleDef>.AllDefsListForReading) d.Apply();
            BankTabs.Register(new BankTab
            {
                Id = "insurance", LabelKey = "HH.Ins.Tab.Bank", LabelEn = "Insurance", Order = 60,
                Draw = InsuranceUI.DrawInBank,
                Visible = () => Modules.On(ModuleId.Bank)
            });
            Caretaker.Register(ModuleId.Bank, "Insurance.Contracts", RepairContracts);
            // pawn tab "Insurance" on every humanlike race (vanilla, Biotech xenotype races and modded alien races alike); it shows itself only once the first policy exists
            int tabs = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.Humanlike))
            {
                if (def.inspectorTabs == null) def.inspectorTabs = new List<Type>();
                if (def.inspectorTabs.Contains(typeof(ITab_SL_Insurance))) continue;
                def.inspectorTabs.Add(typeof(ITab_SL_Insurance));
                if (def.inspectorTabsResolved == null) def.inspectorTabsResolved = new List<InspectTabBase>();
                def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_SL_Insurance)));
                tabs++;
            }
            HLog.Info("Bank", $"Clawback Mutual: insurance tab added to {tabs} humanlike races");
            HLog.Info("Bank", $"Clawback Mutual: {DefDatabase<SL_PolicyDef>.AllDefsListForReading.Count} policies, {DefDatabase<SL_ClauseDef>.AllDefsListForReading.Count} clauses, {LimbSchedule.All.Count()} limb lines");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var c = InsuranceComponent.Instance; if (c == null) yield break;
            yield return $"Clawback Mutual: {c.contracts.Count(x => x.Active)} active policies, {c.OpenClaims.Count()} open claims, debt {c.debt:0.##}, insurer account {InsurerBooks.InsurerBalance}";
        }

        public override void PrepareRemoval(List<string> report)
        {
            var c = InsuranceComponent.Instance; if (c == null) { ClaimLetters.RemoveAll(); return; }
            int n = c.contracts.Count(x => x.Active);
            if (n > 0) report.Add($"Clawback Mutual: {n} active policies will be cancelled and open claims paid out at their current offer.");
            // open filing windows (claim mode "By form") are filed as they are: nothing may be lost to the removal
            foreach (var o in c.OpenOpportunities.ToList()) ClaimOpportunities.Submit(o, null, null);
            foreach (var cl in c.OpenClaims.ToList())
            {
                cl.decisionLetterPending = false;
                if (cl.state == ClaimState.Review) ClaimEngine.Decide(cl);
                if (cl.state == ClaimState.Decided) ClaimEngine.Pay(cl);
            }
            foreach (var ct in c.contracts.Where(x => x.Active).ToList()) c.End(ct, "module removed");
            int letters = ClaimLetters.RemoveAll();   // last: paying out may still have sent letters
            if (letters > 0) report.Add($"Clawback Mutual: {letters} letters removed from the letter stack and the archive.");
        }

        /// <summary>Caretaker: orphaned claims, impossible dates, negative sums.</summary>
        static int RepairContracts()
        {
            var c = InsuranceComponent.Instance; if (c == null) return 0;
            int fixes = 0;
            foreach (var cl in c.claims.ToList())
            {
                if (cl.Open && c.Get(cl.contractId) == null && cl.state != ClaimState.Paid)
                { ClaimEngine.Close(cl); fixes++; Caretaker.Fixed("Insurance.Contracts", "closed claim #" + cl.id + " without a contract"); }
                if (cl.Open && cl.state == ClaimState.Decided && cl.payTick < 0) { cl.payTick = Ins.Now; fixes++; Caretaker.Fixed("Insurance.Contracts", "claim #" + cl.id + " had no payout date"); }
            }
            foreach (var o in c.opportunities.ToList())
                if (o.Open && c.Get(o.contractId) == null) { ClaimOpportunities.Expire(o, true); fixes++; Caretaker.Fixed("Insurance.Contracts", "closed filing window #" + o.id + " without a contract"); }
            foreach (var ct in c.contracts)
                if (ct.Active && ct.HasPendingChange && ct.pendingEffectiveTick < Ins.Now - 3 * ClaimEngine.Day) { c.ApplyPending(ct, Ins.Now); fixes++; Caretaker.Fixed("Insurance.Contracts", "policy " + ct.id + " had an overdue change"); }
            foreach (var ct in c.contracts)
                if (ct.Active && ct.sum < 0) { ct.sum = 0; fixes++; Caretaker.Fixed("Insurance.Contracts", "policy " + ct.id + " had a negative sum"); }
            return fixes;
        }
    }
}
