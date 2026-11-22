// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>
    /// An ingredient class of the menu ("meat", "potatoes", "cheese" …). Resolved once at startup against all loaded defs, so animals,
    /// plants and foods of other mods are included automatically (KONZEPT 12.13: no ingredients of our own).
    /// </summary>
    public sealed class IngredientClass
    {
        public string Key, LabelEn;
        /// <summary>One piece per share (beer, hemogen) instead of a share of the nutrition pool.</summary>
        public bool Unit;
        public Func<ThingDef, bool> Match;
        public readonly List<ThingDef> Things = new List<ThingDef>();
        public string Label => ("HH.Kitchen.Ingredient." + Key).TranslateOr(LabelEn ?? Key);
        public bool Available => Things.Count > 0;

        public ThingFilter MakeFilter()
        {
            var f = new ThingFilter();
            foreach (var t in Things) f.SetAllow(t, true);
            f.customSummary = Label;
            return f;
        }

        /// <summary>Nutrition of one piece (smallest of the class), used for unit ingredients.</summary>
        public float PieceNutrition()
        {
            float v = 0f;
            foreach (var t in Things) { float n = t.GetStatValueAbstract(StatDefOf.Nutrition); if (n > 0f && (v == 0f || n < v)) v = n; }
            return v > 0f ? v : 0.05f;
        }
    }

    public static class IngredientClasses
    {
        static readonly Dictionary<string, IngredientClass> byKey = new Dictionary<string, IngredientClass>();
        public static IEnumerable<IngredientClass> All => byKey.Values;
        public static IngredientClass Find(string key)
        {
            if (byKey.Count == 0) Define();   // config checks run before the startup resolve: the keys are known, the things not yet
            return key != null && byKey.TryGetValue(key, out var c) ? c : null;
        }

        static ThingCategoryDef catFish, catEggs, catFoodRaw, catMeals;
        static bool built;

        static bool Edible(ThingDef t) => t.ingestible != null && t.category == ThingCategory.Item && t.IsNutritionGivingIngestible;
        static bool IsMeal(ThingDef t) => catMeals != null && t.thingCategories != null && t.thingCategories.Contains(catMeals);
        static bool InCat(ThingDef t, ThingCategoryDef c) => c != null && t.thingCategories != null && t.thingCategories.Contains(c);
        static ThingDef Src(ThingDef t) => t.ingestible?.sourceDef;
        static bool Insect(ThingDef t) => t.IsMeat && Src(t)?.race?.FleshType == FleshTypeDefOf.Insectoid;
        static bool Human(ThingDef t) => t.defName == "Meat_Human" || (t.IsMeat && Src(t)?.race?.Humanlike == true);
        static bool Twisted(ThingDef t) => t.defName == "Meat_Twisted";
        static bool Fish(ThingDef t) => Edible(t) && InCat(t, catFish);

        static IEnumerable<string> Tokens(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsUpper(ch) && sb.Length > 0 && !char.IsUpper(sb[sb.Length - 1])) { yield return sb.ToString().ToLowerInvariant(); sb.Clear(); }
                if (char.IsLetter(ch)) sb.Append(ch);
                else if (sb.Length > 0) { yield return sb.ToString().ToLowerInvariant(); sb.Clear(); }
            }
            if (sb.Length > 0) yield return sb.ToString().ToLowerInvariant();
        }

        /// <summary>Soft match for foods of other mods: raw food whose name has one of the words, optionally only from the named packages.</summary>
        static Func<ThingDef, bool> ByName(string[] words, string[] packages = null, string[] not = null, bool substring = false)
        {
            return t =>
            {
                if (!Edible(t) || IsMeal(t)) return false;
                if (!(InCat(t, catFoodRaw) || t.thingCategories?.Any(c => c.Parents.Contains(catFoodRaw)) == true || t.ingestible.foodType.HasFlag(FoodTypeFlags.VegetableOrFruit))) return false;
                if (packages != null && (t.modContentPack == null || !packages.Any(p => string.Equals(p, t.modContentPack.PackageIdPlayerFacing, StringComparison.OrdinalIgnoreCase)))) return false;
                return NameMatches(t.defName + " " + t.label, words, not, substring);
            };
        }

        /// <summary>Name test used for the foods of other mods: whole words (camel case and spaces split them) or, for odd spellings, substrings.</summary>
        public static bool NameMatches(string name, string[] words, string[] not = null, bool substring = false)
        {
            if (substring) { string l = name.ToLowerInvariant(); if (!words.Any(w => l.Contains(w))) return false; }
            else { var tok = new HashSet<string>(Tokens(name)); if (!words.Any(w => tok.Contains(w) || tok.Contains(w + "s"))) return false; }
            if (not != null) { var tok2 = new HashSet<string>(Tokens(name)); if (not.Any(w => tok2.Contains(w))) return false; }
            return true;
        }

        static void Add(string key, string label, Func<ThingDef, bool> match, bool unit = false)
            => byKey[key] = new IngredientClass { Key = key, LabelEn = label, Match = match, Unit = unit };

        static Func<ThingDef, bool> Named(params string[] defNames) => t => Edible(t) && defNames.Contains(t.defName);

        static void Define()
        {
            catFish = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Fish");
            catEggs = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("EggsUnfertilized");
            catFoodRaw = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodRaw");
            catMeals = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodMeals");

            Add("meat", "meat", t => Edible(t) && t.IsMeat && !Insect(t) && !Human(t) && !Twisted(t) && !Fish(t) && !InCat(t, catFish));
            Add("insectmeat", "insect meat", t => Edible(t) && Insect(t));
            Add("humanmeat", "human meat", t => Edible(t) && Human(t));
            Add("twistedmeat", "twisted meat", t => Edible(t) && Twisted(t));
            Add("fish", "fish", Fish);
            Add("eggs", "eggs", t => Edible(t) && InCat(t, catEggs));
            Add("milk", "milk", t => Edible(t) && !IsMeal(t) && t.defName.IndexOf("Milk", StringComparison.OrdinalIgnoreCase) >= 0 && InCat(t, DefDatabase<ThingCategoryDef>.GetNamedSilentFail("AnimalProductRaw")));
            Add("potatoes", "potatoes", Named("RawPotatoes"));
            Add("corn", "corn", Named("RawCorn"));
            Add("rice", "rice", Named("RawRice"));
            Add("berries", "berries", Named("RawBerries"));
            Add("agave", "agave fruit", Named("RawAgave"));
            Add("mushrooms", "mushrooms", Named("RawFungus"));
            Add("chocolate", "chocolate", Named("Chocolate"));
            Add("beer", "beer", t => t.defName == "Beer" && t.ingestible != null, unit: true);
            Add("hemogen", "hemogen pack", t => t.defName == "HemogenPack", unit: true);

            // Foods of other mods (best effort, by name and package; nothing found means the recipe is simply not offered).
            var vce = new[] { "VanillaExpanded.VCookE", "VanillaExpanded.VCookEBakery", "VanillaExpanded.VCookEStews", "VanillaExpanded.VCookESushi", "VanillaExpanded.VCookEHaute", "VanillaExpanded.VPlantsE" };
            var vpe = new[] { "VanillaExpanded.VPlantsE", "VanillaExpanded.VPlantsEMore", "VanillaExpanded.VPlantsEMushrooms", "VanillaExpanded.VPlantsESucculents" };
            var all = vce.Concat(vpe).ToArray();
            var kenshi = new[] { "super.kenshicuisine" };
            Add("cheese", "cheese", ByName(new[] { "cheese" }, all));
            Add("pumpkin", "pumpkin", ByName(new[] { "pumpkin" }, all));
            Add("flour", "flour", ByName(new[] { "flour" }, all, new[] { "straw" }));
            Add("fruit", "fruit", ByName(new[] { "fruit", "apple", "pear", "peach", "plum", "cherry", "orange", "lemon", "grape", "banana", "melon", "mango", "pineapple" }, all, new[] { "dried" }));
            Add("soysauce", "soy sauce", ByName(new[] { "soy", "soya" }, all));
            Add("wasabi", "wasabi paste", ByName(new[] { "wasabi" }, all));
            Add("vegetables", "vegetables", ByName(new[] { "vegetable", "carrot", "tomato", "onion", "cabbage", "lettuce", "pepper", "cucumber", "zucchini", "spinach", "eggplant", "garlic", "bean", "pea", "leek", "beet", "radish", "broccoli", "cauliflower", "celery" }, all));
            Add("sugar", "sugar", ByName(new[] { "sugar" }, all));
            Add("gourmet", "gourmet ingredients", ByName(new[] { "gourmet", "truffle", "caviar", "saffron", "foie", "wagyu", "lobster" }, all));
            Add("herbs", "herbs", ByName(new[] { "herb", "basil", "thyme", "mint", "parsley", "rosemary", "sage", "oregano", "coriander", "dill", "chive" }, all));
            Add("nuts", "nuts", ByName(new[] { "nut", "almond", "walnut", "peanut", "pecan", "hazelnut", "pistachio", "cashew" }, all, new[] { "doughnut", "donut", "coconut" }));
            Add("tealeaves", "tea leaves", ByName(new[] { "tea" }, all, new[] { "steak" }));
            Add("strawflour", "strawflour", ByName(new[] { "strawflour", "wheatstraw" }, kenshi, substring: true));
            Add("pricklypear", "prickly pear", ByName(new[] { "pricklypear", "prickly", "cactus" }, kenshi, substring: true));
            Add("greenfruit", "greenfruit", ByName(new[] { "greenfruit" }, kenshi, substring: true));
        }

        /// <summary>Resolve against the loaded defs. Called once from the module (after all defs). Safe to call again.</summary>
        public static void Resolve()
        {
            byKey.Clear(); Define(); built = true;
            foreach (var c in byKey.Values) c.Things.Clear();
            foreach (var t in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (t.ingestible == null || t.category != ThingCategory.Item || t.IsCorpse) continue;
                foreach (var c in byKey.Values)
                {
                    bool ok;
                    try { ok = c.Match(t); } catch { ok = false; }
                    if (ok) c.Things.Add(t);
                }
            }
            HLog.Info("Kitchen", "Ingredient classes: " + string.Join(", ", byKey.Values.Where(c => c.Available).Select(c => c.Key + "=" + c.Things.Count)));
        }

        public static bool Built => built;
    }
}
