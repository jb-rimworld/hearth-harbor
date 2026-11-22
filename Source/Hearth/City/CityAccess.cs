// SPDX-License-Identifier: MIT
using System;
using RimWorld;
using Verse;

namespace Hearth.City
{
    /// <summary>
    /// Who may use what (KONZEPT 7): the module must be on and the research done, or research mode "off"
    /// (also when a global research mode of the Hospitality tree says off).
    /// </summary>
    public static class CityAccess
    {
        public static bool ModuleOn => Modules.On(ModuleId.CityLife);

        public static bool ResearchOff
        {
            get
            {
                if (HSettings.GetEnum<CityResearchMode>(CitySettings.ResearchMode) == CityResearchMode.Off) return true;
                return CoreSettings.ResearchOff;
            }
        }

        static bool Done(ResearchProjectDef p) => p == null || p.IsFinished || ResearchOff;

        /// <summary>Cinema, salon, gallery.</summary>
        public static bool Level1 => ModuleOn && Done(CityDefs.Research1);
        /// <summary>Taxi and personal security.</summary>
        public static bool Level2 => ModuleOn && Done(CityDefs.Research2);

        public static bool TaxiOn => Level2;
        public static bool SecurityOn => Level2 && CitySettings.B(CitySettings.SecurityOn);

        /// <summary>With research mode off both projects are finished quietly so the build menu opens up (called now and then).</summary>
        public static int AutoFinishResearch()
        {
            if (!ModuleOn || !ResearchOff || Find.ResearchManager == null) return 0;
            int n = 0;
            foreach (var p in new[] { CityDefs.Research1, CityDefs.Research2 })
            {
                if (p == null || p.IsFinished) continue;
                Find.ResearchManager.FinishProject(p, false, null, false);
                n++;
            }
            if (n > 0) HLog.Info("City", "Research mode off: " + n + " City Life project(s) marked as finished");
            return n;
        }
    }
}
