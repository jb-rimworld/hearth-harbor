// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
using System;
using System.Collections.Generic;

namespace Hearth.CasinoMath
{
    public enum SlotVolatility { Low = 0, Medium = 1, High = 2 }

    /// <summary>
    /// "One-Armed Muffalo": three reels, one payline, weighted symbols (KONZEPT 6.6). The symbols are things of the rim (1.1.0, F30):
    /// berries, corn, a packaged survival meal, an industrial component, plasteel, an AI persona core and the thrumbo, the jackpot.
    /// Index 0 (berries) keeps the special rules of the old cherry: a pair on reels 1+2 and a single on reel 1 pay small amounts.
    /// A paytable is a base table of integer multipliers plus a scale factor that makes the exact return-to-player
    /// (computed by full enumeration of all 7³ outcomes) hit the requested target. Volatility variants differ in
    /// symbol weights and pay structure, not in RTP.
    /// </summary>
    public sealed class SlotPaytable
    {
        public static readonly string[] SymbolNames = { "berries", "corn", "meal", "component", "plasteel", "core", "thrumbo" };
        public const int Symbols = 7;

        public readonly SlotVolatility Volatility;
        readonly int[] weights;
        readonly double[] pay3;      // base multiplier for three of a kind, per symbol
        readonly double cherry1;     // berries on reel 1 only (not a pair)
        readonly double cherry2;     // berries on reels 1 and 2 (third reel not berries)
        public readonly double Scale;
        readonly int totalWeight;
        readonly int[] cumulative;

        public int[] Weights => (int[])weights.Clone();
        public int TotalWeight => totalWeight;

        SlotPaytable(SlotVolatility v, int[] w, double[] p3, double c1, double c2, double scale)
        {
            Volatility = v; weights = w; pay3 = p3; cherry1 = c1; cherry2 = c2; Scale = scale;
            cumulative = new int[w.Length]; int s = 0;
            for (int i = 0; i < w.Length; i++) { s += w[i]; cumulative[i] = s; }
            totalWeight = s;
        }

        static void BaseTable(SlotVolatility v, out int[] w, out double[] p3, out double c1, out double c2)
        {
            switch (v)
            {
                case SlotVolatility.Low:
                    w = new[] { 30, 18, 12, 8, 4, 2, 1 }; p3 = new double[] { 4, 6, 10, 16, 30, 60, 150 }; c1 = 1; c2 = 2; break;
                case SlotVolatility.High:
                    w = new[] { 26, 22, 12, 6, 3, 2, 1 }; p3 = new double[] { 8, 14, 30, 80, 300, 1200, 6000 }; c1 = 0; c2 = 0; break;
                default:
                    w = new[] { 22, 18, 14, 8, 4, 2, 1 }; p3 = new double[] { 6, 10, 16, 30, 80, 200, 800 }; c1 = 1; c2 = 3; break;
            }
        }

        /// <summary>Paytable of the given volatility whose exact RTP equals <paramref name="targetRtp"/> (0.5 … 1.5).</summary>
        public static SlotPaytable For(SlotVolatility v, double targetRtp)
        {
            BaseTable(v, out var w, out var p3, out var c1, out var c2);
            var unscaled = new SlotPaytable(v, w, p3, c1, c2, 1.0);
            double baseRtp = unscaled.Rtp;
            return new SlotPaytable(v, w, p3, c1, c2, targetRtp / baseRtp);
        }

        /// <summary>Multiplier of the stake that is paid (stake itself included) for the symbols a, b, c.</summary>
        public double Multiplier(int a, int b, int c) => BaseMultiplier(a, b, c) * Scale;

        double BaseMultiplier(int a, int b, int c)
        {
            if (a == b && b == c) return pay3[a];
            if (a == 0 && b == 0) return cherry2;
            if (a == 0) return cherry1;
            return 0;
        }

        public double Probability(int a, int b, int c)
            => (double)weights[a] * weights[b] * weights[c] / ((double)totalWeight * totalWeight * totalWeight);

