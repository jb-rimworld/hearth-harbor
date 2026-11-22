# Integration requests collected from all module agents (22.11.2026)


## art
### contract_requests
### integration_notes
- Re-render everything with: uv run --with resvg-py --with pillow python3 tools/art/build.py (optional name filter as an argument). Mac tools like VectorCraft are not needed.
- Graphic_Multi: west is the east image mirrored by the game. East/west textures are drawn with the size rotated, so non-square footprints have swapped east dimensions (done automatically).
- Footprint guesses to match in the defs: FrontDesk 2x1, ShopCounter 2x1, PrepCounter 2x1, DoughBench 2x1, ColdStation 2x1, Grill 2x1, BankCounter 3x1, RouletteTable 3x2, PokerTable 3x2, BlackjackTable 2x2, CashierCage 2x2; all other buildings 1x1. If a def differs, edit cells=(w,h) in the @tex line of that texture and re-run.
- Masked (stuffable) textures write <name>_<view>m.png: FrontDesk, ShopCounter, PrepCounter, DoughBench, BankCounter. Use a CutoutComplex shader only for these; others are fully coloured.
- Items (Plate_*, Chips, Wallet, TravelBag) are 64x64; UI icons 128x128; CardHeader 1024x256.
- Add new textures as @tex functions in tools/art/hhart/<module>.py (see README); the integrator can ask for more paths.
- tools/art/.gitignore excludes __pycache__ and out/svg; the contact sheets in out/ are committed.
### not_done
- Epoch and Ideology style variants (KONZEPT 14.7): not required now, planned in the README (@tex style argument, per-style palette and ornament set)
- Guest-bed cover textures with colour-blind patterns: no paths given yet, add once the Inn agent names them
- Footprints for kitchen stations, Roulette and Blackjack were not specified and are guesses; they need to match the defs or be changed in the @tex line and re-rendered
- Upright machines (ATM, SlotMachine, Smoker, VendingMachine, NightSafe) have simple north/east views; a detail pass is possible
- Stuff masks use a neutral pale fill, so counters look pale stone if the defs do not use stuff colour; only FrontDesk, ShopCounter, PrepCounter, DoughBench and BankCounter are masked
- No in-game visual check; no in-game test run (task did not require one)

## casino
### contract_requests
- A shared DesignationCategoryDef HH_Hospitality is not defined by the foundation. I added my own category HH_Casino in Common/Defs/Casino/Casino_Buildings.xml; the integrator may switch the five buildings to the shared category and delete mine.
- HH_VenueSign and a venue hide-list def do not exist yet: the vanilla PokerTable is not hidden (logged at startup), and the venue room path is untested
- GuestApi has no refund call: CasinoBank.CashOut edits GuestInfo.Budget and Spent directly after a cash-out. A GuestApi.Refund(pawn, amount) would be cleaner.
### integration_notes
- Compile errors: Source/build.sh hides them (sed strips the message), and run_test.sh only greps 'error' in that output. Use tools/hb.sh (my addition) to see them before running tests.
- Entry points: CasinoModule : HearthModuleBase (ModuleId.Casino) registers settings, the guest activity 'casino.play', the 'casino' main-tab page, and caretaker checks Casino.DrugPolicy and Casino.Sessions. State lives in the GameComponent CasinoComponent (sessions, gambler records, day book, houses, initialised policy ids).
- The Inn/guest brain must call GuestActivities.TryPick for guests to gamble. Guests read GuestApi.Info(p).Budget and I edit Budget and Spent directly on cash-out.
- Defs added: VenueTypeDef HH_Venue_Casino (iconPath UI/HH/Casino/VenueCasino), JoyKindDef HH_Gambling, JoyGiverDef HH_PlayCasinoJoy, JobDefs HH_PlayCasino and HH_RunCasinoTable, WorkGiverDef HH_RunCasinoTable (Serving), ChemicalDef HH_GamblingChemical, NeedDef HH_Need_Gambling, HediffDef HH_GamblingAddiction, MentalStateDef HH_GamblingSpree, ThoughtDefs, two ThinkTreeDefs injected at Humanlike_PostMentalState and Humanlike_PreMain, HelpHintDef HH_HelpHint_Gambling, research HH_Casino_Slots and HH_Casino_Tables (view positions 6/4 and 6/3 may need moving).
- The virtual drug HH_GamblingSession is a real ThingDef (category Item, IsDrug) so it shows as a row in every drug policy even when the module is off for a save; the row itself cannot be hidden by XML.
- The cashier's cage carries Hearth.CompProperties_TillSpot, so the nightly till silver is put down at the cage.
- Translate quirk: a string argument that follows ': ' in a Keyed text gets its first letter capitalised, so links and odds use {LINK}/{ODDS} with String.Replace instead of {0}.
- The poker table is HH_PokerTable (defName of the 3x2 replacement). Vanilla PokerTable is still in the Furniture menu; add 'PokerTable' to the venues hide-list when that mechanism exists.
- Settings keys start with 'casino.'; the fatal-despair key casino.fatalDespair is a stub with no effect. Game rounds use JRand.Sealed('casino.<game>'); stakes and cash-out rounding use 'casino.stake' and 'casino.cashout'; addiction rolls use JRand.Stream('casino.addiction').
- Underworld later: fake chips can reuse HH_CasinoChips with a quality and a forged flag on a new ThingComp; chips already carry tradeability None and MarketValue 0.
- Check the casino tab and demo windows with casino.shots (not in normal runs): HH_TEST=casino.shots, screenshots end up in /private/tmp/claude-501/hh_tests/casino_shots when the game saves them.
### not_done
- Progressive jackpot (1-2% of stakes): skipped, the pure maths has no pool yet
- N47 upgrade stages (lever, video and holo slots with different spin rates, failures every 15 days): only a volatility toggle; spin rate is a setting
- N49 'Convert to card table' for existing vanilla poker tables, and hiding the vanilla PokerTable in the build menu: no venue hide-list def exists yet, so it is only logged
- Time block 'Casino' (TimeAssignmentDef), the 'Gambling: Always/Casino time only/Never' column, and the Concierge warning at n3 = 2 (the counter shows on the Casino page instead)
- Gesprächstherapie x1.5, Ideology precept 'Gambling', Casino night and Poker night gatherings, colony lottery, prisoner bets, optional colonist pocket money
- Cheating from both sides, card counting, free drinks, responsible-gaming switch for guests, extra Pit Boss views (formula, histogram, CSV export)
- Cage mechanics beyond the marker role: no buy-chips interaction, no counterfeit chips (Underworld, later); chips are only handed to guests as taken-home float
- Separate 'Wellbeing' page: merged into the Casino page
- Builds for 1.6 and 1.5 not verified: RW17/RW16 #if guards are in place, but the foundation's 1.6 build fails on a duplicate TargetFramework attribute in obj/1.6
- casino.slots does not create a venue sign in a real casino room: HH_VenueSign does not exist in this worktree, so the map house account is tested; the Venue.Account path is untested
- Guests only reach the casino through GuestActivities.TryPick; the vanilla-visitor think-node patch belongs to the Inn module and is not in this worktree, so tests start the job directly
- casino.hint cannot always observe real frames when the game window is occluded; it then checks ShowsHelpHint and a direct draw instead

