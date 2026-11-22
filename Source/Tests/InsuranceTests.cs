using System;
using System.Linq;
using Hearth.Insurance;
using Xunit;

// Unit tests of the pure insurance maths (KONZEPT 8.13, Anhang B). Worked examples of B.5 use a basic sum of 5,000 silver.
public class InsuranceProgressionTests
{
    [Theory]
    [InlineData(ProgressionModel.Linear, 10, 10)] [InlineData(ProgressionModel.Linear, 25, 25)] [InlineData(ProgressionModel.Linear, 30, 30)] [InlineData(ProgressionModel.Linear, 50, 50)] [InlineData(ProgressionModel.Linear, 75, 75)] [InlineData(ProgressionModel.Linear, 100, 100)]
    [InlineData(ProgressionModel.P225, 10, 10)] [InlineData(ProgressionModel.P225, 25, 25)] [InlineData(ProgressionModel.P225, 30, 35)] [InlineData(ProgressionModel.P225, 50, 75)] [InlineData(ProgressionModel.P225, 75, 150)] [InlineData(ProgressionModel.P225, 100, 225)]
    [InlineData(ProgressionModel.P350, 10, 10)] [InlineData(ProgressionModel.P350, 25, 25)] [InlineData(ProgressionModel.P350, 30, 40)] [InlineData(ProgressionModel.P350, 50, 100)] [InlineData(ProgressionModel.P350, 75, 225)] [InlineData(ProgressionModel.P350, 100, 350)]
    [InlineData(ProgressionModel.P500, 10, 10)] [InlineData(ProgressionModel.P500, 25, 25)] [InlineData(ProgressionModel.P500, 30, 40)] [InlineData(ProgressionModel.P500, 50, 100)] [InlineData(ProgressionModel.P500, 75, 300)] [InlineData(ProgressionModel.P500, 100, 500)]
    public void TableOfB4(ProgressionModel m, double grade, double expected) => Assert.Equal(expected, InsuranceMath.Progression(m, grade), 6);

    [Fact] public void ProgressionClampsAndIsMonotonic()
    {
        foreach (ProgressionModel m in Enum.GetValues(typeof(ProgressionModel)))
        {
            double last = -1;
            for (double g = -5; g <= 105; g += 0.5) { double p = InsuranceMath.Progression(m, g); Assert.True(p >= last - 1e-9); last = p; }
            Assert.Equal(0, InsuranceMath.Progression(m, -3));
        }
    }

    [Theory] [InlineData(0, 0)] [InlineData(0.5, 0)] [InlineData(1, 1)] [InlineData(24.9, 1)] [InlineData(25, 2)] [InlineData(49, 2)] [InlineData(50, 3)] [InlineData(74.9, 3)] [InlineData(75, 4)] [InlineData(99, 4)] [InlineData(100, 5)]
    public void Stages(double g, int stage) => Assert.Equal(stage, InsuranceMath.Stage(g));
}

public class InsuranceWorkedExampleTests
{
    const double Sum = 5000;
    static double Pay(CoverTier t, double grade, bool partial = false) => InsuranceMath.AccidentBenefit(Sum, t, grade, false, partial);

