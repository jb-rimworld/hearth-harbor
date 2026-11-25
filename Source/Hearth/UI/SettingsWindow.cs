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
        public sealed class PageEntry { public string Id; public string Label; public ModuleId? Module; public bool Dim; public Texture2D Icon => HHIcons.Page(Id); }

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

        public static HearthProfile Target => scenarioTarget ?? (editTemplate || HearthProfile.Current == null ? HearthMod.Template.Profile : HearthProfile.Current);

        /// <summary>F08: while the scenario editor's "More settings…" window draws, every page edits this scratch profile instead of the colony or
        /// the template, and the template/colony switches, interface preferences, diagnosis and about are hidden.</summary>
        static HearthProfile scenarioTarget;
        public static bool ScenarioMode => scenarioTarget != null;

        /// <summary>Draws the whole window against <paramref name="scratch"/> (scenario rules part).</summary>
        public static void DrawForScenario(Rect inRect, HearthProfile scratch)
        {
            var prev = scenarioTarget; scenarioTarget = scratch;
            try { Draw(inRect); } finally { scenarioTarget = prev; }
        }

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
            if (ScenarioMode) return list;
            list.Add(new PageEntry { Id = "diagnosis", Label = "HH.UI.Page.Diagnosis".Translate() });
            list.Add(new PageEntry { Id = "about", Label = "HH.UI.Page.About".Translate() });
            return list;
        }

        public static void Install()
        {
            HearthMod.SettingsWindowOverride = Draw;
            HearthMod.SettingsPageSelector = id => { pageId = id; search = ""; scrollMain = Vector2.zero; mainViewH = 1000f; if (HearthProfile.Current != null) editTemplate = false; };
        }

        public static void Draw(Rect inRect)
        {
            Guard.Run(ModuleId.Core, "UI.SettingsWindow", () => DrawInner(inRect));
        }

        /// <summary>Width of the page list: a fifth of the window, 200 to 260 px.</summary>
        public static float SideWidth(float windowW) => Mathf.Clamp(windowW * 0.2f, 200f, 260f);

        static void DrawInner(Rect inRect)
        {
            UiProbe.Hit("settings");
            bool inGame = HearthProfile.Current != null && !ScenarioMode;
            if (HearthProfile.Current == null) editTemplate = true;
            var pages = BuildPages();
            if (pages.All(p => p.Id != pageId)) pageId = pages[0].Id;

            var side = new Rect(inRect.x, inRect.y, SideWidth(inRect.width), inRect.height);
            var main = new Rect(side.xMax + 12f, inRect.y, inRect.width - side.width - 12f, inRect.height);
            DrawSidebar(side, pages, inGame);
            Widgets.DrawBoxSolid(new Rect(side.xMax + 5f, inRect.y, 1f, inRect.height), UiStyle.Line);
            DrawMain(main, pages, inGame);
        }

        // ---------------------------------------------------------------------------------------------------------
        static void DrawSidebar(Rect r, List<PageEntry> pages, bool inGame)
        {
            float y = r.y;
            // F34: the mod mark heads the sidebar
            var mark = HHIcons.Mark;
            if (mark != null)
            {
                HHIcons.Draw(new Rect(r.x, y, 32f, 32f), mark);
                UiStyle.LabelColored(new Rect(r.x + 38f, y, r.width - 38f, 32f), ScenarioMode ? "HH.Scen.Editor.Side".TranslateOr("Scenario rules") : "Hearth & Harbor", UiStyle.Amber, TextAnchor.MiddleLeft);
                y += 38f;
            }
            if (ScenarioMode)
            {
                string note = "HH.Scen.Editor.Note".TranslateOr("Only what you change here (marked amber) is set by the scenario. Unmarked rows show the default values; in a new game they come from the player's own template. Reset hands a row or module back to the template.");
                float nh = UiStyle.CachedHeight(note, r.width, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(r.x, y, r.width, nh), note, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                y += nh + 6f;
            }
            else if (inGame)
            {
                float hw = (r.width - 6f) / 2f;
                if (UiStyle.Chip(new Rect(r.x, y, hw, 32f), "HH.UI.Settings.ThisSave".Translate(), !editTemplate, "HH.UI.Settings.ThisSaveTip".Translate())) editTemplate = false;
                if (UiStyle.Chip(new Rect(r.x + hw + 6f, y, hw, 32f), "HH.UI.Settings.Template".Translate(), editTemplate, "HH.UI.Settings.TemplateTip".Translate())) editTemplate = true;
                y += 40f;
            }
            else
            {
                string tonly = "HH.UI.Settings.TemplateOnly".Translate();
                float toh = HUI.TextH(tonly, r.width, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(r.x, y, r.width, toh), tonly, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                y += toh + 6f;
            }
            float bottomH = inGame ? 76f : 0f;
            var listR = new Rect(r.x, y, r.width, r.height - (y - r.y) - bottomH);
            const float pageRowH = 34f;
            float contentH = pages.Count * (pageRowH + 2f);
            var view = new Rect(0, 0, listR.width - (contentH > listR.height ? 16f : 0f), contentH);
            Widgets.BeginScrollView(listR, ref scrollSide, view);
            float py = 0;
            foreach (var p in pages)
            {
                var row = new Rect(0, py, view.width, pageRowH);
                bool sel = p.Id == pageId;
                if (sel) { Widgets.DrawBoxSolid(row, UiStyle.AmberSoft); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), UiStyle.Amber); }
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                var icon = p.Icon;
                float lx = row.x + 10f;
                if (icon != null) { HHIcons.Draw(new Rect(row.x + 8f, row.y + 5f, 24f, 24f), icon, p.Dim); lx = row.x + 38f; }
                string plabel = p.Label + (p.Dim ? "  (" + "HH.UI.Settings.Off".Translate() + ")" : "");
                var plr = new Rect(lx, row.y, row.xMax - lx - 2f, row.height);
                UiStyle.LabelColored(plr, plabel.Truncate(plr.width), p.Dim ? UiStyle.DimGrey : Color.white, TextAnchor.MiddleLeft);
                if (HUI.TextW(plabel) > plr.width) TooltipHandler.TipRegion(row, plabel);
                if (Widgets.ButtonInvisible(row)) { pageId = p.Id; scrollMain = Vector2.zero; mainViewH = 1000f; }
                py += pageRowH + 2f;
            }
            Widgets.EndScrollView();

            if (inGame)
            {
                float by = r.yMax - bottomH + 6f;
                if (Widgets.ButtonText(new Rect(r.x, by, r.width, 32f), "HH.UI.Settings.SaveAsTemplate".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Settings.SaveAsTemplateConfirm".Translate(), SaveColonyAsTemplate));
                TooltipHandler.TipRegion(new Rect(r.x, by, r.width, 32f), "HH.UI.Settings.SaveAsTemplateTip".Translate());
                if (Widgets.ButtonText(new Rect(r.x, by + 36f, r.width, 32f), "HH.UI.Settings.ApplyTemplate".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Settings.ApplyTemplateConfirm".Translate(), ApplyTemplateToColony, true));
                TooltipHandler.TipRegion(new Rect(r.x, by + 36f, r.width, 32f), "HH.UI.Settings.ApplyTemplateTip".Translate());
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
            cur.Tier = t.Tier; cur.Content = t.Content;
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
            float tx = r.x;
            var picon = string.IsNullOrEmpty(search) ? page.Icon : null;
            if (picon != null) { HHIcons.Draw(new Rect(r.x, r.y + 3f, 28f, 28f), picon); tx += 36f; }
            bool searchable = pageId != "diagnosis" && pageId != "about";
            float rightW = 0f;
            Rect sr = Rect.zero, nr = Rect.zero;
            if (searchable)
            {
                float sw = Mathf.Clamp(r.width * 0.26f, 200f, 300f);
                sr = new Rect(r.xMax - sw, r.y + 2f, sw, 30f);
                string nerdLabel = "HH.UI.Settings.Nerd".Translate();
                float nw = Mathf.Clamp(HUI.TextW(nerdLabel) + 40f, 150f, 260f);
                nr = new Rect(sr.x - nw - 12f - (search.NullOrEmpty() ? 0f : 34f), r.y + 4f, nw, 26f);
                rightW = r.xMax - nr.x + 12f;
            }
            string headTitle = string.IsNullOrEmpty(search) ? page.Label : "HH.UI.Settings.SearchResults".Translate().ToString();
            Text.Font = GameFont.Medium;
            float titleW = Mathf.Max(120f, r.width - rightW - (tx - r.x));
            var tr = new Rect(tx, r.y, titleW, 34f);
            Widgets.Label(tr, headTitle.Truncate(titleW));
            Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(new Rect(r.x, r.y + 35f, Mathf.Min(r.width, 180f), 2f), UiStyle.Amber);
            if (searchable)
            {
                search = Widgets.TextField(sr, search);
                if (search.NullOrEmpty()) UiStyle.LabelColored(new Rect(sr.x + 6f, sr.y, sr.width - 8f, sr.height), "HH.UI.Settings.Search".Translate(), UiStyle.DimGrey, TextAnchor.MiddleLeft);
                else if (Widgets.ButtonText(new Rect(sr.x - 32f, sr.y, 28f, 30f), "x")) search = "";
                bool nerd = HearthMod.Template.ShowNerd;
                Widgets.CheckboxLabeled(nr, "HH.UI.Settings.Nerd".Translate(), ref nerd, false, null, null, true);
                TooltipHandler.TipRegion(nr, "HH.UI.Settings.NerdTip".Translate());
                HearthMod.Template.ShowNerd = nerd;
            }
            float top = r.y + 44f;
            if (ScenarioMode)
            { UiStyle.LabelColored(new Rect(r.x, top, r.width, 22f), "HH.Scen.Editor.Editing".TranslateOr("Editing the Hearth & Harbor rules of this scenario."), UiStyle.Amber, TextAnchor.UpperLeft, GameFont.Tiny); top += 26f; }
            else if (inGame && editTemplate)
            { UiStyle.LabelColored(new Rect(r.x, top, r.width, 22f), "HH.UI.Settings.EditingTemplate".Translate(), UiStyle.Amber, TextAnchor.UpperLeft, GameFont.Tiny); top += 26f; }
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
            if (ScenarioMode && s.Global) return false;   // preferences of the player, not of a colony: a scenario never sets them
            if (string.IsNullOrEmpty(search)) return true;
            string q = search.ToLowerInvariant();
            return s.Label.ToLowerInvariant().Contains(q) || s.Key.ToLowerInvariant().Contains(q) || (s.Desc ?? "").ToLowerInvariant().Contains(q) || s.Category.ToLowerInvariant().Contains(q) || s.CategoryLabel.ToLowerInvariant().Contains(q);
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
                string rl = "HH.UI.Settings.ResetModule".Translate();
                float rbw = Mathf.Clamp(HUI.TextW(rl) + 32f, 140f, 240f);
                string mdesc = ("HH.UI.ModuleDesc." + m).Translate();
                float mdh = Mathf.Max(36f, HUI.TextH(mdesc, w - rbw - 16f));
                UiStyle.LabelColored(new Rect(0, y, w - rbw - 12f, mdh), mdesc, UiStyle.Grey);
                if (Widgets.ButtonText(new Rect(w - rbw, y, rbw, 30f), rl))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("HH.UI.Settings.ResetModuleConfirm".Translate(Modules.Label(m)), () => ResetModule(m, target), true));
                y += mdh + 6f;
                if (!target.ModuleOn(m))
                {
                    string off = "HH.UI.Settings.ModuleIsOff".Translate();
                    float offH = Mathf.Max(38f, HUI.TextH(off, w - 170f) + 8f);
                    var b = new Rect(0, y, w, offH);
                    Widgets.DrawBoxSolid(b, UiStyle.AmberSoft);
                    UiStyle.LabelColored(new Rect(b.x + 10f, b.y, b.width - 170f, b.height), off, UiStyle.Amber, TextAnchor.MiddleLeft);
                    if (Widgets.ButtonText(new Rect(b.xMax - 150f, b.y + (offH - 30f) / 2f, 140f, 30f), "HH.UI.Settings.SwitchOn".Translate())) target.SetModule(m, true);
                    y += offH + 8f;
                }
                if (m == ModuleId.AiFlavour) y += Hearth.Ai.AiSettingsPanel.Draw(y, w, target);   // F05: consent, relay state, counters
            }
            else if (!ScenarioMode) y = DrawGeneral(w, target, y);
            string last = null;
            foreach (var s in HSettings.All.Where(s => s.Module == m && Matches(s) && !(m == ModuleId.Core && s.Category == "Special switches")))
            {
                if (s.Category != last) { y += 6f; y += UiStyle.Section(0, y, w, s.CategoryLabel); last = s.Category; }
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
            Widgets.CheckboxLabeled(new Rect(0, y, Mathf.Min(w, 520f), 26f), "HH.UI.General.ShowCard".Translate(), ref card, false, null, null, true);
            TooltipHandler.TipRegion(new Rect(0, y, Mathf.Min(w, 520f), 26f), "HH.UI.General.ShowCardTip".Translate());
            tpl.ShowFirstStartCard = card; y += 30f;
            // tutorial level
            UiStyle.LabelColored(new Rect(0, y, 180f, 32f), "HH.UI.General.Tutorial".Translate(), Color.white, TextAnchor.MiddleLeft);
            bool inGameSave = HearthProfile.Current != null && !editTemplate;
            var cur = inGameSave ? UiState.EffectiveTutorial() : tpl.Tutorial;
            var modes = new[] { TutorialMode.Off, TutorialMode.Short, TutorialMode.FullFirstTime, TutorialMode.AlwaysFull };
            float cx = 190f; float cwid = Mathf.Min(190f, (w - 200f) / 4f);
            foreach (var m in modes)
            {
                bool sel = inGameSave ? (m == TutorialMode.FullFirstTime ? cur == TutorialMode.FullFirstTime : cur == m) : tpl.Tutorial == m;
                string lab = inGameSave && m == TutorialMode.FullFirstTime ? "HH.UI.Tutorial.Full".Translate().ToString() : UiState.TutorialLabel(m);
                if (UiStyle.Chip(new Rect(cx, y, cwid, 32f), lab, sel, ("HH.UI.Tutorial." + m + ".Tip").Translate()))
                {
                    if (inGameSave) UiState.SetValue("tutorial", (int)m);
                    else tpl.Tutorial = m;
                }
                cx += cwid + 4f;
            }
            y += 42f;
            bool verbose = tpl.VerboseLog;
            Widgets.CheckboxLabeled(new Rect(0, y, Mathf.Min(w, 520f), 26f), "HH.UI.General.Verbose".Translate(), ref verbose, false, null, null, true);
            tpl.VerboseLog = verbose; HLog.Verbose = verbose; y += 30f;
            y += 8f;
            return y;
        }

        static float DrawModules(float w, HearthProfile target, Rect visible)
        {
            float y = 0;
            string intro = ScenarioMode ? "HH.Scen.Editor.ModulesIntro".TranslateOr("Modules this scenario switches on or off. A module you do not touch follows the player's template.") : target == HearthMod.Template.Profile ? "HH.UI.Modules.IntroTemplate".Translate() : "HH.UI.Modules.IntroSave".Translate();
            float ih = Mathf.Max(24f, HUI.TextH(intro, w));
            UiStyle.LabelColored(new Rect(0, y, w, ih), intro, UiStyle.Grey);
            y += ih + 8f;
            y += UiStyle.Section(0, y, w, "HH.UI.Page.Modules".Translate());
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core))
            {
                bool avail = Modules.Available(m);
                float lw = ModLabelW(w);
                string descText = avail ? ("HH.UI.ModuleDesc." + m).Translate().ToString() : "HH.UI.Modules.NotInstalled".Translate().ToString();
                float descW = w - lw - 70f - (ScenarioMode ? ScenResetW : 0f) - 12f;
                float h = Mathf.Max(44f, HUI.TextH(descText, descW, GameFont.Tiny) + 12f);
                var row = new Rect(0, y, w, h);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                {
                    bool on = target.ModuleOn(m), nb = on;
                    Widgets.Checkbox(new Vector2(row.x + 4f, row.y + (h - 24f) / 2f), ref nb, 24f, !avail);
                    if (nb != on) target.SetModule(m, nb);
                    HHIcons.Draw(new Rect(row.x + 34f, row.y + (h - 24f) / 2f, 24f, 24f), HHIcons.Module(m), !avail || !nb);
                    UiStyle.LabelColored(new Rect(row.x + 66f, row.y, lw, h), Modules.Label(m), avail ? Color.white : UiStyle.DimGrey, TextAnchor.MiddleLeft);
                    UiStyle.LabelColored(new Rect(row.x + 70f + lw, row.y, descW, h), descText, UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    if (avail && Guard.IsPaused(m)) UiStyle.LabelColored(new Rect(row.xMax - 130f - (ScenarioMode ? ScenResetW : 0f), row.y, 126f, 16f), "HH.UI.Diag.Paused".Translate(), UiStyle.Bad, TextAnchor.MiddleRight, GameFont.Tiny);
                }
                if (ScenarioMode && avail) DrawScenarioModuleReset(row, m, target);
                y += h + 2f;
            }
            y += 4f;
            string bankHint = "HH.UI.Modules.BankHint".Translate();
            float bhh = HUI.TextH(bankHint, w, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(0, y, w, bhh), bankHint, UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += bhh + 10f;

            y += UiStyle.Section(0, y, w, "HH.UI.Modules.Content".Translate());
            string contentDesc = "HH.UI.Modules.ContentDesc".Translate();
            float cdh = HUI.TextH(contentDesc, w, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(0, y, w, cdh), contentDesc, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += cdh + 6f;
            float cw = Mathf.Min(190f, (w - 18f) / 4f);
            int i = 0;
            foreach (ContentIntensity c in Enum.GetValues(typeof(ContentIntensity)))
            {
                if (UiStyle.Chip(new Rect(i * (cw + 6f), y, cw, 34f), ("HH.UI.Content." + c).Translate(), target.Content == c, ("HH.UI.Content." + c + ".Tip").Translate())) ChooseContent(target, c);
                i++;
            }
            y += 44f;
            return y;
        }

        /// <summary>The choice applies at once (1.2.0: no confirmation dialog; the chip tooltips say what Dark and Grim add).</summary>
        static void ChooseContent(HearthProfile target, ContentIntensity c)
        {
            if (target.Content == c) return;
            target.Content = c;
            HLog.Info("UI", "Content " + c + " chosen in settings");
        }

        const float ScenResetW = 56f;
        static float ModLabelW(float w) => Mathf.Clamp(w * 0.22f, 176f, 300f);

        /// <summary>F08 scenario editor: a module the scenario switches is marked amber and gets a Reset that hands it back to the player's template
        /// (without it, switching a module back to its old value would still be recorded as an explicit "on"/"off").</summary>
        static void DrawScenarioModuleReset(Rect row, ModuleId m, HearthProfile target)
        {
            if (!target.modules.ContainsKey(m)) return;
            Widgets.DrawBoxSolid(new Rect(row.x, row.y + 4f, 2f, row.height - 8f), UiStyle.Amber);
            var b = new Rect(row.xMax - ScenResetW + 4f, row.y + (row.height - 22f) / 2f, ScenResetW - 6f, 22f);
            TooltipHandler.TipRegion(b, "HH.Scen.Editor.ModuleResetTip".TranslateOr("Leave this module to the player's template."));
            if (Widgets.ButtonText(b, "HH.UI.Row.Reset".Translate())) target.modules.Remove(m);
        }

        static float DrawAbout(float w)
        {
            float y = 0;
            float ax = 0f;
            if (HHIcons.Mark != null) { HHIcons.Draw(new Rect(0f, y, 64f, 64f), HHIcons.Mark); ax = 76f; }
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(ax, y + (ax > 0 ? 6f : 0f), w - ax, 34f), "Hearth & Harbor"); Text.Font = GameFont.Small; y += ax > 0 ? 40f : 36f;
            UiStyle.LabelColored(new Rect(ax, y, w - ax, 24f), HearthMod.Version + "  ·  RimWorld " + VersionControl.CurrentVersionString, UiStyle.Grey); y += ax > 0 ? 34f : 30f;
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
