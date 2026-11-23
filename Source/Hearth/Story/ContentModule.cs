// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using Hearth.Music;
using Verse;

namespace Hearth.Story
{
    /// <summary>
    /// 1.1.0 content that belongs to no single module (ModuleId.Core, Hearth.dll): our music (F07/F32), the storyteller Hetty Hearthwell (F09,
    /// defs only), the shipped scenarios and the full settings editor for the scenario rules part (F08), and the author's easter-egg pawn (F18).
    /// </summary>
    public sealed class ContentModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Core;

        public override void RegisterSettings()
        {
            HearthMusic.RegisterSettings();
            var c = HSettings.Bool(ModuleId.Core, "Fun", CreatorPawn.SettingKey, true, "The author may wander the rim",
                "Like the creator pawns of the base game, Hearth & Harbor's author can turn up now and then as a generated pawn: a kind, hard-working wandering innkeeper.", nerd: true);
            c.Global = true;
        }

        public override void OnDefsLoaded()
        {
            HearthMusic.Init();
            CreatorPawn.Sync();
            ScenarioSeed.OpenProfileEditor = (profile, onClose) => Find.WindowStack.Add(new Shell.Dialog_HHProfileEditor(profile, onClose));
            HLog.Info("Core", "Content ready: storyteller " + (DefDatabase<RimWorld.StorytellerDef>.GetNamedSilentFail("HH_Hetty") != null ? "Hetty" : "missing")
                              + ", creator pawn " + (CreatorPawn.InDatabase ? "in the solid bios" : "off"));
        }

        public override void OnGameStarted() { HearthMusic.Refresh(); CreatorPawn.Sync(); }
        public override void OnGameLoaded() { HearthMusic.Refresh(); CreatorPawn.Sync(); }

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % 250 == 113) HearthMusic.Refresh();
            if (ticksGame % 2500 == 1117) CreatorPawn.Sync();
        }

        public override IEnumerable<string> Diagnosis()
        {
            foreach (var l in HearthMusic.Diagnosis()) yield return l;
            yield return "Storyteller hints: " + (StorytellerHints.Current != null ? "guests ×" + StorytellerHints.GuestArrivalFactor.ToString("0.##") : "none");
            yield return "Creator pawn: " + (CreatorPawn.InDatabase ? "may appear" : "off");
        }
    }
}
