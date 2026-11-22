// SPDX-License-Identifier: MIT
// Pure code (no game references). Price model of Silverline (KONZEPT 8.5, 8.6, 8.8):
// geometric Brownian motion with jumps, drift-compensated so that E[S_T] = S_0 * M_a^(T/60) exactly,
// where M is the "profitability" slider (multiple per game year = 60 days) and M_a = M^beta * (1 + adjust).
using System;

namespace Hearth.Markets
{
    public static class MarketConst
    {
        public const double DaysPerYear = 60.0;
        public const double HoursPerDay = 24.0;
        public const double Dt = 1.0 / 24.0;              // one hourly step, in days
        public const double QuadrumDays = 15.0;
    }

    /// <summary>A market-wide shock (crash, rally) shared by all assets; every asset reacts with its own load.</summary>
    public sealed class SystemicSpec
    {
        public string Id; public double LambdaPerDay, Mean, Sd;
        public SystemicSpec(string id, double lambdaPerDay, double mean, double sd) { Id = id; LambdaPerDay = lambdaPerDay; Mean = mean; Sd = sd; }
        public SystemicSpec Scaled(double lambdaMult) => new SystemicSpec(Id, LambdaPerDay * lambdaMult, Mean, Sd);
    }

    /// <summary>Everything the price process needs for one asset. Filled from an AssetDef (plus settings multipliers).</summary>
    public sealed class AssetSpec
    {
        public double Beta = 1.0;            // coupling to the profitability slider (ln M_a = beta * ln M + ln(1+AnnualAdjust))
        public double AnnualAdjust = 0;      // fees (negative) or bonus (positive) per game year
        public double SigmaDay = 0.01;       // diffusion per day
        public double IdioLambda;            // own jumps per day (quarterly numbers, crypto jumps, memecoin pumps)
        public double IdioMean, IdioSd;      // log jump size ~ N(mean, sd), clamped
        public double IdioMinY = -2.5, IdioMaxY = 2.5;
        public double EventHazard;           // per day: rug pull, default, ...
        public double EventPUp;              // chance that an event is an "up" event (war bonds win)
        public double EventUpMult = 1, EventDownMult = 0.3;
        public double AlphaAnnual;           // manager alpha (log, per year), fixed per fund
        public double[] Loads;               // loads on SystemicSpec[i]; null = Beta for all
        public double LoadFor(int i) => Loads != null && i < Loads.Length ? Loads[i] : Beta;
    }

    public struct StepInfo { public double IdioY; public bool Event, EventUp; }

    public static class MarketMath
    {
        /// <summary>The market-wide shocks with their default frequencies (8.5: index crash lambda 1/120, Y ~ N(-0.25, 0.08)).</summary>
        public static SystemicSpec[] DefaultSystemic(double lambdaMult = 1.0) => new[]
        {
            new SystemicSpec("crash", 1.0 / 120.0 * lambdaMult, -0.25, 0.08),
            new SystemicSpec("rally", 1.0 / 45.0 * lambdaMult, 0.05, 0.02),
        };

        // ---- profitability slider (8.8) --------------------------------------------------------
        public static double LnMAsset(double beta, double lnM, double annualAdjust) => beta * lnM + DetMath.Ln(1.0 + annualAdjust);
        /// <summary>"Mattress Plus": M_spar = max(0.85, 1 + 0.4 (M - 1)).</summary>
        public static double SavingsMultiple(double m) => Math.Max(0.85, 1.0 + 0.4 * (m - 1.0));
        public static double RatePerDay(double lnMa) => lnMa / MarketConst.DaysPerYear;
        public static double DailyPercent(double multiplePerYear) => (DetMath.Exp(DetMath.Ln(multiplePerYear) / MarketConst.DaysPerYear) - 1.0) * 100.0;

        // ---- jump expectations -----------------------------------------------------------------
        /// <summary>E[exp(load * clamp(Y))] - 1 for Y ~ N(m, s), by Simpson quadrature (so clamped jumps compensate exactly).</summary>
        public static double JumpExcess(double load, double m, double s, double lo, double hi)
        {
            if (s <= 1e-12) return DetMath.Exp(load * DetMath.Clamp(m, lo, hi)) - 1.0;
            const int N = 240; const double Z = 7.0;
            double h = 2 * Z / N, sum = 0;
            for (int i = 0; i <= N; i++)
            {
                double z = -Z + i * h;
                double y = DetMath.Clamp(m + s * z, lo, hi);
                double f = DetMath.Exp(load * y - 0.5 * z * z) * 0.3989422804014327;
                sum += f * (i == 0 || i == N ? 1 : (i % 2 == 1 ? 4 : 2));
            }
            return sum * h / 3.0 - 1.0;
        }

        public static double EventExcess(AssetSpec a) => a.EventPUp * (a.EventUpMult - 1.0) + (1.0 - a.EventPUp) * (a.EventDownMult - 1.0);

