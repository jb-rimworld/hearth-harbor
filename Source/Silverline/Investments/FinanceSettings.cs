// SPDX-License-Identifier: MIT
using System;
using Hearth;
using Hearth.Markets;

namespace Silverline
{
    /// <summary>Settings of the investment products and the lottery (keys finance.* and lottery.*). Tier anchors follow KONZEPT 9.2.</summary>
    public static class FinanceSettings
    {
        static bool registered;
        // Tier anchors (Comped … Why?) of the yearly multiple M, interpolated in log space.
        static readonly float[] MAnchors = { 100f, 2.0f, 1.5f, 1.2f, 1.0f, 0.7f, 0.1f };
        static readonly float[] HazardTier = { 0.2f, 0.5f, 0.75f, 1f, 1.25f, 1.6f, 2.2f };

        public static void Register()
        {
            if (registered) return; registered = true;
            const string cat = "Investments";
            // Stored as the plain multiple M on a log slider (tier anchors interpolate in log space). Older saves stored ln(M) under finance.profitLog: converted on read.
            HSettings.LogFloat(ModuleId.Bank, cat, "finance.profit", 1.2f, 0.1f, 100f, "Profitability (yearly multiple M)",
                "How much invested money grows on average per game year (60 days) for the reference fund RimIndex 50: 1.2 means +20 % a year, 1.0 means plus/minus nothing, below 1 loses money. Log scale 0.1 to 100; type a number for the exact value. Follows the difficulty slider until you set it yourself.",
                hardMin: 0.0001f, hardMax: 100f, tiers: MAnchors, legacyKey: "finance.profitLog", legacyToValue: ln => (float)DetMath.Exp(ln));
            HSettings.Float(ModuleId.Bank, cat, "finance.volMult", 1f, 0.2f, 3f, "Volatility ×", "Multiplies how wildly prices swing day to day. 1 = the numbers of the design (index 1.5 % a day, memecoins 15 %).", hardMin: 0f, hardMax: 10f);
            HSettings.Float(ModuleId.Bank, cat, "finance.jumpMult", 1f, 0f, 3f, "Price jumps ×", "Multiplies how often assets make their own jumps (quarterly numbers, crypto jumps, memecoin pumps). The long-run average does not change: the maths compensates.", hardMin: 0f, hardMax: 10f);
            HSettings.Float(ModuleId.Bank, cat, "finance.hazardMult", 1f, 0f, 3f, "Disasters ×", "Multiplies crashes, rug pulls, defaults, exchange hacks and bank runs. The long-run average does not change, only how often it hurts.", hardMin: 0f, hardMax: 10f, tiers: HazardTier);
            HSettings.Bool(ModuleId.Bank, cat, "finance.cryptoOn", true, "Crypto assets", "Offer RimCoin and the memecoins.");
            HSettings.Bool(ModuleId.Bank, cat, "finance.memeOn", true, "Memecoins", "A new memecoin appears every few days and is delisted after three. Most holders lose money, a few get rich.");
            HSettings.Bool(ModuleId.Bank, cat, "finance.nftOn", true, "NFTs", "Ownership certificates of pictures. Selling takes one to four days and costs a spread.");
            HSettings.Float(ModuleId.Bank, cat, "finance.memeInterval", 5f, 1f, 20f, "Days between new memecoins", "Average number of days until the next memecoin is listed.", true, 0.5f, 60f);
            HSettings.Bool(ModuleId.Bank, cat, "finance.newsMessages", true, "Market news as messages", "Show crashes, rallies and hacks as short messages (only when you hold something affected).");
            HSettings.Bool(ModuleId.Bank, cat, "finance.fallbackWindow", false, "Investments in the comms console menu", "Also offer the investment screens in the comms console float menu, even when the bank window is available.", true);

            const string omen = "Omen Exchange";
            HSettings.Float(ModuleId.Bank, omen, "finance.omenGamma", (float)OmenMath.DefaultGamma, 0.5f, 1.2f, "Longshot distortion γ", "Below 1 the exchange overprices unlikely outcomes (the classic favourite–longshot bias). 1 = quotes equal the true odds.", true, 0.3f, 1.5f);
            HSettings.Percent(ModuleId.Bank, omen, "finance.omenFee", (float)OmenMath.DefaultFee, 0f, 0.1f, "Omen fee", "Share of every stake kept by the exchange (and again when you close a position early).");
            HSettings.Percent(ModuleId.Bank, omen, "finance.omenSpread", (float)OmenMath.DefaultSpread, 0f, 0.15f, "Omen spread", "Bookmaker's margin: the YES price and the NO price add up to 1 plus this.", true);
            HSettings.Percent(ModuleId.Bank, omen, "finance.omenCapA", 0.15f, 0.01f, 0.5f, "Position limit, chance markets", "Largest share of your net worth in one Omen chance market.");
            HSettings.Percent(ModuleId.Bank, omen, "finance.omenCapB", 0.05f, 0.01f, 0.5f, "Position limit, world markets", "Largest share of your net worth in one Omen market on a world event.");

            const string proj = "Faction projects";
            HSettings.Bool(ModuleId.Bank, proj, "finance.projectsOn", true, "Faction projects and Project Shorts", "Factions announce projects; you can invest (Long) or bet against them (Short).");
            HSettings.Percent(ModuleId.Bank, proj, "finance.projectStakePct", 0.10f, 0.01f, 0.5f, "Stake limit per project", "Largest stake on one project, as share of your net worth.");
            HSettings.Percent(ModuleId.Bank, proj, "finance.projectTotalPct", 0.20f, 0.02f, 0.8f, "Stake limit, all projects", "Largest total of all open project bets (Longs and Shorts together), as share of your net worth.");
            HSettings.Percent(ModuleId.Bank, proj, "finance.projectYearCapPct", 0.15f, 0.02f, 1f, "Yearly profit cap", "Net profit from Longs and Shorts per game year is capped at this share of your net worth at the start of the year. Above it the bank pays the stake back only.");

            const string lot = "Lottery";
            HSettings.Bool(ModuleId.Bank, lot, "lottery.globalOn", true, "Glitterworld Grand Draw", "The global lottery: a draw every few days, tickets bought in the bank window.");
            HSettings.Silver(ModuleId.Bank, lot, "lottery.globalPrice", 5, 1, 50, "Global ticket price", "Price of one ticket of the global draw.", true);
            HSettings.Percent(ModuleId.Bank, lot, "lottery.globalQuote", 0.5f, 0.2f, 0.95f, "Global payout share", "Share of the ticket sales that goes into prizes (the jackpot gets 40 % of it).", true);
            HSettings.Int(ModuleId.Bank, lot, "lottery.globalInterval", 5, 1, 30, "Days between global draws", "A draw every this many days.", true);
            HSettings.Silver(ModuleId.Bank, lot, "lottery.jackpotCap", 5_000_000, 100_000, 50_000_000, "Global jackpot cap", "The jackpot rolls over until it reaches this amount.", true);
            HSettings.Float(ModuleId.Bank, lot, "lottery.internationalScale", 1f, 0f, 5f, "Simulated players ×", "Multiplies the tickets the simulated players of all settlements buy for the global draw.", true, 0f, 20f);
            HSettings.Bool(ModuleId.Bank, lot, "lottery.colonyOn", false, "Colony lottery", "Run your own lottery: your guests and the simulated players of friendly settlements buy tickets, you keep what is not paid out.");
            HSettings.Silver(ModuleId.Bank, lot, "lottery.colonyPrice", 5, 1, 50, "Colony ticket price", "Price of one ticket of your own lottery.");
            HSettings.Percent(ModuleId.Bank, lot, "lottery.colonyQuote", 0.5f, 0.2f, 0.95f, "Colony payout share", "Share of the ticket price that goes into the pot. The rest is yours. A better share than the global draw attracts more buyers.");
            HSettings.Int(ModuleId.Bank, lot, "lottery.colonyInterval", 5, 1, 30, "Days between colony draws", "A draw every this many days; the jackpot rolls over.");
            HSettings.Float(ModuleId.Bank, lot, "lottery.colonyFee", 0f, 0f, 20f, "Acceptance fee per ticket", "Extra silver per ticket that you keep (it raises the price buyers see).", false, 0f, 100f);
            HSettings.Percent(ModuleId.Bank, lot, "lottery.colonyJackpotShare", 0.5f, 0.1f, 0.9f, "Pot share: jackpot", "Share of the pot for 4 right numbers.");
            HSettings.Percent(ModuleId.Bank, lot, "lottery.colonySecondShare", 0.3f, 0.05f, 0.8f, "Pot share: 2nd rank", "Share of the pot for 3 right numbers.");
            HSettings.Percent(ModuleId.Bank, lot, "lottery.colonyThirdShare", 0.2f, 0.05f, 0.8f, "Pot share: 3rd rank", "Share of the pot for 2 right numbers.");
            HSettings.Silver(ModuleId.Bank, lot, "lottery.colonyJackpotCap", 200_000, 1000, 5_000_000, "Colony jackpot cap", "The jackpot of your lottery rolls over until this amount.", true);
            HSettings.Float(ModuleId.Bank, lot, "lottery.demandBase", (float)new LotteryMath.DemandInput().Basis, 0.005f, 0.5f, "Ticket demand base", "Tickets per person per day at neutral conditions, the base of the demand formula.", true, 0f, 5f);
        }

        // reading helpers ------------------------------------------------------------------------------
        public static double M => Math.Max(1e-6, HSettings.Get("finance.profit"));
        public static double LnMValue => DetMath.Ln(M);
        public static double Vol => HSettings.Get("finance.volMult");
        public static double Jump => HSettings.Get("finance.jumpMult");
        public static double Hazard => HSettings.Get("finance.hazardMult");
        public static bool CryptoOn => HSettings.GetBool("finance.cryptoOn");
        public static bool MemeOn => HSettings.GetBool("finance.memeOn");
        public static bool NftOn => HSettings.GetBool("finance.nftOn");
        public static double OmenGamma => HSettings.Get("finance.omenGamma");
        public static double OmenFee => HSettings.Get("finance.omenFee");
        public static double OmenSpread => HSettings.Get("finance.omenSpread");

        public static ProjectMath.Limits ProjectLimits() => new ProjectMath.Limits
        { PerProjectPct = HSettings.Get("finance.projectStakePct"), TotalPct = HSettings.Get("finance.projectTotalPct"), YearGainPct = HSettings.Get("finance.projectYearCapPct") };
    }
}
