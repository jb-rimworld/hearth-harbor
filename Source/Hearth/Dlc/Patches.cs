// SPDX-License-Identifier: MIT
// Harmony patches of the traits and genes (KONZEPT 11.1/11.2). Each one returns at once when it is not our business.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Dlc
{
    /// <summary>Our traits appear on new pawns at vanilla-like rarity; "traits of a module that is off stay hidden" (3.4): rolled only while the serving module is on.</summary>
    [HarmonyPatch(typeof(TraitDef), nameof(TraitDef.GetGenderSpecificCommonality))]
    public static class Patch_TraitCommonality
    {
        static readonly Dictionary<string, ModuleId> modules = new Dictionary<string, ModuleId>
        {
            { "HH_Lucky", ModuleId.Casino }, { "HH_Unlucky", ModuleId.Casino }, { "HH_GamblingAverse", ModuleId.Casino }, { "HH_Gambler", ModuleId.Casino },
            { "HH_PennyPincher", ModuleId.Bank },
            { "HH_BornHost", ModuleId.Inn }, { "HH_Grump", ModuleId.Inn }, { "HH_SilverTongue", ModuleId.Inn }, { "HH_EagleEye", ModuleId.Inn }, { "HH_SteadyHands", ModuleId.Inn },
            { "HH_Caregiver", ModuleId.Inn }, { "HH_Peacemaker", ModuleId.Inn }, { "HH_Gossip", ModuleId.Inn }, { "HH_KidMagnet", ModuleId.Inn },
        };
        static void Postfix(TraitDef __instance, ref float __result)
        {
            if (__result <= 0f || __instance.defName.Length < 4 || __instance.defName[0] != 'H' || __instance.defName[1] != 'H' || __instance.defName[2] != '_') return;
            if (!modules.TryGetValue(__instance.defName, out var m)) return;
            if (!Modules.On(m) || !HSettings.GetBool("dlc.traitSpawn")) { __result = 0f; return; }
            __result *= HSettings.Get("dlc.traitRarity");
        }
    }

    /// <summary>Hosting experience multiplier (born host x1.5, hospitable x1.25): only social experience gained while working at Hosting.</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_HostingLearn
    {
        static readonly AccessTools.FieldRef<SkillRecord, Pawn> PawnRef = AccessTools.FieldRefAccess<SkillRecord, Pawn>("pawn");
        static void Prefix(SkillRecord __instance, ref float xp)
        {
            if (xp <= 0f || __instance.def != SkillDefOf.Social) return;
            var p = PawnRef(__instance);
            var wt = p?.CurJob?.workGiverDef?.workType; if (wt == null || wt != DlcDefs.Hosting) return;
            if (!Modules.On(ModuleId.Inn)) return;
            var s = DlcDefs.HostingLearn; if (s == null) return;
            xp *= p.GetStatValue(s);
        }
    }

    /// <summary>Tireless smile: rest drains slower while serving (x0.7).</summary>
    [HarmonyPatch(typeof(Need_Rest), nameof(Need_Rest.NeedInterval))]
    public static class Patch_ServeRest
    {
        static readonly AccessTools.FieldRef<Need, Pawn> PawnRef = AccessTools.FieldRefAccess<Need, Pawn>("pawn");
        static void Prefix(Need_Rest __instance, out float __state)
        {
            __state = -1f;
            var p = PawnRef(__instance);
            var wt = p?.CurJob?.workGiverDef?.workType; if (wt == null || wt != DlcDefs.Serving) return;
            if (DlcApi.ServeRestFactor(p) >= 0.999f) return;
            __state = __instance.CurLevel;
        }
        static void Postfix(Need_Rest __instance, float __state)
        {
            if (__state < 0f) return;
            var p = PawnRef(__instance); float drop = __state - __instance.CurLevel;
            if (drop > 0f) __instance.CurLevel += drop * (1f - DlcApi.ServeRestFactor(p));
        }
    }

    /// <summary>Service quality: a friendly host raises a guest's satisfaction a little with every successful chat, a grump lowers it. Throttled per guest.</summary>
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class Patch_ServiceQualityChat
    {
        static readonly AccessTools.FieldRef<Pawn_InteractionsTracker, Pawn> PawnRef = AccessTools.FieldRefAccess<Pawn_InteractionsTracker, Pawn>("pawn");
        static readonly Dictionary<int, int> last = new Dictionary<int, int>();
        public const int Cooldown = 2500;
        static void Postfix(Pawn_InteractionsTracker __instance, bool __result, Pawn recipient)
        {
            if (!__result || recipient == null || !Modules.On(ModuleId.Inn)) return;
            var host = PawnRef(__instance);
            if (host == null || host.Faction != Faction.OfPlayer || !host.RaceProps.Humanlike || host.IsPrisoner) return;
            float q = DlcApi.ServiceQuality(host);
            if (Math.Abs(q) < 0.001f || !GuestApi.IsGuest(recipient)) return;
            int now = Find.TickManager.TicksGame;
            if (last.TryGetValue(recipient.thingIDNumber, out int t) && now - t < Cooldown) return;
            last[recipient.thingIDNumber] = now;
            GuestApi.AddSatisfaction(recipient, q * 0.2f, q > 0 ? "Pleasant host" : "Surly host");
        }
    }
}
