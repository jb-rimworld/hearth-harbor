// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Hearth;
using Xunit;

public class MoneyTests
{
    [Fact] public void KeepsFiveDecimals() => Assert.Equal("1.23457", Money.FromSilver(1.234567).ToStringFull());
    [Fact] public void ShowsTwoDecimals() => Assert.Equal("1,234.50", Money.FromSilver(1234.5).ToStringShort());
    [Fact] public void Arithmetic() { var a = Money.FromSilver(10); Assert.Equal(Money.FromSilver(2.5), a * 0.25); Assert.Equal(Money.FromSilver(7.5), a - a * 0.25); }
    [Fact] public void RoundTrip() { var m = Money.FromSilver(-42.00001); Assert.Equal(m, Money.Parse(m.Serialize())); }
}

public class RandomTests
{
    [Theory]
    [InlineData(RngAlgorithm.SplitMix64)] [InlineData(RngAlgorithm.Pcg32)] [InlineData(RngAlgorithm.Xoshiro256)] [InlineData(RngAlgorithm.ChaCha8)]
    public void UniformAndReproducible(RngAlgorithm algo)
    {
        var a = new RngStream(12345, RngStream.HashString("test"), 0, algo);
        var xs = Enumerable.Range(0, 200_000).Select(_ => a.NextDouble()).ToArray();
        Assert.InRange(xs.Average(), 0.495, 0.505);
        Assert.All(xs, x => Assert.InRange(x, 0.0, 1.0));
        var b = new RngStream(12345, RngStream.HashString("test"), 100, algo);
        var c = new RngStream(12345, RngStream.HashString("test"), 0, algo);
        for (int i = 0; i < 100; i++) c.NextULong();
        Assert.Equal(b.NextULong(), c.NextULong());
    }
}

public class MoneyHelperTests
{
    [Fact] public void WholeAndFraction() { var m = Money.FromSilver(12.34567); Assert.Equal(12, m.WholeSilver); Assert.Equal(34567, m.Fraction.Micro); }
    [Fact] public void CeilIsIntegerExact() { Assert.Equal(2, Money.FromSilver(1.00001).CeilSilver); Assert.Equal(1, Money.FromSilver(1).CeilSilver); Assert.Equal(0, Money.Zero.CeilSilver); Assert.Equal(0, Money.FromSilver(-3).CeilSilver); }
    [Fact] public void ParseEmptyIsZero() => Assert.Equal(Money.Zero, Money.Parse(""));
    [Fact] public void MinMaxAbs() { var a = Money.FromSilver(-2); Assert.Equal(Money.FromSilver(2), a.Abs); Assert.Equal(a, Money.Min(a, Money.Zero)); }
}

public class RangeEdgeTests
{
    [Fact] public void FullIntSpanDoesNotOverflow()
    {
        var r = new RngStream(1, 2);
        for (int i = 0; i < 10000; i++) { int v = r.Range(int.MinValue, int.MaxValue); Assert.InRange(v, int.MinValue, int.MaxValue - 1); }
    }
    [Fact] public void NegativeToPositive() { var r = new RngStream(3, 4); for (int i = 0; i < 1000; i++) Assert.InRange(r.Range(-5, 5), -5, 4); }
    [Fact] public void EmptyRangeReturnsMin() { var r = new RngStream(3, 4); Assert.Equal(7, r.Range(7, 7)); Assert.Equal(7, r.Range(7, 3)); }
    [Theory] [InlineData(RngAlgorithm.ChaCha8)] [InlineData(RngAlgorithm.SplitMix64)]
    public void ReseedMatchesFreshStream(RngAlgorithm algo)
    {
        var a = new RngStream(9, 8, 5, algo); var b = new RngStream(1, 1); b.Reseed(9, 8, 5, algo);
        for (int i = 0; i < 50; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }
}
