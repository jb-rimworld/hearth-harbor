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
using Hearth.Casino.Anim;

namespace Hearth.Casino.PlayerHouse
{
    [StaticConstructorOnStartup]
    public static class ThrumboIcons
    {
        public static readonly Texture2D Logo = ContentFinder<Texture2D>.Get("UI/HH/Casino/LuckyThrumbo", false) ?? BaseContent.BadTex;
        /// <summary>Square mark for the comms console entry; null when missing (the console then shows its vanilla fallback).</summary>
        public static readonly Texture2D Mark = ContentFinder<Texture2D>.Get("UI/HH/Casino/ThrumboMark", false);
    }

    /// <summary>
    /// The Lucky Thrumbo: the orbital casino of Silverline's world (KONZEPT 6.13). The player gambles with real colony silver against a foreign house:
    /// slots, roulette and blackjack on the exact casino maths, sealed random streams, an optional table limit (off by default since 1.1.0),
    /// a plain history, and the quiet help line (K28).
    /// 1.1.0 (F30): every game is settled first and then played out on the table (reels, wheel, cards); the money, the history and the
    /// result line wait until the animation has ended. A click on the table, Space or Enter skips; the table keeps its last state.
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
        string notice; Color noticeColor = Color.white;
        Vector2 histScroll, howScroll;
        bool howTo;
        public static int DrawCount;

        // the tables (1.1.0): they keep their state between rounds
        public readonly SlotMachineView Slots = new SlotMachineView();
        public readonly RouletteView Wheel = new RouletteView();
        public readonly BlackjackView Cards = new BlackjackView();
        // what the window holds back while a round plays out
        ThrumboEntry heldEntry;
        Money heldBank, heldSilver; bool holding;
        int flashSymbol = -1; SlotPaytable.RowKind? flashKind; float flashUntil = -1f;
        ulong uiSeed = (ulong)DateTime.Now.Ticks;

        public override Vector2 InitialSize => HUI.Size(1000f, 820f, 820f, 660f);
        public bool ShowsHelpHint => CasinoUI.HelpHintText() != null;
        public ThrumboGame Game { get => game; set { if (game != value) SkipAll(); game = value; } }
        public bool HowToOpen { get => howTo; set => howTo = value; }
        /// <summary>True while a round still plays out on the table.</summary>
        public bool Busy => Slots.Busy || Wheel.Busy || Cards.Busy;
        public ThrumboEntry HeldEntry => Busy ? heldEntry : null;

        public Window_Thrumbo(Pawn negotiator)
        {
            this.negotiator = negotiator;
            doCloseX = true; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; preventCameraMotion = false; forcePause = false;
            resizeable = false;
            from = ThrumboSeat.For(negotiator).From;
            // the first visit opens the short explanations once (remembered for this player, not per save)
            try { if (!HSettings.GetBool(CS.HowToFirst)) { howTo = true; HSettings.Set(CS.HowToFirst, 1f); } } catch (Exception) { }
            // an open hand (also after a reload) lies on the table at once
            var h = ThrumboHouse.CurrentHand();
            if (h != null) { game = ThrumboGame.Blackjack; Cards.Sync(h.Round.Player, h.Round.Dealer, !h.Round.Done, ThrumboComponent.Get()?.HandSeed ?? 0, null, null, Color.white, false, false); }
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
        /// <summary>Showcase/test hooks: drive the window like a click would. The animation is skipped unless <paramref name="animate"/>.</summary>
        public ThrumboResult TestPlay(string what, string stake, bool animate = false)
        {
            stakeText = stake; var seat = Seat; ThrumboResult r = null;
            switch (what)
            {
                case "slots": game = ThrumboGame.Slots; r = DoSlots(seat); break;
                case "roulette": game = ThrumboGame.Roulette; bet = RouletteBet.Straight(17); r = DoRoulette(seat); bet = RouletteBet.Dozen(2); break;
                case "deal": game = ThrumboGame.Blackjack; r = DoDeal(seat); break;
                case "hit": game = ThrumboGame.Blackjack; r = Act(seat, 'H'); break;
                case "stand": game = ThrumboGame.Blackjack; r = Act(seat, 'S'); break;
            }
            if (!animate) SkipAll();
            return r;
        }
        public Money ShownFunds(PayFrom src) => DisplayFunds(src, Seat);
        public string ResultLine => ResultText(out _);
#endif

        public void SkipAll() { Slots.Skip(); Wheel.Skip(); Cards.Skip(); }

        // ---------------------------------------------------------------------------------------------------------
        static readonly Color dim = new Color(0.72f, 0.72f, 0.72f), good = new Color(0.6f, 0.9f, 0.6f), bad = new Color(1f, 0.58f, 0.48f), accent = new Color(0.95f, 0.82f, 0.45f);
        static readonly Color feltTint = new Color(0.82f, 0.95f, 0.86f);

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
                if (holding && !Busy) Release();
                var seat = Seat; var hand = ThrumboHouse.CurrentHand();
                bool busy = Busy;
                float y = DrawHeader(inRect, seat);
                y = DrawSource(inRect, seat, y, hand != null || busy);
                y = DrawGameTabs(inRect, y, hand != null, busy);
                y = DrawStake(inRect, seat, y, busy || hand != null);
                // the table takes what is left after a history of eight rows (more rows scroll)
                float histReserve = 22f + 8f * 20f + 4f;
                float stageH = Mathf.Clamp(inRect.height - y - 6f - 44f - 48f - 24f - histReserve, 250f, 520f);
                var stage = new Rect(0, y, inRect.width, stageH);
                DrawStage(stage, seat, hand);
                y += stageH + 6f;
                y = DrawControls(new Rect(0, y, inRect.width, 40f), seat, hand);
                y = DrawResult(inRect, y);
                DrawHistory(new Rect(0, y, inRect.width, inRect.height - y - 24f));
                CasinoUI.DrawHelpHint(new Rect(0, inRect.height - 22f, inRect.width, 20f));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }

