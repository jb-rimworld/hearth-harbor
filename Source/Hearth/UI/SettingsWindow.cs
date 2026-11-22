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
    /// The rich settings window (10.3). Sidebar: this save / template switch, Modules, Difficulty, one page per module with
    /// settings, Diagnosis, About. Everything is drawn against an explicit profile, so the template can be edited while a game runs.
    /// Installed through HearthMod.SettingsWindowOverride; exceptions are caught per page so a bad row never breaks the window.
    /// </summary>
    public static class HearthSettingsWindow
    {
        public sealed class PageEntry { public string Id; public string Label; public ModuleId? Module; public bool Dim; }

        static string pageId = "modules";
        static bool editTemplate;
        static string search = "";
        static Vector2 scrollSide, scrollMain;
        static float mainViewH = 1000f;
        static readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        public static string SelectedPage { get => pageId; set => pageId = value; }
        public static string Search { get => search; set => search = value ?? ""; }
        public static bool EditTemplate { get => editTemplate; set => editTemplate = value; }
        public static bool ShowNerd => HearthMod.Template.ShowNerd || (HearthProfile.Current != null && HSettings.GetBool("core.showMath"));

        public static HearthProfile Target => editTemplate || HearthProfile.Current == null ? HearthMod.Template.Profile : HearthProfile.Current;

        public static List<PageEntry> BuildPages()
        {
            var list = new List<PageEntry>
            {
                new PageEntry { Id = "modules", Label = "HH.UI.Page.Modules".Translate() },
                new PageEntry { Id = "difficulty", Label = "HH.UI.Page.Difficulty".Translate() },
            };
            var target = Target;
            foreach (var m in Modules.All)
            {
                if (!Modules.Available(m)) continue;
                if (!HSettings.All.Any(s => s.Module == m && !(m == ModuleId.Core && s.Category == "Special switches"))) continue;
                list.Add(new PageEntry { Id = "mod:" + m, Label = m == ModuleId.Core ? "HH.UI.Page.General".Translate() : Modules.Label(m), Module = m, Dim = m != ModuleId.Core && !target.ModuleOn(m) });
            }
            list.Add(new PageEntry { Id = "diagnosis", Label = "HH.UI.Page.Diagnosis".Translate() });
            list.Add(new PageEntry { Id = "about", Label = "HH.UI.Page.About".Translate() });
            return list;
        }

        public static void Install()
        {
            HearthMod.SettingsWindowOverride = Draw;
        }

        public static void Draw(Rect inRect)
        {
            Guard.Run(ModuleId.Core, "UI.SettingsWindow", () => DrawInner(inRect));
        }

        static void DrawInner(Rect inRect)
        {
            bool inGame = HearthProfile.Current != null;
            if (!inGame) editTemplate = true;
            var pages = BuildPages();
            if (pages.All(p => p.Id != pageId)) pageId = pages[0].Id;

            var side = new Rect(inRect.x, inRect.y, 200f, inRect.height);
            var main = new Rect(side.xMax + 12f, inRect.y, inRect.width - side.width - 12f, inRect.height);
            DrawSidebar(side, pages, inGame);
            Widgets.DrawBoxSolid(new Rect(side.xMax + 5f, inRect.y, 1f, inRect.height), UiStyle.Line);
            DrawMain(main, pages, inGame);
        }

        // ---------------------------------------------------------------------------------------------------------
        static void DrawSidebar(Rect r, List<PageEntry> pages, bool inGame)
        {
            float y = r.y;
            if (inGame)
            {
                float hw = (r.width - 6f) / 2f;
                if (UiStyle.Chip(new Rect(r.x, y, hw, 28f), "HH.UI.Settings.ThisSave".Translate(), !editTemplate, "HH.UI.Settings.ThisSaveTip".Translate())) editTemplate = false;
                if (UiStyle.Chip(new Rect(r.x + hw + 6f, y, hw, 28f), "HH.UI.Settings.Template".Translate(), editTemplate, "HH.UI.Settings.TemplateTip".Translate())) editTemplate = true;
                y += 36f;
            }
            else
            {
                UiStyle.LabelColored(new Rect(r.x, y, r.width, 40f), "HH.UI.Settings.TemplateOnly".Translate(), UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                y += 44f;
            }
            float bottomH = inGame ? 66f : 0f;
            var listR = new Rect(r.x, y, r.width, r.height - (y - r.y) - bottomH);
            float contentH = pages.Count * 30f;
            var view = new Rect(0, 0, listR.width - (contentH > listR.height ? 16f : 0f), contentH);
            Widgets.BeginScrollView(listR, ref scrollSide, view);
            float py = 0;
            foreach (var p in pages)
            {
                var row = new Rect(0, py, view.width, 28f);
                bool sel = p.Id == pageId;
                if (sel) { Widgets.DrawBoxSolid(row, UiStyle.AmberSoft); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), UiStyle.Amber); }
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                UiStyle.LabelColored(new Rect(row.x + 10f, row.y, row.width - 12f, row.height), p.Label + (p.Dim ? "  (" + "HH.UI.Settings.Off".Translate() + ")" : ""), p.Dim ? UiStyle.DimGrey : Color.white, TextAnchor.MiddleLeft);
                if (Widgets.ButtonInvisible(row)) { pageId = p.Id; scrollMain = Vector2.zero; mainViewH = 1000f; }
                py += 30f;
            }
            Widgets.EndScrollView();

            if (inGame)
            {
                float by = r.yMax - bottomH + 6f;
                if (Widgets.ButtonText(new Rect(r.x, by, r.width, 28f), "HH.UI.Settings.SaveAsTemplate".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Settings.SaveAsTemplateConfirm".Translate(), SaveColonyAsTemplate));
                TooltipHandler.TipRegion(new Rect(r.x, by, r.width, 28f), "HH.UI.Settings.SaveAsTemplateTip".Translate());
                if (Widgets.ButtonText(new Rect(r.x, by + 32f, r.width, 28f), "HH.UI.Settings.ApplyTemplate".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Settings.ApplyTemplateConfirm".Translate(), ApplyTemplateToColony, true));
                TooltipHandler.TipRegion(new Rect(r.x, by + 32f, r.width, 28f), "HH.UI.Settings.ApplyTemplateTip".Translate());
            }
        }

        public static void SaveColonyAsTemplate()
        {
            var cur = HearthProfile.Current; if (cur == null) return;
            HearthMod.Template.Profile = UiState.CleanClone(cur);
            try { HearthMod.Instance.WriteSettings(); } catch (Exception e) { HLog.Warn("UI", "Could not write template: " + e.Message); }
            Messages.Message("HH.UI.Settings.TemplateSaved".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void ApplyTemplateToColony()
        {
            var cur = HearthProfile.Current; if (cur == null) return;
            var t = HearthMod.Template.Profile;
            var keep = UiState.OwnKeys().Where(k => cur.Store.Has(k)).Select(k => { cur.Store.TryGet(k, out float v); return new KeyValuePair<string, float>(k, v); }).ToList();
            cur.modules = new Dictionary<ModuleId, bool>(t.modules);
            cur.Tier = t.Tier; cur.Content = t.Content; cur.UnderworldDiscovered = t.UnderworldDiscovered;
            cur.Store = t.Store.Clone();
            foreach (var kv in keep) cur.Store.Set(kv.Key, kv.Value);
            Messages.Message("HH.UI.Settings.TemplateApplied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        // ---------------------------------------------------------------------------------------------------------
        static void DrawMain(Rect r, List<PageEntry> pages, bool inGame)
        {
            var page = pages.First(p => p.Id == pageId);
            var target = Target;
            // header: title, search, nerd
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width * 0.5f, 34f), string.IsNullOrEmpty(search) ? page.Label : "HH.UI.Settings.SearchResults".Translate().ToString());
            Text.Font = GameFont.Small;
            bool searchable = pageId != "diagnosis" && pageId != "about";
            if (searchable)
            {
                float sw = Mathf.Min(220f, r.width * 0.3f);
                var sr = new Rect(r.xMax - sw, r.y + 2f, sw, 28f);
                search = Widgets.TextField(sr, search);
                if (search.NullOrEmpty()) UiStyle.LabelColored(new Rect(sr.x + 6f, sr.y, sr.width - 8f, sr.height), "HH.UI.Settings.Search".Translate(), UiStyle.DimGrey, TextAnchor.MiddleLeft);
                else if (Widgets.ButtonText(new Rect(sr.x - 30f, sr.y, 26f, 28f), "x")) search = "";
                var nr = new Rect(sr.x - 200f, r.y + 4f, 190f, 24f);
                if (!search.NullOrEmpty()) nr.x -= 34f;
                bool nerd = HearthMod.Template.ShowNerd;
                Widgets.CheckboxLabeled(nr, "HH.UI.Settings.Nerd".Translate(), ref nerd);
                TooltipHandler.TipRegion(nr, "HH.UI.Settings.NerdTip".Translate());
                HearthMod.Template.ShowNerd = nerd;
            }
            float top = r.y + 38f;
            if (inGame && editTemplate)
            { UiStyle.LabelColored(new Rect(r.x, top, r.width, 22f), "HH.UI.Settings.EditingTemplate".Translate(), UiStyle.Amber, TextAnchor.UpperLeft, GameFont.Tiny); top += 22f; }
            var outR = new Rect(r.x, top, r.width, r.yMax - top);
            var view = new Rect(0, 0, outR.width - 18f, mainViewH);
            var visible = new Rect(0, scrollMain.y, view.width, outR.height);
            Widgets.BeginScrollView(outR, ref scrollMain, view);
            float h = 0;
            try
            {
                if (searchable && !string.IsNullOrEmpty(search)) h = DrawSearch(view.width, target, visible);
                else switch (pageId)
                {
                    case "modules": h = DrawModules(view.width, target, visible); break;
                    case "difficulty": h = DifficultyPanel.Draw(0f, 0f, view.width, target, visible); break;
                    case "diagnosis": h = DiagnosisPage.Draw(view.width, visible); break;
                    case "about": h = DrawAbout(view.width); break;
                    default: h = DrawModulePage(page.Module ?? ModuleId.Core, view.width, target, visible); break;
                }
            }
            catch (Exception e) { HLog.Once("settingsPage:" + pageId + e.GetType().Name, LogLevel.Error, "UI", "Settings page " + pageId + " failed: " + e); h = 40f; }
            mainViewH = Mathf.Max(h + 20f, 40f);
            Widgets.EndScrollView();
        }

        // ---------------------------------------------------------------------------------------------------------
        static bool Matches(HSetting s)
        {
            if (!ShowNerd && s.Nerd) return false;
            if (string.IsNullOrEmpty(search)) return true;
            string q = search.ToLowerInvariant();
            return s.Label.ToLowerInvariant().Contains(q) || s.Key.ToLowerInvariant().Contains(q) || (s.Desc ?? "").ToLowerInvariant().Contains(q) || s.Category.ToLowerInvariant().Contains(q);
        }

        static float DrawSearch(float w, HearthProfile target, Rect visible)
        {
            float y = 0; int n = 0;
            foreach (var g in HSettings.All.Where(s => Modules.Available(s.Module) && Matches(s)).GroupBy(s => s.Module).OrderBy(g => g.Key))
            {
                y += UiStyle.Section(0, y, w, (g.Key == ModuleId.Core ? "HH.UI.Page.General".Translate().ToString() : Modules.Label(g.Key)));
                foreach (var s in g) { y += SettingRowUI.Draw(0, y, w, s, target, visible); n++; }
                y += 8f;
            }
            if (n == 0) UiStyle.LabelColored(new Rect(0, 0, w, 30f), "HH.UI.Settings.NoMatch".Translate(), UiStyle.Grey);
            return Mathf.Max(y, 36f);
        }

        static float DrawModulePage(ModuleId m, float w, HearthProfile target, Rect visible)
        {
            float y = 0;
            if (m != ModuleId.Core)
            {
                UiStyle.LabelColored(new Rect(0, y, w - 150f, 36f), ("HH.UI.ModuleDesc." + m).Translate(), UiStyle.Grey);
                if (Widgets.ButtonText(new Rect(w - 140f, y, 140f, 26f), "HH.UI.Settings.ResetModule".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Settings.ResetModuleConfirm".Translate(Modules.Label(m)), () => ResetModule(m, target), true));
                y += 40f;
                if (!target.ModuleOn(m))
                {
                    var b = new Rect(0, y, w, 34f);
                    Widgets.DrawBoxSolid(b, UiStyle.AmberSoft);
                    UiStyle.LabelColored(new Rect(b.x + 8f, b.y, b.width - 140f, b.height), "HH.UI.Settings.ModuleIsOff".Translate(), UiStyle.Amber, TextAnchor.MiddleLeft);
                    if (Widgets.ButtonText(new Rect(b.xMax - 128f, b.y + 3f, 120f, 28f), "HH.UI.Settings.SwitchOn".Translate())) target.SetModule(m, true);
                    y += 42f;
                }
            }
            else y = DrawGeneral(w, target, y);
            string last = null;
            foreach (var s in HSettings.All.Where(s => s.Module == m && Matches(s) && !(m == ModuleId.Core && s.Category == "Special switches")))
            {
                if (s.Category != last) { y += 6f; y += UiStyle.Section(0, y, w, s.Category); last = s.Category; }
                y += SettingRowUI.Draw(0, y, w, s, target, visible);
            }
            return y;
        }

        public static void ResetModule(ModuleId m, HearthProfile target)
        {
            foreach (var s in HSettings.All.Where(s => s.Module == m)) SettingRowUI.Reset(s, target);
            HLog.Info("UI", "Settings of module " + m + " reset to defaults");
        }

        /// <summary>The general (Core) page: interface preferences that are not numeric settings.</summary>
        static float DrawGeneral(float w, HearthProfile target, float y)
        {
            y += UiStyle.Section(0, y, w, "HH.UI.General.Interface".Translate());
            var tpl = HearthMod.Template;
            bool card = tpl.ShowFirstStartCard;
            Widgets.CheckboxLabeled(new Rect(0, y, Mathf.Min(w, 520f), 26f), "HH.UI.General.ShowCard".Translate(), ref card);
            TooltipHandler.TipRegion(new Rect(0, y, Mathf.Min(w, 520f), 26f), "HH.UI.General.ShowCardTip".Translate());
            tpl.ShowFirstStartCard = card; y += 30f;
            // tutorial level
            UiStyle.LabelColored(new Rect(0, y, 180f, 28f), "HH.UI.General.Tutorial".Translate(), Color.white, TextAnchor.MiddleLeft);
            bool inGameSave = HearthProfile.Current != null && !editTemplate;
            var cur = inGameSave ? UiState.EffectiveTutorial() : tpl.Tutorial;
            var modes = new[] { TutorialMode.Off, TutorialMode.Short, TutorialMode.FullFirstTime, TutorialMode.AlwaysFull };
            float cx = 190f; float cwid = Mathf.Min(150f, (w - 200f) / 4f);
            foreach (var m in modes)
            {
                bool sel = inGameSave ? (m == TutorialMode.FullFirstTime ? cur == TutorialMode.FullFirstTime : cur == m) : tpl.Tutorial == m;
                string lab = inGameSave && m == TutorialMode.FullFirstTime ? "HH.UI.Tutorial.Full".Translate().ToString() : UiState.TutorialLabel(m);
                if (UiStyle.Chip(new Rect(cx, y, cwid, 28f), lab, sel, ("HH.UI.Tutorial." + m + ".Tip").Translate()))
                {
                    if (inGameSave) UiState.SetValue("tutorial", (int)m);
                    else tpl.Tutorial = m;
                }
                cx += cwid + 4f;
            }
            y += 36f;
            bool verbose = tpl.VerboseLog;
            Widgets.CheckboxLabeled(new Rect(0, y, Mathf.Min(w, 520f), 26f), "HH.UI.General.Verbose".Translate(), ref verbose);
            tpl.VerboseLog = verbose; HLog.Verbose = verbose; y += 30f;
            y += 8f;
            return y;
        }

        static float DrawModules(float w, HearthProfile target, Rect visible)
        {
            float y = 0;
            UiStyle.LabelColored(new Rect(0, y, w, 40f), target == HearthMod.Template.Profile ? "HH.UI.Modules.IntroTemplate".Translate() : "HH.UI.Modules.IntroSave".Translate(), UiStyle.Grey);
            y += 44f;
            y += UiStyle.Section(0, y, w, "HH.UI.Page.Modules".Translate());
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core))
            {
                bool avail = Modules.Available(m);
                float h = 40f;
                var row = new Rect(0, y, w, h);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                if (m == ModuleId.Underworld) DrawUnderworld(row, target, avail);
                else
                {
                    bool on = target.ModuleOn(m), nb = on;
                    Widgets.Checkbox(new Vector2(row.x + 4f, row.y + 8f), ref nb, 24f, !avail);
                    if (nb != on) target.SetModule(m, nb);
                    UiStyle.LabelColored(new Rect(row.x + 36f, row.y, 200f, h), Modules.Label(m), avail ? Color.white : UiStyle.DimGrey, TextAnchor.MiddleLeft);
                    string desc = avail ? ("HH.UI.ModuleDesc." + m).Translate().ToString() : "HH.UI.Modules.NotInstalled".Translate().ToString();
                    UiStyle.LabelColored(new Rect(row.x + 240f, row.y, row.width - 250f, h), desc, UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    if (avail && Guard.IsPaused(m)) UiStyle.LabelColored(new Rect(row.xMax - 130f, row.y, 126f, 16f), "HH.UI.Diag.Paused".Translate(), UiStyle.Bad, TextAnchor.MiddleRight, GameFont.Tiny);
                }
                y += h + 2f;
            }
            y += 4f;
            UiStyle.LabelColored(new Rect(0, y, w, 30f), "HH.UI.Modules.BankHint".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += 34f;

            y += UiStyle.Section(0, y, w, "HH.UI.Modules.Content".Translate());
            UiStyle.LabelColored(new Rect(0, y, w, 24f), "HH.UI.Modules.ContentDesc".Translate(), UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += 24f;
            float cw = Mathf.Min(160f, (w - 18f) / 4f);
            int i = 0;
            foreach (ContentIntensity c in Enum.GetValues(typeof(ContentIntensity)))
            {
                if (UiStyle.Chip(new Rect(i * (cw + 6f), y, cw, 30f), ("HH.UI.Content." + c).Translate(), target.Content == c, ("HH.UI.Content." + c + ".Tip").Translate())) target.Content = c;
                i++;
            }
            y += 40f;
            return y;
        }

        static void DrawUnderworld(Rect row, HearthProfile target, bool avail)
        {
            UiStyle.LabelColored(new Rect(row.x + 36f, row.y, 200f, row.height), Modules.Label(ModuleId.Underworld), avail ? Color.white : UiStyle.DimGrey, TextAnchor.MiddleLeft);
            bool on = target.ModuleOn(ModuleId.Underworld);
            int state = !on ? 2 : (target.UnderworldDiscovered ? 1 : 0); // 0 discover, 1 on, 2 off
            float x = row.x + 240f, cw = Mathf.Min(120f, (row.width - 250f) / 3f);
            string[] names = { "HH.UI.Underworld.Discover", "HH.UI.Underworld.On", "HH.UI.Underworld.Off" };
            for (int i = 0; i < 3; i++)
            {
                if (UiStyle.Chip(new Rect(x + i * (cw + 4f), row.y + 6f, cw, 28f), names[i].Translate(), state == i, (names[i] + ".Tip").Translate(), avail))
                {
                    target.SetModule(ModuleId.Underworld, i != 2);
                    target.UnderworldDiscovered = i == 1;
                }
            }
        }

        static float DrawAbout(float w)
        {
            float y = 0;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, w, 34f), "Hearth & Harbor"); Text.Font = GameFont.Small; y += 36f;
            UiStyle.LabelColored(new Rect(0, y, w, 24f), HearthMod.Version + "  ·  RimWorld " + VersionControl.CurrentVersionString, UiStyle.Grey); y += 30f;
            string body = "HH.UI.About.Body".Translate();
            float h = UiStyle.CachedHeight(body, Mathf.Min(w, 700f), GameFont.Small);
            Widgets.Label(new Rect(0, y, Mathf.Min(w, 700f), h), body); y += h + 14f;
            y += UiStyle.Section(0, y, w, "HH.UI.About.Parts".Translate());
            string parts = "HH.UI.About.PartsBody".Translate(Modules.Available(ModuleId.Bank) ? "HH.UI.Settings.Yes".Translate() : "HH.UI.Settings.No".Translate(), Modules.Available(ModuleId.Inn) ? "HH.UI.Settings.Yes".Translate() : "HH.UI.Settings.No".Translate());
            float ph = UiStyle.CachedHeight(parts, Mathf.Min(w, 700f), GameFont.Small);
            Widgets.Label(new Rect(0, y, Mathf.Min(w, 700f), ph), parts); y += ph + 14f;
            if (Widgets.ButtonText(new Rect(0, y, 220f, 30f), "HH.UI.About.Project".Translate())) Application.OpenURL("https://github.com/jb-rimworld/hearth-harbor");
            y += 40f;
            UiStyle.LabelColored(new Rect(0, y, w, 24f), "HH.UI.About.License".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            return y + 30f;
        }
    }
}
