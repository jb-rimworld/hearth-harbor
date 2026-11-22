// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>
    /// Row drawer for one HSetting against an explicit profile (so the window can edit the colony or the template).
    /// Two lines on the left (label, grey description), slider or checkbox on the right, free-entry switch, reset.
    /// Heights are cached; rows outside the visible part of a scroll view are not drawn at all.
    /// </summary>
    public static class SettingRowUI
    {
        /// <summary>Tests set this to draw every row even outside the viewport.</summary>
        public static bool ForceDrawAll;
        static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();

        public static HearthSettingsStore StoreFor(HSetting s, HearthProfile target) => s.Global ? HearthMod.Template.Profile.Store : target.Store;

        public static float Value(HSetting s, HearthProfile target)
        {
            var st = StoreFor(s, target);
            return st.TryGet(s.Key, out float v) ? s.Clamp(v) : s.DefaultFor(target.Tier);
        }
        public static bool IsOverridden(HSetting s, HearthProfile target) => StoreFor(s, target).Has(s.Key);
        public static void Set(HSetting s, HearthProfile target, float v) => StoreFor(s, target).Set(s.Key, s.Clamp(v));
        public static void Reset(HSetting s, HearthProfile target) { StoreFor(s, target).Remove(s.Key); buffers.Remove(s.Key); }

        public static float LabelWidth(float width) => Mathf.Clamp(width * 0.46f, 180f, 430f);

        public static float Height(HSetting s, float width)
        {
            float lw = LabelWidth(width) - 4f;
            float h = UiStyle.CachedHeight(s.Label, lw, GameFont.Small);
            string d = s.Desc;
            if (!d.NullOrEmpty()) h += UiStyle.CachedHeight(d, lw, GameFont.Tiny);
            return Mathf.Max(36f, h + 10f);
        }

        /// <summary>Draws the row at (x,y) if it intersects <paramref name="visible"/>; always returns its height.</summary>
        public static float Draw(float x, float y, float width, HSetting s, HearthProfile target, Rect visible)
        {
            float h = Height(s, width);
            if (!ForceDrawAll && (y + h < visible.yMin || y > visible.yMax)) return h;
            DrawRow(new Rect(x, y, width, h), s, target);
            return h;
        }

        static void DrawRow(Rect row, HSetting s, HearthProfile target)
        {
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            string tip = s.Desc;
            if (s.TierAnchors != null) tip += "\n\n" + "HH.UI.Row.FollowsTier".Translate();
            if (s.HardLimitReason != null) tip += "\n" + "HH.UI.Row.Limit".Translate() + ": " + s.HardLimitReason;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(row, tip);

            bool over = IsOverridden(s, target);
            float lw = LabelWidth(row.width);
            var lab = new Rect(row.x + 4f, row.y + 3f, lw - 4f, row.height - 4f);
            float lh = UiStyle.CachedHeight(s.Label, lab.width, GameFont.Small);
            UiStyle.LabelColored(new Rect(lab.x, lab.y, lab.width, lh), s.Label, over ? Color.white : new Color(0.88f, 0.88f, 0.88f));
            if (!s.Desc.NullOrEmpty()) UiStyle.LabelColored(new Rect(lab.x, lab.y + lh, lab.width, lab.height - lh), s.Desc, UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            if (over) Widgets.DrawBoxSolid(new Rect(row.x, row.y + 4f, 2f, row.height - 8f), UiStyle.Amber);
            if (s.Nerd) UiStyle.LabelColored(new Rect(row.xMax - 60f, row.y, 56f, 14f), "HH.UI.Row.Nerd".Translate(), UiStyle.DimGrey, TextAnchor.UpperRight, GameFont.Tiny);

            float x0 = row.x + lw + 8f, x1 = row.xMax - 52f;
            float cy = row.y + row.height / 2f;
            float val = Value(s, target);
            if (s.Kind == SettingKind.Bool)
            {
                bool b = val >= 0.5f, nb = b;
                Widgets.Checkbox(new Vector2(x0, cy - 12f), ref nb, 24f);
                if (nb != b) Set(s, target, nb ? 1f : 0f);
                UiStyle.LabelColored(new Rect(x0 + 30f, cy - 11f, 70f, 22f), nb ? "HH.UI.Row.On".Translate() : "HH.UI.Row.Off".Translate(), UiStyle.Grey, TextAnchor.MiddleLeft);
            }
            else
            {
                var store = StoreFor(s, target);
                bool free = store.freeText.TryGetValue(s.Key, out bool f) && f;
                float freeW = 64f, valW = 104f;
                float sliderW = Mathf.Max(70f, x1 - x0 - valW - freeW - 8f);
                float clampedForSlider = Mathf.Clamp(val, s.SliderMin, s.SliderMax);
                if (!free)
                {
                    float step = s.Step > 0 ? s.Step : (s.Kind == SettingKind.Int || s.Kind == SettingKind.Money || s.Kind == SettingKind.Enum ? 1f : -1f);
                    float nv = Widgets.HorizontalSlider(new Rect(x0, cy - 8f, sliderW, 16f), clampedForSlider, s.SliderMin, s.SliderMax, false, null, null, null, step);
                    if (Mathf.Abs(nv - clampedForSlider) > 1e-6f) Set(s, target, nv);
                    string shown = s.Format(Value(s, target));
                    UiStyle.LabelColored(new Rect(x0 + sliderW + 8f, cy - 11f, valW, 22f), shown, Mathf.Abs(val - clampedForSlider) > 1e-6f ? UiStyle.Amber : Color.white, TextAnchor.MiddleLeft);
                    if (Mathf.Abs(val - clampedForSlider) > 1e-6f) TooltipHandler.TipRegion(new Rect(x0 + sliderW, cy - 11f, valW, 22f), "HH.UI.Row.Beyond".Translate());
                }
                else
                {
                    if (!buffers.TryGetValue(s.Key, out string buf)) buf = val.ToString("0.#####", CultureInfo.InvariantCulture);
                    string nb = Widgets.TextField(new Rect(x0, cy - 12f, sliderW, 24f), buf);
                    buffers[s.Key] = nb;
                    if (float.TryParse(nb.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    {
                        float c = s.Clamp(parsed);
                        Set(s, target, c);
                        if (Mathf.Abs(c - parsed) > 1e-6f)
                        {
                            UiStyle.LabelColored(new Rect(x0 + sliderW + 8f, cy - 11f, valW + 40f, 22f), "HH.UI.Row.Limited".Translate(s.Format(c)), UiStyle.Amber, TextAnchor.MiddleLeft);
                            TooltipHandler.TipRegion(new Rect(x0 + sliderW, cy - 11f, valW + 40f, 22f), s.HardLimitReason ?? "HH.UI.Row.LimitGeneric".Translate().ToString());
                        }
                        else UiStyle.LabelColored(new Rect(x0 + sliderW + 8f, cy - 11f, valW, 22f), s.Format(c), UiStyle.Grey, TextAnchor.MiddleLeft);
                    }
                }
                bool nf = free;
                var fr = new Rect(x1 - freeW + 4f, cy - 12f, freeW, 24f);
                Widgets.CheckboxLabeled(fr, "HH.UI.Row.Free".Translate(), ref nf, false, null, null, true);
                TooltipHandler.TipRegion(fr, "HH.UI.Row.FreeTip".Translate());
                if (nf != free) { store.freeText[s.Key] = nf; buffers.Remove(s.Key); }
            }
            if (over && Widgets.ButtonText(new Rect(row.xMax - 48f, cy - 11f, 46f, 22f), "HH.UI.Row.Reset".Translate()))
                Reset(s, target);
        }
    }
}
