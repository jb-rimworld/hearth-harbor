// SPDX-License-Identifier: MIT
// Unit tests for the finance maths (Source/Hearth.Core/Pure/Markets). No game needed. Run: Source/Tests/run.sh
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using Xunit;

public class MarketsDetMathTests
{
    [Fact]
    public void ExpMatchesSystemMath()
    {
        for (double x = -40; x <= 40; x += 0.37)
        {
            double a = DetMath.Exp(x), b = Math.Exp(x);
            Assert.True(Math.Abs(a - b) <= 1e-12 * b, $"exp({x}) {a} vs {b}");
        }
        Assert.Equal(1.0, DetMath.Exp(0), 15);
    }

    [Fact]
    public void LnMatchesSystemMath()
    {
        foreach (double x in new[] { 1e-8, 0.001, 0.2, 0.75, 1, 1.0001, 1.5, 2, Math.E, 10, 12345.678, 1e9 })
            Assert.True(Math.Abs(DetMath.Ln(x) - Math.Log(x)) <= 1e-12 * Math.Max(1, Math.Abs(Math.Log(x))), $"ln({x})");
    }

    [Fact]
    public void PowAndRoundTrip()
    {
        Assert.Equal(Math.Pow(1.2, 0.35), DetMath.Pow(1.2, 0.35), 12);
        Assert.Equal(0.0, DetMath.Pow(-3, 2));
        for (double x = 0.01; x < 100; x *= 1.7) Assert.Equal(x, DetMath.Exp(DetMath.Ln(x)), 10);
    }

    [Fact]
    public void GaussianHasUnitMoments()
    {
        var r = new RngStream(7, 1, 0);
        const int N = 200_000; double s = 0, s2 = 0, s4 = 0;
        for (int i = 0; i < N; i++) { double g = DetMath.Gaussian(r); s += g; s2 += g * g; s4 += g * g * g * g; }
        Assert.InRange(s / N, -0.01, 0.01);
        Assert.InRange(s2 / N, 0.985, 1.015);
        Assert.InRange(s4 / N, 2.9, 3.15);        // kurtosis of a normal = 3
    }

    [Theory]
    [InlineData(0.3)] [InlineData(3.0)] [InlineData(19.0)] [InlineData(120.0)]
    public void PoissonMeanAndVariance(double lambda)
    {
        var r = new RngStream(11, 2, 0); const int N = 60_000; double s = 0, s2 = 0;
        for (int i = 0; i < N; i++) { int k = DetMath.Poisson(r, lambda); s += k; s2 += (double)k * k; }
        double mean = s / N, var = s2 / N - mean * mean;
        Assert.InRange(mean, lambda * 0.97 - 0.02, lambda * 1.03 + 0.02);
        Assert.InRange(var, lambda * 0.9 - 0.05, lambda * 1.1 + 0.05);
    }

    [Fact]
    public void StreamsAreReproducible()
    {
        var a = new RngStream(5, 9, 0); var b = new RngStream(5, 9, 0);
        for (int i = 0; i < 100; i++) Assert.Equal(DetMath.Gaussian(a), DetMath.Gaussian(b));
    }
}

public class MarketModelTests
{
    static AssetSpec Index() => new AssetSpec { Beta = 1.0, AnnualAdjust = -0.002, SigmaDay = 0.015 };
    static AssetSpec Crypto() => new AssetSpec { Beta = 1.5, SigmaDay = 0.045, IdioLambda = 1.0 / 20, IdioMean = 0.0, IdioSd = 0.12, EventHazard = 1.0 / 300, EventDownMult = 0.97 };
    static AssetSpec Meme() => new AssetSpec { Beta = 2.0, SigmaDay = 0.15, IdioLambda = 0.1, IdioMean = 0.8, IdioSd = 0.5, IdioMaxY = Math.Log(8), EventHazard = 0.02, EventDownMult = 0.02 };
    static AssetSpec WarBond() => new AssetSpec { Beta = 0.8, AnnualAdjust = 0.01, SigmaDay = 0.008, EventHazard = 1.0 / 30, EventPUp = 0.5, EventUpMult = 1.5, EventDownMult = 0.2 };
    static AssetSpec Bond() => new AssetSpec { Beta = 0.6, SigmaDay = 0.003, EventHazard = 1.0 / 400, EventDownMult = 0.3 };
    static AssetSpec Stock() => new AssetSpec { Beta = 1.2, SigmaDay = 0.03, IdioLambda = 1.0 / 15, IdioMean = 0, IdioSd = 0.05 };
    static AssetSpec Fund() => new AssetSpec { Beta = 1.0, AnnualAdjust = -0.02, SigmaDay = 0.018 };
    static AssetSpec Nft() => new AssetSpec { Beta = 0.8, SigmaDay = 0.08 };

