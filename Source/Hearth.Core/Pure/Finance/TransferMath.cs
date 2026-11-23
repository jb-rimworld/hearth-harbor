// SPDX-License-Identifier: MIT
// Pure maths of the deposit / withdraw slider in the bank lobby (F23, 1.1.0): the slider is a percentage of the silver the colony holds,
// the typed amount and the presets are absolute, and the two must never fight each other.
using System;

namespace Hearth
{
    public static class TransferMath
    {
        /// <summary>Whole silver for a slider position (0..1) of the available silver. Rounds to the nearest coin, never above what is there.</summary>
        public static long AmountFromPercent(double fraction, long available)
        {
            if (available <= 0 || double.IsNaN(fraction) || fraction <= 0) return 0;
            if (fraction >= 1) return available;
            return Math.Min(available, (long)Math.Round(fraction * available, MidpointRounding.AwayFromZero));
        }

        /// <summary>Slider position (0..1) of a typed amount; above 100 % the slider sits at the end.</summary>
        public static double PercentFromAmount(long amount, long available)
        {
            if (available <= 0 || amount <= 0) return 0;
            return Math.Min(1.0, amount / (double)available);
        }

        /// <summary>Slider fractions snap to whole percent so the label and the amount agree.</summary>
        public static double SnapPercent(double fraction) => Math.Round(Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction)) * 100.0) / 100.0;

        /// <summary>A typed or computed amount as the int the bank API takes (the API counts coins as int). Negative becomes 0, too large is clamped.</summary>
        public static int ClampToCoins(double amount) => double.IsNaN(amount) || amount <= 0 ? 0 : (amount >= int.MaxValue ? int.MaxValue : (int)amount);

        /// <summary>Whole percent for display (0..100).</summary>
        public static int WholePercent(double fraction) => (int)Math.Round(Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction)) * 100.0);
    }
}