        /// <summary>Exact return-to-player by enumeration of all outcomes.</summary>
        public double Rtp
        {
            get
            {
                double r = 0;
                for (int a = 0; a < Symbols; a++) for (int b = 0; b < Symbols; b++) for (int c = 0; c < Symbols; c++)
                    r += Probability(a, b, c) * Multiplier(a, b, c);
                return r;
            }
        }

        /// <summary>Probability that a spin pays anything.</summary>
        public double HitRate
        {
            get
            {
                double r = 0;
                for (int a = 0; a < Symbols; a++) for (int b = 0; b < Symbols; b++) for (int c = 0; c < Symbols; c++)
                    if (Multiplier(a, b, c) > 0) r += Probability(a, b, c);
                return r;
            }
        }

        /// <summary>Standard deviation of the net result of one spin, in stakes.</summary>
        public double Sigma
        {
            get
            {
                double m1 = 0, m2 = 0;
                for (int a = 0; a < Symbols; a++) for (int b = 0; b < Symbols; b++) for (int c = 0; c < Symbols; c++)
                { double p = Probability(a, b, c), m = Multiplier(a, b, c); m1 += p * m; m2 += p * m * m; }
                return Math.Sqrt(Math.Max(0, m2 - m1 * m1));
            }
        }

        public double MaxMultiplier
        {
            get { double mx = 0; for (int a = 0; a < Symbols; a++) mx = Math.Max(mx, pay3[a]); return mx * Scale; }
        }

        /// <summary>Kind of a paytable row: three of a kind, a pair of berries on reels 1+2, berries on reel 1.</summary>
        public enum RowKind { Three = 0, FirstTwo = 1, FirstOne = 2 }

        /// <summary>Paytable rows by symbol and kind (top prize first): what the window shows and what the near-miss and win flash look up.</summary>
        public IEnumerable<(int symbol, RowKind kind, double multiplier, double probability)> PayRows()
        {
            for (int s = Symbols - 1; s >= 0; s--)
                yield return (s, RowKind.Three, pay3[s] * Scale, Probability(s, s, s));
            if (cherry2 > 0)
            {
                double p = 0; for (int c = 1; c < Symbols; c++) p += Probability(0, 0, c);
                yield return (0, RowKind.FirstTwo, cherry2 * Scale, p);
            }
            if (cherry1 > 0)
            {
                double p = 0; for (int b = 1; b < Symbols; b++) for (int c = 0; c < Symbols; c++) p += Probability(0, b, c);
                yield return (0, RowKind.FirstOne, cherry1 * Scale, p);
            }
        }

        /// <summary>Which row pays for the symbols a, b, c (null when nothing pays).</summary>
        public RowKind? PayingRow(int a, int b, int c)
        {
            if (a == b && b == c) return RowKind.Three;
            if (a == 0 && b == 0 && cherry2 > 0) return RowKind.FirstTwo;
            if (a == 0 && cherry1 > 0) return RowKind.FirstOne;
            return null;
        }

        /// <summary>Rows for the Pit Boss / paytable display: (label, multiplier of stake, probability). Labels are plain English ids.</summary>
        public IEnumerable<(string label, double multiplier, double probability)> Rows()
        {
            foreach (var r in PayRows())
                yield return (r.kind == RowKind.Three ? "3 × " + SymbolNames[r.symbol] : r.kind == RowKind.FirstTwo ? "2 × " + SymbolNames[0] + " (reels 1+2)" : SymbolNames[0] + " on reel 1", r.multiplier, r.probability);
        }

        public int DrawSymbol(RngStream rng)
        {
            int r = rng.Range(0, totalWeight);
            for (int i = 0; i < cumulative.Length; i++) if (r < cumulative[i]) return i;
            return cumulative.Length - 1;
        }

        /// <summary>One spin. Returns the three symbols and the multiplier of the stake that is paid back.</summary>
        public double Spin(RngStream rng, out int a, out int b, out int c)
        {
            a = DrawSymbol(rng); b = DrawSymbol(rng); c = DrawSymbol(rng);
            return Multiplier(a, b, c);
        }
    }
}
