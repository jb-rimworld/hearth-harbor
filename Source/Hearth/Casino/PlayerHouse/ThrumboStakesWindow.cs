// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>
    /// "Raise the stakes…" of The Lucky Thrumbo (KONZEPT 6.13, K15/K16): an own stake dialog, no patch on Dialog_Trade. The list is vanilla's
    /// Tradeable list of an orbital trade (beacon range, sellable colony pawns), valued by the house's pawnbroker; one sealed round of slots or
    /// roulette decides. Same look as the Thrumbo window, with the help line (K28) at the bottom.
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
        Vector2 scroll;
        string notice; Color noticeColor = Color.white;
        StakeResult last;

        public override Vector2 InitialSize => new Vector2(900f, 720f);
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
                float y = DrawHeader(inRect);
                y = DrawTabs(inRect, y);
                float bottom = inRect.height - 84f;
                DrawList(new Rect(0, y, 540f, bottom - y));
                DrawPanel(new Rect(556f, y, inRect.width - 556f, bottom - y));
                DrawResult(new Rect(0, bottom + 4f, inRect.width, 54f));
                CasinoUI.DrawHelpHint(new Rect(0, inRect.height - 22f, inRect.width, 20f));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }

        // ---- header ------------------------------------------------------------------------------------------------
        float DrawHeader(Rect r)
        {
            GUI.DrawTexture(new Rect(0, 0, 210f, 52f), ThrumboIcons.Logo, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Medium; GUI.color = new Color(0.97f, 0.93f, 0.82f);
            Widgets.Label(new Rect(220f, 2f, r.width - 220f, 32f), "HH.Thrumbo.Stakes.Title".Translate());
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.8f, 0.82f, 0.9f);
            Widgets.Label(new Rect(220f, 32f, r.width - 220f, 30f), "HH.Thrumbo.Stakes.Intro".Translate());
            GUI.color = dim;
            string who = offer.Negotiator == null ? "HH.Thrumbo.Stakes.NoNegotiator".Translate().ToString()
                : "HH.Thrumbo.Stakes.ValuedBy".Translate(offer.Negotiator.LabelShort, offer.Negotiator.GetStatValue(StatDefOf.TradePriceImprovement).ToStringPercent()).ToString();
            if (!offer.Beacon) who += "   " + "HH.Thrumbo.Stakes.NoBeacon".Translate();
            Widgets.Label(new Rect(0, 62f, r.width - 110f, 18f), who);
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(r.width - 100f, 58f, 100f, 24f), "HH.Thrumbo.Stakes.Refresh".Translate())) { Refresh(); notice = null; }
            return 88f;
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
                if (Choice(new Rect(x, y, 170f, 28f), ("HH.Thrumbo.Stakes.Tab." + k).Translate(n.ToString()), tab == k, enabled, tip)) tab = k;
                x += 176f;
            }
            return y + 36f;
        }

        static bool Choice(Rect r, string label, bool on, bool enabled, string tip)
        {
            if (on) Widgets.DrawBoxSolid(r, new Color(0.25f, 0.45f, 0.30f, 0.55f));
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            bool click = Widgets.ButtonText(r, label, !on, true, enabled);
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
            const float rowH = 30f;
            var view = new Rect(0, 0, inner.width - 16f, lines.Count * rowH);
            Widgets.BeginScrollView(inner, ref scroll, view);
            double factor = ThrumboStakes.Factor;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i]; float yy = i * rowH;
                if (yy + rowH < scroll.y || yy > scroll.y + inner.height) continue;
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
                Widgets.Label(new Rect(58f, yy, 190f, rowH), l.Label.Truncate(190f));
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(250f, yy, 50f, rowH), "×" + l.Held);
                Widgets.Label(new Rect(298f, yy, 66f, rowH), Money.FromSilver(l.UnitPrice * factor).ToStringShort());
                Text.Font = GameFont.Small; GUI.color = Color.white; Text.WordWrap = true;
                TooltipHandler.TipRegion(new Rect(250f, yy, 114f, rowH), "HH.Thrumbo.Stakes.UnitTip".Translate(l.UnitPrice.ToStringMoney(), (ThrumboStakes.Spread * 100).ToString("0.#"), (ThrumboStakes.Fee * 100).ToString("0.#"), Money.FromSilver(l.UnitPrice * factor).ToStringShort()));
                if (thing != null && Mouse.IsOver(new Rect(28f, yy, 220f, rowH))) TooltipHandler.TipRegion(new Rect(28f, yy, 220f, rowH), l.Tradeable.TipDescription);
                Text.Anchor = TextAnchor.UpperLeft;
                // count controls
                float x = 368f; int held = l.Held;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, 24f, 24f), "0")) Set(l, 0); x += 26f;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, 24f, 24f), "−")) Set(l, l.Count - Step()); x += 26f;
                int c = l.Count; string buf = l.Buffer ?? c.ToString();
                Widgets.TextFieldNumeric(new Rect(x, yy + 3f, 46f, 24f), ref c, ref buf, 0, held);
                l.Count = Math.Max(0, Math.Min(held, c)); l.Buffer = buf; x += 48f;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, 24f, 24f), "+")) Set(l, l.Count + Step()); x += 26f;
                if (Widgets.ButtonText(new Rect(x, yy + 3f, view.width - x, 24f), "HH.Thrumbo.Stakes.All".Translate())) Set(l, held);
            }
            Widgets.EndScrollView();
        }

        static int Step() => Event.current != null && Event.current.shift ? 10 : 1;
        static void Set(StakeLine l, int v) { l.Count = Math.Max(0, Math.Min(l.Held, v)); l.Buffer = l.Count.ToString(); }

        // ---- game, value, button -------------------------------------------------------------------------------------
        void DrawPanel(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0.10f, 0.13f, 0.11f, 0.55f)); Widgets.DrawBox(r, 1);
            var a = r.ContractedBy(8f); float y = a.y;
            Widgets.Label(new Rect(a.x, y + 3f, 70f, 24f), "HH.Thrumbo.Stakes.Game".Translate());
            if (Choice(new Rect(a.x + 74f, y, 110f, 28f), "HH.Thrumbo.Game.Roulette".Translate(), game == ThrumboGame.Roulette, true, null)) game = ThrumboGame.Roulette;
            if (Choice(new Rect(a.x + 190f, y, 110f, 28f), "HH.Thrumbo.Game.Slots".Translate(), game == ThrumboGame.Slots, true, null)) game = ThrumboGame.Slots;
            y += 36f;
            double shift = TS.Shift;
            if (game == ThrumboGame.Slots)
            {
                float vx = a.x;
                foreach (SlotVolatility v in Enum.GetValues(typeof(SlotVolatility)))
                {
                    var p = ThrumboOdds.Slots(v, shift);
                    if (Choice(new Rect(vx, y, 96f, 26f), ("HH.Thrumbo.Vol." + v).Translate(), vol == v, true, "HH.Thrumbo.VolTip".Translate((p.HitRate * 100).ToString("0.#"), p.MaxMultiplier.ToString("N0")))) vol = v;
                    vx += 100f;
                }
                y += 30f;
                GUI.color = dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(a.x, y, a.width, 18f), "HH.Thrumbo.Paytable".Translate((ThrumboOdds.SlotRtp(shift) * 100).ToString("0.#")));
                GUI.color = Color.white; Text.Font = GameFont.Small;
                y += 48f;
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
                    if (Choice(rect, ("HH.Thrumbo.Roulette." + keys[i]).Translate(), bet.Kind == b.Kind && bet.Label == b.Label, true, "HH.Thrumbo.Roulette.OutsideTip".Translate(b.Label, b.Odds.ToString()))) bet = b;
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
            GUI.color = value > limit ? bad : Color.white;
            Widgets.Label(valueRect, "HH.Thrumbo.Stakes.Value".Translate(value.ToStringShort()) + " (?)");
            GUI.color = Color.white; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(valueRect, "HH.Thrumbo.Stakes.ValueTip".Translate(Money.FromSilver(raw).ToStringShort(), (ThrumboStakes.Spread * 100).ToString("0.#"), (ThrumboStakes.Fee * 100).ToString("0.#"), value.ToStringShort(), offer.Negotiator?.LabelShort ?? "–"));
            if (Mouse.IsOver(valueRect)) Widgets.DrawHighlight(valueRect);
            y += 32f;
            GUI.color = dim; Text.Font = GameFont.Tiny;
            var limRect = new Rect(a.x, y, a.width, 18f);
            Widgets.Label(limRect, "HH.Thrumbo.Limit".Translate(TS.MinStake.ToStringShort(), limit.ToStringShort()));
            TooltipHandler.TipRegion(limRect, "HH.Thrumbo.LimitTip".Translate((HSettings.Get(TS.MaxBetShare) * 100).ToString("0.#"), HSettings.GetSilver(TS.MaxBetFloor).ToStringShort(), HSettings.GetSilver(TS.MaxBetCap).ToStringShort()));
            y += 20f;
            Widgets.Label(new Rect(a.x, y, a.width, 46f), "HH.Thrumbo.Stakes.Rule".Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            // the button
            var btn = new Rect(a.x, a.yMax - 40f, a.width, 38f);
            string why = ThrumboStakes.Validate(offer, false);
            bool can = why == null;
            if (!can) GUI.color = new Color(1f, 1f, 1f, 0.5f);
            if (Widgets.ButtonText(btn, (game == ThrumboGame.Slots ? "HH.Thrumbo.Stakes.GoSlots" : "HH.Thrumbo.Stakes.GoRoulette").Translate(), true, true, can) && can) Confirm();
            GUI.color = Color.white;
            if (!can) TooltipHandler.TipRegion(btn, why);
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
            last = r; notice = null;
            Refresh();
        }

        void DrawResult(Rect r)
        {
            var line = new Rect(r.x, r.y, r.width, 28f);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (!notice.NullOrEmpty()) { GUI.color = noticeColor; Widgets.Label(line, notice); }
            else if (last != null)
            {
                GUI.color = last.Kept ? (last.Mult > 1.0 ? good : Color.white) : bad;
                Widgets.Label(line, last.Text);
            }
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = dim; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(r.x, r.y + 30f, r.width, 18f), "HH.Thrumbo.Sealed".Translate());
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }
    }
}
