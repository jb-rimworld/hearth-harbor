// SPDX-License-Identifier: MIT
using System;
using Hearth;
using Xunit;

public class DetMathTests
{
    [Theory]
    [InlineData(0.01)] [InlineData(0.5)] [InlineData(1.0)] [InlineData(1.2)] [InlineData(2.0)] [InlineData(100.0)] [InlineData(12345.678)]
    public void LnMatchesMath(double x) => Assert.InRange(Math.Abs(FinDetMath.Ln(x) - Math.Log(x)), 0, 1e-12);

    [Theory]
    [InlineData(-20.0)] [InlineData(-1.0)] [InlineData(0.0)] [InlineData(0.003)] [InlineData(1.0)] [InlineData(4.6)] [InlineData(30.0)]
    public void ExpMatchesMath(double y) => Assert.InRange(Math.Abs(FinDetMath.Exp(y) / Math.Exp(y) - 1.0), 0, 1e-12);

    [Theory]
    [InlineData(1.2, 1.0 / 60)] [InlineData(100.0, 1.0 / 60)] [InlineData(0.1, 1.0 / 60)] [InlineData(2.0, 10.0)] [InlineData(0.7, 3.5)]
    public void PowMatchesMath(double x, double y) => Assert.InRange(Math.Abs(FinDetMath.Pow(x, y) / Math.Pow(x, y) - 1.0), 0, 1e-12);

    [Fact] public void PowEdgeCases()
    {
        Assert.Equal(0.0, FinDetMath.Pow(0, 0.5));
        Assert.Equal(1.0, FinDetMath.Pow(5, 0));
        Assert.Equal(double.NegativeInfinity, FinDetMath.Ln(0));
    }
}

public class YieldTests
{
    [Fact] public void DailyFactorCompoundsToTheYearlyMultiple()
    {
        double f = FinanceMath.DailyFactor(1.2);
        Assert.InRange(Math.Pow(f, 60), 1.2 - 1e-12, 1.2 + 1e-12);
        Assert.InRange(f, 1.0030, 1.0031);   // ≈ +0.30 % per day on Standard Room (8.8)
    }
    [Theory]
    [InlineData(100.0, 0.0790, 0.0800)] [InlineData(2.0, 0.0116, 0.0117)] [InlineData(1.5, 0.0067, 0.0068)] [InlineData(1.0, -1e-12, 1e-12)]
    [InlineData(0.7, -0.0060, -0.0059)] [InlineData(0.1, -0.0377, -0.0376)]
    public void TierDailyRatesMatchTheTable(double m, double lo, double hi)
    {
        double r = FinanceMath.DailyFactor(m) - 1.0;
        Assert.InRange(r, lo, hi);
    }
    [Fact] public void ZeroMultipleMakesMoneyVanish() => Assert.Equal(0.0, FinanceMath.DailyFactor(0));
    [Fact] public void GrowthPreviewMatchesTheSlider()
    {
        Assert.InRange(FinanceMath.GrowthAfterYears(1000, 1.2, 1), 1199.99, 1200.01);
        Assert.InRange(FinanceMath.GrowthAfterYears(1000, 1.2, 10), 6191.7, 6192.0);   // "→ 6.192 nach zehn Jahren"
    }
    [Fact] public void CompoundMatchesClosedFormWithinFourThousandthsOfAPercent()
    {
        double f = FinanceMath.DailyFactor(1.2);
        var end = FinanceMath.Compound(Money.FromSilver(500), f, 120);
        double expect = 500 * Math.Pow(1.2, 2);
        Assert.InRange(Math.Abs(end.Silver / expect - 1), 0, 1e-4);
    }
    [Fact] public void AccountFollowsBetaAndBankModifier()
    {
        Assert.InRange(FinanceMath.AccountYearlyMultiple(1.2, 1.0, 1.0), 1.2 - 1e-12, 1.2 + 1e-12);
        Assert.True(FinanceMath.AccountYearlyMultiple(1.2, 0.4, 1.0) < 1.2);
        Assert.True(FinanceMath.AccountYearlyMultiple(1.2, 1.0, 0.8) < FinanceMath.AccountYearlyMultiple(1.2, 1.0, 1.0));
        Assert.Equal(0.0, FinanceMath.AccountYearlyMultiple(0, 1, 1));
    }
    [Fact] public void NoInterestOnNegativeOrZeroBalance()
    {
        Assert.Equal(Money.Zero, FinanceMath.InterestForDay(Money.Zero, 1.5));
        Assert.Equal(Money.Zero, FinanceMath.InterestForDay(Money.FromSilver(-100), 1.5));
    }
    [Fact] public void NegativeYieldShrinksBalance() => Assert.True(FinanceMath.InterestForDay(Money.FromSilver(1000), FinanceMath.DailyFactor(0.7)).IsNegative);
    [Fact] public void LogInterpHitsAnchorsAndStaysBetween()
    {
        var a = new double[] { 100, 2.0, 1.5, 1.2, 1.0, 0.7, 0.1 };
        for (int i = 0; i < 7; i++) Assert.InRange(Math.Abs(FinanceMath.LogInterp(a, i) / a[i] - 1), 0, 1e-12);
        double mid = FinanceMath.LogInterp(a, 3.5);
        Assert.InRange(mid, 1.0, 1.2);
        double prev = double.MaxValue;
        for (double t = 0; t <= 6; t += 0.1) { double v = FinanceMath.LogInterp(a, t); Assert.True(v <= prev + 1e-12 && v > 0 && !double.IsNaN(v)); prev = v; }
    }
}

