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
    /// <summary>Drawing helpers shared by the Invest and Lottery tabs: sparklines, charts, money fields, the help hint (K28).</summary>
    public static class FinUi
    {
        public static readonly Color Up = new Color(0.45f, 0.80f, 0.47f), Down = new Color(0.92f, 0.42f, 0.38f), Dim = new Color(1f, 1f, 1f, 0.55f), Panel = new Color(0f, 0f, 0f, 0.22f), Line = new Color(1f, 1f, 1f, 0.12f);
        public static string Price(double p) => p.ToString(p >= 1000 ? "#,##0.00000" : "0.00000", CultureInfo.InvariantCulture);

        public static Color ChangeColor(double v) => v > 0.00005 ? Up : v < -0.00005 ? Down : Dim;

        public static void Sparkline(Rect r, IList<float> data, int points)
        {
            if (data == null || data.Count < 2) return;
            int n = Math.Min(points, data.Count), start = data.Count - n;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = start; i < data.Count; i++) { min = Math.Min(min, data[i]); max = Math.Max(max, data[i]); }
            if (max - min < 1e-9f) { min -= 0.5f; max += 0.5f; }
            Color c = data[data.Count - 1] >= data[start] ? Up : Down;
            Vector2 prev = default;
            for (int i = 0; i < n; i++)
            {
                float x = r.x + r.width * i / Math.Max(1, n - 1), y = r.yMax - r.height * (data[start + i] - min) / (max - min);
                var p = new Vector2(x, y);
                if (i > 0) Widgets.DrawLine(prev, p, c, 1.2f);
                prev = p;
            }
        }

        /// <summary>Large chart with three grid lines and min/max labels.</summary>
        public static void Chart(Rect r, IList<float> data, int points, string unitLabel = null)
        {
            Widgets.DrawBoxSolid(r, Panel);
            if (data == null || data.Count < 2) return;
            int n = Math.Min(points, data.Count), start = data.Count - n;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = start; i < data.Count; i++) { min = Math.Min(min, data[i]); max = Math.Max(max, data[i]); }
            float pad = Math.Max((max - min) * 0.08f, 1e-6f); min -= pad; max += pad;
            var inner = r.ContractedBy(4f); inner.xMin += 54f;
            for (int g = 0; g <= 3; g++)
            {
                float y = inner.yMax - inner.height * g / 3f;
                Widgets.DrawLine(new Vector2(inner.x, y), new Vector2(inner.xMax, y), Line, 1f);
                Text.Font = GameFont.Tiny; GUI.color = Dim;
                Widgets.Label(new Rect(r.x + 2, y - 8, 52, 16), Price(min + (max - min) * g / 3f).TrimEnd('0').TrimEnd('.'));
                GUI.color = Color.white;
            }
            Color c = data[data.Count - 1] >= data[start] ? Up : Down; Vector2 prev = default;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(inner.x + inner.width * i / Math.Max(1, n - 1), inner.yMax - inner.height * (data[start + i] - min) / (max - min));
                if (i > 0) Widgets.DrawLine(prev, p, c, 1.6f);
                prev = p;
            }
            if (unitLabel != null) { Text.Font = GameFont.Tiny; GUI.color = Dim; Text.Anchor = TextAnchor.LowerRight; Widgets.Label(r.ContractedBy(4), unitLabel); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }
            Text.Font = GameFont.Small;
        }

        public static bool TryParseSilver(string s, out Money m)
        {
            m = Money.Zero; if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace(',', '.').Replace(" ", "");
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || v > 1e9) return false;
            m = Money.FromSilver(v); return true;
        }

        public static void Label(Rect r, string text, GameFont font = GameFont.Small, Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = true)
        {
            Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap;
            GUI.color = color ?? Color.white; Widgets.Label(r, text); GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; Text.WordWrap = true;
        }

        public static float TextHeight(float width, string text, GameFont font = GameFont.Small)
        {
            Text.Font = font; float h = Text.CalcHeight(text, width); Text.Font = GameFont.Small; return h;
        }

        /// <summary>The quiet real-life help line under every screen where you gamble (K28). Never a pop-up, never satire.</summary>
        public static void HelpHint(Rect r)
        {
            if (!HSettings.GetBool("core.helpHint")) return;
            string text = FT.T("HH.Finance.Help.Line");
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(new Rect(r.x, r.y, r.width - 220f, r.height), text);
            Text.Anchor = TextAnchor.MiddleRight;
            var l1 = new Rect(r.xMax - 215f, r.y, 105f, r.height); var l2 = new Rect(r.xMax - 108f, r.y, 108f, r.height);
            GUI.color = new Color(0.6f, 0.75f, 1f, 0.8f);
            Widgets.Label(l1, "findahelpline.com"); Widgets.Label(l2, "gamblingtherapy.org");
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(l1)) Application.OpenURL(FT.Raw("HH.Finance.Help.Url1"));
            if (Widgets.ButtonInvisible(l2)) Application.OpenURL(FT.Raw("HH.Finance.Help.Url2"));
            TooltipHandler.TipRegion(l1, FT.T("HH.Finance.Help.Tip")); TooltipHandler.TipRegion(l2, FT.T("HH.Finance.Help.Tip"));
        }

        public static bool Button(Rect r, string label, bool enabled = true, string tip = null)
        {
            if (!enabled) { GUI.color = new Color(1, 1, 1, 0.4f); Widgets.ButtonText(r, label, true, false, false); GUI.color = Color.white; if (tip != null) TooltipHandler.TipRegion(r, tip); return false; }
            if (tip != null) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonText(r, label);
        }

        public static string DaysLeft(int endTick, int now)
        {
            double d = Math.Max(0, (endTick - now) / 60000.0);
            return d >= 1 ? d.ToString("0.0", CultureInfo.InvariantCulture) + " d" : Math.Max(0, (endTick - now) / 2500.0).ToString("0", CultureInfo.InvariantCulture) + " h";
        }

        public static bool Nerd => Prefs.DevMode || HearthMod.Template.ShowNerd;
    }
}
