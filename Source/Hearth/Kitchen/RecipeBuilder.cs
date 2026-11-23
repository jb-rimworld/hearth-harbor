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
    /// "Do until you have X" must count THIS dish, not all plates of the same kind. Plain recipe counter semantics otherwise:
    /// a chosen stockpile/shelf ("count only in") counts only what it holds, carried plates count, held plates of haul sources (bookcases,
    /// outfit stands, modded containers) count once. Every plate is counted at most once: a thing that is spawned is counted by the
    /// map's lister only, even if a modded storage also reports it as held.
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
            var group = bill.GetIncludeSlotGroup();
            if (group != null)
            {
                foreach (var t in group.HeldThings) if (t != null && t.def == plateDef) n += Counted(t, bill, ext);
                return n;
            }
            var listed = bill.Map.listerThings.ThingsOfDef(plateDef);
            for (int i = 0; i < listed.Count; i++) n += Counted(listed[i], bill, ext);
            foreach (var src in bill.Map.haulDestinationManager.AllHaulSourcesListForReading)
            {
                var held = src.GetDirectlyHeldThings();
                if (held == null) continue;
                for (int i = 0; i < held.Count; i++)
                {
                    var t = held[i];
                    if (t == null || t.Spawned || t.def != plateDef) continue;   // spawned: already counted by the lister
                    n += Counted(t, bill, ext);
                }
            }
            foreach (var p in bill.Map.mapPawns.FreeColonistsSpawned)
            {
                var c = p.carryTracker?.CarriedThing;
                if (c != null && !c.Spawned && c.def == plateDef) n += Counted(c, bill, ext);
                if (!bill.includeEquipped || p.inventory == null) continue;
                var inv = p.inventory.innerContainer;
                for (int i = 0; i < inv.Count; i++) if (inv[i].def == plateDef) n += Counted(inv[i], bill, ext);
            }
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

        /// <summary>
        /// True for a recipe that only exists with this module: generated dish recipes (HH_Cook_*) and the test-kitchen recipe.
        /// Without the mod a save still loads (vanilla drops a bill whose recipe it cannot resolve, with an error line in the log),
        /// but removing our bills before the module goes keeps the log clean and tells the player what happened.
        /// </summary>
        public static bool IsOurRecipe(RecipeDef r) => r != null && (DishRecipeExtension.Of(r) != null || r.GetModExtension<TestKitchenExtension>() != null || r.defName.StartsWith("HH_Cook_"));

        /// <summary>Removes our bills from every bill stack on every map (work tables, benches, pawns). Returns the number of bills removed.</summary>
        public static int RemoveOurBills()
        {
            int n = 0;
            foreach (var map in Find.Maps)
            {
                var things = map.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    if (!(things[i] is IBillGiver giver)) continue;
                    var bills = giver.BillStack?.Bills; if (bills == null) continue;
                    for (int j = bills.Count - 1; j >= 0; j--)
                        if (IsOurRecipe(bills[j].recipe)) { giver.BillStack.Delete(bills[j]); n++; }
                }
            }
            return n;
        }

        public static void BuildAll()
        {
            Built = 0; Unavailable = 0; Retired = 0; OverlapRepairs = 0; DishRecipeExtension.Clear();
            var touched = new HashSet<ThingDef>();
            foreach (var dish in DefDatabase<DishDef>.AllDefs.OrderBy(d => d.level).ThenBy(d => d.defName).ToList())
            {
                dish.recipes.Clear(); dish.unavailableReason = null;
                try { BuildDish(dish, touched); }
                catch (Exception e) { dish.unavailableReason = "error: " + e.Message; HLog.Error("Kitchen", "Recipe build failed for " + dish.defName, e); }
                if (dish.unavailableReason != null) { Unavailable++; HLog.Debug("Kitchen", dish.defName + " not offered: " + dish.unavailableReason); }
            }
            foreach (var t in touched) allRecipesCached?.SetValue(t, null);
            HLog.Info("Kitchen", $"{Built} recipes built for {DefDatabase<DishDef>.DefCount - Unavailable} dishes ({Unavailable} dishes without recipe: missing ingredients or devices of mods that are not loaded; {Retired} hidden recipes keep old bills loadable; {OverlapRepairs} ingredient overlaps between slots repaired)");
        }

        /// <summary>Hidden recipe defs of forms a dish no longer has (kept so that bills of older saves still resolve).</summary>
        public static int Retired;
        /// <summary>How often two slots of a recipe allowed the same thing (mod foods that match two classes) and the later slot had to give it up.</summary>
        public static int OverlapRepairs;

        // ---- finishing touches (F11) ------------------------------------------------------------------------------------------------
        // A Fine or Signature plate asks for one (Fine) or two (Signature) extra ingredients on top of the dish's own. The vanilla bill solver fills
        // the first slot that allows a stack, so every slot of a recipe must allow DIFFERENT things: otherwise "more berries" would count as the second
        // ingredient of a fruit bowl. Finishers are therefore (1) picked from a pool that suits the plate (no chocolate on a fish, no beer in a salad),
        // (2) never a class the dish already uses, and (3) the slots of a recipe are made pairwise disjoint at the end (first slot wins).

        static readonly string[] PoolDessert = { "milk", "eggs", "chocolate", "berries", "agave" };
        static readonly string[] PoolBreakfast = { "milk", "eggs", "berries", "mushrooms", "agave" };
        static readonly string[] PoolSalad = { "milk", "eggs", "berries", "mushrooms" };
        static readonly string[] PoolRaw = { "eggs", "berries", "mushrooms" };
        static readonly string[] PoolSavory = { "milk", "eggs", "beer", "mushrooms", "berries" };
        /// <summary>Taken when a plate's own pool is used up by the dish (needs two classes for a Signature form): safe on any plate, never chocolate or beer.</summary>
        static readonly string[] PoolFallback = { "eggs", "milk", "berries", "mushrooms", "agave" };
        static readonly string[] SplitCreamy = { "milk", "eggs" };

        /// <summary>Finisher pool of a plate kind (class keys in order of preference).</summary>
        public static string[] PoolOf(PlateKind k)
        {
            switch (k)
            {
                case PlateKind.Dessert: return PoolDessert;
                case PlateKind.Breakfast: return PoolBreakfast;
                case PlateKind.Salad: return PoolSalad;
                case PlateKind.Raw: return PoolRaw;
                default: return PoolSavory;
            }
        }

        /// <summary>Which ingredient classes may finish one dish: Fine = one slot with all of them, Signature = two slots with different ones.</summary>
        public sealed class FinishPlan
        {
            public List<IngredientClass> Fine = new List<IngredientClass>();
            public List<IngredientClass> SigA = new List<IngredientClass>();
            public List<IngredientClass> SigB = new List<IngredientClass>();
            public bool HasFine => Fine.Count > 0;
            public bool HasSignature => SigA.Count > 0 && SigB.Count > 0;
        }

        /// <summary>The finishers of a dish: its own pool (or the plate's), minus everything the dish itself uses. Public for the tests.</summary>
        public static FinishPlan PlanFinishers(DishDef dish)
        {
            var plan = new FinishPlan();
            if (dish.noVariants) return plan;
            var own = new HashSet<ThingDef>(); var ownKeys = new HashSet<string>();
            foreach (var s in dish.ingredients)
            {
                ownKeys.Add(s.cls);
                var c = IngredientClasses.Find(s.cls);
                if (c != null) foreach (var t in c.Things) own.Add(t);
            }
            List<IngredientClass> Pick(IEnumerable<string> keys)
            {
                var list = new List<IngredientClass>();
                foreach (var k in keys)
                {
                    if (ownKeys.Contains(k)) continue;
                    var c = IngredientClasses.Find(k);
                    if (c == null || !c.Available || list.Contains(c)) continue;
                    if (c.Things.All(own.Contains)) continue;   // nothing left of it once the dish's own ingredients are taken away
                    list.Add(c);
                }
                return list;
            }
            var pool = (dish.finishers != null && dish.finishers.Count > 0) ? dish.finishers : (IEnumerable<string>)PoolOf(dish.plate);
            var elig = Pick(pool);
            plan.Fine = elig;
            if (elig.Count < 2 && (dish.finishers == null || dish.finishers.Count == 0))
            {
                var wider = Pick(pool.Concat(PoolFallback));   // a Signature plate needs two different finishing touches
                if (wider.Count >= 2) elig = wider;
            }
            if (elig.Count == 0) { HLog.Once("kitchen.nofin." + dish.defName, LogLevel.Warn, "Kitchen", dish.defName + " has no finishing touch to offer: no Fine and no Signature form."); return plan; }
            if (plan.Fine.Count == 0) plan.Fine = elig;
            if (elig.Count < 2) return plan;
            var a = elig.Where(c => SplitCreamy.Contains(c.Key)).ToList();
            var b = elig.Where(c => !SplitCreamy.Contains(c.Key)).ToList();
            if (a.Count == 0 || b.Count == 0)
            {
                int half = (elig.Count + 1) / 2;
                a = elig.Take(half).ToList(); b = elig.Skip(half).ToList();
            }
            plan.SigA = a; plan.SigB = b;
            return plan;
        }

        /// <summary>Makes the slots pairwise disjoint: a thing allowed by an earlier slot is removed from every later one. Returns the number of removals.</summary>
        public static int MakeDisjoint(IList<IngredientCount> slots)
        {
            int removed = 0;
            var taken = new HashSet<ThingDef>();
            foreach (var slot in slots)
                foreach (var t in slot.filter.AllowedThingDefs.ToList())
                    if (!taken.Add(t)) { slot.filter.SetAllow(t, false); removed++; }
            return removed;
        }

        static string OrList(IEnumerable<string> labels)
        {
            var l = labels.ToList();
            if (l.Count <= 1) return string.Join("", l);
            string last = l[l.Count - 1];
            return string.Join(", ", l.Take(l.Count - 1)) + " " + "HH.Kitchen.Or".TranslateOr("or") + " " + last;
        }

        static IngredientCount FinisherSlot(List<IngredientClass> classes, string key, string fallbackText, HashSet<ThingDef> own)
        {
            var f = new ThingFilter { customSummary = key.TranslateOr(fallbackText).Replace("{0}", OrList(classes.Select(c => c.Label))) };
            foreach (var c in classes) foreach (var t in c.Things) if (!own.Contains(t)) f.SetAllow(t, true);
            var ic = new IngredientCount { filter = f }; ic.SetBaseCount(0.1f);
            return ic;
        }

        static void BuildDish(DishDef dish, HashSet<ThingDef> touched)
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
            var plan = PlanFinishers(dish);
            for (int v = 0; v < 3; v++)
            {
                bool offered = v == 0 || (plan.HasFine && (v == 1 || plan.HasSignature));
                var r = MakeRecipe(dish, v, classes, mains, plan, offered);
                if (r == null && v > 0 && offered)
                {
                    // Keep the def alive (retired, main slots only) so a saved 1.0 bill for this form still loads.
                    offered = false;
                    r = MakeRecipe(dish, v, classes, mains, plan, false);
                }
                if (r == null)
                {
                    if (v == 0) { dish.unavailableReason = "ingredient classes overlap so that a slot has nothing left"; return; }
                    continue;
                }
                if (offered) { dish.recipes.Add(r); Built++; } else Retired++;
            }
            foreach (var m in mains) touched.Add(m);
        }

        static RecipeDef MakeRecipe(DishDef dish, int variant, List<(IngredientClass c, float share)> classes, List<ThingDef> mains, FinishPlan plan, bool offered)
        {
            var ext = new DishRecipeExtension { dish = dish, variant = variant, retired = !offered };
            var r = new RecipeDef
            {
                defName = "HH_Cook_" + dish.Id + (variant == 0 ? "" : "_" + VariantName[variant]),
                label = "cook " + dish.label.ToLowerInvariant() + VariantLabel[variant],
                description = dish.description + "\n\n" + "HH.Kitchen.RecipeInfo".TranslateOr("Devices: {0}. Steps: {1}.").Replace("{0}", dish.StationSummary()).Replace("{1}", dish.StepSummary()),
                jobString = "Cooking " + dish.label.ToLowerInvariant() + ".",
                workSpeedStat = DefDatabase<StatDef>.GetNamedSilentFail("CookSpeed"),
                workSkill = SkillDefOf.Cooking,
                requiredGiverWorkType = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking"),
                effectWorking = CookSounds.EffecterFor(dish),
                soundWorking = CookSounds.SoundFor(dish),
                allowMixingIngredients = true,
                workAmount = (300f + 130f * dish.steps.Count + 50f * dish.level) * VariantWork[variant],
                displayPriority = dish.level * 1000 + variant * 10 + (Mathf.Abs(dish.defName.GetHashCode()) % 9),
                recipeUsers = new List<ThingDef>(Stations.MainDefs(dish.MainStation)),
                products = new List<ThingDefCountClass> { new ThingDefCountClass(dish.plateDef, 1) },
                skillRequirements = new List<SkillRequirement> { new SkillRequirement { skill = SkillDefOf.Cooking, minLevel = Mathf.Min(20, dish.minCooking + VariantSkill[variant]) } },
                modExtensions = new List<DefModExtension> { ext },
                ingredients = new List<IngredientCount>(),
                fixedIngredientFilter = new ThingFilter(),
                generated = true,
            };
            ingredientGetterField.SetValue(r, typeof(IngredientValueGetter_Nutrition));
            r.workerCounterClass = typeof(RecipeWorkerCounter_Dish);
            if (variant == 2) r.researchPrerequisite = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_FineDining");
            r.conceptLearned = null;

            // Main slots: one per ingredient class of the dish (a retired form keeps only these).
            int form = offered ? variant : 0;
            float pool = BaseNutrition * LevelScale[Mathf.Clamp(dish.level, 1, 4)] * VariantScale[form];
            float poolShares = classes.Where(c => !c.c.Unit).Sum(c => c.share);
            if (poolShares <= 0f) poolShares = 1f;
            foreach (var (c, share) in classes)
            {
                var ic = new IngredientCount { filter = c.MakeFilter() };
                float count = c.Unit ? share * c.PieceNutrition() * (form == 0 ? 1f : VariantScale[form]) : pool * share / poolShares;
                ic.SetBaseCount(Mathf.Round(count * 1000f) / 1000f);
                r.ingredients.Add(ic);
            }
            // Finishing touches (never a thing of the dish's own classes, see PlanFinishers).
            if (offered && variant >= 1)
            {
                var own = new HashSet<ThingDef>();
                foreach (var (c, _) in classes) foreach (var t in c.Things) own.Add(t);
                if (variant == 1) r.ingredients.Add(FinisherSlot(plan.Fine, "HH.Kitchen.FinishingTouch", "a finishing touch ({0})", own));
                else
                {
                    r.ingredients.Add(FinisherSlot(plan.SigA, "HH.Kitchen.FinishingTouch", "a finishing touch ({0})", own));
                    r.ingredients.Add(FinisherSlot(plan.SigB, "HH.Kitchen.FinishingTouch2", "a second finishing touch ({0})", own));
                }
            }
            OverlapRepairs += MakeDisjoint(r.ingredients);
            foreach (var slot in r.ingredients)
            {
                if (slot.filter.AllowedDefCount == 0) { HLog.Warn("Kitchen", r.defName + ": a slot is empty after separating the ingredients" + (variant == 0 ? "" : " (form skipped)")); return null; }
                foreach (var t in slot.filter.AllowedThingDefs) r.fixedIngredientFilter.SetAllow(t, true);
            }
            r.defaultIngredientFilter = new ThingFilter();
            r.defaultIngredientFilter.CopyAllowancesFrom(r.fixedIngredientFilter);
            r.ResolveReferences();
            DefDatabase<RecipeDef>.Add(r);
            DishRecipeExtension.Register(r, ext);
            return r;
        }
    }
}
