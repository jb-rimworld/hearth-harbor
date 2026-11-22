// SPDX-License-Identifier: MIT
// Pure code. Omen Exchange (KONZEPT 8.6): binary prediction contracts on events the player cannot influence (D52).
using System;

namespace Hearth.Markets
{
    public static class OmenMath
    {
        public const double DefaultGamma = 0.8;       // outsider distortion: longshots are overpriced, favourites underpriced
        public const double DefaultFee = 0.02;        // 2 % of the stake
        public const double MinQuote = 0.02, MaxQuote = 0.98;

        /// <summary>Probability that at least one event of a Poisson process (per day) happens within <paramref name="days"/>.</summary>
        public static double EventProbability(double lambdaPerDay, double days) => lambdaPerDay <= 0 || days <= 0 ? 0 : 1.0 - DetMath.Exp(-lambdaPerDay * days);

        /// <summary>w(p) = p^g / (p^g + (1-p)^g).</summary>
        public static double Distort(double p, double gamma)
        {
            p = DetMath.Clamp(p, 1e-9, 1 - 1e-9);
            double a = DetMath.Pow(p, gamma), b = DetMath.Pow(1 - p, gamma);
            return a / (a + b);
        }

        public const double DefaultSpread = 0.05;      // bookmaker overround: YES ask + NO ask = 1 + spread
        public const double LogitNoiseSd = 0.25;       // about +-0.06 at q = 0.5, much smaller near 0 and 1 (no cheap 2 % longshots by luck)

        static double Logit(double p) { p = DetMath.Clamp(p, 1e-6, 1 - 1e-6); return DetMath.Ln(p / (1 - p)); }
        static double Sigmoid(double x) => 1.0 / (1.0 + DetMath.Exp(-x));

        /// <summary>Mid quote of YES: q = sigmoid(logit(w(p)) + noise), kept inside [0.02, 0.98]. <paramref name="noise"/> is in logit space.</summary>
        public static double Quote(double p, double gamma, double noise) => DetMath.Clamp(Sigmoid(Logit(Distort(p, gamma)) + noise), MinQuote, MaxQuote);
        public static double AskYes(double q, double spread = DefaultSpread) => Math.Min(0.99, q + spread / 2);
        public static double AskNo(double q, double spread = DefaultSpread) => Math.Min(0.99, 1 - q + spread / 2);
        public static double BidYes(double q, double spread = DefaultSpread) => Math.Max(0.01, q - spread / 2);
        public static double BidNo(double q, double spread = DefaultSpread) => Math.Max(0.01, 1 - q - spread / 2);

        /// <summary>Noise (logit space) for (contract, day): a deterministic function of the key so quotes can be recomputed after loading.</summary>
        public static double Noise(ulong salt, ulong contractKey, int day, double sd = LogitNoiseSd)
        {
            var r = new RngStream(salt, RngStream.Mix(contractKey ^ 0x0E0E), (ulong)Math.Max(0, day) * 4UL);
            return DetMath.Clamp(sd * DetMath.Gaussian(r), -2.5 * sd, 2.5 * sd);
        }

        /// <summary>Shares bought with a stake (fee included in the stake): shares = stake / ((1+fee) * price). One share pays 1 silver.</summary>
        public static double SharesFor(double stake, double sidePrice, double fee) => sidePrice <= 0 ? 0 : stake / ((1 + fee) * sidePrice);
        public static double FeeOf(double stake, double fee) => stake * fee / (1 + fee);
        /// <summary>Proceeds of closing a position early at the current side price (fee again).</summary>
        public static double CloseValue(double shares, double sidePrice, double fee) => shares * sidePrice * (1 - fee);

        /// <summary>Expected profit per silver staked when buying the YES side at the mid quote q (ask = q + spread/2), given the true p.</summary>
        public static double ExpectedReturnYes(double p, double q, double fee, double spread = DefaultSpread) => p / ((1 + fee) * AskYes(q, spread)) - 1.0;
        public static double ExpectedReturnNo(double p, double q, double fee, double spread = DefaultSpread) => (1 - p) / ((1 + fee) * AskNo(q, spread)) - 1.0;

        /// <summary>Position cap of the exchange for world-event markets (B): 5 % of net worth.</summary>
        public static double PositionCap(double netWorth, double pct) => Math.Max(0, netWorth * pct);
    }
}
