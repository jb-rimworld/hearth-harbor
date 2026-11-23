// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Silverline;

namespace Hearth
{
    /// <summary>One step of a guide topic. check = id of a registered check, "venue:&lt;VenueTypeDef&gt;", or "manual" (the player ticks it).</summary>
    public class GuideStep
    {
        public string id;
        public string label;
        public string description;
        public string check = "manual";
        /// <summary>"tab:&lt;pageId&gt;" opens that page of the main tab, "settings" the settings window.</summary>
        public string jump;
        public ModuleId module = ModuleId.Core;
    }

    /// <summary>A lesson of the Hearth &amp; Harbor Guide (4.10). Other mods add topics in XML: &lt;Hearth.GuideTopicDef&gt;.</summary>
    public class GuideTopicDef : Def
    {
        public ModuleId module = ModuleId.Core;
        public int order = 100;
        /// <summary>Check id that makes the topic appear (once true it stays). Empty = from the start.</summary>
        public string unlockedBy;
        public List<GuideStep> steps = new List<GuideStep>();
    }
}

namespace Hearth.Shell
{

    /// <summary>Named conditions read from game state. Modules may register more in OnDefsLoaded (GuideChecks.Register).</summary>
    public static class GuideChecks
    {
        static readonly Dictionary<string, Func<bool>> checks = new Dictionary<string, Func<bool>>();
        static bool builtIn;

        public static void Register(string id, Func<bool> f) { checks[id] = f; }

        static void EnsureBuiltIn()
        {
            if (builtIn) return; builtIn = true;
            R("always", () => true);
            R("guestBed", () => AnyHome(m => HearthUI.GuestBedProbe != null && HearthUI.GuestBedProbe(m)));
            R("venueSign", () => AnyHome(m => Venues.All(m).Count > 0));
            R("venueOpenNow", () => AnyHome(m => Venues.All(m).Any(v => v.IsOpenNow)));
            R("hosting", () => Eval("guestBed") || Eval("venueSign"));
            R("guestArrived", () => UiState.Flag("guestSeen"));
            R("firstPayment", () => UiState.Flag("paymentSeen"));
            R("bankAccount", () => BankApi.Ready);
            R("directDeposit", () => BankApi.Ready && BankApi.Impl.DirectDepositOn);
            R("seenGuestsPage", () => UiState.Flag("seenGuestsPage"));
            R("seenGuestTab", () => UiState.Flag("seenGuestTab"));
        }
        static void R(string id, Func<bool> f) { if (!checks.ContainsKey(id)) checks[id] = f; }

        static bool AnyHome(Func<Map, bool> f)
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            foreach (var m in Find.Maps) if (m.IsPlayerHome && f(m)) return true;
            return false;
        }

