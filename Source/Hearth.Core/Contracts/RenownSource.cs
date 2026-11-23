// SPDX-License-Identifier: MIT
using System;

namespace Hearth
{
    /// <summary>
    /// The colony's renown (0-100) for code that must not reference Hearth.dll (Silverline, the standalone bank).
    /// The Inn module sets <see cref="Getter"/>; without it the colony counts as neutral (50), so nothing is blocked.
    /// </summary>
    public static class RenownSource
    {
        public const float Neutral = 50f;
        public static Func<float> Getter;

        public static float Current
        {
            get
            {
                try { var g = Getter; if (g == null) return Neutral; float v = g(); return v < 0f ? 0f : v > 100f ? 100f : v; }
                catch (Exception e) { HLog.Once("core.renown", LogLevel.Warn, "Core", "Renown getter failed: " + e.Message); return Neutral; }
            }
        }
    }
}
