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
    }
}
