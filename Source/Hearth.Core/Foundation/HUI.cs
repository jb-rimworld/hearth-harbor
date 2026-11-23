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

        /// <summary>Tests only: pretend the screen has this size (HUI.Size and the clamps below read it). Null = the real screen.</summary>
        public static Vector2? TestScreen;
        public static float ScreenW => TestScreen.HasValue ? TestScreen.Value.x : UI.screenWidth;
        public static float ScreenH => TestScreen.HasValue ? TestScreen.Value.y : UI.screenHeight;

        /// <summary>Automatic factor for the current screen (1.0 at 900 px height, up to 1.35) times the player setting.</summary>
        public static float Scale
        {
            get
            {
                float auto = Mathf.Clamp(ScreenH / 900f, 1f, 1.35f);
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
            float maxW = ScreenW * maxFracW, maxH = ScreenH * maxFracH - 35f;
            float rw = Mathf.Min(w * s, maxW), rh = Mathf.Min(h * s, maxH);
            if (minW > 0f) rw = Mathf.Max(rw, Mathf.Min(minW, ScreenW - 20f));
            if (minH > 0f) rh = Mathf.Max(rh, Mathf.Min(minH, ScreenH - 55f));
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

        /// <summary>Height of a wrapped text at the given font and width (never smaller than one line).</summary>
        public static float TextH(string text, float width, GameFont font = GameFont.Small)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var f = Text.Font; Text.Font = font;
            float h = Text.CalcHeight(text, Mathf.Max(20f, width));
            Text.Font = f;
            return h;
        }

        /// <summary>Width of one line of text at the given font.</summary>
        public static float TextW(string text, GameFont font = GameFont.Small)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var f = Text.Font; Text.Font = font;
            float w = Text.CalcSize(text).x;
            Text.Font = f;
            return w;
        }

        /// <summary>True when a window of the given size, centered on the (test) screen, lies completely inside it.</summary>
        public static bool FitsScreen(Vector2 size) => size.x <= ScreenW + 0.5f && size.y <= ScreenH + 0.5f;
    }
}

namespace Hearth
{
    /// <summary>
    /// F35: the mod settings window of Hearth &amp; Harbor / Silverline is bigger and resizable (vanilla fixes it at 900x700).
    /// Only the window of our own mod is touched; every other mod's settings window stays vanilla.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(RimWorld.Dialog_ModSettings), nameof(RimWorld.Dialog_ModSettings.InitialSize), HarmonyLib.MethodType.Getter)]
    public static class Patch_ModSettings_InitialSize
    {
        static readonly System.Reflection.FieldInfo modField = HarmonyLib.AccessTools.Field(typeof(RimWorld.Dialog_ModSettings), "mod");
        public static bool IsOurs(RimWorld.Dialog_ModSettings d)
        {
            try { return modField != null && modField.GetValue(d) is HearthMod; } catch (Exception) { return false; }
        }
        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(RimWorld.Dialog_ModSettings __instance, ref Vector2 __result)
        {
            if (IsOurs(__instance)) __result = HUI.Size(1180f, 800f, 900f, 600f, 0.95f, 0.92f);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(RimWorld.Dialog_ModSettings), HarmonyLib.MethodType.Constructor, new[] { typeof(Mod) })]
    public static class Patch_ModSettings_Ctor
    {
        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(RimWorld.Dialog_ModSettings __instance)
        {
            if (Patch_ModSettings_InitialSize.IsOurs(__instance)) { __instance.resizeable = true; __instance.draggable = true; }
        }
    }
}
