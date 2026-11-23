// SPDX-License-Identifier: MIT
using Hearth;
using Verse;

namespace Silverline
{
    public enum BankResearchMode { Full = 0, Off = 1 }

    /// <summary>All tunable numbers of the bank (rule 4). Difficulty anchors from KONZEPT 9.2: Comped … Standard Room … Why are you hurting yourself?</summary>
    public static class BankSettings
    {
        public static readonly double[] ProfitAnchors = { 100, 2.0, 1.5, 1.2, 1.0, 0.7, 0.1 };
        public static readonly float[] CollectionStageAnchors = { 0f, 1f, 2f, 4f, 5f, 6f, 7f };
        public static readonly float[] AngerAnchors = { 0f, 0.2f, 0.5f, 1.0f, 1.5f, 2.2f, 3.5f };

        public static void Register()
        {
            const ModuleId B = ModuleId.Bank;
            // Interest and profitability (8.8)
            HSettings.LogFloat(B, "Interest & profitability", "bank.profit", 1.2f, 0.01f, 100f,
                "Bank M per game year (profitability)", "How much invested money grows on average per game year (60 days). This is the bank's own M: it drives the account interest and loan rates. The investment M (Investments page) drives the markets; the account never pays more than Mattress Plus at that M. Keep both close together. 1.0 = plus/minus nothing, 1.2 = +20 % a year, 0 = the money disappears. Log scale 0.01 to 100; type a number for the exact value. Follows the difficulty slider until you set it yourself.",
                hardMin: 0f, hardMax: 1000f, tiers: new[] { 100f, 2.0f, 1.5f, 1.2f, 1.0f, 0.7f, 0.1f });
            // F21 (1.1.0): the account is a savings account. It pays only a share of the market's surplus, so investing always pays more.
            HSettings.Float(B, "Interest & profitability", "bank.accountShare", 0.2f, 0f, 1f,
                "Account interest: share of the market return", "A bank account only pays this fraction of the market's surplus (M - 1) a year, and never more than the savings product Mattress Plus (which follows the investment profitability). 0.2 means: at M 1.2 the account pays +4 % a year (the savings product Mattress Plus pays +8 %, the index about +20 %); at M 2 it pays +20 %. Below M 1 it loses a little, never more than 10 % a year. Both profitability sliders (this bank M and the investment M) are worth keeping close together. Each bank adds its own small bonus or malus. Investments are not affected.",
                hardMin: 0f, hardMax: 2f);
            // Old key from before 1.1.0 (account followed M^beta). Kept registered so profiles and saves that stored it stay valid; it is not used any more.
            HSettings.Float(B, "Interest & profitability", "bank.accountBeta", 1.0f, 0f, 2f,
                "Account interest exponent (not used since 1.1.0)", "Replaced by 'Account interest: share of the market return'. Kept so older save games load; changing it does nothing.", nerd: true, hardMin: 0f, hardMax: 4f);
            // Fees and services (8.2, 8.3)
            HSettings.Bool(B, "Account", "bank.directDeposit", true, "Route till money straight to the bank",
                "Once an account exists, the tills of your businesses are emptied into the account at midnight instead of leaving silver on the floor.");
            HSettings.Bool(B, "Account", "bank.tradeDeposit", false, "Auto-deposit trade silver",
                "Off by default, so selling to a trader still hands you coins you can spend at once. With this on (and direct deposit on), silver you receive from traders and trade ships is moved into the account right after the deal (at the direct deposit fee). Caravans are not affected.");
            HSettings.Percent(B, "Fees", "bank.feeFactor", 1.0f, 0f, 3f, "Bank fee level", "Multiplies all fees of your bank. 0 % = no fees at all.", hardMin: 0f, hardMax: 10f);
            HSettings.Percent(B, "Fees", "bank.directDepositFee", 0.005f, 0f, 0.05f,
                "Fee for direct deposit", "Fee on money that is routed straight into the account (till takings). 0 to 100 %.", hardMin: 0f, hardMax: 1f);
            HSettings.Silver(B, "Fees", "bank.switchFee", 50, 0, 500, "Fee for changing banks", "What it costs to move your account to another bank (KONZEPT 2.2: 50 silver).");
            HSettings.Float(B, "Services", "bank.depositDelayHours", 1f, 0f, 6f, "Deposit delay without a branch (hours)", "Silver put into the account without a bank counter or ATM nearby arrives after this many hours.", hardMin: 0f, hardMax: 48f);
            HSettings.Float(B, "Services", "bank.podMinHours", 2f, 0.1f, 24f, "Drop pod delivery: earliest (hours)", "Withdrawals without a counter or ATM come by drop pod after at least this long.", hardMin: 0.01f, hardMax: 120f);
            HSettings.Float(B, "Services", "bank.podMaxHours", 12f, 0.1f, 48f, "Drop pod delivery: latest (hours)", "... and at most this long.", hardMin: 0.01f, hardMax: 240f);
            HSettings.Silver(B, "Services", "bank.atmDailyLimit", 500, 100, 5000, "ATM daily limit", "The most silver you can take out of ATMs per day.");
            HSettings.Silver(B, "Services", "bank.podMaxLoad", 2000, 500, 20000, "Largest single drop pod", "A big withdrawal comes in drop pods of about this many silver each, never more than 10 pods per withdrawal (huge sums make heavier pods instead of more of them). It does not limit how much you can deposit or withdraw.", nerd: true);
            HSettings.Enum(B, "Research", "bank.researchMode", BankResearchMode.Full, "Banking research", "Full: ATMs, night safes, branches and loans need their research. Off: everything is available from the start. (The global Research mode in the core settings does the same for every module.)");
            // Wealth and mood (8.4, 8.10)
            HSettings.Percent(B, "Wealth and mood", "bank.wealthShare", 1.0f, 0f, 1.5f, "Bank balance counts for the storyteller (%)",
                "How much of the account counts towards the colony wealth that the storyteller uses for raid points. 100 % = fair: depositing money does not make raids smaller. 0 = the bank is a hiding place.", hardMin: 0f, hardMax: 1.5f);
            HSettings.Bool(B, "Wealth and mood", "bank.moodOn", true, "Colony mood from the balance", "A small thought (from -4 to +3, doubled for greedy colonists, none for ascetics) depending on the net worth per colonist.");
            HSettings.Percent(B, "Wealth and mood", "bank.moodScale", 1.0f, 0.1f, 5f, "Wealth tier thresholds", "Scales the net worth thresholds of the mood tiers (Broke … Too Big To Fail).", nerd: true, hardMin: 0.01f, hardMax: 100f);
            // Loans and collections (8.9, 9.2, 9.4)
            HSettings.Silver(B, "Loans", "bank.loanFloor", 250, 0, 2000, "Starter credit", "Minimum credit limit for a colony with a fine score, so a young colony can borrow a little.");
            HSettings.Percent(B, "Loans", "bank.loanPortfolioShare", 0.5f, 0f, 1f, "Investments counted for credit limits", "How much of your investments (at market value) counts as wealth when the bank decides how much you may borrow. 100 % = fully, 0 = not at all. The net worth shown in the lobby always counts them fully.", nerd: true, hardMin: 0f, hardMax: 1f);
            HSettings.Bool(B, "Sound", "bank.sounds", true, "Bank menu sounds", "Clicks, coin counting, stamps in the bank window, and the lottery win or lose sound when a draw you bought tickets for is held.");
            HSettings.Int(B, "Loans", "bank.loanIntervalDays", 5, 1, 15, "Days between instalments", "Loan instalments are due every this many days.", hardMin: 1, hardMax: 30);
            HSettings.Int(B, "Loans", "bank.loanMaxTermDays", 120, 15, 240, "Longest loan term (days)", "", nerd: true, hardMin: 5, hardMax: 600);
            HSettings.Percent(B, "Loans", "bank.lateFee", 0.05f, 0f, 0.2f, "Late fee", "Charged on the overdue instalment when a loan reaches collection stage 2.", hardMin: 0f, hardMax: 1f);
            HSettings.Int(B, "Collections", "bank.maxCollectionStage", 4, 0, 7, "Highest collection stage",
                "The collection ladder: 1 reminder, 2 late fee, 3 offset against deposits, 4 friendly visit, 5 seizure team, 6 collection raid, 7 bounty. Standard Room stops at 4: you pay your debt and lose nothing but a little mood.", hardMin: 0, hardMax: 7, tiers: CollectionStageAnchors);
            HSettings.Float(B, "Collections", "bank.collectionAnger", 1.0f, 0f, 3.5f, "Collections anger (raid size)", "Multiplies the size of seizure teams and collection raids. 0 turns the violent stages off.", hardMin: 0f, hardMax: 10f, tiers: AngerAnchors);
            HSettings.Bool(B, "Collections", "bank.bailout", true, "Too Big To Fail bailout", "The bank First Rim Bank of Too Big To Fail rescues a ruined former tycoon once per game year (an interest-free loan, not a gift).");
            HSettings.Percent(B, "Collections", "bank.seizureShare", 0.25f, 0f, 1f, "Black Ledger seizure share", "If the Black Ledger seizes your account (1 % risk per quadrum), this share of the balance is gone.", nerd: true, hardMin: 0f, hardMax: 1f);
        }

