// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
// The maths behind the casino animations (1.1.0, F30): easing curves, the wheel layout of real roulette wheels, a spin plan that ends exactly
// on a pocket that is already known, reel strips that stop on a known symbol, cosmetic card faces and a small poker hand evaluator that turns
// a hand category (RimdeckMath) into real cards. Nothing here draws random numbers for the game: every outcome is decided before an
// animation starts; these helpers only make the display agree with it.
using System;
using System.Collections.Generic;

namespace Hearth.CasinoMath
{
    /// <summary>Easing curves on t in [0, 1] (values outside are clamped).</summary>
    public static class Ease
    {
        public static double Clamp01(double t) => t < 0 ? 0 : (t > 1 ? 1 : t);
        public static double Linear(double t) => Clamp01(t);
        public static double InQuad(double t) { t = Clamp01(t); return t * t; }
        public static double OutQuad(double t) { t = Clamp01(t); return 1 - (1 - t) * (1 - t); }
        public static double OutCubic(double t) { t = Clamp01(t); double u = 1 - t; return 1 - u * u * u; }
        public static double InOutCubic(double t) { t = Clamp01(t); return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
        public static double OutQuart(double t) { t = Clamp01(t); double u = 1 - t; return 1 - u * u * u * u; }
        public static double SmoothStep(double t) { t = Clamp01(t); return t * t * (3 - 2 * t); }

        /// <summary>Overshoots the target a little and settles back (a reel that clunks into place). s ≈ 1.7 is the classic amount.</summary>
        public static double OutBack(double t, double s = 1.70158)
        {
            t = Clamp01(t); double u = t - 1;
            return 1 + (s + 1) * u * u * u + s * u * u;
        }

        /// <summary>A bouncing hop height (0 … 1): <paramref name="hops"/> arcs, each lower than the one before. 0 at t = 0 and t = 1.</summary>
        public static double Hops(double t, int hops, double damping = 0.55)
        {
            t = Clamp01(t); if (hops <= 0) return 0;
            double x = t * hops; int k = (int)Math.Floor(x); if (k >= hops) return 0;
            double f = x - k;
            return Math.Pow(damping, k) * 4 * f * (1 - f);
        }

        /// <summary>A short shake (−1 … 1) that dies away: for a bust or a heavy loss.</summary>
        public static double Shake(double t, double cycles = 5) { t = Clamp01(t); return Math.Sin(t * cycles * 2 * Math.PI) * (1 - t); }

        /// <summary>Linear blend.</summary>
        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);
    }

    /// <summary>The pocket order of real wheels (clockwise from the zero), so a ball that lands shows the right neighbours.</summary>
    public static class WheelLayout
    {
        /// <summary>Single-zero (European) wheel.</summary>
        public static readonly int[] European = { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };
        /// <summary>Double-zero (American) wheel; 37 stands for "00".</summary>
        public static readonly int[] American = { 0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1, 37, 27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2 };

        public static int[] Order(int zeros) => zeros >= 2 ? American : European;

        public static int IndexOf(int pocket, int zeros)
        {
            var o = Order(zeros);
            for (int i = 0; i < o.Length; i++) if (o[i] == pocket) return i;
            return -1;
        }

        /// <summary>Angle of a pocket's centre on the wheel, in degrees clockwise from the wheel's zero mark.</summary>
        public static double PocketAngle(int pocket, int zeros)
        {
            int i = IndexOf(pocket, zeros); if (i < 0) return 0;
            return i * 360.0 / Order(zeros).Length;
        }

        /// <summary>The pocket under a wheel-relative angle (degrees clockwise from the zero mark).</summary>
        public static int PocketAt(double wheelRelativeDeg, int zeros)
        {
            var o = Order(zeros); double step = 360.0 / o.Length;
            double a = Norm(wheelRelativeDeg + step / 2);
            int i = (int)Math.Floor(a / step) % o.Length;
            return o[i];
        }

        /// <summary>Normalises an angle to [0, 360).</summary>
        public static double Norm(double deg) { deg %= 360.0; return deg < 0 ? deg + 360.0 : deg; }
    }

