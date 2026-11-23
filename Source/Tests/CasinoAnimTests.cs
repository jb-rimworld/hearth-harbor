// SPDX-License-Identifier: MIT
// Unit tests for the animation maths of the casino (1.1.0, F30): the display must always agree with an outcome that is decided first.
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.CasinoMath;
using Xunit;

public class CasinoAnimTests
{
    [Fact]
    public void EasingsAreMonotoneAndHitTheirEnds()
    {
        var curves = new Func<double, double>[] { Ease.OutCubic, Ease.InOutCubic, Ease.OutQuad, Ease.InQuad, Ease.OutQuart, Ease.SmoothStep };
        foreach (var f in curves)
        {
            Assert.Equal(0.0, f(0), 9); Assert.Equal(1.0, f(1), 9);
            double prev = -1;
            for (int i = 0; i <= 200; i++) { double v = f(i / 200.0); Assert.True(v >= prev - 1e-12); prev = v; }
            Assert.Equal(1.0, f(5), 9); Assert.Equal(0.0, f(-3), 9);
        }
        Assert.Equal(1.0, Ease.OutBack(1), 9);
        Assert.True(Enumerable.Range(1, 99).Any(i => Ease.OutBack(i / 100.0) > 1.0), "OutBack overshoots");
        Assert.Equal(0.0, Ease.Hops(0, 3), 9); Assert.Equal(0.0, Ease.Hops(1, 3), 9);
        Assert.True(Ease.Hops(1 / 6.0, 3) > Ease.Hops(0.5, 3), "each hop is lower");
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void WheelLayoutsHaveEveryPocketOnce(int zeros)
    {
        var o = WheelLayout.Order(zeros);
        Assert.Equal(36 + zeros, o.Length);
        Assert.Equal(o.Length, o.Distinct().Count());
        Assert.Contains(0, o);
        if (zeros == 2) Assert.Contains(37, o); else Assert.DoesNotContain(37, o);
        for (int n = 1; n <= 36; n++) Assert.Contains(n, o);
        foreach (int p in o) Assert.Equal(p, WheelLayout.PocketAt(WheelLayout.PocketAngle(p, zeros), zeros));
        // colours alternate red/black around the wheel, apart from the zeros
        for (int i = 0; i < o.Length; i++)
        {
            int a = o[i], b = o[(i + 1) % o.Length];
            if (a == 0 || a == 37 || b == 0 || b == 37) continue;
            Assert.NotEqual(RouletteBet.IsRed(a), RouletteBet.IsRed(b));
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void TheBallLandsInTheDrawnPocket(int zeros)
    {
        for (int pocket = 0; pocket < 36 + zeros; pocket++)
            for (ulong seed = 1; seed < 6; seed++)
            {
                var plan = RouletteSpinPlan.Make(pocket, zeros, seed * 37.0, seed * 7919);
                Assert.Equal(pocket, plan.PocketUnderBall(1.0));
                Assert.Equal(pocket, plan.PocketUnderBall(plan.LandT + 0.01));
                Assert.Equal(0.0, plan.BallRadius(1.0), 9);
                Assert.Equal(1.0, plan.BallRadius(0.1), 9);
                // continuity at the landing: no jump of the ball
                double before = plan.BallAngle(plan.LandT - 1e-6), after = plan.BallAngle(plan.LandT);
                double d = Math.Abs(WheelLayout.Norm(before - after + 180) - 180);
                Assert.True(d < 0.5, "ball jumps by " + d + " degrees at landing");
                Assert.True(plan.BallTravel >= 5 * 360, "the ball runs at least five laps");
            }
    }

    [Fact]
    public void ReelStripsEndOnTheResult()
    {
        for (int from = 0; from < 7; from++)
            for (int final = 0; final < 7; final++)
            {
                var s = ReelStrip.Build(from, final, 18, (ulong)(from * 7 + final + 1));
                Assert.Equal(18, s.Length); Assert.Equal(from, s[0]); Assert.Equal(final, s[s.Length - 1]);
                for (int i = 1; i < s.Length; i++) Assert.True(s[i] >= 0 && s[i] < 7);
                for (int i = 1; i < s.Length - 1; i++) Assert.NotEqual(s[i], s[i - 1]);
            }
        Assert.Equal(17.0, ReelStrip.Position(2.0, 1.0, 18), 9);
        Assert.Equal(0.0, ReelStrip.Position(0.0, 1.0, 18), 9);
        Assert.True(Enumerable.Range(1, 99).Any(i => ReelStrip.Position(i / 100.0, 1.0, 18) > 17.0), "the reel overshoots a little before it settles");
    }

    [Fact]
    public void CardCosmeticsAreStable()
    {
        Assert.Equal("A", CardCosmetics.Rank(1, 5, 0));
        Assert.Equal("7", CardCosmetics.Rank(7, 5, 0));
        var tens = Enumerable.Range(0, 200).Select(i => CardCosmetics.Rank(10, 77, i)).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "10", "J", "K", "Q" }, tens);
        Assert.Equal(CardCosmetics.Suit(123, 4), CardCosmetics.Suit(123, 4));
        Assert.Equal(4, Enumerable.Range(0, 200).Select(i => CardCosmetics.Suit(9, i)).Distinct().Count());
    }

    static PCard C(string s)
    {
        string r = s.Substring(0, s.Length - 1); int suit = "shdc".IndexOf(s[s.Length - 1]);
        int rank = r == "A" ? 14 : r == "K" ? 13 : r == "Q" ? 12 : r == "J" ? 11 : int.Parse(r);
        return new PCard(rank, suit);
    }
    static long S(string hand) { var c = hand.Split(' ').Select(C).ToArray(); return PokerHands.Score5(c[0], c[1], c[2], c[3], c[4]); }

    [Fact]
    public void EvaluatorKnowsTheCategories()
    {
        Assert.Equal(0, PokerHands.Category(S("2s 5h 9d Jc Kd")));
        Assert.Equal(1, PokerHands.Category(S("2s 2h 9d Jc Kd")));
        Assert.Equal(2, PokerHands.Category(S("2s 2h 9d 9c Kd")));
        Assert.Equal(3, PokerHands.Category(S("2s 2h 2d 9c Kd")));
        Assert.Equal(4, PokerHands.Category(S("5s 6h 7d 8c 9d")));
        Assert.Equal(4, PokerHands.Category(S("As 2h 3d 4c 5d")));
        Assert.Equal(5, PokerHands.Category(S("2s 7s 9s Js Ks")));
        Assert.Equal(6, PokerHands.Category(S("2s 2h 2d Kc Kd")));
        Assert.Equal(7, PokerHands.Category(S("2s 2h 2d 2c Kd")));
        Assert.Equal(8, PokerHands.Category(S("9h Th Jh Qh Kh".Replace("T", "10"))));
        Assert.True(S("As Ah 9d Jc Kd") > S("Ks Kh 9d Jc Qd"), "pair of aces beats pair of kings");
        Assert.True(S("As Ah 9d Jc Kd") > S("As Ah 9d Jc Qd"), "kicker decides");
        Assert.True(S("2s 3h 4d 5c 6d") > S("As 2h 3d 4c 5d"), "the wheel is the lowest straight");
        Assert.Equal(S("As Ah 9d Jc Kd"), S("Ad Ac 9s Jh Ks"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void CosmeticDealsMatchTheCategory(int category)
    {
        int exact = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            int winner = (int)(seed % 4);
            var d = PokerHands.Cosmetic(category, 4, winner, seed * 104729);
            // 13 different cards, all valid
            var all = d.Board.Concat(d.Hole.SelectMany(h => h)).ToList();
            Assert.Equal(13, all.Count); Assert.Equal(13, all.Select(c => c.Id).Distinct().Count());
            Assert.All(all, c => Assert.InRange(c.Rank, 2, 14));
            Assert.Equal(winner, d.Winner);
            if (!d.Exact) continue;
            exact++;
            Assert.Equal(category, d.Category);
            Assert.Equal(category, PokerHands.Category(PokerHands.Best(d.Hole[winner].Concat(d.Board).ToList())));
            long ws = PokerHands.Best(d.Hole[winner].Concat(d.Board).ToList());
            for (int s = 0; s < 4; s++) if (s != winner) Assert.True(PokerHands.Best(d.Hole[s].Concat(d.Board).ToList()) < ws, "seat " + s + " must lose");
            Assert.Equal(5, d.WinningFive.Length);
        }
        Assert.True(exact >= 57, "category " + category + ": only " + exact + " of 60 deals are exact");
    }

    [Fact]
    public void PayRowsMatchTheMultiplier()
    {
        var pt = SlotPaytable.For(SlotVolatility.Medium, 0.92);
        Assert.Equal("thrumbo", SlotPaytable.SymbolNames[6]);
        Assert.Equal("berries", SlotPaytable.SymbolNames[0]);
        foreach (var r in pt.PayRows())
        {
            int a = r.symbol, b = r.kind == SlotPaytable.RowKind.Three ? r.symbol : r.kind == SlotPaytable.RowKind.FirstTwo ? 0 : 1, c = r.kind == SlotPaytable.RowKind.Three ? r.symbol : 2;
            Assert.Equal(r.multiplier, pt.Multiplier(a, b, c), 9);
            Assert.Equal(r.kind, pt.PayingRow(a, b, c));
        }
        Assert.Null(pt.PayingRow(1, 2, 3));
        Assert.Equal(pt.PayRows().Count(), pt.Rows().Count());
    }
}