## bank
### contract_requests
- Foundation build: 1.6/1.5 builds fail with CS0579 duplicate TargetFrameworkAttribute (Hearth.Core/obj/1.6), and a leftover obj/1.6 then breaks the 1.7 build; please fix BaseIntermediateOutputPath/Compile Remove handling in Directory.Build.props
- Settings UI: bank.profit is a log-scale value (0.01 to 100, 0 allowed): the generic linear slider should offer log scaling for it; Settings also has TierAnchors interpolated linearly whereas Bank M uses log interpolation (BankSettings.ProfitM does this itself)
- If the integrator wants the key HH.Bank.* texts of other modules in my Keyed file, they must use their own HH_<Module>.xml; HH_Bank.xml is generated from code and must not be edited by hand
- Core could expose a tick-independent 'midnight' event; the sink currently relies on Tills.FlushAll in HearthGame (works, tested through a real midnight)
### integration_notes
- Defs other modules may use: DesignationCategoryDef HH_Bank (defined in Common/Defs/Bank/Bank_Buildings.xml: the insurance agent must NOT define it again), ResearchProjectDefs SL_Banking, SL_Branches, SL_Credit in tab HH_Banking, ThingDefs SL_ATM, SL_BankCounter, SL_NightSafe, FactionDef SL_CollectionsAgency, SL_BankDef bank defs
- BankTabs: built-in tab orders lobby 10, loans 20, history 30, terms 40; insurance/investments/lottery should register with order 50 and up. The window wraps tab Draw in Guard.Run and sets Window_Bank.Active (Map, Site, At) for tabs that need the current map or building
- Ledger accounts: bank:colony, bank:transit, bank:loans (negative = debt). Other modules should not post to bank:colony directly: use BankApi.Impl.Deposit/Withdraw or BankGameComp.Current.Credit (applies fee, collection offset and keeps the ledger balanced)
- BankGameComp.Current is the saved component (Silverline.BankGameComp, GameComponent); everything is saved in it, no fields on vanilla classes; module removal leaves ledger entries only
- Harmony patches: Patch_CommsConsole_Targets (Building_CommsConsole.GetCommTargets postfix) and Patch_PlayerWealth (Map.PlayerWealthForStoryteller getter postfix with re-entrancy guard); both return early when Modules.On(Bank) is false
- Settings registered (all ModuleId.Bank): bank.profit, accountBeta, directDepositFee, directDeposit, feeFactor, switchFee, depositDelayHours, podMinHours, podMaxHours, atmDailyLimit, podMaxLoad, researchMode, wealthShare, moodOn, moodScale, loanFloor, loanIntervalDays, loanMaxTermDays, lateFee, defaultFactor (payment default factor, not used by the bank itself: for guest payments), maxCollectionStage, collectionAnger, bailout, seizureShare. Read bank.profit through BankSettings.ProfitM
- The bank module reads HearthProfile.UnderworldDiscovered and Modules.On(ModuleId.Underworld) for The Black Ledger
- Test conventions found: a yield after Find.TickManager.DebugSetTicksGame is measured from before the jump (use jump; yield 1; yield n), and suite MaxTicks counts jumped ticks
- Buildings spawned by tests need SetFaction(Faction.OfPlayer) to appear in allBuildingsColonist (TillSpot and the pod target use that list)
- BankSilver.Depositable(map) is the one place that decides which silver a deposit may take (beacon range or storage, not forbidden)
- Texture/Keyed: run tools/gen_bank_keys.py after changing any L.T text and commit Keyed/HH_Bank.xml
### not_done
- K4 part 'route vanilla trade income to the account' (patch on the trade settlement): only the till sink is done; needs a postfix on the trade execution that diverts the silver sold, plus a virtual 'Bank balance' payment item, with the TradeHelper/DTI test
- Colonists and visitors using ATMs on their own (optional polish); card-paying guests withdrawing cash needs the guest module
- Bank-faction goodwill effects of collection stages and Imperial honour perks; Black Ledger underworld products, Church faith funds and Orbital market access are product features of later modules (investments); only fees/spreads/risk of those banks are implemented
- Products Dispo, Baukredit, Quick Silver, Hypothek, guest loans from 8.9: only the private loan with bank spread exists
- Bounty (stage 7) is the stage 6 raid x1.5 every 3 days plus a letter, no bounty hunters in later raids
- Settings window shows bank.profit with the generic linear slider (the log slider lives in the bank window, Terms tab)
- Builds for 1.6 and 1.5 were not verified: the foundation build fails there with CS0579 (duplicate TargetFrameworkAttribute) and leaves obj/1.6 which then breaks the 1.7 build until deleted
- DefInjected/other languages: only English keyed texts; bank def labels/slogans are plain in the defs (dynamic keys Bank.<defName>.Name/.Slogan are read but not generated)

## kitchen
### contract_requests
- Core/Defs: HH_Innkeeping, HH_Gastronomy, HH_HotelManagement and HH_FineDining research are expected by the device gating. If the research owner defines them, no change on my side is needed (fallback to my HH_Cooking_L2/L3 is automatic). If the integrator prefers the kitchen levels to be those projects, retarget HH_Cooking_L2..L4 or tell DishKnowledge.LevelResearch which def to use.
- Foundation build: Hearth.Core obj/ globbing breaks multi-version builds (duplicate TargetFrameworkAttribute). Directory.Build.props 'Compile Remove obj/**' is evaluated before the default globs; use DefaultItemExcludes or set it in each csproj. This also hides compile errors from build.sh: it greps for the English word 'error' while dotnet prints localised errors.
### integration_notes
- Kitchen module class KitchenModule : HearthModuleBase with Id ShopsFood, next to the venues module's own ShopsFood module class (two module objects with the same id work).
- Set Restaurant.OpenGate = VenueApi.IsOpenForBusiness and Restaurant.VenuePriceFactor = VenueApi.PriceMultiplier once the venues module is merged. HH_Venue_Restaurant is looked up by name; without it the dining rooms act as restaurants.
- Recipes are generated at startup in HearthStartup via KitchenModule.OnDefsLoaded: IngredientClasses.Resolve(), Stations.AssignResearch(), RecipeBuilder.BuildAll(). They use DefDatabase<RecipeDef>.Add and reset ThingDef.allRecipesCached for the main devices.
- Harmony patch classes (all check Modules.On(ShopsFood) or a DishRecipeExtension first): RecipeDef.AvailableNow getter, Bill_Production.ShouldDoNow and its StatusString getter, GenRecipe.PostProcessProduct (two postfixes), CompFoodPoisonable.Notify_RecipeProduced prefix (only our HH_Plate_* things), Need_Food.FoodFallPerTickAssumingCategory postfix, CompRottable.CompInspectStringExtra postfix (plates only).
- Guest activity 'kitchen.eat' is registered via GuestActivities.Register. Without the Inn module it still needs the vanilla-visitor think-node patch of the integrator; my tests start the job by hand.
- Settings added: kitchen.shelfLife, kitchen.wellFedStrength, kitchen.wellFedDuration, kitchen.menuPrice, kitchen.poisonRule, kitchen.variants, kitchen.allKnown, kitchen.darkMenu. Page 'kitchen' (Cookbook) registered with HearthTabs.
- Test hook DishKnowledge.TestLevelLock exists only with HH_TESTS (the quick-test game has no cooking research finished beyond what the module does). Do not rely on it in release builds.
- Common/Patches/Kitchen_Traders.xml adds a StockGenerator_Tag for tradeTag HH_Cookbook to three outlander trader kinds (success=Always).
- Other modules that define VenueTypeDef etc. are untouched. Tests clean up after themselves in the areas center+(+-14..22,..), but they use the map around the centre, so run suites sequentially (run_test.sh does).
### not_done
- Dishes with other mods (VCE, VPE, Kenshi Cuisine) are untested: none of those mods are installed locally. The DishDefs exist, but their recipes only appear when the ingredient names and package ids match; the name matching has unit checks only. Fish dishes were tested with Odyssey.
- The restaurant is only a basic loop. Left out: the 0-100 dish rating with Passung, wait penalty and ambience, critics, pre-cooking before opening, buffet and room service, tips, and cuisine and taste matching. A quick 'Gerichtsbewertung' is not implemented.
- No Variety Matters adapter yet, and no Static Quality / Quality Factors display hooks (the quality rolls go through the vanilla function, so they apply automatically).
- The deep fryer has the 'small fire risk' only as Flammability 1.0 and its text, with no actual ignition comp.
- Guests with Cooking 8+ revealing a dish of their region (10 % per stay) is not implemented; it belongs with the Inn module. Recipe books come from outlander traders only.
- Devices use designationCategory Production, not HH_Hospitality. The Kitchen room role of vanilla requires Production, and HH_Hospitality was not defined in my worktree.
- Fine dining (service, dress code), the Colony cookbook as a separate window and the learn-from-the-Venue-sign flow.
- 1.5 and 1.6 builds compile but were not run in game. The Source/build.sh 'all' mode fails in the foundation (duplicate TargetFrameworkAttribute from obj/ globbing); building each version from a clean obj/ works.

