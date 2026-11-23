// SPDX-License-Identifier: MIT
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>
    /// Shared frame of the pages of the main tab (F35): a header strip with the page title (Medium) and a one-line subtitle, an amber rule,
    /// and a scroll view whose height is measured from what the page drew. Heights come from Text.CalcHeight, so long translations never clip.
    /// </summary>
    public static class PageFrame
    {
        public const float ScrollBar = 18f;

        /// <summary>Draws title + optional subtitle at the top of <paramref name="r"/> and returns the rest of the rect below the rule.</summary>
        public static Rect Header(Rect r, string title, string subtitle = null)
        {
            float y = r.y;
            float th = Mathf.Max(32f, HUI.TextH(title, r.width, GameFont.Medium));
            Text.Font = GameFont.Medium; GUI.color = Color.white;
            Widgets.Label(new Rect(r.x, y, r.width, th), title);
            Text.Font = GameFont.Small;
            y += th;
            if (!subtitle.NullOrEmpty())
            {
                float sh = HUI.TextH(subtitle, r.width, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(r.x, y, r.width, sh), subtitle, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                y += sh + 2f;
            }
            Widgets.DrawBoxSolid(new Rect(r.x, y + 2f, Mathf.Min(r.width, 180f), 2f), UiStyle.Amber);
            Widgets.DrawBoxSolid(new Rect(r.x + 180f, y + 3f, Mathf.Max(0f, r.width - 180f), 1f), UiStyle.Line);
            y += 10f;
            return new Rect(r.x, y, r.width, Mathf.Max(0f, r.yMax - y));
        }

        /// <summary>Opens a scroll view over <paramref name="outer"/>; returns the usable inner width. Pair with <see cref="EndScroll"/>.</summary>
        public static float BeginScroll(Rect outer, ref Vector2 scroll, float viewH)
        {
            bool bar = viewH > outer.height;
            var view = new Rect(0f, 0f, outer.width - (bar ? ScrollBar : 0f), Mathf.Max(viewH, 1f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            return view.width;
        }

        /// <summary>Closes the scroll view and returns the measured height for the next frame (content + a little air).</summary>
        public static float EndScroll(float usedY)
        {
            Widgets.EndScrollView();
            return usedY + 12f;
        }
    }
}
