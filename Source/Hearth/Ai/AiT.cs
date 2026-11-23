// SPDX-License-Identifier: MIT
using System;
using Verse;

namespace Hearth.Ai
{
    /// <summary>
    /// Text lookup of the AI module: AiT.T("Key", "English text", args...) → keyed translation "HH.Ai.Key" with the English text as fallback.
    /// tools/gen_ai_keyed.py extracts every AiT.T("Key", "English") literal into Common/Languages/English/Keyed/HH_Ai.xml, so the file never drifts from the code.
    /// Placeholders are {0}, {1} … (string.Format, not Verse's own formatter).
    /// </summary>
    public static class AiT
    {
        public static string T(string key, string en, params object[] args)
        {
            string s = ("HH.Ai." + key).TranslateOr(en);
            if (args == null || args.Length == 0) return s;
            try { return string.Format(s, args); } catch (FormatException) { try { return string.Format(en, args); } catch (FormatException) { return en; } }
        }
    }
}