## inn
### contract_requests
- Please add DesignationCategoryDef HH_Hospitality to Common/Defs/Core. The Inn's wing designators go there automatically if it exists (otherwise into vanilla Zone), and HH_FrontDesk currently uses designationCategory Furniture: please move it to HH_Hospitality when the category exists (Defs/Inn/Inn_Buildings.xml).
- Source/build.sh: its error filter sed 's#.*/Source/##' removes the whole message because the csproj path at the end of each line also contains /Source/. Suggest: sed -E 's#^.*/Source/([^ (]+\([0-9,]+\)).*: (error[^[]*).*#\1 \2#' or print the raw lines.
- Optional GuestApi addition: a GuestApi.Renown / Whispers read access (today they live in Hearth.Guests.GuestRegistry.Instance.Renown/Whispers), so other modules need not reference Hearth.Guests.
- Optional: HearthModuleBase hook for 'map component wants a draw overlay' — the wings currently draw only while a wing designator is selected.
### integration_notes
- Public entry points: Hearth.Guests.GuestRegistry.Instance (Get(pawn) → GuestRecord, Records, Groups, Renown, Whispers, Stars, StarsText(), DepartGroup, SendAway, Notify_Incident(pawn, amount, text), GizmosFor, ReportFor), GuestRegistry.IsRegisteredGuest(pawn) and IsWingRestricted(pawn) (HashSet, cheap); MapComponent_HH_GuestScheduler (Forecast, GetForecast(days), NextArrival(), RatePerDay(), OpeningWindow(), static FactionCanVisit/EnemyPresent, static SuspendedForTests); GuestArrival.TryFire(map, GuestArrivalRequest) to force an arrival (Size, Kind, Nights, ForceDigital, ForceBudget, ForcePod, Silent, StayTicksOverride); GuestBeds.On/FreeOn/CountOn/IsGuestBed(map/thing); GuestBedPricing.Quote(bed) → BedQuote; GuestWings.Comp(map)/WingFor(pawn)/AllowedFor(pawn, cell)
- GuestApi.Info(p) returns a GuestRecord (subclass of GuestInfo); modules may cast to Hearth.Guests.GuestRecord for room/nights; Events is capped at 40
- GuestApi.IsGuest is false once a stay is settled (guest walking home), so venues do not sell to leaving guests
- Venue rooms are always allowed for wing-restricted guests (AllowedFor checks Venues.All), so ShopsFood/Casino activities work even outside the wing
- Activities: JobGiver_HH_GuestActivity asks GuestActivities.TryPick at most every 600 ticks per guest, after sleep and eating and before recreation and wandering; overnight guests must be checked in first
- Ledger accounts: guest:<thingIDNumber> for card guests (funded from world at arrival, remainder back to world at departure, file closing or recruitment); night payments land in Payments.ColonyTill(map) with memo 'Inn: night in … (cash|card)'
- Payment provider id 'inn.card', priority 50 (above the core cash provider)
- Settings keys all start with 'inn.' (see InnSettings constants); tier anchors on inn.arrivalRate, inn.guestMoney, inn.priceTolerance, inn.expectationOffset, inn.goodwillLossCap, inn.renownLossCap
- Main-tab page registered as HearthTabPage Id 'inn.overview' (Order 10); the UI module may replace or hide it and draw its own overview from the same public API
- Wing designators are added at runtime to DesignationCategoryDef HH_Hospitality if it exists, else Zone (specialDesignatorClasses + resolved designators)
- Defs added: HH_FrontDesk (ThingDef), HH_GuestStay (DutyDef), HH_CheckIn and HH_AccompanyGuest (JobDefs), HH_AccompanyGuest (WorkGiverDef on HH_Hosting), HH_GuestGroupArrival (IncidentDef, base chance 0), HH_HostedGuests (HistoryEventDef), thoughts HH_GuestRoomVerdict, HH_GuestAccompanied, HH_GuestPestered, HH_HostedWell
- Test-only hooks: GuestRegistry.RecruitChanceOverride, BondThresholdOverride, AddRawRecordForTests, ResetGoodwillBookForTests; MapComponent_HH_GuestScheduler.SuspendedForTests (Inn suites switch it off and back on)
- tools/inn/gen_keyed.py regenerates Keyed/HH_Inn.xml from the code fallbacks; run it after changing texts
- My branch includes a merge of main (3 test slots, obj exclusion) before the Inn commits
- Earlier in the session a combined test run froze once after midnight (no log output for 14 minutes, then killed by the timeout). It did not happen again in 6 later runs after the test teardown stopped discarding still-referenced pawns; it may have been an engine stall under parallel load, cause not proven
### not_done
- The day-visitor path (venue opening hours, last orders) is coded but has not been tested in-game: this worktree has no VenueTypeDef or venue-sign building; it needs the ShopsFood module's signs
- Archetypes, interests, desire counters and the activity planner weights from 5.3/5.6 are not done: only the generic GuestActivities.TryPick is used
- Return visits, gift on leaving, scandal events, Critic, whispers-weighted arrivals, door policy/filters (5.9), the travel bag, and the visible wallet apparel (5.15) are not done: out of scope for the core loop
- The '5 % arrival discount' for own transport, shuttle or taxi arrivals, and the wealth-based pod arrival beyond the distance rule are not done; drop pods are the only far-arrival mode
- Waiting-room, night bell and incident thoughts (waiting, cleanliness, price vs expectation as vanilla thoughts) are not done: only the room verdict, company and pestered thoughts exist
- The wing status line ('Bedroom ✓ · Kitchen ✗'), concierge tips, the 'Use this bed as guest bed' offer letter, the auto-assignment of Hosting priorities, and the auto-separation of hostile groups into different wings are not done
- Group-member bed sharing (couples in double beds, multi-slot renting) is not done: one guest per bed
- No save/load roundtrip test: reloading inside the test harness would restart the runner; the ExposeData of every class was written for it but has not been run in-game
- Guests' lords use a LordJob class from our assembly, so removing the mod with guests on the map can still log a lord-load error; PrepareRemoval sends guests home first, but a dedicated removal test is missing
- Not built or checked for 1.6 or 1.5: written against 1.7 APIs (PlanetTile, tickIntervalAction, PawnDiscardDecideMode)
- The Core build.sh error filter strips the error text (its sed removes everything up to the last '/Source/', which the csproj path at the end of each line contains); I used a local copy of the build command to see errors

