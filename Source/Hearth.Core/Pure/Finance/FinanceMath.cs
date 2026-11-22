// SPDX-License-Identifier: MIT
// Pure finance maths of the bank (no game references): compounding, loans, scores, collection ladder, wealth tiers.
// Compiled into Hearth.Core and into the unit tests (Source/Tests/FinanceTests.cs).
using System;

namespace Hearth
{
    /// <summary>
    /// Deterministic exp/ln/pow built only from + - * / (8.11: Math.Exp/Log/Pow may differ in the last bit between
    /// macOS-arm64 and Windows-x64, which would desync multiplayer). Accurate to ~1e-13 relative error in the used range.
    /// </summary>
    public static class FinDetMath
    {
        const double Ln2 = 0.6931471805599453094172321215;

        public static double Ln(double x)
        {
            if (!(x > 0)) return double.NegativeInfinity;
            int k = 0;
            while (x >= 1.5) { x *= 0.5; k++; }
            while (x < 0.75) { x *= 2.0; k--; }
            double z = (x - 1.0) / (x + 1.0), z2 = z * z, term = z, sum = 0;
            for (int n = 1; n <= 61; n += 2) { sum += term / n; term *= z2; }
            return 2.0 * sum + k * Ln2;
        }

        public static double Exp(double y)
        {
            if (y > 700) return double.PositiveInfinity;
            if (y < -700) return 0;
            double kf = Math.Floor(y / Ln2 + 0.5);
            int k = (int)kf;
            double r = y - kf * Ln2, term = 1, sum = 1;
            for (int n = 1; n <= 24; n++) { term *= r / n; sum += term; }
            while (k > 0) { sum *= 2.0; k--; }
            while (k < 0) { sum *= 0.5; k++; }
            return sum;
        }

        /// <summary>x^y for x &gt; 0; x = 0 gives 0 (for y &gt; 0).</summary>
        public static double Pow(double x, double y)
        {
            if (x <= 0) return y > 0 ? 0 : (y == 0 ? 1 : double.PositiveInfinity);
            if (y == 0) return 1;
            return Exp(y * Ln(x));
        }
    }

    public static class FinanceMath
    {
        /// <summary>One game year = 60 days (D17).</summary>
        public const int DaysPerYear = 60;
        public const double MinLoanBase = 0.02;

        // --- yields -------------------------------------------------------------------------------------
        /// <summary>Daily growth factor for a yearly multiple M (1.2 → +20 % per game year → M^(1/60) per day).</summary>
        public static double DailyFactor(double yearlyMultiple) => yearlyMultiple <= 0 ? 0 : FinDetMath.Pow(yearlyMultiple, 1.0 / DaysPerYear);

        /// <summary>Yearly multiple of the account: ln M_acc = beta · bankMod · ln M (M = profitability slider).</summary>
        public static double AccountYearlyMultiple(double m, double beta, double bankMod)
            => m <= 0 ? 0 : FinDetMath.Pow(m, beta * bankMod);

        public static double AccountDailyFactor(double m, double beta, double bankMod) => DailyFactor(AccountYearlyMultiple(m, beta, bankMod));

        /// <summary>Interest (can be negative when M &lt; 1) of one day on a positive balance.</summary>
        public static Money InterestForDay(Money balance, double dailyFactor)
            => balance.Micro <= 0 ? Money.Zero : balance * (dailyFactor - 1.0);

        /// <summary>Compound a balance over whole days, one rounding step per day (as the game does it).</summary>
        public static Money Compound(Money balance, double dailyFactor, int days)
        {
            for (int i = 0; i < days; i++) balance += InterestForDay(balance, dailyFactor);
            return balance;
        }

        /// <summary>The "1,000 silver → 1,200 after one game year" preview of the slider (8.8).</summary>
        public static double GrowthAfterYears(double startSilver, double m, double years) => startSilver * FinDetMath.Pow(m <= 0 ? 0 : m, years);

        /// <summary>Linear interpolation of 7 tier anchors in log space (Bank M, research cost, …). Anchors must be positive.</summary>
        public static double LogInterp(double[] anchors, double tier)
        {
            tier = Math.Max(0, Math.Min(anchors.Length - 1, tier));
            int i = (int)Math.Floor(tier); if (i >= anchors.Length - 1) return anchors[anchors.Length - 1];
            double t = tier - i;
            return FinDetMath.Exp(FinDetMath.Ln(anchors[i]) + (FinDetMath.Ln(anchors[i + 1]) - FinDetMath.Ln(anchors[i])) * t);
        }

        // --- loans --------------------------------------------------------------------------------------
        /// <summary>Base rate of a loan per game year: max(2 %, M − 1) — "borrow → invest" never has a positive expectation (8.9).</summary>
        public static double LoanBaseRate(double m) => Math.Max(MinLoanBase, m - 1.0);

        /// <summary>Yearly rate of a product: base rate + spread (e.g. 0.12 for the private loan).</summary>
        public static double LoanYearlyRate(double m, double spread) => LoanBaseRate(m) + spread;

        /// <summary>Growth factor of a debt over <paramref name="days"/> days at the yearly rate (compounded per game year).</summary>
        public static double LoanFactor(double yearlyRate, double days) => FinDetMath.Pow(1.0 + yearlyRate, days / DaysPerYear);

        public static int InstallmentCount(int termDays, int intervalDays) => Math.Max(1, (int)Math.Ceiling(termDays / (double)Math.Max(1, intervalDays)));

