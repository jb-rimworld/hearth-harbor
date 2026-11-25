// SPDX-License-Identifier: MIT
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Package-owned setup hooks: settings registered from UnderworldModule.RegisterSettings, def lookups from OnDefsLoaded.</summary>
    public static class FacilitySetup
    {
        public static void RegisterSettings(ModuleId m) { }

        public static void OnDefsLoaded() { }
    }
}
