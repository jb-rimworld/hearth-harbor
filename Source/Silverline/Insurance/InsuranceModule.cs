// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
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
                Draw = InsuranceUI.Draw,
                Visible = () => Modules.On(ModuleId.Bank)
            });
            Caretaker.Register(ModuleId.Bank, "Insurance.Contracts", RepairContracts);
            HLog.Info("Bank", $"Clawback Mutual: {DefDatabase<SL_PolicyDef>.AllDefsListForReading.Count} policies, {DefDatabase<SL_ClauseDef>.AllDefsListForReading.Count} clauses, {LimbSchedule.All.Count()} limb lines");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var c = InsuranceComponent.Instance; if (c == null) yield break;
            yield return $"Clawback Mutual: {c.contracts.Count(x => x.Active)} active policies, {c.OpenClaims.Count()} open claims, debt {c.debt:0.##}, insurer account {InsurerBooks.InsurerBalance}";
        }

        public override void PrepareRemoval(List<string> report)
        {
            var c = InsuranceComponent.Instance; if (c == null) return;
            int n = c.contracts.Count(x => x.Active);
            if (n > 0) report.Add($"Clawback Mutual: {n} active policies will be cancelled and open claims paid out at their current offer.");
            foreach (var cl in c.OpenClaims.ToList())
            {
                if (cl.state == ClaimState.Review) ClaimEngine.Decide(cl);
                if (cl.state == ClaimState.Decided) ClaimEngine.Pay(cl);
            }
            foreach (var ct in c.contracts.Where(x => x.Active).ToList()) c.End(ct, "module removed");
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
            foreach (var ct in c.contracts)
                if (ct.Active && ct.sum < 0) { ct.sum = 0; fixes++; Caretaker.Fixed("Insurance.Contracts", "policy " + ct.id + " had a negative sum"); }
            return fixes;
        }
    }
}
