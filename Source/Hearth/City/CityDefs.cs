// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.City
{
    /// <summary>Lazy, null-tolerant references to our defs (no [DefOf]: a missing def must never be a red error).</summary>
    public static class CityDefs
    {
        static ThingDef screen, chair, bench, stand, cabIn, cabOut;
        static JobDef watch, salonSit, salonStyle, salonSelf, gallery, park, taxi, guard;
        static ResearchProjectDef r1, r2;
        static VenueTypeDef vCinema, vLounge, vSalon, vGallery;

        public static ThingDef ProjectorScreen => screen ?? (screen = DefDatabase<ThingDef>.GetNamedSilentFail("HH_ProjectorScreen"));
        public static ThingDef SalonChair => chair ?? (chair = DefDatabase<ThingDef>.GetNamedSilentFail("HH_SalonChair"));
        public static ThingDef ParkBench => bench ?? (bench = DefDatabase<ThingDef>.GetNamedSilentFail("HH_ParkBench"));
        public static ThingDef TaxiStand => stand ?? (stand = DefDatabase<ThingDef>.GetNamedSilentFail("HH_TaxiStand"));
        public static ThingDef TaxiIncoming => cabIn ?? (cabIn = DefDatabase<ThingDef>.GetNamedSilentFail("HH_TaxiIncoming"));
        public static ThingDef TaxiLeaving => cabOut ?? (cabOut = DefDatabase<ThingDef>.GetNamedSilentFail("HH_TaxiLeaving"));

        public static JobDef WatchScreening => watch ?? (watch = DefDatabase<JobDef>.GetNamedSilentFail("HH_WatchScreening"));
        public static JobDef SalonSit => salonSit ?? (salonSit = DefDatabase<JobDef>.GetNamedSilentFail("HH_SalonSit"));
        public static JobDef SalonStyle => salonStyle ?? (salonStyle = DefDatabase<JobDef>.GetNamedSilentFail("HH_SalonStyle"));
        public static JobDef SalonSelf => salonSelf ?? (salonSelf = DefDatabase<JobDef>.GetNamedSilentFail("HH_SalonSelf"));
        public static JobDef GalleryVisit => gallery ?? (gallery = DefDatabase<JobDef>.GetNamedSilentFail("HH_GalleryVisit"));
        public static JobDef ParkRest => park ?? (park = DefDatabase<JobDef>.GetNamedSilentFail("HH_ParkRest"));
        public static JobDef TaxiDeparture => taxi ?? (taxi = DefDatabase<JobDef>.GetNamedSilentFail("HH_TaxiDeparture"));
        public static JobDef Bodyguard => guard ?? (guard = DefDatabase<JobDef>.GetNamedSilentFail("HH_Bodyguard"));

        public static ResearchProjectDef Research1 => r1 ?? (r1 = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_CityLife_1"));
        public static ResearchProjectDef Research2 => r2 ?? (r2 = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_CityLife_2"));

        public static VenueTypeDef VenueCinema => vCinema ?? (vCinema = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Cinema"));
        /// <summary>Owned by the Venues module; a Lounge with a projector screen also works as a cinema.</summary>
        public static VenueTypeDef VenueLounge => vLounge ?? (vLounge = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Lounge"));
        public static VenueTypeDef VenueSalon => vSalon ?? (vSalon = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Salon"));
        public static VenueTypeDef VenueGallery => vGallery ?? (vGallery = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Gallery"));

        public static ThoughtDef Thought(string defName) => DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
        public static bool IsCinemaType(VenueTypeDef t) => t != null && (t == VenueCinema || t == VenueLounge);
    }
}