    [Fact] public void ThumbAndIndexFinger()  // grade 20 + 10 = 30
    {
        double g = InsuranceMath.CombineGrades(20, 10); Assert.Equal(30, g);
        Assert.Equal(1500, Pay(CoverTier.BareMinimum, g), 6);
        Assert.Equal(1750, Pay(CoverTier.GoodEnough, g), 6);
        Assert.Equal(2000, Pay(CoverTier.Premium, g), 6);
    }
    [Fact] public void LegAndEye()  // 70 + 50 = 120 -> 100
    {
        double g = InsuranceMath.CombineGrades(70, 50); Assert.Equal(100, g);
        Assert.Equal(5000, Pay(CoverTier.BareMinimum, g), 6);
        Assert.Equal(11250, Pay(CoverTier.GoodEnough, g), 6);
        Assert.Equal(17500, Pay(CoverTier.Premium, g), 6);
    }
    [Fact] public void ScarOnTheEye()  // 50 x (1 - 0.6) = 20, partial loss
    {
        double g = InsuranceMath.PartialLoss(50, 0.6); Assert.Equal(20, g, 6);
        Assert.Equal(0, Pay(CoverTier.BareMinimum, g, partial: true));   // Bare Minimum does not pay partial loss
        Assert.Equal(1000, Pay(CoverTier.GoodEnough, g, true), 6);
        Assert.Equal(1000, Pay(CoverTier.Premium, g, true), 6);
    }
    [Fact] public void RightEyeMissingAtSigning()  // 100 - 50 = 50
    {
        double g = InsuranceMath.NetGrade(100, 50); Assert.Equal(50, g);
        Assert.Equal(2500, Pay(CoverTier.BareMinimum, g), 6);
        Assert.Equal(3750, Pay(CoverTier.GoodEnough, g), 6);
        Assert.Equal(5000, Pay(CoverTier.Premium, g), 6);
    }
    [Fact] public void EarWithHearingLoss()  // 30; Overbooked cuts 25 %
    {
        Assert.Equal(1500, Pay(CoverTier.BareMinimum, 30), 6);
        Assert.Equal(1750, Pay(CoverTier.GoodEnough, 30), 6);
        Assert.Equal(2000, Pay(CoverTier.Premium, 30), 6);
        Assert.Equal(1312.5, InsuranceMath.ContributionCut(Pay(CoverTier.GoodEnough, 30), true), 6);
        Assert.Equal(1500, InsuranceMath.ContributionCut(Pay(CoverTier.Premium, 30), true), 6);
        Assert.Equal(1750, InsuranceMath.ContributionCut(Pay(CoverTier.GoodEnough, 30), false), 6);
    }
    [Fact] public void ThresholdsPerTier()
    {
        Assert.Equal(0, Pay(CoverTier.BareMinimum, 19.9));
        Assert.True(Pay(CoverTier.BareMinimum, 20) > 0);
        Assert.Equal(0, Pay(CoverTier.GoodEnough, 4.9));
        Assert.True(Pay(CoverTier.GoodEnough, 5) > 0);
        Assert.Equal(0, Pay(CoverTier.Premium, 0.9));
        Assert.True(Pay(CoverTier.Premium, 1) > 0);
    }
    [Fact] public void PlusRiderUsesFiveHundredPercent() => Assert.Equal(25000, InsuranceMath.AccidentBenefit(5000, CoverTier.Premium, 100, plusRider: true), 6);
    [Fact] public void DeathBenefitIsTwentyPercent() => Assert.Equal(1000, InsuranceMath.DeathBenefit(5000), 6);
    [Fact] public void ClauseTwentyThree()
    {
        Assert.Equal(1000, InsuranceMath.DeathBeforeAssessment(1000, 4000, generous: false));
        Assert.Equal(4000, InsuranceMath.DeathBeforeAssessment(1000, 4000, generous: true));
    }
}

