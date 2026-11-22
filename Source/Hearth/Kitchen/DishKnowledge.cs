// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>
    /// What the colony knows how to cook (KONZEPT 6.4, N46). Level 1 is known from the start; levels 2–4 come with the research
    /// HH_Cooking_L2..L4; single dishes can be learned from cookbooks and guests. Saved in the game; removing the mod leaves nothing behind.
    /// </summary>
    public sealed class DishKnowledge : GameComponent
    {
        public static DishKnowledge Instance;
        HashSet<string> learned = new HashSet<string>();
        List<string> tmp;

        public DishKnowledge(Game g) { Instance = this; }

        public override void FinalizeInit() { Instance = this; }
        public override void StartedNewGame() { Instance = this; }
        public override void LoadedGame() { Instance = this; }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref learned, "learnedDishes", LookMode.Value);
            if (learned == null) learned = new HashSet<string>();
        }

        public static ResearchProjectDef LevelResearch(int level) => level <= 1 ? null : DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Cooking_L" + level);

#if HH_TESTS
        /// <summary>Tests only: pretend levels are locked (the quick-test game has all research finished).</summary>
        public static Func<int, bool> TestLevelLock;
#endif

        public static bool LevelUnlocked(int level)
        {
            if (level <= 1) return true;
#if HH_TESTS
            if (TestLevelLock != null) return TestLevelLock(level);
#endif
            var r = LevelResearch(level);
            return r != null && r.IsFinished;
        }

        /// <summary>May the colony cook this dish now (module on, learned, dark menu allowed)?</summary>
        public static bool IsKnown(DishDef d)
        {
            if (d == null || !Modules.On(ModuleId.ShopsFood)) return false;
            if (d.dark && !DarkAllowed()) return false;
            if (HSettings.GetBool("kitchen.allKnown")) return true;
            if (LevelUnlocked(d.level)) return true;
            return Instance != null && Instance.learned.Contains(d.defName);
        }

        /// <summary>Tests and the dev menu: forget every individually learned dish.</summary>
        public static void ForgetAll() { Instance?.learned.Clear(); }

        public static bool IsLearnedIndividually(DishDef d) => Instance != null && Instance.learned.Contains(d.defName);

        public static bool Learn(DishDef d)
        {
            if (d == null || Instance == null) return false;
            bool fresh = Instance.learned.Add(d.defName);
            if (fresh) HLog.Info("Kitchen", "Dish learned: " + d.defName);
            return fresh;
        }

        public static IEnumerable<DishDef> Known() => DefDatabase<DishDef>.AllDefs.Where(IsKnown);

        /// <summary>The dark menu (long pork †): only when the player's rules allow it. Setting, or an ideoligion that approves cannibalism.</summary>
        public static bool DarkAllowed()
        {
            if (HSettings.GetBool("kitchen.darkMenu")) return true;
            try
            {
                var acceptable = DefDatabase<PreceptDef>.GetNamedSilentFail("Cannibalism_Acceptable");
                if (ModsConfig.IdeologyActive && Faction.OfPlayerSilentFail != null)
                    foreach (var ideo in Faction.OfPlayer.ideos.AllIdeos)
                        if (ideo.HasPrecept(PreceptDefOf.Cannibalism_Preferred) || ideo.HasPrecept(PreceptDefOf.Cannibalism_RequiredStrong) || ideo.HasPrecept(PreceptDefOf.Cannibalism_RequiredRavenous) || (acceptable != null && ideo.HasPrecept(acceptable)))
                            return true;
            }
            catch (Exception e) { HLog.Once("kitchen.dark", LogLevel.Warn, "Kitchen", "Could not check cannibalism precepts: " + e.Message); }
            return false;
        }

        /// <summary>Level 1 research is free: finished at the start of every game that has the module on.</summary>
        public static void EnsureFreeResearch()
        {
            var r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Cooking_L1");
            if (r == null || r.IsFinished || Find.ResearchManager == null) return;
            Find.ResearchManager.FinishProject(r, doCompletionDialog: false, researcher: null, doCompletionLetter: false);
        }
    }
}
