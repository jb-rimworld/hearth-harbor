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
            int cur = Mathf.Clamp(Mathf.RoundToInt(target.Tier), 0, 6);

            // --- big name + percent -------------------------------------------------------------------------------
            Text.Font = GameFont.Medium;
            var nameR = new Rect(x, y, w * 0.7f, 34f);
            GUI.color = UiStyle.Amber; Widgets.Label(nameR, HearthProfile.TierName(target.Tier).ToUpperInvariant()); GUI.color = Color.white;
            Text.Font = GameFont.Small;
            UiStyle.LabelColored(new Rect(x + w * 0.7f, y + 4f, w * 0.3f, 28f), "HH.UI.Diff.Intensity".Translate(HearthProfile.PercentFromTier(target.Tier).ToString("0")), UiStyle.Grey, TextAnchor.MiddleRight);
            y += 36f;
            UiStyle.LabelColored(new Rect(x, y, w, 24f), FeelText(target.Tier), UiStyle.Grey);
            y += 28f;

            // --- preset chips -------------------------------------------------------------------------------------
            float cw = (w - 6f * 6f) / 7f;
            for (int i = 0; i < 7; i++)
            {
                var r = new Rect(x + i * (cw + 6f), y, cw, 38f);
                string label = HearthProfile.TierName(i);
                if (UiStyle.Chip(r, label, i == cur && Mathf.Abs(target.Tier - i) < 0.02f, "HH.UI.Diff.ChipTip".Translate(label, HearthProfile.TierPercent[i].ToString("0"))))
                    target.Tier = i;
            }
            y += 46f;

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
            if (target.Tier >= 3.999f) { UiStyle.LabelColored(new Rect(x, y, w, 24f), "HH.UI.Diff.WarnHard".Translate(), UiStyle.Amber); y += 26f; }
            else if (target.Tier <= 0.5f) { UiStyle.LabelColored(new Rect(x, y, w, 24f), "HH.UI.Diff.WarnEasy".Translate(), UiStyle.Amber); y += 26f; }
            if (target.Tier >= 5.5f) { UiStyle.LabelColored(new Rect(x, y, w, 24f), "HH.UI.Diff.WarnBreaks".Translate(), UiStyle.Bad); y += 26f; }

            // --- live calculator ----------------------------------------------------------------------------------
            y += 6f;
            y += UiStyle.Section(x, y, w, "HH.UI.Diff.Calculator".Translate());
            bool infinite = target.Store.TryGet("core.infiniteMoney", out float im) && im >= 0.5f;
            float colW = Mathf.Min(w, 760f);
            foreach (var row in TierTable.Rows)
            {
                float h = 24f;
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
            y += UiStyle.Section(x, y, w, "HH.UI.Diff.Ladder".Translate());
            int ti = Mathf.Clamp(Mathf.RoundToInt(target.Tier), 0, 6);
            string[][] ladder = { TierTable.GoodwillCap, TierTable.RenownCap, TierTable.FineCap, TierTable.Raids };
            string[] ladderKeys = { "Goodwill", "Renown", "Fine", "Raids" };
            for (int i = 0; i < ladder.Length; i++)
            {
                float h = 22f;
                if (ForceOrInView(y, h, visible))
                {
                    UiStyle.LabelColored(new Rect(x + 4f, y, colW * 0.46f, h), ("HH.UI.Ladder." + ladderKeys[i]).Translate(), Color.white, TextAnchor.MiddleLeft);
                    string val = ladder[i][ti];
                    if (i == 3) val = val == "no" ? "HH.UI.Ladder.No".Translate() : val.Replace("yes", "HH.UI.Ladder.Yes".Translate());
                    UiStyle.LabelColored(new Rect(x + colW * 0.46f, y, colW * 0.4f, h), val, Color.white, TextAnchor.MiddleLeft);
                    TooltipHandler.TipRegion(new Rect(x, y, colW, h), ("HH.UI.Ladder." + ladderKeys[i] + ".Desc").Translate());
                }
                y += h;
            }
            UiStyle.LabelColored(new Rect(x, y + 2f, w, 22f), "HH.UI.Ladder.Footer".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += 28f;

            // --- special switches ---------------------------------------------------------------------------------
            y += 8f;
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

        static bool ForceOrInView(float y, float h, Rect visible) => SettingRowUI.ForceDrawAll || (y + h >= visible.yMin && y <= visible.yMax);
    }

    /// <summary>"Hosting difficulty…" window: used from the storyteller page, the first-start card and wherever a short door to the panel is needed.</summary>
    public sealed class Dialog_HostingDifficulty : Window
    {
        readonly HearthProfile target;
        readonly Action onChanged;
        Vector2 scroll; float viewH = 800f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(900f, UI.screenWidth - 40f), Mathf.Min(760f, UI.screenHeight - 60f));

        public Dialog_HostingDifficulty(HearthProfile target, Action onChanged = null)
        {
            this.target = target; this.onChanged = onChanged;
            doCloseX = true; doCloseButton = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UiProbe.Hit("diffdialog");
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), "HH.UI.Diff.Title".Translate());
            Text.Font = GameFont.Small;
            var outR = new Rect(inRect.x, inRect.y + 42f, inRect.width, inRect.height - 42f - 50f);
            var view = new Rect(0, 0, outR.width - 18f, viewH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            float h = Guard.Run(ModuleId.Core, "UI.DifficultyDialog", () => DifficultyPanel.Draw(0f, 0f, view.width, target, new Rect(0, scroll.y, view.width, outR.height)), viewH);
            viewH = h;
            Widgets.EndScrollView();
        }

        public override void PostClose() { base.PostClose(); onChanged?.Invoke(); }
    }
}
