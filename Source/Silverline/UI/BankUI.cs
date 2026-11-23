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

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        /// <summary>A UI texture of the bank ("Ball_On", "Coin" …) from Common/Textures/UI/HH/Bank; null when missing (callers draw a plain fallback).</summary>
        public static Texture2D Tex(string name)
        {
            if (texCache.TryGetValue(name, out var t)) return t;   // a missing texture is cached as null: no retry every frame
            t = ContentFinder<Texture2D>.Get("UI/HH/Bank/" + name, false);
            texCache[name] = t; return t;
        }
        /// <summary>Sets the smallest size a resizeable window can be dragged to (the resizer field is private in the game: reflection, never fails).</summary>
        public static void SetMinSize(Window win, float w, float h)
        {
            try
            {
                var fld = typeof(Window).GetField("resizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (fld == null) return;
                var rz = fld.GetValue(win); if (rz == null) { rz = new WindowResizer(); fld.SetValue(win, rz); }
                var minF = typeof(WindowResizer).GetField("minWindowSize"); if (minF == null) return;
                minF.SetValue(rz, new Vector2(Math.Min(w, UI.screenWidth - 20f), Math.Min(h, UI.screenHeight - 55f)));
            }
            catch (Exception e) { HLog.Once("bankui.minsize." + win.GetType().Name, LogLevel.Warn, "Bank", "Could not set the minimum window size: " + e.Message); }
        }
        public static void DrawTex(Rect r, string name) { var t = Tex(name); if (t != null) GUI.DrawTexture(r, t); }

        /// <summary>A segmented / tab-like button. Plays the lobby click when it is pressed. Returns true on press.</summary>
        public static bool Chip(Rect r, string label, bool selected, string tip = null, bool enabled = true)
        {
            Widgets.DrawBoxSolid(r, selected ? Green : WalnutLight);
            if (selected) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 3f, r.width, 3f), Brass);
            GUI.color = selected ? Brass : BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            Label(r, label, enabled ? (selected ? Cream : CreamDim) : new Color(1f, 1f, 1f, 0.35f), anchor: TextAnchor.MiddleCenter, wrap: false);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            if (!enabled) return false;
            if (!selected) Widgets.DrawHighlightIfMouseover(r);
            if (Widgets.ButtonInvisible(r) && !selected) { BankSound.Play(BankSound.Click); return true; }
            return false;
        }

        /// <summary>
        /// Draws a page that may be taller than its rect: when <paramref name="contentH"/> does not fit, a scroll view is used (and the draw callback gets
        /// the view rect, origin 0,0). The callback returns nothing; the caller knows its own content height from the layout it draws.
        /// </summary>
        public static void ScrollPage(Rect r, ref Vector2 scroll, float contentH, Action<Rect> draw)
        {
            if (contentH <= r.height) { draw(r); return; }
            var view = new Rect(0, 0, r.width - 16f, contentH);
            Widgets.BeginScrollView(r, ref scroll, view);
            try { draw(view); } finally { Widgets.EndScrollView(); }
        }

        /// <summary>A horizontal bar with label left and value right, for comparing yearly returns.</summary>
        public static void BarRow(Rect r, string label, string value, double v, double max, Color col, string tip = null)
        {
            float labW = Math.Min(r.width * 0.42f, 230f), valW = 64f;
            LabelCut(new Rect(r.x, r.y, labW, r.height), label, Cream, GameFont.Tiny, TextAnchor.MiddleLeft);
            var bar = new Rect(r.x + labW + 6f, r.y + r.height * 0.5f - 5f, r.width - labW - valW - 12f, 10f);
            Widgets.DrawBoxSolid(bar, Walnut);
            float frac = max <= 0 ? 0f : Mathf.Clamp01((float)(Math.Max(0, v) / max));
            if (frac > 0f) Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, Mathf.Max(2f, bar.width * frac), bar.height), col);
            Label(new Rect(r.xMax - valW, r.y, valW, r.height), value, v >= 0 ? Cream : Bad, GameFont.Tiny, TextAnchor.MiddleRight, false);
            if (!tip.NullOrEmpty()) { Widgets.DrawHighlightIfMouseover(r); TooltipHandler.TipRegion(r, tip); }
        }

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

        /// <summary>
        /// Label that never shows half a line: wrapped text that does not fit the rect is cut at a word and ends in "...", and the whole text goes into the tooltip.
        /// Colour is taken from GUI.color as set by the caller; the font is the one given (and put back).
        /// </summary>
        public static void FitLabel(Rect r, string text, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            if (text.NullOrEmpty() || r.width < 4f || r.height < 4f) return;
            var f = Text.Font; var a = Text.Anchor; var wr = Text.WordWrap;
            Text.Font = font; Text.Anchor = anchor; Text.WordWrap = true;
            string shown = text;
            if (Text.CalcHeight(text, r.width) > r.height + 0.5f)
            {
                shown = FitText(text, r.width, r.height);
                TooltipHandler.TipRegion(r, text);
            }
            Widgets.Label(r, shown);
            Text.Font = f; Text.Anchor = a; Text.WordWrap = wr;
        }

        /// <summary>FitLabel with an explicit colour (put back to white afterwards).</summary>
        public static void FitLabel2(Rect r, string text, Color col, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        { GUI.color = col; FitLabel(r, text, font, anchor); GUI.color = Color.white; }

        /// <summary>The longest start of <paramref name="text"/> (cut at a space) that fits the box with "..." appended. Needs Text.Font and WordWrap set by the caller.</summary>
        public static string FitText(string text, float width, float height)
        {
            if (Text.CalcHeight(text, width) <= height + 0.5f) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)   // binary search over the length: the height only grows with the text
            {
                int mid = (lo + hi + 1) / 2;
                if (Text.CalcHeight(text.Substring(0, mid).TrimEnd() + "...", width) <= height + 0.5f) lo = mid; else hi = mid - 1;
            }
            int cut = lo;
            int sp = text.LastIndexOf(' ', Math.Max(0, cut - 1), Math.Min(cut, 24));   // back to a word boundary, at most ~24 chars
            if (sp > 8 && cut < text.Length) cut = sp;
            return text.Substring(0, cut).TrimEnd(' ', ',', '.', ';', ':') + "...";
        }

        /// <summary>Single-line text cut to the width with "..." (measured in the font it will be drawn in).</summary>
        public static string Trunc(string text, float width, GameFont font = GameFont.Small)
        {
            if (text.NullOrEmpty()) return text;
            var f = Text.Font; Text.Font = font;
            string r = text.Truncate(width);
            Text.Font = f; return r;
        }

        /// <summary>One-line label that is cut with "..." where it would not fit; the full text is the tooltip when it was cut.</summary>
        public static void LabelCut(Rect r, string text, Color col, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            string shown = Trunc(text, r.width, font);
            Label(r, shown, col, font, anchor, false);
            if (shown != text) { Widgets.DrawHighlightIfMouseover(r); TooltipHandler.TipRegion(r, text); }
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
