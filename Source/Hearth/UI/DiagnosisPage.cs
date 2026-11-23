// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>Plain text window with a copy button (used for the removal report).</summary>
    public sealed class Dialog_TextReport : Window
    {
        readonly string title, text; Vector2 scroll;
        readonly int lineCount;
        public override Vector2 InitialSize => HUI.Size(820f, 640f, 600f, 480f, 0.92f, 0.92f);
        public Dialog_TextReport(string title, string text)
        {
            this.title = title; this.text = text ?? ""; doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true; resizeable = true; draggable = true;
            int n = 1; foreach (char ch in this.text) if (ch == '\n') n++;
            lineCount = this.text.Length == 0 ? 0 : n;
        }
        public override void DoWindowContents(Rect inRect)
        {
            float th = Mathf.Max(34f, HUI.TextH(title, inRect.width, GameFont.Medium));
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, th), title); Text.Font = GameFont.Small;
            string meta = "HH.UI.Diag.Lines".TranslateOr("{0} lines").Replace("{0}", lineCount.ToString());
            UiStyle.LabelColored(new Rect(inRect.x, inRect.y + th, inRect.width, 20f), meta, UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            float top = inRect.y + th + 24f;
            // bottom row: Copy at the left, the vanilla Close button sits at the center of the same row
            float rowH = 40f;
            var outR = new Rect(inRect.x, top, inRect.width, inRect.yMax - top - rowH - 14f);
            Widgets.DrawBoxSolid(outR, new Color(0f, 0f, 0f, 0.18f));
            var inner = outR.ContractedBy(6f);
            float h = UiStyle.CachedHeight(text, inner.width - 20f, GameFont.Small);
            var view = new Rect(0, 0, inner.width - 18f, h + 8f);
            Widgets.BeginScrollView(inner, ref scroll, view);
            Widgets.Label(new Rect(0, 0, view.width, h + 4f), text);
            Widgets.EndScrollView();
            string copy = "HH.UI.Diag.Copy".Translate();
            float cw = Mathf.Clamp(HUI.TextW(copy) + 36f, 200f, Mathf.Max(200f, inRect.width / 2f - Window.CloseButSize.x / 2f - 12f));
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - rowH, cw, 36f), copy)) GUIUtility.systemCopyBuffer = text;
        }
    }

    /// <summary>Diagnosis page (10.3): versions, module state with "Resume", caretaker, patches, recent log, copy and "Prepare removal".</summary>
    public static class DiagnosisPage
    {
        static Vector2 logScroll;
        static string lastRemovalReport;
        public static string LastRemovalReport => lastRemovalReport;

        public static List<string> RecentLog(int n)
        {
            try { return HLog.Recent.Skip(Math.Max(0, HLog.Recent.Count - n)).ToList(); } catch { return new List<string>(); }
        }

        /// <summary>Everything the page shows, as text for the clipboard (no personal data, no file paths).</summary>
        public static string BuildText(int logLines = 60)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hearth & Harbor " + HearthMod.Version + " · RimWorld " + VersionControl.CurrentVersionStringWithRev);
            sb.AppendLine("Mods active: " + LoadedModManager.RunningModsListForReading.Count + " · settings registered: " + HSettings.All.Count + " · log warnings/errors: " + HLog.WarnCount + "/" + HLog.ErrorCount);
            var p = HearthProfile.Current;
            if (p != null) sb.AppendLine("Colony profile: tier " + p.Tier.ToString("0.00") + " (" + HearthProfile.TierName(p.Tier) + "), content " + p.Content);
            sb.AppendLine("Modules:");
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core))
                sb.AppendLine("  " + m + ": " + (Modules.Available(m) ? (Guard.IsPaused(m) ? "PAUSED" : (Modules.On(m) ? "on" : "off")) : "not installed"));
            foreach (var r in Guard.PauseReasons) sb.AppendLine("  pause reason: " + r);
            sb.AppendLine("Patch classes skipped: " + HearthMod.PatchFailures.Count);
            foreach (var f in HearthMod.PatchFailures) sb.AppendLine("  " + f);
            sb.AppendLine("Caretaker checks: " + Caretaker.Checks.Count + ", fixes total " + Caretaker.TotalFixes);
            foreach (var c in Caretaker.Checks) sb.AppendLine("  " + c.Name + " (" + c.Module + "): last " + c.LastFixes + ", total " + c.TotalFixes);
            foreach (var m in HearthMod.ModuleObjects)
            {
                var lines = new List<string>();
                try { lines.AddRange(m.Diagnosis() ?? Enumerable.Empty<string>()); } catch (Exception e) { lines.Add("diagnosis failed: " + e.Message); }
                foreach (var l in lines) sb.AppendLine("  [" + m.Id + "] " + l);
            }
            sb.AppendLine("Last log lines:");
            foreach (var l in RecentLog(logLines)) sb.AppendLine(l.Split('\n')[0]);
            return sb.ToString();
        }

        public static float Draw(float w, Rect visible)
        {
            float y = 0;
            UiStyle.LabelColored(new Rect(0, y, w, 24f), "Hearth & Harbor " + HearthMod.Version + "  ·  RimWorld " + VersionControl.CurrentVersionStringWithRev, UiStyle.Grey);
            y += 26f;
            UiStyle.LabelColored(new Rect(0, y, w, 24f), "HH.UI.Diag.Summary".Translate(HSettings.All.Count, HLog.WarnCount, HLog.ErrorCount), UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += 24f;
            // buttons
            if (Widgets.ButtonText(new Rect(0, y, 240f, 30f), "HH.UI.Diag.Copy".Translate())) { GUIUtility.systemCopyBuffer = BuildText(); Messages.Message("HH.UI.Diag.Copied".Translate(), MessageTypeDefOf.SilentInput, false); }
            if (Widgets.ButtonText(new Rect(250f, y, 240f, 30f), "HH.UI.Diag.PrepareRemoval".Translate()))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Diag.PrepareRemovalConfirm".Translate(), PrepareRemoval, true));
            TooltipHandler.TipRegion(new Rect(250f, y, 240f, 30f), "HH.UI.Diag.PrepareRemovalTip".Translate());
            y += 40f;

            // modules
            y += UiStyle.Section(0, y, w, "HH.UI.Diag.Modules".Translate());
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core))
            {
                var row = new Rect(0, y, w, 26f);
                bool avail = Modules.Available(m), paused = Guard.IsPaused(m);
                UiStyle.LabelColored(new Rect(4f, y, 220f, 26f), Modules.Label(m), avail ? Color.white : UiStyle.DimGrey, TextAnchor.MiddleLeft);
                string state = !avail ? "HH.UI.Modules.NotInstalled".Translate() : paused ? "HH.UI.Diag.Paused".Translate() : Modules.On(m) ? "HH.UI.Row.On".Translate() : "HH.UI.Row.Off".Translate();
                UiStyle.LabelColored(new Rect(230f, y, 160f, 26f), state, paused ? UiStyle.Bad : (Modules.On(m) ? UiStyle.Good : UiStyle.Grey), TextAnchor.MiddleLeft);
                if (paused && Widgets.ButtonText(new Rect(400f, y + 1f, 120f, 24f), "HH.UI.Diag.Resume".Translate())) Guard.Resume(m);
                y += 26f;
            }
            foreach (var r in Guard.PauseReasons)
            {
                float h = UiStyle.CachedHeight(r, w - 20f, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(10f, y, w - 20f, h), r, UiStyle.Bad, TextAnchor.UpperLeft, GameFont.Tiny); y += h + 2f;
            }

            // caretaker
            y += 8f;
            y += UiStyle.Section(0, y, w, "HH.UI.Diag.Caretaker".Translate(Caretaker.TotalFixes));
            if (Caretaker.Checks.Count == 0) { UiStyle.LabelColored(new Rect(4f, y, w, 22f), "HH.UI.Diag.NoChecks".Translate(), UiStyle.DimGrey); y += 24f; }
            foreach (var c in Caretaker.Checks)
            {
                UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 22f), c.Name, Color.white, TextAnchor.MiddleLeft);
                UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 22f), "HH.UI.Diag.Fixes".Translate(c.LastFixes, c.TotalFixes), c.TotalFixes > 0 ? UiStyle.Amber : UiStyle.Grey, TextAnchor.MiddleLeft);
                y += 22f;
            }

            // patches
            y += 8f;
            y += UiStyle.Section(0, y, w, "HH.UI.Diag.Patches".Translate(HearthMod.PatchFailures.Count));
            if (HearthMod.PatchFailures.Count == 0) { UiStyle.LabelColored(new Rect(4f, y, w, 22f), "HH.UI.Diag.PatchesOk".Translate(), UiStyle.Good); y += 24f; }
            foreach (var f in HearthMod.PatchFailures)
            {
                float h = UiStyle.CachedHeight(f, w - 20f, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(10f, y, w - 20f, h), f, UiStyle.Bad, TextAnchor.UpperLeft, GameFont.Tiny); y += h + 2f;
            }

            // module diagnosis lines
            foreach (var m in HearthMod.ModuleObjects)
            {
                List<string> lines;
                try { lines = (m.Diagnosis() ?? Enumerable.Empty<string>()).ToList(); } catch (Exception e) { lines = new List<string> { "diagnosis failed: " + e.Message }; }
                if (lines.Count == 0) continue;
                y += 8f;
                y += UiStyle.Section(0, y, w, Modules.Label(m.Id));
                foreach (var l in lines) { UiStyle.LabelColored(new Rect(4f, y, w - 8f, 20f), l, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f; }
            }

            // log
            y += 8f;
            y += UiStyle.Section(0, y, w, "HH.UI.Diag.Log".Translate());
            var lg = RecentLog(60);
            var logR = new Rect(0, y, w, 260f);
            Widgets.DrawBoxSolid(logR, new Color(0f, 0f, 0f, 0.35f));
            float lineH = 16f;
            var lview = new Rect(0, 0, logR.width - 18f, Mathf.Max(logR.height, lg.Count * lineH + 6f));
            Widgets.BeginScrollView(logR, ref logScroll, lview);
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < lg.Count; i++)
            {
                string line = lg[i].Split('\n')[0];
                bool err = line.Contains("| ERROR |"), warn = line.Contains("| WARN  |");
                GUI.color = err ? UiStyle.Bad : warn ? UiStyle.Amber : UiStyle.Grey;
                Widgets.Label(new Rect(4f, 3f + i * lineH, lview.width, lineH), line);
            }
            GUI.color = Color.white; Text.Font = GameFont.Small;
            Widgets.EndScrollView();
            y += logR.height + 10f;
            return y;
        }

        public static void PrepareRemoval()
        {
            var report = new List<string>();
            if (Current.ProgramState != ProgramState.Playing)
            {
                Find.WindowStack.Add(new Dialog_TextReport("HH.UI.Diag.RemovalTitle".Translate(), "HH.UI.Diag.NeedsSave".Translate()));
                return;
            }
            foreach (var m in HearthMod.ModuleObjects)
            {
                var part = new List<string>();
                Guard.Run(m.Id, m.GetType().Name + ".PrepareRemoval", () => m.PrepareRemoval(part));
                if (part.Count > 0) { report.Add("== " + Modules.Label(m.Id)); report.AddRange(part.Select(l => "  " + l)); }
            }
            if (report.Count == 0) report.Add("HH.UI.Diag.NothingToDo".Translate());
            report.Add("");
            report.Add("HH.UI.Diag.RemovalDone".Translate());
            lastRemovalReport = string.Join("\n", report);
            HLog.Info("UI", "Prepare removal run:\n" + lastRemovalReport);
            Find.WindowStack.Add(new Dialog_TextReport("HH.UI.Diag.RemovalTitle".Translate(), lastRemovalReport));
        }
    }
}
