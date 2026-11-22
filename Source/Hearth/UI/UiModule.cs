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
    /// <summary>Looks of furniture (4.9). Only the setting exists in this module; the furniture textures follow with the styles work.</summary>
    public enum HouseStyle { Automatic = 0, StoneAge = 1, Medieval = 2, Industrial = 3, Spacer = 4, Ultra = 5 }

    /// <summary>
    /// Entry point of the player-facing shell (ModuleId.Core, lives in Hearth.dll): main tab, guest tab, rich settings window,
    /// first-start card, guide, difficulty dialog. It registers the Core settings that belong to difficulty and style.
    /// </summary>
    public sealed class UiModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Core;
        static readonly float[] StageAnchors = { 0f, 1f, 2f, 4f, 5f, 6f, 7f };

        public override void RegisterSettings()
        {
            const string diff = "Difficulty", special = "Special switches", style = "Style";
            // tier-driven numbers every module may read (9.2)
            HSettings.Int(ModuleId.Core, diff, "core.consequenceStage", 4, 0, 7, "Highest consequence stage",
                "How far bad outcomes can climb: 1 mood, 2 renown, 3 goodwill, 4 money, 5 licence, 6 raids, 7 bounty. Follows the hosting difficulty.", hardMin: 0, hardMax: 7, tiers: StageAnchors);
            HSettings.Float(ModuleId.Core, diff, "core.researchCost", 1f, 0.1f, 3f, "Research cost factor",
                "Multiplies the cost of Hearth & Harbor and Silverline research projects. Follows the hosting difficulty.", hardMin: 0.01f, hardMax: 100f, tiers: new[] { 0.25f, 0.6f, 0.8f, 1f, 1.2f, 1.4f, 2f });
            HSettings.Float(ModuleId.Core, diff, "core.upkeep", 1f, 0f, 3f, "Upkeep factor",
                "Multiplies the running costs of your venues and rooms. Follows the hosting difficulty.", hardMin: 0f, hardMax: 100f, tiers: new[] { 0f, 0.5f, 0.8f, 1f, 1.2f, 1.5f, 2f });

            // special switches (9.1). Switches that double a real setting elsewhere are shortcuts to it; until that module reads them, these are the source.
            HSettings.Bool(ModuleId.Core, special, "core.neverDie", false, "Guests never die", "Guests are knocked down instead of killed and are rescued for free.");
            HSettings.Bool(ModuleId.Core, special, "core.noHostile", false, "No hostile guests ever", "Guests never turn hostile, whatever their faction thinks of you.");
            HSettings.Bool(ModuleId.Core, special, "core.infiniteMoney", false, "Infinite guest money", "Guests always have enough to pay. The guest money factor has no effect while this is on.");
            HSettings.Bool(ModuleId.Core, special, "core.picky", false, "Picky guests", "Guests expect a little more from every stay (expectation +0.1).");
            HSettings.Bool(ModuleId.Core, special, "core.feuding", false, "Feuding factions", "Hostile factions dislike each other twice as much when they share a roof.");
            HSettings.Bool(ModuleId.Core, special, "core.mandatory", false, "Mandatory hospitality", "Contracts and billeting are forced on you, even below the Overbooked tier.");
            HSettings.Bool(ModuleId.Core, special, "core.strictInspectors", false, "Strict inspectors", "Inspectors check three times as often.");
            HSettings.Bool(ModuleId.Core, special, "core.realConsequences", false, "Real consequences", "Raises the consequence ladder by one stage.");
            HSettings.Bool(ModuleId.Core, special, "core.ironBank", false, "Iron purse / Iron bank", "No bailout, no overdraft, compound interest without mercy.");
            HSettings.Bool(ModuleId.Core, special, "core.noResearchGating", false, "No research gating", "Everything on the Hearth & Harbor and Silverline trees is available from the start.");
            HSettings.Bool(ModuleId.Core, special, "core.peacefulUnderworld", false, "Peaceful underworld", "Dark venues work without violence.");
            HSettings.Bool(ModuleId.Core, special, "core.showMath", false, "Show me the math", "Shows nerd settings and formulas everywhere.");
            HSettings.Bool(ModuleId.Core, special, "core.perfMode", false, "Performance mode", "Fewer guests at once and simpler guest behaviour on slow computers.");

            // style (4.9): appearance only, never what guests can do
            HSettings.Enum(ModuleId.Core, style, "core.houseStyle", HouseStyle.Automatic, "House style",
                "The look of furniture and signs. Automatic follows your ideology style, otherwise your highest researched era. Looks only: guests and prices are not affected.");
            HSettings.Bool(ModuleId.Core, style, "core.keepBuiltStyle", false, "Built furniture keeps its old style",
                "When the house style changes with a new era, already built furniture keeps its look.");
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
        public static void Postfix(Rect rect) { Guard.Run(ModuleId.Core, "UI.StorytellerLine", () => HostingLine.Draw(rect)); }
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
        public static void Draw(Rect rect)
        {
            float x = rect.x + 170f, right = rect.xMax - 370f, h = 38f;
            if (right - x < 300f) { x = rect.x + 160f; right = rect.xMax - 330f; }
            var line = new Rect(x, rect.yMax - h, right - x, h);
            var prof = UiState.PendingProfile ?? HearthMod.Template.Profile;
            UiStyle.LabelColored(new Rect(line.x, line.y, line.width - 176f, 14f), "HH.UI.Storyteller.Head".Translate(HearthProfile.PercentFromTier(prof.Tier).ToString("0")), UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(line.x, line.y + 12f, line.width - 176f, 26f), HearthProfile.TierName(prof.Tier), UiStyle.Amber, TextAnchor.MiddleLeft);
            var btn = new Rect(line.xMax - 170f, line.y, 170f, h);
            if (Widgets.ButtonText(btn, "HH.UI.Storyteller.Button".Translate()))
                Find.WindowStack.Add(new Dialog_HostingDifficulty(UiState.EnsurePendingProfile()));
            TooltipHandler.TipRegion(btn, "HH.UI.Storyteller.Tip".Translate());
        }
    }
}
