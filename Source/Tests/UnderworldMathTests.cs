// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Xunit;

public class UnderworldGateTests
{
    [Fact] public void CozyHidesEverything() { foreach (UwFeature f in Enum.GetValues(typeof(UwFeature))) Assert.False(UnderworldMath.Allows(0, f), f.ToString()); }
    [Fact] public void StandardHasNoHumanHuntOrOrgans()
    {
        Assert.True(UnderworldMath.Allows(1, UwFeature.Facilities));
        Assert.True(UnderworldMath.Allows(1, UwFeature.HuntAnimals));
        Assert.True(UnderworldMath.Allows(1, UwFeature.PitToIncapacitation));
        Assert.False(UnderworldMath.Allows(1, UwFeature.PitToDeath));
        Assert.False(UnderworldMath.Allows(1, UwFeature.HuntPrisoners));
        Assert.False(UnderworldMath.Allows(1, UwFeature.OrganClinic));
        Assert.False(UnderworldMath.Allows(1, UwFeature.PrisonHardQuestioning));
    }
    [Fact] public void DarkAddsHuntAndOrgans()
    {
        Assert.True(UnderworldMath.Allows(2, UwFeature.HuntPrisoners));
        Assert.True(UnderworldMath.Allows(2, UwFeature.OrganClinic));
        Assert.True(UnderworldMath.Allows(2, UwFeature.PitToDeath));
        Assert.False(UnderworldMath.Allows(2, UwFeature.HuntPrisonersNoPrecept));
        Assert.False(UnderworldMath.Allows(2, UwFeature.OrganOrders));
    }
    [Fact] public void GrimIsEverything() { foreach (UwFeature f in Enum.GetValues(typeof(UwFeature))) Assert.True(UnderworldMath.Allows(3, f), f.ToString()); }
    [Fact] public void GatesGrowMonotonically()
    {
        foreach (UwFeature f in Enum.GetValues(typeof(UwFeature)))
            for (int i = 0; i < 3; i++) Assert.True(!UnderworldMath.Allows(i, f) || UnderworldMath.Allows(i + 1, f), f + " at " + i);
    }
}

public class WhispersTests
{
    [Fact] public void TwoPercentPerFifteenDays() => Assert.Equal(49f, UnderworldMath.WhispersDecay(50f, 15f, 0.02f), 3);
    [Fact] public void NoDecayWithoutTime() => Assert.Equal(40f, UnderworldMath.WhispersDecay(40f, 0f, 0.02f), 5);
    [Fact] public void DecayComposes() => Assert.Equal(UnderworldMath.WhispersDecay(60f, 30f, 0.02f), UnderworldMath.WhispersDecay(UnderworldMath.WhispersDecay(60f, 10f, 0.02f), 20f, 0.02f), 3);
    [Fact] public void NeverNegativeNeverAbove100() { Assert.Equal(0f, UnderworldMath.WhispersDecay(-5, 3, 0.02f)); Assert.Equal(100f, UnderworldMath.WhispersClamp(250f)); Assert.Equal(0f, UnderworldMath.WhispersClamp(float.NaN)); }
    [Fact] public void ArrivalWeightsFollowTheDesign()
    {
        Assert.Equal(1f, UnderworldMath.RespectableArrivalFactor(0f), 4);
        Assert.Equal((float)Math.Exp(-1.0), UnderworldMath.RespectableArrivalFactor(40f), 4);
        Assert.Equal(0.2f, UnderworldMath.OutlawArrivalFactor(0f), 4);
        Assert.Equal(1.2f, UnderworldMath.OutlawArrivalFactor(50f), 4);
    }
}

public class CapTests
{
    [Fact] public void StandardRoomCaps() { Assert.Equal(3f, UnderworldMath.CapAt(UnderworldMath.RenownCapPerEvent, 3f)); Assert.Equal(5f, UnderworldMath.CapAt(UnderworldMath.GoodwillCapPerEvent, 3f)); Assert.Equal(300f, UnderworldMath.CapAt(UnderworldMath.FineCap, 3f)); }
    [Fact] public void NegativeIsCappedPositiveIsNot()
    {
        Assert.Equal(-3f, UnderworldMath.CapNegative(-5f, UnderworldMath.RenownCapPerEvent, 3f));
        Assert.Equal(6f, UnderworldMath.CapNegative(6f, UnderworldMath.RenownCapPerEvent, 3f));
        Assert.Equal(0f, UnderworldMath.CapNegative(-5f, UnderworldMath.RenownCapPerEvent, 0f));
        Assert.Equal(-50f, UnderworldMath.CapNegative(-50f, UnderworldMath.GoodwillCapPerEvent, 6f));
    }
    [Fact] public void CapsAreMonotonic() { foreach (var a in new[] { UnderworldMath.RenownCapPerEvent, UnderworldMath.GoodwillCapPerEvent, UnderworldMath.FineCap }) { float last = -1; for (float t = 0; t <= 6f; t += 0.25f) { float c = UnderworldMath.CapAt(a, t); Assert.True(c >= last, t.ToString()); last = c; } } }
}

