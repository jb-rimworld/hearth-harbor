// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.PlayerHouse
{
    [StaticConstructorOnStartup]
    public static class ThrumboIcons
    {
        public static readonly Texture2D Logo = ContentFinder<Texture2D>.Get("UI/HH/Casino/LuckyThrumbo", false) ?? BaseContent.BadTex;
    }

    /// <summary>
    /// The Lucky Thrumbo: the orbital casino of Silverline's world (KONZEPT 6.13). The player gambles with real colony silver against a foreign house:
    /// slots, roulette and blackjack on the exact casino maths, sealed random streams, table limits, a plain history, and the quiet help line (K28).
    /// </summary>
    public class Window_Thrumbo : Window
    {
        readonly Pawn negotiator;
        PayFrom from;
        ThrumboGame game = ThrumboGame.Slots;
        SlotVolatility vol = SlotVolatility.Medium;
        string stakeText = "5";
        RouletteBet bet = RouletteBet.Red();
        ThrumboResult last;
        int lastTick = -99999;
        string notice; Color noticeColor = Color.white;
        int[] reels;
        int lastPocket = -1;
        readonly List<int> pockets = new List<int>();
        Vector2 histScroll;
        public static int DrawCount;

        public override Vector2 InitialSize => new Vector2(780f, 720f);
        public bool ShowsHelpHint => CasinoUI.HelpHintText() != null;
        public ThrumboGame Game { get => game; set => game = value; }

        public Window_Thrumbo(Pawn negotiator)
        {
            this.negotiator = negotiator;
            doCloseX = true; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; preventCameraMotion = false; forcePause = false;
            from = ThrumboSeat.For(negotiator).From;
        }

        public static Window_Thrumbo Open(Pawn negotiator)
        {
            var open = Find.WindowStack.WindowOfType<Window_Thrumbo>();
            if (open != null) { Find.WindowStack.Notify_ManuallySetFocus(open); return open; }
            var w = new Window_Thrumbo(negotiator);
            Find.WindowStack.Add(w);
            return w;
        }

        ThrumboSeat Seat => ThrumboSeat.For(negotiator, from);

#if HH_TESTS
        /// <summary>Showcase/test hooks: drive the window like a click would.</summary>
        public void TestPlay(string what, string stake)
        {
            stakeText = stake; var seat = Seat;
            switch (what)
            {
                case "slots": game = ThrumboGame.Slots; DoSlots(seat); break;
                case "roulette": game = ThrumboGame.Roulette; bet = RouletteBet.Straight(17); DoRoulette(seat); bet = RouletteBet.Dozen(2); break;
                case "deal": game = ThrumboGame.Blackjack; DoDeal(seat); break;
            }
        }
#endif

        // ---------------------------------------------------------------------------------------------------------
        static readonly Color[] symbolColors =
        {
            new Color(0.80f, 0.25f, 0.25f), new Color(0.88f, 0.78f, 0.30f), new Color(0.60f, 0.38f, 0.70f), new Color(0.85f, 0.65f, 0.30f),
            new Color(0.62f, 0.64f, 0.68f), new Color(0.90f, 0.35f, 0.30f), new Color(0.35f, 0.70f, 0.55f)
        };
        static readonly Color dim = new Color(0.72f, 0.72f, 0.72f), good = new Color(0.6f, 0.9f, 0.6f), bad = new Color(1f, 0.58f, 0.48f), accent = new Color(0.95f, 0.82f, 0.45f);

        public override void DoWindowContents(Rect inRect)
        {
            DrawCount++;
            Guard.Run(ModuleId.Casino, "Thrumbo.Draw", () => Draw(inRect));
        }

        void Draw(Rect inRect)
        {
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            try
            {
                var seat = Seat; var hand = ThrumboHouse.CurrentHand();
                float y = DrawHeader(inRect, seat);
                y = DrawSource(inRect, seat, y, hand != null);
                y = DrawGameTabs(inRect, y, hand != null);
                y = DrawStake(inRect, seat, y);
                float gameH = 232f;
                var area = new Rect(0, y, inRect.width, gameH);
                Widgets.DrawBoxSolid(area, new Color(0.10f, 0.13f, 0.11f, 0.55f)); Widgets.DrawBox(area, 1);
                switch (game)
                {
                    case ThrumboGame.Slots: DrawSlots(area.ContractedBy(8f), seat); break;
                    case ThrumboGame.Roulette: DrawRoulette(area.ContractedBy(8f), seat); break;
                    default: DrawBlackjack(area.ContractedBy(8f), seat, hand); break;
                }
                y += gameH + 6f;
                y = DrawResult(inRect, y);
                DrawHistory(new Rect(0, y, inRect.width, inRect.height - y - 24f));
                CasinoUI.DrawHelpHint(new Rect(0, inRect.height - 22f, inRect.width, 20f));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }

        // ---- header ------------------------------------------------------------------------------------------------
        float DrawHeader(Rect r, ThrumboSeat seat)
        {
            var logo = new Rect(0, 0, 480f, 80f);
            GUI.DrawTexture(logo, ThrumboIcons.Logo, ScaleMode.ScaleToFit);
            Widgets.DrawBoxSolid(new Rect(100f, 10f, 376f, 62f), new Color(0.05f, 0.07f, 0.14f, 0.62f));
            Text.Font = GameFont.Medium; GUI.color = new Color(0.97f, 0.93f, 0.82f);
            Widgets.Label(new Rect(106f, 10f, 366f, 30f), "HH.Thrumbo.Title".Translate());
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.82f, 0.85f, 0.95f);
            Widgets.Label(new Rect(106f, 38f, 366f, 34f), "HH.Thrumbo.Tagline".Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            // the edges of the house, the same numbers the games use
            Text.Anchor = TextAnchor.UpperRight;
            double shift = TS.Shift;
            string edge = "HH.Thrumbo.EdgeLine".Translate((ThrumboOdds.SlotRtp(shift) * 100).ToString("0.#"), (ThrumboOdds.RouletteEdge(shift) * 100).ToString("0.##"), (ThrumboOdds.BlackjackEdge(shift) * 100).ToString("0.##"));
            GUI.color = dim; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(490f, 8f, r.width - 490f, 70f), edge);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(new Rect(490f, 8f, r.width - 490f, 70f), "HH.Thrumbo.EdgeTip".Translate((shift * 100).ToString("+0.##;-0.##;0")));
            return 88f;
        }

        // ---- where the money comes from --------------------------------------------------------------------------
        float DrawSource(Rect r, ThrumboSeat seat, float y, bool locked)
        {
            Widgets.Label(new Rect(0, y + 3f, 80f, 24f), "HH.Thrumbo.PayFrom".Translate());
            var bankSeat = new ThrumboSeat(seat.Map, seat.Player, PayFrom.Bank); var silverSeat = new ThrumboSeat(seat.Map, seat.Player, PayFrom.Silver);
            string bankLabel = BankApi.Ready ? "HH.Thrumbo.FromBank".Translate(ThrumboHouse.Funds(bankSeat, true).ToStringShort()).ToString() : "HH.Thrumbo.FromBankNone".Translate().ToString();
            string silverLabel = "HH.Thrumbo.FromSilver".Translate(ThrumboHouse.Funds(silverSeat, true).WholeSilver.ToString("N0")).ToString();
            if (Choice(new Rect(84f, y, 250f, 28f), bankLabel, from == PayFrom.Bank, BankApi.Ready && !locked, "HH.Thrumbo.FromBankTip".Translate())) { from = PayFrom.Bank; Retext(); }
            if (Choice(new Rect(340f, y, 250f, 28f), silverLabel, from == PayFrom.Silver, !locked, "HH.Thrumbo.FromSilverTip".Translate())) { from = PayFrom.Silver; Retext(); }
            if (from == PayFrom.Bank && !BankApi.Ready) from = PayFrom.Silver;
            GUI.color = dim; Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(r.width - 170f, y, 170f, 28f), "HH.Thrumbo.PlayerLine".Translate(seat.Player?.LabelShort ?? "–"));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            return y + 34f;
        }

        void Retext()
        {
            if (from == PayFrom.Silver && TryStake(out var m)) stakeText = Math.Max(1, m.WholeSilver).ToString();
        }

        static bool Choice(Rect r, string label, bool on, bool enabled, string tip)
        {
            if (on) Widgets.DrawBoxSolid(r, new Color(0.25f, 0.45f, 0.30f, 0.55f));
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            bool click;
            if (on)
            {
                // selected: a plain, centred label on the green plate (a background-less button would left-align it like a text field)
                var oa = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.92f, 1f, 0.92f, enabled ? 1f : 0.45f);
                Widgets.Label(r, "<b>" + label + "</b>"); Text.Anchor = oa; click = false;
            }
            else click = Widgets.ButtonText(r, label, true, true, enabled);
            GUI.color = Color.white;
            if (on) Widgets.DrawBox(r, 1);
            if (tip != null) TooltipHandler.TipRegion(r, tip);
            return click && enabled && !on;
        }

        /// <summary>The "Raise the stakes…" button is usable only while no blackjack hand is open.</summary>
        public static bool StakesButtonEnabled(bool handOpen) => ThrumboStakes.Enabled && !handOpen;

        float DrawGameTabs(Rect r, float y, bool locked)
        {
            float x = 0;
            foreach (ThrumboGame g in Enum.GetValues(typeof(ThrumboGame)))
            {
                var rect = new Rect(x, y, 140f, 30f); x += 146f;
                if (Choice(rect, ("HH.Thrumbo.Game." + g).Translate(), game == g, !locked || g == ThrumboGame.Blackjack, locked && g != ThrumboGame.Blackjack ? "HH.Thrumbo.HandOpenTip".Translate() : null)) game = g;
            }
            // "Raise the stakes…" (K15): only while the setting thrumbo.raiseTheStakes is on
            if (ThrumboStakes.Enabled)
            {
                var rs = new Rect(r.width - 190f, y, 190f, 30f);
                if (Choice(rs, "HH.Thrumbo.Stakes.Button".Translate(), false, StakesButtonEnabled(locked), locked ? "HH.Thrumbo.HandOpenTip".Translate().ToString() : "HH.Thrumbo.Stakes.ButtonTip".Translate().ToString()))
                    Window_ThrumboStakes.Open(Seat.Map, from);
            }
            return y + 38f;
        }

        // ---- stake -------------------------------------------------------------------------------------------------
        bool TryStake(out Money m)
        {
            m = Money.Zero;
            if (!double.TryParse((stakeText ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || d <= 0 || d > 1e9) return false;
            if (from == PayFrom.Silver) d = Math.Round(d);
            m = Money.FromSilver(d); return m.Micro > 0;
        }

        void SetStake(Money m) { stakeText = from == PayFrom.Silver || m.Fraction.Micro == 0 ? m.WholeSilver.ToString() : m.Silver.ToString("0.##", CultureInfo.InvariantCulture); }

        float DrawStake(Rect r, ThrumboSeat seat, float y)
        {
            Widgets.Label(new Rect(0, y + 3f, 60f, 24f), "HH.Thrumbo.Stake".Translate());
            stakeText = Widgets.TextField(new Rect(64f, y, 86f, 28f), stakeText ?? "", 9);
            var max = ThrumboHouse.MaxStake(seat, true); var min = TS.MinStake;
            if (Widgets.ButtonText(new Rect(156f, y, 30f, 28f), "½")) { if (TryStake(out var m)) SetStake(Money.Max(min, from == PayFrom.Silver ? Money.FromSilver(Math.Max(1, m.WholeSilver / 2)) : m * 0.5)); }
            if (Widgets.ButtonText(new Rect(190f, y, 30f, 28f), "×2")) { if (TryStake(out var m)) SetStake(Money.Min(Money.Max(max, min), m * 2.0)); }
            float x = 230f;
            foreach (int v in new[] { 1, 5, 25, 100, 500 })
            {
                var rect = new Rect(x, y, 50f, 28f); x += 54f;
                if (Widgets.ButtonText(rect, v.ToString())) SetStake(Money.FromSilver(v));
            }
            if (Widgets.ButtonText(new Rect(x, y, 56f, 28f), "HH.Thrumbo.Max".Translate())) SetStake(Money.Max(max, min));
            x += 62f;
            Text.Font = GameFont.Tiny; GUI.color = dim; Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, r.width - x, 28f), "HH.Thrumbo.Limit".Translate(min.ToStringShort(), ThrumboHouse.TableLimit(seat.Map).ToStringShort()));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(x, y, r.width - x, 28f), "HH.Thrumbo.LimitTip".Translate((HSettings.Get(TS.MaxBetShare) * 100).ToString("0.#"), HSettings.GetSilver(TS.MaxBetFloor).ToStringShort(), HSettings.GetSilver(TS.MaxBetCap).ToStringShort()));
            return y + 36f;
        }

        // ---- result ------------------------------------------------------------------------------------------------
        void Show(ThrumboResult r)
        {
            if (!r.Ok) { notice = r.Error; noticeColor = new Color(1f, 0.75f, 0.45f); return; }
            last = r; lastTick = Find.TickManager.TicksGame; notice = null;
        }

        float DrawResult(Rect r, float y)
        {
            var line = new Rect(0, y, r.width, 28f);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (!notice.NullOrEmpty()) { GUI.color = noticeColor; Widgets.Label(line, notice); }
            else if (last != null && !last.Text.NullOrEmpty())
            {
                var net = last.Net; GUI.color = net.Micro > 0 ? good : (net.Micro < 0 ? bad : Color.white);
                bool openHand = !last.Done && ThrumboHouse.CurrentHand() != null;   // an open hand is not lost yet: no red, no "-stake"
                if (openHand) GUI.color = Color.white;
                Widgets.Label(line, openHand ? last.Text : last.Text + "   " + "HH.Thrumbo.Net".Translate((net.Micro >= 0 ? "+" : "") + net.ToStringShort()).ToString());
            }
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = dim; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y + 28f, r.width, 18f), "HH.Thrumbo.Sealed".Translate());
            Text.Font = GameFont.Small; GUI.color = Color.white;
            return y + 50f;
        }

        // ---- history and totals --------------------------------------------------------------------------------------
        void DrawHistory(Rect r)
        {
            var c = ThrumboComponent.Get(); if (c == null) return;
            Widgets.DrawLineHorizontal(0, r.y, r.width);
            Text.Font = GameFont.Tiny;
            double staked = c.StakedAll / (double)Money.PerSilver, paid = c.PaidAll / (double)Money.PerSilver;
            string tot = c.RoundsAll == 0 && c.GoodsRounds == 0 ? "HH.Thrumbo.NoHistory".Translate().ToString()
                : "HH.Thrumbo.Totals".Translate(c.RoundsAll.ToString("N0"), staked.ToString("N0"), paid.ToString("N0"), staked > 0 ? (paid / staked * 100).ToString("0.0") : "–", ((paid - staked) >= 0 ? "+" : "") + (paid - staked).ToString("N0")).ToString();
            GUI.color = dim; Widgets.Label(new Rect(0, r.y + 2f, r.width, 18f), "<b>" + "HH.Thrumbo.History".Translate() + "</b>   " + tot);
            float head = 22f;
            if (c.GoodsRounds > 0)
            {
                double gs = c.GoodsStaked / (double)Money.PerSilver, gp = c.GoodsPaid / (double)Money.PerSilver;
                Widgets.Label(new Rect(0, r.y + 20f, r.width, 18f), "HH.Thrumbo.TotalsGoods".Translate(c.GoodsRounds.ToString("N0"), gs.ToString("N0"), gp.ToString("N0"), ((gp - gs) >= 0 ? "+" : "") + (gp - gs).ToString("N0")));
                head = 40f;
            }
            GUI.color = Color.white;
            var list = new Rect(0, r.y + head, r.width, r.height - head);
            float rowH = 18f; int n = c.History.Count;
            var view = new Rect(0, 0, list.width - 18f, Math.Max(list.height, n * rowH));
            Widgets.BeginScrollView(list, ref histScroll, view);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < n; i++)
            {
                var e = c.History[n - 1 - i]; float yy = i * rowH;
                if (yy + rowH < histScroll.y || yy > histScroll.y + list.height) continue;
                var row = new Rect(0, yy, view.width, rowH);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                GUI.color = dim; Widgets.Label(new Rect(2f, yy, 80f, rowH), (now - e.Tick).ToStringTicksToPeriod(false, true, false, false) + " " + "HH.Thrumbo.Ago".Translate());
                GUI.color = Color.white; Widgets.Label(new Rect(84f, yy, 90f, rowH), ("HH.Thrumbo.Game." + (ThrumboGame)e.Game).Translate());
                Widgets.Label(new Rect(176f, yy, view.width - 176f - 190f, rowH), e.Text ?? "");
                var net = e.Net; GUI.color = net.Micro > 0 ? good : (net.Micro < 0 ? bad : Color.white);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(view.width - 186f, yy, 90f, rowH), "HH.Thrumbo.StakeShort".Translate(new Money(e.StakedMicro).ToStringShort()));
                Widgets.Label(new Rect(view.width - 92f, yy, 90f, rowH), (net.Micro >= 0 ? "+" : "") + net.ToStringShort());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        // ---- slots -------------------------------------------------------------------------------------------------
        void DrawSlots(Rect a, ThrumboSeat seat)
        {
            double shift = TS.Shift; var pt = ThrumboOdds.Slots(vol, shift);
            float x = a.x;
            for (int i = 0; i < 3; i++)
            {
                var reel = new Rect(x, a.y + 4f, 112f, 84f); x += 120f;
                Widgets.DrawBoxSolid(reel, new Color(0.93f, 0.9f, 0.8f, 0.96f)); Widgets.DrawBox(reel, 2);
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
                if (reels != null)
                {
                    Widgets.DrawBoxSolid(new Rect(reel.x + 6f, reel.yMax - 12f, reel.width - 12f, 5f), symbolColors[reels[i]]);
                    var icon = SlotSymbolIcons.For(reels[i]);
                    if (icon != null)
                    {
                        // symbol icon, its name small underneath (the name stays for colour-blind players and screen readers)
                        GUI.color = Color.white; GUI.DrawTexture(new Rect(reel.center.x - 28f, reel.y + 4f, 56f, 56f), icon);
                        Text.Font = GameFont.Tiny; GUI.color = new Color(0.25f, 0.21f, 0.17f);
                        Widgets.Label(new Rect(reel.x, reel.y + 58f, reel.width, 16f), ThrumboNames.Symbol(reels[i]));
                    }
                    else { GUI.color = new Color(0.12f, 0.1f, 0.09f); Widgets.Label(reel, ThrumboNames.Symbol(reels[i])); }
                }
                else { GUI.color = new Color(0.3f, 0.27f, 0.22f); Widgets.Label(reel, "?"); }
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            float y = a.y + 98f;
            if (Widgets.ButtonText(new Rect(a.x + 100f, y, 160f, 36f), "HH.Thrumbo.Spin".Translate())) DoSlots(seat);
            // volatility
            Widgets.Label(new Rect(a.x, y + 44f, 80f, 24f), "HH.Thrumbo.Volatility".Translate());
            float vx = a.x + 84f;
            foreach (SlotVolatility v in Enum.GetValues(typeof(SlotVolatility)))
            {
                var rect = new Rect(vx, y + 42f, 84f, 26f); vx += 88f;
                var p = ThrumboOdds.Slots(v, shift);
                if (Choice(rect, ("HH.Thrumbo.Vol." + v).Translate(), vol == v, true, "HH.Thrumbo.VolTip".Translate((p.HitRate * 100).ToString("0.#"), p.MaxMultiplier.ToString("N0")))) vol = v;
            }
            // paytable
            var table = new Rect(a.x + 372f, a.y, a.width - 372f, a.height);
            Text.Font = GameFont.Tiny; GUI.color = dim;
            Widgets.Label(new Rect(table.x, table.y, table.width, 16f), "<b>" + "HH.Thrumbo.Paytable".Translate((pt.Rtp * 100).ToString("0.#")) + "</b>");
            float ty = table.y + 18f;
            foreach (var row in pt.Rows())
            {
                Widgets.Label(new Rect(table.x, ty, table.width - 90f, 15f), ThrumboNames.PayRow(row.label));
                Text.Anchor = TextAnchor.UpperRight; Widgets.Label(new Rect(table.xMax - 90f, ty, 90f, 15f), "×" + row.multiplier.ToString(row.multiplier >= 100 ? "N0" : "0.##")); Text.Anchor = TextAnchor.UpperLeft;
                ty += 15f; if (ty > table.yMax - 12f) break;
            }
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        void DoSlots(ThrumboSeat seat)
        {
            if (!TryStake(out var stake)) { Show(ThrumboResult.Fail("HH.Thrumbo.Err.Number".Translate())); return; }
            var r = ThrumboHouse.PlaySlots(seat, stake, vol); Show(r);
            if (r.Ok && r.Reels != null) reels = r.Reels;
        }

        // ---- roulette ----------------------------------------------------------------------------------------------
        static readonly string[] outsideLabels = { "Low", "Even", "Red", "Black", "Odd", "High" };
        void DrawRoulette(Rect a, ThrumboSeat seat)
        {
            var rules = ThrumboOdds.Roulette(TS.Shift);
            const float cw = 34f, chh = 36f;
            float ox = a.x, oy = a.y;
            // zero column
            var zero = new Rect(ox, oy, cw, rules.Zeros == 2 ? chh * 1.5f : chh * 3f);
            Pocket(zero, 0);
            if (rules.Zeros == 2) Pocket(new Rect(ox, oy + chh * 1.5f, cw, chh * 1.5f), 37);
            for (int col = 0; col < 12; col++)
                for (int row = 0; row < 3; row++)
                {
                    int n = col * 3 + (3 - row);
                    Pocket(new Rect(ox + cw + col * cw, oy + row * chh, cw, chh), n);
                }
            // column bets
            for (int row = 0; row < 3; row++) OutsideCell(new Rect(ox + cw + 12 * cw, oy + row * chh, 44f, chh), "2:1", RouletteBet.Column(3 - row));
            float by = oy + 3 * chh + 3f;
            for (int d = 0; d < 3; d++) OutsideCell(new Rect(ox + cw + d * 4 * cw, by, 4 * cw, 30f), ("HH.Thrumbo.Roulette.Dozen" + (d + 1)).Translate(), RouletteBet.Dozen(d + 1));
            by += 33f;
            var outside = new[] { RouletteBet.Low(), RouletteBet.Even(), RouletteBet.Red(), RouletteBet.Black(), RouletteBet.Odd(), RouletteBet.High() };
            float ow = (12 * cw) / 6f;
            for (int i = 0; i < 6; i++) OutsideCell(new Rect(ox + cw + i * ow, by, ow, 30f), ("HH.Thrumbo.Roulette." + outsideLabels[i]).Translate(), outside[i]);
            // right: the bet, the ball, the spin
            float rx = ox + cw + 12 * cw + 56f; float rw = a.xMax - rx;
            Text.Font = GameFont.Tiny; GUI.color = dim;
            Widgets.Label(new Rect(rx, a.y, rw, 36f), "HH.Thrumbo.Roulette.YourBet".Translate(ThrumboNames.Bet(bet), bet.Odds.ToString()));
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(rx, a.y + 40f, Math.Min(120f, rw), 34f), "HH.Thrumbo.Spin".Translate())) DoRoulette(seat);
            if (lastPocket >= 0)
            {
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
                var ball = new Rect(rx, a.y + 82f, Math.Min(120f, rw), 36f);
                Widgets.DrawBoxSolid(ball, PocketColor(lastPocket)); Widgets.DrawBox(ball, 1); GUI.color = Color.white;
                Widgets.Label(ball, RouletteBet.PocketName(lastPocket));
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            }
            // last numbers
            Text.Font = GameFont.Tiny; float lx = rx; float ly = a.y + 124f;
            foreach (int p in pockets.AsEnumerable().Reverse().Take(8))
            {
                var cell = new Rect(lx, ly, 24f, 20f); lx += 26f; if (lx + 24f > a.xMax) { lx = rx; ly += 22f; }
                Widgets.DrawBoxSolid(cell, PocketColor(p)); Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(cell, RouletteBet.PocketName(p)); Text.Anchor = TextAnchor.UpperLeft;
            }
            Text.Font = GameFont.Small;
        }

        static Color PocketColor(int n) => n == 0 || n == 37 ? new Color(0.20f, 0.50f, 0.30f) : RouletteBet.IsRed(n) ? new Color(0.68f, 0.2f, 0.2f) : new Color(0.16f, 0.16f, 0.18f);

        void Pocket(Rect r, int n)
        {
            var b = RouletteBet.Straight(n);
            Widgets.DrawBoxSolid(r.ContractedBy(1f), PocketColor(n));
            bool sel = bet.Kind == RouletteBetKind.Straight && bet.Pockets[0] == n;
            if (sel) { Widgets.DrawBoxSolid(r.ContractedBy(1f), new Color(1f, 0.9f, 0.4f, 0.35f)); Widgets.DrawBox(r, 2); }
            else if (n == lastPocket) Widgets.DrawBox(r.ContractedBy(1f), 1);
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; GUI.color = Color.white;
            Widgets.Label(r, RouletteBet.PocketName(n));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, "HH.Thrumbo.Roulette.StraightTip".Translate(RouletteBet.PocketName(n)));
            if (Widgets.ButtonInvisible(r)) bet = b;
        }

        void OutsideCell(Rect r, string label, RouletteBet b)
        {
            bool sel = bet.Kind == b.Kind && bet.Label == b.Label;
            var c = b.Kind == RouletteBetKind.Red ? new Color(0.5f, 0.2f, 0.2f, 0.8f) : b.Kind == RouletteBetKind.Black ? new Color(0.14f, 0.14f, 0.16f, 0.9f) : new Color(0.2f, 0.3f, 0.24f, 0.7f);
            Widgets.DrawBoxSolid(r.ContractedBy(1f), c);
            if (sel) { Widgets.DrawBoxSolid(r.ContractedBy(1f), new Color(1f, 0.9f, 0.4f, 0.3f)); Widgets.DrawBox(r, 2); }
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; Widgets.Label(r, label); Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, "HH.Thrumbo.Roulette.OutsideTip".Translate(ThrumboNames.Bet(b), b.Odds.ToString()));
            if (Widgets.ButtonInvisible(r)) bet = b;
        }

        void DoRoulette(ThrumboSeat seat)
        {
            if (!TryStake(out var stake)) { Show(ThrumboResult.Fail("HH.Thrumbo.Err.Number".Translate())); return; }
            var r = ThrumboHouse.PlayRoulette(seat, stake, bet); Show(r);
            if (r.Ok) { lastPocket = r.Pocket; pockets.Add(r.Pocket); if (pockets.Count > 16) pockets.RemoveAt(0); }
        }

        // ---- blackjack ---------------------------------------------------------------------------------------------
        void DrawBlackjack(Rect a, ThrumboSeat seat, ThrumboHand hand)
        {
            var solver = ThrumboOdds.Solver; double shift = TS.Shift;
            var round = hand?.Round;
            // the open hand, or the last one that was played (kept in the result text only)
            float y = a.y;
            Widgets.Label(new Rect(a.x, y, 70f, 24f), "HH.Thrumbo.BJ.Dealer".Translate());
            if (round != null) DrawCards(new Rect(a.x + 74f, y - 2f, 300f, 66f), round.Dealer, !round.Done);
            Widgets.Label(new Rect(a.x, y + 76f, 70f, 24f), "HH.Thrumbo.BJ.You".Translate());
            if (round != null)
            {
                DrawCards(new Rect(a.x + 74f, y + 74f, 300f, 66f), round.Player, false);
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(a.x + 74f + 52f * Math.Min(round.Player.Count, 6) + 10f, y + 92f, 60f, 30f), BlackjackSolver.HandTotal(round.Player).ToString()); Text.Font = GameFont.Small;
            }
            else { GUI.color = dim; Widgets.Label(new Rect(a.x + 74f, y + 40f, 300f, 24f), "HH.Thrumbo.BJ.Press".Translate()); GUI.color = Color.white; }
            float by = a.y + 152f;
            if (round == null)
            {
                if (Widgets.ButtonText(new Rect(a.x + 74f, by, 130f, 34f), "HH.Thrumbo.BJ.Deal".Translate())) DoDeal(seat);
            }
            else
            {
                if (Widgets.ButtonText(new Rect(a.x + 74f, by, 100f, 34f), "HH.Thrumbo.BJ.Hit".Translate())) Show(ThrumboHouse.Hit(seat));
                if (Widgets.ButtonText(new Rect(a.x + 180f, by, 100f, 34f), "HH.Thrumbo.BJ.Stand".Translate())) Show(ThrumboHouse.Stand(seat));
                bool can = ThrumboHouse.CanDouble(seat);
                if (!can) GUI.color = new Color(1f, 1f, 1f, 0.4f);
                if (Widgets.ButtonText(new Rect(a.x + 286f, by, 110f, 34f), "HH.Thrumbo.BJ.Double".Translate(), true, true, can) && can) Show(ThrumboHouse.DoubleDown(seat));
                GUI.color = Color.white;
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(a.x + 74f, by + 38f, 330f, 18f), "HH.Thrumbo.BJ.StakeOpen".Translate(hand.Stake.ToStringShort())); GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            // rules
            var rules = new Rect(a.x + 430f, a.y, a.width - 430f, a.height);
            Text.Font = GameFont.Tiny; GUI.color = dim;
            Widgets.Label(rules, "HH.Thrumbo.BJ.Rules".Translate(ThrumboOdds.BlackjackRules.ToString(), (solver.HouseEdge * 100).ToString("0.##"), (ThrumboOdds.BlackjackEdge(shift) * 100).ToString("0.##"), ThrumboOdds.BlackjackWinScale(shift).ToString("0.###")));
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        void DoDeal(ThrumboSeat seat)
        {
            if (!TryStake(out var stake)) { Show(ThrumboResult.Fail("HH.Thrumbo.Err.Number".Translate())); return; }
            Show(ThrumboHouse.DealBlackjack(seat, stake));
        }

        static void DrawCards(Rect r, List<int> cards, bool hideSecond)
        {
            for (int i = 0; i < cards.Count && i < 6; i++)
            {
                var c = new Rect(r.x + i * 52f, r.y, 44f, 62f);
                bool hidden = hideSecond && i == 1;
                Widgets.DrawBoxSolid(c, hidden ? new Color(0.25f, 0.3f, 0.5f) : new Color(0.94f, 0.92f, 0.84f)); Widgets.DrawBox(c, 1);
                Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Medium; GUI.color = hidden ? new Color(0.8f, 0.85f, 1f) : new Color(0.15f, 0.12f, 0.1f);
                Widgets.Label(c, hidden ? "?" : (cards[i] == 1 ? "A" : cards[i].ToString()));
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            }
        }
    }
}