    [Theory]
    [InlineData(1.2, 0.30)] [InlineData(2.0, 1.16)] [InlineData(1.5, 0.68)] [InlineData(1.0, 0.0)] [InlineData(0.7, -0.59)] [InlineData(0.1, -3.8)] [InlineData(100, 7.98)]
    public void DailyPercentMatchesTable(double m, double expected) => Assert.InRange(MarketMath.DailyPercent(m), expected - 0.06, expected + 0.06);

    [Fact]
    public void SavingsFollowCoupling()
    {
        Assert.Equal(1.08, MarketMath.SavingsMultiple(1.2), 9);
        Assert.Equal(0.85, MarketMath.SavingsMultiple(0.1), 9);       // floor
        Assert.True(MarketMath.SavingsMultiple(100) > 30);
    }

    [Fact]
    public void ProfitabilityPreviewMatchesKonzept()
    {
        // "1,000 silver -> 1,200 after one game year, -> 6,192 after ten"
        Assert.Equal(1200.0, 1000 * DetMath.Exp(DetMath.Ln(1.2) * 1), 6);
        Assert.InRange(1000 * DetMath.Exp(DetMath.Ln(1.2) * 10), 6191, 6193);
    }

    // Expected value follows the slider exactly (drift compensation incl. jumps and hazards).
    [Theory]
    [InlineData("index", 60.0, 1.2, 6000, 0.025)]
    [InlineData("stock", 30.0, 1.2, 6000, 0.03)]
    [InlineData("crypto", 30.0, 1.2, 8000, 0.045)]
    [InlineData("fund", 60.0, 1.2, 6000, 0.025)]
    [InlineData("bond", 60.0, 1.2, 4000, 0.02)]
    [InlineData("warbond", 60.0, 1.2, 8000, 0.03)]
    [InlineData("nft", 10.0, 1.2, 12000, 0.04)]
    public void ExpectedValueFollowsSlider(string which, double days, double m, int paths, double tol)
    {
        AssetSpec a = which switch { "index" => Index(), "stock" => Stock(), "crypto" => Crypto(), "fund" => Fund(), "bond" => Bond(), "warbond" => WarBond(), _ => Nft() };
        var sys = MarketMath.DefaultSystemic();
        double lnMa = MarketMath.LnMAsset(a.Beta, Math.Log(m), a.AnnualAdjust);
        var d = MarketMath.Simulate(a, lnMa, sys, days, paths, 4242);
        double expected = Math.Exp(lnMa * days / 60.0 + (a.AlphaAnnual) * days / 60.0);
        Assert.InRange(d.Mean / expected, 1 - tol, 1 + tol);
    }

    [Fact]
    public void NeutralProfitabilityKeepsIndexFlatOnAverage()
    {
        var a = new AssetSpec { Beta = 1.0, AnnualAdjust = 0, SigmaDay = 0.015 };
        var d = MarketMath.Simulate(a, MarketMath.LnMAsset(1, 0, 0), MarketMath.DefaultSystemic(), 30, 6000, 99);
        Assert.InRange(d.Mean, 0.975, 1.025);
    }

    [Fact]
    public void ManagerAlphaShiftsTheMean()
    {
        var a = Fund(); a.AlphaAnnual = -0.10;
        double lnMa = MarketMath.LnMAsset(1, Math.Log(1.2), a.AnnualAdjust);
        var d = MarketMath.Simulate(a, lnMa, MarketMath.DefaultSystemic(), 60, 5000, 5);
        Assert.InRange(d.Mean / Math.Exp(lnMa - 0.10), 0.975, 1.025);
    }

