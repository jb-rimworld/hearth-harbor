// SPDX-License-Identifier: MIT
// Pure code. Lotteries (KONZEPT 8.14): global "Glitterworld Grand Draw" 5 of 40 + star 1 of 10, and the colony's own pot-model lottery.
using System;
using System.Collections.Generic;

namespace Hearth.Markets
{
    public static class LotteryMath
    {
        public static double Choose(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            k = Math.Min(k, n - k); double r = 1;
            for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return Math.Round(r);
        }

        /// <summary>P(exactly k of the drawn numbers are on a ticket) = C(m,k) C(n-m,m-k) / C(n,m) where the draw takes m of n numbers.</summary>
        public static double MatchProbability(int pool, int picks, int k) => Choose(picks, k) * Choose(pool - picks, picks - k) / Choose(pool, picks);

        public sealed class Rank
        {
            public string Id; public int Matches; public bool Star; public double Share; public bool IsJackpot;
            public double Probability;
        }

        /// <summary>Global lottery: 5 of 40 + star 1 of 10. Pool shares add up to 1 (jackpot 40 %, lower classes descending).</summary>
        public static readonly int GlobalPool = 40, GlobalPicks = 5, GlobalStarPool = 10;
        public static readonly double JackpotOdds = Choose(40, 5) * 10;   // 6,580,080

        public static List<Rank> GlobalRanks()
        {
            var l = new List<Rank>
            {
                new Rank { Id = "r1", Matches = 5, Star = true,  Share = 0.40, IsJackpot = true },
                new Rank { Id = "r2", Matches = 5, Star = false, Share = 0.08 },
                new Rank { Id = "r3", Matches = 4, Star = true,  Share = 0.08 },
                new Rank { Id = "r4", Matches = 4, Star = false, Share = 0.10 },
                new Rank { Id = "r5", Matches = 3, Star = true,  Share = 0.10 },
                new Rank { Id = "r6", Matches = 3, Star = false, Share = 0.12 },
                new Rank { Id = "r7", Matches = 2, Star = true,  Share = 0.12 },
            };
            foreach (var r in l) r.Probability = MatchProbability(GlobalPool, GlobalPicks, r.Matches) * (r.Star ? 1.0 / GlobalStarPool : 1.0 - 1.0 / GlobalStarPool);
            return l;
        }

        /// <summary>Colony lottery: 4 of 25, no star. Jackpot 4 right, 2nd 3 right, 3rd 2 right (pot model).</summary>
        public static readonly int ColonyPool = 25, ColonyPicks = 4;
        public static List<Rank> ColonyRanks(double jackpotShare, double secondShare, double thirdShare)
        {
            var l = new List<Rank>
            {
                new Rank { Id = "c1", Matches = 4, Share = jackpotShare, IsJackpot = true },
                new Rank { Id = "c2", Matches = 3, Share = secondShare },
                new Rank { Id = "c3", Matches = 2, Share = thirdShare },
            };
            foreach (var r in l) r.Probability = MatchProbability(ColonyPool, ColonyPicks, r.Matches);
            return l;
        }

        /// <summary>Fixed shares: whatever the sum, normalise so that the shares add up to 1.</summary>
        public static void NormaliseShares(List<Rank> ranks)
        {
            double sum = 0; foreach (var r in ranks) sum += r.Share;
            if (sum <= 0) { foreach (var r in ranks) r.Share = 1.0 / ranks.Count; return; }
            foreach (var r in ranks) r.Share /= sum;
        }

        /// <summary>Expected return per ticket of the pari-mutuel scheme, as share of the ticket price: simply the payout quote (the rest is the "tax on optimism").</summary>
        public static double ExpectedReturn(double payoutQuote, double jackpotCapSpill = 0) => payoutQuote;

        /// <summary>Draw <paramref name="picks"/> distinct numbers from 1..pool (partial Fisher-Yates), sorted.</summary>
        public static int[] DrawNumbers(RngStream rng, int pool, int picks)
        {
            var a = new int[pool]; for (int i = 0; i < pool; i++) a[i] = i + 1;
            for (int i = 0; i < picks; i++) { int j = rng.Range(i, pool); int t = a[i]; a[i] = a[j]; a[j] = t; }
            var res = new int[picks]; Array.Copy(a, res, picks); Array.Sort(res); return res;
        }

        public static int CountMatches(int[] ticket, int[] drawn) { int c = 0; foreach (var x in ticket) foreach (var y in drawn) if (x == y) { c++; break; } return c; }

        /// <summary>Which rank does a ticket win (null = none)?</summary>
        public static Rank RankOf(List<Rank> ranks, int matches, bool star)
        {
            foreach (var r in ranks) if (r.Matches == matches && (r.Star == star || !HasStar(ranks))) return r;
            return null;
        }
        static bool HasStar(List<Rank> ranks) { foreach (var r in ranks) if (r.Star) return true; return false; }

        // ---- pot model -----------------------------------------------------------------------------
        public sealed class RankResult { public Rank Rank; public int CrowdWinners, PlayerWinners; public double Allocation, PerWinner, Paid, Unclaimed; }

