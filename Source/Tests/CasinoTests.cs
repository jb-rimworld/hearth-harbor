// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Hearth;
using Hearth.CasinoMath;
using Xunit;
using Xunit.Abstractions;

public class SlotTests
{
    readonly ITestOutputHelper o;
    public SlotTests(ITestOutputHelper o) { this.o = o; }

    [Theory]
    [InlineData(SlotVolatility.Low, 0.95)] [InlineData(SlotVolatility.Medium, 0.95)] [InlineData(SlotVolatility.High, 0.95)]
    [InlineData(SlotVolatility.Low, 0.92)] [InlineData(SlotVolatility.Medium, 0.92)] [InlineData(SlotVolatility.High, 0.92)]
    [InlineData(SlotVolatility.Low, 0.88)] [InlineData(SlotVolatility.Medium, 0.88)] [InlineData(SlotVolatility.High, 0.88)]
    [InlineData(SlotVolatility.Medium, 0.70)] [InlineData(SlotVolatility.Medium, 0.995)]
    public void ExactRtpHitsTheTarget(SlotVolatility v, double target)
    {
        var t = SlotPaytable.For(v, target);
        Assert.Equal(target, t.Rtp, 12);
    }

    [Fact]
    public void ProbabilitiesSumToOne()
    {
        foreach (SlotVolatility v in Enum.GetValues(typeof(SlotVolatility)))
        {
            var t = SlotPaytable.For(v, 0.92);
            double s = 0;
            for (int a = 0; a < 7; a++) for (int b = 0; b < 7; b++) for (int c = 0; c < 7; c++) s += t.Probability(a, b, c);
            Assert.Equal(1.0, s, 12);
        }
    }

    [Fact]
    public void VolatilityOrderAndHitRate()
    {
        var low = SlotPaytable.For(SlotVolatility.Low, 0.92); var med = SlotPaytable.For(SlotVolatility.Medium, 0.92); var high = SlotPaytable.For(SlotVolatility.High, 0.92);
        o.WriteLine($"low hit {low.HitRate:P1} sigma {low.Sigma:0.00} max {low.MaxMultiplier:0}x | med hit {med.HitRate:P1} sigma {med.Sigma:0.00} max {med.MaxMultiplier:0}x | high hit {high.HitRate:P1} sigma {high.Sigma:0.00} max {high.MaxMultiplier:0}x");
        Assert.True(low.Sigma < med.Sigma && med.Sigma < high.Sigma);
        Assert.True(low.HitRate > high.HitRate);
        Assert.InRange(low.HitRate, 0.30, 0.55);
        Assert.InRange(high.HitRate, 0.05, 0.25);
        Assert.True(high.MaxMultiplier > 1000 * 0.5);
    }

    [Fact]
    public void RtpIsNeverOver100AtDefaultTiers()
    {
        foreach (SlotVolatility v in Enum.GetValues(typeof(SlotVolatility)))
            foreach (double r in new[] { 0.95, 0.92, 0.88 })
                Assert.True(SlotPaytable.For(v, r).Rtp < 1.0);
    }

    [Theory]
    [InlineData(SlotVolatility.Low)] [InlineData(SlotVolatility.Medium)] [InlineData(SlotVolatility.High)]
    public void MillionSpinsMatchExactRtp(SlotVolatility v)
    {
        var t = SlotPaytable.For(v, 0.92);
        var rng = new RngStream(987654321, RngStream.HashString("casino.slots.test." + v));
        const int n = 1_000_000; double sum = 0, hits = 0;
        for (int i = 0; i < n; i++) { double m = t.Spin(rng, out _, out _, out _); sum += m; if (m > 0) hits++; }
        double se = t.Sigma / Math.Sqrt(n);
        double mean = sum / n;
        o.WriteLine($"{v}: simulated RTP {mean:0.0000} vs exact {t.Rtp:0.0000} (SE {se:0.0000}); hit {hits / n:P2} vs {t.HitRate:P2}");
        Assert.InRange(mean, t.Rtp - 5 * se, t.Rtp + 5 * se);
        Assert.InRange(hits / n, t.HitRate - 0.004, t.HitRate + 0.004);
    }
}

public class RouletteTests
{
    readonly ITestOutputHelper o;
    public RouletteTests(ITestOutputHelper o) { this.o = o; }

    [Fact]
    public void EuropeanWheelEveryBetHasEdgeOneOver37()
    {
        var rules = RouletteRules.European;
        foreach (var bet in RouletteBet.AllBets())
        {
            // raw (unscaled) maths: expected return of every bet on a single-zero wheel is exactly 36/37
            Assert.Equal(36.0 / 37.0, rules.RawExpectedReturn(bet), 12);
            Assert.Equal(1.0 / 37.0, rules.ExactEdge(bet), 12);
        }
    }