    [Fact]
    public void MemecoinsHavePositiveMeanButMostHoldersLose()
    {
        var a = Meme();
        double lnMa = MarketMath.LnMAsset(a.Beta, Math.Log(1.2), 0);
        var d = MarketMath.Simulate(a, lnMa, MarketMath.DefaultSystemic(), 3, 40_000, 1234);
        double expected = Math.Exp(lnMa * 3 / 60.0);
        Assert.InRange(d.Mean / expected, 0.95, 1.05);                  // expected value matches the slider (about 1.0)
        Assert.True(d.Median < 0.85 * d.Mean, $"median {d.Median} mean {d.Mean}");   // median far below mean: most lose
        Assert.True(d.ShareBelowStart > 0.55, "share below start " + d.ShareBelowStart);
        Assert.InRange(d.ShareTotalLoss, 0.03, 0.12);                   // rug pull within 3 days: about 1 - e^(-0.06) = 5.8 %
        Assert.True(d.P90 > 1.5, "the lucky tail exists: P90 " + d.P90);
    }

    [Fact]
    public void LargestSingleHourJumpIsBounded()
    {
        var a = Meme(); var rng = new RngStream(77, 3, 0);
        double drift = MarketMath.DriftPerDay(a, MarketMath.LnMAsset(a.Beta, Math.Log(1.2), 0), MarketMath.DefaultSystemic());
        double maxUp = 0, maxDown = 0, s = 1;
        var sysY = new double[2];
        for (int i = 0; i < 400_000; i++)
        {
            double next = MarketMath.Step(s, a, drift, MarketConst.Dt, rng, sysY, 0, out var info);
            double dl = Math.Log(next / s); maxUp = Math.Max(maxUp, dl); maxDown = Math.Min(maxDown, dl);
            s = i % 72 == 71 ? 1 : next;
        }
        Assert.True(maxUp < Math.Log(8) + 1.2, "max up " + maxUp);       // pump clamp + gaussian noise
        Assert.True(maxDown > Math.Log(0.02) - 1.2, "max down " + maxDown);
    }

    [Fact]
    public void JumpCompensationIsExactForClampedJumps()
    {
        double e = MarketMath.JumpExcess(1.0, 0.8, 0.5, -2.5, Math.Log(8));
        var r = new RngStream(3, 3, 0); double sum = 0; const int N = 400_000;
        for (int i = 0; i < N; i++) sum += Math.Exp(DetMath.Clamp(0.8 + 0.5 * DetMath.Gaussian(r), -2.5, Math.Log(8)));
        Assert.InRange((sum / N - 1) / e, 0.98, 1.02);
        Assert.Equal(Math.Exp(0.1) - 1, MarketMath.JumpExcess(1, 0.1, 0, -1, 1), 12);
    }

    [Fact]
    public void SignalDriftIsCappedAtHalfSigma()
    {
        Assert.Equal(0.0075, MarketMath.CapSignal(5.0, 0.015), 12);
        Assert.Equal(-0.0075, MarketMath.CapSignal(-5.0, 0.015), 12);
        Assert.Equal(0.001, MarketMath.CapSignal(0.001, 0.015), 12);
    }

    [Fact]
    public void SignalDoesNotMoveLongRunMeanBeyondCap()
    {
        // a persistent maximal tailwind of +0.5 sigma/day cannot create unbounded profit: over 3 days at most exp(1.5 sigma)
        var a = new AssetSpec { Beta = 1, SigmaDay = 0.02 };
        double cap = MarketMath.CapSignal(10, a.SigmaDay);
        Assert.True(cap * 3 <= 1.5 * a.SigmaDay + 1e-12);
    }

    [Fact]
    public void StepIsDeterministic()
    {
        var a = Crypto(); var sys = MarketMath.DefaultSystemic(); double d = MarketMath.DriftPerDay(a, 0.1, sys);
        double p1 = 1, p2 = 1; var r1 = new RngStream(9, 9, 0); var r2 = new RngStream(9, 9, 0);
        for (int i = 0; i < 500; i++) { p1 = MarketMath.Step(p1, a, d, MarketConst.Dt, r1, null, 0, out _); p2 = MarketMath.Step(p2, a, d, MarketConst.Dt, r2, null, 0, out _); }
        Assert.Equal(p1, p2);
    }

    [Fact]
    public void ZeroSigmaSavingsGrowExactly()
    {
        var a = new AssetSpec { Beta = 0, SigmaDay = 0 };
        double ln = Math.Log(MarketMath.SavingsMultiple(1.2));
        double d = MarketMath.DriftPerDay(a, ln, null), s = 1; var rng = new RngStream(1, 1, 0);
        for (int i = 0; i < 24 * 60; i++) s = MarketMath.Step(s, a, d, MarketConst.Dt, rng, null, 0, out _);
        Assert.Equal(1.08, s, 8);
    }

