// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>
    /// "Do until you have X" must count THIS dish, not all plates of the same kind. Plain recipe counter semantics otherwise.
    /// </summary>
    public class RecipeWorkerCounter_Dish : RecipeWorkerCounter
    {
        public override bool CanCountProducts(Bill_Production bill) => true;

        public override int CountProducts(Bill_Production bill)
        {
            var ext = DishRecipeExtension.Of(recipe);
            if (ext?.dish == null || bill.Map == null) return 0;
            var plateDef = recipe.products[0].thingDef;
            int n = 0;
            foreach (var t in bill.Map.listerThings.ThingsOfDef(plateDef)) n += Counted(t, bill, ext);
            foreach (var src in bill.Map.haulDestinationManager.AllHaulSourcesListForReading)
            {
                var held = src.GetDirectlyHeldThings();
                for (int i = 0; i < held.Count; i++) n += Counted(held[i], bill, ext);
            }
            foreach (var p in bill.Map.mapPawns.FreeColonistsSpawned)
                if (p.carryTracker?.CarriedThing != null) n += Counted(p.carryTracker.CarriedThing, bill, ext);
            return n;
        }

        static int Counted(Thing t, Bill_Production bill, DishRecipeExtension ext)
        {
            var comp = t.TryGetComp<CompDish>();
            if (comp == null || comp.dish != ext.dish) return 0;
            if (t.def.useHitPoints && t.MaxHitPoints > 0)
            {
                float hp = (float)t.HitPoints / t.MaxHitPoints;
                if (hp < bill.hpRange.min - 1e-4f || hp > bill.hpRange.max + 1e-4f) return 0;
            }
            if (t.TryGetQuality(out var q) && (q < bill.qualityRange.min || q > bill.qualityRange.max)) return 0;
            return t.stackCount;
        }
    }

    /// <summary>
    /// Builds the three recipes of every dish at startup (Everyday, Fine, Signature, 6.4). They are normal RecipeDefs at normal work tables,
    /// so robots and Project RimFactory machines can cook them like any other bill. Ingredient classes are resolved against the loaded mods.
    /// </summary>
    public static class RecipeBuilder
    {
        public const float BaseNutrition = 0.5f;   // an Everyday plate eats as much raw food as a simple meal
        public static readonly float[] LevelScale = { 1.0f, 1.0f, 1.1f, 1.25f, 1.5f };   // index = level
        public static readonly string[] VariantName = { "Everyday", "Fine", "Signature" };
        static readonly string[] VariantLabel = { "", " (fine)", " (signature)" };
        static readonly float[] VariantScale = { 1f, 1.5f, 2f };
        static readonly int[] VariantSkill = { 0, 2, 4 };
        static readonly float[] VariantWork = { 1f, 1.2f, 1.45f };

        static readonly System.Reflection.FieldInfo ingredientGetterField = AccessTools.Field(typeof(RecipeDef), "ingredientValueGetterClass");
        static readonly System.Reflection.FieldInfo allRecipesCached = AccessTools.Field(typeof(ThingDef), "allRecipesCached");

        public static int Built, Unavailable;

        public static void BuildAll()
        {
            Built = 0; Unavailable = 0;
            var touched = new HashSet<ThingDef>();
            var fin = FinisherFilters();
            foreach (var dish in DefDatabase<DishDef>.AllDefs.OrderBy(d => d.level).ThenBy(d => d.defName).ToList())
            {
                dish.recipes.Clear(); dish.unavailableReason = null;
                try { BuildDish(dish, fin, touched); }
                catch (Exception e) { dish.unavailableReason = "error: " + e.Message; HLog.Error("Kitchen", "Recipe build failed for " + dish.defName, e); }
                if (dish.unavailableReason != null) { Unavailable++; HLog.Debug("Kitchen", dish.defName + " not offered: " + dish.unavailableReason); }
            }
            foreach (var t in touched) allRecipesCached?.SetValue(t, null);
            HLog.Info("Kitchen", $"{Built} recipes built for {DefDatabase<DishDef>.DefCount - Unavailable} dishes ({Unavailable} dishes without recipe: missing ingredients or devices of mods that are not loaded)");
        }

        static (ThingFilter any, ThingFilter a, ThingFilter b) FinisherFilters()
        {
            ThingFilter Mk(params string[] keys)
            {
                var f = new ThingFilter();
                foreach (var k in keys) { var c = IngredientClasses.Find(k); if (c != null) foreach (var t in c.Things) f.SetAllow(t, true); }
                return f;
            }
            return (Mk("milk", "eggs", "chocolate", "beer", "berries", "mushrooms"), Mk("milk", "eggs", "chocolate"), Mk("beer", "berries", "mushrooms"));
        }

        static void BuildDish(DishDef dish, (ThingFilter any, ThingFilter a, ThingFilter b) fin, HashSet<ThingDef> touched)
        {
            if (dish.plateDef == null) { dish.unavailableReason = "plate missing"; return; }
            var mains = Stations.MainDefs(dish.MainStation);
            if (mains.Count == 0) { dish.unavailableReason = "no device of kind " + dish.MainStation; return; }
            var classes = new List<(IngredientClass c, float share)>();
            foreach (var s in dish.ingredients)
            {
                var c = IngredientClasses.Find(s.cls);
                if (c == null || !c.Available) { dish.unavailableReason = "no ingredient of class '" + s.cls + "' loaded"; return; }
                classes.Add((c, s.amount));
            }
            for (int v = 0; v < 3; v++)
            {
                if (v == 1 && fin.any.AllowedDefCount == 0) break;
                if (v == 2 && (fin.a.AllowedDefCount == 0 || fin.b.AllowedDefCount == 0)) break;
                var r = MakeRecipe(dish, v, classes, mains, fin);
                dish.recipes.Add(r); Built++;
            }
            foreach (var m in mains) touched.Add(m);
        }

        static RecipeDef MakeRecipe(DishDef dish, int variant, List<(IngredientClass c, float share)> classes, List<ThingDef> mains, (ThingFilter any, ThingFilter a, ThingFilter b) fin)
        {
            var r = new RecipeDef
            {
                defName = "HH_Cook_" + dish.Id + (variant == 0 ? "" : "_" + VariantName[variant]),
                label = "cook " + dish.label.ToLowerInvariant() + VariantLabel[variant],
                description = dish.description + "\n\n" + "HH.Kitchen.RecipeInfo".TranslateOr("Devices: {0}. Steps: {1}.").Replace("{0}", dish.StationSummary()).Replace("{1}", dish.StepSummary()),
                jobString = "Cooking " + dish.label.ToLowerInvariant() + ".",
                workSpeedStat = DefDatabase<StatDef>.GetNamedSilentFail("CookSpeed"),
                workSkill = SkillDefOf.Cooking,
                requiredGiverWorkType = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking"),
                effectWorking = DefDatabase<EffecterDef>.GetNamedSilentFail("Cook"),
                soundWorking = DefDatabase<SoundDef>.GetNamedSilentFail("Recipe_CookMeal"),
                allowMixingIngredients = true,
                workAmount = (300f + 130f * dish.steps.Count + 50f * dish.level) * VariantWork[variant],
                displayPriority = dish.level * 1000 + variant * 10 + (Mathf.Abs(dish.defName.GetHashCode()) % 9),
                recipeUsers = new List<ThingDef>(Stations.MainDefs(dish.MainStation)),
                products = new List<ThingDefCountClass> { new ThingDefCountClass(dish.plateDef, 1) },
                skillRequirements = new List<SkillRequirement> { new SkillRequirement { skill = SkillDefOf.Cooking, minLevel = Mathf.Min(20, dish.minCooking + VariantSkill[variant]) } },
                modExtensions = new List<DefModExtension> { new DishRecipeExtension { dish = dish, variant = variant } },
                ingredients = new List<IngredientCount>(),
                fixedIngredientFilter = new ThingFilter(),
                generated = true,
            };
            ingredientGetterField.SetValue(r, typeof(IngredientValueGetter_Nutrition));
            r.workerCounterClass = typeof(RecipeWorkerCounter_Dish);
            if (variant == 2) r.researchPrerequisite = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_FineDining");
            r.conceptLearned = null;

            float pool = BaseNutrition * LevelScale[Mathf.Clamp(dish.level, 1, 4)] * VariantScale[variant];
            float poolShares = classes.Where(c => !c.c.Unit).Sum(c => c.share);
            if (poolShares <= 0f) poolShares = 1f;
            foreach (var (c, share) in classes)
            {
                var ic = new IngredientCount { filter = c.MakeFilter() };
                float count = c.Unit ? share * c.PieceNutrition() * (variant == 0 ? 1f : VariantScale[variant]) : pool * share / poolShares;
                ic.SetBaseCount(Mathf.Round(count * 1000f) / 1000f);
                r.ingredients.Add(ic);
                foreach (var t in c.Things) r.fixedIngredientFilter.SetAllow(t, true);
            }
            if (variant >= 1)
            {
                var f1 = new ThingFilter { customSummary = "HH.Kitchen.Finishing".TranslateOr("a finishing touch (milk, eggs, chocolate, beer, berries or mushrooms)") };
                foreach (var t in (variant == 1 ? fin.any : fin.a).AllowedThingDefs) f1.SetAllow(t, true);
                var i1 = new IngredientCount { filter = f1 }; i1.SetBaseCount(0.1f); r.ingredients.Add(i1);
                foreach (var t in (variant == 1 ? fin.any : fin.a).AllowedThingDefs) r.fixedIngredientFilter.SetAllow(t, true);
                if (variant == 2)
                {
                    var f2 = new ThingFilter { customSummary = "HH.Kitchen.Finishing2".TranslateOr("a second finishing touch (beer, berries or mushrooms)") };
                    foreach (var t in fin.b.AllowedThingDefs) f2.SetAllow(t, true);
                    var i2 = new IngredientCount { filter = f2 }; i2.SetBaseCount(0.1f); r.ingredients.Add(i2);
                    foreach (var t in fin.b.AllowedThingDefs) r.fixedIngredientFilter.SetAllow(t, true);
                }
            }
            r.defaultIngredientFilter = new ThingFilter();
            r.defaultIngredientFilter.CopyAllowancesFrom(r.fixedIngredientFilter);
            r.ResolveReferences();
            DefDatabase<RecipeDef>.Add(r);
            return r;
        }
    }
}
