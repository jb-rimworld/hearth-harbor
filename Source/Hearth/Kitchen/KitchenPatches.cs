// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>The cold patches of the kitchen: "learned?" at the recipe, "side devices there?" at the bill, quality and dish at the product, poison rule, Well fed.</summary>
    public static class KitchenRules
    {
        static RoomRoleDef roleKitchen;
        public static RoomRoleDef RoleKitchen => roleKitchen ?? (roleKitchen = DefDatabase<RoomRoleDef>.GetNamedSilentFail("Kitchen"));

        /// <summary>6.4: 0 % food poisoning when the cook has Cooking 4+, the room is a kitchen and its cleanliness is at least -0.2.</summary>
        public static bool PoisonRuleApplies(Pawn cook)
        {
            if (cook == null || !Modules.On(ModuleId.ShopsFood) || !HSettings.GetBool("kitchen.poisonRule")) return false;
            int level = cook.RaceProps.IsMechanoid ? cook.RaceProps.mechFixedSkillLevel : (cook.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0);
            if (level < 4) return false;
            var room = cook.GetRoom();
            if (room == null || room.Role != RoleKitchen) return false;
            return room.GetStat(RoomStatDefOf.Cleanliness) >= -0.2f;
        }
    }

    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    static class Patch_RecipeAvailableNow
    {
        static void Postfix(RecipeDef __instance, ref bool __result)
        {
            if (!__result || __instance.modExtensions == null) return;
            var ext = __instance.GetModExtension<DishRecipeExtension>();
            if (ext?.dish == null) return;
            if (!Modules.On(ModuleId.ShopsFood)) { __result = false; return; }
            if (ext.variant > 0 && !HSettings.GetBool("kitchen.variants")) { __result = false; return; }
            if (!DishKnowledge.IsKnown(ext.dish)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    static class Patch_BillShouldDoNow
    {
        static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (!__result || __instance.recipe.modExtensions == null) return;
            var ext = __instance.recipe.GetModExtension<DishRecipeExtension>();
            if (ext?.dish == null || !Modules.On(ModuleId.ShopsFood)) return;
            if (!(__instance.billStack?.billGiver is Thing giver) || !giver.Spawned) return;
            if (Stations.Missing(giver, ext.dish).Count > 0) __result = false;
        }
    }

    [HarmonyPatch]
    static class Patch_BillStatusString
    {
        static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(Bill_Production), "StatusString");
        static void Postfix(Bill_Production __instance, ref string __result)
        {
            if (__instance.recipe.modExtensions == null || !__result.NullOrEmpty()) return;
            var ext = __instance.recipe.GetModExtension<DishRecipeExtension>();
            if (ext?.dish == null || !Modules.On(ModuleId.ShopsFood)) return;
            if (__instance.billStack?.billGiver is Thing giver && giver.Spawned) __result = Stations.MissingText(giver, ext.dish);
        }
    }

    [HarmonyPatch(typeof(GenRecipe), "PostProcessProduct")]
    static class Patch_PostProcessProduct
    {
        static void Postfix(Thing __result, RecipeDef recipeDef, Pawn worker)
        {
            if (__result == null || recipeDef.modExtensions == null) return;
            var ext = recipeDef.GetModExtension<DishRecipeExtension>();
            if (ext?.dish == null) return;
            var comp = __result.TryGetComp<CompDish>();
            if (comp == null) return;
            Guard.Run(ModuleId.ShopsFood, "Dish.Produced", () =>
            {
                comp.SetDish(ext.dish, ext.variant);
                DishEffects.RerollQuality(__result, worker, ext.variant);
            });
        }
    }

    [HarmonyPatch(typeof(CompFoodPoisonable), nameof(CompFoodPoisonable.Notify_RecipeProduced))]
    static class Patch_PoisonRule
    {
        static bool Prefix(CompFoodPoisonable __instance, Pawn pawn)
        {
            if (!Modules.On(ModuleId.ShopsFood)) return true;
            if (__instance.parent.def.defName.StartsWith("HH_Plate_") == false) return true;
            return !KitchenRules.PoisonRuleApplies(pawn);   // false = skip the vanilla roll
        }
    }

    [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.FoodFallPerTickAssumingCategory))]
    static class Patch_WellFed
    {
        static HediffDef wellFed;
        static readonly AccessTools.FieldRef<Need, Pawn> pawnRef = AccessTools.FieldRefAccess<Need, Pawn>("pawn");
        static void Postfix(Need_Food __instance, ref float __result)
        {
            if (__result <= 0f) return;
            if (wellFed == null && (wellFed = DefDatabase<HediffDef>.GetNamedSilentFail("HH_WellFed")) == null) return;
            var set = pawnRef(__instance)?.health?.hediffSet;
            if (set == null || !set.HasHediff(wellFed)) return;
            if (!Modules.On(ModuleId.ShopsFood)) return;
            __result *= 1f - DishEffects.HungerReduction;
        }
    }
}
