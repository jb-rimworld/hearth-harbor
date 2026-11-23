// SPDX-License-Identifier: MIT
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>Who comes and what they carry (5.3): wealth level W, daily budget b(W), payment method, expectation E.</summary>
    public static class GuestEconomy
    {
        // b(W): W 1 → 25, 10 → 55, 25 → 110, 40 → 170, 50 → 230, 75 → 550, 90 → 1100, 100 → 2500 (piecewise linear)
        static readonly float[] bW = { 1, 10, 25, 40, 50, 75, 90, 100 };
        static readonly float[] bV = { 25, 55, 110, 170, 230, 550, 1100, 2500 };

        public static float DailyBudget(float w)
        {
            w = Mathf.Clamp(w, 1f, 100f);
            for (int i = 0; i < bW.Length - 1; i++)
                if (w <= bW[i + 1]) return Mathf.Lerp(bV[i], bV[i + 1], (w - bW[i]) / (bW[i + 1] - bW[i]));
            return bV[bV.Length - 1];
        }

        // band shares (poor 1–10 / middle 11–30 / rich 31–70 / elite 71–100) at renown 0, 50, 100
        static readonly float[][] bands = { new[] { 80f, 18f, 2f, 0f }, new[] { 30f, 45f, 21f, 4f }, new[] { 10f, 30f, 40f, 20f } };
        static readonly int[] bandLo = { 1, 11, 31, 71 }, bandHi = { 10, 30, 70, 100 };

        public static float TechBonus(TechLevel t)
        {
            switch (t)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic: return -8f;
                case TechLevel.Medieval: return -4f;
                case TechLevel.Industrial: return 0f;
                case TechLevel.Spacer: return 8f;
                case TechLevel.Ultra:
                case TechLevel.Archotech: return 15f;
                default: return 0f;
            }
        }

        public static int RollWealth(Pawn p, Faction f, float renown, RngStream rng)
        {
            float r = Mathf.Clamp(renown, 0f, 100f);
            float[] lo = r <= 50 ? bands[0] : bands[1], hi = r <= 50 ? bands[1] : bands[2];
            float t = r <= 50 ? r / 50f : (r - 50f) / 50f;
            float roll = (float)rng.NextDouble() * 100f, acc = 0f;
            int band = 0;
            for (int i = 0; i < 4; i++) { acc += Mathf.Lerp(lo[i], hi[i], t); if (roll <= acc) { band = i; break; } band = i; }
            float w = bandLo[band] + (float)rng.NextDouble() * (bandHi[band] - bandLo[band]);
            if (f != null) w += TechBonus(f.def.techLevel);
            if (p?.royalty != null && p.royalty.AllTitlesForReading.Count > 0) w += 5f;
            return Mathf.Clamp(Mathf.RoundToInt(w), 1, 100);
        }

        public static float TraitFactor(Pawn p)
        {
            var traits = p?.story?.traits;
            if (traits == null) return 1f;
            float f = 1f;
            var greedy = DefDatabase<TraitDef>.GetNamedSilentFail("Greedy");
            var ascetic = DefDatabase<TraitDef>.GetNamedSilentFail("Ascetic");
            if (greedy != null && traits.HasTrait(greedy)) f *= 1.3f;
            if (ascetic != null && traits.HasTrait(ascetic)) f *= 0.5f;
            return f;
        }

        /// <summary>Guests who can afford a drop pod bring a little more (B9: 1.15, was 1.6).</summary>
        public const float PodBudgetFactor = 1.15f;

        public const string InfiniteMoneySwitch = "core.infiniteMoney", PickySwitch = "core.picky";
        public const int InfiniteBudgetSilver = 1_000_000;
        /// <summary>Extra expectation of the special switch core.picky.</summary>
        public const float PickyExpectation = 0.1f;

        /// <summary>Budget for the whole stay: b(W) · days^0.85 · random(0.8–1.2) · guest-money setting · origin · trait.</summary>
        public static Money RollBudget(Pawn p, int wealth, float days, bool byPod, RngStream rng)
        {
            if (CoreSettings.Switch(InfiniteMoneySwitch)) return Money.FromSilver(InfiniteBudgetSilver);   // special switch: a bottomless purse
            float b = DailyBudget(wealth) * Mathf.Pow(Mathf.Max(0.25f, days), 0.85f);
            b *= 0.8f + 0.4f * (float)rng.NextDouble();
            b *= HSettings.Get(InnSettings.GuestMoney);
            b *= byPod ? PodBudgetFactor : 1f;
            b *= TraitFactor(p);
            b = Mathf.Clamp(b, 5f, 1_000_000f);
            return Money.FromSilver((int)Math.Round(b));
        }

        /// <summary>5.3 / N42: industrial or better factions pay by card when the budget is large; everyone else carries silver.</summary>
        public static bool PaysDigital(Faction f, Money budget)
            => f != null && f.def.techLevel >= TechLevel.Industrial && budget > HSettings.GetSilver(InnSettings.CardThreshold);

        /// <summary>E = 0.30 + 0.25·W/100 + 0.15·R/100 + offset − 0.10·(1 − goodwill01)  (5.6)</summary>
        public static float Expectation(int wealth, float renown, Faction f)
        {
            float gw01 = f == null || f.IsPlayer ? 0.5f : Mathf.Clamp01((f.PlayerGoodwill + 100f) / 200f);
            return Mathf.Clamp01(0.30f + 0.25f * wealth / 100f + 0.15f * renown / 100f + HSettings.Get(InnSettings.ExpectationOffset) + (CoreSettings.Switch(PickySwitch) ? PickyExpectation : 0f) - 0.10f * (1f - gw01));
        }

        /// <summary>Room score the guest hopes for (used for the sleep verdict).</summary>
        public static float ExpectedRoomScore(int wealth) => 25f + 0.6f * wealth;

        /// <summary>Bed choice utility (5.11): U = RS·(0.5 + W/100) − 100·price/(0.35·b(W)·tolerance)·(1.5 − W/100) + 10·[near group].</summary>
        public static float BedUtility(float roomScore, Money price, int wealth, bool nearGroup)
        {
            float tol = Mathf.Max(0.05f, HSettings.Get(InnSettings.PriceTolerance));
            float b = DailyBudget(wealth);
            return roomScore * (0.5f + wealth / 100f) - 100f * (float)price.Silver / (0.35f * b * tol) * (1.5f - wealth / 100f) + (nearGroup ? 10f : 0f);
        }

        /// <summary>Most a guest pays per night without grumbling: 60 % of the daily budget × price tolerance.</summary>
        public static Money ComfortPrice(int wealth) => Money.FromSilver(0.6 * DailyBudget(wealth) * Math.Max(0.05f, HSettings.Get(InnSettings.PriceTolerance)));
    }
}
