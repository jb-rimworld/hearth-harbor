// SPDX-License-Identifier: MIT
using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    /// <summary>Card payments of digital guests (N42): money sits in the ledger account guest:&lt;thingID&gt;.</summary>
    public sealed class InnCardProvider : IPaymentProvider
    {
        public int Priority => 50;
        public string Id => "inn.card";
        public PaymentMethod Method => PaymentMethod.Card;

        public Money Available(Pawn payer)
        {
            var r = GuestRegistry.Instance?.Get(payer);
            if (r == null || !r.PaysDigital) return Money.Zero;
            return Ledger.Current?.Balance(r.Account) ?? Money.Zero;
        }

        public bool TryPay(Pawn payer, Money amount, string toAccount, string memo)
        {
            var r = GuestRegistry.Instance?.Get(payer);
            if (r == null || !r.PaysDigital) return false;
            return Ledger.Current != null && Ledger.Current.Post(r.Account, toAccount, amount, memo + " (card)");
        }
    }

    /// <summary>Colonists never use guest beds; Inn guests only use guest beds (5.11).</summary>
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.IsValidBedFor))]
    static class Patch_RestUtility_IsValidBedFor
    {
        static void Postfix(Thing bedThing, Pawn sleeper, ref bool __result)
        {
            if (!Modules.On(ModuleId.Inn)) return;
            bool isGuest = GuestRegistry.IsRegisteredGuest(sleeper);
            if (!isGuest && GuestBeds.TotalCount == 0) return;
            bool guestBed = GuestBeds.IsGuestBed(bedThing);
            if (isGuest && bedThing is Building_Bed bb && bb.Medical) return; // rescued guests may use medical beds
            if (isGuest) __result = guestBed && (GuestRegistry.Instance?.Get(sleeper)?.Bed == bedThing) && RestUtility.CanUseBedNow(bedThing, sleeper, false);
            else if (guestBed) __result = false;
        }
    }

    /// <summary>The assign-owner dialog refuses colonists on guest beds.</summary>
    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.CanAssignTo))]
    static class Patch_CompAssignableToPawn_Bed_CanAssignTo
    {
        static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            if (!Modules.On(ModuleId.Inn) || GuestBeds.TotalCount == 0 || !__result.Accepted) return;
            if (GuestBeds.IsGuestBed(__instance.parent) && !GuestRegistry.IsRegisteredGuest(pawn))
                __result = "HH.Inn.GuestBed.NoColonists".TranslateOr("This is a guest bed.");
        }
    }

    /// <summary>Guests stay inside their wing while staying (5.8). Cheap early-out: only registered, checked-in guests.</summary>
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.InAllowedArea))]
    static class Patch_ForbidUtility_InAllowedArea
    {
        static void Postfix(IntVec3 c, Pawn forPawn, ref bool __result)
        {
            if (!__result || !Modules.On(ModuleId.Inn)) return;
            if (!GuestRegistry.IsWingRestricted(forPawn)) return;
            if (!GuestWings.AllowedFor(forPawn, c)) __result = false;
        }
    }

    /// <summary>Guests get the joy need (and with it, recreation) while they stay; removed again when the file closes.</summary>
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    static class Patch_Pawn_NeedsTracker_ShouldHaveNeed
    {
        static void Postfix(Pawn_NeedsTracker __instance, NeedDef nd, ref bool __result, Pawn ___pawn)
        {
            if (__result || nd?.defName != "Joy") return;
            if (!Modules.On(ModuleId.Inn) || !GuestRegistry.IsRegisteredGuest(___pawn)) return;
            if (___pawn.RaceProps.Humanlike && ___pawn.DevelopmentalStage != DevelopmentalStage.Baby) __result = true;
        }
    }
}
