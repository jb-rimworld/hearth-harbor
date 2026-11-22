// SPDX-License-Identifier: MIT
// Pure maths of the DLC integration (KONZEPT chapter 11, 8.16). No game references, so it is unit-tested in Source/Tests.
using System;
using System.Collections.Generic;

namespace Hearth
{
    /// <summary>Stance of an ideoligion towards one of our money precepts (11.4). NotAThing = no precept (no effect).</summary>
    public enum MoneyStance { NotAThing = 0, Abhorrent = 1, Disapproved = 2, Accepted = 3, Sacred = 4 }

    public static class DlcMath
    {
        // ------------------------------------------------------------------ indulgences (8.16, 11.4)
        public const double RepeatBase = 1.5;
        public const int TicksPerQuadrum = 15 * 60000;
        public const int TicksPerYear = 60 * 60000;

        /// <summary>Price multiplier of a stance. Sacred is 25 % cheaper, Accepted pays list price; Disapproved the list price too.</summary>
        public static double StanceFactor(MoneyStance s) => s == MoneyStance.Sacred ? 0.75 : 1.0;

        /// <summary>Final price in silver: base × indulgence factor × stance × 1.5^k, k = purchases of the same item by the same pawn this year.</summary>
        public static double IndulgencePrice(double basePrice, double indulgenceFactor, MoneyStance stance, int repeatsThisYear)
        {
            if (basePrice <= 0) return 0;
            double p = basePrice * Math.Max(0, indulgenceFactor) * StanceFactor(stance) * Math.Pow(RepeatBase, Math.Max(0, repeatsThisYear));
            return Math.Max(1, Math.Round(p, MidpointRounding.AwayFromZero));
        }

        /// <summary>How many of the given purchase ticks lie inside the window [now - window, now].</summary>
        public static int CountInWindow(IEnumerable<int> ticks, int now, int window)
        {
            int n = 0;
            foreach (var t in ticks) if (now - t >= 0 && now - t < window) n++;
            return n;
        }

        /// <summary>Goodwill gained by paying a faction of the same faith: +1 per 500 silver, at most +5 per quadrum (8.16).</summary>
        public static int FactionGoodwill(double silver, int alreadyGainedThisQuadrum)
        {
            int raw = (int)Math.Floor(silver / 500.0);
            int room = Math.Max(0, 5 - Math.Max(0, alreadyGainedThisQuadrum));
            return Math.Max(0, Math.Min(raw, room));
        }

        // ------------------------------------------------------------------ AI ideoligion rolls (11.4)
        /// <summary>
        /// Weights for [NotAThing, Abhorrent, Disapproved, Accepted, Sacred] of a random AI ideoligion for one theme.
        /// theme: "indulgences" or "ascension". Memes are plain defNames (Guilty, HighLife, Transhumanist, ...).
        /// </summary>
        public static double[] AiStanceWeights(string theme, ICollection<string> memes)
        {
            double[] w = { 60, 12, 12, 10, 6 };
            bool ind = theme == "indulgences";
            double accept = 1, abhorrent = 1;
            bool Has(string m) => memes != null && memes.Contains(m);
            if (ind)
            {
                if (Has("Guilty")) accept *= 3;
                if (Has("HighLife")) accept *= 2;
                if (Has("PainIsVirtue")) abhorrent *= 3;
            }
            else
            {
                if (Has("Transhumanist")) accept *= 3;
                if (Has("Supremacist")) accept *= 2;
                if (Has("Individualist")) accept *= 2;
                if (Has("PainIsVirtue")) abhorrent *= 2;
            }
            if (Has("Loyalist")) accept *= 2;
            if (Has("Proselytizer")) accept *= 1.5;
            if (Has("FleshPurity")) abhorrent *= 2;
            if (Has("Collectivist")) abhorrent *= 2;
            if (Has("NaturePrimacy")) abhorrent *= 2;
            w[(int)MoneyStance.Accepted] *= accept; w[(int)MoneyStance.Sacred] *= accept;
            w[(int)MoneyStance.Abhorrent] *= abhorrent;
            return w;
        }