        public static float Tier => HearthProfile.Current?.Tier ?? HearthMod.Template.Tier;

        /// <summary>Profitability M per game year. Follows the difficulty tier in log space until the player overrides it.</summary>
        public static double ProfitM
        {
            get
            {
                var s = HSettings.Find("bank.profit");
                if (s == null) return 1.2;
                return HSettings.Get(s);   // LogFloat: the tier default is interpolated in log space by the settings core
            }
        }
        public static double DefaultProfitFor(float tier) => FinanceMath.LogInterp(ProfitAnchors, tier);

        /// <summary>The highest collection stage; the special switch core.ironBank always means the top of the ladder (7).</summary>
        public static int MaxCollectionStage => CoreSettings.Switch("core.ironBank") ? 7 : HSettings.GetInt("bank.maxCollectionStage");
        /// <summary>Raid size factor of the collection stages. The special switch core.ironBank means stage 7 is really reached, so the violent stages never run at 0: it floors the factor at 1.</summary>
        public static double CollectionAnger { get { double a = HSettings.Get("bank.collectionAnger"); return CoreSettings.Switch("core.ironBank") ? System.Math.Max(a, 1.0) : a; } }
        public static bool ResearchOff => CoreSettings.ResearchOff || HSettings.GetInt("bank.researchMode") == (int)BankResearchMode.Off;
        /// <summary>Share of the market surplus the account pays (bank.accountShare). Replaces the old exponent bank.accountBeta.</summary>
        public static double AccountShare => HSettings.Get("bank.accountShare");
        /// <summary>The account never pays more than the savings product Mattress Plus at the investment M (F21: the account stays below investing, whatever the two M sliders say).</summary>
        public static double AccountCeiling => Hearth.Markets.MarketMath.SavingsMultiple(FinanceSettings.M);
        /// <summary>Yearly multiple of the account for a bank with the given interest modifier, with the ceiling applied. One place for the tick, the lobby, the terms page and the comparison.</summary>
        public static double AccountMultiple(double bankMod) => FinanceMath.AccountYearlyMultiple(ProfitM, AccountShare, bankMod, AccountCeiling);
        /// <summary>Only this share of the investments counts for credit limits (bank.loanPortfolioShare).</summary>
        public static double LoanPortfolioShare => HSettings.Get("bank.loanPortfolioShare");
        public static bool Sounds => HSettings.GetBool("bank.sounds");
        /// <summary>The bank's fee multiplier: the setting times the colony's fee factor (a penny-pinching colonist reads every contract: x0.8).</summary>
        public static double FeeFactor => HSettings.Get("bank.feeFactor") * ColonistFeeFactor;

