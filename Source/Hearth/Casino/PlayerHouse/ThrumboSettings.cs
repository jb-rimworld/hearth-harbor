// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Hearth.CasinoMath;

namespace Hearth.Casino.PlayerHouse
{
    public enum ThrumboGame { Slots = 0, Roulette = 1, Blackjack = 2 }

    /// <summary>Where the stake comes from and where winnings go: the colony account of Silverline, or silver on the map (storage, or inside the range of a trade beacon).</summary>
    public enum PayFrom { Bank = 0, Silver = 1, Goods = 2 }   // Goods: only in the history ("Raise the stakes…"); a seat is never Goods

    /// <summary>Settings of The Lucky Thrumbo (KONZEPT 6.13, K14–K16), key prefix "thrumbo.". The edge shift follows the "Hausvorteil extern" row of 9.2.</summary>
    public static class TS
    {
        public const string EdgeShift = "thrumbo.edgeShift";
        public const string MinBet = "thrumbo.minBet";
        /// <summary>1.1.0 (F29): the table limit is optional and off by default.</summary>
        public const string LimitOn = "thrumbo.tableLimit";
        public const string MaxBetShare = "thrumbo.maxBetShare";
        public const string MaxBetFloor = "thrumbo.maxBetFloor";
        public const string MaxBetCap = "thrumbo.maxBetCap";
        public const string AllIn = "thrumbo.raiseTheStakes";
        public const string PawnSpread = "thrumbo.pawnbrokerSpread";

        /// <summary>9.2 "Hausvorteil extern, Spieler gegen Haus": percentage points added to the edge of every game (Comped … Why?).</summary>
        static readonly float[] edgeAnchors = { -0.03f, -0.01f, -0.005f, 0f, 0.01f, 0.02f, 0.03f };

        public static void Register()
        {
            const ModuleId M = ModuleId.Casino;
            HSettings.Percent(M, "Lucky Thrumbo", EdgeShift, 0f, -0.10f, 0.15f, "Lucky Thrumbo: house edge shift",
                "Percentage points added to the house edge of every Lucky Thrumbo game (base: slots 8 %, roulette 2.7 %, blackjack by the rules). 0 = the standard edges; negative numbers are in your favour. Follows the difficulty slider.",
                hardMin: -0.5f, hardMax: 0.5f, tiers: edgeAnchors);
            HSettings.Silver(M, "Lucky Thrumbo", MinBet, 1, 1, 100, "Lucky Thrumbo: smallest stake (silver)", "The smallest stake the Lucky Thrumbo accepts.");
            HSettings.Bool(M, "Lucky Thrumbo", LimitOn, false, "Lucky Thrumbo: table limit",
                "When on, one stake is at most a share of your net worth (the three settings below). Off: you can stake everything you have.");
            HSettings.Percent(M, "Lucky Thrumbo", MaxBetShare, 0.05f, 0.005f, 0.5f, "Lucky Thrumbo: table limit (share of net worth)",
                "Only when the table limit is on: one stake may be at most this share of your net worth (account plus silver at home, minus debts), between the floor and the cap below.", hardMin: 0.0001f, hardMax: 1f);
            HSettings.Silver(M, "Lucky Thrumbo", MaxBetFloor, 50, 1, 1000, "Lucky Thrumbo: table limit, floor (silver)", "Only when the table limit is on: even a poor colony may stake at least this much per round.");
            HSettings.Silver(M, "Lucky Thrumbo", MaxBetCap, 50000, 100, 1000000, "Lucky Thrumbo: table limit, cap (silver)", "Only when the table limit is on: no stake is ever higher than this.");
            HSettings.Bool(M, "Lucky Thrumbo", AllIn, true, "Lucky Thrumbo: \"Raise the stakes\"",
                "Adds the button \"Raise the stakes…\" to the Lucky Thrumbo: stake goods in range of a powered orbital trade beacon instead of silver, and from the content intensity Standard also colony animals and prisoners (like vanilla selling). Colonists, slaves, children and guests are never offered.");
            HSettings.Percent(M, "Lucky Thrumbo", PawnSpread, 0.10f, 0f, 0.5f, "Lucky Thrumbo: pawnbroker spread for staked goods",
                "Staked goods count for what the house's pawnbroker would pay (the vanilla sell price with your best negotiator) minus this spread and a fixed 2 % fee.", hardMin: 0f, hardMax: 0.9f);
        }

        public static double Shift => HSettings.Get(EdgeShift);
        public static Money MinStake => HSettings.GetSilver(MinBet);
    }

    /// <summary>The exact odds of the house at the current settings: base edge plus the shift, on the shared casino maths.</summary>
    public static class ThrumboOdds
    {
        public const double BaseSlotRtp = 0.92, BaseRouletteEdge = 0.027;

        static readonly Dictionary<long, SlotPaytable> slotCache = new Dictionary<long, SlotPaytable>();

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        public static double SlotRtp(double shift) => Clamp(BaseSlotRtp - shift, 0.3, 1.5);
        public static double RouletteEdge(double shift) => Clamp(BaseRouletteEdge + shift, -0.2, 0.6);

        public static SlotPaytable Slots(SlotVolatility v, double shift)
        {
            double rtp = SlotRtp(shift);
            long key = (long)v * 1000003L + (long)Math.Round(rtp * 1e6);
            if (!slotCache.TryGetValue(key, out var pt)) { pt = SlotPaytable.For(v, rtp); if (slotCache.Count > 64) slotCache.Clear(); slotCache[key] = pt; }
            return pt;
        }

        public static RouletteRules Roulette(double shift) => RouletteRules.ForEdge(RouletteEdge(shift));

        /// <summary>The Thrumbo deals plain rules: 3:2, dealer stands on soft 17. The shift is applied to the winnings (see <see cref="BlackjackWinScale"/>).</summary>
        public static BlackjackRules BlackjackRules => BlackjackRules.Standard;
        public static BlackjackSolver Solver => BlackjackSolver.Get(BlackjackRules, 1.0);

        static double expectedWin = -1;
        /// <summary>Expected winnings of a hand with perfect play, in stakes (sampled once with a fixed seed, so it is the same everywhere).</summary>
        public static double ExpectedWinPerHand
        {
            get
            {
                if (expectedWin >= 0) return expectedWin;
                var rng = new RngStream(0x7448524F4D42UL, 11);
                var solver = Solver; double plus = 0; const int n = 60000;
                for (int i = 0; i < n; i++) { var r = solver.PlayRound(rng); if (r.Result > 0) plus += r.Result; }
                return expectedWin = plus / n;
            }
        }

        /// <summary>Factor on every winning hand so that the house edge moves by <paramref name="shift"/> (for perfect play; worse play moves it a little less).</summary>
        public static double BlackjackWinScale(double shift)
        {
            double e = ExpectedWinPerHand; if (e <= 1e-6) return 1.0;
            return Clamp(1.0 - shift / e, 0.0, 3.0);
        }

        /// <summary>House edge of blackjack with perfect play at this shift.</summary>
        public static double BlackjackEdge(double shift) => Solver.HouseEdge + shift;
    }
}
