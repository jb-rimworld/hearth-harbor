// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>Loans, the credit score and the collection ladder (KONZEPT 8.9).</summary>
    public sealed partial class BankGameComp
    {
        public static readonly int[] LoanTerms = { 15, 30, 60, 120 };
        public const int MinLoanCoins = 50;

        public bool LoansUnlocked => BankSettings.ResearchOff || (SLDefOf.SL_Credit?.IsFinished ?? true);
        public int LoanIntervalDays => Math.Max(1, HSettings.GetInt("bank.loanIntervalDays"));

        public double YearlyLoanRate(SL_BankDef b = null) => FinanceMath.LoanYearlyRate(BankSettings.ProfitM, (b ?? Bank)?.loanSpread ?? 0.12);

        public double LoanBasis() => Math.Max(0, LoanNetWorth.Silver) + 0.25 * ColonyWealth();

        public int EffectiveScore()
        {
            if (Bank != null && Bank.ignoresScore) return 700;
            double basis = LoanBasis();
            double s = score + FinanceMath.WealthScoreBonus(basis);
            if (Debt.Silver > 0.7 * FinanceMath.CreditLimit(basis, score)) s += FinanceMath.ScoreHighUtilisation;
            return FinanceMath.ClampScore(s);
        }

        public double CreditLimitSilver()
        {
            int eff = EffectiveScore();
            double limit = FinanceMath.CreditLimit(LoanBasis(), eff);
            if (eff >= 450) limit = Math.Max(limit, HSettings.Get("bank.loanFloor"));
            return Math.Floor(limit);
        }

        public double AvailableCreditSilver() => Math.Max(0, CreditLimitSilver() - Debt.Silver);

        public bool AnyTitleHolder()
        {
            if (!ModsConfig.RoyaltyActive) return false;
            foreach (var m in HomeMaps)
                foreach (var p in m.mapPawns.FreeColonistsSpawned)
                    if (p.royalty != null && p.royalty.AllTitlesForReading.Count > 0) return true;
            return false;
        }

        public bool CanBorrow(out string why)
        {
            why = null;
            if (!hasAccount) { why = L.T("Reason.NoAccount", "There is no account yet."); return false; }
            if (!LoansUnlocked) { why = L.T("Reason.LoansResearch", "Loans need the research project \"consumer credit\"."); return false; }
            if (Bank != null && Bank.loansNeedTitle && !AnyTitleHolder()) { why = L.T("Reason.Title", "{0} only lends to holders of a royal title.", Bank.Name); return false; }
            if (loans.Any(l => l.InArrears)) { why = L.T("Reason.Arrears", "You are behind on a payment. Nobody lends to you until that is settled."); return false; }
            if (AvailableCreditSilver() < MinLoanCoins) { why = L.T("Reason.NoCredit", "Your credit limit is used up ({0} silver left).", (int)AvailableCreditSilver()); return false; }
            return true;
        }

        public Loan NewLoan(Money principal, int termDays, double spread, bool bailout = false)
        {
            int interval = LoanIntervalDays;
            double rate = bailout ? 0 : FinanceMath.LoanYearlyRate(BankSettings.ProfitM, spread);
            int n = FinanceMath.InstallmentCount(termDays, interval);
            var pay = Money.FromSilver(Math.Ceiling(FinanceMath.AnnuityPayment(principal.Silver, rate, termDays, interval) * Money.PerSilver) / Money.PerSilver);
            var l = new Loan
            {
                Id = nextLoanId++, BankDefName = bankDefName, PrincipalMicro = principal.Micro, BalanceMicro = principal.Micro, InstallmentMicro = pay.Micro,
                Spread = spread, StartTick = Now, TermDays = termDays, IntervalDays = interval, NextDueTick = Now + interval * 60000, InstallmentsLeft = n, Bailout = bailout,
            };
            loans.Add(l);
            return l;
        }

        /// <summary>Quote for the loan form: yearly rate, instalment and total cost.</summary>
        public void LoanQuote(int coins, int termDays, out double yearlyRate, out Money installment, out Money total)
        {
            yearlyRate = YearlyLoanRate();
            int interval = LoanIntervalDays, n = FinanceMath.InstallmentCount(termDays, interval);
            installment = Money.FromSilver(FinanceMath.AnnuityPayment(coins, yearlyRate, termDays, interval));
            total = installment * n;
        }

        public bool TryTakeLoan(int coins, int termDays, out string why, out Loan loan)
        {
            loan = null;
            if (!CanBorrow(out why)) return false;
            if (coins < MinLoanCoins) { why = L.T("Reason.MinLoan", "The smallest loan is {0} silver.", MinLoanCoins); return false; }
            if (coins > AvailableCreditSilver()) { why = L.T("Reason.OverLimit", "That is more than the {0} silver you may still borrow.", (int)AvailableCreditSilver()); return false; }
            termDays = Math.Max(5, Math.Min(HSettings.GetInt("bank.loanMaxTermDays"), termDays));
            var principal = Money.FromSilver(coins);
            loan = NewLoan(principal, termDays, Bank?.loanSpread ?? 0.12);
            Ledger.Current.Post(LoansAccount, Colony, principal, L.T("Memo.Loan", "Loan {0}", loan.Id), true);
            InvalidateCache();
            HLog.Info("Bank", $"Loan {loan.Id}: {coins} silver, {termDays} days, rate {YearlyLoanRate():P1}");
            return true;
        }

        /// <summary>Voluntary repayment from the account (any amount up to the balance of the loan).</summary>
        public bool RepayLoan(Loan l, Money amount, out string why)
        {
            why = null;
            amount = Money.Min(amount, l.Balance);
            if (amount.Micro <= 0) { why = L.T("Reason.Amount", "Choose an amount first."); return false; }
            if (Balance < amount) { why = L.T("Reason.Funds2", "The account does not hold {0}.", amount.ToStringShort()); return false; }
            ApplyPayment(l, amount, Colony, L.T("Memo.Repay", "Loan {0}: repayment", l.Id), false);
            return true;
        }

        /// <summary>Moves money from <paramref name="fromAccount"/> to the loan account, reduces debt and arrears and clears the ladder when nothing is overdue.</summary>
        public void ApplyPayment(Loan l, Money pay, string fromAccount, string memo, bool onTime)
        {
            pay = Money.Min(pay, l.Balance);
            if (pay.Micro <= 0) return;
            Ledger.Current.Post(fromAccount, LoansAccount, pay, memo);
            l.BalanceMicro -= pay.Micro;
            l.ArrearsMicro = Math.Max(0, l.ArrearsMicro - pay.Micro);
            if (onTime) score = Math.Min(FinanceMath.ScoreMax, score + FinanceMath.ScoreOnTime);
            if (l.ArrearsMicro == 0 && l.OverdueSinceTick >= 0)
            {
                l.OverdueSinceTick = -1; l.Stage = 0; l.LastEscalationDay = -1;
                Messages.Message(L.T("Msg.Settled", "Loan {0} is back in good standing.", l.Id), MessageTypeDefOf.PositiveEvent, false);
                Collections.AfterPayment(this);
            }
            if (l.BalanceMicro <= 0)
            {
                loans.Remove(l);
                score = Math.Min(FinanceMath.ScoreMax, score + 10);
                Messages.Message(L.T("Msg.LoanRepaid", "Loan {0} is fully repaid.", l.Id), MessageTypeDefOf.PositiveEvent, false);
                Collections.AfterPayment(this);
            }
            InvalidateCache();
        }

        void AccrueLoanInterest()
        {
            var led = Ledger.Current;
            foreach (var l in loans)
            {
                double rate = l.Bailout ? 0 : FinanceMath.LoanYearlyRate(BankSettings.ProfitM, l.Spread);
                var interest = l.Balance * (FinanceMath.LoanFactor(rate, 1) - 1.0);
                if (interest.Micro <= 0) continue;
                l.BalanceMicro += interest.Micro;
                led.Post(LoansAccount, "world", interest, L.T("Memo.LoanInterest", "Loan {0}: interest", l.Id), true);
            }
        }

        void ProcessLoanDues(int t)
        {
            for (int i = loans.Count - 1; i >= 0; i--)
            {
                var l = loans[i];
                int guard = 0;
                while (i < loans.Count && loans[i] == l && t >= l.NextDueTick && guard++ < 50)
                {
                    int dueTick = l.NextDueTick;
                    var due = l.InstallmentsLeft <= 1 ? l.Balance : Money.Min(l.Installment, l.Balance);
                    due = Money.Max(due, Money.Zero);
                    if (BankSettings.MaxCollectionStage <= 0 && Balance < due)
                    {
                        // Comped: nobody collects, the instalment is quietly deferred (KONZEPT 8.9)
                        l.NextDueTick += l.IntervalDays * 60000;
                        continue;
                    }
                    if (l.InstallmentsLeft > 0) l.InstallmentsLeft--;
                    l.NextDueTick += l.IntervalDays * 60000;
                    l.ArrearsMicro = Math.Min(l.BalanceMicro, l.ArrearsMicro + due.Micro);
                    bool wasClean = l.OverdueSinceTick < 0;
                    var pay = Money.Min(l.Arrears, Balance);
                    if (pay.Micro > 0) ApplyPayment(l, pay, Colony, L.T("Memo.Installment", "Loan {0}: instalment", l.Id), wasClean && pay >= due);
                    if (!loans.Contains(l)) break;
                    if (l.InArrears)
                    {
                        l.Missed++;
                        if (l.OverdueSinceTick < 0) l.OverdueSinceTick = dueTick;
                        Messages.Message(L.T("Msg.Missed", "The instalment of loan {0} could not be paid.", l.Id), MessageTypeDefOf.NegativeEvent, false);
                    }
                }
            }
        }

        /// <summary>Daily: retry overdue payments, lower the score, climb the ladder (capped by difficulty).</summary>
        void ProcessOverdue(int day)
        {
            foreach (var l in loans.ToList())
            {
                if (!l.InArrears) continue;
                var pay = Money.Min(l.Arrears, Balance);
                if (pay.Micro > 0) { ApplyPayment(l, pay, Colony, L.T("Memo.Late", "Loan {0}: late payment", l.Id), false); if (!loans.Contains(l) || !l.InArrears) continue; }
                score = Math.Max(FinanceMath.ScoreMin, score + FinanceMath.ScoreLateDay);
                AdvanceLadder(l, day);
            }
        }

        public void AdvanceLadder(Loan l, int day)
        {
            int days = l.DaysOverdue(Now);
            int stage = FinanceMath.CollectionStage(days, BankSettings.MaxCollectionStage);
            while (l.Stage < stage) { l.Stage++; Collections.Execute(this, l, l.Stage); }
            if (l.Stage >= 6 && l.Stage == stage && (l.LastEscalationDay < 0 || day - l.LastEscalationDay >= (l.Stage >= 7 ? 3 : 7)))
            {
                if (l.LastEscalationDay >= 0) Collections.Execute(this, l, l.Stage);
                l.LastEscalationDay = day;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Effects of the ladder that change the state of the bank itself (the visible parts are in Collections)
        // ------------------------------------------------------------------------------------------------
        public void ApplyLateFee(Loan l)
        {
            var fee = FinanceMath.LateFee(l.Arrears) * (HSettings.Get("bank.lateFee") / 0.05);
            if (fee.Micro <= 0) return;
            l.BalanceMicro += fee.Micro; l.ArrearsMicro += fee.Micro;
            Ledger.Current.Post(LoansAccount, "world", fee, L.T("Memo.LateFee", "Loan {0}: late fee", l.Id), true);
            totalFeesMicro += fee.Micro;
        }

        /// <summary>Runs the daily overdue step (retry payments, ladder) outside the day roll. Used by tests and by the window after a manual payment.</summary>
        public void ProcessOverdueForTest() => ProcessOverdue(Today);

        public void LowerScore(int by) { score = Math.Max(FinanceMath.ScoreMin, score - by); }
    }
}
