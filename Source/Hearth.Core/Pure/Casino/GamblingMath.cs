// SPDX-License-Identifier: MIT
// Pure code (no game references).
using System;
using System.Collections.Generic;

namespace Hearth.CasinoMath
{
    /// <summary>Addiction model of colonists who gamble (KONZEPT 6.12). All numbers are the Standard Room starting values.</summary>
    public static class GamblingMath
    {
        public const int WindowTicks = 3 * 60000;
        public const int FreeSessions = 3;       // below this many sessions in the window nothing can happen
        public const double Growth = 1.35;
        public const double MaxChance = 0.9;
        public const double SessionFill = 0.35;
        public const double DepthPerSession = 0.015;
        public const double DepthRecoveryPerDay = 0.01;
        public const double DepthPerStage = 0.2;
        public const int StageMax = 5;
        public const int CravingDays = 60;
        public const double RelapseChance = 0.5;

        /// <summary>Chance of becoming addicted after a session, given sessions in the last three days (including this one).</summary>
        public static double AddictionChance(int sessionsInWindow, double p0, double factor, double growth = Growth, int freeSessions = FreeSessions)
        {
            if (sessionsInWindow < freeSessions || p0 <= 0 || factor <= 0) return 0;
            double p = p0 * Math.Pow(growth, sessionsInWindow - freeSessions) * factor;
            return Math.Min(MaxChance, p);
        }

        public static int StageFromDepth(double depth)
        {
            int s = 1 + (int)Math.Floor(Math.Max(0, depth) / DepthPerStage + 1e-9);
            return Math.Max(1, Math.Min(StageMax, s));
        }

        /// <summary>How much the need falls per day at this stage: 0.5 · 1.5^(stage−1).</summary>
        public static double NeedFallPerDay(int stage) => 0.5 * Math.Pow(1.5, Math.Max(1, Math.Min(StageMax, stage)) - 1);

        /// <summary>Sessions per day the addict needs to keep the need up.</summary>
        public static double SessionsNeededPerDay(int stage) => NeedFallPerDay(stage) / SessionFill;

        public static readonly int[] WithdrawalMood = { 10, 15, 22, 30, 40 };
        public static readonly int[] BreakThresholdOffsetPercent = { 5, 8, 12, 16, 20 };
        public static int MoodPenalty(int stage) => WithdrawalMood[Math.Max(1, Math.Min(StageMax, stage)) - 1];
        public static int BreakOffset(int stage) => BreakThresholdOffsetPercent[Math.Max(1, Math.Min(StageMax, stage)) - 1];

        /// <summary>Days of abstinence that cure an addiction of this stage (10 · stage on Standard Room).</summary>
        public static double AbstinenceDaysNeeded(int stage, double daysPerStage) => daysPerStage * Math.Max(1, Math.Min(StageMax, stage));

        /// <summary>Mean days between spree breakdowns while in withdrawal (0 = never).</summary>
        public static double SpreeMtbDays(int stage) => stage >= 5 ? 1.5 : stage >= 4 ? 3.0 : 0;

        /// <summary>Sessions of the same sliding window.</summary>
        public sealed class SessionWindow
        {
            readonly List<int> ticks = new List<int>();
            public IReadOnlyList<int> Ticks => ticks;
            public void Add(int tick) { ticks.Add(tick); }
            public int Count(int now)
            {
                ticks.RemoveAll(t => now - t > WindowTicks);
                return ticks.Count;
            }
            public void Load(IEnumerable<int> t) { ticks.Clear(); ticks.AddRange(t); }
        }
    }

    /// <summary>The house never goes broke on a bad night: the maximum bet follows the cash on hand (KONZEPT 6.6).</summary>
    public static class HouseRisk
    {
        public const double Z = 4.0;

        /// <summary>Highest stake: cash / (z · σ · √n · √devices), with σ per stake, n rounds per day, devices sharing the cash.</summary>
        public static double MaxBet(double cash, double sigmaPerStake, double roundsPerDay, int devicesSharingCash)
        {
            if (cash <= 0 || sigmaPerStake <= 0) return 0;
            double n = Math.Max(1, roundsPerDay), d = Math.Max(1, devicesSharingCash);
            return cash / (Z * sigmaPerStake * Math.Sqrt(n) * Math.Sqrt(d));
        }

        /// <summary>
        /// Monte-Carlo estimate of the chance that the house cash goes below zero within <paramref name="days"/> days, when each
        /// day brings n rounds at the max bet (a normal approximation of the day's result is used for speed).
        /// </summary>
        public static double RuinProbability(RngStream rng, double cash, double edge, double sigmaPerStake, double roundsPerDay, double stake, int days, int trials)
        {
            int ruined = 0;
            double mean = edge * stake * roundsPerDay, sd = sigmaPerStake * stake * Math.Sqrt(roundsPerDay);
            for (int t = 0; t < trials; t++)
            {
                double c = cash;
                for (int d = 0; d < days; d++) { c += rng.Gaussian(mean, sd); if (c < 0) { ruined++; break; } }
            }
            return trials == 0 ? 0 : (double)ruined / trials;
        }

        /// <summary>
        /// Same as <see cref="RuinProbability"/> but the stake follows the rule of the game: every day the max bet is recomputed from the cash on hand,
        /// so a bad day automatically lowers the limit. This is the policy the casino really uses.
        /// </summary>
        public static double RuinProbabilityAdaptive(RngStream rng, double cash, double edge, double sigmaPerStake, double roundsPerDay, int days, int trials, int devices = 1)
        {
            int ruined = 0;
            for (int t = 0; t < trials; t++)
            {
                double c = cash;
                for (int d = 0; d < days; d++)
                {
                    double stake = MaxBet(c, sigmaPerStake, roundsPerDay, devices);
                    c += rng.Gaussian(edge * stake * roundsPerDay, sigmaPerStake * stake * Math.Sqrt(roundsPerDay));
                    if (c <= 0) { ruined++; break; }
                }
            }
            return trials == 0 ? 0 : (double)ruined / trials;
        }
    }
}
