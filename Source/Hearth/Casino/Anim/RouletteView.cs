// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.Anim
{
    /// <summary>One spin of the wheel to a pocket that is already drawn (see <see cref="RouletteSpinPlan"/>), then a short rest on the result.</summary>
    public sealed class RouletteSpinAnim : TableAnim
    {
        public readonly RouletteSpinPlan Plan;
        public const float SpinSeconds = 4.6f;
        public readonly bool Win;

        public RouletteSpinAnim(RouletteSpinPlan plan, bool win, bool big)
        {
            Plan = plan; Win = win;
            Duration = SpinSeconds + (win ? (big ? 1.6f : 0.9f) : 0.5f);
            At(0.0f, CasinoSfx.ChipsPlace);
            At(0.15f, CasinoSfx.WheelSpin);
            At(SpinSeconds * (float)plan.DropT, CasinoSfx.BallDrop);
            if (win) At(SpinSeconds + 0.05f, big ? CasinoSfx.WinBig : CasinoSfx.Win, 1f, true);
            else At(SpinSeconds + 0.05f, CasinoSfx.Lose, 1f, true, null, 0.7f);
        }

        /// <summary>Progress of the spin itself (0 … 1); the rest after it shows the result.</summary>
        public double SpinT => Mathf.Clamp01(Elapsed / SpinSeconds);
        public bool Landed => Elapsed >= SpinSeconds * (float)Plan.LandT;
    }

    /// <summary>A top-down roulette wheel (single or double zero) with the ball; keeps the last angle and pocket between spins.</summary>
    public sealed class RouletteView
    {
        // geometry of UI/HH/Casino/Anim/Wheel* (radius 1 = half the texture, see tools/art/hhart/casino_anim.py: WHEEL_*)
        const float BallTrack = 0.885f, PocketRing = 0.635f, NumberRing = 0.745f;
        public int Zeros = 1;
        public double WheelAngle;            // degrees, clockwise
        public int RestPocket = -1;
        public RouletteSpinAnim Anim;
        public readonly List<int> Recent = new List<int>();
        public bool Busy => Anim != null && !Anim.Done;
        public readonly Particles Fx = new Particles();
        readonly System.Random ui = new System.Random(1213);
        float postSpin;                       // a slow drift of the wheel after a spin, with the ball in its pocket
        float lastNow = -1f;
        bool burst;
        public static int Spins;

        public void Start(int pocket, int zeros, bool win, bool big, ulong seed)
        {
            Spins++; Zeros = zeros; Fx.Clear(); burst = false; postSpin = 0f;
            if (!AnimPrefs.On) { RestPocket = pocket; Anim = null; Push(pocket); return; }
            var plan = RouletteSpinPlan.Make(pocket, zeros, WheelAngle, seed);
            Anim = new RouletteSpinAnim(plan, win, big);
            Anim.At(RouletteSpinAnim.SpinSeconds, null, 1f, true, () => Push(pocket));
            RestPocket = -1;
        }

        void Push(int pocket) { Recent.Add(pocket); if (Recent.Count > 16) Recent.RemoveAt(0); RestPocket = pocket; }

        public void Skip() { Anim?.Skip(); }

        /// <summary>The pocket the ball shows right now (−1 before the first spin or while it still rolls).</summary>
        public int ShownPocket => Anim != null && !Anim.Landed ? -1 : (Anim != null ? Anim.Plan.Pocket : RestPocket);

        public void Draw(Rect area, bool numbers = true)
        {
            Anim?.Update();
            float now = AnimClock.Now; if (lastNow < 0) lastNow = now; float dt = Mathf.Clamp(now - lastNow, 0f, 0.1f); lastNow = now;
            float d = Mathf.Min(area.width, area.height);
            var wr = new Rect(area.center.x - d / 2, area.center.y - d / 2, d, d);
            var c = wr.center; float R = d / 2;

            double wheel, ball = 0, ballR = 0; bool showBall = false;
            if (Anim != null)
            {
                double t = Anim.SpinT;
                if (Anim.Done) postSpin += dt * 6f;     // the wheel keeps turning slowly with the ball in its pocket
                wheel = Anim.Plan.WheelAngle(t) + postSpin;
                ball = Anim.Plan.BallAngle(t) + postSpin;
                ballR = Anim.Plan.BallRadius(t); showBall = true;
                WheelAngle = WheelLayout.Norm(wheel);
            }
            else
            {
                WheelAngle = WheelLayout.Norm(WheelAngle + dt * 6f);
                wheel = WheelAngle;
                if (RestPocket >= 0) { showBall = true; ball = wheel + WheelLayout.PocketAngle(RestPocket, Zeros); ballR = 0; }
            }

            // shadow, bowl and the turning parts
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(wr.x + d * 0.03f, wr.y + d * 0.04f, d, d), CasinoAnimTex.Ball); GUI.color = old;
            GUI.DrawTexture(wr, CasinoAnimTex.WheelRim);
            var inner = wr.ContractedBy(d * (1f - 0.82f) / 2f);
            AnimDraw.Rotated(inner, Zeros >= 2 ? CasinoAnimTex.WheelUS : CasinoAnimTex.WheelEU, (float)WheelLayout.Norm(wheel));
            var turret = wr.ContractedBy(d * (1f - 0.34f) / 2f);
            AnimDraw.Rotated(turret, CasinoAnimTex.WheelTurret, (float)WheelLayout.Norm(wheel));

            // numbers stay upright while the wheel turns (readable at any angle)
            if (numbers && d >= 180f)
            {
                var order = WheelLayout.Order(Zeros); var of = Text.Font; var oa = Text.Anchor;
                Text.Font = d >= 420f ? GameFont.Small : GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                int shown = ShownPocket;
                for (int i = 0; i < order.Length; i++)
                {
                    double a = (wheel + i * 360.0 / order.Length) * Mathf.Deg2Rad;
                    var p = new Vector2(c.x + (float)Math.Sin(a) * R * NumberRing, c.y - (float)Math.Cos(a) * R * NumberRing);
                    var lr = d >= 420f ? new Rect(p.x - 16f, p.y - 12f, 32f, 24f) : new Rect(p.x - 12f, p.y - 9f, 24f, 18f);
                    bool hit = order[i] == shown && shown >= 0;
                    GUI.color = hit ? new Color(1f, 0.92f, 0.45f) : new Color(0.96f, 0.93f, 0.86f, 0.92f);
                    Widgets.Label(lr, RouletteBet.PocketName(order[i]));
                }
                Text.Font = of; Text.Anchor = oa; GUI.color = old;
            }

            // the ball
            if (showBall)
            {
                double r = PocketRing + (BallTrack - PocketRing) * ballR;
                double a = ball * Mathf.Deg2Rad;
                var p = new Vector2(c.x + (float)Math.Sin(a) * R * (float)r, c.y - (float)Math.Cos(a) * R * (float)r);
                float br = Mathf.Max(4f, d * 0.028f);
                if (Anim != null && Anim.Landed) AnimDraw.Glow(p, br * 3.2f, new Color(1f, 0.95f, 0.7f, Anim.Win ? 0.55f : 0.3f));
                GUI.color = new Color(0f, 0f, 0f, 0.4f); GUI.DrawTexture(new Rect(p.x - br + 1.5f, p.y - br + 2f, br * 2, br * 2), CasinoAnimTex.Ball);
                GUI.color = old; GUI.DrawTexture(new Rect(p.x - br, p.y - br, br * 2, br * 2), CasinoAnimTex.Ball);
                if (Anim != null && Anim.Landed && Anim.Win && !burst) { burst = true; Fx.Sparkles(new Rect(p.x - 30, p.y - 30, 60, 60), 14, ui); }
            }
            Fx.UpdateAndDraw(Vector2.zero, area.ExpandedBy(60f));
        }

        /// <summary>The strip of the last numbers (newest left).</summary>
        public void DrawRecent(Rect r, int max)
        {
            var of = Text.Font; var oa = Text.Anchor; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            float w = Mathf.Min(28f, r.width / Math.Max(1, max) - 2f); float x = r.x; int n = 0;
            for (int i = Recent.Count - 1; i >= 0 && n < max; i--, n++)
            {
                var cell = new Rect(x, r.y, w, r.height); x += w + 2f;
                int p = Recent[i];
                Widgets.DrawBoxSolid(cell, PocketColor(p));
                if (n == 0) Widgets.DrawBox(cell, 1);
                Widgets.Label(cell, RouletteBet.PocketName(p));
            }
            Text.Font = of; Text.Anchor = oa;
        }

        public static Color PocketColor(int n) => n == 0 || n == 37 ? new Color(0.20f, 0.50f, 0.30f) : RouletteBet.IsRed(n) ? new Color(0.68f, 0.2f, 0.2f) : new Color(0.16f, 0.16f, 0.18f);
    }
}
