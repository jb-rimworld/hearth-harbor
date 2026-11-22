// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hearth;
using Hearth.Markets;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>The "Lottery" tab of the bank window (KONZEPT 8.14): the global draw with tickets, and the colony's own lottery.</summary>
    public static class LotteryTab
    {
        static readonly HashSet<int> picked = new HashSet<int>(); static int pickedStar;
        static string message; static int messageTick; static Vector2 scroll, scrollColony; static float globalH = 480f, colonyH = 480f;

        public static void Register()
        {
            BankTabs.Register(new BankTab { Id = "lottery", LabelKey = "HH.Finance.Tab.Lottery", LabelEn = "Lottery", Order = 55, Draw = r => Guard.Run(ModuleId.Bank, "LotteryTab.Draw", () => Draw(r)), Visible = () => Modules.On(ModuleId.Bank) && FinanceAccounts.Available && (HSettings.GetBool("lottery.globalOn") || HSettings.GetBool("lottery.colonyOn")) });
        }

        static void Msg(string m) { message = m; messageTick = Find.TickManager.TicksGame; }

        public static int DrawCalls;

        public static void Draw(Rect rect)
        {
            DrawCalls++;
            var lot = LotteryGame.Current; if (lot == null) return; lot.EnsureInit();
            if (!lot.initialized) return;
            var body = new Rect(rect.x, rect.y, rect.width, rect.height - 20f);
            float leftW = body.width * 0.56f;
            var gl = new Rect(body.x, body.y, leftW, body.height);
            var gv = new Rect(0, 0, gl.width - 18f, Math.Max(globalH, gl.height));
            Widgets.BeginScrollView(gl, ref scroll, gv); globalH = DrawGlobal(gv, lot) + 8f; Widgets.EndScrollView();
            var cl = new Rect(body.x + leftW + 10f, body.y, body.width - leftW - 10f, body.height);
            var cv = new Rect(0, 0, cl.width - 18f, Math.Max(colonyH, cl.height));
            Widgets.BeginScrollView(cl, ref scrollColony, cv); colonyH = DrawColony(cv, lot) + 8f; Widgets.EndScrollView();
            FinUi.HelpHint(new Rect(rect.x, rect.yMax - 16f, rect.width, 16f));
        }

        static float DrawGlobal(Rect r, LotteryGame lot)
        {
            float y = r.y; int t = Find.TickManager.TicksGame;
            FinUi.Label(new Rect(r.x, y, r.width, 28), FT.T("HH.Finance.Lottery.GlobalTitle"), GameFont.Medium); y += 30;
            FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Lottery.GlobalTag"), GameFont.Small, FinUi.Dim); y += 22;
            if (!HSettings.GetBool("lottery.globalOn")) { FinUi.Label(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Trade.Off"), GameFont.Small, FinUi.Dim); return y + 24; }
            var price = LotteryGame.GlobalPrice;
            FinUi.Label(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Lottery.Jackpot", FT.Silver(lot.GlobalJackpot)), GameFont.Medium, new Color(1f, 0.88f, 0.4f)); y += 28;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.NextDraw", FinUi.DaysLeft(lot.nextGlobalDrawTick, t), FT.Silver(price), (LotteryGame.GlobalQuote * 100).ToString("0")), GameFont.Small); y += 22;
            string tax = FT.T("HH.Finance.Lottery.Tax", FT.Silver(price * LotteryGame.GlobalQuote), FT.Silver(price), (LotteryMath.JackpotOdds).ToString("#,##0", CultureInfo.InvariantCulture));
            float th = FinUi.TextHeight(r.width, tax, GameFont.Tiny); FinUi.Label(new Rect(r.x, y, r.width, th), tax, GameFont.Tiny, FinUi.Dim); y += th + 6;

            // number picker 1..40 and star 1..10
            FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Lottery.Pick"), GameFont.Small); y += 20;
            float cell = Math.Min(30f, (r.width - 4) / 10f); int cols = 10;
            for (int n = 1; n <= 40; n++)
            {
                int cx = (n - 1) % cols, cy = (n - 1) / cols; var c = new Rect(r.x + cx * cell, y + cy * 24f, cell - 2, 22f);
                bool on = picked.Contains(n); if (on) Widgets.DrawBoxSolid(c, new Color(0.9f, 0.7f, 0.2f, 0.55f));
                if (Widgets.ButtonText(c, n.ToString(), !on, true, true)) { if (on) picked.Remove(n); else if (picked.Count < LotteryMath.GlobalPicks) picked.Add(n); }
            }
            y += 4 * 24f + 4f;
            FinUi.Label(new Rect(r.x, y + 2, 40, 22), "★", GameFont.Small, new Color(1f, 0.88f, 0.4f));
            for (int s = 1; s <= 10; s++)
            {
                var c = new Rect(r.x + 24 + (s - 1) * cell, y, cell - 2, 22f); bool on = pickedStar == s; if (on) Widgets.DrawBoxSolid(c, new Color(0.9f, 0.7f, 0.2f, 0.55f));
                if (Widgets.ButtonText(c, s.ToString(), !on, true, true)) pickedStar = on ? 0 : s;
            }
            y += 28f;
            bool complete = picked.Count == LotteryMath.GlobalPicks && pickedStar > 0;
            if (FinUi.Button(new Rect(r.x, y, 150, 26), FT.T("HH.Finance.Lottery.BuyPicked"), complete)) { var res = lot.BuyGlobalTicket(picked.ToArray(), pickedStar); Msg(res.Message); if (res.Ok) { picked.Clear(); pickedStar = 0; } }
            if (FinUi.Button(new Rect(r.x + 156, y, 100, 26), FT.T("HH.Finance.Lottery.Quick1"))) Msg(lot.QuickPick(1).Message);
            if (FinUi.Button(new Rect(r.x + 260, y, 100, 26), FT.T("HH.Finance.Lottery.Quick5"))) Msg(lot.QuickPick(5).Message);
            if (FinUi.Button(new Rect(r.x + 364, y, 100, 26), FT.T("HH.Finance.Lottery.Quick20"))) Msg(lot.QuickPick(20).Message);
            y += 30;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.YourTickets", lot.PlayerTicketsForNextDraw.ToString(), FT.Silver(new Money(lot.totalSpent)), FT.Silver(new Money(lot.totalWon))), GameFont.Small, FinUi.Dim); y += 22;
            if (message != null && t - messageTick < 3000) { FinUi.Label(new Rect(r.x, y, r.width, 36), message, GameFont.Small, new Color(1f, 0.92f, 0.6f)); }
            y += 38;
            // history
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.History"), GameFont.Small); y += 20;
            int shown = 0;
            for (int i = lot.history.Count - 1; i >= 0 && shown < 4; i--)
            {
                var h = lot.history[i]; if (h.colony) continue; shown++;
                string line = FT.T("HH.Finance.Lottery.HistoryRow", (h.index + 1).ToString(), string.Join(" ", h.numbers) + " ★" + h.star, h.jackpotWinners > 0 ? FT.T("HH.Finance.Lottery.JackpotHit") : "—", h.playerTickets.ToString(), FT.Silver(new Money(h.playerPrize)));
                FinUi.Label(new Rect(r.x, y, r.width, 18), line, GameFont.Tiny, h.playerPrize > 0 ? FinUi.Up : FinUi.Dim, TextAnchor.UpperLeft, false); y += 18;
            }
            return y;
        }

        static float DrawColony(Rect r0, LotteryGame lot)
        {
            Widgets.DrawBoxSolid(r0, FinUi.Panel);
            var r = r0.ContractedBy(8f); float y = r.y;
            FinUi.Label(new Rect(r.x, y, r.width, 26), FT.T("HH.Finance.Lottery.ColonyTitle"), GameFont.Medium); y += 30;
            bool on = lot.ColonyOpen;
            bool want = on; Widgets.CheckboxLabeled(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Lottery.ColonyOpen"), ref want);
            if (want != on) HSettings.Set("lottery.colonyOn", want ? 1 : 0);
            y += 28;
            string intro = FT.T("HH.Finance.Lottery.ColonyIntro"); float ih = FinUi.TextHeight(r.width, intro, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y, r.width, ih), intro, GameFont.Tiny, FinUi.Dim); y += ih + 6;
            y = Stepper(r, y, "lottery.colonyPrice", FT.T("HH.Finance.Lottery.Price"), 1, 1, 50, v => FT.Silver(Money.FromSilver(v)));
            y = Stepper(r, y, "lottery.colonyQuote", FT.T("HH.Finance.Lottery.Quote"), 0.05, 0.2, 0.95, v => (v * 100).ToString("0") + " %");
            y = Stepper(r, y, "lottery.colonyInterval", FT.T("HH.Finance.Lottery.Interval"), 1, 1, 30, v => v.ToString("0") + " d");
            y = Stepper(r, y, "lottery.colonyFee", FT.T("HH.Finance.Lottery.Fee"), 0.5, 0, 20, v => v.ToString("0.0") + " silver");
            y += 4;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.Pot", FT.Silver(lot.ColonyPot), FT.Silver(lot.ColonyJackpot)), GameFont.Small); y += 22;
            double guests, intl; double perDay = lot.ColonyDemandPerDay(out guests, out intl);
            string dem = FT.T("HH.Finance.Lottery.Demand", (lot.ColonyQuote * 100).ToString("0"), (LotteryGame.GlobalQuote * 100).ToString("0"), perDay.ToString("#,##0", CultureInfo.InvariantCulture), Math.Round(guests).ToString("0"), Math.Round(intl).ToString("0"));
            float dh = FinUi.TextHeight(r.width, dem, GameFont.Small); FinUi.Label(new Rect(r.x, y, r.width, dh), dem, GameFont.Small, FinUi.Dim); y += dh + 4;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.Earned", FT.Silver(new Money(lot.colonyEarned))), GameFont.Small, FinUi.Up); y += 22;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.ColonyNext", on ? FinUi.DaysLeft(lot.nextColonyDrawTick, Find.TickManager.TicksGame) : "—"), GameFont.Small, FinUi.Dim); y += 22;
            var last = lot.history.LastOrDefault(h => h.colony);
            if (last != null) FinUi.Label(new Rect(r.x, y, r.width, 36), FT.T("HH.Finance.Lottery.ColonyLast", string.Join(" ", last.numbers), last.jackpotWinners.ToString(), last.crowdTickets.ToString()), GameFont.Tiny, FinUi.Dim);
            y += 38;
            string model = FT.T("HH.Finance.Lottery.PotModel"); float mh = FinUi.TextHeight(r.width, model, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y, r.width, mh), model, GameFont.Tiny, FinUi.Dim); y += mh;
            return y + 8f;
        }

        static float Stepper(Rect r, float y, string key, string label, double step, double min, double max, Func<double, string> fmt)
        {
            double v = HSettings.Get(key);
            FinUi.Label(new Rect(r.x, y + 2, r.width * 0.46f, 22), label, GameFont.Small, FinUi.Dim, TextAnchor.UpperLeft, false);
            if (Widgets.ButtonText(new Rect(r.x + r.width * 0.5f, y, 26, 24), "−")) HSettings.Set(key, (float)Math.Max(min, v - step));
            FinUi.Label(new Rect(r.x + r.width * 0.5f + 28, y + 2, r.width * 0.5f - 60, 22), fmt(v), GameFont.Small, Color.white, TextAnchor.UpperCenter, false);
            if (Widgets.ButtonText(new Rect(r.xMax - 26, y, 26, 24), "+")) HSettings.Set(key, (float)Math.Min(max, v + step));
            return y + 27;
        }
    }
}