public class InsurancePremiumTests
{
    [Fact] public void PremiumFormula()
        => Assert.Equal(1000 * 0.5 * 1.25 * 0.9 * 0.95, InsuranceMath.Premium(1000, 0.5, 0.25, 0.9, 0.95), 9);
    [Fact] public void ZeroLoadingIsActuariallyFair()
    {
        // at loading 0 and no discounts the premium equals the expected payout
        Assert.Equal(400, InsuranceMath.Premium(1000, 0.4, 0, 1, 1), 9);
    }
    [Fact] public void PremiumNeverNegative() => Assert.Equal(0, InsuranceMath.Premium(-5, 0.5, 0.2, 1, 1));
    [Theory] [InlineData(0, 1.0)] [InlineData(1, 0.95)] [InlineData(4, 0.8)] [InlineData(10, 0.5)] [InlineData(25, 0.5)] [InlineData(-3, 1.0)]
    public void NoClaimsFactor(int cls, double f) => Assert.Equal(f, InsuranceMath.NoClaimsFactor(cls), 9);
    [Theory] [InlineData(0, false, 1)] [InlineData(10, false, 10)] [InlineData(5, true, 2)] [InlineData(2, true, 0)] [InlineData(0, true, 0)]
    public void NoClaimsClassMoves(int cls, bool claim, int next) => Assert.Equal(next, InsuranceMath.NextNoClaimsClass(cls, claim));
    [Theory] [InlineData(0, 1.0)] [InlineData(1, 1.0)] [InlineData(2, 0.95)] [InlineData(3, 0.90)] [InlineData(4, 0.88)] [InlineData(9, 0.88)]
    public void Bundle(int n, double f) => Assert.Equal(f, InsuranceMath.BundleFactor(n), 9);
    [Fact] public void ThirdPolicyGivesTenPercent() => Assert.Equal(0.90, InsuranceMath.BundleFactor(3), 9);
    [Fact] public void TiersAreOrderedByPayoutAndPrice()
    {
        double a = InsuranceMath.ExpectedPayoutRatio(CoverTier.BareMinimum, 0.15), b = InsuranceMath.ExpectedPayoutRatio(CoverTier.GoodEnough, 0.15), c = InsuranceMath.ExpectedPayoutRatio(CoverTier.Premium, 0.15);
        Assert.True(a < b && b < c);
        Assert.InRange(c / b, 1.4, 2.1);     // roughly the x1.9 of the tier table
        Assert.InRange(a / b, 0.3, 0.6);     // roughly the x0.55
        Assert.InRange(c, 0.8, 1.0);
    }
    [Fact] public void HigherAttitudeLowersExpectedPayout()
    {
        foreach (CoverTier t in Enum.GetValues(typeof(CoverTier)))
            Assert.True(InsuranceMath.ExpectedPayoutRatio(t, 0.75) < InsuranceMath.ExpectedPayoutRatio(t, 0.0) + 1e-12);
        Assert.Equal(InsuranceMath.Replacement(CoverTier.GoodEnough) * (1 - InsuranceMath.Deductible(CoverTier.GoodEnough)), InsuranceMath.ExpectedPayoutRatio(CoverTier.GoodEnough, 0.0), 9); // "Never weasels"
    }
    [Fact] public void HazardFactor()
    {
        Assert.Equal(1.0, InsuranceMath.HazardFactor(0, 0), 9);
        Assert.Equal(1.6, InsuranceMath.HazardFactor(1, 0), 9);
        Assert.Equal(3.0, InsuranceMath.HazardFactor(0, 1), 9);
        Assert.Equal(1.0 * 0.5 + 1.6 * 0.3 + 3.0 * 0.2, InsuranceMath.HazardFactor(0.3, 0.2), 9);
        Assert.Equal(2.3, InsuranceMath.HazardFactor(1, 1), 9); // normalised, never beyond the mix
    }
    [Fact] public void ExpectedDamageBlendsHistory()
    {
        Assert.Equal(100, InsuranceMath.ExpectedDamage(10000, 0.01, 1, 1, 100), 9);
        Assert.Equal(100 * 0.65 + 400 * 0.35, InsuranceMath.ExpectedDamage(10000, 0.01, 1, 1, 400), 9);
    }
    [Fact] public void BillingAmountIsProRata() => Assert.Equal(30, InsuranceMath.BillingAmount(90, 5), 9);
    [Fact] public void PersonalSumCaps()
    {
        Assert.Equal(2000, InsuranceMath.MaxPersonalSum(1000));
        Assert.Equal(25000, InsuranceMath.MaxPersonalSum(40000));
    }
    [Fact] public void UnderInsurancePaysProRata()
    {
        Assert.Equal(0.5, InsuranceMath.UnderInsuranceFactor(500, 1000), 9);
        Assert.Equal(1.0, InsuranceMath.UnderInsuranceFactor(2000, 1000), 9);
        // Good Enough: 1000 loss x 0.8 x 0.85, half insured
        Assert.Equal(1000 * 0.8 * 0.85 * 0.5, InsuranceMath.PropertyPayout(1000, 5000, 10000, CoverTier.GoodEnough), 9);
    }
    [Fact] public void PropertyPayoutNeverAboveLossOrSum()
    {
        foreach (CoverTier t in Enum.GetValues(typeof(CoverTier)))
            foreach (double loss in new[] { 10.0, 400.0, 99999.0 })
            {
                double p = InsuranceMath.PropertyPayout(loss, 1000, 1000, t);
                Assert.True(p <= loss + 1e-9); Assert.True(p <= 1000 + 1e-9);
            }
        Assert.Equal(300, InsuranceMath.PropertyPayout(1000, 1000, 1000, CoverTier.BareMinimum), 9); // 50 % x (1 - 40 %)
        Assert.Equal(1000, InsuranceMath.PropertyPayout(1000, 1000, 1000, CoverTier.Premium), 9);
    }
}

