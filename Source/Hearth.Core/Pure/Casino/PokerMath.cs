// SPDX-License-Identifier: MIT
// Pure code (no game references).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hearth.CasinoMath
{
    /// <summary>
    /// "Rimdeck": players play against each other, the house only takes a rake from the pot (KONZEPT 6.6).
    /// A hand is one ante from every player; the pot minus the rake goes to the winner. Skill tilts who wins:
    /// for two players the edge of A over B is 0.08·tanh((P_A − P_B)/6) of the pot, with P = 0.55·Intellect + 0.45·Social.
    /// For more players each seat is tilted by its skill against the table average and the odds are renormalised.
    /// </summary>
    public static class RimdeckMath
    {
        public const double SkillEdge = 0.08;
        public const double SkillScale = 6.0;

        public static double Skill(double intellect, double social) => 0.55 * intellect + 0.45 * social;

        /// <summary>Rake taken from a pot: rate × pot, capped at <paramref name="cap"/> (all in silver).</summary>
        public static double Rake(double pot, double rate, double cap) => Math.Min(pot * rate, cap);

        /// <summary>Probability that each seat wins the hand (sums to 1).</summary>
        public static double[] WinProbabilities(IReadOnlyList<double> skills)
        {
            int n = skills.Count;
            if (n == 0) return new double[0];
            if (n == 1) return new[] { 1.0 };
            double mean = skills.Average();
            var w = new double[n]; double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double dev = n == 2 ? (skills[i] - skills[1 - i]) : (skills[i] - mean);
                w[i] = Math.Max(0.001, 1.0 / n + SkillEdge * Math.Tanh(dev / SkillScale) * (n == 2 ? 1.0 : 2.0 / n));
                sum += w[i];
            }
            for (int i = 0; i < n; i++) w[i] /= sum;
            return w;
        }

        /// <summary>Expected net result of seat i per hand, in silver, with the rake taken (negative on average).</summary>
        public static double ExpectedNet(IReadOnlyList<double> skills, int seat, double ante, double rate, double cap)
        {
            int n = skills.Count; double pot = ante * n;
            double rake = Rake(pot, rate, cap);
            return WinProbabilities(skills)[seat] * (pot - rake) - ante;
        }

        /// <summary>The house's share of the money staked: rake ÷ pot (exact, every hand).</summary>
        public static double HouseEdge(int players, double ante, double rate, double cap)
            => players <= 0 ? 0 : Rake(ante * players, rate, cap) / (ante * players);

        public static readonly string[] HandNames = { "high card", "a pair", "two pair", "three of a kind", "a straight", "a flush", "a full house", "four of a kind", "a straight flush" };
        static readonly double[] handWeights = { 17.4, 43.8, 23.5, 4.8, 3.9, 3.0, 2.6, 0.7, 0.3 };

        public sealed class Hand { public int Winner; public double Pot, Rake, Payout; public string WinningHand; public double[] Chances; }

        /// <summary>Deals one hand for the given seats. Only the winner's seat index and the money move; the hand name is flavour.</summary>
        public static Hand Deal(RngStream rng, IReadOnlyList<double> skills, double ante, double rate, double cap)
        {
            int n = skills.Count;
            var probs = WinProbabilities(skills);
            double r = rng.NextDouble(), acc = 0; int win = n - 1;
            for (int i = 0; i < n; i++) { acc += probs[i]; if (r < acc) { win = i; break; } }
            double pot = ante * n, rake = Rake(pot, rate, cap);
            double tot = handWeights.Sum(), x = rng.NextDouble() * tot, hacc = 0; int h = 0;
            for (int i = 0; i < handWeights.Length; i++) { hacc += handWeights[i]; if (x < hacc) { h = i; break; } }
            return new Hand { Winner = win, Pot = pot, Rake = rake, Payout = pot - rake, WinningHand = HandNames[h], Chances = probs };
        }
    }
}
