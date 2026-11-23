// SPDX-License-Identifier: MIT
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// Special switch core.neverDie: a guest whose injuries or blood loss would be lethal stays alive instead (downed). Only guests are touched,
    /// never colonists, prisoners or hostile pawns; the check is a field read plus a dictionary lookup, and it only runs when vanilla says "dead".
    /// </summary>
    [HarmonyPatch]
    static class Patch_HealthTracker_ShouldBeDead
    {
        public const string Switch = "core.neverDie";
        static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> pawnOf = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        static MethodBase TargetMethod() => AccessTools.Method(typeof(Pawn_HealthTracker), "ShouldBeDead");

        /// <summary>True when the pawn is a guest that the switch protects.</summary>
        public static bool Protects(Pawn p) => p != null && !p.Dead && CoreSettings.Switch(Switch) && Modules.On(ModuleId.Inn) && !p.IsPrisoner && !p.IsColonist
            && p.Faction != null && !p.Faction.IsPlayer && !p.HostileTo(Faction.OfPlayer) && GuestApi.IsGuest(p);

        static void Postfix(Pawn_HealthTracker __instance, ref bool __result)
        {
            if (!__result) return;
            if (Protects(pawnOf(__instance))) __result = false;
        }
    }

    /// <summary>The same protection for the direct kill path (vanilla kills a pawn from several places, not only from the health tracker).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class Patch_Pawn_Kill_NeverDie
    {
        static bool Prefix(Pawn __instance) => !Patch_HealthTracker_ShouldBeDead.Protects(__instance);
    }
}