    /// <summary>
    /// One spin of the wheel as a curve over t in [0, 1]: the wheel turns clockwise and slows down, the ball runs the other way on the rim,
    /// drops toward the pockets with a few hops and comes to rest exactly in <see cref="Pocket"/>; from then on it rides with the wheel.
    /// Angles are screen degrees, clockwise from the top. The plan is solved backwards from the known pocket, so the stop is exact.
    /// </summary>
    public sealed class RouletteSpinPlan
    {
        public int Pocket, Zeros;
        public double WheelStart, WheelTravel, BallStart, BallTravel;
        /// <summary>Fraction of the spin when the ball leaves the rim, and when it rests in its pocket.</summary>
        public double DropT = 0.58, LandT = 0.84;
        public int Hops = 3;

        public static RouletteSpinPlan Make(int pocket, int zeros, double wheelStart, ulong seed)
        {
            var rng = new RngStream(seed, 0x57484545_4C534E50UL);
            var p = new RouletteSpinPlan { Pocket = pocket, Zeros = zeros, WheelStart = WheelLayout.Norm(wheelStart) };
            p.WheelTravel = 360.0 * (1.6 + rng.NextDouble() * 0.8);                 // one and a half to two and a half turns of the wheel
            p.BallStart = WheelLayout.Norm(rng.NextDouble() * 360.0);
            p.Hops = 2 + rng.Range(0, 2);
            // where the pocket is on screen when the ball lands
            double target = WheelLayout.Norm(p.WheelAt(p.LandT) + WheelLayout.PocketAngle(pocket, zeros));
            // the ball runs counter-clockwise (decreasing angles): BallStart − travel ≡ target (mod 360), with 5 to 6 laps
            double need = WheelLayout.Norm(p.BallStart - target);
            p.BallTravel = need + 360.0 * (5 + rng.Range(0, 2));
            return p;
        }

        double WheelAt(double t) => WheelStart + WheelTravel * Ease.OutCubic(t);

        /// <summary>Wheel rotation (degrees clockwise) at t.</summary>
        public double WheelAngle(double t) => WheelLayout.Norm(WheelAt(Ease.Clamp01(t)));

        /// <summary>Screen angle of the ball (degrees clockwise from the top) at t.</summary>
        public double BallAngle(double t)
        {
            t = Ease.Clamp01(t);
            if (t >= LandT) return WheelLayout.Norm(WheelAt(t) + WheelLayout.PocketAngle(Pocket, Zeros));
            double u = t / LandT;
            return WheelLayout.Norm(BallStart - BallTravel * Ease.OutQuad(u));
        }

        /// <summary>Radius of the ball: 1 = on the rim track, 0 = in the pocket ring (with small hops while it drops).</summary>
        public double BallRadius(double t)
        {
            t = Ease.Clamp01(t);
            if (t <= DropT) return 1.0;
            if (t >= LandT) return 0.0;
            double u = (t - DropT) / (LandT - DropT);
            double fall = 1.0 - Ease.InOutCubic(u);
            return Math.Max(0.0, Math.Min(1.0, fall + 0.22 * Ease.Hops(u, Hops)));
        }

        /// <summary>The pocket the ball sits in at t (only meaningful once it has landed).</summary>
        public int PocketUnderBall(double t) => WheelLayout.PocketAt(BallAngle(t) - WheelAngle(t), Zeros);
    }

    /// <summary>The symbols one reel shows while it spins: a strip that starts with the symbol on screen before the spin and ends on the result.</summary>
    public static class ReelStrip
    {
        /// <summary>A strip of <paramref name="length"/> symbols; element 0 is <paramref name="from"/>, the last one is <paramref name="final"/>.
        /// The ones in between are cosmetic (seeded, never a game draw), chosen so two neighbours are never the same symbol.</summary>
        public static int[] Build(int from, int final, int length, ulong seed, int symbols = SlotPaytable.Symbols)
        {
            if (length < 2) length = 2;
            var rng = new RngStream(seed, 0x5245454C_53545250UL);
            var s = new int[length];
            s[0] = Math.Max(0, Math.Min(symbols - 1, from));
            s[length - 1] = Math.Max(0, Math.Min(symbols - 1, final));
            for (int i = 1; i < length - 1; i++)
            {
                int v; int guard = 0;
                do { v = rng.Range(0, symbols); } while ((v == s[i - 1] || (i == length - 2 && v == s[length - 1])) && ++guard < 20);
                s[i] = v;
            }
            return s;
        }

