// SPDX-License-Identifier: MIT
using System;
using Verse;

namespace Hearth.City
{
    public enum CityResearchMode { Full = 0, Off = 1 }

    /// <summary>All City Life settings in one place: keys, registration and typed readers. Key prefix: city.</summary>
    public static class CitySettings
    {
        public const string ResearchMode = "city.researchMode";
        public const string PriceTolerance = "city.priceTolerance";
        public const string DesireScale = "city.desireScale";
        public const string CinemaTicket = "city.cinemaTicket";
        public const string CinemaShowTicks = "city.cinemaShowTicks";
        public const string CinemaMinSeats = "city.cinemaMinSeats";
        public const string CinemaCooldownHours = "city.cinemaCooldownHours";
        public const string MovieNight = "city.movieNight";
        public const string MovieNightChance = "city.movieNightChance";
        public const string SalonPrice = "city.salonPrice";
        public const string SalonDyePrice = "city.salonDyePrice";
        public const string SalonTattooPrice = "city.salonTattooPrice";
        public const string SalonServiceTicks = "city.salonServiceTicks";
        public const string SalonWaitTicks = "city.salonWaitTicks";
        public const string SalonCooldownHours = "city.salonCooldownHours";
        public const string GalleryFee = "city.galleryFee";
        public const string GalleryMinArt = "city.galleryMinArt";
        public const string GalleryMaxViews = "city.galleryMaxViews";
        public const string GalleryViewTicks = "city.galleryViewTicks";
        public const string GalleryCooldownHours = "city.galleryCooldownHours";
        public const string ParkRestTicks = "city.parkRestTicks";
        public const string ParkCooldownHours = "city.parkCooldownHours";
        public const string TaxiFare = "city.taxiFare";
        public const string TaxiDepartureChance = "city.taxiDepartureChance";
        public const string TaxiArrivalChance = "city.taxiArrivalChance";
        public const string TaxiArrivalFee = "city.taxiArrivalFee";
        public const string TaxiWaitTicks = "city.taxiWaitTicks";
        public const string SecurityOn = "city.securityOn";
        public const string SecurityMinWealth = "city.securityMinWealth";
        public const string SecurityChance = "city.securityChance";
        public const string SecurityDayShare = "city.securityDayShare";
        public const string SecurityMinHourFee = "city.securityMinHourFee";
        public const string SecurityHireHours = "city.securityHireHours";

        const ModuleId M = ModuleId.CityLife;

