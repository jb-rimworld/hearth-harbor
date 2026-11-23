// SPDX-License-Identifier: MIT
using System;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.Anim
{
    /// <summary>
    /// One spin of the slot machine: the lever is pulled, the three reels run and stop one after another (the third slower after a
    /// near miss), the payline lights and coins jump out according to the multiplier. The symbols are known before it starts.
    /// </summary>
    public sealed class SlotSpinAnim : TableAnim
    {
        public readonly int[] From, To;
        public readonly int[][] Strips = new int[3][];
        public readonly float[] StopAt = new float[3];
        public readonly double Mult;
        public readonly bool NearMiss;
        public const float ReelStart = 0.32f;
        public float WinAt => StopAt[2];
        public readonly int Tier;        // 0 no win, 1 small, 2 big (≥ 20×), 3 jackpot (three thrumbos or ≥ 200×)

        public SlotSpinAnim(int[] from, int[] to, double mult, ulong seed)
        {
            From = (int[])from.Clone(); To = (int[])to.Clone(); Mult = mult;
            NearMiss = To[0] == To[1] && To[0] >= 4 && To[2] != To[0];
            Tier = mult <= 0 ? 0 : (To[0] == 6 && To[1] == 6 && To[2] == 6) || mult >= 200 ? 3 : mult >= 20 ? 2 : 1;
            StopAt[0] = 1.05f; StopAt[1] = 1.42f; StopAt[2] = NearMiss ? 2.55f : 1.8f;
            for (int i = 0; i < 3; i++)
            {
                int len = 12 + i * 5 + (NearMiss && i == 2 ? 6 : 0);
                Strips[i] = ReelStrip.Build(From[i], To[i], len, seed + (ulong)i * 977UL);
            }
            float tail = Tier == 0 ? 0.35f : Tier == 1 ? 1.1f : Tier == 2 ? 2.4f : 3.4f;
            Duration = WinAt + tail;
            At(0.02f, CasinoSfx.Lever);
            At(0.28f, CasinoSfx.Spin);
            for (int i = 0; i < 3; i++) At(StopAt[i], CasinoSfx.ReelStop, 0.92f + 0.09f * i);
            if (NearMiss) At(StopAt[1] + 0.25f, CasinoSfx.NearMiss);
            if (Tier == 1) At(WinAt + 0.05f, CasinoSfx.WinSmall, 1f, true);
            else if (Tier == 2) At(WinAt + 0.05f, CasinoSfx.WinBig, 1f, true);
            else if (Tier == 3) At(WinAt + 0.05f, CasinoSfx.Jackpot, 1f, true);
        }

        /// <summary>Scroll position of reel i in strip cells (0 … length−1).</summary>
        public double ReelPos(int i)
        {
            float t = Elapsed - ReelStart;
            if (t <= 0) return 0;
            return ReelStrip.Position(t, StopAt[i] - ReelStart, Strips[i].Length, i == 2 && NearMiss ? 0.6 : 1.4);
        }

        public bool Stopped(int i) => Elapsed >= StopAt[i];
        /// <summary>Lever travel 0 (up) … 1 (pulled down and back).</summary>
        public float Lever => Elapsed < 0.32f ? Mathf.Sin(Mathf.Clamp01(Elapsed / 0.32f) * Mathf.PI) : 0f;
    }

    /// <summary>The slot cabinet of the Lucky Thrumbo window, the stake dialog and the practice game. Keeps the symbols on show between spins.</summary>
    public sealed class SlotMachineView
    {
        // geometry of UI/HH/Casino/Anim/SlotCabinet (1024 × 512, see tools/art/hhart/casino_anim.py: CAB_*)
        public const float Aspect = 2f;
        static readonly Rect[] windows = { new Rect(110f / 1024f, 165f / 512f, 200f / 1024f, 240f / 512f), new Rect(340f / 1024f, 165f / 512f, 200f / 1024f, 240f / 512f), new Rect(570f / 1024f, 165f / 512f, 200f / 1024f, 240f / 512f) };
        static readonly Rect marquee = new Rect(70f / 1024f, 34f / 512f, 760f / 1024f, 92f / 512f);
        static readonly Vector2 leverPivot = new Vector2(918f / 1024f, 300f / 512f);
        const float leverTop = 74f / 512f;
        static readonly Rect tray = new Rect(250f / 1024f, 432f / 512f, 400f / 1024f, 46f / 512f);

        public int[] Shown = { 6, 5, 4 };
        public SlotSpinAnim Anim;
        public readonly Particles Fx = new Particles();
        readonly System.Random ui = new System.Random(4711);
        public bool Busy => Anim != null && !Anim.Done;
        /// <summary>Where coins may fly (the whole table); default: the area the cabinet is drawn in.</summary>
        public Rect? Clip;
        float winGlowUntil = -1f, lastBurst = -1f; int lastTier;
        bool burstDone;
        public static int Spins;          // tests

        /// <summary>Starts a spin to a known result (or just shows it when animations are off).</summary>
        public void Start(int[] result, double mult, ulong seed)
        {
            if (result == null || result.Length < 3) return;
            Spins++;
            Fx.Clear(); burstDone = false;
            if (!AnimPrefs.On) { Shown = (int[])result.Clone(); Anim = null; lastTier = mult <= 0 ? 0 : mult >= 20 ? 2 : 1; Burst(mult, null, lastTier); return; }
            Anim = new SlotSpinAnim(Shown, result, mult, seed);
            Shown = (int[])result.Clone();
        }

        public void Skip() { Anim?.Skip(); }

        void Burst(double mult, Rect? cab, int tier)
        {
            burstDone = true; lastTier = tier;
            winGlowUntil = AnimClock.Now + (tier >= 2 ? 3.2f : 1.4f);
            if (tier <= 0 || cab == null) return;
            var c = cab.Value; var from = new Vector2(c.x + c.width * (tray.x + tray.width / 2), c.y + c.height * tray.y);
            int n = tier == 1 ? 6 + (int)Math.Min(8, mult * 1.5) : tier == 2 ? 40 : 70;
            Fx.Fountain(from, n, tier >= 2 ? 620f : 430f, ui);
            if (tier >= 2) Fx.Sparkles(c, 26, ui);
            if (tier >= 3) Fx.Fountain(from, 40, 700f, ui, Particles.Kind.Confetti);
            lastBurst = AnimClock.Now;
        }

        /// <summary>Draws the cabinet into <paramref name="area"/> (aspect-fitted). Returns the cabinet rect.</summary>
        public Rect Draw(Rect area, string marqueeText)
        {
            Anim?.Update();
            var cab = AnimDraw.Fit(area, Aspect);
            float now = AnimClock.Now;
            bool spinning = Busy && Anim.Elapsed < Anim.WinAt;
            if (Anim != null && !burstDone && Anim.Elapsed >= Anim.WinAt) Burst(Anim.Mult, cab, Anim.Tier);
            bool glow = now < winGlowUntil && lastTier > 0;

            // glow behind the cabinet on a win
            if (glow) AnimDraw.Glow(cab.center, Mathf.Min(cab.width * 0.6f, area.height * 0.75f), new Color(1f, 0.85f, 0.35f, 0.18f + 0.1f * Mathf.Sin(now * 9f)));
            GUI.DrawTexture(cab, CasinoAnimTex.SlotCabinet);

            // marquee bulbs: a slow chase, fast and golden on a win
            var mq = Sub(cab, marquee);
            DrawBulbs(mq, now, spinning ? 7f : glow ? 14f : 2.2f, glow);
            if (!marqueeText.NullOrEmpty()) AnimDraw.ShadowLabel(mq.ContractedBy(mq.height * 0.18f), marqueeText, new Color(1f, 0.93f, 0.78f), cab.width > 520f ? GameFont.Medium : GameFont.Small);

            // reels
            for (int i = 0; i < 3; i++) DrawReel(Sub(cab, windows[i]), i, now, glow);

            // payline across the windows
            var w0 = Sub(cab, windows[0]); var w2 = Sub(cab, windows[2]);
            float py = w0.center.y;
            var lineCol = glow ? new Color(1f, 0.85f, 0.3f, 0.85f) : new Color(0.85f, 0.25f, 0.2f, 0.55f);
            Widgets.DrawBoxSolid(new Rect(w0.x - 10f, py - 1.5f, w2.xMax - w0.x + 20f, 3f), lineCol);

            // lever
            DrawLever(cab);

            Fx.UpdateAndDraw(Vector2.zero, Clip ?? area.ExpandedBy(6f));
            if (Anim != null && Anim.Done && Fx.Alive == 0) { /* keep Anim for the "final" state queries */ }
            return cab;
        }

        static Rect Sub(Rect r, Rect f) => new Rect(r.x + r.width * f.x, r.y + r.height * f.y, r.width * f.width, r.height * f.height);

        void DrawBulbs(Rect mq, float now, float speed, bool gold)
        {
            int n = 16; float r = Mathf.Max(3f, mq.height * 0.09f);
            var old = GUI.color;
            for (int i = 0; i < n; i++)
            {
                float x = mq.x + mq.width * (i + 0.5f) / n;
                for (int row = 0; row < 2; row++)
                {
                    float y = row == 0 ? mq.y + r * 0.6f : mq.yMax - r * 0.6f;
                    bool lit = ((int)(now * speed) + i + row) % 3 == 0;
                    GUI.color = lit ? (gold ? new Color(1f, 0.86f, 0.35f) : new Color(1f, 0.95f, 0.8f)) : new Color(0.45f, 0.32f, 0.2f, 0.9f);
                    GUI.DrawTexture(new Rect(x - r, y - r, r * 2, r * 2), CasinoAnimTex.Ball);
                    if (lit) AnimDraw.Glow(new Vector2(x, y), r * 3f, new Color(1f, 0.8f, 0.4f, 0.25f));
                }
            }
            GUI.color = old;
        }

        void DrawReel(Rect win, int i, float now, bool glow)
        {
            Widgets.DrawBoxSolid(win, new Color(0.95f, 0.92f, 0.84f));
            float cell = win.height / 2.2f;                  // a little more than two symbols high: one on the payline, halves above and below
            double pos; int[] strip;
            if (Anim == null) { pos = 0; strip = null; }
            else if (Anim.Stopped(i)) { strip = Anim.Strips[i]; pos = strip.Length - 1; }          // stays on the strip until the next spin (no jump of the neighbours)
            else { strip = Anim.Strips[i]; pos = Anim.ReelPos(i); }                                  // lever phase (pos 0: the old symbols) and the run
            bool blur = Anim != null && !Anim.Stopped(i) && Anim.Elapsed > SlotSpinAnim.ReelStart + 0.08f && Anim.Elapsed < Anim.StopAt[i] - 0.18f;
            GUI.BeginGroup(win);
            var old = GUI.color;
            int k0 = (int)Math.Floor(pos);
            for (int k = k0 - 2; k <= k0 + 2; k++)
            {
                int sym = Symbol(strip, i, k);
                float y = win.height / 2 + (float)(pos - k) * cell;          // the strip moves down: later symbols come from above
                float size = Mathf.Min(cell * 0.86f, win.width * 0.8f);
                var r = new Rect(win.width / 2 - size / 2, y - size / 2, size, size);
                if (r.yMax < 0 || r.y > win.height) continue;
                var tex = SlotSymbolIcons.For(sym);
                if (tex == null) continue;
                if (blur)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.38f);
                    GUI.DrawTexture(new Rect(r.x, r.y - size * 0.22f, size, size * 1.44f), tex);
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    GUI.DrawTexture(r, tex);
                }
                else
                {
                    bool onLine = Math.Abs(k - pos) < 0.5;
                    GUI.color = onLine ? Color.white : new Color(1f, 1f, 1f, 0.62f);
                    if (onLine && glow && Anim != null && Anim.Done && Anim.Tier > 0) AnimDraw.Glow(r.center, size * 0.75f, new Color(1f, 0.82f, 0.3f, 0.35f + 0.15f * Mathf.Sin(now * 8f)));
                    GUI.DrawTexture(r, tex);
                }
            }
            GUI.color = old;
            GUI.EndGroup();
            // glass with the inner shadow at the top and bottom of the window
            GUI.DrawTexture(win, CasinoAnimTex.SlotGlass);
        }

        int Symbol(int[] strip, int reel, int k)
        {
            if (strip != null && k >= 0 && k < strip.Length) return strip[k];
            int baseSym = strip != null ? (k < 0 ? strip[0] : strip[strip.Length - 1]) : Shown[reel];
            int off = strip != null ? (k < 0 ? k : k - (strip.Length - 1)) : k;
            if (off == 0) return baseSym;
            int s = ((baseSym + off * 3 + reel * 2) % SlotPaytable.Symbols + SlotPaytable.Symbols) % SlotPaytable.Symbols;
            if (s == baseSym) s = (s + 1) % SlotPaytable.Symbols;
            return s;
        }

        void DrawLever(Rect cab)
        {
            float pull = Anim != null ? Anim.Lever : 0f;
            var pivot = new Vector2(cab.x + cab.width * leverPivot.x, cab.y + cab.height * leverPivot.y);
            float top = cab.y + cab.height * leverTop;
            float len = pivot.y - top;
            // pulled: the arm swings down past the pivot (drawn as a shortening arm and a knob that drops)
            float knobY = Mathf.Lerp(top, pivot.y + len * 0.55f, pull);
            float armW = Mathf.Max(5f, cab.width * 0.016f);
            var arm = new Rect(pivot.x - armW / 2, Mathf.Min(knobY, pivot.y), armW, Mathf.Abs(pivot.y - knobY));
            GUI.DrawTexture(arm, CasinoAnimTex.LeverArm);
            float kr = cab.width * 0.032f * (1f + 0.12f * pull);
            GUI.DrawTexture(new Rect(pivot.x - kr, knobY - kr, kr * 2, kr * 2 * (1f - 0.12f * pull)), CasinoAnimTex.LeverKnob);
        }

        /// <summary>Clickable area of the lever (the window lets the player pull it to spin).</summary>
        public static Rect LeverRect(Rect cab)
        {
            var pivot = new Vector2(cab.x + cab.width * leverPivot.x, cab.y + cab.height * leverPivot.y);
            float top = cab.y + cab.height * leverTop; float kr = cab.width * 0.05f;
            return new Rect(pivot.x - kr, top - kr, kr * 2, pivot.y - top + kr * 1.5f);
        }
    }
}
