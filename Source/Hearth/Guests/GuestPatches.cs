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

    /// <summary>
    /// Colonists never use guest beds; Inn guests only use the guest bed booked for them (5.11). A vanilla "yes" is only ever narrowed.
    /// The one vanilla "no" we overrule is the bed-faction check (bed of the colony, traveller of another faction): for a guest walking to
    /// their own booked bed every other vanilla condition is checked again here, once, behind a re-entry guard (no recursion through mods
    /// that call IsValidBedFor from CanUseBedNow).
    /// </summary>
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.IsValidBedFor))]
    static class Patch_RestUtility_IsValidBedFor
    {
        [ThreadStatic] static bool inside;

        static void Postfix(Thing bedThing, Pawn sleeper, Pawn traveler, bool checkSocialProperness, bool allowMedBedEvenIfSetToNoCare,
            bool ignoreOtherReservations, GuestStatus? guestStatus, ref bool __result)
        {
            if (inside || sleeper == null || bedThing == null || !Modules.On(ModuleId.Inn)) return;
            bool isGuest = GuestRegistry.IsRegisteredGuest(sleeper);
            if (!isGuest && GuestBeds.TotalCount == 0) return;
            bool guestBed = GuestBeds.IsGuestBed(bedThing);
            if (!isGuest) { if (guestBed) __result = false; return; }
            if (bedThing is Building_Bed bb && bb.Medical) return; // rescued guests may use medical beds (vanilla decides)
            bool booked = guestBed && GuestRegistry.Instance?.Get(sleeper)?.Bed == bedThing;
            if (!booked) { __result = false; return; }
            if (__result) return;
            __result = BookedBedUsable((Building_Bed)bedThing, sleeper, traveler ?? sleeper, checkSocialProperness, allowMedBedEvenIfSetToNoCare, ignoreOtherReservations, guestStatus);
        }

        /// <summary>Vanilla's IsValidBedFor without the bed-faction check.</summary>
        internal static bool BookedBedUsable(Building_Bed bed, Pawn sleeper, Pawn traveler, bool social, bool allowMed, bool ignoreRes, GuestStatus? status)
        {
            inside = true;
            try
            {
                if (!RestUtility.CanUseBedNow(bed, sleeper, social, allowMed, status)) return false;
                if (!traveler.CanReach(bed, PathEndMode.OnCell, Danger.Some)) return false;
                if (!sleeper.HasReserved(bed) && !traveler.CanReserve(bed, bed.SleepingSlotsCount, 0, null, ignoreRes)) return false;
                if (bed.IsForbidden(traveler)) return false;
                return true;
            }
            finally { inside = false; }
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

    /// <summary>
    /// Guests stay inside their wing while staying (5.8). Hot path (every cell of every job search of a guest): colonists leave after one
    /// HashSet lookup, guests pay two array reads (wing grid, venue-room grid of the map), see <see cref="GuestWings.AllowedFor"/>.
    /// </summary>
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.InAllowedArea))]
    static class Patch_ForbidUtility_InAllowedArea
    {
        static void Postfix(IntVec3 c, Pawn forPawn, ref bool __result)
        {
            if (!__result || !GuestRegistry.IsWingRestricted(forPawn) || !Modules.On(ModuleId.Inn)) return;
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
