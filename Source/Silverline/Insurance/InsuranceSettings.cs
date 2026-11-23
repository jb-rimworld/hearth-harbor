// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>Per-trick switch (N51, 9.7): follows the "Sales pressure" slider, always on, or off.</summary>
    public enum TrickMode { Follows = 0, On = 1, Off = 2 }

    /// <summary>How claims reach the claims department (1.1.0, F27).</summary>
    public enum ClaimMode { Automatic = 0, ByForm = 1 }

    /// <summary>What happens when a pensioner works more than the allowed hours in the timetable (1.1.0, F26).</summary>
    public enum PensionRuleMode { Off = 0, WarnOnly = 1, Enforce = 2 }

    /// <summary>Settings of Clawback Mutual (module Bank, key prefix "insurance."). Anchors from KONZEPT 9.2 / B.6.</summary>
    public static class InsSettings
    {
        public const string ClaimsAttitude = "insurance.claimsAttitude";
        public const string Loading = "insurance.loading";
        public const string SalesPressure = "insurance.salesPressure";
        public const string WaitingDays = "insurance.waitingDays";
        public const string NoticeDays = "insurance.noticeDays";
        public const string BillingDays = "insurance.billingDays";
        public const string FormError = "insurance.formError";
        public const string RetentionDialogs = "insurance.retentionDialogs";
        public const string AssessmentDays = "insurance.assessmentDays";
        public const string Negotiation = "insurance.negotiation";
        public const string RequireResearch = "insurance.requireResearch";
        public const string TrickCap = "insurance.trickCap";
        public const string TrickPreTicked = "insurance.trick.preTicked";
        public const string TrickRetention = "insurance.trick.retention";
        public const string TrickFormError = "insurance.trick.formError";
        public const string TrickUpsell = "insurance.trick.upsell";
        public const string TrickBadge = "insurance.trick.badge";
        public const string MaxPersonSum = "insurance.maxPersonSum";
        public const string MaxPropertySum = "insurance.maxPropertySum";
        public const string ClaimLetters = "insurance.claimLetters";
        public const string HospitalCapDays = "insurance.hospitalCapDays";
        public const string HospitalCapWindow = "insurance.hospitalCapWindow";
        public const string ChangeFee = "insurance.changeFee";
        public const string ChangeWaitingDays = "insurance.changeWaitingDays";
        public const string ChangeLimit = "insurance.changeLimitPerQuadrum";
        public const string PensionRule = "insurance.pensionRule";
        public const string PensionMaxHours = "insurance.pensionMaxWorkHours";
        public const string ClaimModeKey = "insurance.claimMode";
        public const string DeadlineScale = "insurance.deadlineScale";
        public const string ClaimReminder = "insurance.claimReminder";
        public const string FormTextAi = "insurance.formTextAi";
        public const string ShowStatusColumn = "insurance.showStatusColumn";
        public const string ShowStatusTab = "insurance.showStatusTab";

        static bool registered;

        public static void Register()
        {
            if (registered) return;
            registered = true;
            var m = ModuleId.Bank;
            const string cat = "Insurance (Clawback Mutual)";
            HSettings.Percent(m, cat, ClaimsAttitude, 0.15f, 0f, 1f, "Claims attitude",
                "How eagerly the claims department finds a reason not to pay. 0 % \"Never weasels\" · 15 % \"Fine print\" (Standard Room) · 40 % \"Creative accounting\" · 75 % \"Our lawyers are bored\" · 100 % \"Basically always\".",
                tiers: new[] { 0f, 0.05f, 0.10f, 0.15f, 0.30f, 0.50f, 0.80f });
            HSettings.Percent(m, cat, Loading, 0.25f, 0f, 1f, "Insurance loading",
                "Surcharge on the expected damage that makes up the premium. On average the insurer keeps this share; 0 % is actuarially fair.",
                hardMax: 3f, tiers: new[] { 0f, 0.10f, 0.15f, 0.25f, 0.35f, 0.50f, 0.80f });
            HSettings.Percent(m, cat, SalesPressure, 0.30f, 0f, 1f, "Sales pressure",
                "How hard Clawback Mutual sells: pre-ticked add-ons, retention offers, form errors, upsell hints. Each trick also has its own switch below.",
                tiers: new[] { 0f, 0.05f, 0.10f, 0.30f, 0.60f, 0.80f, 1.0f });
            HSettings.Int(m, cat, WaitingDays, 5, 0, 30, "Waiting period (days)",
                "Cover begins after this many days (\"which begins after cover begins\"). Damage inside the waiting period is not paid.", hardMax: 120);
            HSettings.Int(m, cat, NoticeDays, 3, 0, 60, "Cancellation notice (days)",
                "A cancellation takes effect at the first quadrum change at least this many days after you file it. Standard Room 3 days, Overbooked one quadrum, \"Why?\" a year.", hardMax: 120,
                tiers: new[] { 0f, 0f, 1f, 3f, 15f, 15f, 60f });
            HSettings.Int(m, cat, BillingDays, 5, 1, 15, "Premium billing interval (days)",
                "Premiums are debited automatically every this many days (from the bank account, otherwise the till or silver in storage).", hardMax: 30);
            HSettings.Percent(m, cat, FormError, 0.10f, 0f, 1f, "Cancellation form error chance",
                "Clause 30: the cancellation comes back with a form error. On Standard Room it is re-filed automatically and the deadline stays; harder levels lose a quadrum.",
                tiers: new[] { 0f, 0f, 0.05f, 0.10f, 0.25f, 0.40f, 0.60f });
            HSettings.Int(m, cat, RetentionDialogs, 1, 0, 3, "Retention dialogs",
                "How many times Desmond Tally (Senior Retention Specialist) talks you out of cancelling. Standard Room: one honest offer.", hardMax: 5,
                tiers: new[] { 0f, 0f, 1f, 1f, 3f, 3f, 3f });
            HSettings.Int(m, cat, AssessmentDays, 15, 0, 60, "Accident assessment (days)",
                "An accident plan assesses the invalidity grade this many days after the accident (a quadrum by default). Until then the letter shows a preview.", hardMax: 120);
            HSettings.Bool(m, cat, Negotiation, true, "Allow negotiating claims", "Claims can be negotiated (up to three rounds). On Standard Room negotiating never costs anything.");
            HSettings.Bool(m, cat, RequireResearch, true, "Insurance needs research",
                "Policies unlock with the research projects SL_Insurance (Bare Minimum, Good Enough) and SL_Actuarial (Premium, Wanderlust). Off: everything is available at once.");
            HSettings.Percent(m, cat, TrickCap, 0.05f, 0f, 1f, "Cap on sales-trick costs",
                "All sales tricks together may cost at most this share of the yearly premiums; the rest is paid back as \"Courtesy credit\" in the yearly letter.",
                nerd: true, tiers: new[] { 0.05f, 0.05f, 0.05f, 0.05f, 0.15f, 0.30f, 1.0f });
            HSettings.Enum(m, cat, TrickPreTicked, TrickMode.Follows, "Trick: pre-ticked add-ons", "Add-ons that are ticked for you (follows Sales pressure, always, or never).");
            HSettings.Enum(m, cat, TrickRetention, TrickMode.Follows, "Trick: retention offers", "Retention dialogs when you cancel.");
            HSettings.Enum(m, cat, TrickFormError, TrickMode.Follows, "Trick: form errors", "Cancellations that come back with a form error.");
            HSettings.Enum(m, cat, TrickUpsell, TrickMode.Follows, "Trick: upsell hints", "Letters that suggest more or better cover (never within five days after a death).");
            HSettings.Enum(m, cat, TrickBadge, TrickMode.Follows, "Trick: \"Most colonies choose Premium\" badge", "The badge on the Premium column (and, harder, the greyed-out \"Premium Plus\").");
            HSettings.Silver(m, cat, MaxPersonSum, 25000, 500, 100000, "Maximum personal sum", "Highest basic sum of an accident, retirement or travel policy (also at most twice the market value of the pawn).", nerd: true);
            HSettings.Silver(m, cat, MaxPropertySum, 60000, 500, 500000, "Maximum property sum", "Highest sum insured of a property policy.", nerd: true);
            HSettings.Int(m, cat, HospitalCapDays, 5, 1, 15, "Rim Health Plan: paid sickbed days",
                "The most sickbed days the Rim Health Plan pays per colonist inside the window below (a hospital-indemnity cap; it stops a chronic illness from paying more than the premium costs).", hardMax: 60);
            HSettings.Int(m, cat, HospitalCapWindow, 15, 1, 60, "Rim Health Plan: window (days)",
                "The cap above counts the paid days of every colonist over this many days (a quadrum by default).", hardMax: 120);
            HSettings.Int(m, cat, ChangeFee, 0, 0, 500, "Fee for changing a policy (silver)",
                "What Clawback Mutual charges for every change of sum, tier or add-ons of a running policy (\"administrative effort\"). Free on Standard Room.", hardMax: 5000,
                tiers: new[] { 0f, 0f, 0f, 0f, 25f, 60f, 120f });
            HSettings.Int(m, cat, ChangeWaitingDays, 3, 0, 30, "Waiting period after an upgrade (days)",
                "After you raise the sum, the tier or add an add-on, claims for events inside this waiting period are still assessed with the old cover (a policy raised one minute before a raid does not pay at the new level). Lowering cover has no waiting period.", hardMax: 120,
                tiers: new[] { 0f, 1f, 2f, 3f, 5f, 8f, 15f });
            HSettings.Int(m, cat, ChangeLimit, 0, 0, 6, "Changes per policy and quadrum",
                "How often one policy may be changed per quadrum. 0 = unlimited (Standard Room); Overbooked and harder levels limit it.", hardMax: 20,
                tiers: new[] { 0f, 0f, 0f, 0f, 2f, 1f, 1f });
            HSettings.Enum(m, cat, PensionRule, PensionRuleMode.Enforce, "Pension work rule",
                "Whoever draws a daily pension (Golden Years, Good Enough or Premium, age 60+) may work only a few hours per day in the timetable. Enforce: strikes cost the pension (2nd: this period's pension, 3rd: the entitlement). Warn only: letters and the alert, no penalty. Off: no rule.");
            HSettings.Int(m, cat, PensionMaxHours, 2, 0, 8, "Pension: allowed work hours per day",
                "Hours per day a pensioner may have set to Work in the timetable. Anything, Joy, Sleep and Meditate count as free time.", hardMax: 24,
                tiers: new[] { 4f, 3f, 2f, 2f, 2f, 1f, 0f });
            HSettings.Enum(m, cat, ClaimModeKey, ClaimMode.ByForm, "How claims are filed",
                "By form: an insured event brings a \"claim possible\" letter and you file the claim on a form before the deadline (the outcome is rolled exactly as before; the form is cosmetic apart from the payout choice). Automatic: claims are filed for you at the moment of damage.");
            HSettings.Float(m, cat, DeadlineScale, 1f, 0.25f, 4f, "Claim deadlines (factor)",
                "Multiplies the filing deadline of every policy (Wanderlust 5 days, Legal Shield 5 days, property about 15 days at Good Enough; Premium +50 %, Bare Minimum -30 %).", hardMin: 0.1f, hardMax: 20f,
                tiers: new[] { 2f, 1.5f, 1.25f, 1f, 0.7f, 0.5f, 0.3f });
            HSettings.Bool(m, cat, ClaimReminder, true, "Remind me before a claim deadline", "A short reminder letter two days before a filing window closes.");
            HSettings.Bool(m, cat, FormTextAi, true, "AI reply to the claim statement", "When the AI flavour module is on and you agreed to it, the claims department answers your statement in its own words (the amount and the outcome are not changed). Without it the statement is stored and ignored.", nerd: true);
            HSettings.Bool(m, cat, ShowStatusColumn, true, "Insurance column in the colonist tables", "Shows the insurance status of every colonist in the Assign table (only once the first policy exists).", nerd: true);
            HSettings.Bool(m, cat, ShowStatusTab, true, "Insurance tab on colonists", "Adds an Insurance tab to the inspector of colonists (only once the first policy exists).", nerd: true);
            HSettings.Bool(m, cat, ClaimLetters, true, "Letters for every claim", "Clawback Mutual writes a letter for every filed claim (at most one \"claim filed\" notice per day) and every decision.", nerd: true);
        }

        public static float Tier => HearthProfile.Current?.Tier ?? HearthMod.Template.Tier;
        /// <summary>"Overbooked or harder": the tier from which the nastier variants of the tricks and clauses apply.</summary>
        public static bool Hard => Tier >= 3.5f;

        public static double Attitude => HSettings.Get(ClaimsAttitude);
        public static double LoadingValue => HSettings.Get(Loading);
        public static double Pressure => HSettings.Get(SalesPressure);
        public static int Waiting => HSettings.GetInt(WaitingDays);
        public static int Notice => HSettings.GetInt(NoticeDays);
        public static int Billing => Math.Max(1, HSettings.GetInt(BillingDays));
        public static int HospitalCap => Math.Max(1, HSettings.GetInt(HospitalCapDays));
        public static bool ByForm => HSettings.GetEnum<ClaimMode>(ClaimModeKey) == ClaimMode.ByForm;
        public static PensionRuleMode PensionMode => HSettings.GetEnum<PensionRuleMode>(PensionRule);
        public static int PensionHours => HSettings.GetInt(PensionMaxHours);
        public static int HospitalWindow => Math.Max(1, HSettings.GetInt(HospitalCapWindow));

        public static bool TrickActive(string key, double threshold = 0.04)
        {
            var mode = HSettings.GetEnum<TrickMode>(key);
            if (mode == TrickMode.On) return true;
            if (mode == TrickMode.Off) return false;
            return Pressure >= threshold;
        }
        public static double FormErrorChance
        {
            get
            {
                var mode = HSettings.GetEnum<TrickMode>(TrickFormError);
                if (mode == TrickMode.Off) return 0;
                double p = HSettings.Get(FormError);
                return mode == TrickMode.On ? Math.Max(p, 0.10) : p;
            }
        }
        public static int RetentionCount
        {
            get
            {
                var mode = HSettings.GetEnum<TrickMode>(TrickRetention);
                if (mode == TrickMode.Off) return 0;
                int n = HSettings.GetInt(RetentionDialogs);
                return mode == TrickMode.On ? Math.Max(1, n) : n;
            }
        }
        public static int PreTickedCount(int offered)
        {
            var mode = HSettings.GetEnum<TrickMode>(TrickPreTicked);
            if (mode == TrickMode.Off) return 0;
            double p = mode == TrickMode.On ? Math.Max(Pressure, 0.30) : Pressure;
            return InsuranceMath.PreTickedRiders(p, offered);
        }
    }

    /// <summary>Text helper: all strings are Keyed under HH.Ins.*.</summary>
    public static class Ins
    {
        public static string T(string key, params object[] a)
        {
            string full = "HH.Ins." + key;
            if (!full.CanTranslate()) { HLog.Once("insKey:" + key, LogLevel.Warn, "Bank", "Missing translation key " + full); return key; }
            if (a == null || a.Length == 0) return full.Translate().Resolve();
            var args = new NamedArgument[a.Length];
            for (int i = 0; i < a.Length; i++) args[i] = (a[i] ?? "").ToString();
            return full.Translate(args).Resolve();
        }
        public static string Silver(double v) => v.ToString(v >= 100 ? "#,##0" : "#,##0.0#", System.Globalization.CultureInfo.InvariantCulture) + " silver";
        public static string Pct(double v) => (v * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " %";
        public static Money M(double silver) => Money.FromSilver(silver);

        public static string TierName(CoverTier t) => T(t == CoverTier.BareMinimum ? "Tier.Bare" : t == CoverTier.GoodEnough ? "Tier.Good" : "Tier.Premium");
        public static string Slogan(int i) => T("Slogan." + (1 + ((i % 8) + 8) % 8));
        public static int Now => Find.TickManager?.TicksGame ?? 0;
        public static string StageLabel(double grade) { int s = InsuranceMath.Stage(grade); return T("Stage." + s); }
        public static string Duration(int ticks) => ticks.ToStringTicksToPeriod();
    }
}
