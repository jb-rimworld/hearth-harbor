// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>
    /// Kitchen, dishes and restaurant (part of Shops &amp; Food, KONZEPT 6.4). Ten new kitchen devices, 12 plate items carrying about 100 dishes,
    /// recipes that are learned, a Well fed state, and a restaurant loop for guests.
    /// </summary>
    public sealed class KitchenModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.ShopsFood;
        const string Cat = "Kitchen";

        public override void RegisterSettings()
        {
            var m = ModuleId.ShopsFood;
            HSettings.Percent(m, Cat, "kitchen.shelfLife", 1f, 0.25f, 3f, "Dish shelf life", "How long plates stay fresh, relative to the 3 to 24 hours of each dish. Stale plates are still edible but give no Well fed and no mood bonus.", hardMin: 0.05f, hardMax: 20f);
            HSettings.Percent(m, Cat, "kitchen.wellFedStrength", 1f, 0f, 1.5f, "Well fed strength", "How much slower hunger falls after a proper dish (100 % = 40 % slower).", hardMin: 0f, hardMax: 2.2f);
            HSettings.Percent(m, Cat, "kitchen.wellFedDuration", 1f, 0.25f, 3f, "Well fed duration", "How long the Well fed state lasts, relative to 4 / 6 / 8 / 10 hours by learning level (and by quality).", hardMin: 0f, hardMax: 20f);
            HSettings.Percent(m, Cat, "kitchen.menuPrice", 1f, 0.3f, 3f, "Menu price", "Prices a restaurant asks for dishes, relative to the standard menu (dish level, quality and form).", hardMin: 0.05f, hardMax: 50f);
            HSettings.Bool(m, Cat, "kitchen.poisonRule", true, "No food poisoning from a good kitchen", "Plates cooked by a cook with Cooking 4 or more in a clean kitchen (cleanliness -0.2 or better) can never poison. Off: vanilla rolls apply.");
            HSettings.Bool(m, Cat, "kitchen.variants", true, "Fine and Signature forms", "Offer each dish in a Fine form (more and better ingredients, a better chance for good quality) and a Signature form (research needed).");
            HSettings.Bool(m, Cat, "kitchen.allKnown", false, "All dishes known", "Skip learning: every dish of the menu can be cooked as soon as its devices stand.");
            HSettings.Bool(m, Cat, "kitchen.darkMenu", false, "Dark menu (long pork)", "Allow the long pork dishes. They are also unlocked by an ideoligion that approves of cannibalism.");
        }

        public override void OnDefsLoaded()
        {
            IngredientClasses.Resolve();
            Stations.AssignResearch();
            RecipeBuilder.BuildAll();
            GuestActivities.Register(new RestaurantActivity());
            CookbookPage.Register();
            HLog.Info(Cat, $"Kitchen ready: {DefDatabase<DishDef>.DefCount} dishes, {RecipeBuilder.Built} recipes");
        }

        public override void OnGameStarted() { Guard.Run(Id, "Kitchen.FreeResearch", DishKnowledge.EnsureFreeResearch); }
        public override void OnGameLoaded() { Guard.Run(Id, "Kitchen.FreeResearch", DishKnowledge.EnsureFreeResearch); Stations.ClearCache(); Restaurant.ClearCache(); }

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % 250 == 0) Restaurant.Expire(ticksGame);
        }

        public override void PrepareRemoval(List<string> report)
        {
            // Plates keep working as ordinary food: the dish comp loads without the mod as an unknown comp and is dropped. Nothing to undo.
            report.Add("Kitchen: plates and recipes stay as vanilla-like food; bills for dishes disappear.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            yield return $"Kitchen: {DefDatabase<DishDef>.AllDefs.Count(d => d.IsAvailable)} of {DefDatabase<DishDef>.DefCount} dishes available, {RecipeBuilder.Built} recipes, restaurant sales {Restaurant.TotalSales} ({Restaurant.TotalTakings} silver), unpaid meals {Restaurant.UnpaidMeals}";
        }
    }
}
