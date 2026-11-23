// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>
    /// "Start with a Silverline account" (F08, 1.1.0): the colony begins as a customer of one bank, with an opening balance and, if wanted,
    /// a starting loan (the dark scenario starts in debt to the Black Ledger). Runs once after the new game is set up, only while the bank
    /// module is on. Bank kept by name, so a save or scenario file never asks for a missing def once the mod is gone.
    /// </summary>
    public class ScenPart_SLStartingAccount : ScenPart, IHearthScenarioStart
    {
        public string bank = "SL_Mutual";
        public int balance = 500;
        public bool directDeposit;
        /// <summary>Starting loan in silver (0 = none) and its term in days.</summary>
        public int loan;
        public int loanDays = 30;
        [Unsaved] string bufBalance, bufLoan;

        public SL_BankDef Bank => bank.NullOrEmpty() ? null : DefDatabase<SL_BankDef>.GetNamedSilentFail(bank);
#if !RW15   // 1.5 has no ScenPart.Valid

        public override bool Valid() => base.Valid() && !bank.NullOrEmpty();
#endif

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            float rh = RowHeight + 4f;
            var r = listing.GetScenPartRect(this, rh * 4f);
            ScenarioSeed.GuardedEdit("Scenario.EditAccount", () => DrawRows(r, rh));
        }

        void DrawRows(Rect r, float rh)
        {
            var row = new Rect(r.x, r.y, r.width, rh - 2f);
            var b = Bank;
            if (Widgets.ButtonText(row, b != null ? b.Name : (bank ?? "?")))
                FloatMenuUtility.MakeMenu(DefDatabase<SL_BankDef>.AllDefs.OrderBy(d => d.order), d => d.Name, d => () => bank = d.defName);
            row.y += rh;
            Widgets.Label(row.LeftPart(0.5f), L.T("Scen.Balance", "Opening balance"));
            Widgets.TextFieldNumeric(row.RightPart(0.48f), ref balance, ref bufBalance, 0f, 1_000_000f);
            row.y += rh;
            Widgets.Label(row.LeftPart(0.5f), L.T("Scen.Loan", "Starting loan"));
            Widgets.TextFieldNumeric(row.RightPart(0.48f), ref loan, ref bufLoan, 0f, 1_000_000f);
            TooltipHandler.TipRegion(row, L.T("Scen.LoanTip", "0 = no loan. A loan is paid out into the account and repaid in instalments like any other loan."));
            row.y += rh;
            Widgets.CheckboxLabeled(row, L.T("Scen.DirectDeposit", "Direct deposit"), ref directDeposit);
        }

        public override string Summary(Scenario scen)
        {
            var b = Bank;
            string s = L.T("Scen.Summary", "Starts with an account at {0} holding {1} silver.", b != null ? b.Name : bank, balance.ToString("#,##0", CultureInfo.InvariantCulture));
            if (loan > 0) s += " " + L.T("Scen.SummaryLoan", "It already owes {0} silver ({1} days).", loan.ToString("#,##0", CultureInfo.InvariantCulture), loanDays);
            return s;
        }

        public void OnStart()
        {
            if (!Modules.On(ModuleId.Bank)) { HLog.Info("Bank", "Scenario account skipped: the bank module is off"); return; }
            var comp = BankGameComp.Current; var def = Bank ?? SL_BankDef.Default;
            if (comp == null || def == null) { HLog.Warn("Bank", "Scenario account: no bank component or bank def"); return; }
            if (!comp.HasAccount)
            {
                // the Black Ledger only serves colonies the underworld knows; a scenario that picks it has already set that up (rules part)
                if (!comp.CanOpenAccount(def, out string why)) { HLog.Warn("Bank", "Scenario account at " + def.defName + " not possible (" + why + "), using " + SL_BankDef.Default?.defName); def = SL_BankDef.Default; }
                if (def == null || !comp.OpenAccount(def, directDeposit)) { HLog.Warn("Bank", "Scenario account could not be opened"); return; }
            }
            if (balance > 0) Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(balance), L.T("Memo.ScenBalance", "Opening balance"));
            if (loan > 0)
            {
                var l = comp.NewLoan(Money.FromSilver(loan), Mathf.Max(5, loanDays), comp.Bank?.loanSpread ?? 0.12);
                Ledger.Current.Post(BankGameComp.LoansAccount, BankGameComp.Colony, Money.FromSilver(loan), L.T("Memo.Loan", "Loan {0}", l.Id), true);
                comp.InvalidateCache();
            }
            HLog.Info("Bank", $"Scenario: account at {comp.Bank?.defName}, opening balance {balance}, loan {loan}");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref bank, "bank", "SL_Mutual");
            Scribe_Values.Look(ref balance, "balance", 500);
            Scribe_Values.Look(ref directDeposit, "directDeposit");
            Scribe_Values.Look(ref loan, "loan");
            Scribe_Values.Look(ref loanDays, "loanDays", 30);
        }

        public override int GetHashCode() => base.GetHashCode() ^ (bank ?? "").GetHashCode() ^ balance ^ (loan * 7);
    }
}
