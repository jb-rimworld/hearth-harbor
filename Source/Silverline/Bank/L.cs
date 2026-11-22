// SPDX-License-Identifier: MIT
using Hearth;
using Verse;

namespace Silverline
{
    /// <summary>
    /// Text lookup for the bank: L.T("Key", "English text", args...) → keyed translation "HH.Bank.Key" with the English text as fallback.
    /// tools/gen_bank_keys.py extracts every L.T("Key", "English") literal into Common/Languages/English/Keyed/HH_Bank.xml,
    /// so the file never drifts from the code. Placeholders are {0}, {1} … (string.Format, not Verse's own formatter).
    /// </summary>
    public static class L
    {
        public static string T(string key, string en, params object[] args)
        {
            string s = ("HH.Bank." + key).TranslateOr(en);
            if (args == null || args.Length == 0) return s;
            try { return string.Format(s, args); } catch { return en; }
        }
    }
}