        /// <summary>Scroll position of a reel (in symbols, 0 … length−1) at time t (seconds); it stops at <paramref name="stopAt"/> with a small overshoot.</summary>
        public static double Position(double t, double stopAt, int length, double overshoot = 1.2)
        {
            if (stopAt <= 0) return length - 1;
            double u = t / stopAt;
            if (u >= 1) return length - 1;
            if (u <= 0) return 0;
            return (length - 1) * Ease.OutBack(u, overshoot);
        }
    }

    /// <summary>Card faces for a blackjack table that only knows values 1 … 10: a ten shows as 10, J, Q or K, and every card gets a suit.
    /// Seeded by the hand and the card's index, so a reloaded open hand looks the same.</summary>
    public static class CardCosmetics
    {
        public const int Suits = 4;
        static ulong H(ulong seed, int index, ulong salt) => RngStream.Mix(seed ^ RngStream.Mix((ulong)(index + 1) * 0x9E3779B97F4A7C15UL ^ salt));

        public static int Suit(ulong seed, int index) => (int)(H(seed, index, 0x53554954UL) % Suits);

        /// <summary>Rank label of a blackjack card value: 1 = "A", 2 … 9, 10 = "10", "J", "Q" or "K".</summary>
        public static string Rank(int value, ulong seed, int index)
        {
            if (value <= 1) return "A";
            if (value < 10) return value.ToString();
            switch ((int)(H(seed, index, 0x52414E4BUL) % 4)) { case 0: return "10"; case 1: return "J"; case 2: return "Q"; default: return "K"; }
        }

        /// <summary>Two of the four suits are printed red (see the suit glyphs of the Thrumbo deck).</summary>
        public static bool IsRedSuit(int suit) => suit == 0 || suit == 3;
    }

    /// <summary>A playing card for the poker table (rank 2 … 14, ace high; suit 0 … 3).</summary>
    public struct PCard
    {
        public int Rank, Suit;
        public PCard(int rank, int suit) { Rank = rank; Suit = suit; }
        public int Id => (Rank - 2) * 4 + Suit;
        public override string ToString() => RankLabel(Rank) + "shdc"[Suit];
        public static string RankLabel(int r) => r <= 10 ? r.ToString() : r == 11 ? "J" : r == 12 ? "Q" : r == 13 ? "K" : "A";
    }

    /// <summary>The cards of one cosmetic poker hand: hole cards per seat, the board, the winner and the five cards that win.</summary>
    public sealed class PokerDeal
    {
        public PCard[][] Hole;
        public PCard[] Board;
        public int Winner, Category;
        public PCard[] WinningFive;
        public long WinnerScore;
        /// <summary>False when no deal with exactly this category and a clear winner was found (the display then still shows a valid hand).</summary>
        public bool Exact;
    }

    /// <summary>A plain 5- and 7-card poker evaluator and a generator that deals real cards for a hand category chosen by RimdeckMath.</summary>
    public static class PokerHands
    {
        /// <summary>Category index as in <see cref="RimdeckMath.HandNames"/>: 0 high card … 8 straight flush.</summary>
        public static int Category(long score) => (int)(score >> 40);

