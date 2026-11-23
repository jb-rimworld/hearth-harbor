// SPDX-License-Identifier: MIT
using System;
using Verse;

namespace Hearth.Ai
{
    /// <summary>
    /// The opt-in of the AI texts (F05). Stored per installation in the mod template (not per save: privacy is personal), with the version of the
    /// consent text that was agreed to. A new consent text version asks again. Without consent nothing is ever sent, whatever the module switch says.
    /// </summary>
    public static class AiConsent
    {
        /// <summary>Raise when the privacy text changes in a way that needs a new yes.</summary>
        public const int CurrentVersion = 1;

        public static bool Given => HearthMod.Template.AiConsentVersion >= CurrentVersion;
        public static string Date => HearthMod.Template.AiConsentDate;
        /// <summary>The player said "not now" in this session: do not ask again until the game restarts (the settings page still offers it).</summary>
        public static bool DeclinedThisSession;

        public static void Grant()
        {
            var t = HearthMod.Template;
            t.AiConsentVersion = CurrentVersion;
            t.AiConsentDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            DeclinedThisSession = false;
            Persist();
            HLog.Info("AI", "Consent given (text version " + CurrentVersion + ")");
            AiService.OnConsentChanged();
        }

        public static void Withdraw()
        {
            var t = HearthMod.Template;
            t.AiConsentVersion = 0;
            t.AiConsentDate = null;
            Persist();
            HLog.Info("AI", "Consent withdrawn");
            AiService.OnConsentChanged();
        }

        static void Persist()
        {
            try { HearthMod.Instance?.WriteSettings(); }
            catch (Exception e) { HLog.Warn("AI", "Could not write the template after a consent change: " + e.Message); }
        }
    }
}
