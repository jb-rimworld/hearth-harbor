// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>Numbers of 6.4: what a dish does to the eater and what a restaurant asks for it.</summary>
    public static class DishEffects
    {
        static readonly float[] BaseWellFedHours = { 0f, 4f, 6f, 8f, 10f };                 // by learning level
        static readonly float[] QualityHoursFactor = { 0.6f, 0.8f, 1.0f, 1.2f, 1.4f, 1.6f, 1.8f };
        static readonly int[] BaseMood = { 0, 4, 6, 9, 12 };
        static readonly int[] QualityMood = { -2, -1, 0, 1, 2, 3, 4 };
        static readonly float[] BasePriceByLevel = { 0f, 16f, 28f, 44f, 64f };
        static readonly float[] QualityPriceFactor = { 0.5f, 0.75f, 1.0f, 1.2f, 1.5f, 2.0f, 2.6f };
        static readonly float[] VariantPriceFactor = { 1f, 1.4f, 2.0f };

        public static float WellFedHours(DishDef d, QualityCategory q)
            => BaseWellFedHours[Mathf.Clamp(d.level, 1, 4)] * QualityHoursFactor[(int)q] * Mathf.Max(0f, HSettings.Get("kitchen.wellFedDuration"));

        public static int MoodOffset(DishDef d, QualityCategory q)
            => Mathf.Min(16, BaseMood[Mathf.Clamp(d.level, 1, 4)] + QualityMood[(int)q]);

        public static int ThoughtStage(DishDef d, QualityCategory q) => (Mathf.Clamp(d.level, 1, 4) - 1) * 7 + (int)q;

        /// <summary>Hunger falls this much slower while Well fed (0.4 = 40 %), settings scaled.</summary>
        public static float HungerReduction => Mathf.Clamp(0.4f * HSettings.Get("kitchen.wellFedStrength"), 0f, 0.9f);

        /// <summary>Menu price before the venue's own factor: level, steps, form, quality and the global factor.</summary>
        public static Money MenuPrice(DishDef d, QualityCategory q, int variant, float venueFactor = 1f)
        {
            float basePrice = BasePriceByLevel[Mathf.Clamp(d.level, 1, 4)] + 3f * (d.steps.Count - 1);
            float v = basePrice * QualityPriceFactor[(int)q] * VariantPriceFactor[Mathf.Clamp(variant, 0, 2)] * d.priceFactor * HSettings.Get("kitchen.menuPrice") * venueFactor;
            return Money.FromSilver(Math.Max(1.0, Math.Round(v, 1)));
        }

        public static Money MenuPrice(CompDish c, float venueFactor = 1f) => MenuPrice(c.dish, c.Quality, c.variant, venueFactor);

        /// <summary>Called once per eaten plate (CompDish.PostIngested).</summary>
        public static void OnEaten(CompDish c, Pawn eater)
        {
            if (c.dish == null || eater == null) return;
            var q = c.Quality;
            if (!c.IsStale)
            {
                GiveWellFed(eater, WellFedHours(c.dish, q));
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("HH_AteDish");
                var memories = eater.needs?.mood?.thoughts?.memories;
                if (def != null && memories != null)
                {
                    int stage = Mathf.Clamp(ThoughtStage(c.dish, q), 0, def.stages.Count - 1);
                    memories.TryGainMemory(ThoughtMaker.MakeThought(def, stage));
                }
            }
            Restaurant.OnPlateEaten(c, eater);
        }

        public static void GiveWellFed(Pawn p, float hours)
        {
            if (p?.health?.hediffSet == null || hours <= 0f) return;
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("HH_WellFed");
            if (def == null) return;
            int ticks = Mathf.RoundToInt(hours * CompDish.TicksPerHour);
            var h = p.health.hediffSet.GetFirstHediffOfDef(def);
            bool fresh = h == null;
            if (fresh) { h = HediffMaker.MakeHediff(def, p); p.health.AddHediff(h); }
            var d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null && (fresh || ticks > d.ticksToDisappear)) { d.disappearsAfterTicks = ticks; d.ticksToDisappear = ticks; }
        }

        public static bool IsWellFed(Pawn p)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("HH_WellFed");
            return def != null && p?.health?.hediffSet?.HasHediff(def) == true;
        }

        /// <summary>The vanilla roll of the quality (made by GenRecipe) is followed by 1 (Fine) or 2 (Signature) extra rolls with the cook's skill +3 / +5; the best counts (6.4).</summary>
        /// <summary>Refined palate (HH_DishQuality 0.1): that chance to lift the finished dish by one quality step (never above legendary).</summary>
        public static bool PalateLift(Thing plate, Pawn cook)
        {
            float chance = Hearth.Dlc.DlcApi.DishQualityLift(cook);
            if (chance <= 0f) return false;
            var qc = plate?.TryGetComp<CompQuality>();
            if (qc == null || qc.Quality >= QualityCategory.Legendary || !JRand.Stream("kitchen.palate").Chance(chance)) return false;
            qc.SetQuality(qc.Quality + 1, ArtGenerationContext.Colony);
            return true;
        }

        public static void RerollQuality(Thing plate, Pawn cook, int variant)
        {
            if (variant <= 0 || cook?.skills == null) return;
            var qc = plate.TryGetComp<CompQuality>();
            if (qc == null) return;
            int skill = cook.skills.GetSkill(SkillDefOf.Cooking).Level;
            int rolls = variant == 1 ? 1 : 2, bonus = variant == 1 ? 3 : 5;
            var best = qc.Quality;
            for (int i = 0; i < rolls; i++)
            {
                var q = QualityUtility.GenerateQualityCreatedByPawn(Mathf.Min(20, skill + bonus), false);
                if (q > best) best = q;
            }
            if (best > qc.Quality) qc.SetQuality(best, ArtGenerationContext.Colony);
        }
    }
}
