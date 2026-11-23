using System;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Shared window sizing for every Hearth &amp; Harbor and Silverline window (F35, 1.1.0).
    /// Windows declare the size they were designed for at a 900 px tall screen; HUI.Size scales it up for larger
    /// screens and by the player's "Size of Hearth &amp; Harbor windows" setting, and never lets it leave the screen.
    /// Usage: <c>public override Vector2 InitialSize =&gt; HUI.Size(900f, 640f);</c>
    /// </summary>
    public static class HUI
    {
        public const string WindowScaleKey = "core.windowScale";

        /// <summary>Automatic factor for the current screen (1.0 at 900 px height, up to 1.35) times the player setting.</summary>
        public static float Scale
        {
            get
            {
                float auto = Mathf.Clamp(UI.screenHeight / 900f, 1f, 1.35f);
                float user = 1.15f;
                try { user = HSettings.Get(WindowScaleKey); } catch (Exception) { /* settings not registered yet (early UI): use default */ }
                if (user <= 0f || float.IsNaN(user)) user = 1.15f;
                return auto * user;
            }
        }

        /// <summary>Scaled window size, clamped to a fraction of the screen and never below the given minimum.</summary>
        public static Vector2 Size(float w, float h, float minW = 0f, float minH = 0f, float maxFracW = 0.92f, float maxFracH = 0.92f)
        {
            float s = Scale;
            float maxW = UI.screenWidth * maxFracW, maxH = UI.screenHeight * maxFracH - 35f;
            float rw = Mathf.Min(w * s, maxW), rh = Mathf.Min(h * s, maxH);
            if (minW > 0f) rw = Mathf.Max(rw, Mathf.Min(minW, UI.screenWidth - 20f));
            if (minH > 0f) rh = Mathf.Max(rh, Mathf.Min(minH, UI.screenHeight - 55f));
            return new Vector2(rw, rh);
        }

        /// <summary>Splits a rect horizontally by fractions (e.g. 0.3f, 0.7f); a small gap sits between columns.</summary>
        public static Rect[] Cols(Rect r, float gap, params float[] fractions)
        {
            var res = new Rect[fractions.Length];
            float total = 0f; foreach (var f in fractions) total += f;
            float usable = r.width - gap * (fractions.Length - 1), x = r.x;
            for (int i = 0; i < fractions.Length; i++)
            {
                float w = usable * fractions[i] / total;
                res[i] = new Rect(x, r.y, w, r.height);
                x += w + gap;
            }
            return res;
        }
    }
}
