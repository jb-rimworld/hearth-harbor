// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>A recipe book (KONZEPT 6.4): teaches one to three dishes to the whole colony. Dishes are chosen when the book is made.</summary>
    public class CompProperties_Cookbook : CompProperties
    {
        public int minDishes = 1, maxDishes = 3;
        public CompProperties_Cookbook() { compClass = typeof(CompCookbook); }
    }

    public class CompCookbook : ThingComp
    {
        public List<DishDef> dishes = new List<DishDef>();
        public string origin;
        public CompProperties_Cookbook Props => (CompProperties_Cookbook)props;

        public override void PostPostMake()
        {
            base.PostPostMake();
            if (dishes.Count == 0) Fill(null);
        }

        /// <summary>Pick the dishes: unknown ones first, higher levels rarer. Origin is the region of the first dish (or a mod name for extra recipes).</summary>
        public void Fill(string forceOrigin)
        {
            var pool = DefDatabase<DishDef>.AllDefs.Where(d => d.IsAvailable && !d.dark && d.level >= 2).ToList();
            if (pool.Count == 0) pool = DefDatabase<DishDef>.AllDefs.Where(d => d.IsAvailable && !d.dark).ToList();
            if (pool.Count == 0) return;
            int n = Rand.RangeInclusive(Props.minDishes, Props.maxDishes);
            var first = pool.RandomElementByWeight(d => 1f / d.level);
            dishes.Add(first);
            origin = forceOrigin ?? (first.fromMod.NullOrEmpty() ? first.inspiration : first.fromMod);
            // the rest from the same corner of the world where possible
            var same = pool.Where(d => d != first && (d.cuisine == first.cuisine || d.fromMod == first.fromMod)).ToList();
            for (int i = 1; i < n; i++)
            {
                var src = same.Count > 0 ? same : pool.Where(d => !dishes.Contains(d)).ToList();
                if (src.Count == 0) break;
                var pick = src.RandomElementByWeight(d => 1f / d.level);
                if (!dishes.Contains(pick)) dishes.Add(pick);
                same.Remove(pick);
            }
        }

        public bool AnythingToLearn => dishes.Any(d => !DishKnowledge.IsKnown(d));

        public override string TransformLabel(string label)
            => dishes.Count == 0 ? label : label + " (" + string.Join(", ", dishes.Select(d => d.label)) + ")";

        public override string CompInspectStringExtra()
        {
            if (dishes.Count == 0) return null;
            return ("HH.Kitchen.Cookbook.Teaches".TranslateOr("Teaches: {0}")).Replace("{0}", string.Join(", ", dishes.Select(d => d.label + (DishKnowledge.IsKnown(d) ? " ✓" : ""))));
        }

        public override void PostExposeData() { Scribe_Collections.Look(ref dishes, "dishes", LookMode.Def); Scribe_Values.Look(ref origin, "origin"); if (dishes == null) dishes = new List<DishDef>(); dishes.RemoveAll(d => d == null); }
    }

    /// <summary>Reading the book: the colony learns its dishes.</summary>
    public class CompUseEffect_LearnDishes : CompUseEffect
    {
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var book = parent.TryGetComp<CompCookbook>();
            if (book == null || book.dishes.Count == 0) return "HH.Kitchen.Cookbook.Empty".TranslateOr("The pages are blank.");
            if (!book.AnythingToLearn) return "HH.Kitchen.Cookbook.AllKnown".TranslateOr("The colony already knows every dish in this book.");
            if (p.skills != null && p.skills.GetSkill(SkillDefOf.Cooking).TotallyDisabled) return "HH.Kitchen.Cookbook.CannotCook".TranslateOr("Cannot cook.");
            return true;
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            var book = parent.TryGetComp<CompCookbook>();
            if (book == null) return;
            var learned = new List<string>();
            foreach (var d in book.dishes) if (!DishKnowledge.IsKnown(d) && DishKnowledge.Learn(d)) learned.Add(d.label);
            if (learned.Count > 0)
                Messages.Message(("HH.Kitchen.Cookbook.Learned".TranslateOr("{0} studies the book. The colony can now cook: {1}.")).Replace("{0}", usedBy.LabelShortCap).Replace("{1}", string.Join(", ", learned)), usedBy, MessageTypeDefOf.PositiveEvent);
        }
    }

    /// <summary>
    /// The test kitchen (6.4): experiment at a prep counter with raw food. With p = 0.15 + 0.02 (Cooking - minimum skill) the colony works out a dish it does not know yet
    /// (of the open levels); otherwise the experiment ends as a failed plate of that dish in Awful quality. The plate is made by the normal recipe, so
    /// machines and robots can run it too.
    /// </summary>
    public class TestKitchenExtension : DefModExtension { }

    public static class TestKitchen
    {
        public static int HighestOpenLevel()
        {
            int lv = 1;
            for (int l = 2; l <= 4; l++) if (DishKnowledge.LevelUnlocked(l)) lv = l;
            return Mathf.Min(4, lv + 1);
        }

        /// <summary>Unknown dishes the colony could still work out.</summary>
        public static List<DishDef> Candidates()
        {
            int open = HighestOpenLevel();
            return DefDatabase<DishDef>.AllDefs.Where(d => d.IsAvailable && !d.dark && d.level <= open && !DishKnowledge.IsKnown(d)).ToList();
        }

        public static float SuccessChance(Pawn cook, DishDef d)
        {
            int skill = cook?.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0;
            return Mathf.Clamp(0.15f + 0.02f * (skill - d.minCooking), 0.05f, 0.95f);
        }

        /// <summary>Run one experiment. Returns the plate to hand out (a new thing) or null to keep the product the recipe made.</summary>
        public static Thing Experiment(Thing product, Pawn cook, out DishDef target, out bool success)
        {
            target = null; success = false;
            var cands = Candidates();
            if (cands.Count == 0) return null;
            target = cands.RandomElement();
            success = Rand.Chance(SuccessChance(cook, target));
            var plate = ThingMaker.MakeThing(target.plateDef);
            var q = product.TryGetComp<CompQuality>();
            var nq = plate.TryGetComp<CompQuality>();
            nq?.SetQuality(success ? (q?.Quality ?? QualityCategory.Normal) : QualityCategory.Awful, ArtGenerationContext.Colony);
            plate.TryGetComp<CompDish>()?.SetDish(target, 0);
            var ing = plate.TryGetComp<CompIngredients>(); var ing0 = product.TryGetComp<CompIngredients>();
            if (ing != null && ing0 != null) foreach (var i in ing0.ingredients) ing.RegisterIngredient(i);
            if (success) DishKnowledge.Learn(target);
            return plate;
        }
    }
}
