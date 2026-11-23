// SPDX-License-Identifier: MIT
namespace Hearth
{
    /// <summary>Special switch core.perfMode: rare background jobs of the guest modules run half as often (longer rare-tick intervals).</summary>
    public static class PerfMode
    {
        public static bool On => CoreSettings.Switch("core.perfMode");

        /// <summary>The interval to use for a rare job that normally runs every <paramref name="baseTicks"/> ticks (doubled in performance mode).
        /// Use it as <c>t % PerfMode.Interval(250) == offset</c> with the offset below the base interval: the slow rounds are a subset of the normal ones.</summary>
        public static int Interval(int baseTicks) => On ? baseTicks * 2 : baseTicks;
    }
}
