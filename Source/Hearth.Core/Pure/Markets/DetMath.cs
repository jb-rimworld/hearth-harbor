// SPDX-License-Identifier: MIT
// Pure code (no game references). Deterministic maths for the finance simulation (KONZEPT 8.11):
// exp/ln/pow built only from + - * / (IEEE-exact on every platform) so multiplayer clients and savegames
// never drift apart in the last bit. Math.Sqrt is correctly rounded by IEEE 754 and therefore also safe.
using System;

namespace Hearth.Markets
{
    public static class DetMath
    {
        public const double Ln2 = 0.69314718055994530942;

        public static double Exp(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x > 700) x = 700;
            if (x < -700) return 0;
            double kd = x / Ln2;
            int k = (int)(kd >= 0 ? kd + 0.5 : kd - 0.5);
            double r = x - k * Ln2;                       // |r| <= 0.35
            double term = 1, sum = 1;
            for (int i = 1; i <= 18; i++) { term *= r / i; sum += term; }
            double scale = 1, b = k >= 0 ? 2.0 : 0.5; int n = k >= 0 ? k : -k;
            while (n > 0) { if ((n & 1) != 0) scale *= b; b *= b; n >>= 1; }
            return sum * scale;
        }

        public static double Ln(double x)
        {
            if (double.IsNaN(x) || x <= 0) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
            int e = 0;
            while (x > 1.5) { x *= 0.5; e++; }
            while (x < 0.75) { x *= 2; e--; }
            double y = (x - 1) / (x + 1), y2 = y * y, term = y, sum = 0;
            for (int i = 1; i <= 41; i += 2) { sum += term / i; term *= y2; }
            return 2 * sum + e * Ln2;
        }

        public static double Pow(double a, double b) => a <= 0 ? 0 : Exp(b * Ln(a));

        /// <summary>Standard normal by the Marsaglia polar method (no sin/cos, so bit-exact everywhere).</summary>
        public static double Gaussian(RngStream r)
        {
            for (int guard = 0; guard < 64; guard++)
            {
                double u = 2 * r.NextDouble() - 1, v = 2 * r.NextDouble() - 1, s = u * u + v * v;
                if (s >= 1 || s == 0) continue;
                return u * Math.Sqrt(-2 * Ln(s) / s);
            }
            return 0;
        }

        /// <summary>Poisson variate (Knuth, split into chunks of lambda 20 so exp(-lambda) never underflows).</summary>
        public static int Poisson(RngStream r, double lambda)
        {
            if (lambda <= 0 || double.IsNaN(lambda)) return 0;
            int chunks = (int)Math.Ceiling(lambda / 20.0); double each = lambda / chunks; int total = 0;
            double limit = Exp(-each);
            for (int c = 0; c < chunks; c++)
            {
                double p = 1; int k = 0;
                do { k++; p *= r.NextDouble(); } while (p > limit && k < 400);
                total += k - 1;
            }
            return total;
        }

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