    [Fact]
    public void AmericanWheelEdgeIsTwoOver38()
    {
        var rules = RouletteRules.American;
        foreach (var bet in RouletteBet.AllBets().Where(b => !b.Covers(37)))
            Assert.Equal(36.0 / 38.0, rules.RawExpectedReturn(bet), 12);
        Assert.Equal(2.0 / 38.0, rules.ExactEdge(RouletteBet.Red()), 12);
    }

    [Fact]
    public void BetCatalogueIsComplete()
    {
        var all = RouletteBet.AllBets().ToList();
        Assert.Equal(154, all.Count);
        Assert.Equal(37, all.Count(b => b.Kind == RouletteBetKind.Straight));
        Assert.Equal(60, all.Count(b => b.Kind == RouletteBetKind.Split));
        Assert.Equal(22, all.Count(b => b.Kind == RouletteBetKind.Corner));
        // pockets covered match the payout odds: odds = 36/covered − 1
        foreach (var b in all.Where(b => b.Kind != RouletteBetKind.Straight || b.Pockets[0] != 37))
        {
            int cov = b.Pockets.Length; if (b.IsEvenMoney) Assert.Equal(18, cov);
            Assert.Equal(36.0 / cov - 1, b.Odds, 9);
        }
        Assert.Equal(18, RouletteBet.Red().Pockets.Length);
        Assert.Equal(18, RouletteBet.Black().Pockets.Length);
        Assert.Empty(RouletteBet.Red().Pockets.Intersect(RouletteBet.Black().Pockets));
    }

    [Theory]
    [InlineData(0.0054)] [InlineData(0.0135)] [InlineData(0.027)] [InlineData(0.0526)] [InlineData(0.0658)] [InlineData(0.0789)]
    public void EveryTierEdgeIsExactOnEveryBet(double edge)
    {
        var rules = RouletteRules.ForEdge(edge);
        foreach (var bet in RouletteBet.AllBets().Where(b => !b.Covers(37) || rules.Zeros == 2))
            Assert.Equal(edge, rules.ExactEdge(bet), 12);
        o.WriteLine($"edge {edge:P2}: zeros {rules.Zeros}, la partage {rules.LaPartage}");
    }

    [Fact]
    public void LaPartageHalvesTheEvenMoneyEdge()
    {
        var rules = new RouletteRules(1, true, 1.0 / 74);
        Assert.Equal(36.5 / 37, rules.RawExpectedReturn(RouletteBet.Red()), 12);
        Assert.Equal(1.0 / 74, rules.ExactEdge(RouletteBet.Red()), 12);
        // the zero returns half the stake on even-money bets
        Assert.Equal(0.5, rules.Return(RouletteBet.Red(), 0), 9);
        Assert.Equal(0.0, rules.Return(RouletteBet.Straight(5), 0), 9);
    }

    [Theory]
    [InlineData(0.027)] [InlineData(0.0135)] [InlineData(0.0526)]
    public void MillionSpinsMatchTheEdge(double edge)
    {
        var rules = RouletteRules.ForEdge(edge);
        var rng = new RngStream(42, RngStream.HashString("casino.roulette.test"));
        var bets = new[] { RouletteBet.Red(), RouletteBet.Straight(17), RouletteBet.Dozen(2), RouletteBet.Corner(8), RouletteBet.Odd() };
        foreach (var bet in bets)
        {
            const int n = 1_000_000; double net = 0;
            for (int i = 0; i < n; i++) net += rules.Return(bet, rules.Spin(rng)) - 1.0;
            double mean = net / n, se = rules.Sigma(bet) / Math.Sqrt(n);
            o.WriteLine($"{bet.Label} @ {edge:P2}: simulated edge {-mean:P3} (SE {se:P3})");
            Assert.InRange(-mean, edge - 5 * se, edge + 5 * se);
        }
    }

    [Fact]
    public void SigmaOfStraightBetIsAbout5_8()
    {
        var rules = RouletteRules.European;
        Assert.InRange(rules.Sigma(RouletteBet.Straight(7)), 5.7, 5.9);
        Assert.InRange(rules.Sigma(RouletteBet.Red()), 0.98, 1.0);
    }

    [Fact]
    public void InvalidBetsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => RouletteBet.Split(1, 5));
        Assert.Throws<ArgumentException>(() => RouletteBet.Split(3, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouletteBet.Corner(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouletteBet.Straight(40));
    }
}