## ui
### contract_requests
- None required. Optional for the Inn module: set HearthUI.GuestBedProbe = map => anyGuestBed(map) so the tab and the guide step see guest beds
- Optional for other modules: register tier-driven settings under the keys the calculator looks up (inn.guestMoney, inn.priceTolerance, inn.arrivalRate, inn.expectation, inn.trouble, casino.slotEdge, bank.interestM, bank.defaultFactor, bank.collectionStage, insurance.loading, insurance.salesPressure, casino.addictionChance, casino.detection). Otherwise the calculator shows the 9.2 reference value
- Modules should read the special switches (core.neverDie, core.noHostile, core.infiniteMoney, core.picky, core.feuding, core.mandatory, core.strictInspectors, core.realConsequences, core.ironBank, core.noResearchGating, core.peacefulUnderworld, core.perfMode, core.showMath), core.consequenceStage, core.researchCost and core.upkeep from HSettings
- Source/build.sh prints only the tail of error lines because its sed strips them; run dotnet build directly to see messages (foundation file, not changed)
### integration_notes
- Namespace is Hearth.Shell, not Hearth.UI: a namespace Hearth.UI would hide Verse's UI class for every Hearth.* namespace and break UI.screenWidth. GuideTopicDef and GuideStep are in namespace Hearth, so XML uses <Hearth.GuideTopicDef>
- UiModule is a HearthModuleBase with Id Core. Another Core module object would run alongside it
- Public hooks in Hearth.Shell.HearthUI: GuestBedProbe, UsageProbes, RenownLine, AddOverviewSection, AddGuestTabSection, InvalidateUsage. GuideChecks.Register adds guide conditions; GuestsPage, UiState and UiStyle are public
- Guide, tutorial level and tab-used state are stored as ui.* keys in HearthProfile.Store (class UiState). There is no component of my own, and the keys are stripped when copying to the template
- Harmony patch classes: Tutor.TutorOnGUI postfix, GameDataSaveLoader.SaveGame postfix, Page_SelectStoryteller.DoWindowContents and PreOpen postfixes. All are skipped and logged by the foundation if a target is missing
- MainButtonDef HH_MainTab uses order 85 with icon UI/HH/UI/MainTab. Because an icon is set, the bar shows the icon only, without text
- The special switches and tier values are registered by the UI module under category 'Special switches' / 'Difficulty'. Registering the same key elsewhere is harmless because HSettings.Add keeps the first one
- ui.windows creates a vanilla visitor group via IncidentDefOf.VisitorGroup. Without the Inn module these visitors act as guests
- Screenshots for visual checks: run the suites with HH_UI_SHOTS=<dir> set in the environment
### not_done
- Real guest-bed and venue-sign visibility in the ui.tab test: no such comp or def exists in this build. The path is covered through HearthUI.GuestBedProbe and UsageProbes, and the test notes the skip
- Concierge tips, renown stars and a handbook (?) button: only hooks exist (HearthUI.RenownLine, OverviewSections). The content belongs to other modules
- Arrest / Execute / Send away measures in the Guest tab: they depend on Inn module rules. GuestTabSections is the hook
- Full 9.3 economic model (D value, Monte Carlo, balance badges): only the key numbers, ladder and warnings are shown
- Real PawnColumnDef table (Numbers compatibility) and grouping by faction on the guests page: it is a custom cached table instead
- Texture art for UI/HH/UI/MainTab and CardHeader: placeholders only, nothing committed under Common/Textures
- Verification of 1.6/1.5 builds: not verified. A multi-version build errors with duplicate TargetFrameworkAttribute in Hearth.Core/obj; the error lists no UI file, I did not diagnose the cause, and the 1.7 build is fine. #if RW17 is used for the world-view check

## venues
### contract_requests
- Venues.RoomOfSign (Source/Hearth/Contracts/Venues.cs): for attachments it jumps to the wall cell (no room, so a wall sign is never found). Placeworker_AttachedToWall puts the attachment on the floor cell facing the wall, so the sign's own cell is the right room. Fix the contract and drop Patch_RoomOfSign_Attachment from VanillaHiding.cs.
- DesignationCategoryDef HH_Hospitality is not defined by the foundation. It is added conditionally by my Common/Patches/Venues_DesignationCategory.xml; the owner (UI/Inn module) should define it in Defs/Core and then the patch becomes a no-op.
- Hearth.Core does not build for 1.6 (net472): CS0579 duplicate TargetFrameworkAttribute, so 1.6 and 1.5 DLLs could not be checked. (Note: source/build.sh only prints lines containing 'error', so a German-locale build prints a mangled line.)
- GuestApi: the docs say a small think-node patch offers activities to vanilla visitors when Inn is off. Nobody provides it yet, so my GuestFallback does it; the Inn agent should either take it over (set GuestFallback.Enabled = false) or keep it.
### integration_notes
- Namespace is Hearth.ShopsFood (not Hearth.Venues, which clashes with the contract class Hearth.Venues). Module entry is VenuesModule (Id ShopsFood); the kitchen agent has its own HearthModuleBase with the same id, which is fine (the module list holds both). My settings use the key prefix 'venues.'.
- Other modules must ask VenueApi.Gate(venue) / IsOpenForBusiness(venue) before serving: it includes opening hours, staff presence and the bell. Venue.IsOpenNow from the contract is hours only. Also useful: NeedsStaff, IsStaffed, Staffer, PostCell, CounterOf, PriceMultiplier, PriceOf, VenueOfSign.
- A VenueTypeDef selling from stock needs <modExtensions><li Class="Hearth.ShopsFood.VenueSaleExtension"><sells>Goods|Drinks|None</sells></li></modExtensions>; restaurant, casino, spa and lounge are set to None so their rooms' goods are never forbidden. The kitchen agent's dishes in a restaurant room are untouched.
- Casino module: add vanilla replacements with VanillaHiding.Register("PokerTable", "HH_CardTable") in OnDefsLoaded or a <Hearth.ShopsFood.VanillaReplacementDef> (vanillaDefName / replacementDefName strings). HH_Venue_Casino and HH_Venue_Spa have module Casino / Wellness and need staff, so their signs only offer them when that module is on.
- Types with needsStaff (tavern, restaurant, casino, spa) make my work giver send colonists to stand at the sign/counter during opening hours; a module with its own staff jobs should keep the same VenueState.PingStaff convention or set needsStaff false in its def.
- Items in the room of a Shop/Tavern sign are forbidden for the colony by vanilla forbidding (venues.* sync every 250 ticks). Venue type must not sell if a module wants the colony to use its shelves freely.
- My tests assume the harness default colony; they call SetPriority on all work types of one colonist and clear leftover visitors (VenueTestKit.ClearGuests). Guest suites take 1-4 minutes of game time each.
- GuestFallback.Enabled can be set false if the Inn module takes over driving vanilla visitors through GuestActivities.
- Vending machine research prerequisite is vanilla 'Electricity'; replace with the HH research tree entry when it exists.
- Textures are not committed; run tools/placeholder_textures.py to create placeholders.
### not_done
- Colonists shopping with colony silver when there are no guests (KONZEPT 6.8, marked optional): left out to keep the till and colony balances clean.
- The sign pushing its goods filter and 'Important' priority onto all stockpiles in the room (N45 'Auffuellen'), and display shelves with a per-item maximum: not in the task list.
- Big market conditions of 6.10 (at least 4 market stalls, renown at least 20, stall fee, announcement with postpone/cancel, thieves and brawls): fixed interval plus 'at least one shop' used instead, as the task allowed. Market stalls and the Market venue type do not exist yet.
- Last orders 30 minutes before closing, the staff roster tab, point-based auto-assignment of tenders, and Bouncer/Croupier/etc. roles: only the generic tend job exists.
- Market / plaza customer sharing (+15 %) and Hemogen/xenotype shops (N50).
- Real art: all textures are placeholders from tools/placeholder_textures.py and are not committed.
- Building the 1.6 and 1.5 DLLs: not verified, and a 1.6 attempt hit a foundation build error in Hearth.Core. Only 1.7 was built and tested.
- Dialogs and overlays were exercised only by opening them and checking for red errors; nobody has looked at them with eyes.