public class TotalisatorTests
{
    static List<UnderworldMath.Bet> Bets(params (int bettor, int side, int silver)[] xs) => xs.Select(x => new UnderworldMath.Bet { Bettor = x.bettor, Side = x.side, StakeMicro = x.silver * UnderworldMath.Coin }).ToList();

    [Fact] public void HouseTakesTenPercentAndPoolBalances()
    {
        var bets = Bets((1, 0, 50), (2, 0, 50), (3, 1, 100));
        var s = UnderworldMath.Settle(bets, 0, 0.10);
        Assert.Equal(200 * UnderworldMath.Coin, s.PoolMicro);
        Assert.Equal(20 * UnderworldMath.Coin, s.CutMicro);
        Assert.Equal(s.PoolMicro, s.PaidMicro + s.HouseMicro);
        Assert.Equal(90 * UnderworldMath.Coin, bets[0].PayoutMicro);
        Assert.Equal(90 * UnderworldMath.Coin, bets[1].PayoutMicro);
        Assert.Equal(0, bets[2].PayoutMicro);
        Assert.Equal(20 * UnderworldMath.Coin, s.HouseMicro);
    }
    [Fact] public void RoundingStaysWithTheHouseButNeverMoreThanACoinPerWinner()
    {
        var bets = Bets((1, 0, 7), (2, 0, 13), (3, 1, 29));
        var s = UnderworldMath.Settle(bets, 0, 0.10);
        Assert.Equal(s.PoolMicro, s.PaidMicro + s.HouseMicro);
        Assert.True(s.HouseMicro >= s.CutMicro);
        Assert.True(s.HouseMicro - s.CutMicro < 2 * UnderworldMath.Coin);
        Assert.All(bets, b => Assert.Equal(0, b.PayoutMicro % UnderworldMath.Coin));
    }
    [Fact] public void NobodyBackedTheWinnerRefundsMinusTheCut()
    {
        var bets = Bets((1, 1, 100), (2, 1, 100));
        var s = UnderworldMath.Settle(bets, 0, 0.10);
        Assert.True(s.Refunded);
        Assert.Equal(90 * UnderworldMath.Coin, bets[0].PayoutMicro);
        Assert.Equal(s.PoolMicro, s.PaidMicro + s.HouseMicro);
    }
    [Fact] public void DrawRefundsEverythingWithoutCut()
    {
        var bets = Bets((1, 0, 40), (2, 1, 60));
        var s = UnderworldMath.Settle(bets, -1, 0.10);
        Assert.True(s.Draw);
        Assert.Equal(0, s.CutMicro);
        Assert.Equal(40 * UnderworldMath.Coin, bets[0].PayoutMicro);
        Assert.Equal(60 * UnderworldMath.Coin, bets[1].PayoutMicro);
        Assert.Equal(0, s.HouseMicro);
    }
    [Fact] public void EmptyPoolIsHarmless() { var s = UnderworldMath.Settle(new List<UnderworldMath.Bet>(), 0, 0.10); Assert.Equal(0, s.PoolMicro); Assert.Equal(0, s.HouseMicro); }
    [Theory] [InlineData(1, 1.0f, true)] [InlineData(40, 90, false)] [InlineData(40, 60, true)]
    public void FairPairs(float a, float b, bool fair) => Assert.Equal(fair, UnderworldMath.FairPair(a, b, 2.2f));
    [Fact] public void FavouriteShareIsBounded() { Assert.Equal(0.5f, UnderworldMath.FavouriteShare(10, 10), 3); Assert.Equal(0.9f, UnderworldMath.FavouriteShare(1000, 1), 3); Assert.Equal(0.1f, UnderworldMath.FavouriteShare(1, 1000), 3); }
}

public class FenceTests
{
    [Fact] public void BuyAtFortySellAtSeventy()
    {
        long mv = Money.FromSilver(100).Micro;
        Assert.Equal(Money.FromSilver(40).Micro, UnderworldMath.FenceBuyPrice(mv, 0.40f));
        Assert.Equal(Money.FromSilver(70).Micro, UnderworldMath.FenceSellPrice(mv, 0.70f));
        Assert.True(UnderworldMath.FenceSellPrice(mv, 0.70f) > UnderworldMath.FenceBuyPrice(mv, 0.40f));
    }
}