        // ---- header ------------------------------------------------------------------------------------------------
        float DrawHeader(Rect r, ThrumboSeat seat)
        {
            float lw = Mathf.Min(520f, r.width * 0.52f), lh = lw / 6f;
            var logo = new Rect(0, 0, lw, lh);
            GUI.DrawTexture(logo, ThrumboIcons.Logo, ScaleMode.ScaleToFit);
            var plate = new Rect(lw * 0.21f, lh * 0.12f, lw * 0.77f, lh * 0.76f);
            Widgets.DrawBoxSolid(plate, new Color(0.05f, 0.07f, 0.14f, 0.62f));
            Text.Font = GameFont.Medium; GUI.color = new Color(0.97f, 0.93f, 0.82f);
            Widgets.Label(new Rect(plate.x + 8f, plate.y, plate.width - 12f, 32f), "HH.Thrumbo.Title".Translate());
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.82f, 0.85f, 0.95f);
            Widgets.Label(new Rect(plate.x + 8f, plate.y + 30f, plate.width - 12f, plate.height - 30f), "HH.Thrumbo.Tagline".Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            // the edges of the house, the same numbers the games use
            double shift = TS.Shift;
            string edge = "HH.Thrumbo.EdgeLine".Translate((ThrumboOdds.SlotRtp(shift) * 100).ToString("0.#"), (ThrumboOdds.RouletteEdge(shift) * 100).ToString("0.##"), (ThrumboOdds.BlackjackEdge(shift) * 100).ToString("0.##"));
            var er = new Rect(lw + 12f, 6f, r.width - lw - 12f, lh - 6f);
            GUI.color = dim; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(er, edge);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(er, "HH.Thrumbo.EdgeTip".Translate((shift * 100).ToString("+0.##;-0.##;0")));
            return lh + 8f;
        }

        // ---- where the money comes from --------------------------------------------------------------------------
        Money DisplayFunds(PayFrom src, ThrumboSeat seat)
        {
            if (holding && Busy) return src == PayFrom.Bank ? heldBank : heldSilver;
            return ThrumboHouse.Funds(new ThrumboSeat(seat.Map, seat.Player, src), true);
        }