## insurance
### contract_requests
- Research defs SL_Insurance and SL_Actuarial belong to the bank agent (KONZEPT 7.3). I only look them up by name; if missing, all policies are unlocked
- Bank window: please render BankTabs entry Id=insurance (already registered via BankTabs.Register at OnDefsLoaded; Draw = InsuranceUI.Draw)
- Bank agent: premiums use ledger.Post(BankApi.Impl.ColonyAccount, 'insurer:clawback', ...) and payouts post the other way directly instead of calling IBankImpl.Deposit/Withdraw. If the bank keeps its own statement lines, add a ledger Changed listener for account insurer:clawback
- Designation category HH_Bank (needed for the optional SL_InsuranceDesk building)
- Inn module: guest travel insurance can call ClawbackApi.ReportLoss/ReportLiability and InsurancePricing.QuoteFor; the reception sale is not built
### integration_notes
- Defs use full class names in XML: <Silverline.SL_PolicyDef>, <Silverline.SL_ClauseDef>, <Silverline.SL_LimbScheduleDef>. Short names are not found by the game loader
- InsuranceModule is a second HearthModuleBase with Id=ModuleId.Bank next to BankModule. Both are created by reflection; it registers settings (prefix insurance.), the bank tab and a caretaker check. Do not remove it when merging the bank's own module
- Everything is in namespace Silverline (Source/Silverline/Insurance) plus pure code in namespace Hearth.Insurance (Source/Hearth.Core/Pure/Insurance). No reference to Hearth.dll
- InsuranceComponent is a GameComponent saved on its own; ClaimLetter is a ChoiceLetter subclass (letterClass Silverline.ClaimLetter); removing the mod only drops those letters
- Harmony patches: Thing.Kill (prefix, Building only), Pawn.Kill (prefix+postfix), Pawn_HealthTracker.AddHediff (postfix), GameConditionManager.RegisterCondition, Building_CommsConsole.GetFloatMenuOptions; all return early when Bank is off or the instigator is not our faction
- Public hooks for other modules: ClawbackApi.ReportFine / ReportLiability / ReportClosure / ReportLoss, InsurancePricing.QuoteFor, InsuranceComponent.Instance.Sign/CanSign
- Textures: only UI/HH/Insurance/ClawbackLogo; placeholder created locally and not committed (Common/Textures)
- Debug-level lines go to HearthHarbor.log under channel Bank; tests: tools/run_test.sh all <label>; screenshots only with HH_SHOTS=1 insurance.shots
- The tracked DLLs in 1.7/Assemblies show as modified in this worktree; I did not commit them
### not_done
- Insurance desk building (SL_InsuranceDesk): needs the HH_Bank designation category and research prerequisites from the bank agent; the code already counts a def named SL_InsuranceDesk (one review day shorter)
- Panic button (3 mercenaries), accident pension rider (2-20 silver/day), hospital daily allowance, medicine and tow-drone in-kind benefits, Anwaeltin Brix as guest, Rim Health OfferExternalTreatment: the benefit texts avoid promising them but they are not wired
- Muffalo & Machine Cover covers only vehicle-like buildings (class name contains Shuttle/Vehicle); bionic parts lost are not covered, and Vehicle Framework is not integrated
- Wanderlust for guests (reception sales, Selbst tragen, provision) and Regress on stolen guest bags: belong to the Inn module in Hearth.dll
- Overbooked and harder extras not done: premium re-tick on renewal, unreported danger cutting the benefit (clause 26), stolen goods and illness benefits for caravans, 'Why?' ungated 1-year notice only via setting
- Premium Plus is only a greyed hint; payment mode (yearly -6 %, daily +8 %) and the beneficiary / hazard-group extra fields are not implemented
- The UI was never seen on screen (screenshot access was denied); it only draws without errors in the test game, so layout and fonts need a human look
- Premium calibration (baseRisk values) is a first guess and needs long-run balancing; the test colony was too small to check realistic prices

## wave1b_?.json
### contract_requests
- Contract Guest-Wish: an optional GuestInfo field or API for the wish category 'Faith' (8.16) so indulgences count in guest budgets; for now guests buy at the altar from their remaining budget.
- Contract Venues: a VenueTypeDef for the guest chapel (6.1 sign pattern). HH_GuestAltar works standalone, but its room, opening hours and ratings should come from a Venue Sign once Venues exists.
- Core: a DesignationCategoryDef HH_Hospitality (AGENTS rule 9). The guest altar currently sits in the vanilla Ideology category and should move there when it exists.
- Contract Inn: GuestInfo.PlannedLeaveTick extension is used for the shrine stay bonus (+0.5 day, overnight guests only). The Inn module should honour changes to it.
### integration_notes
- The module class DlcModule uses ModuleId.Core (always on). Each feature checks the module it serves: Casino for luck, Inn for hosting and nobles, CityLife for shrines/indulgences/ascension, Bank for the Penny-pincher spawn weight.
- Other modules must call: LuckApi.RtpShiftFor / AdjustRtp (casino payout plan), LuckApi.LottoTicketIsFree (lotto), DlcApi.* (see docs/DLC.md table), NobleDemands.MaxTitleServedBy (bed button), XenotypeHospitality.* (guest rooms/menus), IndulgenceApi and ShrineApi (UI). Nothing else is needed for the already wired effects.
- Patches (Harmony classes, each with an early-out): Patch_TraitCommonality, Patch_HostingLearn, Patch_ServeRest, Patch_ServiceQualityChat, Patch_AscensionCertainty.
- New files outside Source/Hearth/Dlc and Common/Defs/Dlc: Source/Hearth.Core/Pure/DlcMath.cs, Source/Tests/DlcMathTests.cs, Common/Patches/Dlc_IdeologyOffices.xml, Common/Patches/Dlc_PsychicAscension.xml, tools/run_test_nodlc.sh, docs/DLC.md. No existing files were edited.
- XML defs use the class names Hearth.IndulgenceDef, Hearth.XenotypeHospitalityDef and Hearth.PreceptStanceExt (namespace Hearth, like VenueTypeDef), not HH_-prefixed C# names.
- Settings registered: dlc.traitSpawn, dlc.traitRarity, dlc.luckShift, dlc.indulgenceFactor, dlc.indulgenceCapScale, dlc.fundDestination, dlc.aiIdeoPrecepts, dlc.shrineReactions, dlc.nobleStays, dlc.nobleBasePrice, dlc.nobleMoneyFactor (7 tier anchors from 9.2), dlc.mandatoryBilleting, dlc.xenoHospitality. dlc.mandatoryBilleting is my own key; if the Inn module registers the global 'mandatory hospitality' switch, it should replace it.
- Placeholder PNGs live only in the worktree (not committed); regenerate them with uv run --with pillow python3 tools/placeholder_textures.py.
### not_done
- Xenotype events (Midnight soiree, Tunnel tour, Truffle fair and so on) are only stored as eventIds strings in the data defs. No IncidentDefs exist for them. The Hemogen minibar, Deathrest lounge and the other xenotype shop items need the Shops/Kitchen modules.
- Noble parties arrive on foot from the map edge. A shuttle arrival, an audience in the throne room and the 'suitable up to' button on the bed UI are missing; the bed button is only available as the API NobleDemands.MaxTitleServedBy.
- Shrine room style (ThingStyleDef per ideology) and the symbol drawn on the altar are not done. The altar only takes the ideology colour. Chapel quality uses a radius of 6 around the altar, not the full Venue-sign room. Pilgrim-season events and demolition effects beyond a goodwill hit are missing; own colonists of the shrine's faith do not use it as an altar yet.
- No window for faith purchases (only float menus on the gizmo). No Bank-tab 'Pay' button, no Offering box building (the temple fund is a ledger account only), and no payout of the temple fund as silver.
- Guests' 'Faith' wish category in budgets, inspections by Anomaly/Odyssey, and the exploit suite from KONZEPT 11.4 are not done.
- Genes use placeholder icons. Genetic xenotype template 'Hospitaller' and the gene symbol pack are not done.
- The 1.6 and 1.5 builds compile but were not run in-game.
- Trait effects that live in other modules (theft, brawl risk, rumour spread, care progress, bank fee, dish quality, croupier skill and so on) exist only as stats and API calls until those modules call them.
- Dev-mode rimworld save/load round trip of the new GameComponent was reviewed but not exercised in-game.

