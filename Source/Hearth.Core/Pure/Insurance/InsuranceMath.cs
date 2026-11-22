// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
// Insurance maths of Clawback Mutual (KONZEPT 8.13, Anhang B). All amounts are plain silver (double); the game layer converts to Money.
using System;

namespace Hearth.Insurance
{
    /// <summary>The three tiers every policy comes in (K7): "Bare Minimum", "Good Enough" (de: Passt schon), "Premium of Premium".</summary>
    public enum CoverTier { BareMinimum = 0, GoodEnough = 1, Premium = 2 }

    /// <summary>Progression models for accident cover (B.4).</summary>
    public enum ProgressionModel { Linear = 0, P225 = 1, P350 = 2, P500 = 3 }

    /// <summary>Danger group of a pawn (8.13): A no combat/hunting/mining, B hunting/mining/building, C drafted in combat.</summary>
    public enum HazardGroup { A = 0, B = 1, C = 2 }

    public static class InsuranceMath
    {
        public const int DaysPerQuadrum = 15;
        public const int DaysPerYear = 60;
        public const int TicksPerDay = 60000;
        public const double MaxPersonSum = 25000.0;

        // ---- tier table (K7) ------------------------------------------------------------------------------------
        /// <summary>Own share of a property loss (Selbstbehalt): 40 / 15 / 0 %.</summary>
        public static double Deductible(CoverTier t) => t == CoverTier.BareMinimum ? 0.40 : t == CoverTier.GoodEnough ? 0.15 : 0.0;
        /// <summary>Replacement basis: 50 % / 80 % of the time value, Premium 100 % of the replacement value.</summary>
        public static double Replacement(CoverTier t) => t == CoverTier.BareMinimum ? 0.50 : t == CoverTier.GoodEnough ? 0.80 : 1.0;
        /// <summary>"Drückeberger" factor of the tier: how eager the claims department is to find a reason (K7).</summary>
        public static double SlackerFactor(CoverTier t) => t == CoverTier.BareMinimum ? 1.5 : t == CoverTier.GoodEnough ? 1.0 : 0.4;
        /// <summary>Invalidity threshold in percent: 20 / 5 / 1 (B.4).</summary>
        public static double InvalidityThreshold(CoverTier t) => t == CoverTier.BareMinimum ? 20.0 : t == CoverTier.GoodEnough ? 5.0 : 1.0;
        /// <summary>Nominal price index of the tier (K7 table: x0.55, x1.0, x1.9). Informational; real prices come from <see cref="Premium"/>.</summary>
        public static double NominalPriceIndex(CoverTier t) => t == CoverTier.BareMinimum ? 0.55 : t == CoverTier.GoodEnough ? 1.0 : 1.9;
        /// <summary>Payout delay in days after the decision (min, max): 5-10 / 2-4 / immediate.</summary>
        public static void PayoutDelayDays(CoverTier t, out int min, out int max)
        {
            if (t == CoverTier.BareMinimum) { min = 5; max = 10; }
            else if (t == CoverTier.GoodEnough) { min = 2; max = 4; }
            else { min = 0; max = 0; }
        }
        /// <summary>Review time in days (min, max): the claims department's "checking" phase, 1-3 days before desk/clause modifiers.</summary>
        public static void ReviewDays(CoverTier t, out int min, out int max)
        {
            if (t == CoverTier.BareMinimum) { min = 2; max = 3; }
            else if (t == CoverTier.GoodEnough) { min = 1; max = 2; }
            else { min = 0; max = 1; }
        }
        public static ProgressionModel ModelFor(CoverTier t, bool plusRider = false)
            => t == CoverTier.BareMinimum ? ProgressionModel.Linear : t == CoverTier.GoodEnough ? ProgressionModel.P225 : (plusRider ? ProgressionModel.P500 : ProgressionModel.P350);

        // ---- claims attitude -> denial (K8) --------------------------------------------------------------------
        /// <summary>
        /// p_denial = clamp01(attitude * tierFactor * (1 + 0.5 * grey) - 0.2 * legalShield).
        /// attitude = the "Claims attitude" slider 0..1 (default 0.15 on Standard Room). grey = a clause of Anhang B.7 applies.
        /// </summary>
        public static double DenialProbability(double attitude, CoverTier tier, bool grey, bool legalShield)
        {
            double p = attitude * SlackerFactor(tier) * (1.0 + (grey ? 0.5 : 0.0)) - (legalShield ? 0.2 : 0.0);
            return Clamp01(p);
        }