        public static void Register()
        {
            // Tier anchors from KONZEPT 9.2 (Comped … Why?): price tolerance 3.0 … 0.65.
            HSettings.Float(M, "General", PriceTolerance, 1f, 0.5f, 3f, "Guest price tolerance",
                "How much guests accept to pay for city services compared with the reference price. Follows the difficulty slider; 1 = standard.",
                hardMin: 0.2f, hardMax: 10f, tiers: new[] { 3.0f, 1.3f, 1.15f, 1.0f, 0.9f, 0.8f, 0.65f });
            HSettings.Float(M, "General", DesireScale, 1f, 0.2f, 3f, "Guest interest in city life",
                "Scales how strongly guests want to go to the cinema, salon, gallery and park bench. 0 = never.", nerd: true, hardMin: 0f, hardMax: 10f);
            HSettings.Enum(M, "Research", ResearchMode, CityResearchMode.Full, "Research mode",
                "Full: the City Life projects must be researched. Off: everything in City Life is available right away.");

            HSettings.Silver(M, "Cinema", CinemaTicket, 12, 0, 80, "Cinema ticket", "Price of one ticket. Guests pay when they sit down.");
            HSettings.Int(M, "Cinema", CinemaShowTicks, 2000, 600, 6000, "Film length (ticks)", "How long one screening runs. 2,500 ticks are one hour.", hardMin: 300, hardMax: 20000);
            HSettings.Int(M, "Cinema", CinemaMinSeats, 4, 1, 16, "Seats needed", "A projector screen counts as a cinema when at least this many seats face it.", hardMin: 1, hardMax: 64);
            HSettings.Int(M, "Cinema", CinemaCooldownHours, 4, 0, 24, "Hours between films per guest", "A guest sees at most one film in this time.", hardMin: 0, hardMax: 240);
            HSettings.Bool(M, "Cinema", MovieNight, true, "Movie nights for colonists", "Colonists organise a movie night now and then when a powered projector screen with seats exists.");
            HSettings.Percent(M, "Cinema", MovieNightChance, 0.4f, 0f, 1f, "Movie night chance per evening", "Chance that the colony starts a movie night on a given evening (18:00).");

            HSettings.Silver(M, "Salon", SalonPrice, 25, 0, 150, "Haircut price", "What a guest pays for a haircut and a new style.");
            HSettings.Silver(M, "Salon", SalonDyePrice, 10, 0, 100, "Dye surcharge", "Extra for a hair colour change (needs Biotech or Ideology).");
            HSettings.Silver(M, "Salon", SalonTattooPrice, 20, 0, 150, "Tattoo surcharge", "Extra for a tattoo (needs Ideology).");
            HSettings.Int(M, "Salon", SalonServiceTicks, 420, 120, 1500, "Styling time (ticks)", "How long a haircut takes with an average stylist.", hardMin: 60, hardMax: 6000);
            HSettings.Int(M, "Salon", SalonWaitTicks, 1800, 300, 6000, "Waiting limit (ticks)", "A guest waits this long in the chair for someone to turn up, then leaves unhappy.", nerd: true, hardMin: 120, hardMax: 20000);
            HSettings.Int(M, "Salon", SalonCooldownHours, 16, 0, 72, "Hours between haircuts per guest", "A guest gets at most one new look in this time.", hardMin: 0, hardMax: 600);

            HSettings.Silver(M, "Gallery", GalleryFee, 10, 0, 80, "Gallery entry fee", "Price of walking through the gallery once.");
            HSettings.Int(M, "Gallery", GalleryMinArt, 4, 1, 12, "Artworks needed", "A room counts as a gallery when it holds at least this many artworks.", hardMin: 1, hardMax: 60);
            HSettings.Int(M, "Gallery", GalleryMaxViews, 5, 1, 12, "Pieces per visit", "How many artworks one guest looks at per visit.", hardMin: 1, hardMax: 30);
            HSettings.Int(M, "Gallery", GalleryViewTicks, 240, 60, 900, "Looking time per piece (ticks)", "How long a guest stands in front of one artwork.", nerd: true, hardMin: 30, hardMax: 3000);
            HSettings.Int(M, "Gallery", GalleryCooldownHours, 6, 0, 48, "Hours between visits per guest", "A guest goes through a gallery at most once in this time.", hardMin: 0, hardMax: 480);

            HSettings.Int(M, "Park", ParkRestTicks, 900, 300, 3000, "Bench rest time (ticks)", "How long somebody sits on a park bench.", hardMin: 120, hardMax: 10000);
            HSettings.Int(M, "Park", ParkCooldownHours, 2, 0, 24, "Hours between bench visits per guest", "A guest sits on a bench at most once in this time.", hardMin: 0, hardMax: 240);

            HSettings.Silver(M, "Taxi", TaxiFare, 15, 0, 120, "Taxi fare", "What a departing guest pays for the ride from the taxi stand.");
            HSettings.Percent(M, "Taxi", TaxiDepartureChance, 0.45f, 0f, 1f, "Chance to take a taxi when leaving", "Share of departing guests who call a taxi when a free taxi stand exists (rises with wealth).");
            HSettings.Percent(M, "Taxi", TaxiArrivalChance, 0.25f, 0f, 1f, "Chance to arrive by taxi", "Share of arriving guests who are dropped off at the taxi stand instead of walking in from the map edge.");
            HSettings.Silver(M, "Taxi", TaxiArrivalFee, 6, 0, 80, "Landing fee", "What an arriving guest pays the colony for being set down at the stand.");
            HSettings.Int(M, "Taxi", TaxiWaitTicks, 150, 30, 900, "Cab wait (ticks)", "How long a guest waits at the stand before the cab lifts off.", nerd: true, hardMin: 10, hardMax: 3000);

            HSettings.Bool(M, "Security", SecurityOn, true, "Bodyguard service", "Wealthy guests may hire one of your armed colonists as a bodyguard.");
            HSettings.Int(M, "Security", SecurityMinWealth, 60, 1, 100, "Wealth needed for a bodyguard", "Guests below this wealth level (1 to 100) never hire protection.", hardMin: 1, hardMax: 100);
            HSettings.Percent(M, "Security", SecurityChance, 0.3f, 0f, 1f, "Chance to hire (base)", "Base chance per eligible guest; very wealthy guests (80+) get +40 points.");
            HSettings.Percent(M, "Security", SecurityDayShare, 0.15f, 0.01f, 0.6f, "Daily fee (share of budget)", "The bodyguard costs this share of the guest's budget per day, billed per hour.");
            HSettings.Silver(M, "Security", SecurityMinHourFee, 2, 0, 50, "Minimum hourly fee", "A bodyguard never costs less than this per hour.");
            HSettings.Int(M, "Security", SecurityHireHours, 2, 1, 12, "Hours to find a bodyguard", "A hire that finds no free armed colonist within this time is cancelled without charge.", nerd: true, hardMin: 1, hardMax: 48);
        }

        public static Money Silver(string key) => HSettings.GetSilver(key);
        public static int Int(string key) => HSettings.GetInt(key);
        public static float F(string key) => HSettings.Get(key);
        public static bool B(string key) => HSettings.GetBool(key);
        public static int HoursToTicks(string key) => HSettings.GetInt(key) * 2500;
    }
}
