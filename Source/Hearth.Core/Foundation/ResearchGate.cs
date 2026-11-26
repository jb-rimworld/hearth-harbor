// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// The one place that honours the shared research mode (core.researchMode = Off, KONZEPT 7.1): every project of the tabs
    /// HH_Hospitality and HH_Banking is finished quietly, so every building and feature behind research is open. Since 1.2.0 that includes
    /// the projects of the underworld (they stay hidden while their content level is not allowed; the buildings keep their own content gate).
    /// Runs at game start, after loading, and now and then (the setting can change in the middle of a game).
    /// </summary>
    public static class ResearchGate
    {
        public static bool IsOurs(ResearchProjectDef p) =>
            p?.tab != null && (p.tab.defName == "HH_Hospitality" || p.tab.defName == "HH_Banking");

        /// <summary>The project carries a gate extension of the underworld (content intensity). Since 1.2.0 the research mode applies to them as well.</summary>
        public static bool IsGated(ResearchProjectDef p)
        {
            var ext = p.modExtensions;
            if (ext == null) return false;
            for (int i = 0; i < ext.Count; i++) if (ext[i] != null && ext[i].GetType().Name == "UwGateExtension") return true;
            return false;
        }

        public const string CostKey = "core.researchCost";
        static readonly System.Collections.Generic.Dictionary<ResearchProjectDef, float> baseCosts = new System.Collections.Generic.Dictionary<ResearchProjectDef, float>();

        /// <summary>The cost factor of core.researchCost (1 when the setting does not exist).</summary>
        public static float CostFactor
        {
            get { try { return HSettings.Find(CostKey) != null ? UnityEngine.Mathf.Max(0.01f, HSettings.Get(CostKey)) : 1f; } catch { return 1f; } }
        }

        /// <summary>Sets the cost of every project of our tabs to its printed cost times core.researchCost. Idempotent; remembers the printed cost of each project.</summary>
        public static int ApplyCostFactor()
        {
            float f = CostFactor; int n = 0;
            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!IsOurs(p)) continue;
                if (!baseCosts.TryGetValue(p, out float b)) { b = p.baseCost; baseCosts[p] = b; }
                float want = Math.Max(1f, (float)Math.Round(b * f));
                if (Math.Abs(p.baseCost - want) > 0.01f) { p.baseCost = want; n++; }
            }
            return n;
        }

        /// <summary>Printed (unscaled) cost of one of our projects.</summary>
        public static float PrintedCost(ResearchProjectDef p) => baseCosts.TryGetValue(p, out float b) ? b : p.baseCost;

        /// <summary>Finishes all open projects of our tabs when the research mode is Off. Returns how many were finished.</summary>
        public static int Apply()
        {
            if (Current.Game != null) ApplyCostFactor();
            if (!CoreSettings.ResearchOff || Current.Game == null || Find.ResearchManager == null) return 0;
            int n = 0;
            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!IsOurs(p) || p.IsFinished) continue;
                try { Find.ResearchManager.FinishProject(p, doCompletionDialog: false, researcher: null, doCompletionLetter: false); n++; }
                catch (Exception e) { HLog.Warn("Core", "Research off: could not finish " + p.defName + ": " + e.Message); }
            }
            if (n > 0) HLog.Info("Core", $"Research mode off: {n} projects finished");
            return n;
        }
    }
}
