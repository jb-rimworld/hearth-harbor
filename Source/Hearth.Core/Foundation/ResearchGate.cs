// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// The one place that honours the shared research mode (core.researchMode = Off, KONZEPT 7.1): every project of the tabs
    /// HH_Hospitality and HH_Banking is finished quietly, so every building and feature behind research is open. Projects that carry
    /// a gate extension of the underworld stay as they are: the discovery of the back room and the content intensity still apply.
    /// Runs at game start, after loading, and now and then (the setting can change in the middle of a game).
    /// </summary>
    public static class ResearchGate
    {
        public static bool IsOurs(ResearchProjectDef p) =>
            p?.tab != null && (p.tab.defName == "HH_Hospitality" || p.tab.defName == "HH_Banking");

        /// <summary>Underworld projects are gated by their own discovery and content intensity, never by the research mode.</summary>
        public static bool IsGated(ResearchProjectDef p)
        {
            var ext = p.modExtensions;
            if (ext == null) return false;
            for (int i = 0; i < ext.Count; i++) if (ext[i] != null && ext[i].GetType().Name == "UwGateExtension") return true;
            return false;
        }

        /// <summary>Finishes all open projects of our tabs when the research mode is Off. Returns how many were finished.</summary>
        public static int Apply()
        {
            if (!CoreSettings.ResearchOff || Current.Game == null || Find.ResearchManager == null) return 0;
            int n = 0;
            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!IsOurs(p) || p.IsFinished || IsGated(p)) continue;
                try { Find.ResearchManager.FinishProject(p, doCompletionDialog: false, researcher: null, doCompletionLetter: false); n++; }
                catch (Exception e) { HLog.Warn("Core", "Research off: could not finish " + p.defName + ": " + e.Message); }
            }
            if (n > 0) HLog.Info("Core", $"Research mode off: {n} projects finished");
            return n;
        }
    }
}
