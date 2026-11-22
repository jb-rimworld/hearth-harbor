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