        /// <summary>Comparable score of exactly five cards: higher is better; equal scores split the pot.</summary>
        public static long Score5(PCard a, PCard b, PCard c, PCard d, PCard e)
        {
            var r = new int[5] { a.Rank, b.Rank, c.Rank, d.Rank, e.Rank };
            Array.Sort(r); Array.Reverse(r);
            bool flush = a.Suit == b.Suit && b.Suit == c.Suit && c.Suit == d.Suit && d.Suit == e.Suit;
            bool straight = false; int high = r[0];
            if (r[0] - r[4] == 4 && Distinct(r)) straight = true;
            else if (r[0] == 14 && r[1] == 5 && r[2] == 4 && r[3] == 3 && r[4] == 2) { straight = true; high = 5; }
            // group by count, then rank
            var cnt = new int[15]; foreach (int x in r) cnt[x]++;
            var groups = new List<(int n, int rank)>();
            for (int k = 14; k >= 2; k--) if (cnt[k] > 0) groups.Add((cnt[k], k));
            groups.Sort((x, y) => x.n != y.n ? y.n.CompareTo(x.n) : y.rank.CompareTo(x.rank));
            int cat;
            if (straight && flush) cat = 8;
            else if (groups[0].n == 4) cat = 7;
            else if (groups[0].n == 3 && groups.Count > 1 && groups[1].n == 2) cat = 6;
            else if (flush) cat = 5;
            else if (straight) cat = 4;
            else if (groups[0].n == 3) cat = 3;
            else if (groups[0].n == 2 && groups.Count > 1 && groups[1].n == 2) cat = 2;
            else if (groups[0].n == 2) cat = 1;
            else cat = 0;
            long score = (long)cat << 40;
            if (cat == 8 || cat == 4) return score | ((long)high << 32);
            int shift = 32;
            foreach (var g in groups) { score |= (long)g.rank << shift; shift -= 4; if (shift < 0) break; }
            return score;
        }

        static bool Distinct(int[] sortedDesc) { for (int i = 1; i < sortedDesc.Length; i++) if (sortedDesc[i] == sortedDesc[i - 1]) return false; return true; }

        /// <summary>Best five of the given cards (5 … 7): returns the score and fills <paramref name="best"/>.</summary>
        public static long Best(IList<PCard> cards, PCard[] best = null)
        {
            int n = cards.Count; long top = long.MinValue;
            if (n < 5) return top;
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) for (int c = b + 1; c < n; c++) for (int d = c + 1; d < n; d++) for (int e = d + 1; e < n; e++)
            {
                long s = Score5(cards[a], cards[b], cards[c], cards[d], cards[e]);
                if (s > top) { top = s; if (best != null) { best[0] = cards[a]; best[1] = cards[b]; best[2] = cards[c]; best[3] = cards[d]; best[4] = cards[e]; } }
            }
            return top;
        }

        /// <summary>
        /// Deals hole cards to <paramref name="seats"/> seats and a five-card board so that <paramref name="winner"/> wins with a hand of
        /// <paramref name="category"/> and nobody ties or beats it. Tries random deals, then builds one around the category; if even that
        /// cannot avoid a tie the deal is returned with <see cref="PokerDeal.Exact"/> = false.
        /// </summary>
        public static PokerDeal Cosmetic(int category, int seats, int winner, ulong seed)
        {
            category = Math.Max(0, Math.Min(8, category)); seats = Math.Max(2, seats); winner = Math.Max(0, Math.Min(seats - 1, winner));
            var rng = new RngStream(seed, 0x504F4B45_52444541UL);
            PokerDeal fallback = null;
            for (int attempt = 0; attempt < 600; attempt++)
            {
                var d = category == 0 && seats * 2 + 5 <= 13 ? DistinctRanksDeal(seats, rng)
                      : attempt < 200 && category <= 2 ? RandomDeal(seats, rng) : Built(category, seats, winner, rng);
                if (d == null) continue;
                if (category == 0 || (attempt < 200 && category <= 2))
                {
                    // random deal: find who would win and swap seats so the right player has it
                    int w = BestSeat(d, out _, out bool tie);
                    if (tie) continue;
                    var tmp = d.Hole[w]; d.Hole[w] = d.Hole[winner]; d.Hole[winner] = tmp;
                }
                if (Check(d, category, winner)) { d.Exact = true; return d; }
                if (fallback == null) fallback = d;
            }
            fallback = fallback ?? RandomDeal(seats, rng);
            Check(fallback, -1, fallback.Winner);
            fallback.Exact = false;
            return fallback;
        }

