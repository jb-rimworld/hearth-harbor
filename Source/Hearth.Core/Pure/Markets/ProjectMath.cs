// SPDX-License-Identifier: MIT
// Pure code. Faction projects, Longs and Project Shorts (KONZEPT 8.7, S9).
using System;

namespace Hearth.Markets
{
    public static class ProjectMath
    {
        public const double BankMargin = 0.08;
        public const double LongExponent = 1.2;

        public static double Sigmoid(double x) => 1.0 / (1.0 + DetMath.Exp(-x));
        public static double Logit(double p) { p = DetMath.Clamp(p, 1e-6, 1 - 1e-6); return DetMath.Ln(p / (1 - p)); }

        /// <summary>b0 such that P = 0.5 at goodwill 0, industrial tech (4), no war, no help, no sabotage.</summary>
        public const double B0 = -2.0;

        /// <param name="goodwill">player goodwill with the faction, -100..100</param>
        /// <param name="tech">TechLevel as int (industrial = 4)</param>
        public static double SuccessLogit(double goodwill, double tech, bool atWar, double playerHelp, double sabotageStrikes)
            => B0 + 1.5 * goodwill / 100.0 + 0.5 * tech - 1.2 * (atWar ? 1 : 0) + 0.8 * playerHelp - 0.6 * sabotageStrikes;
        public static double SuccessProbability(double goodwill, double tech, bool atWar, double playerHelp, double sabotageStrikes)
            => DetMath.Clamp(Sigmoid(SuccessLogit(goodwill, tech, atWar, playerHelp, sabotageStrikes)), 0.02, 0.98);

        /// <summary>Q_long = (1 - 0.08) * M^(1.2 T/60) / P_open, fixed when the Long is bought.</summary>
        public static double LongQuote(double mPerYear, double durationDays, double pOpen)
            => (1 - BankMargin) * DetMath.Pow(Math.Max(1e-6, mPerYear), LongExponent * durationDays / MarketConst.DaysPerYear) / Math.Max(0.02, pOpen);

        public static double ShortBaseQuote(double pOpen) => (1 - BankMargin) / Math.Max(0.02, 1 - pOpen);
        /// <summary>Q(E) = 1 + (Q0 - 1) / (1 + E / E_ref): the bigger the stake the worse the quote.</summary>
        public static double ShortQuote(double pOpen, double stake, double eRef) => 1.0 + (ShortBaseQuote(pOpen) - 1.0) / (1.0 + stake / Math.Max(1.0, eRef));

        /// <summary>Project volume = 2 % of the faction wealth, at least 2000 silver. E_ref = 5 % of it.</summary>
        public static double Volume(double factionWealth) => Math.Max(2000.0, 0.02 * factionWealth);
        public static double ERef(double factionWealth) => 0.05 * Volume(factionWealth);

        // ---- limits ----------------------------------------------------------------------------
        public sealed class Limits { public double PerProjectPct = 0.10, TotalPct = 0.20, YearGainPct = 0.15; }

        public static double MaxStake(double netWorth, double projectStakeSoFar, double totalOpen, Limits l)
            => Math.Max(0, Math.Min(netWorth * l.PerProjectPct - projectStakeSoFar, netWorth * l.TotalPct - totalOpen));

        /// <summary>Annual gain cap: net gains of Longs + Shorts per game year may not exceed 15 % of the net worth at year start.
        /// Returns the part of <paramref name="payout"/> (stake + profit) that is actually paid; above the cap only the stake comes back.</summary>
        public static double CapPayout(double stake, double payout, double gainsThisYear, double netWorthAtYearStart, Limits l, out bool capped)
        {
            capped = false;
            double profit = payout - stake;
            if (profit <= 0) return payout;
            double room = Math.Max(0, netWorthAtYearStart * l.YearGainPct - gainsThisYear);
            if (profit <= room) return payout;
            capped = true;
            return stake + room;                // partial: pay what fits under the cap (cap 0 => stake only)
        }

        // ---- detection of sabotage --------------------------------------------------------------
        /// <summary>p = 0.3 + 0.15 [guards escaped] + 0.1 earlier sabotage at this project.</summary>
        public static double DetectionChance(bool guardsEscaped, int earlierStrikes) => Math.Min(0.95, 0.3 + (guardsEscaped ? 0.15 : 0) + 0.1 * earlierStrikes);

        public const int ShortCutoffDays = 3;       // no new bets in the last 3 days
        public const int SmokeLockDays = 10;        // no new Shorts for 10 days after a strike

        // ---- patience bond (Sparvertrag) ---------------------------------------------------------
        public static double PatienceMultiple(double mSpar, int termDays) => mSpar * (1.0 + 0.002 * Math.Floor(termDays / MarketConst.QuadrumDays));
        public static double PatienceValue(double principal, double lnMspar, int termDays, int heldDays)
            => principal * DetMath.Exp(Math.Min(heldDays, termDays) / MarketConst.DaysPerYear * (lnMspar + DetMath.Ln(1.0 + 0.002 * Math.Floor(termDays / MarketConst.QuadrumDays))));
        public const double PatienceLoyalty = 0.02, PatienceEarlyPenalty = 0.03;
    }
}