        /// <summary>Probability of a "partly approved" decision once not denied: only with a clause in play.</summary>
        public static double PartialProbability(double pDenial, bool grey) => grey ? Clamp01(Math.Min(1.0 - pDenial, pDenial * 0.8)) : 0.0;

        /// <summary>
        /// Expected share of a loss that is really paid out for this tier and attitude (the E[Auszahlungsquote] of K5):
        /// (1 - deductible) x replacement x (1 - expected denial - half of expected partial cuts).
        /// greyShare = share of claims in which a clause applies (about a third).
        /// </summary>
        public static double ExpectedPayoutRatio(CoverTier tier, double attitude, double greyShare = 0.3)
        {
            double pPlain = DenialProbability(attitude, tier, false, false);
            double pGrey = DenialProbability(attitude, tier, true, false);
            double pDeny = (1 - greyShare) * pPlain + greyShare * pGrey;
            double cut = greyShare * PartialProbability(pGrey, true) * 0.5;
            double keep = Clamp01(1.0 - pDeny - cut);
            return (1.0 - Deductible(tier)) * Replacement(tier) * keep;
        }

        // ---- premium (K5, D39) ----------------------------------------------------------------------------------
        /// <summary>Premium per quadrum = E[damage] x E[payout ratio] x (1 + loading) x no-claims factor x bundle factor.</summary>
        public static double Premium(double expectedDamage, double payoutRatio, double loading, double noClaimsFactor, double bundleFactor)
            => Math.Max(0.0, expectedDamage) * Math.Max(0.0, payoutRatio) * (1.0 + Math.Max(-0.5, loading)) * noClaimsFactor * bundleFactor;

        /// <summary>No-claims class 0..10: -5 % per claim-free quadrum.</summary>
        public static double NoClaimsFactor(int noClaimsClass) => 1.0 - 0.05 * Math.Max(0, Math.Min(10, noClaimsClass));
        /// <summary>After a quadrum: +1 class when claim-free, -3 classes after a claim (K5).</summary>
        public static int NextNoClaimsClass(int noClaimsClass, bool hadClaim)
            => hadClaim ? Math.Max(0, noClaimsClass - 3) : Math.Min(10, noClaimsClass + 1);

        /// <summary>Bundle: 1st x1, 2nd -5 %, 3rd -10 %, from the 4th -12 % on all policies (K5, B.6).</summary>
        public static double BundleFactor(int activePolicies)
            => activePolicies <= 1 ? 1.0 : activePolicies == 2 ? 0.95 : activePolicies == 3 ? 0.90 : 0.88;

        /// <summary>Hazard multiplier from the measured shares of the last quadrum: B x1.6, C x3 (8.13).</summary>
        public static double HazardFactor(double shareB, double shareC)
        {
            shareB = Clamp01(shareB); shareC = Clamp01(shareC);
            if (shareB + shareC > 1.0) { double s = shareB + shareC; shareB /= s; shareC /= s; }
            double shareA = 1.0 - shareB - shareC;
            return shareA * 1.0 + shareB * 1.6 + shareC * 3.0;
        }

        /// <summary>E[damage] per quadrum for one policy: insured value x base risk x hazard x danger, blended with the own claims history.</summary>
        public static double ExpectedDamage(double insuredValue, double baseRisk, double hazard, double dangerMult, double historyPerQuadrum, double historyWeight = 0.35)
        {
            double model = Math.Max(0.0, insuredValue) * Math.Max(0.0, baseRisk) * Math.Max(0.0, hazard) * Math.Max(0.0, dangerMult);
            historyWeight = Clamp01(historyWeight);
            return model * (1.0 - historyWeight) + Math.Max(0.0, historyPerQuadrum) * historyWeight;
        }

        /// <summary>Premium per billing period (a quadrum has 15 days).</summary>
        public static double BillingAmount(double premiumPerQuadrum, int billingDays)
            => premiumPerQuadrum * Math.Max(1, billingDays) / DaysPerQuadrum;