        /// <summary>
        /// Splits the prize pools over the winners of each rank. The pot model: only money that is really in the pot is paid
        /// (<paramref name="available"/>); a rank without winners stays in the pot (rolls over). Never pays more than <paramref name="available"/>.
        /// </summary>
        public static List<RankResult> Allocate(List<Rank> ranks, double prizePool, double jackpotCarry, double available, int[] crowdWinners, int[] playerWinners, double jackpotCap)
        {
            var res = new List<RankResult>();
            double budget = Math.Max(0, available);
            for (int i = 0; i < ranks.Count; i++)
            {
                var r = ranks[i];
                double alloc = prizePool * r.Share + (r.IsJackpot ? jackpotCarry : 0);
                int w = (crowdWinners?[i] ?? 0) + (playerWinners?[i] ?? 0);
                var rr = new RankResult { Rank = r, CrowdWinners = crowdWinners?[i] ?? 0, PlayerWinners = playerWinners?[i] ?? 0, Allocation = alloc };
                if (w > 0)
                {
                    double pay = Math.Min(alloc, budget);
                    rr.PerWinner = pay / w; rr.Paid = pay; budget -= pay; rr.Unclaimed = alloc - pay;
                }
                else rr.Unclaimed = alloc;
                res.Add(rr);
            }
            return res;
        }

        /// <summary>Unclaimed money rolls over; the jackpot part is capped, the rest of the overflow is redistributed to the next draw's pool.</summary>
        public static double CapJackpot(double carry, double cap, out double overflow)
        {
            overflow = Math.Max(0, carry - cap);
            return Math.Min(carry, cap);
        }

        // ---- "Internationals": simulated players from all faction settlements ------------------------
        public static double TechFactor(int techLevel)
        {
            switch (techLevel) { case 0: case 1: return 0.2; case 2: return 0.4; case 3: return 0.7; case 4: return 1.0; case 5: return 1.5; default: return 2.0; }
        }
        /// <summary>Tickets of one settlement for one draw: tech factor * wealth index * 200 * noise.</summary>
        public static double InternationalTickets(int techLevel, double wealthIndex, double noise, double scale = 1.0) => TechFactor(techLevel) * wealthIndex * 200.0 * noise * scale;

        /// <summary>Hosting gain per day of the reference colony (silver). The own lottery may earn at most 25 % of it, whatever the settings.</summary>
        public const double ReferenceHostingGainPerDay = 800.0;
        public const double InternationalsPerSettlement = 15.0;

        // ---- colony lottery demand (8.14) --------------------------------------------------------------
        public sealed class DemandInput
        {
            public double Basis = 0.05;              // tickets per person per day at neutral conditions
            public double Reach;                     // guests on the map + internationals with goodwill >= 0
            public double OwnQuote, GlobalQuote = 0.5;
            public double OwnJackpot, GlobalJackpot = 20000;
            public double Renown = 50;               // 0..100
            public double GlobalPrice = 5, TicketPrice = 5, AcceptFee;
        }
        /// <summary>
        /// Renown factor of the demand: 0.5 + renown/100, i.e. 0.5 at renown 0, 0.6 for a brand-new colony (start renown 10), 1.0 at the neutral 50, 1.5 at 100.
        /// It never drops below half of the neutral demand, so a new colony still sells tickets; renown is a bonus of up to +50 %, not a gate.
        /// </summary>
        public static double RenownFactor(double renown) => 0.5 + Math.Max(0.0, Math.Min(100.0, renown)) / 100.0;
        public static double TicketsPerDay(DemandInput d)
        {
            double priceEff = Math.Max(0.01, d.TicketPrice + d.AcceptFee);
            double jr = Math.Max(1.0, d.OwnJackpot) / Math.Max(1.0, d.GlobalJackpot);
            double v = d.Basis * d.Reach * DetMath.Exp(4.0 * (d.OwnQuote - d.GlobalQuote)) * DetMath.Pow(jr, 0.3) * RenownFactor(d.Renown) * DetMath.Pow(d.GlobalPrice / priceEff, 1.5);
            return Math.Max(0, v);
        }
        /// <summary>Internationals buy at most Basis * Reach tickets and only up to 3 times the global price.</summary>
        public static double InternationalTicketsPerDay(DemandInput d, double internationals)
        {
            if (d.TicketPrice + d.AcceptFee > 3.0 * d.GlobalPrice) return 0;
            var copy = new DemandInput { Basis = d.Basis, Reach = internationals, OwnQuote = d.OwnQuote, GlobalQuote = d.GlobalQuote, OwnJackpot = d.OwnJackpot, GlobalJackpot = d.GlobalJackpot, Renown = d.Renown, GlobalPrice = d.GlobalPrice, TicketPrice = d.TicketPrice, AcceptFee = d.AcceptFee };
            return Math.Min(TicketsPerDay(copy), d.Basis * internationals);
        }
        /// <summary>Colony income per day: acceptance fees + (1 - quote) * price on every ticket.</summary>
        public static double ColonyIncomePerDay(double tickets, double price, double quote, double fee) => tickets * (price * (1 - quote) + fee);
    }
}
