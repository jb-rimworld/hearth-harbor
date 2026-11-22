// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>Own building destroyed by damage → property claim. Cheap early-out: only Buildings, only with the Bank module on.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Kill))]
    static class Patch_Thing_Kill
    {
        static void Prefix(Thing __instance, DamageInfo? dinfo)
        {
            if (!(__instance is Building b) || !dinfo.HasValue || b.Faction == null || b.Faction != Faction.OfPlayerSilentFail || !Modules.On(ModuleId.Bank)) return;
            Guard.Run(ModuleId.Bank, "Insurance.BuildingKill", () => ClawbackApi.BuildingDestroyed(b, dinfo));
        }
    }

    /// <summary>Colonist death → accident / travel death claim.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class Patch_Pawn_Kill
    {
        static void Prefix(Pawn __instance, out bool __state)
        {
            __state = false;
            if (__instance.Faction == null || __instance.Faction != Faction.OfPlayerSilentFail || !Modules.On(ModuleId.Bank)) return;
            try { __state = !__instance.Spawned && RimWorld.Planet.CaravanUtility.IsCaravanMember(__instance); } catch { }
        }
        static void Postfix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit, bool __state)
        {
            if (!__instance.Dead || __instance.Faction == null || __instance.Faction != Faction.OfPlayerSilentFail || !Modules.On(ModuleId.Bank)) return;
            Guard.Run(ModuleId.Bank, "Insurance.PawnKill", () => ClawbackApi.PawnDied(__instance, dinfo, exactCulprit, __state));
        }
    }

    /// <summary>Lost body parts (accident cover) and caravan injuries (travel cover).</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    static class Patch_AddHediff
    {
        static void Postfix(Pawn_HealthTracker __instance, Pawn ___pawn, Hediff hediff, DamageInfo? dinfo)
        {
            if (___pawn == null || ___pawn.Faction == null || ___pawn.Faction != Faction.OfPlayerSilentFail) return;
            if (hediff is Hediff_MissingPart mp)
            {
                if (!Modules.On(ModuleId.Bank)) return;
                Guard.Run(ModuleId.Bank, "Insurance.PartLost", () => ClawbackApi.BodyPartLost(___pawn, mp, dinfo));
            }
            else if (hediff is Hediff_Injury inj && !___pawn.Spawned)
            {
                if (!Modules.On(ModuleId.Bank)) return;
                Guard.Run(ModuleId.Bank, "Insurance.CaravanInjury", () => ClawbackApi.CaravanInjury(___pawn, inj));
            }
        }
    }

    /// <summary>Cold snap / toxic fallout → crop cover.</summary>
    [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.RegisterCondition))]
    static class Patch_RegisterCondition
    {
        static void Postfix(GameCondition cond)
        {
            if (!Modules.On(ModuleId.Bank)) return;
            Guard.Run(ModuleId.Bank, "Insurance.Condition", () => ClawbackApi.ConditionStarted(cond));
        }
    }
}