        static int BestSeat(PokerDeal d, out long bestScore, out bool tie)
        {
            int w = 0; bestScore = long.MinValue; tie = false;
            var seven = new PCard[7];
            for (int s = 0; s < d.Hole.Length; s++)
            {
                seven[0] = d.Hole[s][0]; seven[1] = d.Hole[s][1]; for (int k = 0; k < 5; k++) seven[2 + k] = d.Board[k];
                long sc = Best(seven);
                if (sc > bestScore) { bestScore = sc; w = s; tie = false; } else if (sc == bestScore) tie = true;
            }
            return w;
        }

        /// <summary>True when <paramref name="winner"/> has the single best hand and (category ≥ 0) it is of that category. Fills the result fields.</summary>
        static bool Check(PokerDeal d, int category, int winner)
        {
            var seven = new PCard[7]; var five = new PCard[5];
            long ws = long.MinValue; bool ok = true;
            for (int s = 0; s < d.Hole.Length; s++)
            {
                seven[0] = d.Hole[s][0]; seven[1] = d.Hole[s][1]; for (int k = 0; k < 5; k++) seven[2 + k] = d.Board[k];
                if (s == winner) ws = Best(seven, five);
            }
            for (int s = 0; s < d.Hole.Length; s++)
            {
                if (s == winner) continue;
                seven[0] = d.Hole[s][0]; seven[1] = d.Hole[s][1]; for (int k = 0; k < 5; k++) seven[2 + k] = d.Board[k];
                if (Best(seven) >= ws) ok = false;
            }
            if (category >= 0 && Category(ws) != category) ok = false;
            d.Winner = winner; d.WinnerScore = ws; d.Category = Category(ws); d.WinningFive = (PCard[])five.Clone();
            return ok;
        }

        static PokerDeal RandomDeal(int seats, RngStream rng)
        {
            var deck = Deck(rng); int k = 0;
            var d = new PokerDeal { Hole = new PCard[seats][], Board = new PCard[5] };
            for (int s = 0; s < seats; s++) d.Hole[s] = new[] { deck[k++], deck[k++] };
            for (int b = 0; b < 5; b++) d.Board[b] = deck[k++];
            return d;
        }

