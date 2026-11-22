// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
using System;
using System.Globalization;

namespace Hearth
{
    /// <summary>
    /// Money in micro-silver: 1 silver = 100 000 micro. Computes with 5 decimals, shows 2 (stocks/finance views show all 5).
    /// Immutable value type, overflow-checked. All money in Hearth &amp; Harbor and Silverline uses this type.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        public const long PerSilver = 100_000L;
        public readonly long Micro;

        public Money(long micro) { Micro = micro; }
        public static readonly Money Zero = new Money(0);

        public static Money FromSilver(double silver) => new Money(checked((long)Math.Round(silver * PerSilver, MidpointRounding.AwayFromZero)));
        public static Money FromSilver(int silver) => new Money(checked(silver * PerSilver));
        public double Silver => Micro / (double)PerSilver;
        /// <summary>Whole silver coins (rounded down towards zero). Used when physical silver changes hands.</summary>
        public int WholeSilver => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Micro / PerSilver));
        /// <summary>Remainder below one coin (always between 0 and PerSilver-1 for positive values).</summary>
        public Money Fraction => new Money(Micro % PerSilver);

        public static Money operator +(Money a, Money b) => new Money(checked(a.Micro + b.Micro));
        public static Money operator -(Money a, Money b) => new Money(checked(a.Micro - b.Micro));
        public static Money operator -(Money a) => new Money(checked(-a.Micro));
        public static Money operator *(Money a, double f) => new Money(checked((long)Math.Round(a.Micro * f, MidpointRounding.AwayFromZero)));
        public static Money operator *(double f, Money a) => a * f;
        public static Money operator /(Money a, double f) => f == 0 ? Zero : new Money(checked((long)Math.Round(a.Micro / f, MidpointRounding.AwayFromZero)));
        public static bool operator <(Money a, Money b) => a.Micro < b.Micro;
        public static bool operator >(Money a, Money b) => a.Micro > b.Micro;
        public static bool operator <=(Money a, Money b) => a.Micro <= b.Micro;
        public static bool operator >=(Money a, Money b) => a.Micro >= b.Micro;
        public static bool operator ==(Money a, Money b) => a.Micro == b.Micro;
        public static bool operator !=(Money a, Money b) => a.Micro != b.Micro;
        public static Money Max(Money a, Money b) => a.Micro >= b.Micro ? a : b;
        public static Money Min(Money a, Money b) => a.Micro <= b.Micro ? a : b;
        public bool IsPositive => Micro > 0;
        public bool IsNegative => Micro < 0;
        public Money Abs => Micro < 0 ? -this : this;

        public bool Equals(Money o) => Micro == o.Micro;
        public override bool Equals(object obj) => obj is Money m && m.Micro == Micro;
        public override int GetHashCode() => Micro.GetHashCode();
        public int CompareTo(Money o) => Micro.CompareTo(o.Micro);

        /// <summary>"1,234.50" style with 2 decimals, invariant digits, silver sign added by UI.</summary>
        public string ToStringShort() => Silver.ToString("#,##0.00", CultureInfo.InvariantCulture);
        /// <summary>All 5 decimals, for finance screens.</summary>
        public string ToStringFull() => Silver.ToString("#,##0.00000", CultureInfo.InvariantCulture);
        public override string ToString() => ToStringShort();
        /// <summary>Stable serialisation (invariant, exact).</summary>
        public string Serialize() => Micro.ToString(CultureInfo.InvariantCulture);
        public static Money Parse(string s) => string.IsNullOrEmpty(s) ? Zero : new Money(long.Parse(s, CultureInfo.InvariantCulture));
    }
}
