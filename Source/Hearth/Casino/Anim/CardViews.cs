// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.Anim
{
    /// <summary>One card on the table: where it flies from and to, when it lands and when it turns over.</summary>
    public sealed class CardSprite
    {
        public string Rank; public int Suit; public bool Red;
        public int Row, Index;                // which row (blackjack: 0 dealer, 1 player; poker: seat 0…3, 9 = board) and place in it
        public float DealAt, DealDur = 0.28f; // seconds of the owning animation clock
        public float FlipAt = -1f;            // when a face-down card turns up (−1: never)
        public bool StartsFaceUp = true;
        public bool Lift;                     // raised a little (winning five at the showdown)
        public Vector2 From;                  // fly-in start (screen position, centre)
        public bool Gone;                     // swept away (between hands)
    }

    /// <summary>Card drawing (Thrumbo deck: muffalo, thrumbo horn, gear and healroot suits; J/Q/K are little rim portraits).</summary>
    public static class CardDraw
    {
        public static readonly Color Ink = new Color(0.16f, 0.13f, 0.11f), RedInk = new Color(0.72f, 0.18f, 0.14f);

        /// <summary>Draws a card centred at <paramref name="c"/>. <paramref name="faceUp"/> 0 … 1 is the flip (0.5 = edge on).</summary>
        public static void Card(Vector2 c, Vector2 size, CardSprite s, float faceUp, float alpha = 1f, float tilt = 0f)
        {
            float scaleX = Mathf.Abs(Mathf.Cos(faceUp * Mathf.PI));
            bool front = faceUp >= 0.5f;
            float w = Mathf.Max(1.5f, size.x * scaleX), h = size.y;
            var r = new Rect(c.x - w / 2, c.y - h / 2 - (s != null && s.Lift ? size.y * 0.14f : 0f), w, h);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.28f * alpha); GUI.DrawTexture(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), CasinoAnimTex.CardBack);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (!front || s == null) { GUI.DrawTexture(r, CasinoAnimTex.CardBack); GUI.color = old; return; }
            GUI.DrawTexture(r, CasinoAnimTex.CardFace);
            if (scaleX < 0.35f) { GUI.color = old; return; }
            var ink = s.Red ? RedInk : Ink;
            var suit = CasinoAnimTex.Suits[Mathf.Clamp(s.Suit, 0, 3)];
            float pad = size.x * 0.08f;
            // corner: rank and a small suit, top left (and upside down bottom right on large cards)
            var of = Text.Font; var oa = Text.Anchor;
            Text.Font = size.y >= 84f ? GameFont.Medium : size.y >= 44f ? GameFont.Small : GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(ink.r, ink.g, ink.b, alpha);
            float cornerW = size.x * 0.5f;
            Widgets.Label(new Rect(r.x + pad * 0.6f, r.y + pad * 0.25f, cornerW, size.y * 0.34f), s.Rank);
            float ss = size.x * 0.2f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(r.x + pad * 0.75f, r.y + size.y * 0.27f, ss, ss), suit);
            if (size.y >= 64f)
            {
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = new Color(ink.r, ink.g, ink.b, alpha);
                Widgets.Label(new Rect(r.xMax - cornerW - pad * 0.6f, r.yMax - size.y * 0.34f - pad * 0.25f, cornerW, size.y * 0.34f), s.Rank);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(new Rect(r.xMax - pad * 0.75f - ss, r.yMax - size.y * 0.27f - ss, ss, ss), suit);
            }
            Text.Font = of; Text.Anchor = oa;
            // centre: a portrait for J/Q/K, else the suit large
            int face = s.Rank == "J" ? 0 : s.Rank == "Q" ? 1 : s.Rank == "K" ? 2 : -1;
            float big = Mathf.Min(r.width, size.x) * (face >= 0 ? 0.74f : 0.5f);
            var mid = new Rect(r.center.x - big / 2, r.center.y - big / 2 + size.y * 0.04f, big, big);
            GUI.DrawTexture(mid, face >= 0 ? CasinoAnimTex.Faces[face] : suit);
            GUI.color = old;
        }

        public static CardSprite Blackjack(int value, ulong seed, int index)
        {
            int suit = CardCosmetics.Suit(seed, index);
            return new CardSprite { Rank = CardCosmetics.Rank(value, seed, index), Suit = suit, Red = CardCosmetics.IsRedSuit(suit) };
        }

        public static CardSprite Poker(PCard c) => new CardSprite { Rank = PCard.RankLabel(c.Rank), Suit = c.Suit, Red = CardCosmetics.IsRedSuit(c.Suit) };
    }

    /// <summary>A blackjack table that animates only what changed: new cards fly from the shoe, the hole card turns, the dealer draws, the result shows.</summary>
    public sealed class BlackjackView
    {
        sealed class Step : TableAnim { public Step(float d) { Duration = d; } }
        readonly List<CardSprite> dealer = new List<CardSprite>(), player = new List<CardSprite>();
        readonly List<int> dealerVals = new List<int>(), playerVals = new List<int>();
        Step anim;
        ulong seed; bool holeDown;
        public string Banner; public Color BannerColor = Color.white; float bannerAt = -1f;
        public BlackjackOutcome? Outcome;
        public bool Busy => anim != null && !anim.Done;
        public int CardsShown => dealer.Count + player.Count;
        public readonly Particles Fx = new Particles();
        readonly System.Random ui = new System.Random(77);
        float shakeAt = -1f; int bustRow = -1;
        float chipsAt = -1f; bool chipsToPlayer;
        public static int Deals;

        public void Skip() { anim?.Skip(); }
        public bool Empty => dealer.Count == 0 && player.Count == 0;

        /// <summary>
        /// Brings the table to the given state. A new hand (other seed) clears the table first. <paramref name="holeHidden"/> keeps the
        /// dealer's second card face down (an open hand). <paramref name="outcome"/> and <paramref name="banner"/> show when everything has landed.
        /// </summary>
        public void Sync(IList<int> playerCards, IList<int> dealerCards, bool holeHidden, ulong handSeed, BlackjackOutcome? outcome, string banner, Color bannerColor, bool win, bool animateThis = true)
        {
            bool fresh = handSeed != seed || playerCards.Count < playerVals.Count || dealerCards.Count < dealerVals.Count;
            if (fresh) { dealer.Clear(); player.Clear(); dealerVals.Clear(); playerVals.Clear(); seed = handSeed; holeDown = false; Banner = null; Outcome = null; bustRow = -1; chipsAt = -1f; Deals++; }
            bool animate = animateThis && AnimPrefs.On;
            anim?.Skip();                                    // a running step ends first (its sounds of the result were already queued as final)
            foreach (var c in dealer) Settle(c);
            foreach (var c in player) Settle(c);
            var a = new Step(0.1f);
            float t = 0f;
            if (fresh && animate) { a.At(0f, CasinoSfx.Shuffle, 1f, false, null, 0.6f); t = 0.2f; }
            // the deal order of a fresh hand: player, dealer, player, dealer (the hole)
            if (fresh && playerCards.Count >= 2 && dealerCards.Count >= 2)
            {
                t = Add(a, player, playerVals, playerCards[0], 1, 0, t, true, animate);
                t = Add(a, dealer, dealerVals, dealerCards[0], 0, 0, t, true, animate);
                t = Add(a, player, playerVals, playerCards[1], 1, 1, t, true, animate);
                t = Add(a, dealer, dealerVals, dealerCards[1], 0, 1, t, !holeHidden, animate);
                holeDown = holeHidden;
            }
            for (int i = playerVals.Count; i < playerCards.Count; i++) t = Add(a, player, playerVals, playerCards[i], 1, i, t, true, animate);
            int playerTotal = BlackjackSolver.HandTotal(new List<int>(playerCards));
            if (playerTotal > 21 && playerVals.Count == playerCards.Count && bustRow < 0) { bustRow = 1; float at = t + 0.1f; a.At(at, CasinoSfx.Bust, 1f, true, () => shakeAt = AnimClock.Now); t = at + 0.35f; }
            // the hole card turns, then the dealer draws one at a time
            if (holeDown && !holeHidden && dealer.Count >= 2)
            {
                holeDown = false; var hole = dealer[1];
                hole.FlipAt = animate ? t + 0.15f : 0f; a.At(hole.FlipAt, CasinoSfx.CardFlip);
                t = hole.FlipAt + 0.4f;
            }
            for (int i = dealerVals.Count; i < dealerCards.Count; i++) { t = Add(a, dealer, dealerVals, dealerCards[i], 0, i, animate ? t + 0.25f : t, true, animate); }
            if (outcome != null)
            {
                Outcome = outcome; Banner = banner; BannerColor = bannerColor;
                float at = animate ? t + 0.2f : 0f;
                bannerAt = -1f;
                a.At(at, win ? CasinoSfx.ChipsPush : outcome == BlackjackOutcome.Push || outcome == BlackjackOutcome.DoublePush ? CasinoSfx.ChipsPlace : CasinoSfx.Lose, 1f, true, () => { bannerAt = AnimClock.Now; chipsAt = AnimClock.Now; chipsToPlayer = win; if (win) burstPending = true; });
                t = at + (win ? 0.9f : 0.6f);
            }
            a.Duration = animate ? Math.Max(0.1f, t) : 0f;
            anim = a;
            if (!animate) anim.Skip();
        }
        bool burstPending;

        /// <summary>Cards of an earlier step are on the table already: their times move into the past of the new step's clock.</summary>
        static void Settle(CardSprite c) { c.DealAt = -100f; if (c.FlipAt != -1f) c.FlipAt = -100f; }

        float Add(Step a, List<CardSprite> row, List<int> vals, int value, int rowId, int index, float t, bool faceUp, bool animate)
        {
            var s = CardDraw.Blackjack(value, seed, rowId * 16 + index);
            s.Row = rowId; s.Index = index; s.DealAt = animate ? t : 0f; s.StartsFaceUp = faceUp;
            row.Add(s); vals.Add(value);
            a.At(s.DealAt, CasinoSfx.CardDeal, 0.95f + 0.04f * (index % 3));
            return t + (animate ? 0.3f : 0f);
        }

        /// <summary>Total of the cards that have landed face up in a row (what the player can see).</summary>
        public int VisibleTotal(int rowId)
        {
            var row = rowId == 0 ? dealer : player; var vals = rowId == 0 ? dealerVals : playerVals;
            var seen = new List<int>();
            for (int i = 0; i < row.Count; i++) if (Landed(row[i]) && FaceUp(row[i]) >= 0.5f) seen.Add(vals[i]);
            return seen.Count == 0 ? 0 : BlackjackSolver.HandTotal(seen);
        }

        bool Landed(CardSprite s) => anim == null || anim.Elapsed >= s.DealAt + s.DealDur;
        float FaceUp(CardSprite s)
        {
            if (s.StartsFaceUp) return 1f;
            if (s.FlipAt == -1f) return 0f;
            if (anim == null) return 1f;
            return Mathf.Clamp01((anim.Elapsed - s.FlipAt) / 0.3f);
        }

        public void Draw(Rect stage)
        {
            anim?.Update();
            float now = AnimClock.Now;
            float ch = Mathf.Clamp(stage.height * 0.38f, 54f, 150f), cw = ch * 0.7f;
            var size = new Vector2(cw, ch);
            var shoe = new Rect(stage.xMax - cw * 1.7f, stage.y + 8f, cw * 1.45f, ch * 0.9f);
            GUI.DrawTexture(shoe, CasinoAnimTex.Shoe);
            var shoeMouth = new Vector2(shoe.x + shoe.width * 0.25f, shoe.y + shoe.height * 0.55f);
            DrawRow(dealer, new Vector2(stage.center.x, stage.y + ch * 0.62f + 6f), size, shoeMouth, now, 0);
            DrawRow(player, new Vector2(stage.center.x, stage.yMax - ch * 0.62f - 6f), size, shoeMouth, now, 1);
            // the chips: a small pile in the middle slides to the winner or to the house
            if (chipsAt > 0f)
            {
                float u = Mathf.Clamp01((now - chipsAt) / 0.45f);
                var from = new Vector2(stage.center.x + cw * 2.2f, stage.center.y);
                var to = chipsToPlayer ? new Vector2(stage.center.x + cw * 2.2f, stage.yMax - ch * 0.4f) : new Vector2(stage.center.x, stage.y - 10f);
                var p = AnimDraw.Lerp(from, to, (float)Ease.OutCubic(u));
                float a = chipsToPlayer ? 1f : 1f - u;
                var old = GUI.color;
                for (int k = 0; k < 4; k++) { GUI.color = new Color(ChipColor(k).r, ChipColor(k).g, ChipColor(k).b, a); GUI.DrawTexture(new Rect(p.x - 13f + k * 4f, p.y - 13f - k * 4f, 26f, 26f), CasinoAnimTex.Chip); }
                GUI.color = old;
                if (burstPending && u >= 1f) { burstPending = false; Fx.Sparkles(new Rect(p.x - 40f, p.y - 30f, 80f, 60f), 12, ui); }
            }
            Fx.UpdateAndDraw(Vector2.zero, stage.ExpandedBy(40f));
        }

        public static Color ChipColor(int k) { switch (k % 4) { case 0: return new Color(0.78f, 0.22f, 0.18f); case 1: return new Color(0.25f, 0.55f, 0.36f); case 2: return new Color(0.3f, 0.45f, 0.72f); default: return new Color(0.86f, 0.68f, 0.28f); } }

        void DrawRow(List<CardSprite> row, Vector2 centre, Vector2 size, Vector2 shoe, float now, int rowId)
        {
            int n = row.Count; if (n == 0) return;
            float step = Mathf.Min(size.x * 0.78f, 380f / Math.Max(1, n));
            float shake = rowId == bustRow && shakeAt > 0f ? (float)Ease.Shake((now - shakeAt) / 0.4f) * 4f : 0f;
            for (int i = 0; i < n; i++)
            {
                var s = row[i];
                var target = new Vector2(centre.x + (i - (n - 1) / 2f) * step + shake, centre.y);
                float u = anim == null ? 1f : Mathf.Clamp01((anim.Elapsed - s.DealAt) / s.DealDur);
                if (u <= 0f) continue;
                var pos = AnimDraw.Arc(shoe, target, (float)Ease.InOutCubic(u), size.y * 0.25f);
                float flip = FaceUp(s);
                if (u < 1f && !s.StartsFaceUp) flip = 0f;
                // a dealt card turns over in the last third of its flight
                float face = s.StartsFaceUp ? (u < 0.66f ? 0f : Mathf.Clamp01((u - 0.66f) / 0.34f)) : flip;
                if (anim == null || anim.Done) face = s.StartsFaceUp ? 1f : flip;
                CardDraw.Card(pos, size, s, face);
                if (rowId == bustRow && face >= 1f && bustRow >= 0 && Landed(s)) { var old = GUI.color; GUI.color = new Color(0.8f, 0.1f, 0.05f, 0.18f); GUI.DrawTexture(new Rect(pos.x - size.x / 2, pos.y - size.y / 2, size.x, size.y), BaseContent.WhiteTex); GUI.color = old; }
            }
        }

        /// <summary>The result plate in the middle of the table (after everything has landed).</summary>
        public void DrawBanner(Rect stage)
        {
            if (Banner.NullOrEmpty() || bannerAt < 0f) return;
            float u = Mathf.Clamp01((AnimClock.Now - bannerAt) / 0.3f);
            float h = 38f, w = Mathf.Min(stage.width * 0.6f, Mathf.Max(220f, Text.CalcSize(Banner).x + 60f));
            var r = new Rect(stage.center.x - w / 2, stage.center.y - h / 2, w, h);
            r = r.ScaledBy(0.7f + 0.3f * (float)Ease.OutBack(u));
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, u); GUI.DrawTexture(r, CasinoAnimTex.BigWin);
            GUI.color = old;
            AnimDraw.ShadowLabel(r, Banner, new Color(BannerColor.r, BannerColor.g, BannerColor.b, u), GameFont.Medium);
        }
    }

    /// <summary>The practice Rimdeck table: four seats, hole cards, flop, turn and river, the showdown and the pot sliding to the winner.</summary>
    public sealed class PokerView
    {
        sealed class Step : TableAnim { public Step(float d) { Duration = d; } }
        Step anim; PokerDeal deal; readonly List<CardSprite> cards = new List<CardSprite>();
        public string Banner; public bool YouWon;
        float showdownAt, potAt;
        public bool Busy => anim != null && !anim.Done;
        public PokerDeal Deal => deal;
        public readonly Particles Fx = new Particles();
        readonly System.Random ui = new System.Random(99);
        public static int Hands;

        public void Skip() { anim?.Skip(); }

        public void Start(PokerDeal d, string banner, bool youWon)
        {
            Hands++;
            deal = d; Banner = banner; YouWon = youWon; cards.Clear(); Fx.Clear();
            var a = new Step(1f); bool animate = AnimPrefs.On; float t = 0f;
            a.At(0f, CasinoSfx.Shuffle);
            t = 0.45f;
            int seats = d.Hole.Length;
            var winning = new HashSet<int>(); foreach (var c in d.WinningFive) winning.Add(c.Id);
            for (int round = 0; round < 2; round++)
                for (int s = 0; s < seats; s++)
                {
                    var cs = CardDraw.Poker(d.Hole[s][round]); cs.Row = s; cs.Index = round; cs.DealAt = t; cs.DealDur = 0.22f;
                    cs.StartsFaceUp = s == 0;
                    if (s != 0) cs.FlipAt = 0f;             // set at the showdown below
                    cs.Lift = winning.Contains(d.Hole[s][round].Id) && s == d.Winner;
                    cards.Add(cs); a.At(t, CasinoSfx.CardDeal, 0.95f + 0.03f * s); t += 0.12f;
                }
            float[] boardAt = { t + 0.5f, t + 0.62f, t + 0.74f, t + 1.4f, t + 2.05f };
            for (int b = 0; b < 5; b++)
            {
                var cs = CardDraw.Poker(d.Board[b]); cs.Row = 9; cs.Index = b; cs.DealAt = boardAt[b]; cs.DealDur = 0.22f; cs.StartsFaceUp = false; cs.FlipAt = boardAt[b] + 0.22f;
                cs.Lift = winning.Contains(d.Board[b].Id);
                cards.Add(cs); a.At(cs.DealAt, CasinoSfx.CardDeal); a.At(cs.FlipAt, CasinoSfx.CardFlip, 1f + 0.03f * b);
            }
            showdownAt = boardAt[4] + 0.75f;
            foreach (var cs in cards) if (cs.Row != 0 && cs.Row != 9) cs.FlipAt = showdownAt;
            a.At(showdownAt, CasinoSfx.CardFlip);
            potAt = showdownAt + 0.7f;
            a.At(potAt, CasinoSfx.ChipsPush, 1f, true);
            a.At(potAt + 0.05f, youWon ? CasinoSfx.Win : null, 1f, true);
            a.Duration = potAt + 1.0f;
            anim = a;
            if (!animate) anim.Skip();
        }

        Vector2 SeatPos(Rect r, int seat)
        {
            switch (seat)
            {
                case 0: return new Vector2(r.center.x, r.yMax - r.height * 0.16f);
                case 1: return new Vector2(r.x + r.width * 0.13f, r.center.y);
                case 2: return new Vector2(r.center.x, r.y + r.height * 0.16f);
                default: return new Vector2(r.xMax - r.width * 0.13f, r.center.y);
            }
        }

        public void Draw(Rect stage, string[] seatNames)
        {
            anim?.Update();
            if (deal == null) return;
            float now = AnimClock.Now; float el = anim?.Elapsed ?? 999f;
            float ch = Mathf.Clamp(stage.height * 0.2f, 44f, 92f), cw = ch * 0.7f; var size = new Vector2(cw, ch);
            var dealerPos = new Vector2(stage.center.x, stage.center.y - ch * 0.2f);
            bool showdown = el >= showdownAt;
            // names
            for (int s = 0; s < deal.Hole.Length && s < 4; s++)
            {
                var p = SeatPos(stage, s);
                var lr = new Rect(p.x - 90f, s == 0 ? p.y - ch * 0.95f - 20f : s == 2 ? p.y + ch * 0.55f : p.y + ch * 0.55f, 180f, 20f);
                bool won = showdown && s == deal.Winner;
                AnimDraw.ShadowLabel(lr, (seatNames != null && s < seatNames.Length ? seatNames[s] : "") + (won ? "  ★" : ""), won ? new Color(1f, 0.88f, 0.4f) : new Color(0.92f, 0.9f, 0.84f), GameFont.Tiny);
            }
            foreach (var cs in cards)
            {
                float u = Mathf.Clamp01((el - cs.DealAt) / cs.DealDur); if (u <= 0f) continue;
                Vector2 target;
                if (cs.Row == 9) target = new Vector2(stage.center.x + (cs.Index - 2) * cw * 1.08f, stage.center.y);
                else { var p = SeatPos(stage, cs.Row); target = new Vector2(p.x + (cs.Index - 0.5f) * cw * 0.62f, p.y); }
                var pos = AnimDraw.Arc(dealerPos, target, (float)Ease.InOutCubic(u), ch * 0.2f);
                float face = cs.StartsFaceUp ? (u < 1f ? 0f : 1f) : (cs.FlipAt >= 0f ? Mathf.Clamp01((el - cs.FlipAt) / 0.28f) : 0f);
                if (cs.StartsFaceUp && u >= 0.66f) face = Mathf.Clamp01((u - 0.66f) / 0.34f);
                bool dim = showdown && el >= showdownAt + 0.35f && !cs.Lift;
                var saveLift = cs.Lift; if (!showdown) cs.Lift = false;
                if (showdown && cs.Lift && face >= 1f) AnimDraw.Glow(pos - new Vector2(0, ch * 0.14f), cw * 0.95f, new Color(1f, 0.85f, 0.35f, 0.32f));
                CardDraw.Card(pos, size, cs, face, dim ? 0.55f : 1f);
                cs.Lift = saveLift;
            }
            // the pot: chips in the middle, sliding to the winner
            if (el > 0.3f)
            {
                float u = Mathf.Clamp01((el - potAt) / 0.5f);
                var potC = new Vector2(stage.center.x, stage.center.y + ch * 0.85f);
                var to = SeatPos(stage, deal.Winner);
                var p = AnimDraw.Lerp(potC, to, (float)Ease.OutCubic(u));
                var old = GUI.color;
                for (int k = 0; k < 6; k++) { var cc = BlackjackView.ChipColor(k); GUI.color = new Color(cc.r, cc.g, cc.b, u >= 1f ? Mathf.Clamp01(1.5f - (el - potAt - 0.5f)) : 1f); GUI.DrawTexture(new Rect(p.x - 12f + (k % 3) * 8f, p.y - 12f - (k / 3) * 6f, 24f, 24f), CasinoAnimTex.Chip); }
                GUI.color = old;
            }
            if (showdown && el >= potAt && !Banner.NullOrEmpty())
            {
                float u = Mathf.Clamp01((el - potAt) / 0.3f);
                float w = Mathf.Min(stage.width * 0.7f, Mathf.Max(240f, Text.CalcSize(Banner).x + 60f));
                var r = new Rect(stage.center.x - w / 2, stage.center.y - ch * 1.25f - 19f, w, 38f).ScaledBy(0.7f + 0.3f * (float)Ease.OutBack(u));
                var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, u); GUI.DrawTexture(r, CasinoAnimTex.BigWin); GUI.color = old;
                AnimDraw.ShadowLabel(r, Banner, YouWon ? new Color(0.8f, 1f, 0.75f, u) : new Color(1f, 0.95f, 0.85f, u), GameFont.Small);
            }
            Fx.UpdateAndDraw(Vector2.zero, stage.ExpandedBy(30f));
        }
    }
}
