// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>Entry point of the bank module (Silverline, KONZEPT 8).</summary>
    public sealed class BankModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Bank;

        public override void RegisterSettings() => BankSettings.Register();

        public override void OnDefsLoaded()
        {
            BankApi.Impl = new BankImpl();
            Tills.BankSink = new BankTillSink();
            BankUiTabs.RegisterAll();
            Caretaker.Register(ModuleId.Bank, "Bank.LoanLedger", BankCaretaker.CheckLoanLedger);
            Caretaker.Register(ModuleId.Bank, "Bank.Transit", BankCaretaker.CheckTransit);
            Caretaker.Register(ModuleId.Bank, "Bank.ResearchOff", BankCaretaker.CheckResearchOff);
            HLog.Info("Bank", $"Silverline ready: {DefDatabase<SL_BankDef>.DefCount} banks");
        }

        public override void OnGameStarted() => BankCaretaker.CheckResearchOff();
        public override void OnGameLoaded() => BankCaretaker.CheckResearchOff();

        public override IEnumerable<string> Diagnosis()
        {
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount) { yield return "Bank: no account."; yield break; }
            yield return $"Bank: {c.BankName}, balance {c.Balance.ToStringFull()}, in transit {c.InTransit.ToStringFull()}, debt {c.Debt.ToStringFull()}, score {c.Score}, {c.Loans.Count} loans, M={BankSettings.ProfitM:0.###}";
        }

        public override void PrepareRemoval(List<string> report)
        {
            var c = BankGameComp.Current;
            if (c != null && c.HasAccount) report.Add($"Silverline: the account at {c.BankName} holds {c.Balance.ToStringShort()} silver. Withdraw it first; without the mod the ledger entries are ignored.");
            // the collection agency (faction, agents, lords) lives in vanilla managers: take it out now
            Guard.Run(ModuleId.Bank, "Bank.RemoveAgency", () => report.AddRange(Collections.CleanUpForRemoval()));
            report.Add("Silverline: bank buildings (counters, ATMs, night safes) stay on the map until you deconstruct them; without the mod RimWorld drops them on load with a warning. See docs/removal_silverline.md.");
        }
    }

    /// <summary>The contract implementation other modules use (BankApi.Impl).</summary>
    public sealed class BankImpl : IBankImpl
    {
        static BankGameComp C => BankGameComp.Current;
        public bool HasAccount => C != null && C.HasAccount;
        public string ColonyAccount => BankGameComp.Colony;
        public Money ColonyBalance => C?.Balance ?? Money.Zero;
        public bool DirectDepositOn => C != null && C.DirectDepositOn;

        /// <summary>Moves money into the colony account: from the map's till if it holds that much, otherwise from the outside world (income).</summary>
        public bool Deposit(Map map, Money amount, string memo)
        {
            var c = C; var led = Ledger.Current;
            if (c == null || !c.HasAccount || led == null || amount.Micro <= 0) return false;
            string from = "world";
            if (map != null) { string till = Payments.ColonyTill(map); if (led.Balance(till) >= amount) from = till; }
            c.Credit(from, amount, memo ?? "Deposit", HSettings.Get("bank.directDepositFee"));
            return true;
        }

        /// <summary>Pays out physical silver (instantly at a bank building, else by drop pod). Whole silver only: the fraction stays on the account.</summary>
        public bool Withdraw(Map map, Money amount, string memo)
        {
            var c = C; if (c == null || !c.HasAccount || map == null) return false;
            int coins = amount.WholeSilver; if (coins <= 0) return false;
            return c.TryWithdraw(map, coins, BankSite.Remote, out _);
        }
    }

    /// <summary>Takes the money of the tills at midnight when direct deposit is on (K4).</summary>
    public sealed class BankTillSink : Tills.ISink
    {
        public bool TryTake(Map map, Money amount, string memo)
        {
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount || !Modules.On(ModuleId.Bank) || !c.DirectDepositOn) return false;
            if (amount.Micro <= 0) return false;
            c.Credit(Payments.ColonyTill(map), amount, memo ?? "Nightly deposit", HSettings.Get("bank.directDepositFee"));
            HLog.Debug("Bank", $"Till of {map} deposited: {amount}");
            return true;
        }
    }

    /// <summary>Repairs (rule: only what is clearly broken, always logged).</summary>
    public static class BankCaretaker
    {
        public static int CheckLoanLedger()
        {
            var c = BankGameComp.Current; var led = Ledger.Current;
            if (c == null || led == null) return 0;
            long want = -c.Loans.Sum(l => l.BalanceMicro), have = led.Balance(BankGameComp.LoansAccount).Micro;
            if (want == have) return 0;
            long diff = want - have;   // negative = more debt than the ledger shows
            if (diff < 0) led.Post(BankGameComp.LoansAccount, "world", new Money(-diff), "Caretaker: loan ledger correction", true);
            else led.Post("world", BankGameComp.LoansAccount, new Money(diff), "Caretaker: loan ledger correction", true);
            Caretaker.Fixed("Bank.LoanLedger", $"loan account was {have} micro, loans say {want}");
            return 1;
        }

        public static int CheckTransit()
        {
            var c = BankGameComp.Current; var led = Ledger.Current;
            if (c == null || led == null) return 0;
            long expected = c.Pending.Sum(p => p.Micro), have = led.Balance(BankGameComp.Transit).Micro;
            if (have == expected) return 0;
            if (have > expected && c.Pending.Count == 0)
            {
                led.Post(BankGameComp.Transit, BankGameComp.Colony, new Money(have - expected), "Caretaker: stray transit money returned");
                Caretaker.Fixed("Bank.Transit", $"{have - expected} micro in transit without a transfer, returned to the account");
                return 1;
            }
            return 0;
        }

        /// <summary>With "research off" all banking projects count as finished (the vanilla way: the project is simply done).</summary>
        public static int CheckResearchOff()
        {
            if (!Modules.On(ModuleId.Bank) || !BankSettings.ResearchOff || Current.Game == null || Find.ResearchManager == null) return 0;
            int n = 0;
            foreach (var p in new[] { SLDefOf.SL_Banking, SLDefOf.SL_Branches, SLDefOf.SL_Credit })
                if (p != null && !p.IsFinished) { Find.ResearchManager.FinishProject(p, false, null, false); n++; }
            if (n > 0) HLog.Info("Bank", $"Research off: {n} banking projects completed");
            return 0;
        }
    }
}