        /// <summary>
        /// Log drift per day that makes E[S_T] follow exp(rate * T) exactly: r + alpha/60 - sigma^2/2 - sum of jump compensators.
        /// Compute once per (asset, settings) and cache; it is the expensive part.
        /// </summary>
        public static double DriftPerDay(AssetSpec a, double lnMa, SystemicSpec[] sys)
        {
            double d = RatePerDay(lnMa) + a.AlphaAnnual / MarketConst.DaysPerYear - 0.5 * a.SigmaDay * a.SigmaDay;
            if (sys != null)
                for (int i = 0; i < sys.Length; i++)
                    d -= sys[i].LambdaPerDay * JumpExcess(a.LoadFor(i), sys[i].Mean, sys[i].Sd, -3.0, 3.0);
            if (a.IdioLambda > 0) d -= a.IdioLambda * JumpExcess(1.0, a.IdioMean, a.IdioSd, a.IdioMinY, a.IdioMaxY);
            if (a.EventHazard > 0) d -= a.EventHazard * EventExcess(a);
            return d;
        }

        // ---- one step --------------------------------------------------------------------------
        /// <param name="sysY">realised systemic log shocks this step (0 = none), parallel to the SystemicSpec array</param>
        /// <param name="signalDrift">system B coupling (8.6), log drift per day, already capped to +-0.5 sigma</param>
        public static double Step(double price, AssetSpec a, double driftPerDay, double dt, RngStream rng, double[] sysY, double signalDrift, out StepInfo info)
        {
            info = default;
            double lnChange = (driftPerDay + signalDrift) * dt;
            if (a.SigmaDay > 0) lnChange += a.SigmaDay * Math.Sqrt(dt) * DetMath.Gaussian(rng);
            if (sysY != null)
                for (int i = 0; i < sysY.Length; i++) if (sysY[i] != 0) lnChange += a.LoadFor(i) * sysY[i];
            if (a.IdioLambda > 0 && rng.NextDouble() < 1.0 - DetMath.Exp(-a.IdioLambda * dt))
            {
                double y = DetMath.Clamp(a.IdioMean + a.IdioSd * DetMath.Gaussian(rng), a.IdioMinY, a.IdioMaxY);
                lnChange += y; info.IdioY = y;
            }
            if (a.EventHazard > 0 && rng.NextDouble() < 1.0 - DetMath.Exp(-a.EventHazard * dt))
            {
                bool up = a.EventPUp > 0 && rng.NextDouble() < a.EventPUp;
                lnChange += DetMath.Ln(up ? a.EventUpMult : a.EventDownMult);
                info.Event = true; info.EventUp = up;
            }
            double next = price * DetMath.Exp(lnChange);
            if (double.IsNaN(next) || double.IsInfinity(next)) return price;
            return Math.Max(1e-9, Math.Min(1e12, next));
        }

        /// <summary>Cap of the system-B drift: +-0.5 sigma per day (8.6). The player shifts timing and selection, never the long-run mean.</summary>
        public static double CapSignal(double drift, double sigmaDay)
        {
            double cap = 0.5 * Math.Max(sigmaDay, 0.002);
            return DetMath.Clamp(drift, -cap, cap);
        }

        // ---- distribution helpers (nerd view "distribution after 3 days") -------------------------
        public sealed class Distribution { public double Mean, Median, P90, ShareTotalLoss, ShareBelowStart; }

        /// <summary>Monte-Carlo distribution of the price multiple after <paramref name="days"/> days (local generator, not the sealed stream).</summary>
        public static Distribution Simulate(AssetSpec a, double lnMa, SystemicSpec[] sys, double days, int paths, ulong seed, double lossThreshold = 0.05)
        {
            double drift = DriftPerDay(a, lnMa, sys);
            var rng = new RngStream(seed, 0xD157, 0);
            int steps = (int)Math.Round(days * MarketConst.HoursPerDay);
            var finals = new double[paths]; double sum = 0; int loss = 0, below = 0;
            var sysY = new double[sys?.Length ?? 0];
            for (int p = 0; p < paths; p++)
            {
                double s = 1.0;
                for (int i = 0; i < steps; i++)
                {
                    for (int k = 0; k < sysY.Length; k++) sysY[k] = rng.NextDouble() < 1 - DetMath.Exp(-sys[k].LambdaPerDay * MarketConst.Dt) ? sys[k].Mean + sys[k].Sd * DetMath.Gaussian(rng) : 0;
                    s = Step(s, a, drift, MarketConst.Dt, rng, sysY, 0, out _);
                }
                finals[p] = s; sum += s; if (s < lossThreshold) loss++; if (s < 1) below++;
            }
            Array.Sort(finals);
            return new Distribution { Mean = sum / paths, Median = finals[paths / 2], P90 = finals[Math.Min(paths - 1, (int)(paths * 0.9))], ShareTotalLoss = loss / (double)paths, ShareBelowStart = below / (double)paths };
        }

        // ---- holdings math (5 decimals) ---------------------------------------------------------
        public const long UnitsPerOne = 100_000L;        // units are held with 5 decimals, like Money
        /// <summary>Units bought for an amount of micro-silver at a price (silver per unit), rounded down to 5 decimals.</summary>
        public static long UnitsFor(long micro, double price) => price <= 0 ? 0 : (long)Math.Floor(micro / price + 1e-9);
        /// <summary>Micro-silver value of units at a price, rounded to the nearest micro-silver.</summary>
        public static long ValueOf(long unitsMicro, double price) => (long)Math.Round(unitsMicro * price, MidpointRounding.AwayFromZero);
    }
}
