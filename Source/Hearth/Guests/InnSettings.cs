// SPDX-License-Identifier: MIT
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    public enum InnReportMode { Letter = 0, Message = 1, Off = 2 }

    /// <summary>All tunable numbers of the Inn module (key prefix "inn."). Tier anchors follow KONZEPT 9.2 / 9.4.</summary>
    public static class InnSettings
    {
        // Arrivals
        public const string ArrivalRate = "inn.arrivalRate";
        public const string MaxGroupSize = "inn.maxGroupSize";
        public const string FirstGuestMinDays = "inn.firstGuestMinDays";
        public const string FirstGuestMaxDays = "inn.firstGuestMaxDays";
        public const string DayVisitors = "inn.dayVisitors";
        public const string ForecastDays = "inn.forecastDays";
        public const string PodDistance = "inn.podDistance";
        public const string ArrivalNotice = "inn.arrivalNotice";
        // Stay
        public const string StayNightsMin = "inn.stayNightsMin";
        public const string StayNightsMax = "inn.stayNightsMax";
        public const string LeaveEarly = "inn.leaveEarly";
        public const string CheckInHour = "inn.checkInHour";
        public const string CheckOutHour = "inn.checkOutHour";
        // Money
        public const string GuestMoney = "inn.guestMoney";
        public const string PriceBase = "inn.priceBase";
        public const string PriceTolerance = "inn.priceTolerance";
        public const string CardThreshold = "inn.cardThreshold";
        // Reputation
        public const string ExpectationOffset = "inn.expectationOffset";
        public const string GoodwillCapQuadrum = "inn.goodwillCapQuadrum";
        public const string GoodwillCeiling = "inn.goodwillCeiling";
        public const string GoodwillLossCap = "inn.goodwillLossCap";
        public const string RenownLossCap = "inn.renownLossCap";
        // Wing, handling, reports
        public const string AutoWing = "inn.autoWing";
        public const string InsultBase = "inn.insultBase";
        public const string ReportMode = "inn.reportMode";

        const ModuleId M = ModuleId.Inn;

        public static void Register()
        {
            HSettings.Float(M, "Arrivals", ArrivalRate, 1f, 0f, 5f, "Guest arrival rate",
                "Multiplier on how often guest groups arrive. 1 = the normal rhythm of your difficulty; 0 stops new arrivals.",
                hardMin: 0f, hardMax: 10f, tiers: new[] { 3f, 1.4f, 1.2f, 1f, 0.9f, 0.8f, 0.6f });
            HSettings.Int(M, "Arrivals", MaxGroupSize, 4, 1, 10, "Largest guest group",
                "No group brings more guests than this. Overnight groups are also limited by your free guest beds.", hardMin: 1, hardMax: 30);
            HSettings.Float(M, "Arrivals", FirstGuestMinDays, 1.5f, 0.1f, 10f, "First guests: earliest day",
                "Days after your first guest bed (or venue) until the very first group can arrive.", nerd: true, hardMin: 0f, hardMax: 60f);
            HSettings.Float(M, "Arrivals", FirstGuestMaxDays, 3f, 0.2f, 15f, "First guests: latest day",
                "Days after your first guest bed (or venue) by which the very first group will have arrived.", nerd: true, hardMin: 0f, hardMax: 60f);
            HSettings.Bool(M, "Arrivals", DayVisitors, true, "Day visitors",
                "Groups that only come for your open venues during opening hours and leave before closing time. Needs at least one venue sign.");
            HSettings.Int(M, "Arrivals", ForecastDays, 5, 1, 15, "Arrival forecast (days)",
                "How far ahead the arrival plan is drawn up (shown in the guest overview).", nerd: true, hardMin: 1, hardMax: 30);
            HSettings.Int(M, "Arrivals", PodDistance, 140, 40, 400, "Drop pod distance",
                "Guests of industrial or better factions arrive by drop pod when the walk from the map edge would be longer than this many cells.", nerd: true, hardMin: 10, hardMax: 2000);
            HSettings.Bool(M, "Arrivals", ArrivalNotice, true, "Announce arrivals",
                "A short letter for your first groups, a quiet message afterwards.");

            HSettings.Int(M, "Stay", StayNightsMin, 1, 1, 10, "Shortest stay (nights)", "Overnight guests stay at least this many nights.", hardMin: 1, hardMax: 60);
            HSettings.Int(M, "Stay", StayNightsMax, 3, 1, 15, "Longest stay (nights)", "Overnight guests stay at most this many nights.", hardMin: 1, hardMax: 60);
            HSettings.Bool(M, "Stay", LeaveEarly, true, "Unhappy guests leave early",
                "Guests who are miserable for a long time, or run out of money, pack up before their planned day.");
            HSettings.Int(M, "Stay", CheckInHour, 14, 0, 23, "Check-in from (hour)", "Overnight guests plan to arrive from this hour on.", nerd: true, hardMin: 0, hardMax: 23);
            HSettings.Int(M, "Stay", CheckOutHour, 10, 0, 23, "Check-out (hour)", "Overnight guests leave around this hour on their last morning.", nerd: true, hardMin: 0, hardMax: 23);

            HSettings.Float(M, "Money", GuestMoney, 1f, 0.1f, 5f, "Guest money",
                "Multiplier on how much silver guests bring.", hardMin: 0f, hardMax: 1000f, tiers: new[] { 5f, 1.6f, 1.25f, 1f, 0.85f, 0.7f, 0.5f });
            HSettings.Silver(M, "Money", PriceBase, 20, 5, 200, "Base price per night",
                "Price of an economy bed for one night. Better rooms, beds and private rooms multiply it (shown on every guest bed).");
            HSettings.Float(M, "Money", PriceTolerance, 1f, 0.3f, 3f, "Price tolerance",
                "How much over their comfort price guests are still willing to pay.", hardMin: 0.05f, hardMax: 100f, tiers: new[] { 3f, 1.3f, 1.15f, 1f, 0.9f, 0.8f, 0.65f });
            HSettings.Silver(M, "Money", CardThreshold, 300, 0, 2000, "Card payment from budget",
                "Guests from industrial or more advanced factions with a budget above this pay by card instead of carrying silver.", nerd: true);

            HSettings.Float(M, "Reputation", ExpectationOffset, 0f, -0.5f, 0.5f, "Guest expectations",
                "Added to every guest's expectation (0 … 1). Higher means harder to please.", nerd: true, hardMin: -1f, hardMax: 1f,
                tiers: new[] { -0.3f, -0.1f, -0.05f, 0f, 0.05f, 0.1f, 0.2f });
            HSettings.Int(M, "Reputation", GoodwillCapQuadrum, 15, 0, 60, "Goodwill from hosting per quadrum",
                "Hosting can raise a faction's goodwill by at most this much per quadrum.", hardMin: 0, hardMax: 200);
            HSettings.Int(M, "Reputation", GoodwillCeiling, 75, 0, 100, "Goodwill ceiling from hosting",
                "Hosting alone never lifts a faction above this goodwill (75 = ally threshold).", hardMin: -100, hardMax: 100);
            HSettings.Int(M, "Reputation", GoodwillLossCap, 5, 0, 100, "Largest goodwill loss per bad stay",
                "A bad stay costs a faction at most this much goodwill.", hardMin: 0, hardMax: 200,
                tiers: new[] { 0f, 0f, 2f, 5f, 15f, 40f, 100f });
            HSettings.Float(M, "Reputation", RenownLossCap, 2f, 0f, 20f, "Largest renown loss per group",
                "One unhappy group lowers your renown by at most this much.", nerd: true, hardMin: 0f, hardMax: 100f,
                tiers: new[] { 0f, 0f, 1f, 2f, 4f, 8f, 100f });

            HSettings.Bool(M, "Wing", AutoWing, true, "Expand guest wings automatically",
                "The first guest bed creates Guest Wing 1 around its room; later guest rooms join the nearest wing.");
            HSettings.Percent(M, "Handling", InsultBase, 0.15f, 0f, 0.6f, "Recruitment insult risk",
                "Base chance that a recruitment talk offends the guest (lower with better social skill).", nerd: true);
            HSettings.Enum(M, "Reports", ReportMode, InnReportMode.Letter, "Departure reports",
                "How the short report after each stay reaches you: as a letter, as a quiet message, or not at all.");
        }

        public static float F(string key) => HSettings.Get(key);
        public static int I(string key) => HSettings.GetInt(key);
        public static bool B(string key) => HSettings.GetBool(key);
    }

    [DefOf]
    public static class InnDefOf
    {
        public static DutyDef HH_GuestStay;
        public static JobDef HH_CheckIn;
        public static JobDef HH_AccompanyGuest;
        public static IncidentDef HH_GuestGroupArrival;
        public static ThingDef HH_FrontDesk;
        public static ThoughtDef HH_GuestRoomVerdict;
        public static ThoughtDef HH_GuestAccompanied;
        public static ThoughtDef HH_GuestPestered;
        public static ThoughtDef HH_HostedWell;
        public static HistoryEventDef HH_HostedGuests;
        static InnDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(InnDefOf)); }
    }
}
