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
                return GlobalResearchOff();
            }
        }

        /// <summary>Soft link to a shared research mode of other modules, if one exists ("core.researchMode" enum with an Off member, or "core.researchOff" bool).</summary>
        static bool GlobalResearchOff()
        {
            try
            {
                var b = HSettings.Find("core.researchOff");
                if (b != null && HSettings.Get(b) >= 0.5f) return true;
                var e = HSettings.Find("core.researchMode");
                if (e != null && e.Kind == SettingKind.Enum && e.EnumType != null)
                    return string.Equals(Enum.GetName(e.EnumType, (int)Math.Round(HSettings.Get(e))), "Off", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { HLog.Once("city.globalResearch", LogLevel.Warn, "City", "Could not read the shared research mode: " + ex.Message); }
            return false;
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