        /// <summary>Retention offer of Desmond Tally: -15 % for two quadrums (B.6).</summary>
        public const double RetentionDiscount = 0.15;
        public const int RetentionQuadrums = 2;

        // ---- sums ----------------------------------------------------------------------------------------------------
        /// <summary>Basic sum of a sum policy: at most 2 x market value of the pawn, at most 25,000 silver (8.13).</summary>
        public static double MaxPersonalSum(double marketValue) => Math.Min(MaxPersonSum, Math.Max(0.0, marketValue) * 2.0);

        /// <summary>Under-insurance: a property sum below the real value pays only pro rata.</summary>
        public static double UnderInsuranceFactor(double sumInsured, double realValue)
            => realValue <= 0 ? 1.0 : Math.Min(1.0, Math.Max(0.0, sumInsured) / realValue);

        /// <summary>
        /// Gross payout for a property loss: loss x replacement x (1 - deductible) x under-insurance, capped by the sum insured.
        /// (Denial, partial cuts and clauses are applied by the claims engine afterwards.)
        /// </summary>
        public static double PropertyPayout(double lossValue, double sumInsured, double realValue, CoverTier tier)
        {
            double v = Math.Max(0.0, lossValue) * Replacement(tier) * (1.0 - Deductible(tier)) * UnderInsuranceFactor(sumInsured, realValue);
            return Math.Min(v, Math.Max(0.0, sumInsured));
        }

        // ---- accident cover (B.4, B.5) ------------------------------------------------------------------------------
        /// <summary>Progression P(g): the share of the basic sum (in percent) that is paid for invalidity grade g (0..100).</summary>
        public static double Progression(ProgressionModel m, double g)
        {
            g = Math.Max(0.0, Math.Min(100.0, g));
            switch (m)
            {
                case ProgressionModel.P225:
                    return g <= 25 ? g : g <= 50 ? 25 + 2 * (g - 25) : 75 + 3 * (g - 50);
                case ProgressionModel.P350:
                    return g <= 25 ? g : g <= 50 ? 25 + 3 * (g - 25) : 100 + 5 * (g - 50);
                case ProgressionModel.P500:
                    return g <= 25 ? g : g <= 50 ? 25 + 3 * (g - 25) : 100 + 8 * (g - 50);
                default: return g;
            }
        }

        /// <summary>Invalidity stage 0 (below 1 %), I..V (B.4).</summary>
        public static int Stage(double g) => g < 1 ? 0 : g < 25 ? 1 : g < 50 ? 2 : g < 75 ? 3 : g < 100 ? 4 : 5;
        public static string StageName(int stage)
        {
            switch (stage)
            {
                case 1: return "Flesh wound";
                case 2: return "Inconvenienced";
                case 3: return "Significantly reimagined";
                case 4: return "Mostly paperwork";
                case 5: return "Total and permanent";
                default: return "Unscathed";
            }
        }

        /// <summary>
        /// Benefit of the accident plan: basic sum x P(grade)/100, zero below the tier threshold.
        /// <paramref name="partialOnly"/> = the grade consists of partial losses only (scars): Bare Minimum does not pay those (B.3).
        /// </summary>
        public static double AccidentBenefit(double sum, CoverTier tier, double grade, bool plusRider = false, bool partialOnly = false)
        {
            grade = Math.Max(0.0, Math.Min(100.0, grade));
            if (grade < InvalidityThreshold(tier)) return 0.0;
            if (partialOnly && tier == CoverTier.BareMinimum) return 0.0;
            return sum * Progression(ModelFor(tier, plusRider), grade) / 100.0;
        }