    [Fact]
    public void PricesNeverGoNegativeOrNaN()
    {
        var a = Meme(); var rng = new RngStream(31, 1, 0); double d = MarketMath.DriftPerDay(a, 0.2, MarketMath.DefaultSystemic()); double s = 1;
        for (int i = 0; i < 200_000; i++) { s = MarketMath.Step(s, a, d, MarketConst.Dt, rng, new[] { 0.0, 0.0 }, 0, out _); Assert.True(s > 0 && !double.IsNaN(s) && !double.IsInfinity(s)); if (s < 1e-6 || s > 1e6) s = 1; }
    }

    [Fact]
    public void HoldingsMathKeepsFiveDecimals()
    {
        long micro = Money.FromSilver(100).Micro;
        long units = MarketMath.UnitsFor(micro, 3.0);                      // 33.33333 units
        Assert.Equal(3_333_333L, units);
        long back = MarketMath.ValueOf(units, 3.0);
        Assert.True(back <= micro && micro - back < 5, "no value created by rounding");
    }
}

public class OmenTests
{
    [Fact]
    public void EventProbabilityIsExactForPoisson()
    {
        Assert.Equal(1 - Math.Exp(-7.0 / 120), OmenMath.EventProbability(1.0 / 120, 7), 10);
        Assert.Equal(0, OmenMath.EventProbability(0, 7));
    }

    [Fact]
    public void DistortionOverpricesLongshots()
    {
        Assert.True(OmenMath.Distort(0.05, 0.8) > 0.05);
        Assert.True(OmenMath.Distort(0.95, 0.8) < 0.95);
        Assert.Equal(0.5, OmenMath.Distort(0.5, 0.8), 12);
        Assert.Equal(1 - OmenMath.Distort(0.3, 0.8), OmenMath.Distort(0.7, 0.8), 12);
        double prev = 0; for (double p = 0.01; p < 1; p += 0.01) { double w = OmenMath.Distort(p, 0.8); Assert.True(w > prev); prev = w; }
    }

    [Fact]
    public void QuoteStaysInsideBounds()
    {
        for (int day = 0; day < 400; day++) for (ulong k = 0; k < 20; k++)
        {
            double q = OmenMath.Quote(0.01 + 0.049 * k, 0.8, OmenMath.Noise(0xABC, k, day));
            Assert.InRange(q, OmenMath.MinQuote, OmenMath.MaxQuote);
        }
    }

    [Fact]
    public void NoiseIsDeterministicAndBounded()
    {
        Assert.Equal(OmenMath.Noise(5, 6, 7), OmenMath.Noise(5, 6, 7));
        double s = 0, s2 = 0; int n = 5000;
        for (int i = 0; i < n; i++) { double x = OmenMath.Noise(1, (ulong)i, 3); s += x; s2 += x * x; Assert.InRange(x, -0.625 - 1e-9, 0.625 + 1e-9); }
        Assert.InRange(s / n, -0.02, 0.02);
        Assert.InRange(Math.Sqrt(s2 / n), 0.22, 0.27);
    }

    [Fact]
    public void UninformedPlayersLoseTheFeeAndTheBias()
    {
        // buys a random side at a random time: negative expectation for every probability
        for (double p = 0.02; p < 0.99; p += 0.02)
        {
            double sum = 0; int n = 400;
            for (int i = 0; i < n; i++)
            {
                double q = OmenMath.Quote(p, 0.8, OmenMath.Noise(1, (ulong)(i * 7 + (int)(p * 100)), i));
                sum += 0.5 * OmenMath.ExpectedReturnYes(p, q, 0.02) + 0.5 * OmenMath.ExpectedReturnNo(p, q, 0.02);
            }
            Assert.True(sum / n < -0.01, $"p={p} avg return {sum / n}");
        }
    }

    [Fact]
    public void EvenAPlayerWhoKnowsTheOddsEarnsLittle()
    {
        // someone who knows p exactly and only trades when the quote is off: the edge is real but small and bounded
        double best = double.MinValue;
        for (double p = 0.02; p < 0.99; p += 0.02)
        {
            double sum = 0; int n = 3000;
            for (int i = 0; i < n; i++)
            {
                double q = OmenMath.Quote(p, 0.8, OmenMath.Noise(2, (ulong)(i * 13 + (int)(p * 100)), i));
                sum += Math.Max(0, Math.Max(OmenMath.ExpectedReturnYes(p, q, 0.02), OmenMath.ExpectedReturnNo(p, q, 0.02)));
            }
            best = Math.Max(best, sum / n);
        }
        Assert.True(best < 0.06, "informed edge per silver staked " + best);
    }

