// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    public class CompProperties_Dish : CompProperties
    {
        public CompProperties_Dish() { compClass = typeof(CompDish); }
    }

    /// <summary>
    /// The dish on a plate (KONZEPT 6.4). Holds the DishDef and the form it was cooked in, renames the plate ("Muffalo bourguignon (excellent)"),
    /// runs the freshness clock (3–24 h per dish, independent of the fridge, drives vanilla rot), and on eating gives full nutrition
    /// plus the Well fed state and a mood thought by learning level and quality.
    /// Stale plates (past their shelf life) are still edible but give neither Well fed nor the thought.
    /// </summary>
    public class CompDish : ThingComp
    {
        public const int TicksPerHour = 2500;
        /// <summary>A plate goes bad (vanilla rot) after this many shelf lives; stale in between.</summary>
        public const float RotAfterShelfLives = 4f;

        public DishDef dish;
        public int variant;                 // 0 Everyday, 1 Fine, 2 Signature
        public float freshAge;              // aged ticks (not real ticks: cooled salad ages slower)
        int lastTick = -1;

        public CompProperties_Dish Props => (CompProperties_Dish)props;

        public bool HasDish => dish != null;
        public float ShelfTicks => Mathf.Max(1f, (dish?.shelfLifeHours ?? 8f) * TicksPerHour * Mathf.Max(0.05f, HSettings.Get("kitchen.shelfLife")));
        public float FreshFraction => Mathf.Clamp01(1f - freshAge / ShelfTicks);
        public bool IsStale => dish != null && freshAge >= ShelfTicks;
        public int FreshTicksLeft => Mathf.Max(0, Mathf.RoundToInt(ShelfTicks - freshAge));

        public QualityCategory Quality => parent.TryGetQuality(out var q) ? q : QualityCategory.Normal;

        public void SetDish(DishDef d, int variantIndex)
        {
            dish = d; variant = variantIndex; freshAge = 0f; lastTick = Find.TickManager?.TicksGame ?? -1;
            Sync();
        }

        public override void PostExposeData()
        {
            Scribe_Defs.Look(ref dish, "dish");
            Scribe_Values.Look(ref variant, "variant");
            Scribe_Values.Look(ref freshAge, "freshAge");
            Scribe_Values.Look(ref lastTick, "lastTick", -1);
        }

        // ---- naming and texts ---------------------------------------------------------------------------------------------
        public override string TransformLabel(string label)
        {
            if (dish == null) return label;
            string defLabel = parent.def.label;
            string name = dish.label;
            if (label.StartsWith(defLabel, StringComparison.OrdinalIgnoreCase)) return name + label.Substring(defLabel.Length);
            return name;
        }

        public override string GetDescriptionPart()
        {
            if (dish == null) return null;
            var sb = new StringBuilder();
            sb.AppendLine(dish.description);
            sb.AppendLine();
            sb.AppendLine(("HH.Kitchen.Inspiration".TranslateOr("Inspiration: {0}")).Replace("{0}", dish.inspiration));
            sb.AppendLine(("HH.Kitchen.Cuisine".TranslateOr("Cuisine: {0}")).Replace("{0}", dish.CuisineLabel));
            sb.AppendLine(("HH.Kitchen.Cooked".TranslateOr("Cooked at: {0} ({1})")).Replace("{0}", dish.StationSummary()).Replace("{1}", dish.StepSummary()));
            sb.Append(("HH.Kitchen.Made".TranslateOr("Made from: {0}")).Replace("{0}", dish.IngredientSummary()));
            return sb.ToString();
        }

        public override string CompInspectStringExtra()
        {
            if (dish == null) return null;
            var sb = new StringBuilder();
            sb.Append(variant == 2 ? "HH.Kitchen.Signature".TranslateOr("Signature dish") : variant == 1 ? "HH.Kitchen.Fine".TranslateOr("Fine dish") : "HH.Kitchen.Everyday".TranslateOr("Everyday dish"));
            sb.Append(" · " + ("HH.Kitchen.Level".TranslateOr("level {0}")).Replace("{0}", dish.level.ToString()));
            sb.Append("\n");
            if (IsStale) sb.Append("HH.Kitchen.Stale".TranslateOr("Stale: no Well fed, no mood bonus."));
            else sb.Append(("HH.Kitchen.FreshFor".TranslateOr("Fresh for {0}")).Replace("{0}", FreshTicksLeft.ToStringTicksToPeriod()));
            return sb.ToString();
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            if (dish == null) yield break;
            yield return new StatDrawEntry(StatCategoryDefOf.Basics, "HH.Kitchen.Stat.Well".TranslateOr("Well fed"), DishEffects.WellFedHours(dish, Quality).ToString("0.#") + " h",
                "HH.Kitchen.Stat.WellDesc".TranslateOr("How long hunger falls slower after eating this dish. Scales with learning level and quality."), 4010);
            yield return new StatDrawEntry(StatCategoryDefOf.Basics, "HH.Kitchen.Stat.Mood".TranslateOr("Mood when eaten"), DishEffects.MoodOffset(dish, Quality).ToString("+0;-0"),
                "HH.Kitchen.Stat.MoodDesc".TranslateOr("Mood bonus from a proper dish, by learning level and quality (stale dishes give none)."), 4000);
        }

        // ---- freshness ---------------------------------------------------------------------------------------------------
        public override void CompTickRare()
        {
            if (dish == null) return;
            int now = Find.TickManager.TicksGame;
            if (lastTick < 0) lastTick = now;
            int delta = Mathf.Clamp(now - lastTick, 0, 6000);
            lastTick = now;
            if (delta == 0) { Sync(); return; }
            float rate = 1f;
            if ((dish.plate == PlateKind.Salad || dish.plate == PlateKind.Raw || dish.plate == PlateKind.Dessert) && parent.Spawned && parent.Position.GetTemperature(parent.Map) <= 10f) rate = 0.5f;
            freshAge += delta * rate;
            Sync();
        }

        /// <summary>Drive vanilla rot from our clock: stale from one shelf life, rotten after four.</summary>
        void Sync()
        {
            var rot = parent.GetComp<CompRottable>();
            if (rot == null || dish == null) return;
            rot.RotProgress = Mathf.Min(freshAge / (ShelfTicks * RotAfterShelfLives), 1.2f) * rot.PropsRot.TicksToRotStart;
        }

        public override bool AllowStackWith(Thing other)
        {
            var o = other.TryGetComp<CompDish>();
            return o != null && o.dish == dish && o.variant == variant;
        }

        public override void PreAbsorbStack(Thing otherStack, int count)
        {
            var o = otherStack.TryGetComp<CompDish>();
            if (o != null) freshAge = Mathf.Max(freshAge, o.freshAge);
        }

        public override void PostSplitOff(Thing piece)
        {
            var o = piece.TryGetComp<CompDish>();
            if (o != null) { o.dish = dish; o.variant = variant; o.freshAge = freshAge; o.lastTick = lastTick; }
        }

        // ---- eating ------------------------------------------------------------------------------------------------------
        public override void PostIngested(Pawn ingester)
        {
            if (dish == null) return;
            Guard.Run(ModuleId.ShopsFood, "Dish.Ingested", () => DishEffects.OnEaten(this, ingester));
        }
    }
}