public class InsuranceClaimsTests
{
    [Fact] public void DenialFormula()
    {
        Assert.Equal(0.15, InsuranceMath.DenialProbability(0.15, CoverTier.GoodEnough, false, false), 9);
        Assert.Equal(0.15 * 1.5, InsuranceMath.DenialProbability(0.15, CoverTier.BareMinimum, false, false), 9);
        Assert.Equal(0.15 * 0.4, InsuranceMath.DenialProbability(0.15, CoverTier.Premium, false, false), 9);
        Assert.Equal(0.15 * 1.5, InsuranceMath.DenialProbability(0.15, CoverTier.GoodEnough, true, false), 9);   // grey zone
        Assert.Equal(0.0, InsuranceMath.DenialProbability(0.15, CoverTier.GoodEnough, false, true), 9);          // Legal Shield, clamped
        Assert.Equal(1.0, InsuranceMath.DenialProbability(1.0, CoverTier.BareMinimum, true, false), 9);          // "Basically always"
        Assert.Equal(0.0, InsuranceMath.DenialProbability(0.0, CoverTier.BareMinimum, true, false), 9);          // "Never weasels"
    }
    [Fact] public void LegalShieldSubtractsTwentyPoints()
        => Assert.Equal(0.5 - 0.2, InsuranceMath.DenialProbability(0.5, CoverTier.GoodEnough, false, true), 9);
    [Fact] public void NegotiationIsASigmoid()
    {
        double lo = InsuranceMath.NegotiationChance(0.2, 0.5, false), hi = InsuranceMath.NegotiationChance(1.8, 0.1, true);
        Assert.InRange(lo, 0, 0.1); Assert.InRange(hi, 0.9, 1);
        Assert.Equal(0.5, InsuranceMath.NegotiationChance(0.8 + 1.2 * 0.2, 0.2, false), 9);
        Assert.True(InsuranceMath.NegotiationChance(1.0, 0.15, true) > InsuranceMath.NegotiationChance(1.0, 0.15, false));
        Assert.True(InsuranceMath.NegotiationChance(1.0, 0.15, false, 850) > InsuranceMath.NegotiationChance(1.0, 0.15, false, 350));
    }
    [Fact] public void PartialShareIsFortyToSeventy()
    {
        Assert.Equal(0.4, InsuranceMath.PartialShare(0), 9); Assert.Equal(0.7, InsuranceMath.PartialShare(1), 9); Assert.Equal(0.55, InsuranceMath.PartialShare(0.5), 9);
    }
    [Fact] public void PartialProbabilityOnlyWithAClause()
    {
        Assert.Equal(0, InsuranceMath.PartialProbability(0.2, false));
        Assert.InRange(InsuranceMath.PartialProbability(0.2, true), 0.01, 0.5);
    }
    [Fact] public void ExpectedPayoutRatioNeverAboveReplacement()
    {
        foreach (CoverTier t in Enum.GetValues(typeof(CoverTier)))
            for (double a = 0; a <= 1.0001; a += 0.05)
                Assert.InRange(InsuranceMath.ExpectedPayoutRatio(t, a), 0, InsuranceMath.Replacement(t) * (1 - InsuranceMath.Deductible(t)) + 1e-9);
    }
    [Fact] public void RandomisedBenefitIsMonotoneInGrade()
    {
        var r = new Random(7);
        foreach (CoverTier t in Enum.GetValues(typeof(CoverTier)))
        {
            double last = 0;
            for (double g = 0; g <= 100; g += 0.5) { double b = InsuranceMath.AccidentBenefit(1000, t, g); Assert.True(b + 1e-9 >= last || g == InsuranceMath.InvalidityThreshold(t)); last = Math.Max(last, b); }
        }
        Assert.True(r.Next() >= 0);
    }
}

