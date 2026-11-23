// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Verse;

[assembly: InternalsVisibleTo("Hearth")]
[assembly: InternalsVisibleTo("Silverline")]

namespace Hearth
{
    /// <summary>
    /// Saving helper for Money. Money.ToString() is a display format ("1,234.50") and does not round-trip, so never put a
    /// Money into Scribe_Values directly. Use ScribeMoney.Look(ref amount, "label"): it stores the exact micro-silver as an integer.
    /// </summary>
    public static class ScribeMoney
    {
        public static void Look(ref Money value, string label, Money defaultValue = default)
        {
            long micro = value.Micro;
            Scribe_Values.Look(ref micro, label, defaultValue.Micro);
            if (Scribe.mode == LoadSaveMode.LoadingVars) value = new Money(micro);
        }

        /// <summary>
        /// A double that must come back bit-identical (market prices feed sealed random walks: a reload must continue exactly).
        /// Scribe_Values writes doubles with 15 significant digits, which changes the last bits; this writes the round-trip form.
        /// Reads old saves written by Scribe_Values unchanged (same node, plain number).
        /// </summary>
        public static void LookExact(ref double value, string label, double defaultValue = 0)
        {
            string s = Scribe.mode == LoadSaveMode.Saving ? value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null;
            Scribe_Values.Look(ref s, label, defaultValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                value = s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
        }
    }
}