public class LoanTests
{
    [Fact] public void BaseRateFollowsSliderWithFloor()
    {
        Assert.InRange(FinanceMath.LoanBaseRate(1.2), 0.1999, 0.2001);
        Assert.Equal(0.02, FinanceMath.LoanBaseRate(1.0));
        Assert.Equal(0.02, FinanceMath.LoanBaseRate(0.1));
        Assert.InRange(FinanceMath.LoanYearlyRate(1.2, 0.12), 0.3199, 0.3201);   // private loan 32 % on Standard Room (8.9)
    }
    [Fact] public void BorrowAndInvestNeverHasPositiveExpectation()
    {
        foreach (double m in new[] { 0.1, 0.7, 1.0, 1.2, 1.5, 2.0, 100.0 })
        {
            double invest = m - 1.0;                       // best possible asset class (the index itself)
            double cost = FinanceMath.LoanYearlyRate(m, 0.05);
            Assert.True(cost > invest, "m=" + m);
        }
    }
    [Theory]
    [InlineData(1000.0, 0.32, 30, 5)] [InlineData(250.0, 0.20, 15, 5)] [InlineData(5000.0, 0.02, 120, 5)] [InlineData(800.0, 3.0, 60, 5)]
    public void AnnuityPaysTheLoanOffExactly(double p, double rate, int term, int interval)
    {
        int n = FinanceMath.InstallmentCount(term, interval);
        double pay = FinanceMath.AnnuityPayment(p, rate, term, interval);
        double debt = p, g = FinanceMath.LoanFactor(rate, interval);
        for (int i = 0; i < n; i++) debt = debt * g - pay;
        Assert.InRange(Math.Abs(debt), 0, 1e-6 * p);
    }
    [Fact] public void ZeroRateAnnuityIsSimpleDivision() => Assert.InRange(FinanceMath.AnnuityPayment(600, 0, 30, 5), 99.9999, 100.0001);
    [Fact] public void LoanFactorOverAYearIsOnePlusRate() => Assert.InRange(FinanceMath.LoanFactor(0.32, 60), 1.3199999, 1.3200001);
    [Fact] public void CreditLimitGrowsWithScoreAndWealth()
    {
        Assert.Equal(0, FinanceMath.CreditLimit(10000, 300));
        Assert.Equal(0, FinanceMath.CreditLimit(-5, 800));
        Assert.True(FinanceMath.CreditLimit(10000, 700) > FinanceMath.CreditLimit(10000, 500));
        Assert.True(FinanceMath.CreditLimit(20000, 600) > FinanceMath.CreditLimit(10000, 600));
        Assert.InRange(FinanceMath.CreditLimit(10000, 850), 1499.9, 1500.1);
    }
    [Fact] public void ScoreIsClamped()
    {
        Assert.Equal(300, FinanceMath.ClampScore(-50)); Assert.Equal(850, FinanceMath.ClampScore(9999)); Assert.Equal(600, FinanceMath.ClampScore(600.4));
    }
    [Fact] public void WealthBonusIsZeroForSmallColoniesAndLogForBig()
    {
        Assert.Equal(0, FinanceMath.WealthScoreBonus(500));
        Assert.InRange(FinanceMath.WealthScoreBonus(10000), -1e-9, 1e-9);
        Assert.InRange(FinanceMath.WealthScoreBonus(100000), 19.999, 20.001);
    }
    [Fact] public void LeverageCapIsThirtyPercent() { Assert.Equal(300, FinanceMath.LeverageCap(1000)); Assert.Equal(0, FinanceMath.LeverageCap(-1)); }
    [Fact] public void FeeUsesMinimumAndFlat()
    {
        Assert.Equal(Money.FromSilver(5), FinanceMath.Fee(Money.FromSilver(100), 0.01, 5, 0));        // 1 % of 100 = 1 → minimum 5
        Assert.Equal(Money.FromSilver(12), FinanceMath.Fee(Money.FromSilver(1200), 0.01, 5, 0));
        Assert.Equal(Money.FromSilver(7), FinanceMath.Fee(Money.FromSilver(500), 0.01, 0, 2));        // ATM: 2 + 1 %
    }
}

