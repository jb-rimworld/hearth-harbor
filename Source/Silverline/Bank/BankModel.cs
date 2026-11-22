// SPDX-License-Identifier: MIT
using System;
using Hearth;
using Verse;

namespace Silverline
{
    public enum BankSite { Remote, Atm, Branch }
    public enum TransferKind { Deposit, Withdraw }

    /// <summary>Money that is on its way: a deposit waiting to be credited, or a withdrawal waiting for the drop pod.</summary>
    public sealed class PendingTransfer : IExposable
    {
        public TransferKind Kind;
        public long Micro;          // gross amount (physical silver for withdrawals)
        public int DueTick;
        public int MapId = -1;
        public IntVec3 Cell = IntVec3.Invalid;
        public string Memo;
        public double FeeRate;      // fee fraction applied on arrival (deposits)

        public Money Amount => new Money(Micro);

        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "k");
            Scribe_Values.Look(ref Micro, "a");
            Scribe_Values.Look(ref DueTick, "due");
            Scribe_Values.Look(ref MapId, "map", -1);
            Scribe_Values.Look(ref Cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref Memo, "memo");
            Scribe_Values.Look(ref FeeRate, "fee");
        }
    }

    /// <summary>A loan of the colony (8.9). Debt = Balance (principal plus accrued interest). The ledger account "bank:loans" mirrors the sum (negative).</summary>
    public sealed class Loan : IExposable
    {
        public int Id;
        public string BankDefName;
        public long PrincipalMicro, BalanceMicro, InstallmentMicro, ArrearsMicro;
        public double Spread;
        public int StartTick, TermDays, IntervalDays, NextDueTick, InstallmentsLeft;
        public int OverdueSinceTick = -1;   // first unpaid due date, -1 = in good standing
        public int Stage;                   // collection stage reached (0 = none)
        public int Missed;                  // missed instalments over the life of the loan
        public int LastEscalationDay = -1;  // for repeating stages (6, 7)
        public bool Bailout;                // interest-free rescue loan

        public Money Principal => new Money(PrincipalMicro);
        public Money Balance => new Money(BalanceMicro);
        public Money Installment => new Money(InstallmentMicro);
        public Money Arrears => new Money(ArrearsMicro);
        public bool InArrears => ArrearsMicro > 0;
        public int DaysOverdue(int nowTick) => OverdueSinceTick < 0 ? 0 : Math.Max(0, (nowTick - OverdueSinceTick) / 60000) + 1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref BankDefName, "bank");
            Scribe_Values.Look(ref PrincipalMicro, "p"); Scribe_Values.Look(ref BalanceMicro, "b");
            Scribe_Values.Look(ref InstallmentMicro, "i"); Scribe_Values.Look(ref ArrearsMicro, "arr");
            Scribe_Values.Look(ref Spread, "spread");
            Scribe_Values.Look(ref StartTick, "start"); Scribe_Values.Look(ref TermDays, "term"); Scribe_Values.Look(ref IntervalDays, "interval", 5);
            Scribe_Values.Look(ref NextDueTick, "next"); Scribe_Values.Look(ref InstallmentsLeft, "left");
            Scribe_Values.Look(ref OverdueSinceTick, "overdue", -1); Scribe_Values.Look(ref Stage, "stage"); Scribe_Values.Look(ref Missed, "missed");
            Scribe_Values.Look(ref LastEscalationDay, "lastEsc", -1);
            Scribe_Values.Look(ref Bailout, "bailout");
        }
    }

    /// <summary>One day of history for the chart and the 60-day peak (Too Big To Fail).</summary>
    public sealed class DaySample : IExposable
    {
        public int Day; public long Balance, NetWorth;
        public void ExposeData() { Scribe_Values.Look(ref Day, "d"); Scribe_Values.Look(ref Balance, "b"); Scribe_Values.Look(ref NetWorth, "n"); }
    }
}
