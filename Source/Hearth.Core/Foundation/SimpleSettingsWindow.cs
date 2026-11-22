// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Fallback settings window drawing every registered HSetting (used by Silverline standalone and until Hearth.dll
    /// installs its richer window via HearthMod.SettingsWindowOverride). Reusable row drawing: SettingRow.Draw.
    /// </summary>
    public static class SimpleSettingsWindow
    {
        static Vector2 scroll; static string search = ""; static float viewH = 1000f;

        public static void Draw(Rect inRect)
        {
            var tpl = HearthMod.Template;
            bool inGame = HearthProfile.Current != null;
            var top = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            Widgets.Label(top.LeftPart(0.6f), inGame ? "HH.Settings.ThisSave".TranslateOr("Settings of this save game") : "HH.Settings.Template".TranslateOr("Template for new games"));
            search = Widgets.TextField(top.RightPart(0.38f), search);
            var opts = new Rect(inRect.x, inRect.y + 32f, inRect.width, 26f);
            Widgets.CheckboxLabeled(opts.LeftHalf(), "HH.Settings.ShowNerd".TranslateOr("Show nerd settings"), ref tpl.ShowNerd);
            Widgets.CheckboxLabeled(opts.RightHalf(), "HH.Settings.Verbose".TranslateOr("Verbose log"), ref tpl.VerboseLog);
            var outR = new Rect(inRect.x, inRect.y + 64f, inRect.width, inRect.height - 64f);
            var view = new Rect(0, 0, outR.width - 16f, viewH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            float y = 0;
            var prof = HearthProfile.Current ?? tpl.Profile;
            // modules the error barrier has paused: show why, offer to restart them
            foreach (var pm in Guard.Paused.ToList())
            {
                var pr = new Rect(0, y, view.width, 44);
                Widgets.DrawBoxSolid(pr, new Color(0.45f, 0.12f, 0.12f, 0.35f));
                Widgets.Label(new Rect(pr.x + 6, pr.y + 2, pr.width - 130, 40), Modules.Label(pm) + ": " + ("HH.Settings.Paused".TranslateOr("paused after repeated errors")) + "\n" + (Guard.ReasonFor(pm) ?? ""));
                if (Widgets.ButtonText(new Rect(pr.xMax - 118, pr.y + 8, 112, 28), "HH.Settings.Resume".TranslateOr("Resume"))) Guard.Resume(pm);
                y += 48;
            }
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, view.width, 32), "HH.Settings.Modules".TranslateOr("Modules")); Text.Font = GameFont.Small; y += 34;
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core && Modules.Available(m)))
            {
                bool was = prof.ModuleOn(m), on = was;
                Widgets.CheckboxLabeled(new Rect(0, y, Math.Min(420, view.width), 24), Modules.Label(m), ref on);
                if (on != was) ChangeModule(prof, m, on);
                y += 26;
            }
            y += 8;
            Widgets.Label(new Rect(0, y, 260, 24), "HH.Settings.Difficulty".TranslateOr("Hosting difficulty") + ": " + HearthProfile.TierName(prof.Tier));
            float oldTier = prof.Tier;
            prof.Tier = Widgets.HorizontalSlider(new Rect(270, y + 4, Math.Max(100, view.width - 280), 20), prof.Tier, 0f, 6f, true, null, null, null, 0.05f);
            if (prof.Tier != oldTier) SettingRow.ClearBuffers(); // defaults of tier-linked settings moved
            y += 34;
            string lastHead = null;
            foreach (var s in HSettings.All.Where(s => (tpl.ShowNerd || !s.Nerd) && Modules.Available(s.Module) && Match(s)).OrderBy(s => s.Module).ThenBy(s => s.Category))
            {
                string head = Modules.Label(s.Module) + " · " + s.Category;
                if (head != lastHead)
                {
                    y += 6; Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, view.width, 30), head); Text.Font = GameFont.Small; y += 32; lastHead = head;
                }
                y += SettingRow.Draw(new Rect(0, y, view.width, 0), s);
            }
            viewH = y + 20;
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Switches a module on or off. Switching off inside a loaded game asks whether to run the module's PrepareRemoval first
        /// (send guests home, refund, strip hediffs …); the module is switched off either way, unless the player cancels.
        /// </summary>
        static void ChangeModule(HearthProfile prof, ModuleId m, bool on)
        {
            bool loaded = HearthProfile.Current == prof && Current.ProgramState == ProgramState.Playing;
            var obj = HearthMod.ModuleObjects.FirstOrDefault(x => x.Id == m);
            if (on || !loaded || obj == null) { prof.SetModule(m, on); return; }
            var dlg = new Dialog_MessageBox(
                ("HH.Settings.PrepareRemovalText".TranslateOr("You are switching off {0} in this save. Prepare removal now (send guests home, refund, clean up) or just keep everything in place?")).Replace("{0}", Modules.Label(m)),
                "HH.Settings.PrepareNow".TranslateOr("Prepare removal now"), () =>
                {
                    var report = new List<string>();
                    Guard.Run(m, obj.GetType().Name + ".PrepareRemoval", () => obj.PrepareRemoval(report));
                    prof.SetModule(m, false);
                    foreach (var line in report) HLog.Info(m.ToString(), "PrepareRemoval: " + line);
                    Messages.Message(report.Count == 0 ? Modules.Label(m) + ": " + "HH.Settings.RemovalDone".TranslateOr("nothing to clean up") : string.Join("\n", report), MessageTypeDefOf.NeutralEvent, false);
                },
                "HH.Settings.KeepInPlace".TranslateOr("Keep things in place"), () => prof.SetModule(m, false));
            dlg.buttonCText = "HH.Settings.Cancel".TranslateOr("Cancel");
            dlg.buttonCAction = () => { };
            Find.WindowStack.Add(dlg);
        }

        static bool Match(HSetting s)
        {
            if (string.IsNullOrEmpty(search)) return true;
            string q = search.ToLowerInvariant();
            return s.Label.ToLowerInvariant().Contains(q) || s.Key.ToLowerInvariant().Contains(q) || (s.Desc ?? "").ToLowerInvariant().Contains(q);
        }
    }

    public static class SettingRow
    {
        static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();
        internal static void ClearBuffers() => buffers.Clear();

        /// <summary>Draws one setting row and returns its height. Slider + value, free-text toggle, reset, tooltip with description.</summary>
        public static float Draw(Rect r, HSetting s)
        {
            float h = 28f;
            var row = new Rect(r.x, r.y, r.width, h);
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            string tip = s.Desc + (s.TierAnchors != null ? "\n\n" + "HH.Settings.FollowsDifficulty".TranslateOr("Default follows the hosting difficulty.") : "")
                         + (s.HardLimitReason != null ? "\n" + "HH.Settings.Limit".TranslateOr("Technical limit") + ": " + s.HardLimitReason : "");
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(row, tip);
            float labelW = Math.Min(340f, r.width * 0.42f);
            bool over = HSettings.IsOverridden(s);
            GUI.color = over ? Color.white : new Color(0.85f, 0.85f, 0.85f);
            Widgets.Label(new Rect(row.x, row.y + 3, labelW, 24), s.Label);
            GUI.color = Color.white;
            float x = row.x + labelW + 6;
            float val = HSettings.Get(s);
            if (s.Kind == SettingKind.Bool)
            {
                bool b = val >= 0.5f; bool nb = b;
                Widgets.Checkbox(new Vector2(x, row.y + 2), ref nb);
                if (nb != b) HSettings.Set(s, nb ? 1 : 0);
            }
            else
            {
                var store = HSettings.Store(s); // same store as the value (global settings live in the template)
                bool free = store.freeText.TryGetValue(s.Key, out bool f) && f;
                float sliderW = Math.Max(80f, row.xMax - x - 210f);
                if (!free)
                {
                    float roundTo = s.IsLog ? -1f : s.Step > 0 ? s.Step : (s.Kind == SettingKind.Int || s.Kind == SettingKind.Money || s.Kind == SettingKind.Enum ? 1f : -1f);
                    // log-scale settings move on ln(value); ToSlider/FromSlider hide that
                    float input = s.ToSlider(val);
                    // the slider rounds its output even when untouched: compare with the equally rounded input so that
                    // merely drawing a row never overrides (and thereby detaches) a tier-linked default
                    float rounded = roundTo > 0f ? (float)Mathf.RoundToInt(input / roundTo) * roundTo : input;
                    float nv = Widgets.HorizontalSlider(new Rect(x, row.y + 8, sliderW, 16), input, s.SliderLo, s.SliderHi, false, null, null, null, roundTo);
                    if (Math.Abs(nv - rounded) > Math.Max(1e-6f, Math.Abs(rounded) * 1e-6f)) HSettings.Set(s, s.FromSlider(nv));
                    Widgets.Label(new Rect(x + sliderW + 6, row.y + 3, 110, 24), s.Format(HSettings.Get(s)));
                }
                else
                {
                    string key = s.Key;
                    if (!buffers.TryGetValue(key, out string buf)) buf = HSettings.Get(s).ToString(CultureInfo.InvariantCulture);
                    string nb = Widgets.TextField(new Rect(x, row.y + 2, sliderW, 24), buf);
                    bool typed = nb != buf;
                    buffers[key] = nb;
                    if (float.TryParse(nb.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    {
                        float c = s.Clamp(parsed);
                        if (typed) HSettings.Set(s, c); // only when the text changed, never just from drawing
                        if (Math.Abs(c - parsed) > 1e-6f)
                        {
                            GUI.color = new Color(1f, 0.75f, 0.4f);
                            Widgets.Label(new Rect(x + sliderW + 6, row.y + 3, 110, 24), "HH.Settings.Limited".TranslateOr("limit") + " " + s.Format(c));
                            GUI.color = Color.white;
                            TooltipHandler.TipRegion(row, s.HardLimitReason ?? "HH.Settings.LimitGeneric".TranslateOr("This value cannot be used by the game; the nearest possible value is used."));
                        }
                    }
                }
                bool nf = free;
                Widgets.CheckboxLabeled(new Rect(row.xMax - 96, row.y + 2, 70, 24), "HH.Settings.Free".TranslateOr("Free"), ref nf);
                if (nf != free) { store.freeText[s.Key] = nf; buffers.Remove(s.Key); }
            }
            if (over && Widgets.ButtonText(new Rect(row.xMax - 24, row.y + 2, 24, 24), "↺", false))
            { HSettings.Reset(s); buffers.Remove(s.Key); }
            return h;
        }
    }
}