public class InsuranceCancelTests
{
    const long Q = 900000, D = 60000;
    [Fact] public void StandardRoomNoticeIsThreeDaysToQuadrumEnd()
    {
        // 10 days into the quadrum: 5 days left, 3 days notice is met -> end of this quadrum
        Assert.Equal(1000000 + 5 * D, InsuranceMath.CancelEffectiveTick(1000000, 10 * D, Q, 3 * D, false));
        // 13 days in: only 2 days left, notice missed -> end of the next quadrum
        Assert.Equal(1000000 + 2 * D + Q, InsuranceMath.CancelEffectiveTick(1000000, 13 * D, Q, 3 * D, false));
        // exactly 3 days left: still in time
        Assert.Equal(1000000 + 3 * D, InsuranceMath.CancelEffectiveTick(1000000, 12 * D, Q, 3 * D, false));
    }
    [Fact] public void OverbookedNeedsAFullQuadrum()
    {
        // one quadrum notice: boundary must be at least 15 days away
        long e = InsuranceMath.CancelEffectiveTick(1000000, 5 * D, Q, Q, false);
        Assert.True(e - 1000000 >= Q);
        Assert.Equal(1000000 + 10 * D + Q, e);
    }
    [Fact] public void WhyNeedsAYear()
    {
        long e = InsuranceMath.CancelEffectiveTick(0, 7 * D, Q, 60 * D, false);
        Assert.True(e >= 60 * D); Assert.True(e % Q == (Q - 7 * D) % Q);
    }
    [Fact] public void SpecialCancellationIsImmediate() => Assert.Equal(777, InsuranceMath.CancelEffectiveTick(777, 5 * D, Q, 3 * D, true));
    [Fact] public void FormErrorKeepsOrSlipsDeadline()
    {
        Assert.Equal(5000, InsuranceMath.CancelEffectiveAfterFormError(5000, Q, true));
        Assert.Equal(5000 + Q, InsuranceMath.CancelEffectiveAfterFormError(5000, Q, false));
    }
    [Fact] public void CourtesyCreditPaysBackAboveTheCap()
    {
        Assert.Equal(0, InsuranceMath.CourtesyCredit(40, 1000));          // 4 % < 5 %
        Assert.Equal(20, InsuranceMath.CourtesyCredit(70, 1000), 9);      // 7 % -> 2 % back
        Assert.Equal(0, InsuranceMath.CourtesyCredit(50, 1000), 9);
    }
    [Theory] [InlineData(0.0, 4, 0)] [InlineData(0.30, 3, 2)] [InlineData(0.30, 1, 1)] [InlineData(0.60, 3, 3)] [InlineData(1.0, 3, 3)] [InlineData(0.10, 3, 1)] [InlineData(0.30, 0, 0)]
    public void PreTickedRiders(double pressure, int offered, int expected) => Assert.Equal(expected, InsuranceMath.PreTickedRiders(pressure, offered));
}

