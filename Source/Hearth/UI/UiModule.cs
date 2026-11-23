// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>
    /// Entry point of the player-facing shell (ModuleId.Core, lives in Hearth.dll): main tab, guest tab, rich settings window,
    /// first-start card, guide, difficulty dialog. It registers the Core settings that belong to difficulty.
    /// </summary>
    public sealed class UiModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Core;

        public override void RegisterSettings()
        {
            const string diff = "Difficulty", special = "Special switches";
            // tier-driven numbers every module may read (9.2)
            HSettings.Float(ModuleId.Core, diff, "core.researchCost", 1f, 0.1f, 3f, "Research cost factor",
                "Multiplies the cost of Hearth & Harbor and Silverline research projects. Follows the hosting difficulty.", hardMin: 0.01f, hardMax: 100f, tiers: new[] { 0.25f, 0.6f, 0.8f, 1f, 1.2f, 1.4f, 2f });

            // special switches (9.1). Every switch listed here is read by game code (see DifficultyTests); a switch nobody reads does not ship.
            HSettings.Bool(ModuleId.Core, special, "core.neverDie", false, "Guests never die", "Guests are knocked down instead of killed. They keep living, downed, until someone tends them.");
            HSettings.Bool(ModuleId.Core, special, "core.infiniteMoney", false, "Infinite guest money", "Guests who arrive while this is on carry a bottomless purse. The guest money factor has no effect on them.");
            HSettings.Bool(ModuleId.Core, special, "core.picky", false, "Picky guests", "Guests expect a little more from every stay (expectation +0.1).");
            HSettings.Bool(ModuleId.Core, special, "core.ironBank", false, "Iron bank", "Debt collection always climbs to the highest stage, whatever the difficulty. Nobody quietly defers your instalments.");
            HSettings.Bool(ModuleId.Core, special, "core.noResearchGating", false, "No research gating", "Everything on the Hearth & Harbor and Silverline trees is available from the start.");
            HSettings.Bool(ModuleId.Core, special, "core.peacefulUnderworld", false, "Peaceful underworld", "Dark venues work without violence: no fight pit, no questioning, no hunting of people, no clinic, no protection racket. Back room, fence and drug lounge stay.");
            HSettings.Bool(ModuleId.Core, special, "core.showMath", false, "Show me the math", "Shows nerd settings and formulas everywhere.");
            HSettings.Bool(ModuleId.Core, special, "core.perfMode", false, "Performance mode", "Guest planning and room checks run half as often. Helps on slow computers; guests may react a little later.");

        }

        public override void OnDefsLoaded()
        {
            HearthSettingsWindow.Install();

            HearthTabs.Register(new HearthTabPage { Id = "overview", LabelKey = "HH.UI.Page.Overview", LabelEn = "Overview", Module = ModuleId.Core, Order = 0, Draw = OverviewPage.Draw });
            HearthTabs.Register(new HearthTabPage { Id = "guests", LabelKey = "HH.UI.Page.Guests", LabelEn = "Guests", Module = ModuleId.Inn, Order = 10, Draw = r => { UiState.SetFlag("seenGuestsPage"); GuestsPage.Draw(r); } });

            int tabs = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.Humanlike))
            {
                if (def.inspectorTabs == null) def.inspectorTabs = new List<Type>();
                if (def.inspectorTabs.Contains(typeof(ITab_HH_Guest))) continue;
                def.inspectorTabs.Add(typeof(ITab_HH_Guest));
                if (def.inspectorTabsResolved == null) def.inspectorTabsResolved = new List<InspectTabBase>();
                def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_HH_Guest)));
                tabs++;
            }
            Ledger.Changed += (from, to, amount, memo) => { if (to != null && to.StartsWith("colony:till")) UiState.SetFlag("paymentSeen"); };
            HLog.Info("UI", $"Shell ready: settings window installed, Guest tab added to {tabs} humanlike races, {DefDatabase<GuideTopicDef>.DefCount} guide topics");
        }

        public override void OnGameStarted()
        {
            FirstStartCardTrigger.Reset();
            GuideState.Views.Clear();
            UiState.ApplyPendingDifficulty(HearthProfile.Current);
        }

        public override void OnGameLoaded()
        {
            FirstStartCardTrigger.Reset();
            GuideState.Views.Clear();
            UiState.SetFlag("saved"); // a loaded colony has been saved at least once → the guide continues in short mode
            HearthUI.InvalidateUsage();
        }

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % 30 == 5) FirstStartCardTrigger.Tick(ticksGame);
            if (ticksGame % 60 == 7 || GuideState.Views.Count == 0 && ticksGame % 10 == 3) GuideState.Update();
        }

        public override IEnumerable<string> Diagnosis()
        {
            yield return "Main tab visible: " + (HearthProfile.Current != null && HearthUI.IsUsedInColony()) + ", pages: " + string.Join(", ", HearthTabs.VisiblePages.Select(p => p.Id));
            yield return "Guide topics: " + GuideState.Views.Count(v => v.Unlocked) + " unlocked, " + GuideState.Views.Count(v => v.Done) + " done";
        }
    }

    /// <summary>Patches on vanilla pages. Minimal postfixes that return at once when nothing is to be drawn.</summary>
    [HarmonyPatch(typeof(Page_SelectStoryteller), nameof(Page_SelectStoryteller.DoWindowContents))]
    public static class Patch_StorytellerHostingLine
    {
        [HarmonyPostfix]
        public static void Postfix(Page_SelectStoryteller __instance, Rect rect) { Guard.Run(ModuleId.Core, "UI.StorytellerLine", () => HostingLine.Draw(rect, HostingLine.ChosenStoryteller(__instance))); }
    }

    [HarmonyPatch(typeof(Page_SelectStoryteller), nameof(Page_SelectStoryteller.PreOpen))]
    public static class Patch_StorytellerPreOpen
    {
        [HarmonyPostfix]
        public static void Postfix() { UiState.ClearPendingDifficulty(); }
    }

    /// <summary>The one line below the vanilla difficulty block (4.3, 9.6): tier name, percent and the button that opens the difficulty dialog.</summary>
    public static class HostingLine
    {
        static AccessTools.FieldRef<Page_SelectStoryteller, StorytellerDef> chosenRef;
        /// <summary>The storyteller selected on the page (vanilla keeps it private); null if the field is missing on this game version.</summary>
        public static StorytellerDef ChosenStoryteller(Page_SelectStoryteller page)
        {
            if (page == null) return null;
            try { if (chosenRef == null) chosenRef = AccessTools.FieldRefAccess<Page_SelectStoryteller, StorytellerDef>("storyteller"); return chosenRef(page); }
            catch (Exception e) { HLog.Once("ui.chosenStoryteller", LogLevel.Warn, "UI", "Page_SelectStoryteller.storyteller not found: no storyteller suggestion (" + e.Message + ")"); return null; }
        }

        public static void Draw(Rect rect) => Draw(rect, null);

        /// <summary>Tests: the suggestion chip text of the last frame (null if no chip was drawn).</summary>
        public static string LastDrawn { get; private set; }

        /// <summary>Forget the cached texts (language change, tests).</summary>
        public static void ClearCache() { headText = null; chipText = null; chipFor = null; btnText = null; }

        // strings of the line, rebuilt only when what they show changes (the page draws every frame)
        static float headTier = float.NaN; static bool headScen; static string headText, tierName;
        static StorytellerDef chipFor; static float chipTier = float.NaN; static string chipText, chipTip, btnText, btnTip;

        public static void Draw(Rect rect, StorytellerDef chosen)
        {
            float x = rect.x + 170f, right = rect.xMax - 370f, h = 38f;
            if (right - x < 300f) { x = rect.x + 160f; right = rect.xMax - 330f; }
            var line = new Rect(x, rect.yMax - h, right - x, h);
            var prof = UiState.PreviewProfile();
            bool scen = ScenarioSeed.HasSeeds(Find.Scenario);
            // the storyteller's suggestion (F09: Hetty suggests Bed & Breakfast), shown while the chosen tier differs
            var hints = StorytellerHints.Of(chosen);
            bool suggest = hints != null && hints.suggestedTier >= 0f && Mathf.Abs(prof.Tier - hints.suggestedTier) > 0.01f;
            float sugW = suggest ? Mathf.Min(190f, (line.width - 176f) * 0.5f) : 0f;
            float labW = line.width - 176f - sugW;
            if (headText == null || prof.Tier != headTier || scen != headScen)
            {
                headTier = prof.Tier; headScen = scen;
                headText = "HH.UI.Storyteller.Head".Translate(HearthProfile.PercentFromTier(prof.Tier).ToString("0"));
                if (scen) headText += "  ·  " + "HH.Scen.SetByScenario".TranslateOr("set by the scenario");
                tierName = HearthProfile.TierName(prof.Tier);
            }
            UiStyle.LabelColored(new Rect(line.x, line.y, labW, 14f), headText, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(line.x, line.y + 12f, labW, 26f), tierName, UiStyle.Amber, TextAnchor.MiddleLeft);
            if (suggest)
            {
                if (chipText == null || chipFor != chosen || chipTier != hints.suggestedTier)
                {
                    chipFor = chosen; chipTier = hints.suggestedTier;
                    string who = chosen.label.NullOrEmpty() ? chosen.defName : chosen.label.Split(' ')[0];
                    chipText = "HH.Story.Suggests".TranslateOr("{0} suggests: {1}").Replace("{0}", who).Replace("{1}", HearthProfile.TierName(hints.suggestedTier));
                    chipTip = "HH.Story.SuggestsTip".TranslateOr("Click to use the hosting difficulty this storyteller suggests. You can still change it with the button.");
                }
                var sr = new Rect(line.x + labW, line.y + 2f, sugW - 6f, h - 4f);
                if (UiStyle.Chip(sr, chipText, false, chipTip)) UiState.EnsurePendingProfile().Tier = hints.suggestedTier;
            }
            LastDrawn = suggest ? chipText : null;
            var btn = new Rect(line.xMax - 170f, line.y, 170f, h);
            if (btnText == null) { btnText = "HH.UI.Storyteller.Button".Translate(); btnTip = "HH.UI.Storyteller.Tip".Translate(); }
            if (Widgets.ButtonText(btn, btnText))
                Find.WindowStack.Add(new Dialog_HostingDifficulty(UiState.EnsurePendingProfile()));
            TooltipHandler.TipRegion(btn, btnTip);
        }
    }
}
