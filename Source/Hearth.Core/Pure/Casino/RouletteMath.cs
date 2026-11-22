// SPDX-License-Identifier: MIT
// Pure code (no game references).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hearth.CasinoMath
{
    public enum RouletteBetKind { Straight, Split, Street, Corner, SixLine, Column, Dozen, Red, Black, Odd, Even, Low, High }

    /// <summary>A bet on a set of pockets. Pocket 0 is the zero, 37 is the double zero (only on American wheels).</summary>
    public sealed class RouletteBet
    {
        public readonly RouletteBetKind Kind;
        public readonly int[] Pockets;
        public readonly string Label;
        /// <summary>Payout odds N:1 (a winning stake returns N+1 stakes before the house scale).</summary>
        public int Odds => Kind switch
        {
            RouletteBetKind.Straight => 35, RouletteBetKind.Split => 17, RouletteBetKind.Street => 11, RouletteBetKind.Corner => 8,
            RouletteBetKind.SixLine => 5, RouletteBetKind.Column => 2, RouletteBetKind.Dozen => 2, _ => 1
        };
        public bool IsEvenMoney => Odds == 1;

        RouletteBet(RouletteBetKind k, string label, params int[] pockets) { Kind = k; Label = label; Pockets = pockets; }
        public bool Covers(int pocket) => Array.IndexOf(Pockets, pocket) >= 0;

        static readonly HashSet<int> reds = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        public static bool IsRed(int n) => reds.Contains(n);
        public static string PocketName(int n) => n == 37 ? "00" : n.ToString();

        public static RouletteBet Straight(int n)
        { if (n < 0 || n > 37) throw new ArgumentOutOfRangeException(nameof(n)); return new RouletteBet(RouletteBetKind.Straight, "Straight " + PocketName(n), n); }
        public static RouletteBet Split(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            bool zeroSplit = lo == 0 && hi >= 1 && hi <= 3;
            bool horizontal = hi - lo == 1 && lo >= 1 && (lo - 1) / 3 == (hi - 1) / 3;
            bool vertical = hi - lo == 3 && lo >= 1 && hi <= 36;
            if (!zeroSplit && !horizontal && !vertical) throw new ArgumentException("not adjacent: " + a + "/" + b);
            return new RouletteBet(RouletteBetKind.Split, $"Split {lo}/{hi}", lo, hi);
        }
        /// <summary>Row 1..12 of the layout: numbers 3r-2 … 3r.</summary>
        public static RouletteBet Street(int row)
        { if (row < 1 || row > 12) throw new ArgumentOutOfRangeException(nameof(row)); return new RouletteBet(RouletteBetKind.Street, "Street " + (3 * row - 2) + "-" + (3 * row), 3 * row - 2, 3 * row - 1, 3 * row); }
        /// <summary>Corner of four numbers; <paramref name="lowest"/> is the smallest (1..32, not a multiple of 3).</summary>
        public static RouletteBet Corner(int lowest)
        { if (lowest < 1 || lowest > 32 || lowest % 3 == 0) throw new ArgumentOutOfRangeException(nameof(lowest)); return new RouletteBet(RouletteBetKind.Corner, "Corner " + lowest, lowest, lowest + 1, lowest + 3, lowest + 4); }
        public static RouletteBet SixLine(int row)
        { if (row < 1 || row > 11) throw new ArgumentOutOfRangeException(nameof(row)); return new RouletteBet(RouletteBetKind.SixLine, "Six line " + (3 * row - 2) + "-" + (3 * row + 3), Enumerable.Range(3 * row - 2, 6).ToArray()); }
        public static RouletteBet Column(int c)
        { if (c < 1 || c > 3) throw new ArgumentOutOfRangeException(nameof(c)); return new RouletteBet(RouletteBetKind.Column, "Column " + c, Enumerable.Range(1, 36).Where(n => n % 3 == c % 3).ToArray()); }
        public static RouletteBet Dozen(int d)
        { if (d < 1 || d > 3) throw new ArgumentOutOfRangeException(nameof(d)); return new RouletteBet(RouletteBetKind.Dozen, "Dozen " + d, Enumerable.Range(12 * (d - 1) + 1, 12).ToArray()); }
        public static RouletteBet Red() => new RouletteBet(RouletteBetKind.Red, "Red", Enumerable.Range(1, 36).Where(IsRed).ToArray());
        public static RouletteBet Black() => new RouletteBet(RouletteBetKind.Black, "Black", Enumerable.Range(1, 36).Where(n => !IsRed(n)).ToArray());
        public static RouletteBet Odd() => new RouletteBet(RouletteBetKind.Odd, "Odd", Enumerable.Range(1, 36).Where(n => n % 2 == 1).ToArray());
        public static RouletteBet Even() => new RouletteBet(RouletteBetKind.Even, "Even", Enumerable.Range(1, 36).Where(n => n % 2 == 0).ToArray());
        public static RouletteBet Low() => new RouletteBet(RouletteBetKind.Low, "Low 1-18", Enumerable.Range(1, 18).ToArray());
        public static RouletteBet High() => new RouletteBet(RouletteBetKind.High, "High 19-36", Enumerable.Range(19, 18).ToArray());

        /// <summary>A reasonable spread of bets a guest might place.</summary>
        public static RouletteBet RandomBet(RngStream rng, double riskAppetite)
        {
            double r = rng.NextDouble();
            if (r < 0.55 - 0.35 * riskAppetite)
                switch (rng.Range(0, 6)) { case 0: return Red(); case 1: return Black(); case 2: return Odd(); case 3: return Even(); case 4: return Low(); default: return High(); }
            if (r < 0.80 - 0.2 * riskAppetite) return rng.Chance(0.5) ? Dozen(rng.Range(1, 4)) : Column(rng.Range(1, 4));
            if (r < 0.92) return rng.Chance(0.5) ? Street(rng.Range(1, 13)) : Corner(PickCorner(rng));
            return Straight(rng.Range(0, 37));
        }
        static int PickCorner(RngStream rng) { while (true) { int n = rng.Range(1, 33); if (n % 3 != 0) return n; } }

        public static IEnumerable<RouletteBet> AllBets()
        {
            for (int n = 0; n <= 36; n++) yield return Straight(n);
            for (int n = 1; n <= 36; n++) { if (n % 3 != 0) yield return Split(n, n + 1); if (n + 3 <= 36) yield return Split(n, n + 3); }
            for (int z = 1; z <= 3; z++) yield return Split(0, z);
            for (int r = 1; r <= 12; r++) yield return Street(r);
            for (int n = 1; n <= 32; n++) if (n % 3 != 0) yield return Corner(n);
            for (int r = 1; r <= 11; r++) yield return SixLine(r);
            for (int c = 1; c <= 3; c++) { yield return Column(c); yield return Dozen(c); }
            yield return Red(); yield return Black(); yield return Odd(); yield return Even(); yield return Low(); yield return High();
        }
    }

    /// <summary>Wheel layout and house rules. The house edge target is met exactly on every bet through a return scale.</summary>
    public sealed class RouletteRules
    {
        public readonly int Zeros;           // 1 = European, 2 = American
        public readonly bool LaPartage;      // half the stake back on zero for even-money bets
        public readonly double TargetEdge;   // house edge on every bet

        public RouletteRules(int zeros, bool laPartage, double targetEdge) { Zeros = zeros; LaPartage = laPartage; TargetEdge = targetEdge; }
        public int Pockets => 36 + Zeros;

        /// <summary>Wheel flavour that fits the edge: under 2 % la partage, over 4 % double zero, else a plain single zero.</summary>
        public static RouletteRules ForEdge(double edge) => new RouletteRules(edge >= 0.04 ? 2 : 1, edge < 0.02, edge);
        public static RouletteRules European => new RouletteRules(1, false, 1.0 / 37);
        public static RouletteRules American => new RouletteRules(2, false, 2.0 / 38);

        /// <summary>Unscaled expected return of a bet on this wheel, e.g. 36/37 for a straight bet on a European wheel.</summary>
        public double RawExpectedReturn(RouletteBet bet)
        {
            double r = 0;
            for (int p = 0; p < Pockets; p++) r += RawReturn(bet, p);
            return r / Pockets;
        }
        double RawReturn(RouletteBet bet, int pocket)
        {
            if (bet.Covers(pocket)) return bet.Odds + 1;
            if (LaPartage && bet.IsEvenMoney && pocket == 0) return 0.5;
            return 0;
        }

        /// <summary>Scale on all returns so that the bet's exact edge equals the target.</summary>
        public double ScaleFor(RouletteBet bet) { double raw = RawExpectedReturn(bet); return raw <= 0 ? 0 : (1.0 - TargetEdge) / raw; }

        /// <summary>Total return (stake included) in stakes for this bet when the ball lands in <paramref name="pocket"/>.</summary>
        public double Return(RouletteBet bet, int pocket) => RawReturn(bet, pocket) * ScaleFor(bet);

        /// <summary>Exact house edge of a bet by full enumeration of the pockets (equals the target by construction).</summary>
        public double ExactEdge(RouletteBet bet)
        {
            double sc = ScaleFor(bet), r = 0;
            for (int p = 0; p < Pockets; p++) r += RawReturn(bet, p) * sc;
            return 1.0 - r / Pockets;
        }

        /// <summary>Standard deviation of the net result for one stake on this bet.</summary>
        public double Sigma(RouletteBet bet)
        {
            double sc = ScaleFor(bet), m1 = 0, m2 = 0;
            for (int p = 0; p < Pockets; p++) { double x = RawReturn(bet, p) * sc - 1.0; m1 += x; m2 += x * x; }
            m1 /= Pockets; m2 /= Pockets;
            return Math.Sqrt(Math.Max(0, m2 - m1 * m1));
        }

        /// <summary>One uniform spin: pocket index 0..Pockets-1 (37 is the double zero).</summary>
        public int Spin(RngStream rng) => rng.Range(0, Pockets);
    }
}
