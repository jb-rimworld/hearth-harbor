// SPDX-License-Identifier: MIT
using System;
using Verse;

namespace Hearth.City
{
    /// <summary>
    /// Price perception for city services (KONZEPT 6.1): tolerance T(W) = 0.85 + 1.65 (W/100)^1.3, buy chance
    /// p = sigma((T q (1+eps) - m) / 0.12), price thought after rho = m / (T q).
    /// m is the price relative to the reference price of the service; q the quality perception (0.7 to 1.5).
    /// </summary>
    public static class CityPricing
    {
        public static double Tolerance(int wealth)
        {
            double w = Math.Max(0, Math.Min(100, wealth)) / 100.0;
            return 0.85 + 1.65 * Math.Pow(w, 1.3);
        }

        /// <summary>Tolerance including the difficulty-dependent setting.</summary>
        public static double EffectiveTolerance(GuestInfo gi) => Tolerance(gi?.Wealth ?? 30) * HSettings.Get(CitySettings.PriceTolerance);

        /// <summary>Fixed noise of +-10 % per (guest, service), so one guest always judges one price the same way.</summary>
        public static double Noise(Pawn p, string key)
        {
            int h = GenText.StableStringHash(key) ^ (p?.thingIDNumber ?? 0) * 7919;
            h = (h ^ (h >> 13)) * 1274126177;
            double u = ((h & 0x7fffffff) % 20001) / 20000.0;
            return (u - 0.5) * 0.2;
        }

        public static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

        public static double BuyChance(Pawn guest, GuestInfo gi, Money price, Money reference, string key, double q = 1.0)
        {
            if (price.Micro <= 0) return 1.0;
            double m = reference.Micro <= 0 ? 1.0 : price.Micro / (double)reference.Micro;
            double t = EffectiveTolerance(gi);
            return Sigmoid((t * q * (1.0 + Noise(guest, key)) - m) / 0.12);
        }

        public static double Rho(GuestInfo gi, Money price, Money reference, double q = 1.0)
        {
            if (price.Micro <= 0) return 0;
            double m = reference.Micro <= 0 ? 1.0 : price.Micro / (double)reference.Micro;
            return m / Math.Max(0.05, EffectiveTolerance(gi) * q);
        }

        /// <summary>Price thought in mood points after rho (KONZEPT 6.1); 0 means nothing notable. 1 point is 0.02 satisfaction.</summary>
        public static int PricePoints(double rho)
        {
            if (rho < 0.6) return 3;
            if (rho < 0.9) return 1;
            if (rho <= 1.1) return 0;
            if (rho <= 1.3) return -2;
            return -5;
        }

        public static string PriceLabel(double rho)
        {
            if (rho < 0.6) return "Bargain!";
            if (rho < 0.9) return "Good value";
            if (rho <= 1.1) return null;
            if (rho <= 1.3) return "Pricey";
            return "Ripped off";
        }

        public const float PointsToSatisfaction = 0.02f;

        /// <summary>Adds the price thought (if any) to the guest's satisfaction.</summary>
        public static void ApplyPriceThought(Pawn guest, GuestInfo gi, Money price, Money reference, string service, double q = 1.0)
        {
            double rho = Rho(gi, price, reference, q);
            int pts = PricePoints(rho);
            string label = PriceLabel(rho);
            if (pts != 0 && label != null) GuestApi.AddSatisfaction(guest, pts * PointsToSatisfaction, service + ": " + label);
        }
    }
}
