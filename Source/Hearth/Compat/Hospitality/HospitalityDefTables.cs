// SPDX-License-Identifier: MIT
// Def names of Hospitality and its add-ons (1.7 builds), used only to recognise them in old save games.
// Generated from the mods' Defs folders (names only, vanilla names excluded); see docs/hospitality-shim.md.
using System.Collections.Generic;

namespace Hearth.Compat.Hospitality
{
    public static class HospitalityDefTables
    {
        /// <summary>Memories of guests and colonists about Hospitality services. No counterpart: dropped silently.</summary>
        public static readonly string[] Thoughts =
        {
            "CheapSpa", "EndorsedByRecruiter", "ExpensiveSpa", "Gastronomy_BoughtFood", "Gastronomy_HadToWait", "Gastronomy_Serviced", "Gastronomy_ServicedMood",
            "GenerousSlotmachine", "GuestAngered", "GuestBedCount", "GuestCantAffordBed", "GuestCheapFood", "GuestCheapVendingmachine", "GuestClaimedBed",
            "GuestDismissiveAttitude", "GuestExpensiveFood", "GuestExpensiveVendingmachine", "GuestFreeVendingmachine", "GuestHasNoFood", "GuestOffendedRelationship",
            "GuestPleasedRelationship", "GuestRecruitmentForced", "HC_WonSlotMachineGame", "RiggedSlotmachine", "RoomService_Accepted", "RoomService_Declined",
            "RoomService_DislikesIt", "RoomService_ExperienceThought", "RoomService_ForcedCompanionship", "RoomService_FreeTreatment", "RoomService_GotPaid",
            "RoomService_LovesIt", "RoomService_MealDelivered", "RoomService_PaidForTreatment", "Storefront_HadToWait", "Storefront_Serviced", "Storefront_ServicedMood",
        };

        public static readonly string[] HistoryEvents = { "VisitBetterThanExpected", "VisitWorseThanExpected" };
        public static readonly string[] Hediffs = { "RoomService_Experience" };
        public static readonly string[] TraderKinds = { "Guest" };
        public static readonly string[] JoyKinds = { "Gamble", "Shopping" };

        /// <summary>Jobs: replaced by vanilla "Wait"; the driver stub ends them at once after loading (running jobs are dropped, 15.7).</summary>
        public static readonly string[] Jobs =
        {
            "BrowseItems", "BuyItem", "CashRegister_EmptyRegister", "CharmGuest", "ClaimGuestBed", "Gastronomy_Dine", "Gastronomy_MakeTable", "Gastronomy_Serve",
            "Gastronomy_StandBy", "Gastronomy_TakeOrder", "GuestImproveRelationship", "HospitalityCasino_EmptyVendingMachine", "HospitalitySpa_EmptyVendingMachine",
            "InviteToStay_InvitePawn", "Play_SlotMachine", "RoomService_DeliverMeal", "RoomService_SolicitGuest", "ScroungeFood", "Storefront_BrowseItems",
            "Storefront_BuyItem", "Storefront_Sell", "Storefront_StandBy", "SwipeFood", "VendingMachine_EmptyVendingMachine", "VendingMachines_BuyItem",
            "VendingMachines_EmptyVendingMachine",
        };

        public static readonly string[] WorkGivers =
        {
            "CashRegister_EmptyRegister", "Diplomat", "Gastronomy_MakeTable", "Gastronomy_Serve", "Gastronomy_StandBy", "Gastronomy_TakeOrder",
            "HospitalityCasino_WithdrawVendingMachineEarnings", "HospitalitySpa_WithdrawVendingMachineEarnings", "Hospitality_WithdrawVendingMachineEarnings",
            "Recruiter", "RoomService_DeliverMeal", "RoomService_Solicit", "Storefront_Sell", "Storefront_StandBy", "VendingMachines_WithdrawVendingMachineEarnings",
        };

        public static readonly string[] Interactions = { "CharmGuestAttempt", "GuestDiplomacy", "RoomService_SolicitAttempt", "ScroungeFoodAttempt" };
        public static readonly string[] Incidents = { "CasinoVisitorGroup", "HappyGuestJoins", "VisitorGroupMax", "VisitorGroupSelectFaction", "VisitorGroupSpacerCruise", "VisitorGroupSpacerLuxury" };
        public static readonly string[] Duties = { "CasinoVisitor", "Relax" };
        public static readonly string[] Letters = { "HappyGuestJoins" };

        /// <summary>Work types → our shared work types (KONZEPT 15.7: Diplomat/Entertain → Hosting).</summary>
        public static readonly Dictionary<string, string> WorkTypes = new Dictionary<string, string>
        {
            { "Diplomat", "HH_Hosting" }, { "Gastronomy_Waiting", "HH_Serving" }, { "Storefront_Selling", "HH_Serving" },
        };

        static HashSet<string> jobSet, interactionSet, incidentSet, dutySet, letterSet;
        static HashSet<string> Set(ref HashSet<string> s, string[] a) => s ?? (s = new HashSet<string>(a));
        public static bool IsJob(string n) => Set(ref jobSet, Jobs).Contains(n);
        public static bool IsInteraction(string n) => Set(ref interactionSet, Interactions).Contains(n);
        public static bool IsIncident(string n) => Set(ref incidentSet, Incidents).Contains(n);
        public static bool IsDuty(string n) => Set(ref dutySet, Duties).Contains(n);
        public static bool IsLetter(string n) => Set(ref letterSet, Letters).Contains(n);
    }
}
