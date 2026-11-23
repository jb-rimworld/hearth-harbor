// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>
    /// The difficulty page (9.1–9.3, 9.6): seven named tiers, one big slider (0–500 %), the live calculator with the key numbers
    /// of the current tier, the consequence ladder and the special switches. Used inside the settings window and in the
    /// "Hosting difficulty…" dialog, always against an explicit profile.
    /// </summary>
    public static class DifficultyPanel
    {
        static bool showAllTierSettings;
        /// <summary>Section start positions of the last Draw (scroll-view coordinates), for the dialog's "jump to" list.</summary>
        public static readonly List<KeyValuePair<string, float>> Anchors = new List<KeyValuePair<string, float>>();
        static void Anchor(string label, float y) { Anchors.Add(new KeyValuePair<string, float>(label, y)); }
        /// <summary>Special switches of 9.1 in display order (keys registered by the UI module; sealed fate is the Core randomness setting).</summary>
        public static readonly string[] SpecialKeys =
        {
            "core.neverDie", "core.infiniteMoney", "core.picky", "core.ironBank", "core.sealedFate", "core.noResearchGating",
            "core.peacefulUnderworld", "core.showMath", "core.perfMode",
        };

        public static string FeelText(float tier) => ("HH.UI.Feel." + Mathf.Clamp(Mathf.RoundToInt(tier), 0, 6)).Translate();

        /// <summary>Draws the panel starting at y within width w; returns the used height. <paramref name="visible"/> is the visible part (scroll view coordinates).</summary>
        public static float Draw(float x, float y0, float w, HearthProfile target, Rect visible)
        {
            float y = y0;
            Anchors.Clear(); Anchor("HH.UI.Diff.JumpTop".TranslateOr("Tier and slider"), y0);
            int cur = Mathf.Clamp(Mathf.RoundToInt(target.Tier), 0, 6);

            // --- big name + percent -------------------------------------------------------------------------------
            Text.Font = GameFont.Medium;
            var nameR = new Rect(x, y, w * 0.62f, 36f);
            GUI.color = UiStyle.Amber; Widgets.Label(nameR, HearthProfile.TierName(target.Tier).ToUpperInvariant()); GUI.color = Color.white;
            Text.Font = GameFont.Small;
            UiStyle.LabelColored(new Rect(x + w * 0.62f, y + 4f, w * 0.38f, 28f), "HH.UI.Diff.Intensity".Translate(HearthProfile.PercentFromTier(target.Tier).ToString("0")), UiStyle.Grey, TextAnchor.MiddleRight);
            y += 36f;
            string feel = FeelText(target.Tier);
            float feelH = Mathf.Max(24f, HUI.TextH(feel, w));
            UiStyle.LabelColored(new Rect(x, y, w, feelH), feel, UiStyle.Grey);
            y += feelH + 6f;

            // --- preset chips -------------------------------------------------------------------------------------
            float cw = (w - 6f * 6f) / 7f;
            for (int i = 0; i < 7; i++)
            {
                var r = new Rect(x + i * (cw + 6f), y, cw, 42f);
                string label = HearthProfile.TierName(i);
                if (UiStyle.Chip(r, label, i == cur && Mathf.Abs(target.Tier - i) < 0.02f, "HH.UI.Diff.ChipTip".Translate(label, HearthProfile.TierPercent[i].ToString("0"))))
                    target.Tier = i;
            }
            y += 50f;

            // --- big slider ---------------------------------------------------------------------------------------
            var sr = new Rect(x + 4f, y, w - 8f, 24f);
            float nt = Widgets.HorizontalSlider(sr, target.Tier, 0f, 6f, false, null, null, null, 0.01f);
            if (Mathf.Abs(nt - target.Tier) > 1e-5f) target.Tier = Mathf.Clamp(nt, 0f, 6f);
            // tick marks under the slider with the percentages
            for (int i = 0; i < 7; i++)
            {
                float tx = Mathf.Clamp(sr.x + 6f + (sr.width - 12f) * (i / 6f), sr.x + 30f, sr.xMax - 34f);
                UiStyle.LabelColored(new Rect(tx - 30f, y + 20f, 60f, 16f), HearthProfile.TierPercent[i].ToString("0") + " %", i == cur ? UiStyle.Amber : UiStyle.DimGrey, TextAnchor.UpperCenter, GameFont.Tiny);
            }
            y += 44f;

            // --- warnings -----------------------------------------------------------------------------------------
            if (target.Tier >= 3.999f) y += Warn(x, y, w, "HH.UI.Diff.WarnHard".Translate(), UiStyle.Amber);
            else if (target.Tier <= 0.5f) y += Warn(x, y, w, "HH.UI.Diff.WarnEasy".Translate(), UiStyle.Amber);
            if (target.Tier >= 5.5f) y += Warn(x, y, w, "HH.UI.Diff.WarnBreaks".Translate(), UiStyle.Bad);

            // --- live calculator ----------------------------------------------------------------------------------
            y += 6f;
            Anchor("HH.UI.Diff.Calculator".Translate(), y);
            y += UiStyle.Section(x, y, w, "HH.UI.Diff.Calculator".Translate());
            bool infinite = target.Store.TryGet("core.infiniteMoney", out float im) && im >= 0.5f;
            float colW = Mathf.Min(w, 900f);
            foreach (var row in TierTable.Rows)
            {
                float h = 28f;
                if (ForceOrInView(y, h, visible))
                {
                    var rr = new Rect(x, y, colW, h);
                    if (Mouse.IsOver(rr)) Widgets.DrawHighlight(rr);
                    float v = TierTable.Effective(row, target, out bool fromSetting, out bool overridden);
                    string label = ("HH.UI.Calc." + row.Id).Translate();
                    bool dead = infinite && row.Id == "guestMoney";
                    UiStyle.LabelColored(new Rect(x + 4f, y, colW * 0.46f, h), label, dead ? UiStyle.DimGrey : Color.white, TextAnchor.MiddleLeft);
                    string val = dead ? "∞" : row.FormatValue(v);
                    UiStyle.LabelColored(new Rect(x + colW * 0.46f, y, colW * 0.2f, h), val, dead ? UiStyle.DimGrey : (overridden ? UiStyle.Amber : Color.white), TextAnchor.MiddleLeft);
                    UiStyle.LabelColored(new Rect(x + colW * 0.64f, y, colW * 0.36f, h), "HH.UI.Calc.StandardIs".Translate(row.FormatValue(row.Standard)).ToString() + (fromSetting ? "" : "  ·  " + "HH.UI.Calc.Reference".Translate().ToString()),
                        UiStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    string tip = ("HH.UI.Calc." + row.Id + ".Desc").Translate();
                    if (dead) tip += "\n\n" + "HH.UI.Calc.NoEffectInfinite".Translate();
                    if (overridden) tip += "\n\n" + "HH.UI.Calc.SetByYou".Translate();
                    TooltipHandler.TipRegion(rr, tip);
                }
                y += h;
            }
            if (infinite) { UiStyle.LabelColored(new Rect(x, y, w, 22f), "HH.UI.Calc.NoEffectInfinite".Translate(), UiStyle.Amber, TextAnchor.UpperLeft, GameFont.Tiny); y += 22f; }

            // --- consequence ladder -------------------------------------------------------------------------------
            y += 8f;
            Anchor("HH.UI.Diff.Ladder".Translate(), y);
            y += UiStyle.Section(x, y, w, "HH.UI.Diff.Ladder".Translate());
            int ti = Mathf.Clamp(Mathf.RoundToInt(target.Tier), 0, 6);
            string[][] ladder = { TierTable.GoodwillCap, TierTable.RenownCap, TierTable.FineCap };
            string[] ladderKeys = { "Goodwill", "Renown", "Fine" };
            for (int i = 0; i < ladder.Length; i++)
            {
                float h = 26f;
                if (ForceOrInView(y, h, visible))
                {
                    UiStyle.LabelColored(new Rect(x + 4f, y, colW * 0.46f, h), ("HH.UI.Ladder." + ladderKeys[i]).Translate(), Color.white, TextAnchor.MiddleLeft);
                    string val = ladder[i][ti];
                    UiStyle.LabelColored(new Rect(x + colW * 0.46f, y, colW * 0.4f, h), val, Color.white, TextAnchor.MiddleLeft);
                    TooltipHandler.TipRegion(new Rect(x, y, colW, h), ("HH.UI.Ladder." + ladderKeys[i] + ".Desc").Translate());
                }
                y += h;
            }
            string ladderFoot = "HH.UI.Ladder.Footer".Translate();
            float lfh = HUI.TextH(ladderFoot, w, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(x, y + 2f, w, lfh), ladderFoot, UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += lfh + 8f;

            // --- special switches ---------------------------------------------------------------------------------
            y += 8f;
            Anchor("HH.UI.Diff.Special".Translate(), y);
            y += UiStyle.Section(x, y, w, "HH.UI.Diff.Special".Translate());
            foreach (var key in SpecialKeys)
            {
                var s = HSettings.Find(key);
                if (s == null) continue;
                y += SettingRowUI.Draw(x, y, w, s, target, visible);
            }

            // --- everything that follows the tier -----------------------------------------------------------------
            y += 10f;
            var tierSettings = HSettings.All.Where(s => s.TierAnchors != null && Modules.Available(s.Module)).ToList();
            var tog = new Rect(x, y, w, 24f);
            Widgets.CheckboxLabeled(tog, "HH.UI.Diff.ShowAll".Translate(tierSettings.Count), ref showAllTierSettings, false, null, null, true);
            y += 28f;
            if (showAllTierSettings)
            {
                foreach (var s in tierSettings.OrderBy(s => s.Module).ThenBy(s => s.Category))
                {
                    float h = 24f;
                    if (ForceOrInView(y, h, visible))
                    {
                        bool over = SettingRowUI.IsOverridden(s, target);
                        UiStyle.LabelColored(new Rect(x + 4f, y, w * 0.18f, h), Modules.Label(s.Module), UiStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny);
                        UiStyle.LabelColored(new Rect(x + w * 0.18f, y, w * 0.46f, h), s.Label, Color.white, TextAnchor.MiddleLeft);
                        UiStyle.LabelColored(new Rect(x + w * 0.66f, y, w * 0.3f, h), s.Format(SettingRowUI.Value(s, target)), over ? UiStyle.Amber : Color.white, TextAnchor.MiddleLeft);
                        TooltipHandler.TipRegion(new Rect(x, y, w, h), s.Desc);
                    }
                    y += h;
                }
            }
            return y - y0 + 10f;
        }

        static float Warn(float x, float y, float w, string text, Color c)
        {
            float h = Mathf.Max(24f, HUI.TextH(text, w));
            UiStyle.LabelColored(new Rect(x, y, w, h), text, c);
            return h + 4f;
        }

        static bool ForceOrInView(float y, float h, Rect visible) => SettingRowUI.ForceDrawAll || (y + h >= visible.yMin && y <= visible.yMax);
    }

    /// <summary>"Hosting difficulty…" window: used from the storyteller page, the first-start card and wherever a short door to the panel is needed.</summary>
    public sealed class Dialog_HostingDifficulty : Window
    {
        readonly HearthProfile target;
        readonly Action onChanged;
        Vector2 scroll; float viewH = 800f;
        public override Vector2 InitialSize => HUI.Size(1040f, 800f, 820f, 600f, 0.96f, 0.94f);

        public Dialog_HostingDifficulty(HearthProfile target, Action onChanged = null)
        {
            this.target = target; this.onChanged = onChanged;
            doCloseX = true; doCloseButton = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
            resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UiProbe.Hit("diffdialog");
            string title = "HH.UI.Diff.Title".Translate();
            float th = Mathf.Max(36f, HUI.TextH(title, inRect.width, GameFont.Medium));
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, th), title);
            Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(new Rect(inRect.x, inRect.y + th + 2f, Mathf.Min(inRect.width, 180f), 2f), UiStyle.Amber);
            float top = inRect.y + th + 10f;
            var area = new Rect(inRect.x, top, inRect.width, inRect.height - (top - inRect.y) - 50f);
            // quick-jump list on the left when there is room
            float navW = area.width >= 860f ? 190f : 0f;
            var outR = new Rect(area.x + (navW > 0 ? navW + 14f : 0f), area.y, area.width - (navW > 0 ? navW + 14f : 0f), area.height);
            float jumpTo = -1f;
            if (navW > 0f)
            {
                UiStyle.LabelColored(new Rect(area.x, area.y, navW, 22f), "HH.UI.Diff.Jump".TranslateOr("Jump to"), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
                float ny = area.y + 24f;
                foreach (var a in DifficultyPanel.Anchors.ToList())
                {
                    float rh = Mathf.Max(30f, HUI.TextH(a.Key, navW - 20f) + 8f);
                    var rr = new Rect(area.x, ny, navW, rh);
                    if (Mouse.IsOver(rr)) Widgets.DrawHighlight(rr);
                    UiStyle.LabelColored(new Rect(rr.x + 10f, rr.y, rr.width - 14f, rr.height), a.Key, Color.white, TextAnchor.MiddleLeft);
                    if (Widgets.ButtonInvisible(rr)) jumpTo = a.Value;
                    ny += rh + 2f;
                }
                Widgets.DrawBoxSolid(new Rect(area.x + navW + 6f, area.y, 1f, area.height), UiStyle.Line);
            }
            if (jumpTo >= 0f) scroll.y = Mathf.Max(0f, jumpTo);
            var view = new Rect(0, 0, outR.width - 18f, viewH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            float h = Guard.Run(ModuleId.Core, "UI.DifficultyDialog", () => DifficultyPanel.Draw(0f, 0f, view.width, target, new Rect(0, scroll.y, view.width, outR.height)), viewH);
            viewH = h;
            Widgets.EndScrollView();
        }

        public override void PostClose() { base.PostClose(); onChanged?.Invoke(); }
    }
}
