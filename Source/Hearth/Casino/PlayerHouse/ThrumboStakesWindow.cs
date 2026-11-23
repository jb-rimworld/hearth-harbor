// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;
using Hearth.Casino.Anim;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>
    /// "Raise the stakes…" of The Lucky Thrumbo (KONZEPT 6.13, K15/K16): an own stake dialog, no patch on Dialog_Trade. The list is vanilla's
    /// Tradeable list of an orbital trade (beacon range, sellable colony pawns), valued by the house's pawnbroker; one sealed round of slots or
    /// roulette decides. Same look as the Thrumbo window, with the help line (K28) at the bottom.
    /// 1.1.0 (F30/F35): larger, columns by fraction, and the round plays out on a small table (reels or wheel) before the result shows.
    /// </summary>
    public class Window_ThrumboStakes : Window
    {
        readonly Map map;
        StakeOffer offer;
        StakeKind tab = StakeKind.Goods;
        ThrumboGame game = ThrumboGame.Roulette;
        SlotVolatility vol = SlotVolatility.Medium;
        RouletteBet bet = RouletteBet.Red();
        PayFrom winTo;
        Vector2 scroll, howScroll;
        string notice; Color noticeColor = Color.white;
        StakeResult last, pending;
        public readonly SlotMachineView Slots = new SlotMachineView();
        public readonly RouletteView Wheel = new RouletteView();
        ulong uiSeed = (ulong)DateTime.Now.Ticks ^ 0x5354414BUL;
        public bool Busy => Slots.Busy || Wheel.Busy;

        public override Vector2 InitialSize => HUI.Size(1040f, 800f, 900f, 660f);
        public bool ShowsHelpHint => CasinoUI.HelpHintText() != null;
        public StakeOffer Offer => offer;

        static readonly Color dim = new Color(0.72f, 0.72f, 0.72f), good = new Color(0.6f, 0.9f, 0.6f), bad = new Color(1f, 0.58f, 0.48f), warn = new Color(1f, 0.75f, 0.45f);

        public Window_ThrumboStakes(Map map, PayFrom winTo)
        {
            this.map = map;
            this.winTo = BankApi.Ready ? winTo : PayFrom.Silver;
            doCloseX = true; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; preventCameraMotion = false; forcePause = false;   // like the Thrumbo window; the round re-checks that every staked thing is still in reach
            Refresh();
        }

        public static Window_ThrumboStakes Open(Map map, PayFrom winTo)
        {
            if (!ThrumboStakes.Enabled) return null;
            var open = Find.WindowStack.WindowOfType<Window_ThrumboStakes>();
            if (open != null) { Find.WindowStack.Notify_ManuallySetFocus(open); return open; }
            var w = new Window_ThrumboStakes(map, winTo);
            Find.WindowStack.Add(w);
            return w;
        }

        void Refresh() { offer = Guard.Run(ModuleId.Casino, "Thrumbo.Stakes.Offer", () => ThrumboStakes.Offer(map), new StakeOffer { Map = map }); }

        public override void DoWindowContents(Rect inRect)
        {
            if (!ThrumboStakes.Enabled) { Close(false); return; }
            Guard.Run(ModuleId.Casino, "Thrumbo.Stakes.Draw", () => Draw(inRect));
        }

        void Draw(Rect inRect)
        {
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            try
            {
                if (pending != null && !Busy) { last = pending; pending = null; }
                float y = DrawHeader(inRect);
                y = DrawTabs(inRect, y);
                float bottom = inRect.height - 92f;
                float panelW = Mathf.Max(360f, inRect.width * 0.42f);
                DrawList(new Rect(0, y, inRect.width - panelW - 14f, bottom - y));
                DrawPanel(new Rect(inRect.width - panelW, y, panelW, bottom - y));
                DrawResult(new Rect(0, bottom + 6f, inRect.width, 62f));
                CasinoUI.DrawHelpHint(new Rect(0, inRect.height - 22f, inRect.width, 20f));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }

        // ---- header ------------------------------------------------------------------------------------------------
        float DrawHeader(Rect r)
        {
            float lw = Mathf.Min(300f, r.width * 0.28f);
            GUI.DrawTexture(new Rect(0, 0, lw, lw / 6f * 1.2f), ThrumboIcons.Logo, ScaleMode.ScaleToFit);
            float tx = lw + 12f;
            Text.Font = GameFont.Medium; GUI.color = new Color(0.97f, 0.93f, 0.82f);
            Widgets.Label(new Rect(tx, 0f, r.width - tx, 32f), "HH.Thrumbo.Stakes.Title".Translate());
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.8f, 0.82f, 0.9f);
            string intro = "HH.Thrumbo.Stakes.Intro".Translate();
            float ih = Mathf.Max(30f, Text.CalcHeight(intro, r.width - tx));
            Widgets.Label(new Rect(tx, 30f, r.width - tx, ih), intro);
            float wy = Mathf.Max(62f, 30f + ih + 2f);
            GUI.color = dim;
            string who = offer.Negotiator == null ? "HH.Thrumbo.Stakes.NoNegotiator".Translate().ToString()
                : "HH.Thrumbo.Stakes.ValuedBy".Translate(offer.Negotiator.LabelShort, offer.Negotiator.GetStatValue(StatDefOf.TradePriceImprovement).ToStringPercent()).ToString();
            if (!offer.Beacon) who += "   " + "HH.Thrumbo.Stakes.NoBeacon".Translate();
            Widgets.Label(new Rect(0, wy, r.width - 130f, 18f), who);
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(r.width - 120f, wy - 4f, 120f, 26f), "HH.Thrumbo.Stakes.Refresh".Translate(), true, true, !Busy) && !Busy) { Refresh(); notice = null; }
            return wy + 28f;
        }

        float DrawTabs(Rect r, float y)
        {
            float x = 0;
            foreach (StakeKind k in Enum.GetValues(typeof(StakeKind)))
            {
                bool pawns = k != StakeKind.Goods;
                bool enabled = !pawns || ThrumboStakes.PawnsAllowed;
                int n = offer.Lines.Count(l => l.Kind == k);
                string tip = enabled ? null : "HH.Thrumbo.Stakes.PawnsCozy".Translate().ToString();
                string label = ("HH.Thrumbo.Stakes.Tab." + k).Translate(n.ToString());
                float w = Mathf.Max(150f, Text.CalcSize(label).x + 28f);
                if (Choice(new Rect(x, y, w, 28f), label, tab == k, enabled, tip)) tab = k;
                x += w + 6f;
            }
            return y + 36f;
        }

        static bool Choice(Rect r, string label, bool on, bool enabled, string tip)
        {
            if (on) Widgets.DrawBoxSolid(r, new Color(0.25f, 0.45f, 0.30f, 0.55f));
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            bool click;
            if (on)
            {
                // selected: a plain, centred label on the green plate (a background-less button would left-align it)
                var oa = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.92f, 1f, 0.92f, enabled ? 1f : 0.45f);
                Widgets.Label(r, "<b>" + label + "</b>"); Text.Anchor = oa; click = false;
            }
            else click = Widgets.ButtonText(r, label, true, true, enabled);
            GUI.color = Color.white;
            if (on) Widgets.DrawBox(r, 1);
            if (tip != null) TooltipHandler.TipRegion(r, tip);
            return click && enabled && !on;
        }

        // ---- the list ----------------------------------------------------------------------------------------------
        void DrawList(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0.10f, 0.12f, 0.14f, 0.45f)); Widgets.DrawBox(r, 1);
            var lines = offer.Lines.Where(l => l.Kind == tab).ToList();
            var inner = r.ContractedBy(4f);
            if (lines.Count == 0)
            {
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(inner, ("HH.Thrumbo.Stakes.Empty." + tab).Translate());
                GUI.color = Color.white; Text.Font = GameFont.Small;
                return;
            }
            const float rowH = 30f, headH = 20f;
            // column header
            float wLabel = Mathf.Max(150f, inner.width - 16f - 360f), xHeld = 58f + wLabel + 4f, xUnit = xHeld + 54f, xCtl = xUnit + 74f;
            Text.Font = GameFont.Tiny; GUI.color = dim;
            Widgets.Label(new Rect(inner.x + 58f, inner.y, wLabel, headH), "HH.Thrumbo.Stakes.ColItem".Translate());
            Widgets.Label(new Rect(inner.x + xHeld, inner.y, 52f, headH), "HH.Thrumbo.Stakes.ColHeld".Translate());
            Widgets.Label(new Rect(inner.x + xUnit, inner.y, 72f, headH), "HH.Thrumbo.Stakes.ColEach".Translate());
            Widgets.Label(new Rect(inner.x + xCtl, inner.y, 160f, headH), "HH.Thrumbo.Stakes.ColStake".Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            var listRect = new Rect(inner.x, inner.y + headH, inner.width, inner.height - headH);
            var view = new Rect(0, 0, listRect.width - 16f, lines.Count * rowH);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            double factor = ThrumboStakes.Factor;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i]; float yy = i * rowH;
                if (yy + rowH < scroll.y || yy > scroll.y + listRect.height) continue;
                var row = new Rect(0, yy, view.width, rowH);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                if (l.Count > 0) Widgets.DrawBoxSolid(row, new Color(0.95f, 0.82f, 0.45f, 0.10f));
                var thing = l.Any;
                if (thing != null)
                {
                    Widgets.InfoCardButton(2f, yy + 3f, thing);
                    Widgets.ThingIcon(new Rect(28f, yy + 2f, 26f, 26f), thing);
                }
                Text.Anchor = TextAnchor.MiddleLeft; Text.WordWrap = false;
                Widgets.Label(new Rect(58f, yy, wLabel, rowH), l.Label.Truncate(wLabel));
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(xHeld, yy, 52f, rowH), "×" + l.Held);
                Widgets.Label(new Rect(xUnit, yy, 72f, rowH), Money.FromSilver(l.UnitPrice * factor).ToStringShort());
                Text.Font = GameFont.Small; GUI.color = Color.white; Text.WordWrap = true;
                TooltipHandler.TipRegion(new Rect(xHeld, yy, xCtl - xHeld, rowH), "HH.Thrumbo.Stakes.UnitTip".Translate(l.UnitPrice.ToStringMoney(), (ThrumboStakes.Spread * 100).ToString("0.#"), (ThrumboStakes.Fee * 100).ToString("0.#"), Money.FromSilver(l.UnitPrice * factor).ToStringShort()));
                if (thing != null && Mouse.IsOver(new Rect(28f, yy, wLabel + 30f, rowH))) TooltipHandler.TipRegion(new Rect(28f, yy, wLabel + 30f, rowH), l.Tradeable.TipDescription);
                Text.Anchor = TextAnchor.UpperLeft;
                // count controls
                float x = xCtl; int held = l.Held;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, 26f, 24f), "0")) Set(l, 0); x += 28f;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, 26f, 24f), "−")) Set(l, l.Count - Step()); x += 28f;
                int c = l.Count; string buf = l.Buffer ?? c.ToString();
                Widgets.TextFieldNumeric(new Rect(x, yy + 3f, 54f, 24f), ref c, ref buf, 0, held);
                l.Count = Math.Max(0, Math.Min(held, c)); l.Buffer = buf; x += 56f;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, 26f, 24f), "+")) Set(l, l.Count + Step()); x += 28f;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, Mathf.Max(40f, view.width - x), 24f), "HH.Thrumbo.Stakes.All".Translate())) Set(l, held);
            }
            Widgets.EndScrollView();
            // a short list leaves room: say where the things come from and how the buttons work
            float free = listRect.height - lines.Count * rowH;
            if (free >= 70f)
            {
                string foot = "HH.Thrumbo.Stakes.ListFoot".Translate();
                Text.Font = GameFont.Tiny; GUI.color = dim;
                float fh = Text.CalcHeight(foot, listRect.width - 24f);
                var fr = new Rect(listRect.x + 12f, listRect.yMax - fh - 10f, listRect.width - 24f, fh);
                Widgets.DrawLineHorizontal(fr.x, fr.y - 6f, fr.width);
                Widgets.Label(fr, foot);
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
        }

        static int Step() => Event.current != null && Event.current.shift ? 10 : 1;
        static void Set(StakeLine l, int v) { l.Count = Math.Max(0, Math.Min(l.Held, v)); l.Buffer = l.Count.ToString(); }

        // ---- game, value, button -------------------------------------------------------------------------------------
        void DrawPanel(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0.10f, 0.13f, 0.11f, 0.55f)); Widgets.DrawBox(r, 1);
            var a = r.ContractedBy(8f); float y = a.y;
            bool busy = Busy;
            Text.Anchor = TextAnchor.MiddleLeft;
            float gl = Text.CalcSize("HH.Thrumbo.Stakes.Game".Translate()).x + 8f;
            Widgets.Label(new Rect(a.x, y, gl, 28f), "HH.Thrumbo.Stakes.Game".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            float gw = (a.width - gl - 6f) / 2f;
            if (Choice(new Rect(a.x + gl, y, gw, 28f), "HH.Thrumbo.Game.Roulette".Translate(), game == ThrumboGame.Roulette, !busy, null)) game = ThrumboGame.Roulette;
            if (Choice(new Rect(a.x + gl + gw + 6f, y, gw, 28f), "HH.Thrumbo.Game.Slots".Translate(), game == ThrumboGame.Slots, !busy, null)) game = ThrumboGame.Slots;
            y += 34f;
            // the little table: the round plays out here
            float sh = Mathf.Clamp(a.height * 0.36f, 130f, 300f);
            var stage = new Rect(a.x, y, a.width, sh);
            Widgets.DrawBoxSolid(stage, new Color(0.22f, 0.14f, 0.09f));
            var felt = stage.ContractedBy(3f);
            var oc = GUI.color; GUI.color = new Color(0.82f, 0.95f, 0.86f); AnimDraw.Tiled(felt, CasinoAnimTex.Felt, 160f); GUI.color = oc;
            if (busy && AnimDraw.SkipRequested(stage)) { Slots.Skip(); Wheel.Skip(); }
            var inner = felt.ContractedBy(6f);
            if (game == ThrumboGame.Slots) Slots.Draw(inner, null);
            else
            {
                var rules = ThrumboOdds.Roulette(TS.Shift); if (!Wheel.Busy) Wheel.Zeros = rules.Zeros;
                float wx = inner.center.x - inner.height / 2f;
                Wheel.Draw(new Rect(wx, inner.y, inner.height, inner.height), inner.height >= 180f);
                Wheel.DrawRecent(new Rect(inner.x, inner.yMax - 20f, Mathf.Max(60f, wx - inner.x - 8f), 20f), 6);
            }
            y += sh + 8f;
            double shift = TS.Shift;
            if (game == ThrumboGame.Slots)
            {
                float vx = a.x;
                foreach (SlotVolatility v in Enum.GetValues(typeof(SlotVolatility)))
                {
                    var p = ThrumboOdds.Slots(v, shift);
                    float vw = (a.width - 8f) / 3f;
                    if (Choice(new Rect(vx, y, vw, 26f), ("HH.Thrumbo.Vol." + v).Translate(), vol == v, !busy, "HH.Thrumbo.VolTip".Translate((p.HitRate * 100).ToString("0.#"), p.MaxMultiplier.ToString("N0")))) vol = v;
                    vx += vw + 4f;
                }
                y += 30f;
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(a.x, y, a.width, 18f), "HH.Thrumbo.Paytable".Translate((ThrumboOdds.SlotRtp(shift) * 100).ToString("0.#")));
                GUI.color = Color.white; Text.Font = GameFont.Small;
                y += 22f;
            }
            else
            {
                var bets = new[] { RouletteBet.Red(), RouletteBet.Black(), RouletteBet.Odd(), RouletteBet.Even(), RouletteBet.Low(), RouletteBet.High(), RouletteBet.Dozen(1), RouletteBet.Dozen(2), RouletteBet.Dozen(3) };
                var keys = new[] { "Red", "Black", "Odd", "Even", "Low", "High", "Dozen1", "Dozen2", "Dozen3" };
                float bw = (a.width - 8f) / 3f;
                for (int i = 0; i < bets.Length; i++)
                {
                    var rect = new Rect(a.x + (i % 3) * (bw + 4f), y + (i / 3) * 28f, bw, 26f);
                    var b = bets[i];
                    if (Choice(rect, ("HH.Thrumbo.Roulette." + keys[i]).Translate(), bet.Kind == b.Kind && bet.Label == b.Label, !busy, "HH.Thrumbo.Roulette.OutsideTip".Translate(ThrumboNames.Bet(b), b.Odds.ToString()))) { bet = b; CasinoSfx.Play(CasinoSfx.Chip); }
                }
                y += 3 * 28f + 2f;
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(a.x, y, a.width, 18f), "HH.Thrumbo.Stakes.RouletteEdge".Translate((ThrumboOdds.RouletteEdge(shift) * 100).ToString("0.##")));
                GUI.color = Color.white; Text.Font = GameFont.Small;
                y += 22f;
            }
            // where winnings go
            Widgets.Label(new Rect(a.x, y + 3f, a.width, 24f), "HH.Thrumbo.Stakes.WinTo".Translate());
            y += 26f;
            float hw = (a.width - 6f) / 2f;
            if (Choice(new Rect(a.x, y, hw, 28f), "HH.Thrumbo.Stakes.ToAccount".Translate(), winTo == PayFrom.Bank, BankApi.Ready, BankApi.Ready ? "HH.Thrumbo.FromBankTip".Translate().ToString() : "HH.Thrumbo.Err.NoBank".Translate().ToString())) winTo = PayFrom.Bank;
            if (Choice(new Rect(a.x + hw + 6f, y, hw, 28f), "HH.Thrumbo.Stakes.ToPod".Translate(), winTo != PayFrom.Bank, true, "HH.Thrumbo.Stakes.ToPodTip".Translate())) winTo = PayFrom.Silver;
            if (winTo == PayFrom.Bank && !BankApi.Ready) winTo = PayFrom.Silver;
            y += 36f;
            Widgets.DrawLineHorizontal(a.x, y, a.width); y += 6f;
            // value
            var value = ThrumboStakes.Value(offer); var limit = ThrumboHouse.TableLimit(map);
            double raw = ThrumboStakes.RawValue(offer);
            var valueRect = new Rect(a.x, y, a.width, 26f);
            Text.Font = GameFont.Medium;
            GUI.color = ThrumboHouse.LimitOn && value > limit ? bad : Color.white;
            Widgets.Label(valueRect, "HH.Thrumbo.Stakes.Value".Translate(value.ToStringShort()) + " (?)");
            GUI.color = Color.white; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(valueRect, "HH.Thrumbo.Stakes.ValueTip".Translate(Money.FromSilver(raw).ToStringShort(), (ThrumboStakes.Spread * 100).ToString("0.#"), (ThrumboStakes.Fee * 100).ToString("0.#"), value.ToStringShort(), offer.Negotiator?.LabelShort ?? "–"));
            if (Mouse.IsOver(valueRect)) Widgets.DrawHighlight(valueRect);
            y += 32f;
            GUI.color = dim; Text.Font = GameFont.Tiny;
            var limRect = new Rect(a.x, y, a.width, 18f);
            if (ThrumboHouse.LimitOn)
            {
                Widgets.Label(limRect, "HH.Thrumbo.Limit".Translate(TS.MinStake.ToStringShort(), limit.ToStringShort()));
                TooltipHandler.TipRegion(limRect, "HH.Thrumbo.LimitTip".Translate((HSettings.Get(TS.MaxBetShare) * 100).ToString("0.#"), HSettings.GetSilver(TS.MaxBetFloor).ToStringShort(), HSettings.GetSilver(TS.MaxBetCap).ToStringShort()));
            }
            else { Widgets.Label(limRect, "HH.Thrumbo.Stakes.NoLimit".Translate(TS.MinStake.ToStringShort())); TooltipHandler.TipRegion(limRect, "HH.Thrumbo.NoLimitTip".Translate()); }
            y += 20f;
            string rule = "HH.Thrumbo.Stakes.Rule".Translate();
            float rh = Mathf.Min(Text.CalcHeight(rule, a.width), Mathf.Max(18f, a.yMax - 44f - y));
            Widgets.Label(new Rect(a.x, y, a.width, rh), rule);
            GUI.color = Color.white; Text.Font = GameFont.Small;
            y += rh + 8f;
            // how the chosen game works, in the room that is left above the button
            var how = new Rect(a.x, y, a.width, a.yMax - 48f - y);
            if (how.height >= 60f) DrawHowTo(how);
            // the button: while the round plays out it skips to the result
            var btn = new Rect(a.x, a.yMax - 40f, a.width, 38f);
            if (busy)
            {
                if (Widgets.ButtonText(btn, "HH.Thrumbo.Anim.Skip".Translate())) { Slots.Skip(); Wheel.Skip(); }
                TooltipHandler.TipRegion(btn, "HH.Thrumbo.Anim.SkipHint".Translate());
                return;
            }
            string why = ThrumboStakes.Validate(offer, false);
            bool can = why == null;
            if (!can) GUI.color = new Color(1f, 1f, 1f, 0.5f);
            if (Widgets.ButtonText(btn, (game == ThrumboGame.Slots ? "HH.Thrumbo.Stakes.GoSlots" : "HH.Thrumbo.Stakes.GoRoulette").Translate(), true, true, can) && can) Confirm();
            GUI.color = Color.white;
            if (!can) TooltipHandler.TipRegion(btn, why);
        }

        void DrawHowTo(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0.03f, 0.05f, 0.04f, 0.45f));
            var inner = r.ContractedBy(6f);
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.95f, 0.82f, 0.45f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 18f), "HH.Thrumbo.HowTo.Title".Translate(("HH.Thrumbo.Game." + game).Translate()));
            GUI.color = new Color(0.88f, 0.88f, 0.84f);
            string text = ("HH.Thrumbo.Stakes.HowTo." + game).Translate();
            var outer = new Rect(inner.x, inner.y + 20f, inner.width, inner.height - 20f);
            float h = Text.CalcHeight(text, outer.width - 16f);
            var view = new Rect(0, 0, outer.width - 16f, Math.Max(outer.height, h + 4f));
            Widgets.BeginScrollView(outer, ref howScroll, view);
            Widgets.Label(new Rect(0, 0, view.width, h + 2f), text);
            Widgets.EndScrollView();
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        void Confirm()
        {
            var value = ThrumboStakes.Value(offer);
            string text = "HH.Thrumbo.Stakes.Confirm".Translate(ThrumboStakes.Describe(offer), value.ToStringShort());
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, Play, true));
        }

        void Play()
        {
            var r = game == ThrumboGame.Slots ? ThrumboStakes.PlaySlots(offer, vol, winTo) : ThrumboStakes.PlayRoulette(offer, bet, winTo);
            if (!r.Ok) { notice = r.Error; noticeColor = warn; return; }
            notice = null;
            uiSeed = RngStream.Mix(uiSeed + 0x9E3779B97F4A7C15UL);
            // the round is settled; the table plays it out and the result line waits for it
            if (game == ThrumboGame.Slots && r.Reels != null) Slots.Start(r.Reels, r.Mult, uiSeed);
            else if (r.Pocket >= 0) Wheel.Start(r.Pocket, ThrumboOdds.Roulette(TS.Shift).Zeros, r.Kept && r.Mult > 1.0, r.Mult >= 20, uiSeed);
            if (Busy) { pending = r; last = null; } else last = r;
            Refresh();
        }

        void DrawResult(Rect r)
        {
            var box = new Rect(r.x, r.y, r.width, 40f);
            Widgets.DrawBoxSolid(box, new Color(0.05f, 0.06f, 0.08f, 0.5f)); Widgets.DrawBox(box, 1);
            var line = box.ContractedBy(8f, 4f);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (!notice.NullOrEmpty()) { GUI.color = noticeColor; Widgets.Label(line, notice); }
            else if (pending != null) { GUI.color = dim; Widgets.Label(line, "HH.Thrumbo.Anim.Spinning".Translate()); }
            else if (last != null)
            {
                GUI.color = last.Kept ? (last.Mult > 1.0 ? good : Color.white) : bad;
                Text.Font = GameFont.Medium; Text.WordWrap = false;
                Widgets.Label(line, last.Text.Truncate(line.width));
                Text.WordWrap = true; Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(box, last.Text);
            }
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = dim; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(r.x, r.y + 42f, r.width, 18f), "HH.Thrumbo.Sealed".Translate());
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

#if HH_TESTS
        /// <summary>Tests: is a finished round still held back by the animation?</summary>
        public bool ResultPending => pending != null;
        public StakeResult LastShown => last;
        public void TestPlayNow() { Play(); }
        public void SkipAll() { Slots.Skip(); Wheel.Skip(); }
        public ThrumboGame TestGame { get => game; set => game = value; }
#endif
    }
}