        /// <summary>Equal payment per instalment (annuity) in silver; the last instalment of a schedule is the remaining debt.</summary>
        public static double AnnuityPayment(double principalSilver, double yearlyRate, int termDays, int intervalDays)
        {
            int n = InstallmentCount(termDays, intervalDays);
            double g = LoanFactor(yearlyRate, intervalDays);
            if (g <= 1.0 + 1e-12) return principalSilver / n;
            double gn = FinDetMath.Pow(g, n);
            return principalSilver * gn * (g - 1.0) / (gn - 1.0);
        }

        /// <summary>Debt after one interval of interest (before payment).</summary>
        public static Money AccrueDebt(Money debt, double yearlyRate, int intervalDays) => debt * LoanFactor(yearlyRate, intervalDays);

        // --- score and limits ---------------------------------------------------------------------------
        public const int ScoreMin = 300, ScoreMax = 850, ScoreStart = 600;
        public static int ClampScore(double s) => (int)Math.Max(ScoreMin, Math.Min(ScoreMax, Math.Round(s)));
        public const int ScoreOnTime = 4, ScoreLateDay = -6, ScoreDefault = -120, ScoreLateStage = -30, ScoreHighUtilisation = -20;

        /// <summary>+20·log10(wealth/10 000), zero below 1 000 silver (never a punishment for a young colony).</summary>
        public static double WealthScoreBonus(double wealthSilver) => wealthSilver < 1000 ? 0 : 20.0 * FinDetMath.Ln(wealthSilver / 10000.0) / 2.302585092994046;

        /// <summary>Private loan limit: 0.15 · wealth · (score − 300) / 550, never negative.</summary>
        public static double CreditLimit(double wealthSilver, int score)
            => Math.Max(0, 0.15 * Math.Max(0, wealthSilver) * (Math.Max(ScoreMin, Math.Min(ScoreMax, score)) - ScoreMin) / 550.0);

        /// <summary>Leverage cap: debts ≤ 30 % of the net worth.</summary>
        public static double LeverageCap(double netWorthSilver) => 0.30 * Math.Max(0, netWorthSilver);

        // --- collection ladder (8.9) --------------------------------------------------------------------
        /// <summary>Days overdue at which stage 1…7 starts.</summary>
        public static readonly int[] StageStartDays = { 1, 4, 7, 10, 15, 20, 30 };

        /// <summary>Stage (0 = none … 7) for a number of days overdue, capped by the difficulty (9.2 "höchste Inkasso-Stufe").</summary>
        public static int CollectionStage(int daysOverdue, int maxStage)
        {
            int stage = 0;
            for (int i = 0; i < StageStartDays.Length; i++) if (daysOverdue >= StageStartDays[i]) stage = i + 1;
            return Math.Max(0, Math.Min(stage, Math.Max(0, Math.Min(7, maxStage))));
        }

        /// <summary>Late fee of stage 2: 5 % of the overdue instalment.</summary>
        public static Money LateFee(Money overdue) => overdue * 0.05;

        /// <summary>Raid points of the collection raid: min(debt · 0.5, storyteller points · 1.5) · anger (by debt, not by wealth).</summary>
        public static double CollectionRaidPoints(double debtSilver, double storytellerPoints, double anger)
            => Math.Max(0, Math.Min(debtSilver * 0.5, storytellerPoints * 1.5) * Math.Max(0, anger));

        /// <summary>Value the seizure team takes: 120 % of the debt.</summary>
        public static double SeizureValue(double debtSilver) => debtSilver * 1.2;

        // --- wealth tiers and mood (8.4) ----------------------------------------------------------------
        public static readonly double[] TierLimits = { -5000, 0, 500, 2000, 10000, 50000, 250000, 1000000 };
        public static readonly int[] TierMood = { -4, -2, -1, 0, 0, 1, 2, 3, 3 };
        public static readonly string[] TierNamesEn = { "Drowning in Debt", "Broke", "Scraping By", "Getting By", "Comfortable", "Well-Off", "Rich", "Filthy Rich", "Too Big To Fail" };

        /// <summary>Tier 0…8 of a net worth. <paramref name="scale"/> scales all thresholds (slider, default 1).</summary>
        public static int WealthTier(double netWorthSilver, double scale = 1.0)
        {
            scale = scale <= 0 ? 1 : scale;
            for (int i = 0; i < TierLimits.Length; i++) if (netWorthSilver < TierLimits[i] * scale) return i;
            return TierLimits.Length;
        }

        /// <summary>The tier only changes after the new value held for a whole day: returns the tier to show now.</summary>
        public static int StableTier(int shown, int candidate, int candidateSinceTick, int nowTick, int holdTicks = 60000)
            => candidate == shown ? shown : (nowTick - candidateSinceTick >= holdTicks ? candidate : shown);

        // --- bailout ------------------------------------------------------------------------------------
        /// <summary>First Rim Bank of Too Big To Fail (2.2): interest-free loan over the debt, at most 10 % of the best net worth of the last 60 days.</summary>
        public static double BailoutAmount(double netWorthSilver, double peakNetWorthSilver)
            => netWorthSilver >= 0 ? 0 : Math.Min(-netWorthSilver, 0.10 * Math.Max(0, peakNetWorthSilver));

        // --- fees ---------------------------------------------------------------------------------------
        /// <summary>Fee = max(min, pct · amount) + flat.</summary>
        public static Money Fee(Money amount, double pct, double minSilver, double flatSilver)
            => Money.Max(amount * pct, Money.FromSilver(minSilver)) + Money.FromSilver(flatSilver);
    }
}