        /// <summary>Invalidity grade from several part values: added up, at most 100 (B.2).</summary>
        public static double CombineGrades(params double[] values)
        {
            double s = 0; if (values != null) foreach (var v in values) s += Math.Max(0.0, v);
            return Math.Min(100.0, s);
        }
        public static double CombineGrades(System.Collections.Generic.IEnumerable<double> values)
        {
            double s = 0; if (values != null) foreach (var v in values) s += Math.Max(0.0, v);
            return Math.Min(100.0, s);
        }
        /// <summary>Partial loss (scar): value x (1 - efficiency of the part) (B.3).</summary>
        public static double PartialLoss(double partValue, double efficiency) => partValue * Clamp01(1.0 - efficiency);
        /// <summary>Pre-existing damage at signing is deducted from the grade afterwards (B.3).</summary>
        public static double NetGrade(double gradeNow, double gradeAtSigning) => Math.Max(0.0, Math.Min(100.0, gradeNow) - Math.Max(0.0, gradeAtSigning));
        /// <summary>From Overbooked on, a pre-existing ailment on the same part cuts the benefit by 25 % (B.3).</summary>
        public static double ContributionCut(double benefit, bool contributed) => contributed ? benefit * 0.75 : benefit;
        /// <summary>Death sum: 20 % of the basic sum by default (B.4); paid out at the higher of both if death comes before assessment (§23).</summary>
        public static double DeathBenefit(double sum, double share = 0.20) => sum * share;
        /// <summary>Clause 23: dying before the assessment ends the assessment; on Standard Room the higher of both is paid.</summary>
        public static double DeathBeforeAssessment(double deathBenefit, double pendingAccidentBenefit, bool generous) => generous ? Math.Max(deathBenefit, pendingAccidentBenefit) : deathBenefit;

        // ---- negotiation (K8) ---------------------------------------------------------------------------------------
        /// <summary>
        /// p_round = sigmoid((negotiation - 0.8 - 1.2 x slacker + 0.3 [Premium] + 0.2 (ledgerScore - 600) / 250) / 0.25).
        /// negotiation = NegotiationAbility of the best talker (about 1.0 for an average colonist).
        /// slacker = the Claims-attitude slider as fraction times the tier factor.
        /// </summary>
        public static double NegotiationChance(double negotiation, double slacker, bool premium, double ledgerScore = 600.0, double extra = 0.0)
        {
            double x = (negotiation - 0.8 - 1.2 * slacker + (premium ? 0.3 : 0.0) + 0.2 * (ledgerScore - 600.0) / 250.0 + extra) / 0.25;
            return 1.0 / (1.0 + Math.Exp(-x));
        }
        /// <summary>Share paid after a partial success of a negotiation: 40-70 % (u = uniform 0..1).</summary>
        public static double PartialShare(double u) => 0.40 + 0.30 * Clamp01(u);

        // ---- cancellation (B.6) ---------------------------------------------------------------------------------------
        /// <summary>
        /// Effective tick of a cancellation filed at <paramref name="requestTick"/>: the first quadrum change at least
        /// <paramref name="noticeTicks"/> after the request. <paramref name="ticksIntoQuadrum"/> = ticks since the quadrum began.
        /// A special cancellation (after a settled claim or a premium increase) is effective at once.
        /// </summary>
        public static long CancelEffectiveTick(long requestTick, long ticksIntoQuadrum, long quadrumLength, long noticeTicks, bool special)
        {
            if (special) return requestTick;
            long boundary = requestTick + (quadrumLength - ticksIntoQuadrum);
            if (ticksIntoQuadrum <= 0) boundary = requestTick; // exactly on a boundary: today is the change
            long earliest = requestTick + Math.Max(0, noticeTicks);
            while (boundary < earliest) boundary += quadrumLength;
            return boundary;
        }

        /// <summary>A form error on a cancellation (clause 30). On Standard Room the cancellation is re-filed automatically and the deadline stays; above it the deadline slips by one quadrum.</summary>
        public static long CancelEffectiveAfterFormError(long effectiveTick, long quadrumLength, bool resubmitKeepsDeadline)
            => resubmitKeepsDeadline ? effectiveTick : effectiveTick + quadrumLength;

        // ---- sales tricks (B.6, 8.13) ------------------------------------------------------------------------------------
        /// <summary>Courtesy credit: what the sales tricks cost above the cap (share of the yearly premiums) is paid back.</summary>
        public static double CourtesyCredit(double trickCost, double annualPremiums, double capShare = 0.05)
            => Math.Max(0.0, trickCost - Math.Max(0.0, capShare) * Math.Max(0.0, annualPremiums));

        /// <summary>How many riders are pre-ticked for a given sales pressure (0..1) and offered count.</summary>
        public static int PreTickedRiders(double salesPressure, int offered)
        {
            if (offered <= 0 || salesPressure < 0.04) return 0;
            if (salesPressure < 0.5) return Math.Min(Math.Min(2, offered), Math.Max(1, (int)Math.Round(salesPressure * 6.0)));
            return offered;
        }

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
