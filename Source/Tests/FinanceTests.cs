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
    [Fact] public void AccountIsASavingsFormulaNotTheMarket()
    {
        // default M 1.2, share 0.2: +4 % a year (F21)
        Assert.InRange(FinanceMath.AccountYearlyMultiple(1.2, 0.2, 1.0), 1.04 - 1e-12, 1.04 + 1e-12);
        Assert.InRange(FinanceMath.AccountYearlyPercent(1.2, 0.2, 1.1), 4.4 - 1e-9, 4.4 + 1e-9);   // Too Big To Fail
        Assert.InRange(FinanceMath.AccountYearlyPercent(1.2, 0.2, 0.9), 3.6 - 1e-9, 3.6 + 1e-9);   // Imperial Treasury
        Assert.Equal(1.0, FinanceMath.AccountYearlyMultiple(1.0, 0.2, 1.0));                       // M = 1: nothing
        Assert.True(FinanceMath.AccountYearlyMultiple(0.7, 0.2, 1.0) < 1.0);                         // M < 1 loses a little
        Assert.True(FinanceMath.AccountYearlyMultiple(1.2, 0.4, 1.0) > FinanceMath.AccountYearlyMultiple(1.2, 0.2, 1.0));
        Assert.True(FinanceMath.AccountYearlyMultiple(1.2, 0.2, 0.8) < FinanceMath.AccountYearlyMultiple(1.2, 0.2, 1.0));
    }
    [Fact] public void AccountHasAFloorAndACap()
    {
        Assert.Equal(FinanceMath.AccountFloor, FinanceMath.AccountYearlyMultiple(0.1, 0.2, 1.0), 12);   // the hardest tier costs 10 %, not 90 %
        Assert.Equal(FinanceMath.AccountFloor, FinanceMath.AccountYearlyMultiple(0, 1, 1), 12);
        Assert.Equal(FinanceMath.AccountCap, FinanceMath.AccountYearlyMultiple(100, 0.2, 1.0), 12);       // Comped: triples at most
        Assert.Equal(1.0, FinanceMath.AccountYearlyMultiple(double.NaN, 0.2, 1.0));
        Assert.Equal(1.2, FinanceMath.AccountYearlyMultiple(2.0, 0.2, 1.0), 12);                           // +20 % at M = 2
    }
    [Theory]
    [InlineData(0.7)] [InlineData(1.0)] [InlineData(1.2)] [InlineData(1.5)] [InlineData(2.0)]
    public void AccountPaysLessThanSavingsLessThanBondLessThanIndex(double m)
    {
        double account = FinanceMath.AccountYearlyMultiple(m, FinanceMath.DefaultAccountShare, 1.0);
        double mattress = Hearth.Markets.MarketMath.SavingsMultiple(m);
        double index = Hearth.Markets.MarketMath.LnMAsset(1.0, FinDetMath.Ln(m), 0);   // beta 1 reference index
        if (m >= 1.0)
        {
            Assert.True(account <= mattress + 1e-12, $"account {account} <= savings product {mattress} at M {m}");
            Assert.True(mattress <= Math.Exp(index) + 1e-9, "savings product <= index");
        }
        else Assert.True(account >= mattress, "in a bad market the account loses less than the savings product (the floor protects it)");
    }
    [Fact] public void AccountOrderingAtTheDefault()
    {
        double m = 1.2;
        double account = FinanceMath.AccountYearlyMultiple(m, FinanceMath.DefaultAccountShare, 1.1);   // best bank
        double mattress = Hearth.Markets.MarketMath.SavingsMultiple(m);                                    // 1.08
        Assert.True(account < mattress && mattress < 1.116 && 1.116 < Math.Pow(m, 1.0), "account < Mattress < bond < index");
    }
    [Fact] public void AccountNeverBeatsTheCeiling()
    {
        double mattress = Hearth.Markets.MarketMath.SavingsMultiple(1.2);                                   // 1.08
        Assert.Equal(mattress, FinanceMath.AccountYearlyMultiple(2.0, 0.2, 1.0, mattress), 12);           // bank M 2.0 would pay 1.2: capped
        Assert.Equal(1.04, FinanceMath.AccountYearlyMultiple(1.2, 0.2, 1.0, mattress), 12);               // below the ceiling nothing changes
        Assert.Equal(mattress, FinanceMath.AccountYearlyMultiple(5, 1.0, 1.1, mattress), 12);
        Assert.Equal(FinanceMath.AccountFloor, FinanceMath.AccountYearlyMultiple(0.1, 0.2, 1.0, 0.85), 12);  // the floor wins over a low ceiling
        Assert.Equal(FinanceMath.AccountYearlyMultiple(1.5, 0.2, 1.0), FinanceMath.AccountYearlyMultiple(1.5, 0.2, 1.0, double.PositiveInfinity), 12);
    }

    [Fact] public void AccountDailyFactorMatchesTheYear()
    {
        double f = FinanceMath.AccountDailyFactor(1.2, 0.2, 1.0);
        Assert.InRange(Math.Abs(Math.Pow(f, FinanceMath.DaysPerYear) - 1.04), 0, 1e-9);
    }
    [Fact] public void NetWorthCountsInvestmentsAtMarketValue()
    {
        Assert.Equal(1000 + 50 + 200 + 700 - 300, FinanceMath.NetWorth(1000, 50, 200, 700, 300));
        Assert.Equal(1000, FinanceMath.NetWorth(1000, 0, 0, 0, 0));
        Assert.Equal(-300, FinanceMath.NetWorth(0, 0, 0, 0, 300));
    }
    [Fact] public void LoanBasisCountsOnlyAShareOfTheInvestments()
    {
        double nw = FinanceMath.NetWorth(1000, 0, 0, 800, 0);                // 1800
        Assert.Equal(1400, FinanceMath.LoanNetWorth(nw, 800, 0.5), 9);
        Assert.Equal(1000, FinanceMath.LoanNetWorth(nw, 800, 0.0), 9);
        Assert.Equal(1800, FinanceMath.LoanNetWorth(nw, 800, 1.0), 9);
        Assert.Equal(1800, FinanceMath.LoanNetWorth(nw, 800, 7.0), 9);       // shares above 1 are clamped
        Assert.Equal(500, FinanceMath.LoanNetWorth(500, -50, 0.5), 9);       // negative investments never add
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

public class TransferMathTests
{
    [Theory]
    [InlineData(0.0, 3340, 0)] [InlineData(1.0, 3340, 3340)] [InlineData(0.37, 3340, 1236)] [InlineData(0.5, 3341, 1671)] [InlineData(1.7, 100, 100)]
    [InlineData(-0.2, 100, 0)] [InlineData(0.5, 0, 0)] [InlineData(double.NaN, 100, 0)]
    public void AmountFromPercent(double f, long avail, long expect) => Assert.Equal(expect, TransferMath.AmountFromPercent(f, avail));

    [Theory]
    [InlineData(0, 3340, 0.0)] [InlineData(1670, 3340, 0.5)] [InlineData(3340, 3340, 1.0)] [InlineData(99999, 3340, 1.0)] [InlineData(50, 0, 0.0)] [InlineData(-5, 100, 0.0)]
    public void PercentFromAmount(long amount, long avail, double expect) => Assert.InRange(TransferMath.PercentFromAmount(amount, avail), expect - 1e-9, expect + 1e-9);

    [Fact] public void RoundTripNeverDrifts()
    {
        foreach (long avail in new long[] { 1, 7, 100, 3340, 123_456, 50_000_000 })
            for (int pct = 0; pct <= 100; pct++)
            {
                long a = TransferMath.AmountFromPercent(pct / 100.0, avail);
                double back = TransferMath.PercentFromAmount(a, avail);
                Assert.True(a >= 0 && a <= avail);
                Assert.Equal(a, TransferMath.AmountFromPercent(back, avail));   // typing the amount back gives the same amount
            }
    }
    [Fact] public void MoreThanTheColonyHoldsPegsTheSliderAtTheEnd() => Assert.Equal(1.0, TransferMath.PercentFromAmount(1_000_000, 500));
    [Fact] public void TypedAmountsAboveIntAreClamped()
    {
        Assert.Equal(int.MaxValue, TransferMath.ClampToCoins(1e12));
        Assert.Equal(0, TransferMath.ClampToCoins(-4));
        Assert.Equal(0, TransferMath.ClampToCoins(double.NaN));
        Assert.Equal(1234, TransferMath.ClampToCoins(1234.9));
    }
    [Fact] public void SnapAndWholePercent()
    {
        Assert.Equal(0.37, TransferMath.SnapPercent(0.3712), 9);
        Assert.Equal(1.0, TransferMath.SnapPercent(7));
        Assert.Equal(0.0, TransferMath.SnapPercent(double.NaN));
        Assert.Equal(37, TransferMath.WholePercent(0.372));
        Assert.Equal(100, TransferMath.WholePercent(3));
    }
}

public class LotteryOddsTests
{
    [Fact] public void AnyPrizeIsAboutOneInFiftyTwo()
    {
        double odds = Hearth.Markets.LotteryMath.AnyPrizeOdds();
        Assert.InRange(odds, 30, 80);
        Assert.InRange(Hearth.Markets.LotteryMath.AnyPrizeProbability(), 0.01, 0.04);
    }
    [Fact] public void AnyPrizeIsMoreLikelyThanTheJackpot() => Assert.True(Hearth.Markets.LotteryMath.AnyPrizeOdds() < Hearth.Markets.LotteryMath.JackpotOdds);
}
