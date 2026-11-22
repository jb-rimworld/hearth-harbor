// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>The 12 plate kinds (KONZEPT 6.4, S5). Each kind is one ThingDef HH_Plate_&lt;Kind&gt;.</summary>
    public enum PlateKind { Soup, Stew, Grill, Noodles, Rice, Bake, Fry, Salad, Raw, Breakfast, Dessert, StreetFood }

    /// <summary>Kitchen devices. A dish names the devices of its steps; the LAST one is the main device that carries the bill (6.4).</summary>
    public enum StationKind { PrepCounter, StockPot, Grill, Oven, Wok, Fryer, Smoker, Steamer, DoughBench, ColdStation, Campfire, Stove, ButcherTable }

    public enum StepKind { Chop, Simmer, Boil, Grill, KneadDough, Smoke, Roast, StirFry, DeepFry, Steam, Chill, Carve, SpitRoast, PanFry, Sear }

    /// <summary>Kitchen styles of 6.4. Used for guest taste and the restaurant rating.</summary>
    public enum Cuisine { Frontier, TribalFeast, TavernFare, ImperialHaute, GlitterFusion, Street, Seafood, Insect, Fungal, Vegetarian, Ascetic, LongPork }

    /// <summary>"meat 2": an ingredient class with a share. Written in XML as the plain text of an li.</summary>
    public sealed class IngredientShare
    {
        public string cls;
        public float amount = 1f;
        public IngredientShare() { }
        public IngredientShare(string cls, float amount) { this.cls = cls; this.amount = amount; }

        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            string s = (xmlRoot.InnerText ?? "").Trim();
            int sp = s.LastIndexOf(' ');
            if (sp > 0 && float.TryParse(s.Substring(sp + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float a)) { cls = s.Substring(0, sp).Trim(); amount = a; }
            else { cls = s; amount = 1f; }
        }
        public override string ToString() => cls + " " + amount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One dish of the menu (KONZEPT 6.4, Anhang A). Dishes are data: a plate item carries a CompDish that points here.
    /// Recipes are generated from this at game start (RecipeBuilder). XML: Common/Defs/Kitchen/Dishes_*.xml (generated, tools/dishes).
    /// </summary>
    public class DishDef : Def
    {
        public string inspiration;                 // "Burgundy, France"
        public Cuisine cuisine = Cuisine.Frontier;
        public PlateKind plate = PlateKind.Stew;
        public List<StationKind> stations = new List<StationKind>();
        public List<StepKind> steps = new List<StepKind>();
        public List<IngredientShare> ingredients = new List<IngredientShare>();
        public float shelfLifeHours = 8f;          // 3–24 h fresh
        public int level = 1;                      // learning level 1–4 (1 = home cooking, 4 = haute cuisine)
        public int minCooking = 1;
        public string unlockHint;                  // research that the main device needs, for the cookbook ("HH_Innkeeping"), null = free
        public string fromMod;                     // "Vanilla Cooking Expanded", for extra recipes
        public bool dark;                          // † long pork: hidden unless the player's rules allow it
        public float priceFactor = 1f;             // fine tuning of the menu price

        [Unsaved] public ThingDef plateDef;
        [Unsaved] public List<RecipeDef> recipes = new List<RecipeDef>();
        /// <summary>Empty = ready. Otherwise why there is no recipe (e.g. "no ingredient of class fish loaded").</summary>
        [Unsaved] public string unavailableReason;

        /// <summary>Soft tint of the plate: hue by cuisine, a little jitter by dish, low saturation so real art keeps its look.</summary>
        public Color Tint
        {
            get
            {
                if (tintCache.a == 0f)
                {
                    float hue = ((int)cuisine * 0.083f + (Mathf.Abs(defName.GetHashCode()) % 1000) / 1000f * 0.05f) % 1f;
                    tintCache = Color.HSVToRGB(hue, 0.18f + 0.02f * level, 1f);
                    tintCache.a = 1f;
                }
                return tintCache;
            }
        }
        [Unsaved] Color tintCache;

        public StationKind MainStation => stations.Count > 0 ? stations[stations.Count - 1] : StationKind.PrepCounter;
        public bool IsAvailable => unavailableReason == null && recipes.Count > 0;
        public string Id => defName.StartsWith("HH_Dish_") ? defName.Substring(8) : defName;

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            plateDef = DefDatabase<ThingDef>.GetNamedSilentFail("HH_Plate_" + plate);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (stations.NullOrEmpty()) yield return "no stations";
            if (steps.NullOrEmpty()) yield return "no steps";
            if (ingredients.NullOrEmpty()) yield return "no ingredients";
            if (level < 1 || level > 4) yield return "level must be 1–4";
            if (shelfLifeHours < 1f || shelfLifeHours > 48f) yield return "shelfLifeHours out of range";
            if (description.NullOrEmpty()) yield return "no description";
            if (plateDef == null) yield return "plate def HH_Plate_" + plate + " not found";
            if (stations.Count > 4) yield return "more than 4 stations";
            foreach (var ing in ingredients)
                if (IngredientClasses.Find(ing.cls) == null) yield return "unknown ingredient class '" + ing.cls + "'";
        }

        public string IngredientSummary()
            => string.Join(", ", ingredients.Select(i => (IngredientClasses.Find(i.cls)?.Label ?? i.cls) + " ×" + i.amount.ToString("0.#")));

        public string StationSummary() => string.Join(" → ", stations.Select(s => Stations.LabelOf(s)));

        public string StepSummary() => string.Join(" → ", steps.Select(s => ("HH.Kitchen.Step." + s).TranslateOr(s.ToString())));

        public string CuisineLabel => ("HH.Kitchen.Cuisine." + cuisine).TranslateOr(cuisine.ToString());
    }

    /// <summary>On every generated recipe: which dish it makes and in which form (0 Everyday, 1 Fine, 2 Signature).</summary>
    public class DishRecipeExtension : DefModExtension
    {
        public DishDef dish;
        public int variant;
        public static DishRecipeExtension Of(RecipeDef r) => r?.GetModExtension<DishRecipeExtension>();
    }

    /// <summary>Marks a building as a kitchen device of a kind (our own devices carry it; other mods may add it by patch).</summary>
    public class StationExtension : DefModExtension
    {
        public StationKind kind;
        /// <summary>Research of the hospitality chapters that gates the device (defName); if it does not exist the fallback is used.</summary>
        public string researchHint;
        public string fallbackResearch;
    }
}