        float DrawSource(Rect r, ThrumboSeat seat, float y, bool locked)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            float lw = Text.CalcSize("HH.Thrumbo.PayFrom".Translate()).x + 8f;
            Widgets.Label(new Rect(0, y, lw, 28f), "HH.Thrumbo.PayFrom".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            string bankLabel = BankApi.Ready ? "HH.Thrumbo.FromBank".Translate(DisplayFunds(PayFrom.Bank, seat).ToStringShort()).ToString() : "HH.Thrumbo.FromBankNone".Translate().ToString();
            string silverLabel = "HH.Thrumbo.FromSilver".Translate(DisplayFunds(PayFrom.Silver, seat).WholeSilver.ToString("N0")).ToString();
            float bw = Mathf.Clamp((r.width - lw - 200f) / 2f, 200f, 300f);
            if (Choice(new Rect(lw, y, bw, 28f), bankLabel, from == PayFrom.Bank, BankApi.Ready && !locked, "HH.Thrumbo.FromBankTip".Translate())) { from = PayFrom.Bank; Retext(); }
            if (Choice(new Rect(lw + bw + 6f, y, bw, 28f), silverLabel, from == PayFrom.Silver, !locked, "HH.Thrumbo.FromSilverTip".Translate())) { from = PayFrom.Silver; Retext(); }
            if (from == PayFrom.Bank && !BankApi.Ready) from = PayFrom.Silver;
            GUI.color = dim; Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            float px = lw + 2 * bw + 12f;
            Widgets.Label(new Rect(px, y, r.width - px, 28f), "HH.Thrumbo.PlayerLine".Translate(seat.Player?.LabelShort ?? "–"));
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

        float DrawGameTabs(Rect r, float y, bool handOpen, bool busy)
        {
            float x = 0;
            foreach (ThrumboGame g in Enum.GetValues(typeof(ThrumboGame)))
            {
                string label = ("HH.Thrumbo.Game." + g).Translate();
                var rect = new Rect(x, y, Mathf.Max(130f, Text.CalcSize(label).x + 36f), 30f); x += rect.width + 6f;
                if (Choice(rect, label, game == g, !handOpen || g == ThrumboGame.Blackjack, handOpen && g != ThrumboGame.Blackjack ? "HH.Thrumbo.HandOpenTip".Translate() : null)) Game = g;
            }
            // "How to play" (1.1.0): a short explanation per game, in place of the paytable
            string hl = "HH.Thrumbo.HowTo.Button".Translate();
            var hr = new Rect(x + 8f, y, Text.CalcSize(hl).x + 30f, 30f);
            if (Choice(hr, hl, howTo, true, "HH.Thrumbo.HowTo.ButtonTip".Translate())) howTo = true;
            else if (howTo && Widgets.ButtonInvisible(hr)) howTo = false;
            // "Raise the stakes…" (K15): only while the setting thrumbo.raiseTheStakes is on
            if (ThrumboStakes.Enabled)
            {
                string sl = "HH.Thrumbo.Stakes.Button".Translate();
                float sw = Mathf.Max(190f, Text.CalcSize(sl).x + 30f);
                var rs = new Rect(r.width - sw, y, sw, 30f);
                if (Choice(rs, sl, false, StakesButtonEnabled(handOpen) && !busy, handOpen ? "HH.Thrumbo.HandOpenTip".Translate().ToString() : "HH.Thrumbo.Stakes.ButtonTip".Translate().ToString()))
                    Window_ThrumboStakes.Open(Seat.Map, from);
            }
            return y + 38f;
        }

        // ---- stake -------------------------------------------------------------------------------------------------
        bool TryStake(out Money m)
        {
            m = Money.Zero;
            if (!double.TryParse((stakeText ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || d <= 0 || d > 1e12) return false;   // above the house ceiling Validate explains it
            if (from == PayFrom.Silver) d = Math.Round(d);
            m = Money.FromSilver(d); return m.Micro > 0;
        }

        void SetStake(Money m) { stakeText = from == PayFrom.Silver || m.Fraction.Micro == 0 ? m.WholeSilver.ToString() : m.Silver.ToString("0.##", CultureInfo.InvariantCulture); CasinoSfx.Play(CasinoSfx.Chip); }

        float DrawStake(Rect r, ThrumboSeat seat, float y, bool locked)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            float lw = Text.CalcSize("HH.Thrumbo.Stake".Translate()).x + 8f;
            Widgets.Label(new Rect(0, y, lw, 28f), "HH.Thrumbo.Stake".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            var field = new Rect(lw, y, 100f, 28f);
            if (locked)
            {
                // a round or an open hand holds the stake: shown, not editable (keys then go to the skip, not into the field)
                Widgets.DrawBoxSolid(field, new Color(0.08f, 0.08f, 0.09f, 0.6f)); Widgets.DrawBox(field, 1);
                Text.Anchor = TextAnchor.MiddleLeft; GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(field.ContractedBy(6f, 0f), stakeText ?? "");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                TooltipHandler.TipRegion(field, "HH.Thrumbo.StakeLockedTip".Translate());
            }
            else stakeText = Widgets.TextField(field, stakeText ?? "", 12);
            var max = ThrumboHouse.MaxStake(seat, true); var min = TS.MinStake;
            bool limitOn = ThrumboHouse.LimitOn;
            float x = lw + 106f;
            if (Widgets.ButtonText(new Rect(x, y, 32f, 28f), "½", true, true, !locked) && !locked) { if (TryStake(out var m)) SetStake(Money.Max(min, from == PayFrom.Silver ? Money.FromSilver(Math.Max(1, m.WholeSilver / 2)) : m * 0.5)); }
            x += 36f;
            if (Widgets.ButtonText(new Rect(x, y, 32f, 28f), "×2", true, true, !locked) && !locked) { if (TryStake(out var m)) SetStake(Money.Min(Money.Max(max, min), m * 2.0)); }
            x += 42f;
            foreach (int v in new[] { 1, 5, 25, 100, 500 })
            {
                var rect = new Rect(x, y, 48f, 28f); x += 52f;
                if (Widgets.ButtonText(rect, v.ToString(), true, true, !locked) && !locked) SetStake(Money.FromSilver(v));
            }
            string maxLabel = (limitOn ? "HH.Thrumbo.Max" : "HH.Thrumbo.AllIn").Translate();
            float mw = Mathf.Max(60f, Text.CalcSize(maxLabel).x + 20f);
            if (Widgets.ButtonText(new Rect(x, y, mw, 28f), maxLabel, true, true, !locked) && !locked) SetStake(Money.Max(max, min));
            TooltipHandler.TipRegion(new Rect(x, y, mw, 28f), limitOn ? "HH.Thrumbo.MaxTip".Translate().ToString() : "HH.Thrumbo.AllInTip".Translate().ToString());
            x += mw + 8f;
            Text.Font = GameFont.Tiny; GUI.color = dim; Text.Anchor = TextAnchor.MiddleLeft;
            var lr = new Rect(x, y, r.width - x, 28f);
            if (limitOn)
            {
                Widgets.Label(lr, "HH.Thrumbo.Limit".Translate(min.ToStringShort(), ThrumboHouse.TableLimit(seat.Map).ToStringShort()));
                TooltipHandler.TipRegion(lr, "HH.Thrumbo.LimitTip".Translate((HSettings.Get(TS.MaxBetShare) * 100).ToString("0.#"), HSettings.GetSilver(TS.MaxBetFloor).ToStringShort(), HSettings.GetSilver(TS.MaxBetCap).ToStringShort()));
            }
            else
            {
                Widgets.Label(lr, "HH.Thrumbo.NoLimit".Translate(min.ToStringShort(), ThrumboHouse.Funds(seat, true).ToStringShort()));
                TooltipHandler.TipRegion(lr, "HH.Thrumbo.NoLimitTip".Translate());
            }
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            return y + 36f;
        }

        // ---- the table -----------------------------------------------------------------------------------------------
        void DrawStage(Rect stage, ThrumboSeat seat, ThrumboHand hand)
        {
            // felt with a wooden edge
            Widgets.DrawBoxSolid(stage, new Color(0.22f, 0.14f, 0.09f));
            var felt = stage.ContractedBy(4f);
            var old = GUI.color; GUI.color = feltTint; AnimDraw.Tiled(felt, CasinoAnimTex.Felt, 160f); GUI.color = old;
            Widgets.DrawBox(stage, 1);
            if (Busy && AnimDraw.SkipRequested(stage)) SkipAll();
            float side = Mathf.Clamp(stage.width * 0.3f, 250f, 360f);
            var main = new Rect(felt.x + 8f, felt.y + 8f, felt.width - side - 24f, felt.height - 16f);
            var right = new Rect(main.xMax + 10f, felt.y + 8f, side, felt.height - 16f);
            switch (game)
            {
                case ThrumboGame.Slots: DrawSlotsStage(main, seat); break;
                case ThrumboGame.Roulette: DrawRouletteStage(new Rect(felt.x + 8f, felt.y + 8f, felt.width - 16f, felt.height - 16f), seat); break;
                default: DrawBlackjackStage(main, seat, hand); break;
            }
            if (game != ThrumboGame.Roulette)
            {
                Widgets.DrawBoxSolid(right, new Color(0.04f, 0.06f, 0.05f, 0.55f));
                var inner = right.ContractedBy(8f);
                if (howTo) DrawHowTo(inner, game);
                else if (game == ThrumboGame.Slots) DrawPaytable(inner);
                else DrawBlackjackRules(inner);
            }
            else if (howTo)
            {
                var pr = new Rect(stage.xMax - side - 12f, stage.y + 10f, side, stage.height - 20f);
                Widgets.DrawBoxSolid(pr, new Color(0.03f, 0.05f, 0.04f, 0.92f)); Widgets.DrawBox(pr, 1);
                DrawHowTo(pr.ContractedBy(8f), game);
            }
            if (Busy)
            {
                // a small dark plate under the hint so it reads on any felt
                Text.Font = GameFont.Tiny;
                string hint = "HH.Thrumbo.Anim.SkipHint".Translate();
                var hs = Text.CalcSize(hint);
                var hr = new Rect(stage.x + 8f, stage.yMax - hs.y - 12f, hs.x + 16f, hs.y + 6f);
                Widgets.DrawBoxSolid(hr, new Color(0.04f, 0.05f, 0.04f, 0.72f));
                AnimDraw.ShadowLabel(hr, hint, new Color(0.96f, 0.94f, 0.86f, 0.95f), GameFont.Tiny);
                Text.Font = GameFont.Small;
            }
        }

        void DrawHowTo(Rect r, ThrumboGame g)
        {
            Text.Font = GameFont.Small; GUI.color = accent;
            Widgets.Label(new Rect(r.x, r.y, r.width, 24f), "HH.Thrumbo.HowTo.Title".Translate(("HH.Thrumbo.Game." + g).Translate()));
            GUI.color = new Color(0.92f, 0.92f, 0.88f); Text.Font = GameFont.Tiny;
            string text = ("HH.Thrumbo.HowTo." + g).Translate();
            var outer = new Rect(r.x, r.y + 26f, r.width, r.height - 26f);
            float h = Text.CalcHeight(text, outer.width - 16f);
            var view = new Rect(0, 0, outer.width - 16f, Math.Max(outer.height, h + 6f));
            Widgets.BeginScrollView(outer, ref howScroll, view);
            Widgets.Label(new Rect(0, 0, view.width, h + 4f), text);
            Widgets.EndScrollView();
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        // ---- slots -------------------------------------------------------------------------------------------------
        void DrawSlotsStage(Rect a, ThrumboSeat seat)
        {
            Slots.Clip = a.ExpandedBy(8f);
            var cab = Slots.Draw(a, "HH.Thrumbo.SlotMarquee".Translate());
            // a click on the lever spins
            var lever = SlotMachineView.LeverRect(cab);
            if (!Busy && Mouse.IsOver(lever)) { Widgets.DrawHighlight(lever); TooltipHandler.TipRegion(lever, "HH.Thrumbo.LeverTip".Translate()); }
            if (!Busy && Widgets.ButtonInvisible(lever)) DoSlots(seat);
            var an = Slots.Anim;
            if (an != null && an.Elapsed >= an.WinAt && an.Mult > 0) DrawWinPlate(a, an.Tier, an.Elapsed - an.WinAt, last?.Paid ?? Money.Zero);
        }

        /// <summary>The prize: a rising "+12" for a small win, a plate with a count-up for a big one, the thrumbo running past for the jackpot.</summary>
        void DrawWinPlate(Rect a, int tier, float since, Money paid)
        {
            if (since > 4.5f) return;
            double count = paid.Silver * Ease.OutCubic(Mathf.Clamp01(since / (tier >= 2 ? 1.4f : 0.6f)));
            string amount = "+" + count.ToString(count >= 100 ? "N0" : "0.##");
            float fade = Mathf.Clamp01((4.5f - since) / 0.6f);
            if (tier <= 1)
            {
                float rise = Mathf.Clamp01(since / 1.2f) * 40f;
                AnimDraw.ShadowLabel(new Rect(a.center.x - 120f, a.yMax - 60f - rise, 240f, 34f), amount, new Color(1f, 0.9f, 0.45f, fade), GameFont.Medium);
                return;
            }
            float u = Mathf.Clamp01(since / 0.35f);
            float w = Mathf.Min(a.width * 0.7f, 380f), h = w * 0.3f;
            var r = new Rect(a.center.x - w / 2, a.center.y - h / 2, w, h).ScaledBy(0.6f + 0.4f * (float)Ease.OutBack(u, 2.2));
            var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, fade); GUI.DrawTexture(r, CasinoAnimTex.BigWin); GUI.color = old;
            AnimDraw.ShadowLabel(new Rect(r.x, r.y + r.height * 0.12f, r.width, r.height * 0.45f), (tier >= 3 ? "HH.Thrumbo.Anim.Jackpot" : "HH.Thrumbo.Anim.BigWin").Translate(), new Color(1f, 0.88f, 0.4f, fade), GameFont.Medium);
            AnimDraw.ShadowLabel(new Rect(r.x, r.y + r.height * 0.52f, r.width, r.height * 0.4f), amount, new Color(1f, 1f, 0.92f, fade), GameFont.Medium);
            if (tier >= 3 && since < 3.2f)
            {
                // the house mascot runs across the table
                float k = Mathf.Clamp01(since / 3.0f);
                float tw = a.height * 0.55f, th = tw * 0.62f;
                float x = Mathf.Lerp(a.x - tw, a.xMax + tw, k);
                float bob = Mathf.Abs(Mathf.Sin(since * 14f)) * 8f;
                GUI.DrawTexture(new Rect(x - tw / 2, a.yMax - th - 6f - bob, tw, th), CasinoAnimTex.ThrumboRun);
            }
        }

        void DrawPaytable(Rect t)
        {
            double shift = TS.Shift; var pt = ThrumboOdds.Slots(vol, shift);
            Text.Font = GameFont.Tiny; GUI.color = accent;
            string head = "HH.Thrumbo.Paytable".Translate((pt.Rtp * 100).ToString("0.#"));
            float hh = Text.CalcHeight(head, t.width);
            Widgets.Label(new Rect(t.x, t.y, t.width, hh), head);
            GUI.color = Color.white;
            var rows = pt.PayRows().ToList();
            float avail = t.height - hh - 4f;
            float rowH = Mathf.Clamp(avail / Math.Max(1, rows.Count), 16f, 28f);
            float icon = rowH - 4f, y = t.y + hh + 4f;
            bool flashing = AnimClock.Now < flashUntil;
            foreach (var row in rows)
            {
                var rr = new Rect(t.x, y, t.width, rowH);
                bool hit = flashing && row.symbol == flashSymbol && row.kind == flashKind;
                if (hit) Widgets.DrawBoxSolid(rr, new Color(1f, 0.82f, 0.3f, 0.25f + 0.2f * Mathf.Sin(AnimClock.Now * 12f)));
                float x = t.x + 2f;
                int n = row.kind == SlotPaytable.RowKind.Three ? 3 : row.kind == SlotPaytable.RowKind.FirstTwo ? 2 : 1;
                for (int i = 0; i < 3; i++)
                {
                    var ir = new Rect(x, y + 2f, icon, icon); x += icon + 2f;
                    if (i < n) { var tex = SlotSymbolIcons.For(row.symbol); if (tex != null) GUI.DrawTexture(ir, tex); }
                    else { GUI.color = dim; Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(ir, "·"); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }
                }
                GUI.color = hit ? accent : dim; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x + 4f, y, t.xMax - x - 70f, rowH), ThrumboNames.PayRow(row.symbol, row.kind));
                Text.Anchor = TextAnchor.MiddleRight; GUI.color = hit ? accent : Color.white;
                Widgets.Label(new Rect(t.xMax - 68f, y, 66f, rowH), "×" + row.multiplier.ToString(row.multiplier >= 100 ? "N0" : "0.##"));
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                TooltipHandler.TipRegion(rr, "HH.Thrumbo.PayRowTip".Translate((row.probability * 100).ToString(row.probability < 0.001 ? "0.####" : "0.##")));
                y += rowH;
            }
            Text.Font = GameFont.Small;
        }

        ThrumboResult DoSlots(ThrumboSeat seat)
        {
            if (Busy) { SkipAll(); return null; }
            if (!TryStake(out var stake)) { Show(ThrumboResult.Fail("HH.Thrumbo.Err.Number".Translate())); return null; }
            Snapshot(seat, stake);
            var r = ThrumboHouse.PlaySlots(seat, stake, vol); Show(r);
            if (r.Ok && r.Reels != null)
            {
                Slots.Start(r.Reels, r.Mult, Next());
                var pt = ThrumboOdds.Slots(vol, TS.Shift);
                var row = r.Mult > 0 ? pt.PayingRow(r.Reels[0], r.Reels[1], r.Reels[2]) : null;
                flashSymbol = row == SlotPaytable.RowKind.Three ? r.Reels[0] : 0; flashKind = row;
                flashUntil = AnimClock.Now + (Slots.Anim != null ? Slots.Anim.WinAt / Mathf.Max(0.25f, AnimPrefs.Speed) : 0f) + 3f;
                Hold(r);
            }
            else holding = false;
            return r;
        }

        // ---- roulette ----------------------------------------------------------------------------------------------
        static readonly string[] outsideLabels = { "Low", "Even", "Red", "Black", "Odd", "High" };
        void DrawRouletteStage(Rect a, ThrumboSeat seat)
        {
            var rules = ThrumboOdds.Roulette(TS.Shift);
            if (Wheel.Zeros != rules.Zeros && !Wheel.Busy) Wheel.Zeros = rules.Zeros;
            float wd = Mathf.Min(a.height, a.width * 0.42f);
            var wheelRect = new Rect(a.x, a.y + (a.height - wd) / 2f, wd, wd);
            Wheel.Draw(wheelRect);
            // the board, sized to what is left
            var board = new Rect(wheelRect.xMax + 16f, a.y, a.xMax - wheelRect.xMax - 16f, a.height);
            DrawBoard(board, rules);
        }

        void DrawBoard(Rect b, RouletteRules rules)
        {
            float cw = Mathf.Clamp((b.width - 4f) / 14.2f, 20f, 64f);
            float chh = Mathf.Clamp((b.height - 2f * 30f - 64f) / 3f, 26f, 60f);
            float ox = b.x, oy = b.y + 4f;
            int shown = Busy ? -1 : Wheel.ShownPocket;
            var zero = new Rect(ox, oy, cw, rules.Zeros == 2 ? chh * 1.5f : chh * 3f);
            Pocket(zero, 0, shown);
            if (rules.Zeros == 2) Pocket(new Rect(ox, oy + chh * 1.5f, cw, chh * 1.5f), 37, shown);
            for (int col = 0; col < 12; col++)
                for (int row = 0; row < 3; row++)
                {
                    int n = col * 3 + (3 - row);
                    Pocket(new Rect(ox + cw + col * cw, oy + row * chh, cw, chh), n, shown);
                }
            for (int row = 0; row < 3; row++) OutsideCell(new Rect(ox + cw + 12 * cw, oy + row * chh, cw * 1.2f, chh), "2:1", RouletteBet.Column(3 - row), shown);
            float by = oy + 3 * chh + 3f;
            for (int d = 0; d < 3; d++) OutsideCell(new Rect(ox + cw + d * 4 * cw, by, 4 * cw, 30f), ("HH.Thrumbo.Roulette.Dozen" + (d + 1)).Translate(), RouletteBet.Dozen(d + 1), shown);
            by += 33f;
            var outside = new[] { RouletteBet.Low(), RouletteBet.Even(), RouletteBet.Red(), RouletteBet.Black(), RouletteBet.Odd(), RouletteBet.High() };
            float ow = (12 * cw) / 6f;
            for (int i = 0; i < 6; i++) OutsideCell(new Rect(ox + cw + i * ow, by, ow, 30f), ("HH.Thrumbo.Roulette." + outsideLabels[i]).Translate(), outside[i], shown);
            by += 36f;
            // the bet and the last numbers
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.9f, 0.9f, 0.86f);
            Widgets.Label(new Rect(ox, by, b.width * 0.55f, 32f), "HH.Thrumbo.Roulette.YourBet".Translate(ThrumboNames.Bet(bet), bet.Odds.ToString()));
            GUI.color = Color.white;
            Wheel.DrawRecent(new Rect(ox + b.width * 0.56f, by + 2f, b.width * 0.44f, 20f), 10);
            Text.Font = GameFont.Small;
        }