public class CollectionTests
{
    [Theory]
    [InlineData(0, 4, 0)] [InlineData(1, 4, 1)] [InlineData(3, 4, 1)] [InlineData(4, 4, 2)] [InlineData(7, 4, 3)] [InlineData(10, 4, 4)]
    [InlineData(15, 4, 4)] [InlineData(40, 4, 4)] [InlineData(15, 5, 5)] [InlineData(20, 6, 6)] [InlineData(30, 7, 7)] [InlineData(99, 7, 7)]
    [InlineData(99, 0, 0)] [InlineData(5, 1, 1)] [InlineData(5, 2, 2)] [InlineData(99, 99, 7)]
    public void StageFollowsDaysAndDifficultyCap(int days, int cap, int expected) => Assert.Equal(expected, FinanceMath.CollectionStage(days, cap));

    [Fact] public void StageNeverDecreasesWithDays()
    {
        for (int cap = 0; cap <= 7; cap++) { int prev = 0; for (int d = 0; d < 60; d++) { int s = FinanceMath.CollectionStage(d, cap); Assert.True(s >= prev && s <= cap); prev = s; } }
    }
    [Fact] public void LateFeeIsFivePercent() => Assert.Equal(Money.FromSilver(12), FinanceMath.LateFee(Money.FromSilver(240)));
    [Fact] public void RaidPointsFollowDebtNotWealth()
    {
        Assert.Equal(500, FinanceMath.CollectionRaidPoints(1000, 10000, 1.0));
        Assert.Equal(150, FinanceMath.CollectionRaidPoints(1000, 100, 1.0));      // capped by storyteller points · 1.5
        Assert.Equal(0, FinanceMath.CollectionRaidPoints(1000, 100, 0));
        Assert.Equal(1200, FinanceMath.SeizureValue(1000));
    }
}

public class WealthTierTests
{
    [Theory]
    [InlineData(-6000, 0)] [InlineData(-1, 1)] [InlineData(0, 2)] [InlineData(499, 2)] [InlineData(500, 3)] [InlineData(1999, 3)] [InlineData(2000, 4)]
    [InlineData(9999, 4)] [InlineData(10000, 5)] [InlineData(60000, 6)] [InlineData(300000, 7)] [InlineData(999999, 7)] [InlineData(1000000, 8)]
    public void TierTable(double worth, int tier) => Assert.Equal(tier, FinanceMath.WealthTier(worth));

    [Fact] public void ScaleMovesTheThresholds()
    {
        Assert.Equal(4, FinanceMath.WealthTier(3000, 1.0));
        Assert.Equal(3, FinanceMath.WealthTier(3000, 2.0));
    }
    [Fact] public void MoodTableMatchesTheConcept()
    {
        Assert.Equal(-4, FinanceMath.TierMood[0]); Assert.Equal(-2, FinanceMath.TierMood[1]); Assert.Equal(0, FinanceMath.TierMood[4]); Assert.Equal(3, FinanceMath.TierMood[8]);
        Assert.Equal(FinanceMath.TierNamesEn.Length, FinanceMath.TierMood.Length);
    }
    [Fact] public void TierWaitsAWholeDayBeforeChanging()
    {
        Assert.Equal(4, FinanceMath.StableTier(4, 5, 1000, 1000 + 59999));
        Assert.Equal(5, FinanceMath.StableTier(4, 5, 1000, 1000 + 60000));
        Assert.Equal(4, FinanceMath.StableTier(4, 4, 0, 999999));
    }
    [Fact] public void BailoutIsCappedAtTenPercentOfPeak()
    {
        Assert.Equal(0, FinanceMath.BailoutAmount(50, 1_000_000));
        Assert.Equal(100000, FinanceMath.BailoutAmount(-500000, 1_000_000));
        Assert.Equal(300, FinanceMath.BailoutAmount(-300, 1_000_000));
    }
}