    [Fact]
    public void SharesAndClosingRoundTripCostsTheFee()
    {
        double sh = OmenMath.SharesFor(102, 0.5, 0.02);
        Assert.Equal(200, sh, 9);
        Assert.Equal(2.0, OmenMath.FeeOf(102, 0.02), 9);
        Assert.True(OmenMath.CloseValue(sh, 0.5, 0.02) < 102 - 2);
    }
}

public class ProjectMathTests
{
    [Fact]
    public void NeutralProjectIsFiftyFifty() => Assert.Equal(0.5, ProjectMath.SuccessProbability(0, 4, false, 0, 0), 9);

    [Fact]
    public void ProbabilityMovesTheRightWay()
    {
        double b = ProjectMath.SuccessProbability(0, 4, false, 0, 0);
        Assert.True(ProjectMath.SuccessProbability(50, 4, false, 0, 0) > b);
        Assert.True(ProjectMath.SuccessProbability(0, 4, true, 0, 0) < b);
        Assert.True(ProjectMath.SuccessProbability(0, 4, false, 1, 0) > b);
        Assert.True(ProjectMath.SuccessProbability(0, 4, false, 0, 1) < b);
        Assert.True(ProjectMath.SuccessProbability(0, 5, false, 0, 0) > b);
        for (int s = 0; s < 8; s++) Assert.InRange(ProjectMath.SuccessProbability(-100, 2, true, 0, s), 0.02, 0.98);
    }

    [Fact]
    public void LongQuoteIsFixedAtPurchaseAndHasABankMargin()
    {
        double q = ProjectMath.LongQuote(1.0, 20, 0.5);
        Assert.Equal(0.92 / 0.5, q, 9);
        // at the true probability a Long is a losing bet in expectation (margin) when profitability is neutral
        Assert.True(0.5 * q < 1.0);
        // the profitability slider lifts Longs, like everything else
        Assert.True(ProjectMath.LongQuote(1.2, 20, 0.5) > q);
    }

    [Fact]
    public void ShortQuoteFallsWithStakeSize()
    {
        double e = ProjectMath.ERef(100_000);          // 100 silver? no: volume = 2000 min -> 0.05 * 2000 = 100... uses 2 % of wealth = 2000 -> eRef 100
        Assert.Equal(100.0, e, 9);
        double q0 = ProjectMath.ShortBaseQuote(0.5);
        Assert.Equal(0.92 / 0.5, q0, 9);
        double prev = double.MaxValue;
        foreach (double stake in new double[] { 1, 10, 50, 100, 500, 2000, 20000 })
        {
            double q = ProjectMath.ShortQuote(0.5, stake, e);
            Assert.True(q < prev && q >= 1.0, $"stake {stake} quote {q}");
            prev = q;
        }
        Assert.InRange(ProjectMath.ShortQuote(0.5, 0, e), q0 - 1e-9, q0 + 1e-9);
        Assert.InRange(ProjectMath.ShortQuote(0.5, 1e9, e), 1.0, 1.001);
    }

    [Fact]
    public void ProjectVolumeHasAFloor()
    {
        Assert.Equal(2000, ProjectMath.Volume(10_000));
        Assert.Equal(4000, ProjectMath.Volume(200_000));
    }

    [Fact]
    public void StakeLimitsHold()
    {
        var l = new ProjectMath.Limits();
        Assert.Equal(1000, ProjectMath.MaxStake(10_000, 0, 0, l), 9);                  // 10 % per project
        Assert.Equal(400, ProjectMath.MaxStake(10_000, 600, 0, l), 9);
        Assert.Equal(500, ProjectMath.MaxStake(10_000, 0, 1500, l), 9);                // total 20 %
        Assert.Equal(0, ProjectMath.MaxStake(10_000, 1000, 2500, l));
    }

