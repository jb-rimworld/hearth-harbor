// SPDX-License-Identifier: MIT
// Pure code (no game references).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hearth.CasinoMath
{
    public enum BlackjackAction { Stand, Hit, Double }
    public enum BlackjackOutcome { Lose, Push, Win, Blackjack, DealerBlackjack, Bust, DoubleWin, DoubleLose, DoublePush }

    /// <summary>
    /// Simplified blackjack: infinite deck, one hand, hit / stand / double on any two cards, no split, no surrender,
    /// dealer peeks for blackjack. The rules that move the house edge are the dealer's soft 17 and the blackjack payout
    /// (3:2 S17 on Standard Room, 6:5 H17 on Overbooked), plus how well the player plays (accuracy).
    /// </summary>
    public sealed class BlackjackRules
    {
        public readonly bool DealerHitsSoft17;
        public readonly double BlackjackPays;   // 1.5 or 1.2
        public BlackjackRules(bool h17, double bjPays) { DealerHitsSoft17 = h17; BlackjackPays = bjPays; }
        public static BlackjackRules Standard => new BlackjackRules(false, 1.5);
        public static BlackjackRules Greedy => new BlackjackRules(true, 1.2);
        public override string ToString() => (BlackjackPays >= 1.5 ? "3:2" : "6:5") + (DealerHitsSoft17 ? " H17" : " S17");
        public string Key => (DealerHitsSoft17 ? "H" : "S") + BlackjackPays.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Dynamic-programming solution of the game for one rule set and one player accuracy. With probability <c>accuracy</c>
    /// the player takes the best action at every decision, otherwise the naive one (mimic the dealer: hit below 17, never double).
    /// All numbers are exact (no sampling).
    /// </summary>
    public sealed class BlackjackSolver
    {
        static readonly Dictionary<string, BlackjackSolver> cache = new Dictionary<string, BlackjackSolver>();
        public static BlackjackSolver Get(BlackjackRules rules, double accuracy)
        {
            accuracy = Math.Max(0, Math.Min(1, accuracy));
            string key = rules.Key + "|" + Math.Round(accuracy, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            lock (cache) { if (!cache.TryGetValue(key, out var s)) cache[key] = s = new BlackjackSolver(rules, Math.Round(accuracy, 3)); return s; }
        }

        public readonly BlackjackRules Rules;
        public readonly double Accuracy;
        static readonly double[] cardP = { 0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 1 / 13.0, 4 / 13.0 };

        // dealer distribution given the upcard (index 1..10), conditional on "no dealer blackjack"; [0]=17 … [4]=21, [5]=bust
        readonly double[][] dealerDist = new double[11][];
        readonly double[] pDealerBj = new double[11];
        readonly Dictionary<int, double> memoHit = new Dictionary<int, double>();
        readonly Dictionary<int, double> memoV = new Dictionary<int, double>();

        BlackjackSolver(BlackjackRules rules, double accuracy)
        {
            Rules = rules; Accuracy = accuracy;
            for (int u = 1; u <= 10; u++) BuildDealer(u);
        }

        void BuildDealer(int up)
        {
            var dist = new double[6]; double bj = 0;
            for (int hole = 1; hole <= 10; hole++)
            {
                double p = cardP[hole];
                if ((up == 1 && hole == 10) || (up == 10 && hole == 1)) { bj += p; continue; }
                int total = up + hole; bool soft = up == 1 || hole == 1;
                if (soft) total += 10;
                if (total > 21) { total -= 10; soft = false; }
                DealerPlay(total, soft, p, dist);
            }
            pDealerBj[up] = bj;
            double norm = 1 - bj;
            for (int i = 0; i < dist.Length; i++) dist[i] /= norm;
            dealerDist[up] = dist;
        }

        void DealerPlay(int total, bool soft, double weight, double[] dist)
        {
            if (total > 21) { if (soft) { DealerPlay(total - 10, false, weight, dist); return; } dist[5] += weight; return; }
            bool stand = total > 17 || (total == 17 && (!soft || !Rules.DealerHitsSoft17));
            if (stand) { dist[total - 17] += weight; return; }
            for (int c = 1; c <= 10; c++)
            {
                int t = total + c; bool s = soft;
                if (c == 1 && !soft && total + 11 <= 21) { t = total + 11; s = true; }
                DealerPlay(t, s, weight * cardP[c], dist);
            }
        }

        /// <summary>Expected result (in stakes) of standing on <paramref name="total"/> against the upcard, given no dealer blackjack.</summary>
        public double StandEv(int total, int up)
        {
            if (total > 21) return -1;
            var d = dealerDist[up]; double ev = d[5];             // dealer busts: win
            for (int dt = 17; dt <= 21; dt++)
            {
                double p = d[dt - 17];
                if (total > dt) ev += p; else if (total < dt) ev -= p;
            }
            // a player total below 17 beats only a bust (dt >= 17 always), handled above
            return ev;
        }

        static int Key(int total, bool soft, bool first, int up) => ((total * 2 + (soft ? 1 : 0)) * 2 + (first ? 1 : 0)) * 16 + up;

        double HitEv(int total, bool soft, int up)
        {
            double ev = 0;
            for (int c = 1; c <= 10; c++)
            {
                int t = total + c; bool s = soft;
                if (c == 1 && !soft && total + 11 <= 21) { t = total + 11; s = true; }
                if (t > 21 && s) { t -= 10; s = false; }
                ev += cardP[c] * (t > 21 ? -1.0 : V(t, s, false, up));
            }
            return ev;
        }

        double DoubleEv(int total, bool soft, int up)
        {
            double ev = 0;
            for (int c = 1; c <= 10; c++)
            {
                int t = total + c; bool s = soft;
                if (c == 1 && !soft && total + 11 <= 21) { t = total + 11; s = true; }
                if (t > 21 && s) { t -= 10; s = false; }
                ev += cardP[c] * (t > 21 ? -1.0 : StandEv(t, up));
            }
            return 2 * ev;
        }

        /// <summary>The naive action: hit below 17 (and soft 17 when the dealer would), never double.</summary>
        public BlackjackAction NaiveAction(int total, bool soft)
        {
            bool hit = total < 17 || (total == 17 && soft && Rules.DealerHitsSoft17);
            return hit ? BlackjackAction.Hit : BlackjackAction.Stand;
        }

        /// <summary>Best action when all later decisions are made with this solver's accuracy.</summary>
        public BlackjackAction BestAction(int total, bool soft, bool first, int up, out double ev)
        {
            double stand = StandEv(total, up), hit = HitEv(total, soft, up);
            var best = BlackjackAction.Stand; ev = stand;
            if (hit > ev) { best = BlackjackAction.Hit; ev = hit; }
            if (first) { double dbl = DoubleEv(total, soft, up); if (dbl > ev) { best = BlackjackAction.Double; ev = dbl; } }
            return best;
        }

        double ActionEv(BlackjackAction a, int total, bool soft, int up)
            => a == BlackjackAction.Stand ? StandEv(total, up) : a == BlackjackAction.Hit ? HitEv(total, soft, up) : DoubleEv(total, soft, up);

        /// <summary>Value of a hand under the accuracy-mixed policy.</summary>
        double V(int total, bool soft, bool first, int up)
        {
            if (total >= 21) return StandEv(total, up);   // 21 always stands (a hand of 21 cannot improve)
            int k = Key(total, soft, first, up);
            if (memoV.TryGetValue(k, out var v)) return v;
            var best = BestAction(total, soft, first, up, out double bestEv);
            double naive = ActionEv(NaiveAction(total, soft), total, soft, up);
            v = Accuracy * bestEv + (1 - Accuracy) * naive;
            memoV[k] = v;
            return v;
        }

        /// <summary>Probability that the dealer has a natural, for this upcard (1 = ace).</summary>
        public double DealerBlackjackProbability(int up) => pDealerBj[up];

        /// <summary>Expected result in stakes of the whole round, by enumeration of all deals.</summary>
        public double ExpectedValue
        {
            get
            {
                double ev = 0;
                for (int up = 1; up <= 10; up++)
                    for (int c1 = 1; c1 <= 10; c1++) for (int c2 = 1; c2 <= 10; c2++)
                        ev += cardP[up] * cardP[c1] * cardP[c2] * InitialEv(c1, c2, up);
                return ev;
            }
        }

        public double HouseEdge => -ExpectedValue;

        public double InitialEv(int c1, int c2, int up)
        {
            bool playerBj = (c1 == 1 && c2 == 10) || (c1 == 10 && c2 == 1);
            double pd = pDealerBj[up];
            double vs;
            if (playerBj) vs = Rules.BlackjackPays;
            else
            {
                int total = c1 + c2; bool soft = c1 == 1 || c2 == 1;
                if (soft) total += 10;
                if (total > 21) { total -= 10; soft = false; }
                vs = V(total, soft, true, up);
            }
            double vsBj = playerBj ? 0 : -1;
            return pd * vsBj + (1 - pd) * vs;
        }

        readonly Dictionary<int, double> memoM2 = new Dictionary<int, double>();

        double StandM2(int total, int up)
        {
            if (total > 21) return 1;
            double push = total >= 17 ? dealerDist[up][total - 17] : 0;
            return 1 - push;
        }

        double HitM2(int total, bool soft, int up)
        {
            double m = 0;
            for (int c = 1; c <= 10; c++)
            {
                int t = total + c; bool s = soft;
                if (c == 1 && !soft && total + 11 <= 21) { t = total + 11; s = true; }
                if (t > 21 && s) { t -= 10; s = false; }
                m += cardP[c] * (t > 21 ? 1.0 : V2(t, s, false, up));
            }
            return m;
        }

        double DoubleM2(int total, bool soft, int up)
        {
            double m = 0;
            for (int c = 1; c <= 10; c++)
            {
                int t = total + c; bool s = soft;
                if (c == 1 && !soft && total + 11 <= 21) { t = total + 11; s = true; }
                if (t > 21 && s) { t -= 10; s = false; }
                m += cardP[c] * 4 * StandM2(t, up);
            }
            return m;
        }

        double ActionM2(BlackjackAction a, int total, bool soft, int up)
            => a == BlackjackAction.Stand ? StandM2(total, up) : a == BlackjackAction.Hit ? HitM2(total, soft, up) : DoubleM2(total, soft, up);

        double V2(int total, bool soft, bool first, int up)
        {
            if (total >= 21) return StandM2(total, up);
            int k = Key(total, soft, first, up);
            if (memoM2.TryGetValue(k, out var v)) return v;
            var best = BestAction(total, soft, first, up, out _);
            v = Accuracy * ActionM2(best, total, soft, up) + (1 - Accuracy) * ActionM2(NaiveAction(total, soft), total, soft, up);
            memoM2[k] = v;
            return v;
        }

        /// <summary>Standard deviation of one round's result in stakes (exact, second-moment DP).</summary>
        public double Sigma
        {
            get
            {
                double m2 = 0;
                for (int up = 1; up <= 10; up++)
                    for (int c1 = 1; c1 <= 10; c1++) for (int c2 = 1; c2 <= 10; c2++)
                    {
                        bool pBj = (c1 == 1 && c2 == 10) || (c1 == 10 && c2 == 1);
                        double pd = pDealerBj[up], vs;
                        if (pBj) vs = Rules.BlackjackPays * Rules.BlackjackPays;
                        else
                        {
                            int total = c1 + c2; bool soft = c1 == 1 || c2 == 1;
                            if (soft) total += 10;
                            if (total > 21) { total -= 10; soft = false; }
                            vs = V2(total, soft, true, up);
                        }
                        m2 += cardP[up] * cardP[c1] * cardP[c2] * (pd * (pBj ? 0 : 1) + (1 - pd) * vs);
                    }
                double m1 = ExpectedValue;
                return Math.Sqrt(Math.Max(0, m2 - m1 * m1));
            }
        }

        // ---- sampling ------------------------------------------------------------------------------

        public static int DrawCard(RngStream rng) { int r = rng.Range(1, 14); return r > 10 ? 10 : r; }

        public sealed class Round
        {
            public readonly List<int> Player = new List<int>();
            public readonly List<int> Dealer = new List<int>();
            public bool Doubled, DealerBlackjack, PlayerBlackjack, Done;
            public double Result;                 // net result in stakes
            public BlackjackOutcome Outcome;
            public int PlayerTotal, DealerTotal;
            public readonly List<BlackjackAction> Actions = new List<BlackjackAction>();
        }

        static void Total(List<int> cards, out int total, out bool soft)
        {
            int sum = 0; bool ace = false;
            foreach (int c in cards) { sum += c; if (c == 1) ace = true; }
            if (ace && sum + 10 <= 21) { total = sum + 10; soft = true; } else { total = sum; soft = false; }
        }

        public static int HandTotal(List<int> cards) { Total(cards, out int t, out _); return t; }

        /// <summary>Plays one complete round with this solver's policy (the player acts with the configured accuracy).</summary>
        public Round PlayRound(RngStream rng)
        {
            var r = new Round();
            int c1 = DrawCard(rng), c2 = DrawCard(rng), up = DrawCard(rng), hole = DrawCard(rng);
            r.Player.Add(c1); r.Player.Add(c2); r.Dealer.Add(up); r.Dealer.Add(hole);
            bool pBj = (c1 == 1 && c2 == 10) || (c1 == 10 && c2 == 1);
            bool dBj = (up == 1 && hole == 10) || (up == 10 && hole == 1);
            r.PlayerBlackjack = pBj; r.DealerBlackjack = dBj;
            if (dBj) { r.Result = pBj ? 0 : -1; r.Outcome = pBj ? BlackjackOutcome.Push : BlackjackOutcome.DealerBlackjack; Finish(r); return r; }
            if (pBj) { r.Result = Rules.BlackjackPays; r.Outcome = BlackjackOutcome.Blackjack; Finish(r); return r; }
            bool first = true;
            while (true)
            {
                Total(r.Player, out int t, out bool soft);
                if (t >= 21) break;
                var act = rng.NextDouble() < Accuracy ? BestAction(t, soft, first, up, out _) : NaiveAction(t, soft);
                if (act == BlackjackAction.Double && !first) act = BlackjackAction.Hit;
                r.Actions.Add(act);
                if (act == BlackjackAction.Stand) break;
                r.Player.Add(DrawCard(rng));
                if (act == BlackjackAction.Double) { r.Doubled = true; break; }
                first = false;
            }
            Settle(r, rng);
            return r;
        }

        /// <summary>Deals a hand for an interactive player. Naturals are settled at once (round.Done); otherwise the player acts with Hit / Stand / DoubleDown.</summary>
        public Round Deal(RngStream rng)
        {
            var r = new Round();
            int c1 = DrawCard(rng), c2 = DrawCard(rng), up = DrawCard(rng), hole = DrawCard(rng);
            r.Player.Add(c1); r.Player.Add(c2); r.Dealer.Add(up); r.Dealer.Add(hole);
            bool pBj = (c1 == 1 && c2 == 10) || (c1 == 10 && c2 == 1);
            bool dBj = (up == 1 && hole == 10) || (up == 10 && hole == 1);
            r.PlayerBlackjack = pBj; r.DealerBlackjack = dBj;
            if (dBj) { r.Result = pBj ? 0 : -1; r.Outcome = pBj ? BlackjackOutcome.Push : BlackjackOutcome.DealerBlackjack; Finish(r); }
            else if (pBj) { r.Result = Rules.BlackjackPays; r.Outcome = BlackjackOutcome.Blackjack; Finish(r); }
            return r;
        }

        public void Hit(Round r, RngStream rng)
        {
            if (r.Done) return;
            r.Player.Add(DrawCard(rng));
            if (HandTotal(r.Player) >= 21) Settle(r, rng);
        }

        public void Stand(Round r, RngStream rng) { if (!r.Done) Settle(r, rng); }

        public void DoubleDown(Round r, RngStream rng)
        {
            if (r.Done) return;
            r.Doubled = true; r.Player.Add(DrawCard(rng)); Settle(r, rng);
        }

        /// <summary>Plays the dealer's hand and settles the round (also used by the interactive window).</summary>
        public void Settle(Round r, RngStream rng)
        {
            Total(r.Player, out int pt, out _);
            r.PlayerTotal = pt;
            double stake = r.Doubled ? 2 : 1;
            if (pt > 21) { r.Result = -stake; r.Outcome = r.Doubled ? BlackjackOutcome.DoubleLose : BlackjackOutcome.Bust; Finish(r); return; }
            while (true)
            {
                Total(r.Dealer, out int dt, out bool dsoft);
                bool stand = dt > 17 || (dt == 17 && (!dsoft || !Rules.DealerHitsSoft17));
                if (stand) break;
                r.Dealer.Add(DrawCard(rng));
            }
            Total(r.Dealer, out int d, out _);
            r.DealerTotal = d;
            if (d > 21 || pt > d) { r.Result = stake; r.Outcome = r.Doubled ? BlackjackOutcome.DoubleWin : BlackjackOutcome.Win; }
            else if (pt < d) { r.Result = -stake; r.Outcome = r.Doubled ? BlackjackOutcome.DoubleLose : BlackjackOutcome.Lose; }
            else { r.Result = 0; r.Outcome = r.Doubled ? BlackjackOutcome.DoublePush : BlackjackOutcome.Push; }
            Finish(r);
        }

        static void Finish(Round r) { Total(r.Player, out r.PlayerTotal, out _); Total(r.Dealer, out r.DealerTotal, out _); r.Done = true; }

        /// <summary>Strategy accuracy of a player (KONZEPT 6.6): a = clamp(0.25 + 0.0375·S, 0.25, 1), S = (0.75·Intellect + 0.25·Social)·Consciousness.</summary>
        public static double AccuracyFor(double intellect, double social, double consciousness = 1.0)
        {
            double s = (0.75 * intellect + 0.25 * social) * consciousness;
            return Math.Max(0.25, Math.Min(1.0, 0.25 + 0.0375 * s));
        }
    }
}
