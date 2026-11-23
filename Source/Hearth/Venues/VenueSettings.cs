// SPDX-License-Identifier: MIT
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>Setting keys and registration of the venues module (9). All numbers a player may tune live here.</summary>
    public static class VS
    {
        // Prices and shopping
        public const string ShopPrice = "venues.shopPrice";
        public const string PriceTolerance = "venues.priceTolerance";
        public const string SelfService = "venues.selfService";
        public const string BrowseTicks = "venues.browseTicks";
        public const string MaxPurchases = "venues.maxPurchases";
        public const string ShopDesire = "venues.shopDesire";
        public const string DrinkGapHours = "venues.drinkGapHours";
        public const string VendingPrice = "venues.vendingPrice";
        // Staffing and hours
        public const string StaffRuleKey = "venues.staffRule";
        public const string TendHours = "venues.tendHours";
        public const string WaitForStaffTicks = "venues.waitForStaffTicks";
        public const string NightBell = "venues.nightBell";
        public const string NightBellSurcharge = "venues.nightBellSurcharge";
        public const string NightBellMin = "venues.nightBellMin";
        public const string NightBellWaitHours = "venues.nightBellWaitHours";
        public const string DayOpen = "venues.dayOpen";
        public const string DayClose = "venues.dayClose";
        public const string EveningOpen = "venues.eveningOpen";
        public const string EveningClose = "venues.eveningClose";
        public const string NightOpen = "venues.nightOpen";
        public const string NightClose = "venues.nightClose";
        // Market
        public const string MarketEnabled = "venues.marketEnabled";
        public const string MarketDays = "venues.marketDays";
        public const string MarketCaravans = "venues.marketCaravans";
        public const string MarketVisitors = "venues.marketVisitors";
        public const string MarketStalls = "venues.marketStalls";
        public const string MarketRenown = "venues.marketRenown";
        public const string StallFee = "venues.stallFee";
        // Last orders
        public const string LastOrders = "venues.lastOrdersMinutes";
        // Furniture
        public const string HideVanilla = "venues.hideVanilla";
        // 1.1.0: setup checklist (F12), serving (F14), honor system (F15), business names (F33)
        public const string RequireSetup = VenueSetup.RequireKey;
        public const string SetupHints = VenueSetup.HintsKey;
        public const string ServiceSkill = "venues.serviceSkill";
        public const string TipShare = "venues.tipShare";
        public const string BusyPerHour = "venues.busyServedPerHour";
        public const string ShiftThoughts = "venues.shiftThoughts";
        public const string IdleHours = "venues.idleHours";
        public const string HonorUnderpay = "venues.honorUnderpay";
        public const string HonorNoPay = "venues.honorNoPay";
        public const string HonorWatched = "venues.honorWatched";
        public const string HonorMessages = "venues.honorMessages";
        public const string AutoNames = "venues.autoNames";

        public static void Register()
        {
            const ModuleId M = ModuleId.ShopsFood;
            HSettings.Float(M, "Prices", ShopPrice, 1f, 0.25f, 3f, "Shop price level",
                "Multiplies the market value of goods sold in shops and taverns, on top of each sign's own price factor.", hardMin: 0.01f, hardMax: 20f);
            HSettings.Float(M, "Prices", PriceTolerance, 1f, 0.3f, 3f, "Guest price tolerance",
                "How much guests put up with. Above 1 they accept steeper prices, below 1 they haggle with their feet. Follows the difficulty slider.",
                hardMin: 0.1f, hardMax: 10f, tiers: new[] { 3.0f, 1.3f, 1.15f, 1.0f, 0.9f, 0.8f, 0.65f });
            HSettings.Percent(M, "Prices", SelfService, 0.10f, 0f, 0.5f, "Self-service discount",
                "Venues that do not need staff but have nobody behind the counter charge this much less (the guest fetches everything themselves).");
            HSettings.Float(M, "Prices", VendingPrice, 1f, 0.5f, 2f, "Vending machine price level",
                "Multiplies the market value of what vending machines sell.", hardMin: 0.01f, hardMax: 20f);
            HSettings.Int(M, "Guest behaviour", BrowseTicks, 300, 0, 1800, "Browsing time (ticks)",
                "How long a guest looks at an item before deciding. 60 ticks is one second at normal speed.", hardMax: 20000);
            HSettings.Int(M, "Guest behaviour", MaxPurchases, 4, 1, 12, "Purchases per guest and stay",
                "A guest stops shopping after this many purchases in one stay.", hardMin: 1, hardMax: 100);
            HSettings.Float(M, "Guest behaviour", ShopDesire, 1f, 0f, 3f, "Guest appetite for shopping",
                "How eagerly guests walk into shops and taverns compared with other things they could do. 0 = they never shop.", hardMax: 20f);
            HSettings.Int(M, "Guest behaviour", DrinkGapHours, 2, 0, 12, "Hours between drinks",
                "A guest does not order another drink before this many hours have passed.", hardMax: 100);

            HSettings.Enum(M, "Staffing", StaffRuleKey, StaffRule.PerVenueType, "Staffing rule",
                "Per venue type: taverns, restaurants, casinos and spas need someone on duty, shops and lounges do not. Never: every venue runs as self-service. Always: every venue needs someone on duty.");
            HSettings.Int(M, "Staffing", TendHours, 2, 1, 8, "Hours per shift",
                "How long a colonist tends a venue before looking for other work (they also stop when it closes).", hardMax: 24);
            HSettings.Int(M, "Staffing", WaitForStaffTicks, 1500, 300, 7500, "Guest patience for staff (ticks)",
                "A guest waits this long for somebody to come to an open but unstaffed venue, then gives up annoyed.", nerd: true, hardMin: 60, hardMax: 60000);
            HSettings.Bool(M, "Staffing", NightBell, true, "Night bell",
                "Guests may ring a bell for a venue that is closed. Awake colonists with the Serving work type come and open it for a surcharge. Nobody is woken up.");
            HSettings.Percent(M, "Staffing", NightBellSurcharge, 0.5f, 0f, 2f, "Night bell surcharge",
                "Extra share of the price that guests pay when they ring the bell outside opening hours.", hardMax: 10f);
            HSettings.Silver(M, "Staffing", NightBellMin, 3, 0, 50, "Night bell minimum charge",
                "A bell service never costs less than this (silver).");
            HSettings.Int(M, "Staffing", NightBellWaitHours, 1, 1, 4, "Night bell patience (hours)",
                "How long a guest waits after ringing before giving up ('Rang the bell. Nobody came.').", hardMax: 12);

            HSettings.Int(M, "Opening hours", LastOrders, 30, 0, 120, "Last orders (minutes before closing)",
                "In this last stretch before closing time guests in a hurry buy one more round, no guest starts a long browse and a purchase already under way is still served after closing. 0 = no last orders.", hardMax: 600);
            HSettings.Int(M, "Opening hours", DayOpen, 8, 0, 23, "Day preset opens", "Hour the 'Day' preset in the sign's hours dialog opens.", hardMax: 23);
            HSettings.Int(M, "Opening hours", DayClose, 22, 0, 23, "Day preset closes", "Hour the 'Day' preset closes.", hardMax: 23);
            HSettings.Int(M, "Opening hours", EveningOpen, 17, 0, 23, "Evening preset opens", "Hour the 'Evening' preset opens.", hardMax: 23);
            HSettings.Int(M, "Opening hours", EveningClose, 2, 0, 23, "Evening preset closes", "Hour the 'Evening' preset closes (an earlier hour than opening means after midnight).", hardMax: 23);
            HSettings.Int(M, "Opening hours", NightOpen, 18, 0, 23, "Night preset opens", "Hour the 'Night' preset opens.", hardMax: 23);
            HSettings.Int(M, "Opening hours", NightClose, 4, 0, 23, "Night preset closes", "Hour the 'Night' preset closes.", hardMax: 23);

            HSettings.Bool(M, "Market", MarketEnabled, true, "Big market",
                "Every so often friendly factions bring several trader caravans and day visitors at once. Only happens while you run at least one shop.");
            HSettings.Int(M, "Market", MarketDays, 15, 3, 60, "Days between markets",
                "Average interval of the big market, with a little randomness.", hardMin: 1, hardMax: 600);
            HSettings.Int(M, "Market", MarketCaravans, 3, 1, 6, "Trader caravans per market",
                "How many trader caravans come on market day (fewer if few factions are friendly).", hardMin: 1, hardMax: 20);
            HSettings.Int(M, "Market", MarketVisitors, 2, 0, 4, "Visitor groups per market",
                "How many groups of day visitors come on market day, to shop in your venues.", hardMin: 0, hardMax: 20);

            HSettings.Int(M, "Market", MarketStalls, 4, 1, 12, "Market stalls needed",
                "The big market only happens once you own this many free-standing market stalls (traders set up at them). With fewer stalls fewer caravans come.", hardMin: 1, hardMax: 100);
            HSettings.Int(M, "Market", MarketRenown, 20, 0, 100, "Renown needed for the market",
                "Minimum renown (0-100) before traders and visitors want to hold a market with you. Without a renown system the colony counts as neutral (50).", hardMax: 100);
            HSettings.Silver(M, "Market", StallFee, 25, 0, 200, "Stall fee per trader",
                "Every trader caravan pays this much silver for its stall on market day (it goes into the colony till).");

            HSettings.Bool(M, "Setup", RequireSetup, true, "Venues need their furniture",
                "A business only opens once the furniture its type needs stands in the room (a tavern needs a counter, a table and seats, a casino at least one game, and so on; see the sign's Setup tab). Off: missing furniture is only a hint.");
            HSettings.Bool(M, "Setup", SetupHints, true, "Build buttons on the sign",
                "A selected venue sign shows build buttons for the furniture its business still needs.");
            HSettings.Bool(M, "Setup", AutoNames, true, "Name new businesses automatically",
                "A new venue sign gets a name that fits its kind of business (for example 'The Drowsy Alpaca' for a tavern). You can always rename it or roll another one.");

            HSettings.Float(M, "Staffing", ServiceSkill, 1f, 0f, 2f, "Weight of the Social skill",
                "How much the Social skill of whoever is on duty matters: friendlier service, bigger purchases, tips and quicker sales. 0 = skill does not matter.", hardMax: 10f);
            HSettings.Percent(M, "Staffing", TipShare, 0.08f, 0f, 0.3f, "Tips",
                "Largest tip a guest leaves on top of a purchase, as a share of the price, for a perfect tender. The chance and size of a tip grow with the tender's Social skill.", hardMax: 1f);
            HSettings.Bool(M, "Staffing", ShiftThoughts, true, "Shift moods",
                "Colonists who tend a venue are pleased after a busy shift and bored when they stand around for hours with nothing to do.");
            HSettings.Float(M, "Staffing", BusyPerHour, 1.5f, 0.25f, 6f, "Busy shift: guests served per hour",
                "A shift counts as busy (good mood) when the tender served at least this many guests per hour.", nerd: true, hardMin: 0.05f, hardMax: 60f);
            HSettings.Float(M, "Staffing", IdleHours, 1.5f, 0.5f, 6f, "Bored after (hours without a guest)",
                "A tender who has served nobody for this many hours of a shift gets bored (bad mood, a little worse every further stretch).", hardMin: 0.1f, hardMax: 24f,
                tiers: new[] { 6f, 3f, 2f, 1.5f, 1.25f, 1f, 0.75f });

            HSettings.Percent(M, "Honor system", HonorUnderpay, 0.25f, 0f, 0.8f, "Honor system: chance to pay too little",
                "At an unattended honor-system venue, the chance that a guest leaves only part of the price (30-80 %) in the box.",
                tiers: new[] { 0.05f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.45f });
            HSettings.Percent(M, "Honor system", HonorNoPay, 0.10f, 0f, 0.6f, "Honor system: chance to pay nothing",
                "At an unattended honor-system venue, the chance that a guest takes the goods without paying at all.",
                tiers: new[] { 0.02f, 0.05f, 0.08f, 0.10f, 0.12f, 0.15f, 0.2f });
            HSettings.Percent(M, "Honor system", HonorWatched, 0.5f, 0f, 1f, "Honor system: effect of being watched",
                "How much less often guests underpay while a colonist can see them (1 = never while watched).");
            HSettings.Bool(M, "Honor system", HonorMessages, true, "Honor system: report seen thefts",
                "Show a message when a colonist saw a guest leave without paying.");

            var hide = HSettings.Bool(M, "Furniture", HideVanilla, true, "Hide replaced vanilla furniture",
                "When a Hearth & Harbor building replaces a vanilla one (for example the card table for the poker table), the vanilla one disappears from the build menu once the replacement is available. Buildings you already placed stay. Takes effect after a restart or the next game load.");
            hide.Global = true;
        }

        public static float F(string key) => HSettings.Get(key);
        public static int I(string key) => HSettings.GetInt(key);
        public static bool B(string key) => HSettings.GetBool(key);
    }
}