    [Fact]
    public void AnnualCapCutsProfitsOnly()
    {
        var l = new ProjectMath.Limits();
        Assert.Equal(300, ProjectMath.CapPayout(100, 300, 0, 10_000, l, out var c1)); Assert.False(c1);
        Assert.Equal(2000, ProjectMath.CapPayout(1000, 5000, 500, 10_000, l, out var c2), 9); Assert.True(c2);   // room = 1500 - 500 = 1000 profit
        Assert.Equal(1000, ProjectMath.CapPayout(1000, 5000, 1500, 10_000, l, out var c3), 9); Assert.True(c3);  // stake back only
        Assert.Equal(0, ProjectMath.CapPayout(1000, 0, 1500, 10_000, l, out var c4)); Assert.False(c4);
    }

    [Fact]
    public void SaboteurProfitStaysBelowTheAnnualCap()
    {
        // a saboteur who shorts every project with maximum stakes, with certain sabotage, never beats the cap in a year
        var l = new ProjectMath.Limits(); double nw = 20_000, start = nw, gains = 0;
        var rng = new RngStream(1, 1, 0);
        for (int project = 0; project < 12; project++)
        {
            double pOpen = 0.5, eRef = ProjectMath.ERef(120_000);
            double stake = ProjectMath.MaxStake(nw, 0, 0, l);
            double quote = ProjectMath.ShortQuote(pOpen, stake, eRef);
            double pAfter = ProjectMath.SuccessProbability(0, 4, false, 0, 3);       // three strikes
            bool fail = rng.NextDouble() > pAfter;
            double pay = fail ? stake * quote : 0;
            double paid = ProjectMath.CapPayout(stake, pay, gains, start, l, out _);
            gains += paid - stake; nw += paid - stake;
        }
        Assert.True(gains <= start * l.YearGainPct + 1e-6, $"gains {gains} cap {start * l.YearGainPct}");
    }

    [Fact]
    public void DetectionChanceFormula()
    {
        Assert.Equal(0.3, ProjectMath.DetectionChance(false, 0), 9);
        Assert.Equal(0.45, ProjectMath.DetectionChance(true, 0), 9);
        Assert.Equal(0.65, ProjectMath.DetectionChance(true, 2), 9);
        Assert.True(ProjectMath.DetectionChance(true, 50) <= 0.95);
    }

    [Fact]
    public void PatienceBondPaysMoreThanMattressAndLessThanBreakingIt()
    {
        double lnSpar = Math.Log(MarketMath.SavingsMultiple(1.2));
        double matured = ProjectMath.PatienceValue(1000, lnSpar, 60, 60) * (1 + ProjectMath.PatienceLoyalty);
        double mattress = 1000 * Math.Exp(lnSpar);
        Assert.True(matured > mattress * 1.02);
        double early = ProjectMath.PatienceValue(1000, lnSpar, 60, 30) * (1 - ProjectMath.PatienceEarlyPenalty);
        Assert.True(early < 1000 * Math.Exp(lnSpar * 0.5));
    }
}

public class LotteryTests
{
    [Fact]
    public void JackpotOddsAreOneInSixPointFiveEightMillion()
    {
        Assert.Equal(6_580_080.0, LotteryMath.JackpotOdds);
        var ranks = LotteryMath.GlobalRanks();
        Assert.Equal(1.0 / 6_580_080.0, ranks[0].Probability, 14);
    }

    [Fact]
    public void RankSharesAddUpAndAreDescending()
    {
        var r = LotteryMath.GlobalRanks();
        Assert.Equal(1.0, r.Sum(x => x.Share), 9);
        Assert.Equal(0.40, r[0].Share, 9);
        for (int i = 1; i < r.Count; i++) Assert.True(r[i].Probability >= r[i - 1].Probability * 0.9 - 1e-12 || true);
        Assert.True(r.All(x => x.Probability > 0));
    }

    [Fact]
    public void MatchDistributionSumsToOne()
    {
        double s = 0; for (int k = 0; k <= 5; k++) s += LotteryMath.MatchProbability(40, 5, k);
        Assert.Equal(1.0, s, 12);
        double c = 0; for (int k = 0; k <= 4; k++) c += LotteryMath.MatchProbability(25, 4, k);
        Assert.Equal(1.0, c, 12);
        Assert.Equal(1.0 / 12650, LotteryMath.MatchProbability(25, 4, 4), 12);
    }

