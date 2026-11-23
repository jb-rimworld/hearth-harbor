// SPDX-License-Identifier: MIT
using System;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    /// <summary>All casino settings (key prefix "casino."). Difficulty anchors are the 7 values of KONZEPT 9.2: Comped … Why?</summary>
    public static class CS
    {
        // Odds ---------------------------------------------------------------------------------------
        public const string SlotRtp = "casino.slotRtp";
        public const string RouletteEdge = "casino.rouletteEdge";
        public const string BjPays = "casino.blackjackPays";
        public const string BjH17 = "casino.blackjackH17";
        public const string PokerRake = "casino.pokerRake";
        public const string PokerRakeCap = "casino.pokerRakeCap";
        // Tables and limits ----------------------------------------------------------------------------
        public const string LimitsOn = "casino.tableLimits";
        public const string MinBet = "casino.minBet";
        public const string MaxBet = "casino.maxBet";
        public const string RiskLimit = "casino.houseRiskLimit";
        public const string Croupier = "casino.croupierRequired";
        public const string RoundsSlot = "casino.roundsPerHour.slot";
        public const string RoundsRoulette = "casino.roundsPerHour.roulette";
        public const string RoundsBlackjack = "casino.roundsPerHour.blackjack";
        public const string RoundsPoker = "casino.roundsPerHour.poker";
        public const string HouseFloat = "casino.houseFloat";
        // Guests ---------------------------------------------------------------------------------------
        public const string GuestShare = "casino.guestBudgetShare";
        public const string GuestBetShare = "casino.guestBetShare";
        public const string GuestStopLoss = "casino.guestStopLoss";
        public const string GuestWinTarget = "casino.guestWinTarget";
        public const string GuestMinBuyIn = "casino.guestMinBuyIn";
        public const string ChipBonus = "casino.chipSpendBonus";
        public const string ChipTakeHome = "casino.chipTakeHome";
        // Colonists ------------------------------------------------------------------------------------
        public const string ColonistChips = "casino.colonistChips";
        public const string JoyGain = "casino.joyGain";
        public const string ToleranceFactor = "casino.toleranceFactor";
        public const string AddictionP0 = "casino.addictionP0";
        public const string AddictionFree = "casino.addictionFreeSessions";
        public const string AddictionGrowth = "casino.addictionGrowth";
        public const string AbstinenceDays = "casino.abstinenceDays";
        public const string FatalDespair = "casino.fatalDespair";

        static readonly float[] slotRtpAnchors = { 0.98f, 0.965f, 0.95f, 0.92f, 0.88f, 0.85f, 0.82f };
        static readonly float[] rouletteAnchors = { 0.0054f, 0.00945f, 0.0135f, 0.027f, 0.0526f, 0.06575f, 0.0789f };
        static readonly float[] rakeAnchors = { 0.012f, 0.021f, 0.03f, 0.05f, 0.07f, 0.0875f, 0.105f };
        static readonly float[] rakeCapAnchors = { 1f, 1.4f, 2f, 3f, 5f, 6f, 7f };

        public static void Register()
        {
            const ModuleId M = ModuleId.Casino;
            // 9.2 "Hausvorteil des Spiels": B&B / Standard / Overbooked from 6.6, the others follow with fixed factors
            HSettings.Percent(M, "House odds", SlotRtp, 0.92f, 0.70f, 1.05f, "Slot machine payout (RTP)",
                "Share of the stakes a One-Armed Muffalo pays back in the long run. 92 % = house edge 8 %. Above 100 % the house loses money.", hardMin: 0.5f, hardMax: 1.5f, tiers: slotRtpAnchors);
            HSettings.Percent(M, "House odds", RouletteEdge, 0.027f, 0.0f, 0.10f, "Roulette house edge",
                "0.54 % to 7.9 %: small edges use a la partage wheel, 2.7 % is one zero, over 4 % is a double-zero wheel. Every bet gets exactly this edge.", hardMin: -0.05f, hardMax: 0.5f, tiers: rouletteAnchors);
            HSettings.Float(M, "House odds", BjPays, 1.5f, 1.0f, 1.5f, "Blackjack pays (blackjack bonus)", "3:2 = 1.5, 6:5 = 1.2. Changes the house edge by about 1.4 points.", hardMin: 1.0f, hardMax: 2.0f, tiers: new[] { 1.5f, 1.5f, 1.5f, 1.5f, 1.2f, 1.2f, 1.2f });
            HSettings.Int(M, "House odds", BjH17, 0, 0, 1, "Blackjack dealer hits soft 17", "0 = dealer stands on soft 17, 1 = dealer hits (about +0.2 points of edge).", hardMin: 0, hardMax: 1, tiers: new[] { 0f, 0f, 0f, 0f, 1f, 1f, 1f });
            HSettings.Percent(M, "House odds", PokerRake, 0.05f, 0.0f, 0.15f, "Rimdeck rake", "The share of every pot the house keeps (up to the rake cap).", hardMin: 0f, hardMax: 0.5f, tiers: rakeAnchors);
            HSettings.Float(M, "House odds", PokerRakeCap, 3f, 0.5f, 10f, "Rimdeck rake cap (silver)", "The most the house takes from one pot.", hardMin: 0.1f, hardMax: 1000f, tiers: rakeCapAnchors);

            HSettings.Bool(M, "Tables", LimitsOn, true, "Enforce table limits", "On: bets stay between the minimum and maximum bet below. Off: only the house risk rule limits bets.");
            HSettings.Float(M, "Tables", MinBet, 0.5f, 0.05f, 10f, "Minimum bet (silver)", "Smallest stake. Fractions are fine: the bet is not paid out in coins.", hardMin: 0.01f, hardMax: 1000f);
            HSettings.Float(M, "Tables", MaxBet, 25f, 1f, 200f, "Maximum bet (silver)", "Largest stake at any game while limits are enforced.", hardMin: 0.1f, hardMax: 100000f);
            HSettings.Bool(M, "Tables", RiskLimit, true, "Protect the house from ruin", "Caps each bet at cash ÷ (4 · σ · √rounds per day): a bad night cannot break the house. The cap shrinks when the cash is low.");
            HSettings.Bool(M, "Tables", Croupier, true, "Tables need a croupier", "Roulette, blackjack and Rimdeck only run for guests while a colonist with the Serving work type staffs the table. Slot machines never need one.");
            HSettings.Int(M, "Tables", RoundsSlot, 40, 10, 200, "Slot spins per hour", "How fast a machine spins, per in-game hour (2500 ticks, so 40 is a spin every 1 to 2 real seconds at normal speed).", hardMin: 2, hardMax: 2500);
            HSettings.Int(M, "Tables", RoundsRoulette, 10, 3, 60, "Roulette spins per hour", "Spins per in-game hour.", hardMin: 2, hardMax: 600);
            HSettings.Int(M, "Tables", RoundsBlackjack, 18, 5, 90, "Blackjack hands per hour", "Hands per in-game hour.", hardMin: 2, hardMax: 600);
            HSettings.Int(M, "Tables", RoundsPoker, 8, 3, 50, "Rimdeck hands per hour", "Hands per in-game hour.", hardMin: 2, hardMax: 600);
            HSettings.Silver(M, "Tables", HouseFloat, 300, 0, 2000, "House float (silver)", "Cash the house keeps in its drawer. Whatever it wins above this (plus what guests currently play with) goes to the colony till when the casino closes.");

            HSettings.Percent(M, "Guests", GuestShare, 0.25f, 0.05f, 0.6f, "Gambling budget of guests", "Share of a guest's remaining budget they bring to the tables (before personality).");
            HSettings.Percent(M, "Guests", GuestBetShare, 0.10f, 0.01f, 0.30f, "Guest bet size", "A guest stakes this share of their gambling money per round. The default is larger than in a real casino because the tables run far fewer rounds per in-game hour; hourly takings stay about the same.");
            HSettings.Percent(M, "Guests", GuestStopLoss, 0.4f, 0.1f, 1f, "Guests stop after losing", "A guest leaves the tables after losing this share of their gambling money.");
            HSettings.Percent(M, "Guests", GuestWinTarget, 0.5f, 0.1f, 3f, "Guests cash out after winning", "A guest cashes out after winning this share on top of their gambling money.");
            HSettings.Silver(M, "Guests", GuestMinBuyIn, 8, 1, 100, "Smallest buy-in (silver)", "Guests with less to spend do not gamble.");
            HSettings.Percent(M, "Guests", ChipBonus, 0.05f, 0f, 0.3f, "Spending bonus with a cashier's cage", "Guests bring this much more to the tables when a cage sells chips (\"doesn't feel like money\").");
            HSettings.Percent(M, "Guests", ChipTakeHome, 0.03f, 0f, 0.1f, "Chips taken home", "At most this share of a guest's winnings stays on them as chips when they leave: pure house profit (\"chip float\").");

            HSettings.Silver(M, "Colonists", ColonistChips, 100, 20, 500, "House chips per session", "Colonists gamble with worthless house chips: no colony silver is ever lost at the tables.");
            HSettings.Float(M, "Colonists", JoyGain, 2.5f, 1f, 5f, "Gambling joy multiplier", "Joy gain compared with a vanilla poker table.", nerd: true, hardMin: 0.1f, hardMax: 20f);
            HSettings.Float(M, "Colonists", ToleranceFactor, 0.6f, 0.1f, 1f, "Gambling tolerance build-up", "How quickly joy tolerance builds up compared with vanilla (0.6 = 40 % slower).", nerd: true, hardMin: 0f, hardMax: 1f);
            HSettings.Percent(M, "Addiction", AddictionP0, 0.08f, 0f, 0.4f, "Gambling addiction chance p₀", "Chance of addiction after the third session within three days; it grows ×1.35 with each further session. 0 turns addiction off.", tiers: new[] { 0f, 0f, 0.04f, 0.08f, 0.10f, 0.13f, 0.18f });
            HSettings.Int(M, "Addiction", AddictionFree, 3, 1, 10, "Sessions before addiction can happen", "Below this many sessions in three days nothing happens.", nerd: true, hardMin: 1, hardMax: 100);
            HSettings.Float(M, "Addiction", AddictionGrowth, 1.35f, 1.0f, 2.0f, "Growth per extra session", "p = p₀ · growth^(sessions − 3).", nerd: true, hardMin: 1f, hardMax: 5f);
            HSettings.Float(M, "Addiction", AbstinenceDays, 10f, 3f, 30f, "Abstinence days per addiction stage", "Days without any session needed to cure each stage of the addiction (stage 3 = 3 × this).", hardMin: 1f, hardMax: 100f, tiers: new[] { 7f, 7f, 7f, 10f, 12f, 15f, 20f });
            HSettings.Bool(M, "Addiction", FatalDespair, false, "Fatal despair (not implemented)", "Reserved. Nothing in this module can kill a colonist; the switch has no effect.", nerd: true);
        }

        // Readers --------------------------------------------------------------------------------------
        public static float F(string key) => HSettings.Get(key);
        public static bool B(string key) => HSettings.GetBool(key);

        public static double SlotTargetRtp => HSettings.Get(SlotRtp);
        public static RouletteRules Roulette => RouletteRules.ForEdge(HSettings.Get(RouletteEdge));
        public static BlackjackRules Blackjack => new BlackjackRules(HSettings.GetInt(BjH17) >= 1, HSettings.Get(BjPays));
        public static double PokerRakeRate => HSettings.Get(PokerRake);
        public static double PokerCap => HSettings.Get(PokerRakeCap);

        public static int RoundsPerHour(CasinoGame g)
        {
            switch (g)
            {
                case CasinoGame.Slots: return Math.Max(2, HSettings.GetInt(RoundsSlot));
                case CasinoGame.Roulette: return Math.Max(2, HSettings.GetInt(RoundsRoulette));
                case CasinoGame.Blackjack: return Math.Max(2, HSettings.GetInt(RoundsBlackjack));
                default: return Math.Max(2, HSettings.GetInt(RoundsPoker));
            }
        }
        /// <summary>Ticks between two rounds of a game (2500 ticks per hour).</summary>
        public static int RoundInterval(CasinoGame g) => Math.Max(1, 2500 / RoundsPerHour(g));
    }

    public enum CasinoGame { Slots, Roulette, Blackjack, Poker }
}
