// SPDX-License-Identifier: MIT
using RimWorld;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>What a venue type sells from the stockpiles and shelves inside its room (N45).</summary>
    public enum SellKind { None, Goods, Drinks }

    /// <summary>How strictly venues need staff. PerVenueType follows VenueTypeDef.needsStaff.</summary>
    public enum StaffRule { PerVenueType, NeverRequired, AlwaysRequired }

    /// <summary>Why a venue can or cannot serve right now.</summary>
    public enum VenueGate
    {
        /// <summary>Open and (if needed) staffed.</summary>
        Open,
        /// <summary>Outside opening hours.</summary>
        ClosedHours,
        /// <summary>In opening hours but nobody is tending it.</summary>
        WaitingForStaff,
    }

    /// <summary>XML extension on a VenueTypeDef: what the venue sells (N45).</summary>
    public class VenueSaleExtension : DefModExtension
    {
        public SellKind sells = SellKind.None;
    }

    [DefOf]
    public static class HHVenueDefOf
    {
        public static JobDef HH_TendVenue;
        public static JobDef HH_BuyGoods;
        public static JobDef HH_BuyDrink;
        public static JobDef HH_WaitAtVenue;
        public static ThingDef HH_VenueSign;
        public static ThingDef HH_VenueSignWall;
        public static ThingDef HH_ShopCounter;
        public static ThingDef HH_VendingMachine;
        public static ThingDef HH_MarketStall;
        public static WorkTypeDef HH_Serving;
        public static VenueTypeDef HH_Venue_Tavern;
        public static VenueTypeDef HH_Venue_Shop;
        public static VenueTypeDef HH_Venue_Restaurant;
        public static VenueTypeDef HH_Venue_Casino;
        public static VenueTypeDef HH_Venue_Spa;
        public static VenueTypeDef HH_Venue_Lounge;

        static HHVenueDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(HHVenueDefOf)); }
    }

    /// <summary>
    /// A vanilla building that one of our buildings replaces (N49). The vanilla def disappears from the architect menu
    /// once the replacement is available (researched); buildings already placed stay. Other modules add pairs in XML
    /// (&lt;Hearth.ShopsFood.VanillaReplacementDef&gt;) or in code with VanillaHiding.Register.
    /// </summary>
    public class VanillaReplacementDef : Def
    {
        /// <summary>defName of the vanilla ThingDef to hide. A def that does not exist (missing DLC) is skipped silently.</summary>
        public string vanillaDefName;
        /// <summary>defName of our replacement ThingDef.</summary>
        public string replacementDefName;
    }
}