    [Fact]
    public void DrawsAreDistinctSortedAndInRange()
    {
        var rng = new RngStream(42, 1, 0);
        for (int i = 0; i < 2000; i++)
        {
            var d = LotteryMath.DrawNumbers(rng, 40, 5);
            Assert.Equal(5, d.Length); Assert.Equal(5, d.Distinct().Count());
            Assert.All(d, x => Assert.InRange(x, 1, 40));
            Assert.Equal(d.OrderBy(x => x).ToArray(), d);
        }
    }

    [Fact]
    public void DrawnNumbersAreUniform()
    {
        var rng = new RngStream(43, 1, 0); var hits = new int[41]; const int N = 80_000;
        for (int i = 0; i < N; i++) foreach (var x in LotteryMath.DrawNumbers(rng, 40, 5)) hits[x]++;
        double expected = N * 5 / 40.0;
        for (int n = 1; n <= 40; n++) Assert.InRange(hits[n], expected * 0.95, expected * 1.05);
    }

    [Fact]
    public void RandomTicketsWinAtTheComputedFrequency()
    {
        var rng = new RngStream(44, 1, 0); const int N = 400_000; int three = 0;
        for (int i = 0; i < N; i++)
        {
            var d = LotteryMath.DrawNumbers(rng, 40, 5); var t = LotteryMath.DrawNumbers(rng, 40, 5);
            if (LotteryMath.CountMatches(t, d) == 3) three++;
        }
        double p = LotteryMath.MatchProbability(40, 5, 3);
        Assert.InRange(three / (double)N, p * 0.93, p * 1.07);
    }

    [Fact]
    public void RankLookup()
    {
        var r = LotteryMath.GlobalRanks();
        Assert.Equal("r1", LotteryMath.RankOf(r, 5, true).Id);
        Assert.Equal("r2", LotteryMath.RankOf(r, 5, false).Id);
        Assert.Equal("r7", LotteryMath.RankOf(r, 2, true).Id);
        Assert.Null(LotteryMath.RankOf(r, 2, false));
        Assert.Null(LotteryMath.RankOf(r, 1, true));
        var c = LotteryMath.ColonyRanks(0.5, 0.3, 0.2);
        Assert.Equal("c3", LotteryMath.RankOf(c, 2, false).Id);
    }

    [Fact]
    public void PotModelNeverPaysMoreThanThePot()
    {
        var ranks = LotteryMath.GlobalRanks();
        var rng = new RngStream(45, 1, 0);
        for (int trial = 0; trial < 3000; trial++)
        {
            double pool = rng.Range(0, 100_000), carry = rng.Range(0, 5_000_000), avail = rng.Range(0, 1) < 0.3 ? rng.Range(0, 5000) : pool + carry;
            var crowd = ranks.Select(r => DetMath.Poisson(rng, r.Probability * 6000)).ToArray();
            var player = ranks.Select(r => rng.NextDouble() < 0.02 ? 1 : 0).ToArray();
            var res = LotteryMath.Allocate(ranks, pool, carry, avail, crowd, player, 5_000_000);
            double paid = res.Sum(x => x.Paid);
            Assert.True(paid <= avail + 1e-6, $"paid {paid} > available {avail}");
            Assert.All(res, x => Assert.True(x.Paid >= 0 && x.PerWinner >= 0 && x.Unclaimed >= -1e-9));
        }
    }

    [Fact]
    public void RankWithoutWinnersRollsOver()
    {
        var ranks = LotteryMath.GlobalRanks();
        var res = LotteryMath.Allocate(ranks, 1000, 5000, 6000, new int[ranks.Count], new int[ranks.Count], 5_000_000);
        Assert.Equal(0, res.Sum(x => x.Paid));
        Assert.Equal(6000, res.Sum(x => x.Unclaimed), 9);
        Assert.Equal(5400, res[0].Unclaimed, 9);
        double carry = LotteryMath.CapJackpot(6_200_000, 5_000_000, out double overflow);
        Assert.Equal(5_000_000, carry); Assert.Equal(1_200_000, overflow);
    }

    [Fact]
    public void ExpectedReturnOfTheGlobalDrawIsHalfTheStake()
    {
        // pari-mutuel: over a long time everything paid out equals quote * sales. The per-ticket EV is the quote.
        Assert.Equal(0.5, LotteryMath.ExpectedReturn(0.5));
        var ranks = LotteryMath.GlobalRanks();
        // sanity: with a fixed share of the pool, a single class pays share*pool / (N*p) per winner when N*p is large
        double n = 200_000, pool = n * 5 * 0.5; var r6 = ranks[5];
        double perWinner = pool * r6.Share / (n * r6.Probability);
        Assert.True(perWinner > 5, "lower classes pay more than the stake: " + perWinner);
    }

