// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Testing;
using Hearth.Compat.Hospitality;

namespace Hearth.Shell.Tests
{
    /// <summary>
    /// F35 window pass: every special window of the shell (main tab, first-start card, difficulty dialog, text report, Hospitality takeover,
    /// mod settings, guide card) is sized at 1280x720, 1920x1080 and 2560x1440 (HUI.TestScreen pretends the screen), must lie completely inside that
    /// screen and be larger than before on big screens. Windows that fit the real screen are opened and drawn as well; with HH_UI_SHOTS set
    /// they leave screenshots (ui_layout_&lt;window&gt;_&lt;size&gt;.png) to look at.
    /// </summary>
    public sealed class LayoutSuite : HearthTestSuite
    {
        public override string Name => "ui.layout";
        public override int MaxTicks => 9000;

        static bool InsideScreen(Vector2 size, Vector2 scr, float reserveBottom = 0f)
        {
            var r = new Rect((scr.x - size.x) / 2f, (scr.y - size.y) / 2f, size.x, size.y);
            return r.x >= -0.5f && r.y >= -0.5f && r.xMax <= scr.x + 0.5f && r.yMax <= scr.y - reserveBottom + 0.5f;
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            int errors0 = HLog.ErrorCount;
            var screens = new[] { new Vector2(1280f, 720f), new Vector2(1920f, 1080f), new Vector2(2560f, 1440f) };
            var realScreen = new Vector2(UI.screenWidth, UI.screenHeight);
            t.Note("real screen " + realScreen.x + "x" + realScreen.y + ", window scale setting " + HSettings.Get(HUI.WindowScaleKey).ToString("0.00"));
            var prof = HearthProfile.Current;
            bool cardDone = prof.FirstStartCardDone;
            try
            {
                foreach (var scr in screens)
                {
                    HUI.TestScreen = scr;
                    string tag = (int)scr.x + "x" + (int)scr.y;
                    bool big = scr.y >= 1000f;

                    // ---- 1. the sizes themselves -------------------------------------------------------------------------------------------
                    var tabSize = new MainTabWindow_HH().RequestedTabSize;
                    t.Check(InsideScreen(tabSize, scr, 35f), tag + ": main tab inside the screen (" + tabSize + ")");
                    var settingsSize = new Dialog_ModSettings(HearthMod.Instance).InitialSize;
                    t.Check(InsideScreen(settingsSize, scr), tag + ": mod settings inside the screen (" + settingsSize + ")");
                    var card = new Dialog_FirstStartCard(true); var cardSize = card.InitialSize;
                    t.Check(InsideScreen(cardSize, scr), tag + ": first-start card inside the screen (" + cardSize + ")");
                    var diff = new Dialog_HostingDifficulty(prof); var diffSize = diff.InitialSize;
                    t.Check(InsideScreen(diffSize, scr), tag + ": difficulty dialog inside the screen (" + diffSize + ")");
                    var rep = new Dialog_TextReport("Report", "x"); var repSize = rep.InitialSize;
                    t.Check(InsideScreen(repSize, scr), tag + ": text report inside the screen (" + repSize + ")");
                    var take = new Dialog_HospitalityTakeover(); var takeSize = take.InitialSize;
                    t.Check(InsideScreen(takeSize, scr), tag + ": Hospitality takeover inside the screen (" + takeSize + ")");
                    t.Check(GuidePanel.W >= 260f && GuidePanel.W <= scr.x * 0.3f, tag + ": guide card width " + GuidePanel.W.ToString("0") + " is sensible");
                    t.Check(settingsSize.x >= Mathf.Min(900f, scr.x - 20f) && settingsSize.y >= Mathf.Min(600f, scr.y - 55f), tag + ": settings window keeps its minimum");
                    if (big)
                    {
                        t.Check(tabSize.x > 1000f && tabSize.y > 640f, tag + ": main tab is larger than the old 1000x640");
                        t.Check(cardSize.x > 860f && cardSize.y > 660f, tag + ": first-start card is larger than the old 860x660");
                        t.Check(diffSize.x > 900f && diffSize.y > 760f, tag + ": difficulty dialog is larger than the old 900x760");
                        t.Check(repSize.x > 640f && repSize.y > 560f, tag + ": text report is larger than the old 640x560");
                        t.Check(takeSize.x > 640f && takeSize.y > 560f, tag + ": takeover is larger than the old 640x560");
                        t.Check(settingsSize.x > 900f && settingsSize.y > 700f, tag + ": settings window is larger than the vanilla 900x700");
                    }
                    t.Check(card.resizeable == false && diff.resizeable && rep.resizeable, tag + ": difficulty dialog and text report are resizeable");
                    var sdlg = new Dialog_ModSettings(HearthMod.Instance);
                    t.Check(sdlg.resizeable, tag + ": the settings window of our mod is resizeable");
                    var other = new Dialog_ModSettings(null);
                    t.Check(!other.resizeable && other.InitialSize == new Vector2(900f, 700f), tag + ": other mods' settings windows stay vanilla");

                    // ---- 2. the main tab's inner layout stays inside the window --------------------------------------------------------------
                    var inRect = new Rect(0f, 0f, tabSize.x - 2f * 18f, tabSize.y - 2f * 18f);
                    MainTabWindow_HH.Layout(inRect, out var head, out var list, out var content);
                    bool inside = head.xMin >= 0 && list.xMin >= 0 && content.xMax <= inRect.xMax + 0.5f && content.yMax <= inRect.yMax + 0.5f && list.yMax <= inRect.yMax + 0.5f && list.xMax < content.xMin;
                    t.Check(inside && content.width >= 600f && list.width >= 170f, tag + ": tab header, page list and page area fit (page " + content.width.ToString("0") + "x" + content.height.ToString("0") + ")");

                    // ---- 3. open and draw what fits the real screen --------------------------------------------------------------------------
                    if (scr.x > realScreen.x || scr.y > realScreen.y) { t.Note(tag + ": larger than the real screen, windows are not opened"); continue; }
                    var wsRect = new Rect(0f, 0f, realScreen.x, realScreen.y);
                    var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
                    Find.MainTabsRoot.SetCurrentTab(tabDef, false);
                    yield return 6;
                    var tab = Find.WindowStack.WindowOfType<MainTabWindow_HH>();
                    t.Check(tab != null, tag + ": main tab opened");
                    if (tab != null)
                        foreach (var pg in MainTabWindow_HH.Pages())
                        {
                            MainTabWindow_HH.SelectedPage = pg.Id;
                            yield return 20;
                            if (pg.Id == "overview" || pg.Id == "guests") { UiTestUtil.Shot("ui_layout_tab_" + pg.Id + "_" + tag); yield return 20; }
                            t.Check(tab.windowRect.x >= -0.5f && tab.windowRect.y >= -0.5f && tab.windowRect.xMax <= wsRect.xMax + 0.5f && tab.windowRect.yMax <= wsRect.yMax + 0.5f, tag + ": tab window rect inside the real screen on page " + pg.Id + " (" + tab.windowRect + ")");
                        }
                    Find.MainTabsRoot.EscapeCurrentTab(false);
                    yield return 3;

                    var windows = new List<(string name, Window w)>
                    {
                        ("card", UiTestUtil.NoPause(new Dialog_FirstStartCard(true))),
                        ("difficulty", UiTestUtil.NoPause(new Dialog_HostingDifficulty(prof))),
                        ("report", UiTestUtil.NoPause(new Dialog_TextReport("Report", string.Join("\n", Enumerable.Range(1, 80).Select(i => "line " + i + ": the quick brown fox jumps over the lazy dog, again and again, so that the line is long enough to wrap in a narrow window.")))) ),
                        ("takeover", UiTestUtil.NoPause(new Dialog_HospitalityTakeover())),
                        ("settings", UiTestUtil.NoPause(new Dialog_ModSettings(HearthMod.Instance))),
                    };
                    foreach (var (name, w) in windows)
                    {
                        Find.WindowStack.Add(w);
                        yield return 25;
                        UiTestUtil.Shot("ui_layout_" + name + "_" + tag); yield return 20;
                        t.Check(w.IsOpen, tag + ": " + name + " stays open while drawing");
                        var wr = w.windowRect;
                        t.Check(wr.x >= -0.5f && wr.y >= -0.5f && wr.xMax <= wsRect.xMax + 0.5f && wr.yMax <= wsRect.yMax + 0.5f && wr.width <= scr.x + 0.5f && wr.height <= scr.y + 0.5f, tag + ": " + name + " window rect " + wr + " inside the screen");
                        if (name == "difficulty") t.Check(DifficultyPanel.Anchors.Count >= 3, tag + ": difficulty dialog collected jump anchors (" + DifficultyPanel.Anchors.Count + ")");
                        w.Close(false);
                        yield return 2;
                    }
                    prof.FirstStartCardDone = cardDone;
                }
            }
            finally { HUI.TestScreen = null; prof.FirstStartCardDone = cardDone; }
            t.Check(HLog.ErrorCount == errors0, "no errors while sizing and drawing the windows (" + (HLog.ErrorCount - errors0) + ")");
        }
    }
}
#endif