        public static bool Eval(string id)
        {
            if (string.IsNullOrEmpty(id)) return true;
            EnsureBuiltIn();
            if (id.StartsWith("not:")) return !Eval(id.Substring(4));
            if (id.StartsWith("venue:"))
            {
                string def = id.Substring(6);
                return AnyHome(m => Venues.All(m).Any(v => v.Type != null && v.Type.defName == def));
            }
            return checks.TryGetValue(id, out var f) && Guard.Run(ModuleId.Core, "Guide.Check." + id, f, false);
        }
    }

    /// <summary>Evaluated guide state. Updated every 60 ticks from game state; the completion tick of a topic is saved in the profile.</summary>
    public static class GuideState
    {
        public sealed class TopicView
        {
            public GuideTopicDef Def; public bool Unlocked, Done; public int DoneTick;
            public List<(GuideStep step, bool done)> Steps = new List<(GuideStep, bool)>();
            public int DoneCount => Steps.Count(s => s.done);
        }
        public static readonly List<TopicView> Views = new List<TopicView>();
        public const int DoneGraceTicks = 60000;

        public static string ManualKey(GuideTopicDef t, GuideStep s) => "step." + t.defName + "." + s.id;
        public static bool IsManualDone(GuideTopicDef t, GuideStep s) => UiState.Flag(ManualKey(t, s));

        /// <summary>Sticky facts that cannot be derived later (a guest who has left, the first payment …).</summary>
        public static void ObserveFacts()
        {
            if (Current.ProgramState != ProgramState.Playing || HearthProfile.Current == null) return;
            if (!UiState.Flag("guestSeen"))
                foreach (var m in Find.Maps)
                    if (m.IsPlayerHome && HearthUI.GuestsViaInn && GuestApi.GuestsOn(m).Any()) { UiState.SetFlag("guestSeen"); break; }
            if (!UiState.Flag("paymentSeen") && Ledger.Current != null)
            {
                foreach (var e in Ledger.Current.Journal)
                    if (e.To != null && e.To.StartsWith("colony:till")) { UiState.SetFlag("paymentSeen"); break; }
            }
        }

        public static void Update()
        {
            if (Current.ProgramState != ProgramState.Playing || HearthProfile.Current == null) return;
            ObserveFacts();
            int now = Find.TickManager.TicksGame;
            Views.Clear();
            foreach (var t in DefDatabase<GuideTopicDef>.AllDefs.OrderBy(d => d.order).ThenBy(d => d.defName))
            {
                if (!Modules.On(t.module)) continue;
                var v = new TopicView { Def = t };
                string openKey = "open." + t.defName, doneKey = "done." + t.defName;
                v.Unlocked = UiState.HasValue(openKey) || GuideChecks.Eval(t.unlockedBy);
                if (!v.Unlocked) { Views.Add(v); continue; }
                if (!UiState.HasValue(openKey)) UiState.SetValue(openKey, now);
                foreach (var s in t.steps)
                {
                    if (!Modules.On(s.module)) continue;
                    bool done = s.check == "manual" ? IsManualDone(t, s) : GuideChecks.Eval(s.check);
                    v.Steps.Add((s, done));
                }
                bool allDone = v.Steps.Count > 0 && v.Steps.All(x => x.done);
                if (UiState.HasValue(doneKey)) { v.Done = true; v.DoneTick = (int)UiState.Value(doneKey, now); }
                else if (allDone) { UiState.SetValue(doneKey, now); v.Done = true; v.DoneTick = now; }
                Views.Add(v);
            }
        }

        /// <summary>Topics shown expanded in the panel: unlocked, not finished (or finished less than a day ago); at most three.</summary>
        public static List<TopicView> ActiveTopics(out int hidden)
        {
            int now = Find.TickManager.TicksGame;
            var all = Views.Where(v => v.Unlocked && v.Steps.Count > 0 && (!v.Done || now - v.DoneTick < DoneGraceTicks)).ToList();
            hidden = Math.Max(0, all.Count - 3);
            return all.Take(3).ToList();
        }
        public static List<TopicView> DoneTopics()
        {
            int now = Find.TickManager.TicksGame;
            return Views.Where(v => v.Unlocked && v.Done && now - v.DoneTick >= DoneGraceTicks).ToList();
        }
    }

    /// <summary>The small lesson panel at the top right, directly under vanilla's learning helper (4.10).</summary>
    public static class GuidePanel
    {
        public const int WindowId = 76136313;
        const float W = 230f, Margin = 8f, Inner = 7f;
        static bool panelCollapsed;
        static readonly HashSet<string> toggled = new HashSet<string>();
        static bool doneOpen;
        static float contentHeight = 120f;
        static Vector2 scroll;
        static Rect lastRect;
        static readonly System.Reflection.FieldInfo windowRectField = AccessTools.Field(typeof(LearningReadout), "windowRect");
        public static Rect LastRect => lastRect;
        public static int DrawCount;

#if RW16PLUS
        static bool WorldOpenNow() => WorldRendererUtility.WorldSelected;
#else
        static bool WorldOpenNow() => WorldRendererUtility.WorldRenderedNow;
#endif

        public static bool Enabled()
        {
            if (Current.ProgramState != ProgramState.Playing || HearthProfile.Current == null) return false;
            if (!HSettings.GetBool("core.guide")) return false;
            if (UiState.EffectiveTutorial() == TutorialMode.Off) return false;
            if (Find.CurrentMap == null || WorldOpenNow()) return false;
            if (Find.WindowStack.IsOpen<Screen_Credits>()) return false;
            return true;
        }

        /// <summary>Full tour only until the colony was saved once (4.11 "Full first time, then short").</summary>
        public static bool ExpandByDefault()
        {
            var mode = UiState.EffectiveTutorial();
            if (mode == TutorialMode.AlwaysFull) return true;
            if (mode == TutorialMode.FullFirstTime) return !UiState.Flag("saved");
            return false;
        }

        static float TopY()
        {
            try
            {
                var lr = Find.Tutor?.learningReadout;
                if (lr == null) return Margin;
                bool shown = !TutorSystem.TutorialMode && TutorSystem.AdaptiveTrainingEnabled && (Find.PlaySettings.showLearningHelper || lr.ActiveConceptsCount != 0);
                if (!shown || windowRectField == null) return Margin;
                return ((Rect)windowRectField.GetValue(lr)).yMax + 6f;
            }
            catch { return Margin; }
        }

        public static void OnGUI()
        {
            if (!Enabled()) return;
            if (Event.current.type == EventType.Layout) return;
            var views = GuideState.Views;
            if (views.Count == 0) return;
            var active = GuideState.ActiveTopics(out int hidden);
            if (active.Count == 0) return;
            float y = TopY();
            float maxH = Mathf.Max(120f, UI.screenHeight - y - 60f);
            var rect = new Rect(UI.screenWidth - Margin - W, y, W, Mathf.Min(contentHeight + Inner * 2f, maxH));
            lastRect = rect; DrawCount++; UiProbe.Hit("guide");
            Find.WindowStack.ImmediateWindow(WindowId, rect, WindowLayer.Super, () => Guard.Run(ModuleId.Core, "UI.Guide", () => DrawContents(rect, active, hidden)), doBackground: false);
        }

        static void DrawContents(Rect windowRect, List<GuideState.TopicView> active, int hidden)
        {
            var r = windowRect.AtZero().ContractedBy(Inner);
            Widgets.DrawWindowBackgroundTutor(windowRect.AtZero());
            // right click anywhere hides the guide (setting core.guide)
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(windowRect.AtZero()))
            {
                HSettings.Set("core.guide", 0f);
                Messages.Message("HH.UI.Guide.Hidden".Translate(), MessageTypeDefOf.SilentInput, false);
                Event.current.Use();
                return;
            }
            Text.Font = GameFont.Small;
            float y = r.y;
            var head = new Rect(r.x, y, r.width - 24f, 24f);
            Widgets.Label(head, "HH.UI.Guide.Title".Translate());
            if (Widgets.ButtonImage(new Rect(head.xMax, y, 24f, 24f), panelCollapsed ? TexButton.Plus : TexButton.Minus)) panelCollapsed = !panelCollapsed;
            TooltipHandler.TipRegion(head, "HH.UI.Guide.TitleTip".Translate());
            y += 26f;
            if (!panelCollapsed)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f); Widgets.DrawLineHorizontal(r.x, y, r.width); GUI.color = Color.white; y += 4f;
                var outR = new Rect(r.x, y, r.width, r.yMax - y);
                float bodyH = Mathf.Max(0f, contentHeight - (y - r.y));
                bool scrolls = bodyH > outR.height + 1f;
                var view = new Rect(0, 0, outR.width - (scrolls ? 16f : 0f), scrolls ? bodyH : outR.height);
                Widgets.BeginScrollView(outR, ref scroll, view);
                float cy = 0;
                foreach (var v in active) cy = DrawTopic(v, view.width, cy, false);
                if (hidden > 0) { UiStyle.LabelColored(new Rect(0, cy, view.width, 20f), "HH.UI.Guide.More".Translate(hidden), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); cy += 22f; }
                var done = GuideState.DoneTopics();
                if (done.Count > 0)
                {
                    var hr = new Rect(0, cy, view.width, 22f);
                    if (Widgets.ButtonText(hr, (doneOpen ? "- " : "+ ") + "HH.UI.Guide.Done".Translate(done.Count), false)) doneOpen = !doneOpen;
                    cy += 24f;
                    if (doneOpen) foreach (var v in done) cy = DrawTopic(v, view.width, cy, true);
                }
                contentHeight = (y - r.y) + cy + 4f;
                Widgets.EndScrollView();
            }
            else contentHeight = 30f;
        }

        static bool IsExpanded(GuideState.TopicView v)
        {
            bool flipped = toggled.Contains(v.Def.defName);
            return ExpandByDefault() ? !flipped : flipped;
        }

        static float DrawTopic(GuideState.TopicView v, float w, float y, bool inDone)
        {
            bool expanded = IsExpanded(v) && !inDone || (inDone && toggled.Contains(v.Def.defName));
            var tr = new Rect(0, y, w, 24f);
            if (Mouse.IsOver(tr)) Widgets.DrawHighlight(tr);
            string title = v.Def.LabelCap + "  " + v.DoneCount + "/" + v.Steps.Count;
            GUI.color = v.Done ? UiStyle.Good : UiStyle.Amber;
            var tw = new Rect(tr.x + 2f, tr.y, tr.width - 4f, tr.height);
            Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(tw, (expanded ? "- " : "+ ") + title); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            if (!v.Def.description.NullOrEmpty()) TooltipHandler.TipRegion(tr, v.Def.description);
            if (Widgets.ButtonInvisible(tr)) { if (!toggled.Add(v.Def.defName)) toggled.Remove(v.Def.defName); }
            y += 26f;
            if (!expanded) return y;
            foreach (var (step, done) in v.Steps)
            {
                float h = Mathf.Max(22f, UiStyle.CachedHeight(step.label, w - 34f, GameFont.Small));
                var sr = new Rect(6f, y, w - 6f, h);
                if (Mouse.IsOver(sr)) Widgets.DrawHighlight(sr);
                // check box: manual steps are clickable, others reflect game state
                bool on = done;
                if (step.check == "manual") { if (Widgets.ButtonInvisible(new Rect(sr.x, sr.y, 22f, 22f))) UiState.SetFlag(GuideState.ManualKey(v.Def, step), !on); }
                Widgets.CheckboxDraw(sr.x, sr.y - 1f, on, false, 20f);
                GUI.color = done ? UiStyle.DimGrey : Color.white;
                Widgets.Label(new Rect(sr.x + 24f, sr.y, sr.width - 24f, h), step.label);
                GUI.color = Color.white;
                string tip = step.description;
                if (!step.jump.NullOrEmpty()) tip = (tip.NullOrEmpty() ? "" : tip + "\n\n") + "HH.UI.Guide.ClickJump".Translate();
                if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(sr, tip);
                if (!step.jump.NullOrEmpty() && Widgets.ButtonInvisible(new Rect(sr.x + 24f, sr.y, sr.width - 24f, h))) Jump(step.jump);
                y += h + 2f;
            }
            return y + 4f;
        }

        public static void Jump(string jump)
        {
            if (jump.NullOrEmpty()) return;
            if (jump == "settings") { MainTabWindow_HH.OpenSettings(); return; }
            if (jump.StartsWith("tab:"))
            {
                MainTabWindow_HH.SelectedPage = jump.Substring(4);
                if (DefDatabase<MainButtonDef>.GetNamedSilentFail("HH_MainTab") is MainButtonDef def && Find.MainTabsRoot.OpenTab != def) Find.MainTabsRoot.SetCurrentTab(def);
            }
        }
    }

    [HarmonyPatch(typeof(Tutor), "TutorOnGUI")]
    public static class Patch_Tutor_TutorOnGUI
    {
        [HarmonyPostfix]
        public static void Postfix() { UiProbe.Hit("ongui"); Guard.Run(ModuleId.Core, "UI.GuidePanel", GuidePanel.OnGUI); }
    }

    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
    public static class Patch_SaveGame
    {
        [HarmonyPostfix]
        public static void Postfix() { if (HearthProfile.Current != null) UiState.SetFlag("saved"); }
    }
}