public class BlackjackTests
{
    readonly ITestOutputHelper o;
    public BlackjackTests(ITestOutputHelper o) { this.o = o; }

    [Fact]
    public void StandardRulesHaveASmallEdgeForPerfectPlay()
    {
        var s = BlackjackSolver.Get(BlackjackRules.Standard, 1.0);
        o.WriteLine($"3:2 S17, perfect play: edge {s.HouseEdge:P3}, sigma {s.Sigma:0.000}");
        Assert.InRange(s.HouseEdge, 0.002, 0.012);
        Assert.InRange(s.Sigma, 1.05, 1.35);
    }

    [Fact]
    public void GreedyRulesCostThePlayerMore()
    {
        var a = BlackjackSolver.Get(BlackjackRules.Standard, 1.0).HouseEdge;
        var b = BlackjackSolver.Get(BlackjackRules.Greedy, 1.0).HouseEdge;
        o.WriteLine($"3:2 S17 {a:P3}  vs  6:5 H17 {b:P3}");
        Assert.True(b > a + 0.012);
        Assert.InRange(b, 0.015, 0.035);
    }

    [Fact]
    public void WorseStrategyMeansBiggerEdge()
    {
        double prev = -1;
        foreach (double acc in new[] { 1.0, 0.8, 0.6, 0.4, 0.25 })
        {
            double e = BlackjackSolver.Get(BlackjackRules.Standard, acc).HouseEdge;
            o.WriteLine($"accuracy {acc:0.00}: edge {e:P2}");
            Assert.True(e > prev); prev = e;
        }
        Assert.InRange(prev, 0.03, 0.10);
    }

    [Fact]
    public void AccuracyFormula()
    {
        Assert.Equal(0.25, BlackjackSolver.AccuracyFor(0, 0), 9);
        Assert.Equal(1.0, BlackjackSolver.AccuracyFor(20, 20), 9);
        Assert.InRange(BlackjackSolver.AccuracyFor(10, 10), 0.6, 0.7);
        Assert.Equal(0.25, BlackjackSolver.AccuracyFor(10, 10, 0.0), 9);   // knocked out: coin-flip play
    }

    [Fact]
    public void DealerBlackjackProbabilities()
    {
        var s = BlackjackSolver.Get(BlackjackRules.Standard, 1.0);
        Assert.Equal(4.0 / 13, s.DealerBlackjackProbability(1), 12);
        Assert.Equal(1.0 / 13, s.DealerBlackjackProbability(10), 12);
        Assert.Equal(0, s.DealerBlackjackProbability(6), 12);
    }

    [Theory]
    [InlineData(1.0, false, 1.5)] [InlineData(0.5, false, 1.5)] [InlineData(1.0, true, 1.2)] [InlineData(0.25, true, 1.2)]
    public void MillionRoundsMatchExactEdge(double acc, bool h17, double pays)
    {
        var s = BlackjackSolver.Get(new BlackjackRules(h17, pays), acc);
        var rng = new RngStream(2026, RngStream.HashString("casino.blackjack.test"));
        const int n = 1_000_000; double sum = 0, sum2 = 0;
        for (int i = 0; i < n; i++) { double r = s.PlayRound(rng).Result; sum += r; sum2 += r * r; }
        double mean = sum / n, sd = Math.Sqrt(sum2 / n - mean * mean), se = sd / Math.Sqrt(n);
        o.WriteLine($"acc {acc} {(h17 ? "H17" : "S17")} {pays}: simulated EV {mean:+0.0000;-0.0000} vs exact {s.ExpectedValue:+0.0000;-0.0000} (SE {se:0.0000}); sd {sd:0.000} vs {s.Sigma:0.000}");
        Assert.InRange(mean, s.ExpectedValue - 5 * se, s.ExpectedValue + 5 * se);
        Assert.InRange(sd, s.Sigma - 0.02, s.Sigma + 0.02);
    }

    [Fact]
    public void RoundResultsAreConsistent()
    {
        var s = BlackjackSolver.Get(BlackjackRules.Standard, 0.7);
        var rng = new RngStream(5, 5);
        for (int i = 0; i < 20000; i++)
        {
            var r = s.PlayRound(rng);
            Assert.True(r.Player.Count >= 2 && r.Dealer.Count >= 2);
            if (r.PlayerBlackjack && !r.DealerBlackjack) Assert.Equal(1.5, r.Result, 9);
            if (r.PlayerTotal > 21) Assert.True(r.Result < 0);
            Assert.InRange(r.Result, -2, 2);
            if (r.Doubled) Assert.Equal(3, r.Player.Count > 2 ? 3 : 3 + (r.Player.Count - 3));
        }
    }
}