## wellness
### contract_requests
- Please add a ready-made path so vanilla visitors without the Inn module pick up GuestActivities.TryPick (think-node patch), so wellness.spa works for them in play.
- If the Venues module does not define HH_Venue_Spa with module = Wellness, `WellnessBilling.IsSpaVenueType` matches only by defName HH_Venue_Spa or module == Wellness. Please keep one of the two true.
- HH_Hospitality (DesignationCategoryDef) and the research projects HH_Bathhouse and HH_Wellness: the category is added by a conditional patch (Common/Patches/Wellness_EnsureDesignationCategory.xml, only if missing), the research projects are in Defs/Wellness/Wellness_Research.xml. Please make sure no other module defines HH_Bathhouse or HH_Wellness.
### integration_notes
- Entry point is Hearth.Wellness.WellnessModule (ModuleId.Wellness). It registers the SpaGuestActivity in OnDefsLoaded; it needs HH_Serving from Core and the shared research tab HH_Hospitality.
- The venue sign type (Venues module) should list HH_Venue_Spa; guests only use facilities whose room is that venue's room and while the venue is open.
- Prices are charged through GuestApi.ChargeGuest, so income lands in the colony till; the venue price factor and the sign price factor both apply.
- WellnessState.Current is a GameComponent holding day passes (pawn id, expiry, venue sign id), last session per pawn, session counts and revenue. It is small and safe to drop.
- The UI agent can show wellness stats via WellnessState.Current; WellnessBilling.Price(facility, kind) gives the price a guest would pay.
- Harmony patches (Patch_NoHeatstrokeInSauna, Patch_NoHotThoughtInSauna) cheaply early-out unless the pawn's current job is HH_UseSauna.
### not_done
- HH_SpaPool (optional heated pool) is not built.
- Heating effect of Soothed is a pain factor of 0.85, not the 'chronic pain heals 0.5 % per hour faster' from KONZEPT 6.7, and it does not stack with other mods' healing.
- Full rehab programs from KONZEPT 13.2 (physio, withdrawal, sanatorium, rehab furniture, RimWard discharge offer) are not built. Only the walking-patient recreation bridge from the task exists.
- No sauna benches, no hot springs (Odyssey) and no attraction x1.5 bonus.
- Guests only reach the spa activity through GuestActivities.TryPick. No think-node patch exists in the foundation yet (it belongs to the Inn module or the integrator), so the guest test starts the job directly.
- The test-only venue sign HH_TestOnly_VenueSign is non-buildable but still ships in the Defs; the test-only venue type is created in code.

