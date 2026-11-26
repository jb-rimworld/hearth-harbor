// SPDX-License-Identifier: MIT
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Package-owned setup hooks: settings registered from UnderworldModule.RegisterSettings, def lookups from OnDefsLoaded.</summary>
    public static class FacilitySetup
    {
        public static void RegisterSettings(ModuleId m)
        {
            HSettings.Float(m, "Gangs", "underworld.gang.firstDays", 1f, 0.2f, 6f, "Days until the first gang",
                "The first gang comes about this many days (half to one and a half times) after the first back room, fence counter or fight pit exists.", nerd: true, hardMin: 0.05f, hardMax: 60f);
            HSettings.Float(m, "Gangs", "underworld.gang.retryHours", 6f, 1f, 48f, "Hours before a failed gang visit is tried again",
                "When a gang cannot come (no friendly faction, no way onto the map), the next try is this many hours later.", nerd: true, hardMin: 0.25f, hardMax: 600f);
            HSettings.Int(m, "Gangs", "underworld.gang.demandHours", 6, 1, 24, "Hours to answer a protection demand",
                "A demand you do not answer within this time counts as a refusal.", hardMin: 1, hardMax: 240);
            HSettings.Int(m, "Smuggler's dock", "underworld.smuggler.offerHours", 6, 1, 24, "Hours a smuggler's boat waits",
                "A boat you do not answer within this time leaves; nothing else happens.", hardMin: 1, hardMax: 240);
        }

        public static void OnDefsLoaded()
        {
            HearthGame.OnGameInit -= FacilityStatus.Clear;
            HearthGame.OnGameInit += FacilityStatus.Clear;
        }
    }
}
