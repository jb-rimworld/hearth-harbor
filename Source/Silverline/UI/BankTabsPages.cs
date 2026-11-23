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
        static int amount = 100; static string buf = "100"; static bool wd; static Vector2 scroll;
        const float MinH = 540f;
        /// <summary>Test hooks: the transfer state of the lobby.</summary>
        public static int Amount { get => amount; set { amount = value; buf = value.ToString(CultureInfo.InvariantCulture); } }
        public static bool WithdrawMode { get => wd; set => wd = value; }

        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; var w = Window_Bank.Active; if (c == null || w == null) return;
            BankUI.ScrollPage(r, ref scroll, MinH, v => DrawIn(v, c, w));
        }

        static void DrawIn(Rect r, BankGameComp c, Window_Bank w)
        {
            const float gap = 10f;
            float extra = Mathf.Clamp(r.height - MinH, 0f, 260f);
            float topH = 250f + extra * 0.5f, midH = 222f + extra * 0.5f;
            float leftW = Math.Max(400f, r.width * 0.44f);
            var rA = new Rect(r.x, r.y, leftW, topH);
            var rB = new Rect(r.x + leftW + gap, r.y, r.width - leftW - gap, topH);
            var rC = new Rect(r.x, r.y + topH + gap, leftW, midH);
            var rD = new Rect(rB.x, rC.y, rB.width, midH);
            var rE = new Rect(r.x, rC.yMax + gap, r.width, Math.Max(68f, Math.Min(96f, r.yMax - rC.yMax - gap)));
            Accounts(rA, c, w); ChartCard(rB, c); Transfer(rC, c, w); Recent(rD, c); BankStrip(rE, c);
        }

        static string Num(double v, string f = "0.##") => v.ToString(f, CultureInfo.InvariantCulture);

        static void Accounts(Rect r, BankGameComp c, Window_Bank w)
        {
            var i = BankUI.Card(r, L.T("Lobby.Accounts", "Accounts"));
            double target = c.Balance.Silver;
            if (double.IsNaN(w.ShownBalance)) w.ShownBalance = target;
            double shown = BankUI.Ease(ref w.ShownBalance, target);
            double m = BankSettings.ProfitM, bankMod = c.Bank?.interestMod ?? 1.0, share = BankSettings.AccountShare;
            double accM = BankSettings.AccountMultiple(bankMod), accPct = (accM - 1.0) * 100.0;
            var big = new Rect(i.x, i.y, i.width, 34f);
            BankUI.Label(new Rect(big.x, big.y + 6f, 120f, 24f), L.T("Lobby.Balance", "Balance"), BankUI.CreamDim);
            var coin = BankUI.Tex("Coin"); if (coin != null) GUI.DrawTexture(new Rect(big.x + 80f, big.y + 5f, 24f, 24f), coin);
            BankUI.Label(new Rect(big.x + 108f, big.y, big.width - 108f, 34f), shown.ToString("#,##0.00", CultureInfo.InvariantCulture), BankUI.Cream, GameFont.Medium, TextAnchor.MiddleRight, false);
            var balRect = big; Widgets.DrawHighlightIfMouseover(balRect);
            TooltipHandler.TipRegion(balRect, L.T("Lobby.BalanceTip", "Exact: {0} silver.\nThe account is a savings account: it pays {1} % of the market's surplus (M − 1) a year, times {2} for this bank. With M = {3} that is {4} % a year ({5} % a day). Investing pays more, and costs risk.", c.Balance.ToStringFull(), Num(share * 100), Num(bankMod), Num(m, "0.###"), Num(accPct, "+0.0;-0.0;0.0"), Num((FinanceMath.DailyFactor(accM) - 1) * 100)));
            float y = i.y + 38f;
            Action<string, string, string, Color?> row = (l, v, t, col) => { BankUI.Row(new Rect(i.x, y, i.width, 22f), l, v, t, col); y += 22f; };
            var parts = c.InvestmentParts;
            row(L.T("Lobby.Transit", "In transit"), BankUI.Silver(c.InTransit), L.T("Lobby.TransitTip", "Silver on its way: deposits not credited yet (deposit delay without a branch or ATM) and withdrawals still in a drop pod. It counts towards your net worth."), null);
            row(L.T("Lobby.Cash", "Cash at home"), c.CashAtHome.ToString("#,##0", CultureInfo.InvariantCulture), L.T("Lobby.CashTip", "Silver lying around on your home maps."), null);
            row(L.T("Lobby.Invested", "Investments"), BankUI.Silver(c.InvestmentsValue), L.T("Lobby.InvestedTip", "Stocks, funds, crypto and NFTs at today's market value: {0}. Patience bonds at their current value: {1}. Money staked in Omen contracts and faction projects (at cost): {2}.", new Money(parts.Securities).ToStringShort(), new Money(parts.Bonds).ToStringShort(), new Money(parts.Stakes).ToStringShort()), null);
            row(L.T("Lobby.Debt", "Debt"), c.Debt.Micro > 0 ? "-" + BankUI.Silver(c.Debt) : "0.00", L.T("Lobby.DebtTip", "Sum of your open loans including accrued interest."), c.Debt.Micro > 0 ? BankUI.Bad : (Color?)null);
            row(L.T("Lobby.Net", "Net worth"), BankUI.Silver(c.NetWorth), L.T("Lobby.NetTip", "Account {0} + in transit {1} + silver at home {2} + investments at market value {3} − debts {4} = {5}.\nSets your wealth tier. Credit limits count only {6} % of the investments (setting bank.loanPortfolioShare).", BankUI.Silver(c.Balance), BankUI.Silver(c.InTransit), c.CashAtHome.ToString("#,##0", CultureInfo.InvariantCulture), BankUI.Silver(c.InvestmentsValue), BankUI.Silver(c.Debt), BankUI.Silver(c.NetWorth), Num(BankSettings.LoanPortfolioShare * 100, "0")), BankUI.Brass);
            row(L.T("Lobby.Rate", "Interest rate"), YieldCompare.Pct(accPct) + " " + L.T("Lobby.PerYear", "a year"),
                L.T("Lobby.RateTip", "What the account earns per game year (60 days), on average. Compare with the products in the Invest tab:\n{0}", string.Join("\n", YieldCompare.Rows(c.Bank).Select(x => x.Label + ": " + YieldCompare.Pct(x.YearlyPercent)))), accPct >= 0 ? BankUI.Good : BankUI.Bad);
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
            var map = w.Map; int depositable = BankSilver.DepositableCoinsCached(map);
            int withdrawable = c.MaxWithdrawCoins(w.Site);
            long avail = wd ? withdrawable : depositable;
            float y = i.y;
            float half = (i.width - 6f) / 2f;
            if (BankUI.Chip(new Rect(i.x, y, half, 24f), L.T("Lobby.ModeDeposit", "Deposit"), !wd)) wd = false;
            if (BankUI.Chip(new Rect(i.x + half + 6f, y, half, 24f), L.T("Lobby.ModeWithdraw", "Withdraw"), wd)) wd = true;
            y += 28f;
            double pct = TransferMath.PercentFromAmount(amount, avail);
            string lab = wd ? L.T("Lobby.PercentWd", "{0} % of what the account can pay out ({1} of {2})", TransferMath.WholePercent(pct), amount.ToString("#,##0", CultureInfo.InvariantCulture), avail.ToString("#,##0", CultureInfo.InvariantCulture))
                            : L.T("Lobby.Percent", "{0} % of your silver ({1} of {2})", TransferMath.WholePercent(pct), amount.ToString("#,##0", CultureInfo.InvariantCulture), avail.ToString("#,##0", CultureInfo.InvariantCulture));
            BankUI.LabelCut(new Rect(i.x, y, i.width, 18f), lab, avail > 0 ? BankUI.CreamDim : BankUI.Bad, GameFont.Tiny);
            y += 18f;
            // slider: relative to the silver of the colony, 0 to 100 %; typed amounts and presets are absolute and never limited by the slider
            var sr = new Rect(i.x, y + 4f, i.width - 96f, 20f);
            if (avail > 0)
            {
                float v = Widgets.HorizontalSlider(sr, (float)pct, 0f, 1f, false, null, null, null, -1f);
                if (Math.Abs(v - (float)pct) > 0.0004f)
                {
                    long a = TransferMath.AmountFromPercent(TransferMath.SnapPercent(v), avail);
                    amount = (int)a; buf = amount.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f); Widgets.HorizontalSlider(sr, 0f, 0f, 1f, false, null, null, null, -1f); GUI.color = Color.white;
                TooltipHandler.TipRegion(sr, wd ? L.T("Reason.NothingToWithdraw", "The account has nothing to pay out here right now.") : L.T("Reason.NoSilverAtAll", "There is no silver in storage or at the trade beacon."));
            }
            int a2 = amount; Widgets.TextFieldNumeric(new Rect(i.xMax - 88f, y, 88f, 26f), ref a2, ref buf, 0f, 2_000_000_000f);
            if (a2 != amount) amount = a2;
            y += 30f;
            float bw = (i.width - 12f) / 4f; int[] presets = { 50, 100, 500, 0 };
            for (int k = 0; k < 4; k++)
            {
                var pr = new Rect(i.x + k * (bw + 4f), y, bw, 22f);
                string lbl = k == 3 ? L.T("Lobby.Max", "Max") : presets[k].ToString(CultureInfo.InvariantCulture);
                if (Widgets.ButtonText(pr, lbl)) { amount = k == 3 ? (int)Math.Min(avail, int.MaxValue) : (avail > 0 ? (int)Math.Min(presets[k], avail) : presets[k]); buf = amount.ToString(CultureInfo.InvariantCulture); BankSound.Play(BankSound.Tick); }
            }
            TooltipHandler.TipRegion(new Rect(i.x + 3 * (bw + 4f), y, bw, 22f), wd ? L.T("Lobby.MaxTipWd", "Everything the account can pay out here (after fees).") : L.T("Lobby.MaxTip", "All the silver of the colony in storage and at the trade beacon. There is no upper limit."));
            y += 26f;
            bool instantDeposit = w.Site != BankSite.Remote || c.HasInstantSite(map);
            var fee = c.WithdrawFee(Money.FromSilver(amount), w.Site);
            Rect btn = new Rect(i.x, y, i.width, 30f);
            if (!wd)
            {
                string why1 = null; bool canDep = amount > 0 && depositable >= amount; if (!canDep) why1 = amount <= 0 ? L.T("Reason.Amount", "Choose an amount first.") : L.T("Reason.NoSilver", "Not enough silver in storage or at the trade beacon ({0} available).", depositable);
                if (BankUI.Button(btn, L.T("Lobby.Deposit", "Deposit"), canDep, why1))
                {
                    if (c.TryDeposit(map, amount, w.Site, out string why)) BankSound.Play(BankSound.CoinsIn); else { BankSound.Play(BankSound.Reject); Messages.Message(why, MessageTypeDefOf.RejectInput, false); }
                }
            }
            else
            {
                string why2 = null; bool canWd = amount > 0 && c.Balance >= Money.FromSilver(amount) + fee; if (!canWd) why2 = amount <= 0 ? L.T("Reason.Amount", "Choose an amount first.") : L.T("Reason.Funds", "The account does not cover {0} plus {1} in fees.", Money.FromSilver(amount).ToStringShort(), fee.ToStringShort());
                if (w.Site == BankSite.Atm && canWd && amount > c.AtmRemainingToday()) { canWd = false; why2 = L.T("Reason.AtmLimit", "The ATM will only hand out {0} more today.", c.AtmRemainingToday()); }
                string wdLabel = w.Site == BankSite.Remote && !c.HasInstantSite(map) ? L.T("Lobby.WithdrawPod", "Withdraw (pod)") : L.T("Lobby.Withdraw", "Withdraw");
                if (BankUI.Button(btn, wdLabel, canWd, why2))
                {
                    if (c.TryWithdraw(map, amount, w.Site, out string why, w.At)) BankSound.Play(BankSound.CoinsOut); else { BankSound.Play(BankSound.Reject); Messages.Message(why, MessageTypeDefOf.RejectInput, false); }
                }
            }
            y += 34f;
            string eta1 = instantDeposit ? L.T("Lobby.DepInstant", "booked at once") : L.T("Lobby.DepDelay", "credited after {0}", BankUI.HoursText((int)(HSettings.Get("bank.depositDelayHours") * 2500f)));
            string eta2 = w.Site != BankSite.Remote || c.HasInstantSite(map) ? L.T("Lobby.WdInstant", "cash at the building at once") : L.T("Lobby.WdPod", "drop pod in {0} to {1} h", HSettings.Get("bank.podMinHours").ToString("0.#", CultureInfo.InvariantCulture), HSettings.Get("bank.podMaxHours").ToString("0.#", CultureInfo.InvariantCulture));
            if (i.yMax - y > 14f) BankUI.Label(new Rect(i.x, y, i.width, i.yMax - y), L.T("Lobby.TransferInfo", "In storage: {0}. Deposit {1}. Withdrawal fee {2}, {3}.", depositable, eta1, fee.ToStringShort(), eta2), BankUI.CreamDim, GameFont.Tiny);
        }

        static void Recent(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Lobby.Recent", "Recent activity"));
            float y = i.y; int shown = 0; int maxRows = Math.Max(3, (int)(i.height / 19f));
            foreach (var p in c.Pending.Reverse())
            {
                if (shown >= maxRows) break;
                string txt = p.Kind == TransferKind.Deposit ? L.T("Lobby.PendingDep", "↓ {0} arriving in {1}", p.Amount.ToStringShort(), BankUI.HoursText(Math.Max(0, p.DueTick - c.Now))) : L.T("Lobby.PendingWd", "↑ {0} by pod in {1}", p.Amount.ToStringShort(), BankUI.HoursText(Math.Max(0, p.DueTick - c.Now)));
                BankUI.LabelCut(new Rect(i.x, y, i.width, 20f), txt, BankUI.Brass, GameFont.Tiny); y += 19f; shown++;
            }
            foreach (var e in BankLedgerView.Entries(40))
            {
                if (shown >= maxRows) break;
                var amt = BankLedgerView.SignedForColony(e);
                string amtText = BankUI.Signed(amt);
                float amtW = Math.Max(54f, 8f + Text.CalcSize(amtText).x * 0.9f);   // the amount keeps its place, the memo gives way
                BankUI.LabelCut(new Rect(i.x, y, i.width - amtW - 6f, 20f), BankUI.TickToDay(e.Tick) + "  " + e.Memo, BankUI.CreamDim, GameFont.Tiny);
                BankUI.Label(new Rect(i.xMax - amtW, y, amtW, 20f), amtText, amt.Micro >= 0 ? BankUI.Good : BankUI.Bad, GameFont.Tiny, TextAnchor.UpperRight, false);
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
            BankUI.LabelCut(new Rect(r.x + 70f, r.y + 6f, r.width - 360f, 22f), b.Name, BankUI.Brass);
            BankUI.LabelCut(new Rect(r.x + 70f, r.y + 28f, r.width - 360f, 22f), "\"" + b.Slogan + "\"", BankUI.CreamDim, GameFont.Tiny);
            TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width - 330f, r.height), b.description + "\n\n" + string.Join("\n", b.perks.Select(p => "• " + p)));
            // F19: direct deposit and trade deposit are terms: shown here, changed in the mod settings only
            string onOff(bool v) => v ? L.T("Lobby.On", "on") : L.T("Lobby.Off", "off");
            var cb = new Rect(r.xMax - 330f, r.y + 8f, 190f, 24f);
            BankUI.LabelCut(cb, L.T("Lobby.DirectState", "Direct deposit: {0}", onOff(c.DirectDepositOn)), c.DirectDepositOn ? BankUI.Good : BankUI.CreamDim, GameFont.Small);
            TooltipHandler.TipRegion(cb, L.T("Lobby.DirectTip", "Tills of your businesses are emptied into the account at midnight, minus {0} %. Change it in the mod settings (Bank > Account).", (HSettings.Get("bank.directDepositFee") * 100).ToString("0.##", CultureInfo.InvariantCulture)));
            bool trade = HSettings.GetBool("bank.tradeDeposit") && c.DirectDepositOn;
            var tb = new Rect(r.xMax - 330f, r.y + 38f, 190f, 24f);
            BankUI.LabelCut(tb, L.T("Lobby.TradeState", "Trade silver to account: {0}", onOff(trade)), trade ? BankUI.Good : BankUI.CreamDim, GameFont.Small);
            TooltipHandler.TipRegion(tb, L.T("Lobby.TradeTip", "Silver you get from traders and trade ships goes into the account right after the deal, at the same fee as direct deposit, instead of lying on the floor as coins. Needs direct deposit. Off by default, so you can still pay the next trader in cash. Caravans are not affected. Change it in the mod settings (Bank > Account)."));
            if (Widgets.ButtonText(new Rect(r.xMax - 130f, r.y + 8f, 120f, 30f), L.T("Lobby.ChangeBank", "Change bank"))) Find.WindowStack.Add(new Window_ChooseBank());
            var sb = new Rect(r.xMax - 130f, r.y + 42f, 120f, 24f);
            if (Widgets.ButtonText(sb, L.T("Lobby.Settings", "Settings"))) { BankSound.Play(BankSound.Click); HearthMod.OpenSettings("mod:Bank"); }
            TooltipHandler.TipRegion(sb, L.T("Terms.OpenSettingsTip", "Opens Mod Options for Hearth & Harbor on the Bank page. Profitability, fees, loans and the lottery are real settings of this save game, set there and nowhere else."));
        }
    }

    // ==================================================================================================
    /// <summary>Reads the bank's accounts out of the shared ledger (the transaction history is the ledger, KONZEPT 8.1).</summary>
    public static class BankLedgerView
    {
        public static bool Involves(Ledger.Entry e) => (e.From != null && e.From.StartsWith("bank:")) || (e.To != null && e.To.StartsWith("bank:"));

        /// <summary>Newest first.</summary>
        public static List<Ledger.Entry> Entries(int max)
        {
            var led = Ledger.Current; var res = new List<Ledger.Entry>(); if (led == null) return res;
            var j = led.Journal;
            for (int i = j.Count - 1; i >= 0 && res.Count < max; i--)
            {
                var e = j[i];
                if (!Involves(e)) continue;
                bool touchesColony = e.From == BankGameComp.Colony || e.To == BankGameComp.Colony;
                if (!touchesColony && !(e.From == BankGameComp.LoansAccount || e.To == BankGameComp.LoansAccount)) continue;
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
            var entries = BankLedgerView.Entries(400);
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

        static Vector2 pageScroll;
        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; var w = Window_Bank.Active; if (c == null || w == null) return;
            BankUI.ScrollPage(r, ref pageScroll, 400f, v => DrawIn(v, c));
        }

        static void DrawIn(Rect r, BankGameComp c)
        {
            float leftW = Math.Max(400f, r.width * 0.42f);
            float topH = 204f + Mathf.Clamp(r.height - 420f, 0f, 40f);
            var rA = new Rect(r.x, r.y, leftW, topH);
            var rB = new Rect(r.x + leftW + 10f, r.y, r.width - leftW - 10f, topH);
            var rC = new Rect(r.x, r.y + topH + 8f, r.width, Math.Max(140f, r.height - topH - 8f));
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
            int clamped = Math.Max(BankGameComp.MinLoanCoins, Math.Min(amount, max));
            if (clamped != amount) { amount = clamped; buf = amount.ToString(CultureInfo.InvariantCulture); }
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
                if (c.TryTakeLoan(amount, term, out string w2, out _)) BankSound.Play(BankSound.Stamp); else { BankSound.Play(BankSound.Reject); Messages.Message(w2, MessageTypeDefOf.RejectInput, false); }
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
                    if (c.RepayLoan(l, amt100, out string why)) BankSound.Play(BankSound.CoinsIn); else Messages.Message(why, MessageTypeDefOf.RejectInput, false);
                if (c.Loans.Contains(l) && BankUI.Button(new Rect(bx + 126f, row.y + 12f, 130f, 30f), L.T("Loans.RepayAll", "Repay all"), c.Balance >= l.Balance, L.T("Reason.Funds2", "The account does not hold {0}.", l.Balance.ToStringShort())))
                    if (c.RepayLoan(l, l.Balance, out string why2)) BankSound.Play(BankSound.Stamp); else Messages.Message(why2, MessageTypeDefOf.RejectInput, false);
                y += rowH + 4f;
            }
            Widgets.EndScrollView();
        }
    }

    // ==================================================================================================
    static class TermsPage
    {
        static Vector2 scroll;
        const float MinH = 540f;
        public static void Draw(Rect r)
        {
            var c = BankGameComp.Current; if (c == null) return;
            BankUI.ScrollPage(r, ref scroll, MinH, v => DrawIn(v, c));
        }

        static void DrawIn(Rect r, BankGameComp c)
        {
            float rest = r.height - 150f - 152f - 20f;
            var rA = new Rect(r.x, r.y, r.width, 150f);
            var rB = new Rect(r.x, rA.yMax + 10f, r.width, 152f);
            var rC = new Rect(r.x, rB.yMax + 10f, Math.Max(430f, r.width * 0.46f), Math.Min(300f, Math.Max(214f, rest)));
            var rD = new Rect(rC.xMax + 10f, rC.y, r.width - rC.width - 10f, rC.height);
            Profit(rA, c); Compare(rB, c); Fees(rC, c); Tiers(rD, c);
        }

        static string Num(double v, string f = "0.##") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>The terms are real mod settings (F19): this page shows them and points to the window where they are changed.</summary>
        public static void OpenSettings() => HearthMod.OpenSettings("mod:Bank");

        static void Profit(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Terms.Profit", "Profitability"));
            double m = BankSettings.ProfitM, mInv = FinanceSettings.M, share = BankSettings.AccountShare, mod = c.Bank?.interestMod ?? 1.0;
            float btnW = 210f;
            string head = L.T("Terms.MNow", "M = {0} per game year", Num(m, "0.###")) + (Math.Abs(m - mInv) > 1e-6 ? "   " + L.T("Terms.MInv", "(investments: M = {0})", Num(mInv, "0.###")) : "");
            BankUI.Label(new Rect(i.x, i.y, i.width - btnW - 10f, 26f), head, BankUI.Brass, GameFont.Medium, wrap: false);
            if (BankUI.Button(new Rect(i.xMax - btnW, i.y, btnW, 30f), L.T("Terms.OpenSettings", "Open the mod settings")))
            { BankSound.Play(BankSound.Click); OpenSettings(); }
            TooltipHandler.TipRegion(new Rect(i.xMax - btnW, i.y, btnW, 30f), L.T("Terms.OpenSettingsTip", "Opens Mod Options for Hearth & Harbor on the Bank page. Profitability, fees, loans and the lottery are real settings of this save game, set there and nowhere else."));
            string note = L.T("Terms.SetIn", "The terms of the bank are settings of this save game: change them in Mod Options > Hearth & Harbor > Bank (Interest & profitability, Fees, Loans, Collections, Lottery). 1.0 is plus/minus nothing, 1.2 is +20 % a year for the index; the account only gets a share of that. Loans follow M too.");
            Text.Font = GameFont.Tiny; float nh = Text.CalcHeight(note, i.width - btnW - 10f); Text.Font = GameFont.Small;
            BankUI.Label(new Rect(i.x, i.y + 28f, i.width - btnW - 10f, Math.Min(nh + 2f, 46f)), note, BankUI.CreamDim, GameFont.Tiny);
            float y = i.y + Math.Max(34f + Math.Min(nh, 46f), 62f);
            double daily = FinanceMath.DailyFactor(m) - 1, accM = BankSettings.AccountMultiple(mod), accPct = (accM - 1.0) * 100.0;
            float cw = (i.width - 60f) / 3f;
            BankUI.Row(new Rect(i.x, y, cw, 22f), L.T("Terms.Daily", "Per day (index)"), (daily * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " %", L.T("Terms.DailyTip", "M^(1/60) − 1. A game year has 60 days."));
            BankUI.Row(new Rect(i.x + cw + 30f, y, cw, 22f), L.T("Terms.AccountYear", "Your account per year"), YieldCompare.Pct(accPct),
                L.T("Terms.AccountYearTip", "Account multiple per year = 1 + share × bank factor × (M − 1) = 1 + {0} × {1} × ({2} − 1) = {3}, never below {4} or above {5}, and never above the savings product Mattress Plus ({6}).", Num(share), Num(mod), Num(m, "0.###"), Num(accM, "0.####"), Num(FinanceMath.AccountFloor), Num(FinanceMath.AccountCap), Num(BankSettings.AccountCeiling, "0.####")));
            BankUI.Row(new Rect(i.x + 2 * (cw + 30f), y, cw, 22f), L.T("Terms.AccountDaily", "Your account per day"), ((FinanceMath.DailyFactor(accM) - 1) * 100).ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " %",
                L.T("Terms.AccountDailyTip", "Daily factor = (yearly multiple)^(1/60) = {0}.", Num(FinanceMath.DailyFactor(accM), "0.000000")));
            y += 24f;
            BankUI.LabelCut(new Rect(i.x, y, i.width, 20f), L.T("Terms.Preview", "1,000 silver at the index become {0} after one game year and {1} after ten. In your account: {2} and {3}.", FinanceMath.GrowthAfterYears(1000, m, 1).ToString("#,##0", CultureInfo.InvariantCulture), FinanceMath.GrowthAfterYears(1000, m, 10).ToString("#,##0", CultureInfo.InvariantCulture), FinanceMath.GrowthAfterYears(1000, accM, 1).ToString("#,##0", CultureInfo.InvariantCulture), FinanceMath.GrowthAfterYears(1000, accM, 10).ToString("#,##0", CultureInfo.InvariantCulture)), BankUI.Cream, GameFont.Small);
        }

        static void Compare(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Terms.Compare", "Where money earns more (per game year, on average)"));
            var rows = YieldCompare.Rows(c.Bank);
            double max = Math.Max(1.0, rows.Max(x => x.YearlyPercent));
            float y = i.y; float rh = Math.Min(22f, i.height / Math.Max(1, rows.Count));
            foreach (var row in rows)
            {
                BankUI.BarRow(new Rect(i.x, y, i.width, rh), row.Label, YieldCompare.Pct(row.YearlyPercent), row.YearlyPercent, max, row.IsAccount ? BankUI.Brass : BankUI.GreenLight,
                    row.IsAccount ? L.T("Terms.CompareAccountTip", "The account is safe and pays little. Mattress Plus, the bond and everything below it carry more risk or lock your money and pay more on average; the account itself never pays more than Mattress Plus.") : L.T("Terms.CompareTip", "Long-run average of this product at the current profitability. Prices swing; a single year can be far above or below."));
                y += rh;
            }
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
            float bh = 28f;
            BankUI.Label(new Rect(i.x, y, i.width, Math.Max(18f, i.yMax - y - bh - 4f)), L.T("Terms.FeesNote", "All fees scale with the \"bank fee level\" setting ({0} %).", (f * 100).ToString("0", CultureInfo.InvariantCulture)), BankUI.CreamDim, GameFont.Tiny);
            if (Widgets.ButtonText(new Rect(i.x, i.yMax - bh, Math.Min(i.width, 210f), bh), L.T("Terms.OpenSettingsShort", "Change in the settings"))) { BankSound.Play(BankSound.Click); OpenSettings(); }
        }

        static void Tiers(Rect r, BankGameComp c)
        {
            var i = BankUI.Card(r, L.T("Terms.Anchors", "By difficulty"));
            float y = i.y; float tier = BankSettings.Tier; int near = (int)Math.Round(tier);
            double mod = c.Bank?.interestMod ?? 1.0, share = BankSettings.AccountShare;
            float c1 = i.width * 0.40f, c2 = i.width * 0.17f, c3 = i.width * 0.25f;
            BankUI.Label(new Rect(i.x, y, i.width, 20f), L.T("Terms.AnchorsHead", "Index M per year  /  your account a year  /  highest collection stage"), BankUI.CreamDim, GameFont.Tiny, wrap: false); y += 20f;
            for (int k = 0; k < 7; k++)
            {
                var row = new Rect(i.x, y, i.width, 22f);
                var col = k == near ? BankUI.Cream : BankUI.CreamDim;
                if (k == near) Widgets.DrawBoxSolid(row, BankUI.Green);
                BankUI.LabelCut(new Rect(row.x + 4f, row.y, c1 - 6f, 22f), L.T("Tier.Name" + k, HearthProfile.TierNamesEn[k]), col, GameFont.Tiny);
                BankUI.Label(new Rect(row.x + c1, row.y, c2, 22f), BankSettings.ProfitAnchors[k].ToString("0.##", CultureInfo.InvariantCulture), col, GameFont.Tiny, TextAnchor.UpperRight, false);
                BankUI.Label(new Rect(row.x + c1 + c2, row.y, c3, 22f), YieldCompare.Pct(FinanceMath.AccountYearlyPercent(BankSettings.ProfitAnchors[k], share, mod)), col, GameFont.Tiny, TextAnchor.UpperRight, false);
                BankUI.Label(new Rect(row.xMax - (i.width - c1 - c2 - c3) , row.y, i.width - c1 - c2 - c3 - 4f, 22f), ((int)BankSettings.CollectionStageAnchors[k]).ToString(CultureInfo.InvariantCulture), col, GameFont.Tiny, TextAnchor.UpperRight, false);
                y += 22f;
            }
            y += 6f;
            BankUI.Label(new Rect(i.x, y, i.width, i.yMax - y), L.T("Terms.StageNote", "Collection ladder: 1 reminder, 2 late fee, 3 offset against deposits, 4 friendly visit, 5 seizure team, 6 collection raid, 7 bounty. Now: stops at stage {0}.", BankSettings.MaxCollectionStage), BankUI.CreamDim, GameFont.Tiny);
        }
    }
}