        public static MoneyStance PickStance(double[] weights, double roll01)
        {
            double sum = 0; foreach (var x in weights) sum += x;
            double r = Math.Max(0, Math.Min(0.999999999, roll01)) * sum;
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r < 0) return (MoneyStance)i; }
            return MoneyStance.NotAThing;
        }

        // ------------------------------------------------------------------ luck (11.1, 8.11)
        /// <summary>RTP after a luck shift. The shift is positive only while the result stays below the cap, so no trait gives a positive expectation.</summary>
        public static double LuckRtp(double baseRtp, double shift, double cap = 0.995)
        {
            double r = baseRtp + shift;
            if (shift > 0) r = Math.Min(r, Math.Max(baseRtp, cap));
            return Math.Max(0.0, Math.Min(r, 0.999));
        }

        /// <summary>Every 10th lottery ticket is free for a lucky person (counter counts bought tickets, starting at 1).</summary>
        public static bool FreeLottoTicket(int ticketNumber) => ticketNumber > 0 && ticketNumber % 10 == 0;

        // ------------------------------------------------------------------ guest shrine (11.6)
        /// <summary>Quality 0..1 of a guest chapel from room impressiveness, number of seats and the share of matching style.</summary>
        public static double ShrineQuality(double impressiveness, int seats, double styleShare)
        {
            double imp = Math.Max(0, Math.Min(1, impressiveness / 50.0));
            double seat = Math.Max(0, Math.Min(1, seats / 4.0));
            double style = Math.Max(0, Math.Min(1, styleShare));
            return 0.5 * imp + 0.3 * seat + 0.2 * style;
        }
        /// <summary>"A place to pray": +2 .. +6 mood in five stages.</summary>
        public static int PrayerStage(double quality) => Math.Max(0, Math.Min(4, (int)Math.Round(quality * 4.0)));
        public static int PrayerMood(double quality) => 2 + PrayerStage(quality);
        /// <summary>Guest satisfaction gain of one prayer (satisfaction runs from -1 to +1).</summary>
        public static double PrayerSatisfaction(double quality) => 0.04 + 0.08 * Math.Max(0, Math.Min(1, quality));
        /// <summary>Donation of one visit: 1-3 % of the remaining budget times certainty. roll01 picks inside the range.</summary>
        public static double ShrineDonation(double remainingBudget, double certainty, double roll01)
            => Math.Max(0, remainingBudget) * (0.01 + 0.02 * Math.Max(0, Math.Min(1, roll01))) * Math.Max(0, Math.Min(1, certainty));
        /// <summary>Share of the remaining budget a guest spends on indulgences at the altar: Accepted 2-6 %, Sacred 5-12 %.</summary>
        public static double GuestIndulgenceShare(MoneyStance s, double roll01)
        {
            roll01 = Math.Max(0, Math.Min(1, roll01));
            if (s == MoneyStance.Sacred) return 0.05 + 0.07 * roll01;
            if (s == MoneyStance.Accepted) return 0.02 + 0.04 * roll01;
            return 0;
        }

        // ------------------------------------------------------------------ nobles (11.7)
        public static double SatisfactionFactor(double satisfaction)
        {
            satisfaction = Math.Max(-1, Math.Min(1, satisfaction));
            return satisfaction >= 0 ? 1.0 + 0.25 * satisfaction : 1.0 + 0.5 * satisfaction;
        }
        /// <summary>Payment of a noble stay: base per noble and day × (1 + 0.15 × seniority / 100) × satisfaction factor.</summary>
        public static double NoblePayment(double basePerNobleDay, int nobles, double days, int seniority, double satisfaction)
            => Math.Max(0, basePerNobleDay) * Math.Max(0, nobles) * Math.Max(0, days) * (1.0 + 0.15 * Math.Max(0, seniority) / 100.0) * SatisfactionFactor(satisfaction);
        /// <summary>Honour for the colony's title holder: +1 .. +4 by seniority (Knight 300 = 1 ... Count 600 = 3, Duke 700+ = 4).</summary>
        public static int NobleHonor(int seniority) => Math.Max(1, Math.Min(4, seniority / 200));
        /// <summary>Compensation share of forced billeting by tier: Overbooked (4) 25 %, One-Star (5) 10 %, Why? (6) 0. Below 4 billeting never happens.</summary>
        public static double BilletingCompensation(float tier)
        {
            if (tier < 4f) return 0.25;
            if (tier >= 6f) return 0.0;
            if (tier <= 5f) return 0.25 + (0.10 - 0.25) * (tier - 4f);
            return 0.10 * (6f - tier);
        }
        /// <summary>Goodwill loss of a badly lodged forced guest by satisfaction: -5 (just unhappy) .. -15 (furious); 0 when satisfied.</summary>
        public static int BilletingPenalty(double satisfaction)
        {
            if (satisfaction >= -0.1) return 0;
            double t = Math.Min(1.0, (-0.1 - satisfaction) / 0.9);
            return -(int)Math.Round(5 + 10 * t);
        }
    }
}