public class RimdeckTests
{
    readonly ITestOutputHelper o;
    public RimdeckTests(ITestOutputHelper o) { this.o = o; }

    [Fact]
    public void DuelFollowsTheFormula()
    {
        var p = RimdeckMath.WinProbabilities(new[] { 12.0, 8.0 });
        Assert.Equal(1.0, p.Sum(), 12);
        double expected = 0.5 + 0.08 * Math.Tanh(4.0 / 6.0);
        Assert.Equal(expected, p[0], 12);
        Assert.Equal(0.5, RimdeckMath.WinProbabilities(new[] { 9.0, 9.0 })[0], 12);
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(6)]
    public void WinProbabilitiesAreADistribution(int n)
    {
        var skills = Enumerable.Range(0, n).Select(i => 4.0 + 2 * i).ToArray();
        var p = RimdeckMath.WinProbabilities(skills);
        Assert.Equal(1.0, p.Sum(), 12);
        for (int i = 1; i < n; i++) Assert.True(p[i] > p[i - 1]);
        Assert.All(p, x => Assert.InRange(x, 0.0, 1.0));
    }

    [Fact]
    public void RakeIsCapped()
    {
        Assert.Equal(1.0, RimdeckMath.Rake(20, 0.05, 3), 12);
        Assert.Equal(3.0, RimdeckMath.Rake(400, 0.05, 3), 12);
        Assert.Equal(0.05, RimdeckMath.HouseEdge(4, 10, 0.05, 3), 12);
        Assert.Equal(3.0 / 400, RimdeckMath.HouseEdge(4, 100, 0.05, 3), 12);
    }

    [Fact]
    public void EqualPlayersLoseExactlyTheirShareOfTheRake()
    {
        var skills = new[] { 8.0, 8.0, 8.0, 8.0 };
        // pot 40, rake 5 % = 2, each winner chance 1/4: (38 / 4) - 10 = -0.5 per hand
        for (int s = 0; s < 4; s++) Assert.Equal(-0.5, RimdeckMath.ExpectedNet(skills, s, 10, 0.05, 3), 9);
    }

    [Fact]
    public void MillionHandsHouseTakeAndSkillAdvantage()
    {
        var skills = new[] { 14.0, 8.0, 8.0, 5.0 };
        var rng = new RngStream(7, RngStream.HashString("casino.poker.test"));
        const int n = 1_000_000; var net = new double[4]; double rake = 0;
        for (int i = 0; i < n; i++)
        {
            var h = RimdeckMath.Deal(rng, skills, 10, 0.05, 3);
            for (int s = 0; s < 4; s++) net[s] -= 10;
            net[h.Winner] += h.Payout; rake += h.Rake;
        }
        Assert.Equal(2.0 * n, rake, 3);                            // the rake is exact on every hand
        Assert.Equal(0.0, net.Sum() + rake, 3);                    // money is conserved
        for (int s = 0; s < 4; s++)
        {
            double exp = RimdeckMath.ExpectedNet(skills, s, 10, 0.05, 3), mean = net[s] / n;
            double sd = 10 * 1.0, se = sd / Math.Sqrt(n) * 3;      // payout ~ 38 with p ~ 0.25: sd ~ 16
            o.WriteLine($"seat {s} skill {skills[s]}: simulated {mean:+0.000;-0.000} vs exact {exp:+0.000;-0.000}");
            Assert.InRange(mean, exp - 5 * se, exp + 5 * se);
        }
        Assert.True(net[0] > net[3]);
        Assert.True(net[0] / n > -0.5 + 0.3);                      // the best player beats the rake share
    }
}

public class AddictionMathTests
{
    [Fact]
    public void ChanceMatchesTheDesignExamples()
    {
        Assert.Equal(0, GamblingMath.AddictionChance(2, 0.08, 1));
        Assert.Equal(0.08, GamblingMath.AddictionChance(3, 0.08, 1), 9);
        Assert.Equal(0.108, GamblingMath.AddictionChance(4, 0.08, 1), 9);
        Assert.InRange(GamblingMath.AddictionChance(6, 0.08, 1), 0.196, 0.198);
        Assert.InRange(GamblingMath.AddictionChance(8, 0.08, 1), 0.358, 0.360);
        Assert.InRange(GamblingMath.AddictionChance(10, 0.08, 1), 0.65, 0.66);
        Assert.Equal(0.9, GamblingMath.AddictionChance(30, 0.08, 1.5), 9);
        Assert.Equal(0, GamblingMath.AddictionChance(9, 0.08, 0));
        Assert.Equal(0, GamblingMath.AddictionChance(9, 0, 1));
    }