        static int feeTick = -99999; static double feeCached = 1.0;
        static BankSettings() { HearthGame.OnGameInit += ResetFeeCache; }
        /// <summary>Lowest HH_BankFeeFactor stat among the free colonists (1 without such a pawn or without the stat def). Cached for 2500 ticks.</summary>
        public static double ColonistFeeFactor
        {
            get
            {
                if (Current.Game == null || Find.TickManager == null) return 1.0;
#if HH_TESTS
                if (!ColonistFeeInTests) return 1.0;   // a random penny-pincher among the starting colonists must not move the bank suites' fee checks
#endif
                int now = Find.TickManager.TicksGame;
                if (now >= feeTick && now - feeTick < 2500) return feeCached;
                feeTick = now; feeCached = 1.0;
                var stat = DefDatabase<RimWorld.StatDef>.GetNamedSilentFail("HH_BankFeeFactor");
                if (stat != null)
                    foreach (var p in RimWorld.PawnsFinder.AllMaps_FreeColonists)
                        if (p.RaceProps.Humanlike && !p.Dead) { double v = RimWorld.StatExtension.GetStatValue(p, stat); if (v < feeCached) feeCached = System.Math.Max(0.1, v); }
                return feeCached;
            }
        }
#if HH_TESTS
        /// <summary>Tests: the colonists' fee factor is only read while a suite switches this on (dlc.wiring).</summary>
        public static bool ColonistFeeInTests;
#endif
        /// <summary>Tests: forget the cached colony fee factor.</summary>
        public static void ResetFeeCache() => feeTick = -99999;
    }
}