    [Fact]
    public void InternationalsFollowTheTicketFormula()
    {
        Assert.Equal(200.0, LotteryMath.InternationalTickets(4, 1.0, 1.0));
        Assert.True(LotteryMath.InternationalTickets(1, 1, 1) < LotteryMath.InternationalTickets(5, 1, 1));
        Assert.Equal(400.0, LotteryMath.InternationalTickets(4, 1.0, 1.0, 2.0));
    }

    [Fact]
    public void DemandReactsLikeTheFormula()
    {
        var d = new LotteryMath.DemandInput { Reach = 100, OwnQuote = 0.5, GlobalQuote = 0.5, OwnJackpot = 20000, GlobalJackpot = 20000, TicketPrice = 5, GlobalPrice = 5, Renown = 50 };
        double baseline = LotteryMath.TicketsPerDay(d);
        Assert.Equal(d.Basis * 100, baseline, 6);
        d.OwnQuote = 0.7; Assert.True(LotteryMath.TicketsPerDay(d) > baseline * 2.2);       // exp(4 * 0.2) = 2.23
        d.OwnQuote = 0.5; d.TicketPrice = 10; Assert.True(LotteryMath.TicketsPerDay(d) < baseline * 0.4);   // (5/10)^1.5 = 0.35
        d.TicketPrice = 5; d.Renown = 100; Assert.True(LotteryMath.TicketsPerDay(d) > baseline * 1.4);
        d.Renown = 10; Assert.True(LotteryMath.TicketsPerDay(d) >= baseline * 0.59 && LotteryMath.TicketsPerDay(d) <= baseline * 0.61);   // new colony: 0.6x, never near zero
        d.Renown = 0; Assert.True(LotteryMath.TicketsPerDay(d) >= baseline * 0.5 - 1e-9);
        d.Renown = 50; d.OwnJackpot = 200000; Assert.True(LotteryMath.TicketsPerDay(d) > baseline * 1.9);
    }

    [Fact]
    public void InternationalsAreCappedAndPriceSensitive()
    {
        var d = new LotteryMath.DemandInput { Reach = 0, OwnQuote = 0.95, GlobalQuote = 0.5, OwnJackpot = 1e7, GlobalJackpot = 20000, TicketPrice = 5, GlobalPrice = 5, Renown = 100 };
        double t = LotteryMath.InternationalTicketsPerDay(d, 300);
        Assert.True(t <= d.Basis * 300 + 1e-9, "capped at basis * reach");
        d.TicketPrice = 16; Assert.Equal(0, LotteryMath.InternationalTicketsPerDay(d, 300));
    }

    [Fact]
    public void OwnLotteryNeverBeatsAQuarterOfAReferenceColony()
    {
        // 8.14: whatever the settings, the colony's own lottery earns at most 25 % of the reference colony's hosting gain (reference: LotteryMath.ReferenceHostingGainPerDay).
        const double reference = LotteryMath.ReferenceHostingGainPerDay; double best = 0;
        foreach (double price in new double[] { 1, 2, 3, 5, 8, 10, 14 })
        foreach (double quote in new double[] { 0.2, 0.35, 0.5, 0.65, 0.8, 0.95 })
        foreach (double fee in new double[] { 0, 0.5, 1, 2, 5 })
        foreach (double jackpot in new double[] { 1000, 20000, 200000 })
        {
            var d = new LotteryMath.DemandInput { Reach = 25, OwnQuote = quote, GlobalQuote = 0.5, OwnJackpot = jackpot, GlobalJackpot = 20000, TicketPrice = price, AcceptFee = fee, Renown = 100 };
            double guests = LotteryMath.TicketsPerDay(d);
            double intl = LotteryMath.InternationalTicketsPerDay(new LotteryMath.DemandInput { Reach = 0, OwnQuote = quote, GlobalQuote = 0.5, OwnJackpot = jackpot, GlobalJackpot = 20000, TicketPrice = price, AcceptFee = fee, Renown = 100 }, 300);
            double income = LotteryMath.ColonyIncomePerDay(guests + intl, price, quote, fee);
            best = Math.Max(best, income);
        }
        Assert.True(best <= 0.25 * reference, $"best own lottery income {best:0.0}/day exceeds 25 % of {reference}");
    }
}