    [Fact]
    public void StagesAndNeedFall()
    {
        Assert.Equal(1, GamblingMath.StageFromDepth(0));
        Assert.Equal(1, GamblingMath.StageFromDepth(0.19));
        Assert.Equal(2, GamblingMath.StageFromDepth(0.2));
        Assert.Equal(5, GamblingMath.StageFromDepth(0.8));
        Assert.Equal(5, GamblingMath.StageFromDepth(3));
        Assert.Equal(0.5, GamblingMath.NeedFallPerDay(1), 9);
        Assert.Equal(0.75, GamblingMath.NeedFallPerDay(2), 9);
        Assert.Equal(1.125, GamblingMath.NeedFallPerDay(3), 9);
        Assert.InRange(GamblingMath.NeedFallPerDay(5), 2.53, 2.54);
        Assert.InRange(GamblingMath.SessionsNeededPerDay(5), 7.2, 7.3);
        Assert.Equal(40, GamblingMath.MoodPenalty(5));
        Assert.Equal(50, GamblingMath.AbstinenceDaysNeeded(5, 10));
    }

    [Fact]
    public void SlidingWindowForgetsOldSessions()
    {
        var w = new GamblingMath.SessionWindow();
        w.Add(0); w.Add(1000); w.Add(2000);
        Assert.Equal(3, w.Count(3000));
        Assert.Equal(1, w.Count(GamblingMath.WindowTicks + 1500));
        Assert.Equal(0, w.Count(GamblingMath.WindowTicks * 3));
    }

    [Fact]
    public void SeededLoopAddictionFrequencyMatchesTheFormula()
    {
        // 100,000 players each play 5 sessions inside three days: P(addicted) = 1 − Π(1 − p_k)
        var rng = new RngStream(11, RngStream.HashString("casino.addiction.test"));
        double p3 = GamblingMath.AddictionChance(3, 0.08, 1), p4 = GamblingMath.AddictionChance(4, 0.08, 1), p5 = GamblingMath.AddictionChance(5, 0.08, 1);
        double expected = 1 - (1 - p3) * (1 - p4) * (1 - p5);
        int n = 200_000, hits = 0;
        for (int i = 0; i < n; i++)
        {
            bool a = false;
            for (int s = 1; s <= 5 && !a; s++) a = rng.Chance(GamblingMath.AddictionChance(s, 0.08, 1));
            if (a) hits++;
        }
        double f = (double)hits / n, se = Math.Sqrt(expected * (1 - expected) / n);
        Assert.InRange(f, expected - 5 * se, expected + 5 * se);
    }
}

public class HouseRiskTests
{
    [Fact]
    public void MaxBetMatchesTheWorkedExample()
    {
        double b = HouseRisk.MaxBet(4000, 9.9, 1200, 1);
        Assert.InRange(b, 2.8, 3.0);
        Assert.True(HouseRisk.MaxBet(8000, 9.9, 1200, 1) > b);
        Assert.True(HouseRisk.MaxBet(4000, 9.9, 1200, 4) < b);
        Assert.Equal(0, HouseRisk.MaxBet(0, 9.9, 1200, 1));
    }

    [Theory]
    [InlineData(SlotVolatility.Low)] [InlineData(SlotVolatility.Medium)] [InlineData(SlotVolatility.High)]
    public void HouseRarelyGoesBrokeInSixtyDays(SlotVolatility v)
    {
        var t = SlotPaytable.For(v, 0.92);
        var rng = new RngStream(3, RngStream.HashString("casino.ruin.test"));
        double ruin = HouseRisk.RuinProbabilityAdaptive(rng, 4000, 1 - t.Rtp, t.Sigma, 1200, 60, 40000);
        Assert.True(ruin < 0.01, "ruin probability " + ruin);
    }

    [Fact]
    public void FixedHugeBetsWouldBeDangerous()
    {
        var t = SlotPaytable.For(SlotVolatility.High, 0.92);
        var rng = new RngStream(3, RngStream.HashString("casino.ruin.test2"));
        double reckless = HouseRisk.RuinProbability(rng, 400, 1 - t.Rtp, t.Sigma, 1200, 40, 60, 4000);
        Assert.True(reckless > 0.05, "reckless ruin probability " + reckless);
    }
}
