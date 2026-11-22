// SPDX-License-Identifier: MIT
using System;
using Hearth;
using Xunit;

public class DlcMathTests
{
    [Fact] public void PriceRepeatsGrow() { Assert.Equal(150, DlcMath.IndulgencePrice(100, 1, MoneyStance.Accepted, 0) * 1.5); Assert.Equal(225, DlcMath.IndulgencePrice(100, 1, MoneyStance.Accepted, 2)); }
    [Fact] public void SacredIsCheaper() => Assert.Equal(75, DlcMath.IndulgencePrice(100, 1, MoneyStance.Sacred, 0));
    [Fact] public void PriceNeverBelowOne() => Assert.Equal(1, DlcMath.IndulgencePrice(0.2, 0.1, MoneyStance.Sacred, 0));
    [Fact] public void CountWindow() => Assert.Equal(2, DlcMath.CountInWindow(new[] { 100, 85000, 90000 }, 100000, 20000));
    [Fact] public void GoodwillCapped() { Assert.Equal(2, DlcMath.FactionGoodwill(1200, 0)); Assert.Equal(1, DlcMath.FactionGoodwill(5000, 4)); Assert.Equal(0, DlcMath.FactionGoodwill(5000, 5)); }
    [Fact] public void LuckNeverAboveCap() { Assert.True(DlcMath.LuckRtp(0.99, 0.015) <= 0.995 + 1e-9); Assert.Equal(0.9, DlcMath.LuckRtp(0.92, -0.02), 6); Assert.Equal(0.9999 > 0.999 ? 0.999 : 0, DlcMath.LuckRtp(1.2, 0.0), 6); }
    [Fact] public void LuckShiftNeverRaisesAboveBaseWhenBaseAlreadyHigh() => Assert.Equal(0.997, DlcMath.LuckRtp(0.997, 0.015), 6);
    [Fact] public void FreeLotto() { Assert.True(DlcMath.FreeLottoTicket(10)); Assert.False(DlcMath.FreeLottoTicket(9)); Assert.False(DlcMath.FreeLottoTicket(0)); }
    [Fact] public void ShrineBounds() { Assert.Equal(2, DlcMath.PrayerMood(0)); Assert.Equal(6, DlcMath.PrayerMood(1)); Assert.InRange(DlcMath.ShrineQuality(500, 99, 5), 0.999, 1.001); }
    [Fact] public void DonationRange() { Assert.InRange(DlcMath.ShrineDonation(1000, 1, 0), 9.99, 10.01); Assert.InRange(DlcMath.ShrineDonation(1000, 1, 1), 29.99, 30.01); Assert.Equal(0, DlcMath.ShrineDonation(1000, 0, 0.5)); }
    [Fact] public void GuestShare() { Assert.Equal(0, DlcMath.GuestIndulgenceShare(MoneyStance.Disapproved, 0.5)); Assert.InRange(DlcMath.GuestIndulgenceShare(MoneyStance.Sacred, 1), 0.119, 0.121); }
    [Fact] public void NobleScalesWithTitle() { Assert.True(DlcMath.NoblePayment(100, 1, 3, 600, 0) > DlcMath.NoblePayment(100, 1, 3, 300, 0)); Assert.Equal(100 * 3 * 1.45, DlcMath.NoblePayment(100, 1, 3, 300, 0), 6); }
    [Fact] public void NobleSatisfaction() { Assert.Equal(1.25, DlcMath.SatisfactionFactor(1), 6); Assert.Equal(0.5, DlcMath.SatisfactionFactor(-1), 6); }
    [Fact] public void Honor() { Assert.Equal(1, DlcMath.NobleHonor(300)); Assert.Equal(3, DlcMath.NobleHonor(600)); Assert.Equal(4, DlcMath.NobleHonor(1200)); }
    [Fact] public void Billeting() { Assert.Equal(0.25, DlcMath.BilletingCompensation(4), 6); Assert.Equal(0.10, DlcMath.BilletingCompensation(5), 6); Assert.Equal(0, DlcMath.BilletingCompensation(6), 6); Assert.Equal(0, DlcMath.BilletingPenalty(0.3)); Assert.Equal(-15, DlcMath.BilletingPenalty(-1)); }
    [Fact] public void AiWeights() { var w = DlcMath.AiStanceWeights("indulgences", new[] { "Guilty", "HighLife" }); Assert.Equal(10 * 6, w[(int)MoneyStance.Accepted], 6); Assert.Equal(60, w[0]); var a = DlcMath.AiStanceWeights("ascension", new[] { "Transhumanist", "PainIsVirtue" }); Assert.Equal(24, a[(int)MoneyStance.Abhorrent], 6); }
    [Fact] public void PickStanceCoversAll() { var w = DlcMath.AiStanceWeights("indulgences", null); Assert.Equal(MoneyStance.NotAThing, DlcMath.PickStance(w, 0)); Assert.Equal(MoneyStance.Sacred, DlcMath.PickStance(w, 0.9999)); }
}