        /// <summary>High card needs every rank at most once on the table (any pair would beat it): one card per rank, at most two of a suit on the board.</summary>
        static PokerDeal DistinctRanksDeal(int seats, RngStream rng)
        {
            var ranks = new List<int>(); for (int r = 2; r <= 14; r++) ranks.Add(r);
            for (int i = ranks.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); int t = ranks[i]; ranks[i] = ranks[j]; ranks[j] = t; }
            var d = new PokerDeal { Hole = new PCard[seats][], Board = new PCard[5] };
            var boardSuits = new int[4]; int k = 0;
            for (int b = 0; b < 5; b++)
            {
                int s; int guard = 0;
                do { s = rng.Range(0, 4); } while (boardSuits[s] >= 2 && ++guard < 40);
                boardSuits[s]++; d.Board[b] = new PCard(ranks[k++], s);
            }
            for (int seat = 0; seat < seats; seat++) d.Hole[seat] = new[] { new PCard(ranks[k++], rng.Range(0, 4)), new PCard(ranks[k++], rng.Range(0, 4)) };
            return d;
        }

        static List<PCard> Deck(RngStream rng)
        {
            var deck = new List<PCard>(52);
            for (int r = 2; r <= 14; r++) for (int s = 0; s < 4; s++) deck.Add(new PCard(r, s));
            for (int i = deck.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = deck[i]; deck[i] = deck[j]; deck[j] = t; }
            return deck;
        }

        /// <summary>Builds the five cards of the category, gives two to the winner and three to the board, fills the rest at random.</summary>
        static PokerDeal Built(int category, int seats, int winner, RngStream rng)
        {
            var five = Make(category, rng); if (five == null) return null;
            var used = new HashSet<int>(); foreach (var c in five) used.Add(c.Id);
            // shuffle the five, then: two hole cards for the winner, three on the board
            for (int i = 4; i > 0; i--) { int j = rng.Range(0, i + 1); var t = five[i]; five[i] = five[j]; five[j] = t; }
            var rest = new List<PCard>();
            foreach (var c in Deck(rng)) if (!used.Contains(c.Id)) rest.Add(c);
            int k = 0;
            var d = new PokerDeal { Hole = new PCard[seats][], Board = new PCard[5] };
            d.Hole[winner] = new[] { five[0], five[1] };
            var board = new List<PCard> { five[2], five[3], five[4], rest[k++], rest[k++] };
            for (int i = 4; i > 0; i--) { int j = rng.Range(0, i + 1); var t = board[i]; board[i] = board[j]; board[j] = t; }
            d.Board = board.ToArray();
            for (int s = 0; s < seats; s++) if (s != winner) d.Hole[s] = new[] { rest[k++], rest[k++] };
            return d;
        }

        /// <summary>Five cards that form exactly the category (random ranks and suits).</summary>
        static PCard[] Make(int cat, RngStream rng)
        {
            int Suit() => rng.Range(0, 4);
            int[] Ranks(int n, params int[] avoid)
            {
                var pool = new List<int>(); for (int r = 2; r <= 14; r++) if (Array.IndexOf(avoid, r) < 0) pool.Add(r);
                var res = new int[n];
                for (int i = 0; i < n; i++) { int j = rng.Range(0, pool.Count); res[i] = pool[j]; pool.RemoveAt(j); }
                return res;
            }
            PCard[] OfRanks(int[] ranks, bool sameSuit)
            {
                var res = new PCard[ranks.Length]; int s0 = Suit();
                var usedSuits = new Dictionary<int, int>();
                for (int i = 0; i < ranks.Length; i++)
                {
                    int s;
                    if (sameSuit) s = s0;
                    else
                    {
                        // never repeat a rank in the same suit, and avoid an accidental flush
                        usedSuits.TryGetValue(ranks[i], out int mask); int guard = 0;
                        do { s = Suit(); } while (((mask >> s) & 1) == 1 && ++guard < 20);
                        usedSuits[ranks[i]] = mask | (1 << s);
                    }
                    res[i] = new PCard(ranks[i], s);
                }
                if (!sameSuit && res.Length == 5 && res[0].Suit == res[1].Suit && res[1].Suit == res[2].Suit && res[2].Suit == res[3].Suit && res[3].Suit == res[4].Suit)
                    res[4] = new PCard(res[4].Rank, (res[4].Suit + 1) % 4);
                return res;
            }
            bool IsStraight(int[] r) { var s = (int[])r.Clone(); Array.Sort(s); if (s[4] - s[0] == 4 && s[1] != s[0] && s[2] != s[1] && s[3] != s[2] && s[4] != s[3]) return true; return s[4] == 14 && s[0] == 2 && s[1] == 3 && s[2] == 4 && s[3] == 5; }
            int[] StraightRanks() { int lo = rng.Range(1, 11); var r = new int[5]; for (int i = 0; i < 5; i++) r[i] = lo + i == 1 ? 14 : lo + i; return r; }
            switch (cat)
            {
                case 8: return OfRanks(StraightRanks(), true);
                case 7: { var r = Ranks(2); return OfRanks(new[] { r[0], r[0], r[0], r[0], r[1] }, false); }
                case 6: { var r = Ranks(2); return OfRanks(new[] { r[0], r[0], r[0], r[1], r[1] }, false); }
                case 5: { int[] r; int g = 0; do { r = Ranks(5); } while (IsStraight(r) && ++g < 20); return OfRanks(r, true); }
                case 4: return OfRanks(StraightRanks(), false);
                case 3: { var r = Ranks(3); return OfRanks(new[] { r[0], r[0], r[0], r[1], r[2] }, false); }
                case 2: { var r = Ranks(3); return OfRanks(new[] { r[0], r[0], r[1], r[1], r[2] }, false); }
                case 1: { var r = Ranks(4); return OfRanks(new[] { r[0], r[0], r[1], r[2], r[3] }, false); }
                default: { int[] r; int g = 0; do { r = Ranks(5); } while (IsStraight(r) && ++g < 20); var c = OfRanks(r, false); return c; }
            }
        }
    }
}