## underworld
### contract_requests
- Renown API: the Inn module should subscribe to Hearth.Underworld.Underworld.RenownDelta(Map, float, string), or a Renown.Add(map, delta, reason) should be added to the contracts. Until then scandal renown effects are only logged.
- StayKind.Shady (5.2): the Inn module should call Underworld.MarkShady(pawn) for Shady and outlaw stays so they may use the back room door, fence and holding cells. Also multiply arrival weights with Underworld.RespectableArrivalFactor and OutlawArrivalFactor.
- Rooms and venues: Inn/Venues should skip rooms where BackRoom.IsBackRoom(room) is true when rating the house and building zones. Venue sign UI should filter VenueTypeDef by UwDefs.Open(def.GetModExtension<UwGateExtension>()) so the drug lounge stays hidden on Cozy or before discovery.
- Designation category: the underworld buildings use their own HH_Underworld category (defined in Common/Defs/Underworld). The integrator may merge it into HH_Hospitality. VenueTypeDef HH_Venue_DrugLounge uses the shared Hearth.VenueTypeDef class from the Venues contract.
- Guest activity wiring: the module registers three IGuestActivity (underworld.fightpit, underworld.lounge, underworld.fence). The think-node patch that offers activities to vanilla visitors and the Inn guest brain must call GuestActivities.TryPick. Without it, only shady groups are driven (by the module's own director).
- Content warning: the UI/first-start card may call UnderworldComponent.ContentWarningAck = true after its own warning dialog. Otherwise the module shows its own dialog once Content is Dark or Grim.
- Optional: a StayKind.Shady value and an official Renown contract so the Underworld module does not need its own event.
### integration_notes
- Files touched are only new ones: Source/Hearth/Underworld/*.cs, Source/Hearth.Core/Pure/UnderworldMath.cs, Source/Tests/UnderworldMathTests.cs, Common/Defs/Underworld/*.xml, Common/Languages/English/Keyed/HH_Underworld.xml, docs/underworld.md. No existing file was edited.
- Harmony patches: ResearchProjectDef.IsHidden postfix (cheap HashSet check) and Pawn.PreApplyDamage prefix (exits at once while no bout is running). Both are in UwDefs.cs; the damage prefix also uses FightBout.cs.
- XML class names Hearth.VenueTypeDef and Hearth.Underworld.* are used. If another module defines the same defNames as HH_Underworld, HH_TheHunt, HH_BlackClinic or HH_Venue_DrugLounge there would be duplicates.
- Money: accounts house:pit:<mapId> and house:fence:<mapId> are always swept to the till by caretakers. The pit account holds stakes only during a bout.
- Everything is gated: with Content=Cozy or module off, no building is visible and no event fires; the Underworld page is visible only when Unlocked.
- Test hooks: Tests.cs sets Content, discovery and warning flags with UwKit.Begin and restores them with UwKit.End. A combined run shares one map, so suites compare counter deltas, not absolute values.
- Settings keys all start with 'underworld.' and show up automatically in the settings window.
### not_done
- Gang meeting (Parley, temporarily neutral hostile pirates). Gangs come only from existing non-hostile factions, preferring Rough/Outlaw/Pirate names. If none exists, no gang visit fires.
- Debtors' wing and the 'watching' sub-offer of the holding cells tour (paying to watch executions or prisoner bouts).
- Full protection racket event chain; only the single demand dialog exists. Guests rarely react to the fight pit beyond thoughts and satisfaction (no noble departure).
- Hunt fine points: after the hunt, leftover quarry stays as house animals. Human quarry has no start animation or head-start path, only a teleport into the ground and a flee job. The human-quarry death path and the ideology-precept check were not covered by an automated test.
- Drug lounge was tested only without a real venue sign (the Venues module is not in this worktree). Only its driver, activity, price and ideology gate were tested, not an open lounge venue.
- Animals cannot open fence gates, so animal fighters are 'led in' by teleport when the gate does not let them through.
- Research tree coordinates (X 22-31, Y 0-12) are placeholders for the UI/integrator to lay out. Textures are placeholders only. No real art or sounds.
- Not tested in the game: real save/load of a whole save with fence stock (only the component round trip was tested), 1.5 and 1.6 in-game (they compile clean), Inn-driven activity picking, and the Steam content-rule reading of the Dark features.

## city
### contract_requests
- GuestApi: events or a query for guest arrival and departure (e.g. GuestApi.IsDeparting(Pawn) and an Arrived/Departing callback). The taxi currently guesses from GuestInfo.PlannedLeaveTick and the vanilla lord toil name LordToil_ExitMap.
- Core: a shared research mode in HSettings (e.g. core.researchMode with Full/Simplified/Off) so all modules honour one switch. City Life has its own city.researchMode and also reads core.researchMode/core.researchOff if present.
- Venues: a staffing/open query such as Venues.IsStaffed(Venue) or Venue.OpenAndStaffed. City Life estimates staffing itself (any free colonist with HH_Serving active).
- Core or Venues: a single owner for DesignationCategoryDef HH_Hospitality. I ship the same conditional patch as the Venues module (Common/Patches/City_DesignationCategory.xml).
- GuestInfo: a VIP/archetype flag. Bodyguard eligibility uses Wealth >= 60 for now.
- Inn or Core: confirm which module wires vanilla visitors into GuestActivities.TryPick (AGENTS describes a think-node patch that does not exist in this worktree).
### integration_notes
- Tracked build outputs 1.5/1.6/1.7 Assemblies/Hearth.dll are modified by every build; I restored them with git checkout, so the worktree only has untracked Common/Textures. Rebuild after merging.
- VenueTypeDefs use the tag Hearth.VenueTypeDef (same as the Venues worktree). My three venue types (HH_Venue_Cinema, _Salon, _Gallery) need the Venues sign to list them (its allowedTypes empty = all). HH_Venue_Lounge is read softly; a lounge with a projector screen also works as a cinema.
- Tests use HH_VenueSign if the Venues module is present and its comp derives from CompVenueSign; otherwise they clone a vanilla lamp def with a plain CompProperties_VenueSign. Test visitors come from the vanilla VisitorGroup incident, are moved into a calm LordJob_VisitColony and are driven through the activities directly.
- Textures (not committed): Things/HH/City/ProjectorScreen Graphic_Multi, drawSize 3x1, north/south 384x128 and east 128x384; SalonChair Graphic_Multi 1x1, 128x128 (north/east/south); ParkBench Graphic_Multi 2x1, north/south 256x128 and east 128x256; TaxiStand Graphic_Single 2x2, 256x256; TaxiCab Graphic_Single skyfaller (CutoutFlying, drawSize 2.2), 256x256. UI/HH/City/Type_Cinema, Type_Salon, Type_Gallery Graphic_Single icons, 64x64. All are placeholders from tools/city_placeholders.py. Real art can replace the PNGs one-to-one.
- New files outside my folders: Common/Patches/City_DesignationCategory.xml, docs/CITY_LIFE.md, tools/city_placeholders.py. No foundation, contract, About or LoadFolders files were touched.
- CityTab (HearthTabs page id 'city', order 70) is a simple summary page. The UI module may restyle it.
- Settings category names used: General, Research, Cinema, Salon, Gallery, Park, Taxi, Security. Enum label keys HH.Enum.CityResearchMode.* are in Keyed.
### not_done
- No viewpoint building. 'Viewpoint' is covered by the bench's scenery bonus; a separate building would need real art.
- No taxi via a real Royalty/Odyssey shuttle thing and no gravship taxi (KONZEPT 5.19 mentions both). The cab is only a skyfaller visual with no world-map flight, no pilot and no fuel; 'Royalty shuttle or Odyssey tech' is expressed through research prerequisites.
- No beauty clinic, shopping mall, gym, pool, concert hall, night club, petting zoo, fishing pier, sports field or guest chapel from the 6.9 table. They are outside the task list. Salon tattoos and dyes depend on Ideology/Biotech and were exercised only with all DLCs active.
- Bodyguards do not yet count for the security/escort logic of chapter 5.8, because the Inn module is not visible here.
- No save/load round-trip test: the harness has no save/load step. The save paths follow the foundation patterns (Scribe lists, Deep contracts, comp fields).
- Guest activities are only picked automatically once the Inn module or the vanilla-visitor think-node patch calls GuestActivities.TryPick. My tests start the jobs through activity.Desire/TryGiveJob directly.
- Textures are placeholders (not committed). Sounds and effecters beyond vanilla hair-cutting were not added. No popcorn or snack for the cinema. English only; no DefInjected translations.
- ProjectorScreen placeholder east/west art was not checked in-game.

## finance
### contract_requests
- HSettings: a log-scale slider kind with tier interpolation in log space. The profitability setting is stored as ln(M) (finance.profitLog) because DefaultFor() interpolates linearly (100 at Comped would give 51 at tier 0.5). A shared Core widget for the K28 help hint (I wrote a local one, FinUi.HelpHint, with the links in Keyed HH.Finance.Help.Url1/2)
- Bank module: embed BankTabs ids 'invest' (order 40) and 'lottery' (order 50) in the bank window. BankApi.Impl.ColonyAccount must be the ledger account that holds the colony's money (I use FinanceAccounts.Colony = Impl?.ColonyAccount ?? 'bank:colony')
- Inn / Venues: call LotteryGame.Current.SellColonyTickets(n, who) after charging a guest who buys a colony lottery ticket; a renown getter (0-100) so the demand formula can use it instead of the fixed 50
- Hearth world-map work: call FinanceGame.Current.ProjectSabotageStrike(projectId, guardsEscaped) and ProjectHelp(projectId) from the sabotage/delivery quests
- Core: Tills/Payments stay as they are (colony lottery credits the till via Payments.CreditColony); optional Core API for a 'guest crowd size' so Silverline need not count visitor pawns itself
### integration_notes
- Module entry is Silverline.FinanceModule (second HearthModuleBase with ModuleId.Bank next to BankModule); no edits to BankModule.cs. Save data lives in two GameComponents, FinanceGame and LotteryGame, so removing the mod leaves a loadable save
- Ledger accounts: bank:colony (or Impl.ColonyAccount), market:<assetId>, market:omen, market:projects, market:patience, bank:fees:finance, lottery:global:pool / jackpot, lottery:colony:pot / jackpot. market:* may go negative; lottery pots never
- Harmony patch Patch_CommsConsole_Silverline adds the fallback float-menu option only while BankApi.Impl == null or finance.fallbackWindow is on; after the bank window exists it disappears by itself
- Settings keys: finance.profitLog, volMult, jumpMult, hazardMult, cryptoOn, memeOn, nftOn, memeInterval, newsMessages, fallbackWindow, omenGamma/Fee/Spread/CapA/CapB, projectsOn, projectStakePct, projectTotalPct, projectYearCapPct; lottery.globalOn/Price/Quote/Interval, jackpotCap, internationalScale, colonyOn/Price/Quote/Interval/Fee/JackpotShare/SecondShare/ThirdShare/JackpotCap, demandBase
- New XML def type: Silverline.AssetDef; other mods can add products without code. Faction-linked assets only appear in worlds that have that faction
- English texts: edit FinanceText.cs, then run python3 tools/gen_finance_keyed.py. Test hooks (HH_TESTS only): InvestTab.TestSetPage/TestSelect, DrawGlobal(forcedNumbers). 1.5/1.6/1.7 Assemblies in the working tree are rebuilt output and not part of the commits
- Net worth for stake limits = bank cash + portfolio + Omen/project stakes at cost; the project yearly cap uses the net worth at the start of the game year (60 days)
### not_done
- World-map sabotage site 'Project works' (needs site parts, guards, map gen). The bet, the strike effects and the API exist and are tested, but there is no site yet; there is only a dev-mode button
- Colony IPO, Conflict Futures, the Black Ledger pyramid scheme (needs Underworld) and the Omen insider investigation
- Savings contracts: no monthly top-ups; the Patience Bond is a lump sum only
- Religious fund signals (ritual quality, ideology split) and the HH Holdings renown signal (renown lives in Hearth.dll, which this module must not reference)
- NFT floor price decay of -1 %/day outside hype is not modelled (sale delay and spread are)
- Guarantee-jackpot model for the colony lottery, lottery licence and Rigged draw (Underworld)
- Colony lottery has no per-guest tickets or deliveries (demand is aggregated); guests are only counted as reach. Renown in the demand formula is fixed at 50
- War-bond outcome is a hazard coin flip nudged by settlement count; the settlement-attribution rule (player-caused losses do not count) is not implemented
- Layout never seen on screen: the quicktest game runs no OnGUI and ScreenCapture/screencapture produced nothing. The pages were drawn directly in-game without exceptions (12 draws), row widths and scroll panes are reasoned, not eyeballed
- Only English texts; no DefInjected for asset blurbs

## hospcompat
### contract_requests
- Inn module: please adopt imported guest beds through Hearth.Compat.Hospitality.HospitalityImport: subscribe to GuestBedImported(Building_Bed, int feeSilver) in OnDefsLoaded, or read HospitalityImport.PendingBeds after load, then call MarkAdopted(bed). A stable Inn-side method such as GuestApi.MarkGuestBed(Building_Bed, Money fee) would replace my reflection guesses on Hearth.Guests.CompGuestBed (SetGuestBed(bool), or a bool IsGuestBed/Enabled/Active member plus a Fee/RentalFee/Price member).
- Inn module: if Area_HH_GuestWing exists, give it a constructor (AreaManager, string label) or (AreaManager); the importer then copies Hospitality guest areas into wings. HospitalityImport.SettingsFor(map) (GuestsWelcome, RefuseUntilBeds, FreeFood, DefaultEntertain, DefaultMakeFriends) and KnownVisitors (Entertain/MakeFriends flags → HandlingMode) are ready for the Inn to read.
- Integrator: the S7 shim needs a new project Source/HospitalityShim (AssemblyName Hospitality, references Hearth.dll) and LoadFolders entries <li IfModNotActive="Orion.Hospitality">Compat/HospitalityShim/<ver></li>. The design is in docs/hospitality-shim.md section 4. The shim must not define any saved Hospitality class, or the importer would be bypassed.
- Venues/Kitchen/Casino: a replacement table from Hospitality add-on ThingDefs (VendingMachine, MedievalVendingMachine, CashRegister_CashRegister, Gastronomy_DiningSpot, HC_SlotMachine*, GCBVS_VisitorSpot) to our defNames would let the importer map those buildings instead of losing them.
### integration_notes
- Compat is part of the core: HospitalityCompatModule has ModuleId.Core and lives in Hearth.dll (namespace Hearth.Compat.Hospitality). HLog channel 'Compat'. No Common/Defs/Compat files were needed.
- AGENTS.md exception: the takeover uses dynamic Harmony patches (HearthMod.Harmony.Patch/Unpatch), not [HarmonyPatch] classes. They are applied only when Hospitality is detected, only to Hospitality's own methods, and the prefix is a single bool check. In normal games nothing of ours is patched.
- Settings: compat.hospitalityNeverAsk (Global, stored in the template) and compat.importLetter (per save). The smoke suite count therefore rises by 2.
- The dialog is not drawn when the HH_TEST env var is set (it only records DialogOffered). HH_HSP_NOTAKEOVER is compiled only in HH_TESTS builds.
- HospitalityMapStandIn derives from CustomMapComponent on 1.6+; vanilla's FillComponents skips those, so the stand-ins never appear in normal saves. Do not turn them back into plain MapComponents. For the same reason the Hospitality game component is mapped onto our own ImportedGuestBeds (which detects the foreign Class attribute) instead of a stub GameComponent.
- In the tracked 1.5/1.6/1.7 Assemblies, Hearth.dll was rebuilt but not committed; rebuild at integration.
- Running hospcompat.import alone uses the synthetic save. The real-save variant needs the fixture unpacked to /private/tmp/claude-501/hh_tests/hospcompat_fixture.rws (with its .expect) and HH_HSP_REAL=1; HH_HSP_FIXTURE overrides the path. Commands are in docs/hospitality-shim.md.
- Takeover tests need Hospitality Continued linked into the game's Mods folder as _hsp_3509486825 and orion.hospitality passed as an extra mod. My runs created and removed that link; no link is left behind.
### not_done
- The read-only shim assembly (S7) itself: it needs a separate project with AssemblyName=Hospitality and a LoadFolders.xml IfModNotActive entry, which is outside my scope. Only the design is written (docs/hospitality-shim.md section 4).
- Work priorities Diplomat → Hosting: vanilla saves priorities as a DefMap by def index, so the old index of Diplomat cannot be recovered once Hospitality is gone. Only by-name references are mapped.
- Add-on buildings (vending machines, cash register, dining spots, slot machines, Storefront counters) are not mapped to our buildings and their material is not refunded. Their ThingDefs are gone, so vanilla still logs its own 'could not load reference' errors for those things. This needs the Venues/Kitchen/Casino defNames, which are not visible here.
- The 'Import Hospitality settings' button from KONZEPT 15.7 (Hospitality's ModSettings) is not built, because the Inn settings it would map onto do not exist yet.
- Hospitality's guest food policy is dropped, not imported. World-pawn retention for known visitors (WorldPawnGC postfix) is left to the Inn module.
- The add-ons (Casino, Spa, Storefront, Vending, Gastronomy, Cash Register, Room Service, Invite to Stay) were not loaded in a test. They are covered by package id, namespace and def-name tables only.
- Taking Hospitality data over live while Hospitality is still active (rather than on a later load) only records its guest beds; it does not convert them while Hospitality's classes are still running.

## dlc
### contract_requests
- Contract Guest-Wish: an optional GuestInfo field or API for the wish category 'Faith' (8.16) so indulgences count in guest budgets; for now guests buy at the altar from their remaining budget.
- Contract Venues: a VenueTypeDef for the guest chapel (6.1 sign pattern). HH_GuestAltar works standalone, but its room, opening hours and ratings should come from a Venue Sign once Venues exists.
- Core: a DesignationCategoryDef HH_Hospitality (AGENTS rule 9). The guest altar currently sits in the vanilla Ideology category and should move there when it exists.
- Contract Inn: GuestInfo.PlannedLeaveTick extension is used for the shrine stay bonus (+0.5 day, overnight guests only). The Inn module should honour changes to it.
### integration_notes
- The module class DlcModule uses ModuleId.Core (always on). Each feature checks the module it serves: Casino for luck, Inn for hosting and nobles, CityLife for shrines/indulgences/ascension, Bank for the Penny-pincher spawn weight.
- Other modules must call: LuckApi.RtpShiftFor / AdjustRtp (casino payout plan), LuckApi.LottoTicketIsFree (lotto), DlcApi.* (see docs/DLC.md table), NobleDemands.MaxTitleServedBy (bed button), XenotypeHospitality.* (guest rooms/menus), IndulgenceApi and ShrineApi (UI). Nothing else is needed for the already wired effects.
- Patches (Harmony classes, each with an early-out): Patch_TraitCommonality, Patch_HostingLearn, Patch_ServeRest, Patch_ServiceQualityChat, Patch_AscensionCertainty.
- New files outside Source/Hearth/Dlc and Common/Defs/Dlc: Source/Hearth.Core/Pure/DlcMath.cs, Source/Tests/DlcMathTests.cs, Common/Patches/Dlc_IdeologyOffices.xml, Common/Patches/Dlc_PsychicAscension.xml, tools/run_test_nodlc.sh, docs/DLC.md. No existing files were edited.
- XML defs use the class names Hearth.IndulgenceDef, Hearth.XenotypeHospitalityDef and Hearth.PreceptStanceExt (namespace Hearth, like VenueTypeDef), not HH_-prefixed C# names.
- Settings registered: dlc.traitSpawn, dlc.traitRarity, dlc.luckShift, dlc.indulgenceFactor, dlc.indulgenceCapScale, dlc.fundDestination, dlc.aiIdeoPrecepts, dlc.shrineReactions, dlc.nobleStays, dlc.nobleBasePrice, dlc.nobleMoneyFactor (7 tier anchors from 9.2), dlc.mandatoryBilleting, dlc.xenoHospitality. dlc.mandatoryBilleting is my own key; if the Inn module registers the global 'mandatory hospitality' switch, it should replace it.
- Placeholder PNGs live only in the worktree (not committed); regenerate them with uv run --with pillow python3 tools/placeholder_textures.py.
### not_done
- Xenotype events (Midnight soiree, Tunnel tour, Truffle fair and so on) are only stored as eventIds strings in the data defs. No IncidentDefs exist for them. The Hemogen minibar, Deathrest lounge and the other xenotype shop items need the Shops/Kitchen modules.
- Noble parties arrive on foot from the map edge. A shuttle arrival, an audience in the throne room and the 'suitable up to' button on the bed UI are missing; the bed button is only available as the API NobleDemands.MaxTitleServedBy.
- Shrine room style (ThingStyleDef per ideology) and the symbol drawn on the altar are not done. The altar only takes the ideology colour. Chapel quality uses a radius of 6 around the altar, not the full Venue-sign room. Pilgrim-season events and demolition effects beyond a goodwill hit are missing; own colonists of the shrine's faith do not use it as an altar yet.
- No window for faith purchases (only float menus on the gizmo). No Bank-tab 'Pay' button, no Offering box building (the temple fund is a ledger account only), and no payout of the temple fund as silver.
- Guests' 'Faith' wish category in budgets, inspections by Anomaly/Odyssey, and the exploit suite from KONZEPT 11.4 are not done.
- Genes use placeholder icons. Genetic xenotype template 'Hospitaller' and the gene symbol pack are not done.
- The 1.6 and 1.5 builds compile but were not run in-game.
- Trait effects that live in other modules (theft, brawl risk, rumour spread, care progress, bank fee, dish quality, croupier skill and so on) exist only as stats and API calls until those modules call them.
- Dev-mode rimworld save/load round trip of the new GameComponent was reviewed but not exercised in-game.
