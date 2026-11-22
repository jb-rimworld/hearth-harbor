// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hearth;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>The look of the lobby (KONZEPT 8.4): walnut, brass, deep green. Vanilla fonts and controls, nothing blinks.</summary>
    public static class BankUI
    {
        public static readonly Color Walnut = new Color(0.17f, 0.11f, 0.08f);
        public static readonly Color WalnutLight = new Color(0.25f, 0.17f, 0.12f);
        public static readonly Color Brass = new Color(0.80f, 0.64f, 0.32f);
        public static readonly Color BrassDim = new Color(0.55f, 0.44f, 0.24f);
        public static readonly Color Green = new Color(0.09f, 0.25f, 0.19f);
        public static readonly Color GreenLight = new Color(0.14f, 0.36f, 0.27f);
        public static readonly Color Cream = new Color(0.94f, 0.90f, 0.79f);
        public static readonly Color CreamDim = new Color(0.72f, 0.68f, 0.58f);
        public static readonly Color Good = new Color(0.55f, 0.82f, 0.55f);
        public static readonly Color Bad = new Color(0.90f, 0.45f, 0.38f);

        public static Texture2D Ottoline => ContentFinder<Texture2D>.Get("UI/HH/Bank/Ottoline", false);

        public static string Silver(Money m) => m.ToStringShort();
        public static string Signed(Money m) => (m.Micro >= 0 ? "+" : "-") + m.Abs.ToStringShort();

        /// <summary>A framed panel with a brass heading. Returns the inner rect.</summary>
        public static Rect Card(Rect r, string title)
        {
            Widgets.DrawBoxSolid(r, WalnutLight);
            GUI.color = BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            var head = new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 22f);
            if (!title.NullOrEmpty())
            {
                GUI.color = Brass; Text.Font = GameFont.Small; Widgets.Label(head, title.ToUpperInvariant()); GUI.color = Color.white;
                Widgets.DrawLineHorizontal(r.x + 10f, r.y + 28f, r.width - 20f, BrassDim);
            }
            return new Rect(r.x + 10f, r.y + (title.NullOrEmpty() ? 8f : 34f), r.width - 20f, r.height - (title.NullOrEmpty() ? 16f : 42f));
        }

        public static void Label(Rect r, string text, Color col, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = true)
        {
            var f = Text.Font; var a = Text.Anchor; var w = Text.WordWrap;
            Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = col;
            Widgets.Label(r, text);
            GUI.color = Color.white; Text.Font = f; Text.Anchor = a; Text.WordWrap = w;
        }

        /// <summary>"Label ........ value" row with an optional tooltip formula (every number explains itself, 8.4).</summary>
        public static void Row(Rect r, string label, string value, string tip = null, Color? valueColor = null, bool big = false)
        {
            Label(new Rect(r.x, r.y, r.width * 0.55f, r.height), label, CreamDim, wrap: false);
            Label(new Rect(r.x + r.width * 0.45f, r.y, r.width * 0.55f, r.height), value, valueColor ?? Cream, big ? GameFont.Medium : GameFont.Small, TextAnchor.UpperRight, false);
            if (!tip.NullOrEmpty()) { Widgets.DrawHighlightIfMouseover(r); TooltipHandler.TipRegion(r, tip); }
        }

        public static bool Button(Rect r, string label, bool enabled = true, string disabledReason = null)
        {
            if (!enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.ButtonText(r, label, true, true, false);
                GUI.color = Color.white;
                if (!disabledReason.NullOrEmpty()) { TooltipHandler.TipRegion(r, disabledReason); }
                if (Mouse.IsOver(r) && Event.current.type == EventType.MouseDown) Event.current.Use();
                return false;
            }
            return Widgets.ButtonText(r, label);
        }

        // ---- chart ----------------------------------------------------------------------------------
        public static void DrawChart(Rect r, List<KeyValuePair<int, double>> pts, string unitLabel = null)
        {
            Widgets.DrawBoxSolid(r, Walnut);
            GUI.color = BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            if (pts == null || pts.Count == 0) return;
            var plot = new Rect(r.x + 44f, r.y + 8f, r.width - 54f, r.height - 24f);
            double min = pts.Min(p => p.Value), max = pts.Max(p => p.Value);
            if (max - min < 1e-6) { max += 1; min = Math.Max(0, min - 1); }
            double pad = (max - min) * 0.08; max += pad; min -= pad; if (min < 0 && pts.Min(p => p.Value) >= 0) min = 0;
            int d0 = pts.First().Key, d1 = Math.Max(d0 + 1, pts.Last().Key);
            for (int i = 0; i <= 3; i++)
            {
                float y = plot.yMax - plot.height * i / 3f;
                Widgets.DrawLineHorizontal(plot.x, y, plot.width, new Color(1f, 1f, 1f, 0.07f));
                double v = min + (max - min) * i / 3.0;
                Label(new Rect(r.x + 2f, y - 8f, 40f, 16f), Compact(v), CreamDim, GameFont.Tiny, TextAnchor.MiddleRight, false);
            }
            Vector2 P(KeyValuePair<int, double> p) => new Vector2(plot.x + plot.width * (p.Key - d0) / (float)(d1 - d0), plot.yMax - plot.height * (float)((p.Value - min) / (max - min)));
            for (int i = 1; i < pts.Count; i++) Widgets.DrawLine(P(pts[i - 1]), P(pts[i]), Brass, 2f);
            var last = P(pts[pts.Count - 1]);
            Widgets.DrawBoxSolid(new Rect(last.x - 3f, last.y - 3f, 6f, 6f), Cream);
            Label(new Rect(plot.x, plot.yMax + 2f, 80f, 14f), L.T("Chart.DayN", "day {0}", d0), CreamDim, GameFont.Tiny, wrap: false);
            Label(new Rect(plot.xMax - 80f, plot.yMax + 2f, 80f, 14f), L.T("Chart.Today", "today"), CreamDim, GameFont.Tiny, TextAnchor.UpperRight, false);
            if (Mouse.IsOver(plot))
            {
                float mx = Event.current.mousePosition.x;
                var near = pts.OrderBy(p => Math.Abs(P(p).x - mx)).First();
                var pp = P(near);
                Widgets.DrawBoxSolid(new Rect(pp.x, plot.y, 1f, plot.height), new Color(1f, 1f, 1f, 0.25f));
                TooltipHandler.TipRegion(plot, L.T("Chart.Tip", "Day {0}: {1} silver", near.Key, near.Value.ToString("#,##0.00", CultureInfo.InvariantCulture)));
            }
        }

        public static string Compact(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1_000_000) return (v / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (a >= 10_000) return (v / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return v.ToString("0", CultureInfo.InvariantCulture);
        }

        public static string TickToDay(int tick) => L.T("Time.DayHour", "day {0}, {1}:00", tick / 60000, ((tick % 60000) / 2500).ToString("00"));

        public static string HoursText(int ticks)
        {
            float h = ticks / 2500f;
            return h < 1f ? L.T("Time.Minutes", "{0} min", Math.Max(1, (int)(h * 60))) : L.T("Time.Hours", "{0} h", h.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Easing display value ("gently rolling numbers").</summary>
        public static double Ease(ref double shown, double target)
        {
            if (Math.Abs(target - shown) < 0.005) shown = target; else shown += (target - shown) * 0.18;
            return shown;
        }
    }
}