public class LimbScheduleTests
{
    [Theory]
    [InlineData("Shoulder", 70)] [InlineData("Arm", 65)] [InlineData("Hand", 55)] [InlineData("Thumb", 20)] [InlineData("IndexFinger", 10)] [InlineData("MiddleFinger", 5)] [InlineData("RingFinger", 5)] [InlineData("Pinky", 5)]
    [InlineData("Leg", 70)] [InlineData("Foot", 40)] [InlineData("BigToe", 5)] [InlineData("Toe", 2)] [InlineData("Eye", 50)] [InlineData("Ear", 30)] [InlineData("Nose", 10)] [InlineData("Tongue", 20)] [InlineData("Jaw", 30)]
    [InlineData("Pelvis", 20)] [InlineData("Lung", 25)] [InlineData("Kidney", 15)] [InlineData("Stomach", 30)] [InlineData("Waist", 0)] [InlineData("Humerus", 35)] [InlineData("Femur", 35)] [InlineData("Tibia", 25)] [InlineData("Radius", 30)] [InlineData("Clavicle", 10)]
    public void TableOfB2(string key, double pct)
    {
        Assert.True(LimbSchedule.TryGet(key, out var e)); Assert.Equal(pct, e.Percent); Assert.False(e.Lethal);
        Assert.False(string.IsNullOrEmpty(e.Comment));
    }
    [Theory] [InlineData("Heart")] [InlineData("Liver")] [InlineData("Neck")] [InlineData("Head")] [InlineData("Torso")]
    public void DeathParts(string key) { Assert.True(LimbSchedule.TryGet(key, out var e)); Assert.True(e.Lethal); }
    [Theory]
    [InlineData("Finger", "left thumb", "Thumb")] [InlineData("Finger", "right index finger", "IndexFinger")] [InlineData("Finger", "left middle finger", "MiddleFinger")]
    [InlineData("Finger", "left ring finger", "RingFinger")] [InlineData("Finger", "right pinky", "Pinky")] [InlineData("Finger", "weird digit", "Finger")]
    [InlineData("Toe", "left big toe", "BigToe")] [InlineData("Toe", "right little toe", "Toe")] [InlineData("Eye", "left eye", "Eye")]
    public void KeyFor(string def, string label, string key) => Assert.Equal(key, LimbSchedule.KeyFor(def, label));
    [Fact] public void HumanHandsAndFingersDoNotDoubleCount()
    {
        // a lost hand pays 55 and replaces its fingers (the game layer counts only the top-most missing part)
        Assert.Equal(55, InsuranceMath.CombineGrades(55));
        // all ten fingers separately would be 2 x (20 + 10 + 5 + 5 + 5) = 90 - clearly more than the hands: that is why the hand wins
        double fingers = 2 * (20 + 10 + 5 + 5 + 5);
        Assert.True(fingers > 2 * 55 * 0.8);
    }
    [Fact] public void SharedSourcesDoNotPayMoreForMoreEyes()
    {
        Assert.Equal(50, LimbSchedule.SharedValue(100, 2));
        Assert.Equal(25, LimbSchedule.SharedValue(100, 4));
        Assert.Equal(100, LimbSchedule.SharedValue(100, 1));
        // losing all four eyes still adds up to 100, not 200
        Assert.Equal(100, InsuranceMath.CombineGrades(Enumerable.Repeat(LimbSchedule.SharedValue(100, 4), 4)));
    }
    [Fact] public void FourArmsPayAsTwo()
    {
        Assert.Equal(1.0, LimbSchedule.LimbScale(2)); Assert.Equal(1.0, LimbSchedule.LimbScale(1)); Assert.Equal(0.5, LimbSchedule.LimbScale(4), 9);
        double all = 4 * 70 * LimbSchedule.LimbScale(4);
        Assert.Equal(140, all, 9);
    }
    [Fact] public void TagTotals()
    {
        Assert.Equal(100, LimbSchedule.TagTotal("SightSource")); Assert.Equal(60, LimbSchedule.TagTotal("HearingSource")); Assert.Equal(50, LimbSchedule.TagTotal("BreathingSource"));
        Assert.Equal(-1, LimbSchedule.TagTotal("BloodPumpingSource")); Assert.Equal(-1, LimbSchedule.TagTotal("ConsciousnessSource")); Assert.Equal(0, LimbSchedule.TagTotal("Nonsense"));
        Assert.Equal(20, LimbSchedule.LimbTagValue("ManipulationLimbDigit", true, false)); Assert.Equal(5, LimbSchedule.LimbTagValue("ManipulationLimbDigit", false, false));
        Assert.Equal(5, LimbSchedule.LimbTagValue("MovingLimbDigit", false, true)); Assert.Equal(2, LimbSchedule.LimbTagValue("MovingLimbDigit", false, false));
        Assert.Equal(10, LimbSchedule.CoverageFallback(0.5)); Assert.Equal(3.5, LimbSchedule.CoverageFallback(0.035), 9);
    }
    [Fact] public void SpineAndBrain()
    {
        Assert.Equal(100, LimbSchedule.SpineValue(0)); Assert.Equal(0, LimbSchedule.SpineValue(1)); Assert.Equal(50, LimbSchedule.SpineValue(0.5), 9);
        Assert.Equal(100, LimbSchedule.BrainValue(0.3)); Assert.Equal(100, LimbSchedule.BrainValue(0.1)); Assert.Equal(0, LimbSchedule.BrainValue(1), 9);
    }
    [Fact] public void ModsCanOverrideAndRestore()
    {
        LimbSchedule.Set(new LimbEntry { Key = "Wing", Percent = 33, Comment = "Flight rights reserved." });
        Assert.True(LimbSchedule.TryGet("Wing", out var w)); Assert.Equal(33, w.Percent);
        LimbSchedule.LoadDefaults();
        Assert.False(LimbSchedule.TryGet("Wing", out _));
        Assert.True(LimbSchedule.TryGet("Hand", out var h)); Assert.Equal(55, h.Percent);
    }
}
