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
    /// <summary>
    /// The "Lottery" tab of the bank window (KONZEPT 8.14): the global draw with tickets, and the colony's own lottery.
    /// 1.1.0 (F20): a header with the big ticket count and what it cost, number balls, a quantity field for quick picks, the list of every ticket
    /// of the draw, and a review of the last draw with the matching numbers marked. All terms of the colony lottery are real settings (F19).
    /// </summary>
    public static class LotteryTab
    {
        static readonly HashSet<int> picked = new HashSet<int>(); static int pickedStar;
        static Vector2 scroll, scrollColony, scrollMine, scrollLast; static float globalH = 700f, colonyH = 480f;
        static int qty = 5; static string qtyBuf = "5";
        const int MaxQty = 200;
        static string flash; static float flashUntil;
        static double shownCount = double.NaN;

        public static void Register()
        {
            BankTabs.Register(new BankTab { Id = "lottery", LabelKey = "HH.Finance.Tab.Lottery", LabelEn = "Lottery", Order = 55, Draw = r => Guard.Run(ModuleId.Bank, "LotteryTab.Draw", () => Draw(r)), Visible = () => Modules.On(ModuleId.Bank) && FinanceAccounts.Available && (HSettings.GetBool("lottery.globalOn") || HSettings.GetBool("lottery.colonyOn")) });
        }

        static void Flash(string m) { flash = m; flashUntil = Time.realtimeSinceStartup + 3.5f; }

        public static int DrawCalls;
        /// <summary>Tests: the number of tickets the player has picked so far in the builder, and a way to set them.</summary>
        public static int PickedCount => picked.Count;
        public static void TestReset() { picked.Clear(); pickedStar = 0; qty = 5; qtyBuf = "5"; flash = null; shownCount = double.NaN; }

        public static void Draw(Rect rect)
        {
            DrawCalls++;
            var lot = LotteryGame.Current; if (lot == null) return; lot.EnsureInit();
            if (!lot.initialized) return;
            var body = new Rect(rect.x, rect.y, rect.width, rect.height - 20f);
            float leftW = body.width * 0.60f;
            var gl = new Rect(body.x, body.y, leftW, body.height);
            var gv = new Rect(0, 0, gl.width - 18f, Math.Max(globalH, gl.height));
            Widgets.BeginScrollView(gl, ref scroll, gv); globalH = DrawGlobal(gv, lot) + 8f; Widgets.EndScrollView();
            var cl = new Rect(body.x + leftW + 10f, body.y, body.width - leftW - 10f, body.height);
            var cv = new Rect(0, 0, cl.width - 18f, Math.Max(colonyH, cl.height));
            Widgets.BeginScrollView(cl, ref scrollColony, cv); colonyH = DrawColony(cv, lot) + 8f; Widgets.EndScrollView();
            FinUi.HelpHint(new Rect(rect.x, rect.yMax - 16f, rect.width, 16f));
        }

        // ---- small drawing helpers -----------------------------------------------------------------------------------------
        static readonly Color Gold = new Color(1f, 0.88f, 0.4f);

        /// <summary>A ball with a number. state: 0 off, 1 picked, 2 matched. Star balls use the red star when on.</summary>
        static void Ball(Rect r, int number, int state, bool star = false)
        {
            string tex = state == 2 ? "Ball_Hit" : state == 1 ? (star ? "Ball_Star" : "Ball_On") : "Ball_Off";
            var t = BankUI.Tex(tex);
            if (t != null) GUI.DrawTexture(r, t); else Widgets.DrawBoxSolid(r, state == 0 ? BankUI.WalnutLight : BankUI.Brass);
            Color col = state == 0 ? BankUI.CreamDim : (star && state == 1 ? new Color(0.45f, 0.1f, 0.08f) : new Color(0.12f, 0.08f, 0.04f));
            BankUI.Label(r, number.ToString(CultureInfo.InvariantCulture), col, r.height >= 26f ? GameFont.Small : GameFont.Tiny, TextAnchor.MiddleCenter, false);
        }

        /// <summary>One ticket as chips. matchWith = the winning numbers (marks the hits), or null.</summary>
        static void TicketRow(Rect row, int[] nums, int star, int[] winning, int winStar)
        {
            float cs = Math.Min(24f, row.height - 2f); float x = row.x;
            foreach (int n in nums.OrderBy(v => v))
            {
                bool hit = winning != null && winning.Contains(n);
                Ball(new Rect(x, row.y + (row.height - cs) / 2f, cs, cs), n, hit ? 2 : (winning != null ? 0 : 1)); x += cs + 2f;
            }
            x += 6f;
            bool starHit = winning != null && star == winStar;
            Ball(new Rect(x, row.y + (row.height - cs) / 2f, cs, cs), star, starHit ? 2 : (winning != null ? 0 : 1), true);
        }

        static float Header(Rect r, ref float y, string text, GameFont font, Color col)
        {
            Text.Font = font; float h = Text.CalcHeight(text, r.width); Text.Font = GameFont.Small;
            FinUi.Label(new Rect(r.x, y, r.width, h), text, font, col); y += h; return h;
        }

        static float DrawGlobal(Rect r, LotteryGame lot)
        {
            float y = r.y; int t = Find.TickManager.TicksGame;
            FinUi.Label(new Rect(r.x, y, r.width, 28), FT.T("HH.Finance.Lottery.GlobalTitle"), GameFont.Medium); y += 30;
            FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Lottery.GlobalTag"), GameFont.Small, FinUi.Dim); y += 22;
            if (!HSettings.GetBool("lottery.globalOn")) { FinUi.Label(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Trade.Off"), GameFont.Small, FinUi.Dim); return y + 24; }
            var price = LotteryGame.GlobalPrice;
            double quote = LotteryGame.GlobalQuote;

            // ---- A: header card ------------------------------------------------------------------------------------------
            int count = lot.PlayerTicketsForNextDraw; var spent = lot.PlayerSpentThisDraw; int free = lot.FreeTicketsThisDraw;
            string tax = FT.T("HH.Finance.Lottery.Expected", FT.Silver(price * quote), FT.Silver(price), (quote * 100).ToString("0"));
            string odds = FT.T("HH.Finance.Lottery.Odds", LotteryMath.JackpotOdds.ToString("#,##0", CultureInfo.InvariantCulture), LotteryMath.AnyPrizeOdds().ToString("#,##0", CultureInfo.InvariantCulture));
            Text.Font = GameFont.Tiny; float taxH = Text.CalcHeight(tax, r.width - 20f), oddsH = Text.CalcHeight(odds, r.width - 20f); Text.Font = GameFont.Small;
            var cardA = new Rect(r.x, y, r.width, 34f + 30f + 24f + 46f + taxH + oddsH + 14f);
            var ia = BankUI.Card(cardA, FT.T("HH.Finance.Lottery.MyCount"));
            if (double.IsNaN(shownCount)) shownCount = count;
            BankUI.Ease(ref shownCount, count);
            float ay = ia.y;
            var ticketTex = BankUI.Tex("Ticket"); if (ticketTex != null) GUI.DrawTexture(new Rect(ia.x, ay, 44f, 44f), ticketTex);
            FinUi.Label(new Rect(ia.x + 52f, ay - 2f, 110f, 46f), Math.Round(shownCount).ToString("0", CultureInfo.InvariantCulture), GameFont.Medium, Gold, TextAnchor.MiddleLeft, false);
            Text.Font = GameFont.Medium; float cw = Text.CalcSize(Math.Round(shownCount).ToString("0", CultureInfo.InvariantCulture)).x; Text.Font = GameFont.Small;
            string spentText = count == 0 ? FT.T("HH.Finance.Lottery.MyNone") : (free > 0 ? FT.T("HH.Finance.Lottery.MySpentFree", FT.Silver(spent), free.ToString()) : FT.T("HH.Finance.Lottery.MySpent", FT.Silver(spent)));
            FinUi.Label(new Rect(ia.x + 60f + cw + 8f, ay + 2f, ia.width - 60f - cw - 8f - 4f, 40f), spentText, GameFont.Small, count == 0 ? FinUi.Dim : Color.white, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(new Rect(ia.x, ay, ia.width, 44f), FT.T("HH.Finance.Lottery.CountTip"));
            if (flash != null && Time.realtimeSinceStartup < flashUntil)
                FinUi.Label(new Rect(ia.x + 52f, ay + 40f, ia.width - 52f, 20f), flash, GameFont.Small, FinUi.Up);
            ay += 62f;
            FinUi.Label(new Rect(ia.x, ay, ia.width * 0.5f, 26f), FT.T("HH.Finance.Lottery.Jackpot", FT.Silver(lot.GlobalJackpot)), GameFont.Medium, Gold, TextAnchor.UpperLeft, false);
            FinUi.Label(new Rect(ia.x + ia.width * 0.5f, ay + 4f, ia.width * 0.5f, 22f), FT.T("HH.Finance.Lottery.NextDraw", FinUi.DaysLeft(lot.nextGlobalDrawTick, t), FT.Silver(price), (quote * 100).ToString("0")), GameFont.Small, FinUi.Dim, TextAnchor.UpperRight, false);
            ay += 30f;
            FinUi.Label(new Rect(ia.x, ay, ia.width, oddsH), odds, GameFont.Tiny, FinUi.Dim); ay += oddsH;
            FinUi.Label(new Rect(ia.x, ay, ia.width, taxH), tax, GameFont.Tiny, FinUi.Dim);
            y = cardA.yMax + 8f;

            // ---- B: buy card ---------------------------------------------------------------------------------------------
            float cell = Math.Min(34f, (r.width - 24f) / 11f);
            float bh = 34f + 20f + 4 * (cell + 2f) + 6f + (cell + 2f) + 10f + 22f + 36f + 36f + 8f;
            var cardB = new Rect(r.x, y, r.width, bh);
            var ib = BankUI.Card(cardB, FT.T("HH.Finance.Lottery.BuyCard"));
            float by = ib.y;
            FinUi.Label(new Rect(ib.x, by, ib.width, 18f), FT.T("HH.Finance.Lottery.Pick"), GameFont.Small, FinUi.Dim); by += 20f;
            for (int n = 1; n <= 40; n++)
            {
                int cx = (n - 1) % 10, cy = (n - 1) / 10; var c = new Rect(ib.x + cx * (cell + 2f), by + cy * (cell + 2f), cell, cell);
                bool on = picked.Contains(n);
                Ball(c, n, on ? 1 : 0); Widgets.DrawHighlightIfMouseover(c);
                if (Widgets.ButtonInvisible(c)) { if (on) picked.Remove(n); else if (picked.Count < LotteryMath.GlobalPicks) picked.Add(n); BankSound.Play(BankSound.Ball); }
            }
            by += 4 * (cell + 2f) + 6f;
            FinUi.Label(new Rect(ib.x, by + cell / 2f - 11f, 24f, 22f), "★", GameFont.Medium, Gold, TextAnchor.MiddleLeft, false);
            for (int s = 1; s <= 10; s++)
            {
                var c = new Rect(ib.x + 24f + (s - 1) * (cell + 2f), by, cell, cell); bool on = pickedStar == s;
                Ball(c, s, on ? 1 : 0, true); Widgets.DrawHighlightIfMouseover(c);
                if (Widgets.ButtonInvisible(c)) { pickedStar = on ? 0 : s; BankSound.Play(BankSound.Ball); }
            }
            by += cell + 2f + 6f;
            FinUi.Label(new Rect(ib.x, by, ib.width, 20f), FT.T("HH.Finance.Lottery.Preview", picked.Count.ToString(), LotteryMath.GlobalPicks.ToString(), pickedStar > 0 ? "★" + pickedStar : FT.T("HH.Finance.Lottery.StarNone")), GameFont.Small, picked.Count == LotteryMath.GlobalPicks && pickedStar > 0 ? FinUi.Up : FinUi.Dim);
            by += 24f;
            bool complete = picked.Count == LotteryMath.GlobalPicks && pickedStar > 0;
            bool canAffordOne = FinanceAccounts.Cash >= price;
            if (FinUi.Button(new Rect(ib.x, by, 190f, 28f), FT.T("HH.Finance.Lottery.BuyTicket", FT.Silver(price)), complete && canAffordOne, !canAffordOne ? FT.T("HH.Finance.Lottery.NotEnough", FT.Silver(price)) : null))
            {
                var res = lot.BuyGlobalTicket(picked.ToArray(), pickedStar);
                if (res.Ok) { picked.Clear(); pickedStar = 0; BankSound.Play(BankSound.Ticket); Flash(res.Money.Micro > 0 ? FT.T("HH.Finance.Lottery.Flash", "1", FT.Silver(res.Money)) : FT.T("HH.Finance.Lottery.FlashFree", "1", "1")); }
                else { BankSound.Play(BankSound.Reject); Flash(res.Message); }
            }
            if (Widgets.ButtonText(new Rect(ib.x + 196f, by, 90f, 28f), FT.T("HH.Finance.Lottery.Clear"), complete || picked.Count > 0 || pickedStar > 0)) { picked.Clear(); pickedStar = 0; BankSound.Play(BankSound.Tick); }
            by += 36f;
            // quick picks: a number and one button
            FinUi.Label(new Rect(ib.x, by + 3f, 110f, 22f), FT.T("HH.Finance.Lottery.QuickN"), GameFont.Small, FinUi.Dim, TextAnchor.UpperLeft, false);
            if (Widgets.ButtonText(new Rect(ib.x + 112f, by, 26f, 26f), "−")) { SetQty(qty - 1); BankSound.Play(BankSound.Tick); }
            int q2 = qty; Widgets.TextFieldNumeric(new Rect(ib.x + 142f, by, 60f, 26f), ref q2, ref qtyBuf, 1f, MaxQty); if (q2 != qty) qty = Math.Max(1, Math.Min(MaxQty, q2));
            if (Widgets.ButtonText(new Rect(ib.x + 206f, by, 26f, 26f), "+")) { SetQty(qty + 1); BankSound.Play(BankSound.Tick); }
            var cost = price * qty; bool afford = FinanceAccounts.Cash >= cost;
            if (FinUi.Button(new Rect(ib.x + 240f, by, Math.Max(120f, Math.Min(220f, ib.width - 240f)), 28f), FT.T("HH.Finance.Lottery.BuyN", qty.ToString(), FT.Silver(cost)), afford, !afford ? FT.T("HH.Finance.Lottery.NotEnough", FT.Silver(cost)) : FT.T("HH.Finance.Lottery.QuickTip", MaxQty.ToString())))
            {
                int before = lot.PlayerTicketsForNextDraw; var spent0 = lot.PlayerSpentThisDraw;
                var res = lot.QuickPick(qty);
                int got = lot.PlayerTicketsForNextDraw - before;
                if (res.Ok) { BankSound.Play(qty > 3 ? BankSound.Ratchet : BankSound.Ticket); Flash(FT.T("HH.Finance.Lottery.Flash", got.ToString(), FT.Silver(lot.PlayerSpentThisDraw - spent0))); }
                else { BankSound.Play(BankSound.Reject); Flash(res.Message); }
            }
            y = cardB.yMax + 8f;

            // ---- C: my tickets -------------------------------------------------------------------------------------------
            var mine = lot.TicketsOfNextDraw();
            var cardC = new Rect(r.x, y, r.width, 34f + 22f + Math.Min(Math.Max(mine.Count, 1), 6) * 28f + 12f);
            var ic = BankUI.Card(cardC, FT.T("HH.Finance.Lottery.CardTickets"));
            FinUi.Label(new Rect(ic.x, ic.y, ic.width, 18f), FT.T("HH.Finance.Lottery.TicketsCount", mine.Count.ToString()), GameFont.Tiny, FinUi.Dim);
            var listR = new Rect(ic.x, ic.y + 22f, ic.width, ic.height - 22f);
            if (mine.Count == 0) FinUi.Label(listR, FT.T("HH.Finance.Lottery.MyNone"), GameFont.Small, FinUi.Dim);
            else
            {
                const float rowH = 28f;
                var view = new Rect(0, 0, listR.width - 16f, mine.Count * rowH);
                Widgets.BeginScrollView(listR, ref scrollMine, view);
                int first = Math.Max(0, (int)(scrollMine.y / rowH) - 1), last = Math.Min(mine.Count - 1, (int)((scrollMine.y + listR.height) / rowH) + 1);
                for (int i = first; i <= last; i++)
                {
                    var tk = mine[i]; var row = new Rect(0, i * rowH, view.width, rowH);
                    if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                    FinUi.Label(new Rect(row.x + 4f, row.y + 4f, 40f, 20f), "#" + (mine.Count - i), GameFont.Tiny, FinUi.Dim, TextAnchor.UpperLeft, false);
                    TicketRow(new Rect(row.x + 44f, row.y, 240f, row.height), tk.Numbers, tk.star, null, 0);
                    if (tk.free) FinUi.Label(new Rect(row.x + 290f, row.y + 6f, 80f, 18f), FT.T("HH.Finance.Lottery.FreeTag"), GameFont.Tiny, FinUi.Up, TextAnchor.UpperLeft, false);
                }
                Widgets.EndScrollView();
            }
            y = cardC.yMax + 8f;

            // ---- D: last draw ---------------------------------------------------------------------------------------------
            var rec = lot.LastGlobalDraw;
            int lines = rec != null ? Math.Min(Math.Max(rec.myTickets.Count, 0), 5) : 0;
            Text.Font = GameFont.Small;
            string lastLine = rec == null ? FT.T("HH.Finance.Lottery.NoLast") : LastDrawLine(rec);
            float llH = Text.CalcHeight(lastLine, r.width - 20f);
            var cardD = new Rect(r.x, y, r.width, 34f + (cell > 0 ? 30f : 0f) + llH + 6f + lines * 28f + 10f);
            var id = BankUI.Card(cardD, rec == null ? FT.T("HH.Finance.Lottery.History") : FT.T("HH.Finance.Lottery.LastDraw", (rec.index + 1).ToString()));
            float dy = id.y;
            if (rec != null)
            {
                TicketRow(new Rect(id.x, dy, 280f, 28f), rec.numbers.ToArray(), rec.star, rec.numbers.ToArray(), rec.star); dy += 30f;
            }
            FinUi.Label(new Rect(id.x, dy, id.width, llH), lastLine, GameFont.Small, rec != null && rec.playerPrize > 0 ? FinUi.Up : Color.white); dy += llH + 6f;
            if (rec != null && lines > 0)
            {
                var winning = rec.numbers.ToArray();
                var viewH = rec.myTickets.Count * 28f;
                var area = new Rect(id.x, dy, id.width, id.yMax - dy);
                var view = new Rect(0, 0, area.width - 16f, viewH);
                Widgets.BeginScrollView(area, ref scrollLast, view);
                int first = Math.Max(0, (int)(scrollLast.y / 28f) - 1), lastI = Math.Min(rec.myTickets.Count - 1, (int)((scrollLast.y + area.height) / 28f) + 1);
                for (int i = first; i <= lastI; i++)
                {
                    int idx = rec.myTickets.Count - 1 - i; // newest first
                    if (!LotteryTicket.TryParse(rec.myTickets[idx], out int[] nums, out int st)) continue;
                    var row = new Rect(0, i * 28f, view.width, 28f);
                    if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                    TicketRow(new Rect(row.x + 4f, row.y, 280f, 28f), nums, st, winning, rec.star);
                    int m = LotteryMath.CountMatches(nums, winning);
                    FinUi.Label(new Rect(row.x + 290f, row.y + 5f, 160f, 18f), FT.T("HH.Finance.Lottery.Matches", m.ToString()) + (st == rec.star ? " ★" : ""), GameFont.Tiny, m >= 2 ? FinUi.Up : FinUi.Dim, TextAnchor.UpperLeft, false);
                }
                Widgets.EndScrollView();
            }
            y = cardD.yMax + 8f;

            // ---- history (4 rows) ------------------------------------------------------------------------------------------
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.History"), GameFont.Small); y += 20;
            int shown = 0;
            for (int i = lot.history.Count - 1; i >= 0 && shown < 4; i--)
            {
                var h = lot.history[i]; if (h.colony) continue; shown++;
                string line = FT.T("HH.Finance.Lottery.HistoryRow", (h.index + 1).ToString(), string.Join(" ", h.numbers) + " ★" + h.star, h.jackpotWinners > 0 ? FT.T("HH.Finance.Lottery.JackpotHit") : "—", h.playerTickets.ToString(), FT.Silver(new Money(h.playerPrize)));
                FinUi.Label(new Rect(r.x, y, r.width, 18), line, GameFont.Tiny, h.playerPrize > 0 ? FinUi.Up : FinUi.Dim, TextAnchor.UpperLeft, false);
                if (h.playerTickets > 0) TooltipHandler.TipRegion(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Lottery.LastDrawLine", string.Join(" ", h.numbers) + " ★" + h.star, h.playerTickets.ToString(), FT.Silver(new Money(h.playerSpent)), BestMatches(h).ToString(), "", FT.Silver(new Money(h.playerPrize))));
                y += 18;
            }
            return y;
        }

        static void SetQty(int v) { qty = Math.Max(1, Math.Min(MaxQty, v)); qtyBuf = qty.ToString(CultureInfo.InvariantCulture); }

        /// <summary>Best match count of a finished draw: stored since 1.1.1 (all tickets); older records only know the kept tickets.</summary>
        static int BestMatches(LotteryDrawRecord rec) { int b; bool st; Best(rec, out b, out st); return b; }

        static void Best(LotteryDrawRecord rec, out int best, out bool starOnBest)
        {
            if (rec.bestMatches >= 0) { best = rec.bestMatches; starOnBest = rec.bestStar; return; }
            best = 0; starOnBest = false; var win = rec.numbers.ToArray();
            foreach (var s in rec.myTickets)
                if (LotteryTicket.TryParse(s, out int[] nums, out int st)) { int m = LotteryMath.CountMatches(nums, win); if (m > best || (m == best && st == rec.star)) { best = m; starOnBest = st == rec.star; } }
        }

        static string LastDrawLine(LotteryDrawRecord rec)
        {
            string numbers = string.Join(" ", rec.numbers) + " ★" + rec.star;
            if (rec.playerTickets == 0) return FT.T("HH.Finance.Lottery.HistoryRow", (rec.index + 1).ToString(), numbers, rec.jackpotWinners > 0 ? FT.T("HH.Finance.Lottery.JackpotHit") : "—", "0", FT.Silver(Money.Zero));
            Best(rec, out int best, out bool starOnBest);
            string line = FT.T("HH.Finance.Lottery.LastDrawLine", numbers, rec.playerTickets.ToString(), FT.Silver(new Money(rec.playerSpent)), best.ToString(), starOnBest ? FT.T("HH.Finance.Lottery.StarMatch") : "", FT.Silver(new Money(rec.playerPrize)));
            if (rec.myTickets.Count < rec.playerTickets) line += FT.T("HH.Finance.Lottery.KeptNote", rec.myTickets.Count.ToString(), rec.playerTickets.ToString());
            return line;
        }

        static float DrawColony(Rect r0, LotteryGame lot)
        {
            Widgets.DrawBoxSolid(r0, BankUI.WalnutLight); GUI.color = BankUI.BrassDim; Widgets.DrawBox(r0, 1); GUI.color = Color.white;
            var r = r0.ContractedBy(8f); float y = r.y;
            FinUi.Label(new Rect(r.x, y, r.width, 26), FT.T("HH.Finance.Lottery.ColonyTitle"), GameFont.Medium); y += 30;
            bool on = lot.ColonyOpen;
            // F19: the switch is a real setting; this window only shows its state
            FinUi.Label(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Lottery.ColonyState", on ? FT.T("HH.Finance.Lottery.StateOpen") : FT.T("HH.Finance.Lottery.StateClosed")), GameFont.Small, on ? BankUI.Good : BankUI.CreamDim);
            y += 28;
            string intro = FT.T("HH.Finance.Lottery.ColonyIntro"); float ih = FinUi.TextHeight(r.width, intro, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y, r.width, ih), intro, GameFont.Tiny, FinUi.Dim); y += ih + 6;
            // terms: shown here, changed in the mod settings (F19)
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.ColonyTerms"), GameFont.Small, Gold); y += 22;
            y = TermRow(r, y, FT.T("HH.Finance.Lottery.Price"), FT.Silver(lot.ColonyPrice));
            y = TermRow(r, y, FT.T("HH.Finance.Lottery.Quote"), (lot.ColonyQuote * 100).ToString("0") + " %");
            y = TermRow(r, y, FT.T("HH.Finance.Lottery.Interval"), FT.T("HH.Finance.Lottery.DaysUnit", lot.ColonyInterval.ToString()));
            y = TermRow(r, y, FT.T("HH.Finance.Lottery.Fee"), FT.T("HH.Finance.Lottery.SilverUnit", lot.ColonyFee.ToString("0.0")));
            string setIn = FT.T("HH.Finance.Lottery.SetIn"); float sh = FinUi.TextHeight(r.width, setIn, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y, r.width, sh), setIn, GameFont.Tiny, FinUi.Dim); y += sh + 2f;
            var btn = new Rect(r.x, y, Math.Min(r.width, 220f), 26f);
            if (Widgets.ButtonText(btn, FT.T("HH.Finance.Lottery.OpenSettings"))) { BankSound.Play(BankSound.Click); HearthMod.OpenSettings("mod:Bank"); }
            TooltipHandler.TipRegion(btn, FT.T("HH.Finance.Lottery.OpenSettingsTip"));
            y += 34f;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.Pot", FT.Silver(lot.ColonyPot), FT.Silver(lot.ColonyJackpot)), GameFont.Small); y += 22;
            string sold = FT.T("HH.Finance.Lottery.ColonySold", lot.colonyTicketsThisDraw.ToString(), FT.Silver(new Money(lot.colonyEarned)));
            float sdh = FinUi.TextHeight(r.width, sold, GameFont.Small); FinUi.Label(new Rect(r.x, y, r.width, sdh), sold, GameFont.Small, FinUi.Up); y += sdh + 4;
            double guests, intl; double perDay = lot.ColonyDemandPerDay(out guests, out intl);
            string dem = FT.T("HH.Finance.Lottery.Demand", (lot.ColonyQuote * 100).ToString("0"), (LotteryGame.GlobalQuote * 100).ToString("0"), perDay.ToString("#,##0", CultureInfo.InvariantCulture), Math.Round(guests).ToString("0"), Math.Round(intl).ToString("0"));
            float dh = FinUi.TextHeight(r.width, dem, GameFont.Small); FinUi.Label(new Rect(r.x, y, r.width, dh), dem, GameFont.Small, FinUi.Dim); y += dh + 4;
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Lottery.ColonyNext", on ? FinUi.DaysLeft(lot.nextColonyDrawTick, Find.TickManager.TicksGame) : "—"), GameFont.Small, FinUi.Dim); y += 22;
            var last = lot.history.LastOrDefault(h => h.colony);
            if (last != null) { string ll = FT.T("HH.Finance.Lottery.ColonyLast", string.Join(" ", last.numbers), last.jackpotWinners.ToString(), last.crowdTickets.ToString()); float lh = FinUi.TextHeight(r.width, ll, GameFont.Tiny); FinUi.Label(new Rect(r.x, y, r.width, lh), ll, GameFont.Tiny, FinUi.Dim); y += lh + 2f; }
            y += 4f;
            string model = FT.T("HH.Finance.Lottery.PotModel"); float mh = FinUi.TextHeight(r.width, model, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y, r.width, mh), model, GameFont.Tiny, FinUi.Dim); y += mh;
            return y + 8f;
        }

        static float TermRow(Rect r, float y, string label, string value)
        {
            FinUi.Label(new Rect(r.x, y + 1, r.width * 0.55f, 22), label, GameFont.Small, FinUi.Dim, TextAnchor.UpperLeft, false);
            FinUi.Label(new Rect(r.x + r.width * 0.45f, y + 1, r.width * 0.55f, 22), value, GameFont.Small, Color.white, TextAnchor.UpperRight, false);
            return y + 24;
        }
    }
}
