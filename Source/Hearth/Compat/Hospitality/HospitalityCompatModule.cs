// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Hearth.Compat.Hospitality
{
    /// <summary>
    /// Hospitality compatibility, part of the core (always on). Registers the save importer, runs the takeover when Hospitality
    /// is active alongside us (after every other mod's StaticConstructorOnStartup), and offers the disable dialog in the main menu.
    /// </summary>
    public sealed class HospitalityCompatModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Core;

        public override void RegisterSettings()
        {
            var never = HSettings.Bool(ModuleId.Core, "Compatibility", HospitalityDisabler.NeverAskKey, false, "Don't offer to disable Hospitality",
                "When Hospitality runs alongside Hearth & Harbor, Hearth & Harbor switches it off for the session and offers to disable it in the main menu. On: no more offers (the switch-off still happens).");
            never.Global = true;
            HSettings.Bool(ModuleId.Core, "Compatibility", "compat.importLetter", true, "Letter after importing a Hospitality save",
                "After loading a save that was played with Hospitality, a letter lists what Hearth & Harbor took over (guest beds, areas, visitors) and what was dropped.");
        }

        public override void OnDefsLoaded()
        {
            HospitalityImporter.Register();
            var found = HospitalityFamily.Detected;
            if (found.Count > 0) HLog.Info("Compat", "Hospitality family mods running: " + string.Join(", ", found.Select(d => d + " [" + d.Entry.Role + "]")));
            // after every other StaticConstructorOnStartup (Hospitality injects its comps, tabs and guest beds there)
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Guard.Run(ModuleId.Core, "HospitalityTakeover.Run", HospitalityTakeover.Run);
                Guard.Run(ModuleId.Core, "HospitalityDisabler.Offer", HospitalityDisabler.OfferAtMainMenu);
            });
        }

        public override void OnGameStarted() => AfterGameReady();
        public override void OnGameLoaded() => AfterGameReady();

        static void AfterGameReady()
        {
            if (!HospitalityTakeover.Active) return;
            // in case a family mod patched lazily (first map, first game): sweep again
            Guard.Run(ModuleId.Core, "HospitalityTakeover.Sweep", () => HospitalityTakeover.UnpatchFamily("game start"));
        }

        public override IEnumerable<string> Diagnosis()
        {
            foreach (var l in HospitalityTakeover.Diagnosis()) yield return l;
            yield return "Hospitality importer: " + (HospitalityImporter.IsInChain() ? "registered" : "NOT registered");
            var rec = ImportedGuestBeds.Current;
            if (rec != null && (rec.Beds.Count > 0 || rec.Areas.Count > 0 || rec.Visitors.Count > 0))
                yield return $"Imported from Hospitality: {rec.Beds.Count} guest beds ({rec.Beds.Count(b => !b.Adopted)} not yet adopted), {rec.Areas.Count} areas, {rec.Visitors.Count} known visitors";
        }
    }
}
