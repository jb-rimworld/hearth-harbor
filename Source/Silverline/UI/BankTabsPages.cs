// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Silverline
{
    /// <summary>Registers the pages of the bank window that the bank module owns. Others (insurance, lottery, investments) add theirs with BankTabs.Register.</summary>
    public static class BankUiTabs
    {
        public static void RegisterAll()
        {
            BankTabs.Register(new BankTab { Id = "lobby", LabelKey = "HH.Bank.Tab.Lobby", LabelEn = "Lobby", Order = 10, Draw = LobbyPage.Draw });
            BankTabs.Register(new BankTab { Id = "loans", LabelKey = "HH.Bank.Tab.Loans", LabelEn = "Loans", Order = 20, Draw = LoansPage.Draw, Visible = () => BankGameComp.Current?.HasAccount == true });
            BankTabs.Register(new BankTab { Id = "history", LabelKey = "HH.Bank.Tab.History", LabelEn = "History", Order = 30, Draw = HistoryPage.Draw });
            BankTabs.Register(new BankTab { Id = "terms", LabelKey = "HH.Bank.Tab.Terms", LabelEn = "Terms", Order = 40, Draw = TermsPage.Draw });
        }
    }

    // ==================================================================================================
    static class LobbyPage
    {
        static int amount = 100; static string buf = "100";

        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; var w = Window_Bank.Active; if (c == null || w == null) return;
            float leftW = 410f;
            var rA = new Rect(r.x, r.y, leftW, 190f);
            var rB = new Rect(r.x + leftW + 10f, r.y, r.width - leftW - 10f, 190f);
            var rC = new Rect(r.x, r.y + 198f, leftW, 170f);
            var rD = new Rect(rB.x, rC.y, rB.width, 170f);
            var rE = new Rect(r.x, r.y + 376f, r.width, Math.Max(40f, r.height - 376f));
            Accounts(rA, c, w); ChartCard(rB, c); Transfer(rC, c, w); Recent(rD, c); BankStrip(rE, c);
        }

        static void Accounts(Rect r, BankGameComp c, Window_Bank w)
        {
            var i = BankUI.Card(r, L.T("Lobby.Accounts", "Accounts"));
            double target = c.Balance.Silver;
            if (double.IsNaN(w.ShownBalance)) w.ShownBalance = target;
            double shown = BankUI.Ease(ref w.ShownBalance, target);
            double m = BankSettings.ProfitM, accM = FinanceMath.AccountYearlyMultiple(m, BankSettings.AccountBeta, c.Bank?.interestMod ?? 1.0);
            var big = new Rect(i.x, i.y, i.width, 34f);
            BankUI.Label(new Rect(big.x, big.y + 6f, 120f, 24f), L.T("Lobby.Balance", "Balance"), BankUI.CreamDim);
            BankUI.Label(new Rect(big.x + 100f, big.y, big.width - 100f, 34f), shown.ToString("#,##0.00", CultureInfo.InvariantCulture), BankUI.Cream, GameFont.Medium, TextAnchor.MiddleRight, false);
            var balRect = big; Widgets.DrawHighlightIfMouseover(balRect);
            TooltipHandler.TipRegion(balRect, L.T("Lobby.BalanceTip", "Exact: {0} silver.\nInterest every day: balance × (M^({1} × {2} / 60) − 1), with M = {3} per game year (the profitability slider). That is {4:0.##} % a day.", c.Balance.ToStringFull(), BankSettings.AccountBeta.ToString("0.##", CultureInfo.InvariantCulture), (c.Bank?.interestMod ?? 1.0).ToString("0.##", CultureInfo.InvariantCulture), m.ToString("0.###", CultureInfo.InvariantCulture), (FinanceMath.DailyFactor(accM) - 1) * 100));
            float y = i.y + 38f;
            Action<string, string, string, Color?> row = (l, v, t, col) => { BankUI.Row(new Rect(i.x, y, i.width, 22f), l, v, t, col); y += 22f; };
            row(L.T("Lobby.Transit", "In transit"), BankUI.Silver(c.InTransit), L.T("Lobby.TransitTip", "Silver that has left your storage but is not credited yet (deposit delay without a branch or ATM)."), null);
            row(L.T("Lobby.Cash", "Cash at home"), c.CashAtHome.ToString("#,##0", CultureInfo.InvariantCulture), L.T("Lobby.CashTip", "Silver lying around on your home maps."), null);
            row(L.T("Lobby.Debt", "Debt"), c.Debt.Micro > 0 ? "-" + BankUI.Silver(c.Debt) : "0.00", L.T("Lobby.DebtTip", "Sum of your open loans including accrued interest."), c.Debt.Micro > 0 ? BankUI.Bad : (Color?)null);
            row(L.T("Lobby.Net", "Net worth"), BankUI.Silver(c.NetWorth), L.T("Lobby.NetTip", "Account + silver at home − debts. Sets your wealth tier."), null);
            row(L.T("Lobby.Interest", "Interest this quadrum"), BankUI.Signed(c.InterestThisQuadrum), L.T("Lobby.InterestTip", "Interest paid on the balance since the quadrum began (15 days)."), c.InterestThisQuadrum.Micro >= 0 ? BankUI.Good : BankUI.Bad);
        }

        static void ChartCard(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Lobby.Chart", "Last 30 days"));
            BankUI.DrawChart(new Rect(i.x, i.y, i.width, i.height), c.ChartPoints(30));
        }

        static void Transfer(Rect r, BankGameComp c, Window_Bank w)
        {
            var i = BankUI.Card(r, L.T("Lobby.Transfer", "Deposit and withdraw"));
            var map = w.Map; int depositable = BankSilver.DepositableCoins(map);
            int max = Math.Max(100, Math.Max(depositable, (int)c.Balance.Silver));
            max = Math.Min(max, Math.Max(100, HSettings.GetInt("bank.podMaxLoad")));
            float y = i.y;
            BankUI.Label(new Rect(i.x, y + 2f, 60f, 24f), L.T("Lobby.Amount", "Amount"), BankUI.CreamDim);
            float before = amount;
            float v = Widgets.HorizontalSlider(new Rect(i.x + 64f, y + 6f, i.width - 64f - 96f, 20f), amount, 0f, max, false, null, null, null, 1f);
            if (Math.Abs(v - before) > 0.5f) { amount = (int)v; buf = amount.ToString(CultureInfo.InvariantCulture); }
            int a2 = amount; Widgets.TextFieldNumeric(new Rect(i.xMax - 88f, y + 2f, 88f, 26f), ref a2, ref buf, 0f, 1_000_000f);
            if (a2 != amount) amount = a2;
            y += 32f;
            float bw = (i.width - 12f) / 4f; int[] presets = { 50, 100, 500, max };
            for (int k = 0; k < 4; k++)
            {
                var pr = new Rect(i.x + k * (bw + 4f), y, bw, 22f);
                string lbl = k == 3 ? L.T("Lobby.Max", "Max") : presets[k].ToString(CultureInfo.InvariantCulture);
                if (Widgets.ButtonText(pr, lbl)) { amount = Math.Min(presets[k], max); buf = amount.ToString(CultureInfo.InvariantCulture); }
            }
            y += 28f;
            bool instantDeposit = w.Site != BankSite.Remote || c.HasInstantSite(map);
            string why1 = null, why2 = null;
            bool canDep = amount > 0 && depositable >= amount; if (!canDep) why1 = amount <= 0 ? L.T("Reason.Amount", "Choose an amount first.") : L.T("Reason.NoSilver", "Not enough silver in storage or at the trade beacon ({0} available).", depositable);
            var fee = c.WithdrawFee(Money.FromSilver(amount), w.Site);
            bool canWd = amount > 0 && c.Balance >= Money.FromSilver(amount) + fee; if (!canWd) why2 = amount <= 0 ? L.T("Reason.Amount", "Choose an amount first.") : L.T("Reason.Funds", "The account does not cover {0} plus {1} in fees.", Money.FromSilver(amount).ToStringShort(), fee.ToStringShort());
            if (w.Site == BankSite.Atm && canWd && amount > c.AtmRemainingToday()) { canWd = false; why2 = L.T("Reason.AtmLimit", "The ATM will only hand out {0} more today.", c.AtmRemainingToday()); }
            float half = (i.width - 8f) / 2f;
            if (BankUI.Button(new Rect(i.x, y, half, 30f), L.T("Lobby.Deposit", "Deposit"), canDep, why1))
            {
                if (c.TryDeposit(map, amount, w.Site, out string why)) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); else Messages.Message(why, MessageTypeDefOf.RejectInput, false);
            }
            string wdLabel = w.Site == BankSite.Remote && !c.HasInstantSite(map) ? L.T("Lobby.WithdrawPod", "Withdraw (pod)") : L.T("Lobby.Withdraw", "Withdraw");
            if (BankUI.Button(new Rect(i.x + half + 8f, y, half, 30f), wdLabel, canWd, why2))
            {
                if (c.TryWithdraw(map, amount, w.Site, out string why, w.At)) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); else Messages.Message(why, MessageTypeDefOf.RejectInput, false);
            }
            y += 34f;
            string eta1 = instantDeposit ? L.T("Lobby.DepInstant", "booked at once") : L.T("Lobby.DepDelay", "credited after {0}", BankUI.HoursText((int)(HSettings.Get("bank.depositDelayHours") * 2500f)));
            string eta2 = w.Site != BankSite.Remote || c.HasInstantSite(map) ? L.T("Lobby.WdInstant", "cash at the building at once") : L.T("Lobby.WdPod", "drop pod in {0} to {1} h", HSettings.Get("bank.podMinHours").ToString("0.#", CultureInfo.InvariantCulture), HSettings.Get("bank.podMaxHours").ToString("0.#", CultureInfo.InvariantCulture));
            BankUI.Label(new Rect(i.x, y, i.width, i.yMax - y), L.T("Lobby.TransferInfo", "In storage: {0}. Deposit {1}. Withdrawal fee {2}, {3}.", depositable, eta1, fee.ToStringShort(), eta2), BankUI.CreamDim, GameFont.Tiny);
        }

        static void Recent(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Lobby.Recent", "Recent activity"));
            float y = i.y; int shown = 0;
            foreach (var p in c.Pending.Reverse())
            {
                if (shown >= 6) break;
                string txt = p.Kind == TransferKind.Deposit ? L.T("Lobby.PendingDep", "↓ {0} arriving in {1}", p.Amount.ToStringShort(), BankUI.HoursText(Math.Max(0, p.DueTick - c.Now))) : L.T("Lobby.PendingWd", "↑ {0} by pod in {1}", p.Amount.ToStringShort(), BankUI.HoursText(Math.Max(0, p.DueTick - c.Now)));
                BankUI.Label(new Rect(i.x, y, i.width, 20f), txt, BankUI.Brass, GameFont.Tiny, wrap: false); y += 19f; shown++;
            }
            foreach (var e in BankLedgerView.Entries(40))
            {
                if (shown >= 6) break;
                var amt = BankLedgerView.SignedForColony(e);
                BankUI.Label(new Rect(i.x, y, i.width * 0.66f, 20f), BankUI.TickToDay(e.Tick) + "  " + e.Memo, BankUI.CreamDim, GameFont.Tiny, wrap: false);
                BankUI.Label(new Rect(i.x + i.width * 0.6f, y, i.width * 0.4f, 20f), BankUI.Signed(amt), amt.Micro >= 0 ? BankUI.Good : BankUI.Bad, GameFont.Tiny, TextAnchor.UpperRight, false);
                y += 19f; shown++;
            }
            if (shown == 0) BankUI.Label(new Rect(i.x, y, i.width, 40f), L.T("Lobby.Nothing", "Nothing yet. A deposit would be a fine start."), BankUI.CreamDim, GameFont.Tiny);
        }

        static void BankStrip(Rect r, BankGameComp c)
        {
            Widgets.DrawBoxSolid(r, BankUI.WalnutLight);
            GUI.color = BankUI.BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            var b = c.Bank; if (b == null) return;
            GUI.DrawTexture(new Rect(r.x + 8f, r.y + 6f, Math.Min(52f, r.height - 12f), Math.Min(52f, r.height - 12f)), b.Icon);
            BankUI.Label(new Rect(r.x + 70f, r.y + 6f, r.width - 360f, 22f), b.Name, BankUI.Brass, wrap: false);
            BankUI.Label(new Rect(r.x + 70f, r.y + 28f, r.width - 360f, 22f), "\"" + b.Slogan + "\"", BankUI.CreamDim, GameFont.Tiny, wrap: false);
            TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width - 330f, r.height), b.description + "\n\n" + string.Join("\n", b.perks.Select(p => "• " + p)));
            bool direct = c.DirectDepositOn;
            var cb = new Rect(r.xMax - 330f, r.y + 8f, 190f, 24f);
            Widgets.CheckboxLabeled(cb, L.T("Lobby.Direct", "Direct deposit"), ref direct);
            TooltipHandler.TipRegion(cb, L.T("Lobby.DirectTip", "Tills of your businesses are emptied into the account at midnight, minus {0} %.", (HSettings.Get("bank.directDepositFee") * 100).ToString("0.##", CultureInfo.InvariantCulture)));
            if (direct != c.DirectDepositOn) HSettings.Set("bank.directDeposit", direct ? 1f : 0f);
            if (Widgets.ButtonText(new Rect(r.xMax - 130f, r.y + 8f, 120f, 30f), L.T("Lobby.ChangeBank", "Change bank"))) Find.WindowStack.Add(new Window_ChooseBank());
        }
    }

    // ==================================================================================================
    /// <summary>Reads the bank's accounts out of the shared ledger (the transaction history is the ledger, KONZEPT 8.1).</summary>
    public static class BankLedgerView
    {
        public static bool Involves(Ledger.Entry e) => (e.From != null && e.From.StartsWith("bank:")) || (e.To != null && e.To.StartsWith("bank:"));

        /// <summary>Newest first.</summary>
        public static List<Ledger.Entry> Entries(int max, bool includeTransit = false)
        {
            var led = Ledger.Current; var res = new List<Ledger.Entry>(); if (led == null) return res;
            var j = led.Journal;
            for (int i = j.Count - 1; i >= 0 && res.Count < max; i--)
            {
                var e = j[i];
                if (!Involves(e)) continue;
                bool touchesColony = e.From == BankGameComp.Colony || e.To == BankGameComp.Colony;
                if (!touchesColony && !includeTransit && !(e.From == BankGameComp.LoansAccount || e.To == BankGameComp.LoansAccount)) continue;
                res.Add(e);
            }
            return res;
        }

        /// <summary>The change of the colony account caused by this entry (positive = money in).</summary>
        public static Money SignedForColony(Ledger.Entry e)
        {
            if (e.To == BankGameComp.Colony) return e.Amount;
            if (e.From == BankGameComp.Colony) return -e.Amount;
            if (e.To == BankGameComp.LoansAccount) return e.Amount;     // repayment shown as plus on the loan
            if (e.From == BankGameComp.LoansAccount) return -e.Amount;
            return e.Amount;
        }
    }

    // ==================================================================================================
    static class HistoryPage
    {
        static Vector2 scroll;
        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; if (c == null) return;
            var head = new Rect(r.x, r.y, r.width, 54f);
            var i0 = BankUI.Card(head, null);
            BankUI.Row(new Rect(i0.x, i0.y, i0.width / 2f - 8f, 22f), L.T("Hist.TotalInterest", "Interest earned so far"), BankUI.Signed(c.TotalInterest), L.T("Hist.TotalInterestTip", "Everything the bank has paid you in interest (negative interest counted against it)."));
            BankUI.Row(new Rect(i0.x + i0.width / 2f + 8f, i0.y, i0.width / 2f - 8f, 22f), L.T("Hist.TotalFees", "Fees paid so far"), BankUI.Silver(c.TotalFees), L.T("Hist.TotalFeesTip", "All fees, late fees and the price of changing banks."));
            var list = new Rect(r.x, r.y + 62f, r.width, r.height - 62f);
            Widgets.DrawBoxSolid(list, BankUI.WalnutLight); GUI.color = BankUI.BrassDim; Widgets.DrawBox(list, 1); GUI.color = Color.white;
            var entries = BankLedgerView.Entries(400, true);
            float rowH = 22f;
            BankUI.Label(new Rect(list.x + 10f, list.y + 4f, 140f, 20f), L.T("Hist.When", "When"), BankUI.Brass, GameFont.Tiny);
            BankUI.Label(new Rect(list.x + 160f, list.y + 4f, 300f, 20f), L.T("Hist.What", "What"), BankUI.Brass, GameFont.Tiny);
            BankUI.Label(new Rect(list.xMax - 190f, list.y + 4f, 180f, 20f), L.T("Hist.Amount", "Amount (5 decimals)"), BankUI.Brass, GameFont.Tiny, TextAnchor.UpperRight);
            var outR = new Rect(list.x, list.y + 24f, list.width, list.height - 26f);
            var view = new Rect(0, 0, outR.width - 16f, Math.Max(outR.height, entries.Count * rowH + 4f));
            Widgets.BeginScrollView(outR, ref scroll, view);
            float y = 0;
            foreach (var e in entries)
            {
                var row = new Rect(0, y, view.width, rowH);
                if (((int)(y / rowH)) % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                var amt = BankLedgerView.SignedForColony(e);
                BankUI.Label(new Rect(10f, y + 2f, 150f, rowH), BankUI.TickToDay(e.Tick), BankUI.CreamDim, GameFont.Tiny, wrap: false);
                BankUI.Label(new Rect(160f, y + 2f, view.width - 360f, rowH), e.Memo ?? "", BankUI.Cream, GameFont.Tiny, wrap: false);
                BankUI.Label(new Rect(view.width - 200f, y + 2f, 190f, rowH), (amt.Micro >= 0 ? "+" : "-") + amt.Abs.ToStringFull(), amt.Micro >= 0 ? BankUI.Good : BankUI.Bad, GameFont.Tiny, TextAnchor.UpperRight, false);
                TooltipHandler.TipRegion(row, e.From + " → " + e.To);
                y += rowH;
            }
            if (entries.Count == 0) BankUI.Label(new Rect(10f, 4f, view.width, 30f), L.T("Hist.Empty", "No transactions yet."), BankUI.CreamDim);
            Widgets.EndScrollView();
        }
    }

    // ==================================================================================================
    static class LoansPage
    {
        static int amount = 200, term = 30; static string buf = "200"; static Vector2 scroll;

        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; var w = Window_Bank.Active; if (c == null || w == null) return;
            var rA = new Rect(r.x, r.y, 400f, 196f);
            var rB = new Rect(r.x + 410f, r.y, r.width - 410f, 196f);
            var rC = new Rect(r.x, r.y + 204f, r.width, r.height - 204f);
            Credit(rA, c); NewLoan(rB, c); MyLoans(rC, c);
        }

        static void Credit(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Loans.Credit", "Your credit"));
            int eff = c.EffectiveScore();
            float y = i.y;
            BankUI.Row(new Rect(i.x, y, i.width, 22f), L.T("Loans.Score", "Ledger score"), eff.ToString(CultureInfo.InvariantCulture),
                L.T("Loans.ScoreTip", "300 to 850, starts at 600. On-time instalment +4, every late day -6, default -120, using more than 70 % of the limit -20, plus 20 × log10(wealth / 10,000). Base score {0}.", c.Score), eff >= 600 ? BankUI.Good : (eff >= 450 ? BankUI.Cream : BankUI.Bad));
            y += 22f;
            var bar = new Rect(i.x, y + 2f, i.width, 10f);
            Widgets.DrawBoxSolid(bar, BankUI.Walnut);
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * (eff - 300) / 550f, bar.height), eff >= 600 ? BankUI.GreenLight : BankUI.BrassDim);
            y += 18f;
            double limit = c.CreditLimitSilver(), debt = c.Debt.Silver, avail = c.AvailableCreditSilver();
            Action<string, string, string, Color?> row = (l, v, t, col) => { BankUI.Row(new Rect(i.x, y, i.width, 22f), l, v, t, col); y += 22f; };
            row(L.T("Loans.Limit", "Credit limit"), limit.ToString("#,##0", CultureInfo.InvariantCulture), L.T("Loans.LimitTip", "0.15 × wealth × (score − 300) / 550, where wealth = net worth + a quarter of the colony wealth. At least the starter credit ({0} silver) for a fine score.", HSettings.GetInt("bank.loanFloor")), null);
            row(L.T("Loans.Used", "In use"), debt.ToString("#,##0.00", CultureInfo.InvariantCulture), null, debt > 0 ? BankUI.Bad : (Color?)null);
            row(L.T("Loans.Available", "Available"), avail.ToString("#,##0", CultureInfo.InvariantCulture), null, null);
            row(L.T("Loans.Rate", "Rate per game year"), (c.YearlyLoanRate() * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %",
                L.T("Loans.RateTip", "Base rate max(2 %, M − 1) = {0} % plus the spread of your bank ({1} points). It follows the profitability slider, so borrowing to invest never has a positive expectation.", (FinanceMath.LoanBaseRate(BankSettings.ProfitM) * 100).ToString("0.#", CultureInfo.InvariantCulture), ((c.Bank?.loanSpread ?? 0) * 100).ToString("0.#", CultureInfo.InvariantCulture)), null);
        }

        static void NewLoan(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Loans.New", "New loan"));
            bool can = c.CanBorrow(out string why);
            int max = (int)Math.Max(BankGameComp.MinLoanCoins, c.AvailableCreditSilver());
            amount = Math.Max(BankGameComp.MinLoanCoins, Math.Min(amount, max));
            float y = i.y;
            BankUI.Label(new Rect(i.x, y + 2f, 60f, 24f), L.T("Lobby.Amount", "Amount"), BankUI.CreamDim);
            float v = Widgets.HorizontalSlider(new Rect(i.x + 64f, y + 6f, i.width - 64f - 96f, 20f), amount, BankGameComp.MinLoanCoins, Math.Max(BankGameComp.MinLoanCoins + 1, max), false, null, null, null, 1f);
            if (Math.Abs(v - amount) > 0.5f) { amount = (int)v; buf = amount.ToString(CultureInfo.InvariantCulture); }
            int a2 = amount; Widgets.TextFieldNumeric(new Rect(i.xMax - 88f, y + 2f, 88f, 26f), ref a2, ref buf, BankGameComp.MinLoanCoins, 1_000_000f); amount = a2;
            y += 32f;
            BankUI.Label(new Rect(i.x, y + 2f, 60f, 22f), L.T("Loans.Term", "Term"), BankUI.CreamDim);
            float bw = (i.width - 70f) / 4f; int maxTerm = HSettings.GetInt("bank.loanMaxTermDays");
            foreach (var t in BankGameComp.LoanTerms.Where(t => t <= maxTerm).Select((t, k) => new { t, k }))
            {
                var br = new Rect(i.x + 64f + t.k * (bw + 2f), y, bw, 24f);
                if (term == t.t) Widgets.DrawBoxSolid(br, BankUI.GreenLight);
                if (Widgets.ButtonText(br, t.t + L.T("Loans.DaysShort", " d"))) term = t.t;
            }
            y += 30f;
            c.LoanQuote(amount, term, out double rate, out Money inst, out Money total);
            BankUI.Label(new Rect(i.x, y, i.width, 34f), L.T("Loans.Quote", "{0} silver at {1} % a year: {2} every {3} days, {4} in total ({5} interest).", amount, (rate * 100).ToString("0.#", CultureInfo.InvariantCulture), inst.ToStringShort(), c.LoanIntervalDays, total.ToStringShort(), (total - Money.FromSilver(amount)).ToStringShort()), BankUI.Cream, GameFont.Tiny);
            y += 36f;
            if (BankUI.Button(new Rect(i.x, y, i.width, 28f), L.T("Loans.Take", "Take the loan"), can && amount <= c.AvailableCreditSilver(), why ?? L.T("Reason.OverLimit", "That is more than the {0} silver you may still borrow.", (int)c.AvailableCreditSilver())))
            {
                if (c.TryTakeLoan(amount, term, out string w2, out _)) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); else Messages.Message(w2, MessageTypeDefOf.RejectInput, false);
            }
        }

        static void MyLoans(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Loans.Mine", "Your loans"));
            if (c.Loans.Count == 0) { BankUI.Label(new Rect(i.x, i.y, i.width, 40f), L.T("Loans.None", "No loans. Ottoline is almost disappointed."), BankUI.CreamDim); return; }
            float rowH = 54f;
            var view = new Rect(0, 0, i.width - 16f, c.Loans.Count * (rowH + 4f));
            Widgets.BeginScrollView(i, ref scroll, view);
            float y = 0;
            foreach (var l in c.Loans.ToList())
            {
                var row = new Rect(0, y, view.width, rowH);
                Widgets.DrawBoxSolid(row, l.InArrears ? new Color(0.35f, 0.12f, 0.1f) : BankUI.Walnut);
                string status = l.InArrears ? L.T("Loans.Overdue", "OVERDUE, stage {0}: {1} due", l.Stage, l.Arrears.ToStringShort())
                    : L.T("Loans.NextDue", "next instalment {0} in {1}", l.InstallmentsLeft <= 1 ? l.Balance.ToStringShort() : l.Installment.ToStringShort(), BankUI.HoursText(Math.Max(0, l.NextDueTick - c.Now)));
                BankUI.Label(new Rect(row.x + 8f, row.y + 3f, row.width - 280f, 22f), L.T("Loans.Line", "Loan {0}: {1} borrowed, {2} still owed", l.Id, l.Principal.ToStringShort(), l.Balance.ToStringShort()) + (l.Bailout ? L.T("Loans.BailoutTag", " (bailout, no interest)") : ""), BankUI.Cream, wrap: false);
                BankUI.Label(new Rect(row.x + 8f, row.y + 27f, row.width - 280f, 22f), status, l.InArrears ? BankUI.Bad : BankUI.CreamDim, GameFont.Tiny, wrap: false);
                float bx = row.xMax - 264f;
                var amt100 = Money.Min(l.Balance, Money.FromSilver(100));
                if (BankUI.Button(new Rect(bx, row.y + 12f, 120f, 30f), L.T("Loans.Repay100", "Repay {0}", amt100.ToStringShort()), c.Balance >= amt100 && amt100.Micro > 0, L.T("Reason.Funds2", "The account does not hold {0}.", amt100.ToStringShort())))
                    if (!c.RepayLoan(l, amt100, out string why)) Messages.Message(why, MessageTypeDefOf.RejectInput, false);
                if (c.Loans.Contains(l) && BankUI.Button(new Rect(bx + 126f, row.y + 12f, 130f, 30f), L.T("Loans.RepayAll", "Repay all"), c.Balance >= l.Balance, L.T("Reason.Funds2", "The account does not hold {0}.", l.Balance.ToStringShort())))
                    if (!c.RepayLoan(l, l.Balance, out string why2)) Messages.Message(why2, MessageTypeDefOf.RejectInput, false);
                y += rowH + 4f;
            }
            Widgets.EndScrollView();
        }
    }

    // ==================================================================================================
    static class TermsPage
    {
        static string mBuf; static float lastM = -1f;
        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; if (c == null) return;
            var rA = new Rect(r.x, r.y, r.width, 214f);
            var rB = new Rect(r.x, r.y + 222f, 430f, r.height - 222f);
            var rC = new Rect(rB.xMax + 10f, rB.y, r.width - 440f, rB.height);
            Profit(rA, c); Fees(rB, c); Tiers(rC, c);
        }

        static void Profit(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Terms.Profit", "Profitability"));
            double m = BankSettings.ProfitM;
            BankUI.Label(new Rect(i.x, i.y, i.width, 40f), L.T("Terms.ProfitIntro", "How much invested money grows on average per game year (60 days). 1.0 is plus/minus nothing, 1.2 is +20 % a year. It also sets what loans cost. This is a setting of this save game."), BankUI.CreamDim);
            float y = i.y + 44f;
            float lo = Mathf.Log(0.01f), hi = Mathf.Log(100f);
            float s = Mathf.Clamp(m > 0 ? Mathf.Log((float)m) : lo, lo, hi);
            BankUI.Label(new Rect(i.x, y + 2f, 40f, 24f), "M", BankUI.Brass, GameFont.Medium);
            float s2 = Widgets.HorizontalSlider(new Rect(i.x + 36f, y + 6f, i.width - 36f - 200f, 20f), s, lo, hi, false, null, "0.01", "100", -1f);
            float shown = (float)m;
            if (Math.Abs(shown - lastM) > 1e-6f || mBuf == null) { mBuf = shown.ToString("0.####", CultureInfo.InvariantCulture); lastM = shown; }
            float typed = shown; Widgets.TextFieldNumeric(new Rect(i.xMax - 160f, y + 2f, 100f, 26f), ref typed, ref mBuf, 0f, 1000f);
            if (Math.Abs(s2 - s) > 1e-4f) { float nm = Mathf.Exp(s2); nm = nm >= 10 ? Mathf.Round(nm * 10f) / 10f : (nm >= 1 ? Mathf.Round(nm * 100f) / 100f : Mathf.Round(nm * 1000f) / 1000f); HSettings.Set("bank.profit", nm); mBuf = null; }
            else if (Math.Abs(typed - shown) > 1e-6f) { HSettings.Set("bank.profit", typed); lastM = typed; }
            if (Widgets.ButtonText(new Rect(i.xMax - 54f, y + 2f, 54f, 26f), L.T("Terms.Auto", "Auto")))
            { var st = HSettings.Find("bank.profit"); if (st != null) HSettings.Reset(st); mBuf = null; }
            TooltipHandler.TipRegion(new Rect(i.xMax - 54f, y + 2f, 54f, 26f), L.T("Terms.AutoTip", "Follow the difficulty slider again ({0} on your current difficulty).", BankSettings.DefaultProfitFor(BankSettings.Tier).ToString("0.###", CultureInfo.InvariantCulture)));
            y += 38f;
            double daily = FinanceMath.DailyFactor(m) - 1, accM = FinanceMath.AccountYearlyMultiple(m, BankSettings.AccountBeta, c.Bank?.interestMod ?? 1.0);
            BankUI.Row(new Rect(i.x, y, i.width / 2f - 10f, 22f), L.T("Terms.Daily", "Per day (index)"), (daily * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " %", L.T("Terms.DailyTip", "M^(1/60) − 1. A game year has 60 days."));
            BankUI.Row(new Rect(i.x + i.width / 2f + 10f, y, i.width / 2f - 10f, 22f), L.T("Terms.AccountDaily", "Your account per day"), ((FinanceMath.DailyFactor(accM) - 1) * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " %",
                L.T("Terms.AccountDailyTip", "Account multiple per year = M^({0} × {1}) = {2}.", BankSettings.AccountBeta.ToString("0.##", CultureInfo.InvariantCulture), (c.Bank?.interestMod ?? 1.0).ToString("0.##", CultureInfo.InvariantCulture), accM.ToString("0.####", CultureInfo.InvariantCulture)));
            y += 24f;
            BankUI.Label(new Rect(i.x, y, i.width, 40f), L.T("Terms.Preview", "1,000 silver at the index become {0} after one game year and {1} after ten.", FinanceMath.GrowthAfterYears(1000, m, 1).ToString("#,##0", CultureInfo.InvariantCulture), FinanceMath.GrowthAfterYears(1000, m, 10).ToString("#,##0", CultureInfo.InvariantCulture)), BankUI.Cream);
        }

        static void Fees(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Terms.Fees", "Fees of your bank"));
            var b = c.Bank; if (b == null) return;
            double f = BankSettings.FeeFactor; float y = i.y;
            Action<string, string, string> row = (l, v, t) => { BankUI.Row(new Rect(i.x, y, i.width, 22f), l, v, t); y += 22f; };
            row(L.T("Terms.Dep", "Deposit"), ((b.depositFee * f) * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %", null);
            row(L.T("Terms.Wd", "Withdrawal (pod / counter)"), ((b.withdrawFee * f) * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %, min " + (b.withdrawFeeMin * f).ToString("0.##", CultureInfo.InvariantCulture), null);
            row(L.T("Terms.Atm", "ATM"), (b.atmFlatFee * f).ToString("0.##", CultureInfo.InvariantCulture) + " + " + ((b.atmFee * f) * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %, " + HSettings.GetInt("bank.atmDailyLimit") + "/day", null);
            row(L.T("Terms.AccFee", "Account fee per quadrum"), (b.accountFeePerQuadrum * f).ToString("0.##", CultureInfo.InvariantCulture), null);
            row(L.T("Terms.Direct", "Direct deposit"), ((HSettings.Get("bank.directDepositFee")) * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %", null);
            row(L.T("Terms.Switch", "Changing banks"), c.SwitchFee.ToStringShort(), null);
            y += 4f;
            BankUI.Label(new Rect(i.x, y, i.width, i.yMax - y), L.T("Terms.FeesNote", "All fees scale with the \"bank fee level\" setting ({0} %).", (f * 100).ToString("0", CultureInfo.InvariantCulture)), BankUI.CreamDim, GameFont.Tiny);
        }

        static void Tiers(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Terms.Anchors", "By difficulty"));
            float y = i.y; float tier = BankSettings.Tier; int near = (int)Math.Round(tier);
            BankUI.Label(new Rect(i.x, y, i.width, 20f), L.T("Terms.AnchorsHead", "M per year / highest collection stage"), BankUI.CreamDim, GameFont.Tiny); y += 20f;
            for (int k = 0; k < 7; k++)
            {
                var row = new Rect(i.x, y, i.width, 22f);
                if (k == near) Widgets.DrawBoxSolid(row, BankUI.Green);
                BankUI.Label(new Rect(row.x + 4f, row.y, row.width * 0.5f, 22f), L.T("Tier.Name" + k, HearthProfile.TierNamesEn[k]), k == near ? BankUI.Cream : BankUI.CreamDim, GameFont.Tiny, wrap: false);
                BankUI.Label(new Rect(row.x + row.width * 0.5f, row.y, row.width * 0.5f - 4f, 22f), BankSettings.ProfitAnchors[k].ToString("0.##", CultureInfo.InvariantCulture) + "  /  " + (int)BankSettings.CollectionStageAnchors[k], k == near ? BankUI.Cream : BankUI.CreamDim, GameFont.Tiny, TextAnchor.UpperRight, false);
                y += 22f;
            }
            y += 6f;
            BankUI.Label(new Rect(i.x, y, i.width, i.yMax - y), L.T("Terms.StageNote", "Collection ladder: 1 reminder, 2 late fee, 3 offset against deposits, 4 friendly visit, 5 seizure team, 6 collection raid, 7 bounty. Now: stops at stage {0}.", BankSettings.MaxCollectionStage), BankUI.CreamDim, GameFont.Tiny);
        }
    }
}