        void Pocket(Rect r, int n, int shown)
        {
            var b = RouletteBet.Straight(n);
            Widgets.DrawBoxSolid(r.ContractedBy(1f), RouletteView.PocketColor(n));
            bool sel = bet.Kind == RouletteBetKind.Straight && bet.Pockets[0] == n;
            if (n == shown) { Widgets.DrawBoxSolid(r.ContractedBy(1f), new Color(1f, 0.95f, 0.6f, 0.25f + 0.2f * Mathf.Sin(AnimClock.Now * 7f))); Widgets.DrawBox(r, 2); }
            if (sel) { Widgets.DrawBoxSolid(r.ContractedBy(1f), new Color(1f, 0.9f, 0.4f, 0.3f)); Widgets.DrawBox(r, 2); }
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; GUI.color = Color.white;
            Widgets.Label(r, RouletteBet.PocketName(n));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (sel) DrawBetChip(r, n == shown);
            if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, "HH.Thrumbo.Roulette.StraightTip".Translate(RouletteBet.PocketName(n)));
            if (!Busy && Widgets.ButtonInvisible(r)) { bet = b; CasinoSfx.Play(CasinoSfx.Chip); }
        }

        void OutsideCell(Rect r, string label, RouletteBet b, int shown)
        {
            bool sel = bet.Kind == b.Kind && bet.Label == b.Label;
            var c = b.Kind == RouletteBetKind.Red ? new Color(0.5f, 0.2f, 0.2f, 0.85f) : b.Kind == RouletteBetKind.Black ? new Color(0.14f, 0.14f, 0.16f, 0.9f) : new Color(0.12f, 0.28f, 0.19f, 0.8f);
            Widgets.DrawBoxSolid(r.ContractedBy(1f), c);
            bool hit = shown >= 0 && b.Covers(shown);
            if (hit) Widgets.DrawBoxSolid(r.ContractedBy(1f), new Color(1f, 0.95f, 0.6f, 0.12f + 0.1f * Mathf.Sin(AnimClock.Now * 7f)));
            if (sel) { Widgets.DrawBoxSolid(r.ContractedBy(1f), new Color(1f, 0.9f, 0.4f, 0.25f)); Widgets.DrawBox(r, 2); }
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; Widgets.Label(r, label); Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (sel) DrawBetChip(r, hit);
            if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, "HH.Thrumbo.Roulette.OutsideTip".Translate(ThrumboNames.Bet(b), b.Odds.ToString()));
            if (!Busy && Widgets.ButtonInvisible(r)) { bet = b; CasinoSfx.Play(CasinoSfx.Chip); }
        }

        /// <summary>The player's chip on the chosen bet; after a winning spin a small stack of winnings joins it.</summary>
        void DrawBetChip(Rect cell, bool won)
        {
            float s = Mathf.Min(22f, Mathf.Min(cell.width, cell.height) * 0.7f);
            var p = new Vector2(cell.xMax - s * 0.55f, cell.yMax - s * 0.55f);
            var old = GUI.color;
            int stack = won ? 3 : 1;
            for (int k = 0; k < stack; k++) { var cc = BlackjackView.ChipColor(k == 0 ? 2 : k); GUI.color = cc; GUI.DrawTexture(new Rect(p.x - s / 2 - k * 2f, p.y - s / 2 - k * 4f, s, s), CasinoAnimTex.Chip); }
            GUI.color = old;
        }

        ThrumboResult DoRoulette(ThrumboSeat seat)
        {
            if (Busy) { SkipAll(); return null; }
            if (!TryStake(out var stake)) { Show(ThrumboResult.Fail("HH.Thrumbo.Err.Number".Translate())); return null; }
            Snapshot(seat, stake);
            var r = ThrumboHouse.PlayRoulette(seat, stake, bet); Show(r);
            if (r.Ok)
            {
                var rules = ThrumboOdds.Roulette(TS.Shift);
                Wheel.Start(r.Pocket, rules.Zeros, r.Mult > 0, r.Mult >= 20, Next());
                Hold(r);
            }
            else holding = false;
            return r;
        }

        // ---- blackjack ---------------------------------------------------------------------------------------------
        void DrawBlackjackStage(Rect a, ThrumboSeat seat, ThrumboHand hand)
        {
            // dealer and player labels with the totals of the cards that are already visible
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.9f, 0.92f, 0.86f, 0.85f);
            int dt = Cards.VisibleTotal(0), pt = Cards.VisibleTotal(1);
            Widgets.Label(new Rect(a.x + 4f, a.y + 2f, 220f, 18f), "HH.Thrumbo.BJ.Dealer".Translate() + (dt > 0 ? "  ·  " + dt : ""));
            Widgets.Label(new Rect(a.x + 4f, a.yMax - 20f, 220f, 18f), "HH.Thrumbo.BJ.You".Translate() + (pt > 0 ? "  ·  " + pt : ""));
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Cards.Empty)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f); Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(a, "HH.Thrumbo.BJ.Press".Translate());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            Cards.Draw(a);
            if (!Cards.Busy) Cards.DrawBanner(a);
        }

        void DrawBlackjackRules(Rect r)
        {
            var solver = ThrumboOdds.Solver; double shift = TS.Shift;
            var rules = ThrumboOdds.BlackjackRules;
            Text.Font = GameFont.Tiny; GUI.color = dim;
            string text = "HH.Thrumbo.BJ.Rules".Translate(rules.ToString(), (solver.HouseEdge * 100).ToString("0.##"), (ThrumboOdds.BlackjackEdge(shift) * 100).ToString("0.##"), ThrumboOdds.BlackjackWinScale(shift).ToString("0.###"));
            float th = Text.CalcHeight(text, r.width);
            Widgets.Label(new Rect(r.x, r.y, r.width, th), text);
            float y = r.y + th + 10f;
            // what a hand pays, as a small table
            GUI.color = accent;
            Widgets.Label(new Rect(r.x, y, r.width, 18f), "HH.Thrumbo.BJ.PayTitle".Translate()); y += 20f;
            double scale = ThrumboOdds.BlackjackWinScale(shift);
            var pays = new (string key, string val)[]
            {
                ("HH.Thrumbo.BJ.PayWin", "×" + (1 + 1.0 * scale).ToString("0.##")),
                ("HH.Thrumbo.BJ.PayNatural", "×" + (1 + rules.BlackjackPays * scale).ToString("0.##")),
                ("HH.Thrumbo.BJ.PayPush", "×1"),
                ("HH.Thrumbo.BJ.PayDouble", "×" + (2 * (1 + 1.0 * scale)).ToString("0.##")),
                ("HH.Thrumbo.BJ.PayLose", "×0"),
            };
            for (int i = 0; i < pays.Length; i++)
            {
                var row = new Rect(r.x, y, r.width, 18f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                GUI.color = dim; Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(row.x + 4f, row.y, row.width - 70f, row.height), pays[i].key.Translate());
                GUI.color = Color.white; Text.Anchor = TextAnchor.MiddleRight; Widgets.Label(new Rect(row.xMax - 66f, row.y, 62f, row.height), pays[i].val);
                Text.Anchor = TextAnchor.UpperLeft;
                y += 18f;
            }
            y += 10f;
            DrawRecent(new Rect(r.x, y, r.width, r.yMax - y), ThrumboGame.Blackjack);
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        /// <summary>The last rounds of one game (newest first), as far as the room allows; the round on the table is not shown yet.</summary>
        void DrawRecent(Rect r, ThrumboGame g)
        {
            if (r.height < 40f) return;
            var entries = HistoryView();
            Text.Font = GameFont.Tiny; GUI.color = accent;
            Widgets.Label(new Rect(r.x, r.y, r.width, 18f), "HH.Thrumbo.Recent".Translate());
            float y = r.y + 20f; int shown = 0;
            for (int i = 0; i < entries.Count && y + 18f <= r.yMax; i++)
            {
                var e = entries[i]; if (e.Game != (int)g) continue;
                var net = e.Net;
                Text.Anchor = TextAnchor.MiddleLeft; GUI.color = new Color(0.88f, 0.88f, 0.84f); Text.WordWrap = false;
                Widgets.Label(new Rect(r.x + 4f, y, r.width - 76f, 18f), (e.Text ?? "").Truncate(r.width - 76f));
                Text.WordWrap = true;
                GUI.color = net.Micro > 0 ? good : (net.Micro < 0 ? bad : Color.white); Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(r.xMax - 70f, y, 66f, 18f), (net.Micro >= 0 ? "+" : "") + net.ToStringShort());
                Text.Anchor = TextAnchor.UpperLeft;
                y += 18f; shown++;
            }
            if (shown == 0) { GUI.color = dim; Widgets.Label(new Rect(r.x + 4f, y, r.width, 18f), "HH.Thrumbo.RecentNone".Translate()); }
            GUI.color = Color.white;
        }

        // the history as the window shows it (newest first, without the round on the table), rebuilt only when the history changed
        readonly List<ThrumboEntry> historyView = new List<ThrumboEntry>(64);
        int historyCount = -1; ThrumboEntry historyFirst, historyLast, historyHeld; ThrumboComponent historyComp;
        List<ThrumboEntry> HistoryView()
        {
            var c = ThrumboComponent.Get(); var held = HeldEntry;
            if (c == null) { historyView.Clear(); historyComp = null; return historyView; }
            var h = c.History; int n = h.Count;
            var first = n > 0 ? h[0] : null; var lastE = n > 0 ? h[n - 1] : null;
            if (c != historyComp || n != historyCount || !ReferenceEquals(first, historyFirst) || !ReferenceEquals(lastE, historyLast) || !ReferenceEquals(held, historyHeld))
            {
                historyComp = c; historyCount = n; historyFirst = first; historyLast = lastE; historyHeld = held;
                historyView.Clear();
                for (int i = n - 1; i >= 0; i--) if (!ReferenceEquals(h[i], held)) historyView.Add(h[i]);
            }
            return historyView;
        }

        ThrumboResult DoDeal(ThrumboSeat seat)
        {
            if (Busy) { SkipAll(); return null; }
            if (!TryStake(out var stake)) { Show(ThrumboResult.Fail("HH.Thrumbo.Err.Number".Translate())); return null; }
            Snapshot(seat, stake);
            var r = ThrumboHouse.DealBlackjack(seat, stake); Show(r);
            if (r.Ok) { SyncCards(r); Hold(r); } else holding = false;
            return r;
        }

        ThrumboResult Act(ThrumboSeat seat, char what)
        {
            if (Busy) { SkipAll(); return null; }
            var h = ThrumboHouse.CurrentHand(); if (h == null) return null;
            Snapshot(new ThrumboSeat(seat.Map, seat.Player, h.From), what == 'D' ? h.Stake : Money.Zero);
            var r = what == 'H' ? ThrumboHouse.Hit(seat) : what == 'S' ? ThrumboHouse.Stand(seat) : ThrumboHouse.DoubleDown(seat);
            Show(r);
            if (r.Ok) { if (what == 'D') CasinoSfx.Play(CasinoSfx.ChipsPlace); SyncCards(r); Hold(r); } else holding = false;
            return r;
        }

        void SyncCards(ThrumboResult r)
        {
            if (r.PlayerCards == null || r.DealerCards == null) return;
            string banner = null; Color col = Color.white; bool win = false;
            if (r.Done && r.Outcome != null)
            {
                var net = r.Net; win = net.Micro > 0;
                col = net.Micro > 0 ? good : net.Micro < 0 ? bad : Color.white;
                banner = ("HH.Thrumbo.BJ." + r.Outcome.Value).Translate().CapitalizeFirst() + "   " + (net.Micro >= 0 ? "+" : "") + net.ToStringShort();
            }
            Cards.Sync(r.PlayerCards, r.DealerCards, !r.Done, r.HandSeed, r.Done ? r.Outcome : null, banner, col, win);
        }

        // ---- the buttons under the table -------------------------------------------------------------------------------
        float DrawControls(Rect r, ThrumboSeat seat, ThrumboHand hand)
        {
            bool busy = Busy;
            float x = r.x;
            string skip = "HH.Thrumbo.Anim.Skip".Translate();
            switch (game)
            {
                case ThrumboGame.Slots:
                {
                    var sb = new Rect(x, r.y, 170f, 36f);
                    if (Widgets.ButtonText(sb, busy ? skip : "HH.Thrumbo.Spin".Translate())) DoSlots(seat);
                    x += 186f;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    float vl = Text.CalcSize("HH.Thrumbo.Volatility".Translate()).x + 8f;
                    Widgets.Label(new Rect(x, r.y, vl, 36f), "HH.Thrumbo.Volatility".Translate()); x += vl;
                    Text.Anchor = TextAnchor.UpperLeft;
                    double shift = TS.Shift;
                    foreach (SlotVolatility v in Enum.GetValues(typeof(SlotVolatility)))
                    {
                        var rect = new Rect(x, r.y + 4f, 96f, 28f); x += 100f;
                        var p = ThrumboOdds.Slots(v, shift);
                        if (Choice(rect, ("HH.Thrumbo.Vol." + v).Translate(), vol == v, !busy, "HH.Thrumbo.VolTip".Translate((p.HitRate * 100).ToString("0.#"), p.MaxMultiplier.ToString("N0")))) vol = v;
                    }
                    break;
                }
                case ThrumboGame.Roulette:
                {
                    var sb = new Rect(x, r.y, 170f, 36f);
                    if (Widgets.ButtonText(sb, busy ? skip : "HH.Thrumbo.Spin".Translate())) DoRoulette(seat);
                    break;
                }
                default:
                {
                    if (hand == null)
                    {
                        string deal = busy ? skip : (Cards.Empty ? "HH.Thrumbo.BJ.Deal" : "HH.Thrumbo.BJ.DealAgain").Translate();
                        if (Widgets.ButtonText(new Rect(x, r.y, 170f, 36f), deal)) DoDeal(seat);
                    }
                    else
                    {
                        if (Widgets.ButtonText(new Rect(x, r.y, 120f, 36f), busy ? skip : "HH.Thrumbo.BJ.Hit".Translate())) { if (busy) SkipAll(); else Act(seat, 'H'); }
                        x += 126f;
                        if (Widgets.ButtonText(new Rect(x, r.y, 120f, 36f), "HH.Thrumbo.BJ.Stand".Translate(), true, true, !busy) && !busy) Act(seat, 'S');
                        x += 126f;
                        bool can = !busy && ThrumboHouse.CanDouble(seat);
                        if (Widgets.ButtonText(new Rect(x, r.y, 140f, 36f), "HH.Thrumbo.BJ.Double".Translate(), true, true, can) && can) Act(seat, 'D');
                        x += 150f;
                        GUI.color = dim; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(new Rect(x, r.y, r.width - x, 36f), "HH.Thrumbo.BJ.StakeOpen".Translate(hand.Stake.ToStringShort()));
                        GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                    }
                    break;
                }
            }
            return r.y + r.height + 4f;
        }

        // ---- result, hold-back -----------------------------------------------------------------------------------------
        void Show(ThrumboResult r)
        {
            if (r == null) return;
            if (!r.Ok) { notice = r.Error; noticeColor = new Color(1f, 0.75f, 0.45f); return; }
            last = r; notice = null;
        }

        ulong Next() => uiSeed = RngStream.Mix(uiSeed + 0x9E3779B97F4A7C15UL);

        /// <summary>Before a round: the funds as they are, minus the stake the round will take (what the player sees while it plays out).</summary>
        void Snapshot(ThrumboSeat seat, Money takes)
        {
            heldBank = ThrumboHouse.Funds(new ThrumboSeat(seat.Map, seat.Player, PayFrom.Bank), true);
            heldSilver = ThrumboHouse.Funds(new ThrumboSeat(seat.Map, seat.Player, PayFrom.Silver), true);
            if (seat.From == PayFrom.Bank) heldBank = Money.Max(Money.Zero, heldBank - takes); else heldSilver = Money.Max(Money.Zero, heldSilver - Money.FromSilver(takes.WholeSilver));
        }

        void Hold(ThrumboResult r)
        {
            heldEntry = r.Entry;
            holding = Busy;
            if (!holding) Release();
        }

        void Release() { holding = false; heldEntry = null; }

        string ResultText(out Color col)
        {
            col = Color.white;
            if (!notice.NullOrEmpty()) { col = noticeColor; return notice; }
            if (Busy && last != null)
            {
                col = dim;
                return (game == ThrumboGame.Blackjack ? "HH.Thrumbo.Anim.Dealing" : "HH.Thrumbo.Anim.Spinning").Translate();
            }
            if (last == null || last.Text.NullOrEmpty())
            {
                if (last != null && !last.Done && ThrumboHouse.CurrentHand() != null) return "HH.Thrumbo.BJ.Dealt".Translate();
                return null;
            }
            var net = last.Net; col = net.Micro > 0 ? good : (net.Micro < 0 ? bad : Color.white);
            bool openHand = !last.Done && ThrumboHouse.CurrentHand() != null;   // an open hand is not lost yet: no red, no "-stake"
            if (openHand) { col = Color.white; return last.Text; }
            return last.Text + "   " + "HH.Thrumbo.Net".Translate((net.Micro >= 0 ? "+" : "") + net.ToStringShort());
        }

        float DrawResult(Rect r, float y)
        {
            var line = new Rect(0, y, r.width, 28f);
            Text.Anchor = TextAnchor.MiddleLeft;
            string text = ResultText(out var col);
            if (!text.NullOrEmpty()) { GUI.color = col; Widgets.Label(line, text); }
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = dim; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y + 28f, r.width, 18f), "HH.Thrumbo.Sealed".Translate());
            Text.Font = GameFont.Small; GUI.color = Color.white;
            return y + 48f;
        }

        // ---- history and totals --------------------------------------------------------------------------------------
        void DrawHistory(Rect r)
        {
            var c = ThrumboComponent.Get(); if (c == null) return;
            var held = HeldEntry;
            Widgets.DrawLineHorizontal(0, r.y, r.width);
            Text.Font = GameFont.Tiny;
            // the round on the table is not in the totals yet
            long hs = 0, hp = 0; int hr = 0; long gs = 0, gp = 0; int gr = 0;
            if (held != null) { if (held.From == (int)PayFrom.Goods) { gs = held.StakedMicro; gp = held.PaidMicro; gr = 1; } else { hs = held.StakedMicro; hp = held.PaidMicro; hr = 1; } }
            int rounds = c.RoundsAll - hr;
            double staked = (c.StakedAll - hs) / (double)Money.PerSilver, paid = (c.PaidAll - hp) / (double)Money.PerSilver;
            string tot = rounds == 0 && c.GoodsRounds - gr == 0 ? "HH.Thrumbo.NoHistory".Translate().ToString()
                : "HH.Thrumbo.Totals".Translate(rounds.ToString("N0"), staked.ToString("N0"), paid.ToString("N0"), staked > 0 ? (paid / staked * 100).ToString("0.0") : "–", ((paid - staked) >= 0 ? "+" : "") + (paid - staked).ToString("N0")).ToString();
            GUI.color = dim; Widgets.Label(new Rect(0, r.y + 2f, r.width, 18f), "<b>" + "HH.Thrumbo.History".Translate() + "</b>   " + tot);
            float head = 22f;
            if (c.GoodsRounds - gr > 0)
            {
                double gss = (c.GoodsStaked - gs) / (double)Money.PerSilver, gpp = (c.GoodsPaid - gp) / (double)Money.PerSilver;
                Widgets.Label(new Rect(0, r.y + 20f, r.width, 18f), "HH.Thrumbo.TotalsGoods".Translate((c.GoodsRounds - gr).ToString("N0"), gss.ToString("N0"), gpp.ToString("N0"), ((gpp - gss) >= 0 ? "+" : "") + (gpp - gss).ToString("N0")));
                head = 40f;
            }
            GUI.color = Color.white;
            var list = new Rect(0, r.y + head, r.width, r.height - head);
            float rowH = 20f;
            var entries = HistoryView();
            int n = entries.Count;
            var view = new Rect(0, 0, list.width - 18f, Math.Max(list.height, n * rowH));
            Widgets.BeginScrollView(list, ref histScroll, view);
            int now = Find.TickManager.TicksGame;
            float wAgo = 96f, wGame = 96f, wStake = 110f, wNet = 100f;
            for (int i = 0; i < n; i++)
            {
                var e = entries[i]; float yy = i * rowH;
                if (yy + rowH < histScroll.y || yy > histScroll.y + list.height) continue;
                var row = new Rect(0, yy, view.width, rowH);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = dim; Widgets.Label(new Rect(2f, yy, wAgo, rowH), (now - e.Tick).ToStringTicksToPeriod(false, true, false, false) + " " + "HH.Thrumbo.Ago".Translate());
                GUI.color = Color.white; Widgets.Label(new Rect(wAgo + 4f, yy, wGame, rowH), ("HH.Thrumbo.Game." + (ThrumboGame)e.Game).Translate());
                float tx = wAgo + wGame + 8f, tw = view.width - tx - wStake - wNet - 8f;
                Text.WordWrap = false;
                Widgets.Label(new Rect(tx, yy, tw, rowH), (e.Text ?? "").Truncate(tw));
                Text.WordWrap = true;
                if (Mouse.IsOver(row) && !e.Text.NullOrEmpty()) TooltipHandler.TipRegion(row, e.Text);
                var net = e.Net; GUI.color = net.Micro > 0 ? good : (net.Micro < 0 ? bad : Color.white);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(view.width - wStake - wNet - 4f, yy, wStake, rowH), "HH.Thrumbo.StakeShort".Translate(new Money(e.StakedMicro).ToStringShort()));
                Widgets.Label(new Rect(view.width - wNet - 2f, yy, wNet, rowH), (net.Micro >= 0 ? "+" : "") + net.ToStringShort());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }
    }
}
