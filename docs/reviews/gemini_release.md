(1) THE 10 MOST IMPORTANT RELEASE-DAY RISKS FOR PLAYERS

1. Hospitality co-existence conflict and save-import crash when both mods are loaded
Where in the code:
- About/About.xml (lines 46-48)
- Source/Hearth/Compat/Hospitality/HospitalityTakeover.cs (lines 292-302, RemoveInjectedComps and SilenceComponents)
- Source/Hearth/Compat/Hospitality/HospitalityImporter.cs (lines 184-189, MapType)
- Source/Hearth/Compat/Hospitality/ImportedGuestBeds.cs (lines 218-227, TryMarkInnComp)
What goes wrong for players:
First, About/About.xml declares <incompatibleWith><li>Orion.Hospitality</li></incompatibleWith>. RimWorld's mod manager and external managers (RimSort, RimPy) treat this as a hard incompatibility error and refuse or warn against enabling both mods together, which prevents players from ever reaching your in-game Hospitality takeover dialog (Dialog_HospitalityTakeover.cs).
Second, if a player forces both mods active and clicks "Later" or "Don't ask again" in Dialog_HospitalityTakeover, and then loads an existing Hospitality save: HospitalityImporter.MapType checks if (GenTypes.GetTypeInAnyAssembly(cls) != null) return null; (line 188). Because Hospitality.dll is loaded in memory, MapType does NOT convert Hospitality.LordJob_VisitColony, Hospitality.Building_GuestBed, or Hospitality.JobDriver_* into Hearth & Harbor stand-ins. Vanilla instantiates live Hospitality objects, while HospitalityTakeover.RemoveInjectedComps (line 299) has stripped Hospitality.CompGuest from ThingDefOf.Human and SilenceComponents only silenced MapComponent/GameComponent/WorldComponent/Alert (not LordToil.LordToilTick or Building_GuestBed). Live Hospitality visit lords and guest beds then tick against pawns without CompGuest and throw NullReferenceExceptions.
Third, in ImportedGuestBeds.TryMarkInnComp (line 218), reflection looks for SetGuestBed(bool), whereas CompHH_GuestBed.SetGuestBed(bool on, bool byPlayer = true) in Source/Hearth/Guests/GuestBeds.cs (line 42) has signature (bool, bool), and the fallback field loop (line 220) looks for "IsGuestBed" or "isGuestBed" instead of the actual private field "guestBed". It only succeeds today because InnModule also subscribes to GuestBedImported.
Concrete fix:
- Remove <incompatibleWith><li>Orion.Hospitality</li></incompatibleWith> from About/About.xml (keep <loadBefore>).
- In HospitalityImporter.MapType and MapDefName, still intercept and convert Hospitality save types/defs when HospitalityTakeover.Active is true (not only when GenTypes.GetTypeInAnyAssembly(cls) == null).
- In ImportedGuestBeds.TryMarkInnComp, invoke SetGuestBed with two arguments (true, false) or include "guestBed" in the field-name array, and if the imported fee is 0, set PriceMode = BedPriceMode.Free in InnModule.AdoptImportedBed (Source/Hearth/Guests/InnModule.cs, line 90).

2. Switching modules off per save leaves all their buildings, research projects, work types, and recipes visible and buildable
Where in the code:
- Source/Hearth/Underworld/UwDefs.cs (lines 58-83, PlaceWorker_UnderworldGate and Patch_ResearchHidden)
- Source/Hearth.Core/Foundation/Modules.cs (lines 40-54)
- Common/Defs/*/*_Buildings.xml and Common/Defs/*/*_Research.xml
- Common/Defs/Hospitality/Hospitality_Shared.xml (HH_Hosting, HH_Serving)
What goes wrong for players:
Both Workshop descriptions promise that switching a module off removes its buildings and research (HearthHarbor.bbcode.txt line 83: "Off means no buildings, no research, no events"; Silverline.bbcode.txt line 5: "switch all guest modules off"). In code, only the Underworld module hides its buildings and research via PlaceWorker_UnderworldGate and Patch_ResearchHidden. If a player picks the "Cozy Inn" preset on the First-Start Card (which disables Casino, Wellness, CityLife, and Bank/Insurance/Investments/Lottery) or disables guest modules to play Silverline-only, every Casino table, Slot machine, Hot tub, Sauna, ATM, Vault building, and every research project in HH_Hospitality and HH_Banking remains visible and buildable. Worse, if a player builds a Casino table or Hot tub while its module is off, colonists haul materials and construct a completely dead building because CompCasinoGame, WorkGiver_Wellness, and JoyGiver_Gambling early-out when Modules.On(...) is false.
Concrete fix:
- Generalize Patch_ResearchHidden (Source/Hearth/Underworld/UwDefs.cs, line 70) and add a Harmony postfix on Designator_Build.Visible (or attach a module-gating PlaceWorker to all HH_/SL_ BuildableDefs at startup in HearthMod) using a prefix/def-to-ModuleId lookup table.
- Call DesignationCategoryDef.ResolveDesignators() / dirty the Architect cache whenever HearthProfile.SetModule changes a module's state.

3. Royalty noble stays break the "Nothing Forced (N08)" rule and visiting nobles cannot claim or sleep in any bed
Where in the code:
- Source/Hearth/Dlc/Nobles.cs (lines 54-64, NobleDemands.GuestBeds and HasSuitableBedroom; lines 202-222, NobleStays.StartStay; lines 309-341, IncidentWorker_NobleStay and IncidentWorker_ImperialBilleting)
- Source/Hearth/Guests/GuestPatches.cs (lines 47-55, Patch_RestUtility_IsValidBedFor)
- Common/Defs/Dlc/Dlc_Nobles.xml (lines 11-36)
What goes wrong for players:
First, NobleDemands.GuestBeds(Map map) (Nobles.cs, line 54) iterates map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>() and checks only !b.Medical && !b.ForPrisoners. It never checks Hearth.Guests.GuestBeds.IsGuestBed(b). As a result, in a colony that has built zero guest beds and zero venue signs, as soon as any colonist has a decent bedroom (day 18+, population 3+), the storyteller fires HH_NobleStay or HH_ImperialBilleting, violating Rule 8 (N08: "until you mark a guest bed or put up a sign, the game plays like vanilla").
Second, when the player accepts a noble stay, NobleStays.StartStay (Nobles.cs, line 217) spawns the noble and guards under a vanilla LordJob_VisitColony and never registers them in GuestRegistry. Because they are not registered in GuestRegistry, Patch_RestUtility_IsValidBedFor (GuestPatches.cs, line 50: if (!isGuest) { if (guestBed) __result = false; return; }) forbids them from using guest beds, while vanilla forbids non-colonists from using colonist beds. The noble and retinue loiter outdoors for 2 to 5 days without ever sleeping in a room, NobleDemands.RoomMeets is never evaluated during the stay, and AverageSatisfaction (Nobles.cs, line 224) reads the default 0.0 fallback from GuestApi.Info(p).
Concrete fix:
- In NobleDemands.GuestBeds(Map map), return Hearth.Guests.GuestBeds.On(map) (and in IncidentWorker_ImperialBilleting.CanFireNowSub, require GuestBeds.CountOn(map) > 0 || Venues.All(map).Count > 0).
- In NobleStays.StartStay, register the noble and retinue via GuestArrival.Arrive (or GuestRegistry.Current.Register with LordJob_HH_GuestStay) and assign a title-compliant guest bed, or evaluate NobleDemands.RoomMeets in NobleStayRecord.DailyCheck to adjust satisfaction.

4. Most DLC features, traits, genes, xenotype tastes, and the LuckApi are unwired outside unit tests
Where in the code:
- Source/Hearth/Dlc/DlcModule.cs (lines 143-238, DlcApi and LuckApi)
- Source/Hearth/Dlc/Xenotypes.cs (lines 65-125, XenotypeHospitality)
- Source/Hearth/Casino/Addiction.cs (lines 167-179, Gambling.Factor)
- Source/Hearth/Casino/CasinoBank.cs (lines 100 and 113)
- Source/Hearth/Casino/CasinoEngine.cs and CompCasinoGame.cs
- Source/Silverline/Lottery/LotteryGame.cs (line 128)
- Common/Defs/Dlc/Dlc_Traits.xml and Common/Defs/Dlc/Dlc_Genes.xml
What goes wrong for players:
The DLC module defines 14 traits, 9 Biotech genes, XenotypeHospitality, DlcApi, and LuckApi, and tests their math directly in Source/Hearth/Dlc/DlcTests.cs. However, almost none of those entry points are called by gameplay code:
- LuckApi (RtpShiftFor, AdjustRtp, WillGamble, JoyFactor, AddictionFactor, LottoTicketIsFree, NoteResult) is never called outside DlcTests.cs. Pawns with HH_Lucky or HH_Unlucky receive no RTP shift in CasinoEngine/CompCasinoGame, no free 10th lottery ticket in LotteryGame.BuyGlobalTicket, and never get the HH_LuckyStreak or HH_OfCourseILost thoughts. Pawns with HH_GamblingAverse still gamble because JoyGiver_Gambling and CasinoGuestActivity never call LuckApi.WillGamble(pawn).
- In Addiction.cs (lines 167-179) and CasinoBank.cs (lines 100, 113), the code looks up trait "Gambler" and genes "AddictionImmune" / "AddictionSensitivity" instead of your actual defs "HH_Gambler", "HH_Gene_AddictionProofReward", and "HH_Gene_Compulsive".
- XenotypeHospitality (For, RoomScore, InterestWeight, SpendFactor, PriceToleranceOffset, NudgeGuests) is never called in DlcState.RareTick, GuestEconomy, or VenueShopping.
- 11 of the 14 traits and 6 of the 9 genes have player-facing descriptions promising effects (e.g. HH_PennyPincher lower bank fees, HH_SilverTongue lower guest expectations and cheaper recruitment, HH_Gene_RefinedPalate +10% dish quality lift and tips, HH_EagleEye, HH_SteadyHands, HH_Caregiver, HH_Peacemaker, HH_Gossip, HH_KidMagnet, HH_Gene_NumberSense, HH_Gene_DexterousFingers, HH_Gene_HighRoller) whose DlcApi methods are never called anywhere outside DlcTests.cs.
Concrete fix:
- Call LuckApi.WillGamble in JoyGiver_Gambling.CanBeGivenTo and CasinoGuestActivity.Desire; apply LuckApi.AdjustRtp in CompCasinoGame.Paytable and LuckApi.NoteResult after CasinoEngine rounds; call LuckApi.LottoTicketIsFree in LotteryGame.BuyGlobalTicket; call LuckApi.AddictionFactor in Gambling.Factor (Addiction.cs, line 167).
- Fix "Gambler" -> "HH_Gambler" in CasinoBank.cs (lines 100, 113).
- Call XenotypeHospitality.NudgeGuests(map) in DlcState.RareTick (DlcModule.cs, line 114), and wire XenotypeHospitality.SpendFactor / PriceToleranceOffset / InterestWeight into GuestArrival and VenueShopping.
- Wire DlcApi.DishQualityLift into DishEffects.RerollQuality (Source/Hearth/Kitchen/DishEffects.cs, line 81), DlcApi.GuestExpectationOffset into GuestEconomy.Expectation (Source/Hearth/Guests/GuestEconomy.cs, line 233), and DlcApi.BankFeeFactor into Silverline fee calculations.

5. Twelve of the fourteen "Special switches" on the Difficulty Panel do nothing, and seven "Live calculator" rows reference wrong setting keys
Where in the code:
- Source/Hearth/UI/UiModule.cs (lines 28-55)
- Source/Hearth/UI/DifficultyPanel.cs (lines 20-24, SpecialKeys; lines 75-99)
- Source/Hearth/UI/TierMath.cs (lines 85-103, TierTable.Rows)
- Source/Silverline/Bank/BankSettings.cs (line 51, bank.defaultFactor)
What goes wrong for players:
First, DifficultyPanel draws 14 prominent toggle chips under "Special switches". Twelve of them are never read anywhere in the codebase: core.neverDie ("Guests never die"), core.noHostile ("No hostile guests ever"), core.infiniteMoney ("Infinite guest money"), core.picky ("Picky guests"), core.feuding ("Feuding factions"), core.mandatory ("Mandatory hosting"), core.strictInspectors ("Strict inspectors"), core.realConsequences ("Real consequences"), core.ironBank ("Iron bank"), core.peacefulUnderworld ("Peaceful underworld"), and core.perfMode ("Performance mode"). Only core.sealedFate, core.noResearchGating, and core.showMath actually work. Six additional settings (core.consequenceStage, core.researchCost, core.upkeep, core.houseStyle, core.keepBuiltStyle, core.perfGuardTps, plus bank.defaultFactor) are also unread stubs.
Second, in TierTable.Rows (TierMath.cs, lines 85-103), 7 of the 16 rows in the "Live calculator" pass wrong or non-existent setting keys:
- "inn.expectation" (actual registered key in InnSettings.cs: "inn.expectationOffset")
- "casino.slotEdge" (actual registered key in CasinoSettings.cs: "casino.slotRtp")
- "bank.interestM" (actual registered key in BankSettings.cs: "bank.profit")
- "bank.collectionStage" (actual registered key in BankSettings.cs: "bank.maxCollectionStage")
- "casino.addictionChance" (actual registered key in CasinoSettings.cs: "casino.addictionP0")
- "inn.trouble" and "casino.detection" (no corresponding registered setting keys)
Because HSettings.TryGet fails on those keys, the Live Calculator marks them "· reference" and ignores player overrides.
Concrete fix:
- Update the 5 mismatched setting keys in TierTable.Rows (TierMath.cs, lines 88-100).
- Wire the core special switches (e.g. core.infiniteMoney in GuestArrival.RollBudget, core.picky in GuestEconomy.Expectation, core.noHostile/core.feuding in GuestArrival, core.ironBank in BankSettings.MaxCollectionStage, core.peacefulUnderworld in Underworld incidents, core.researchCost in ResearchGate) or comment out unimplemented switches from DifficultyPanel.SpecialKeys before release.

6. Restaurant venues ignore staffing/night-bell rules, do not record sales, let colonists eat restaurant stock, and turn vanilla dining rooms into uninvited shops
Where in the code:
- Source/Hearth/Kitchen/Restaurant.cs (lines 61-63, OpenGate and VenuePriceFactor; lines 75-100, Spots; lines 207-246, OnPlateEaten and OnVanillaEaten)
- Common/Defs/Venues/Venues_Types.xml (lines 40-55, HH_Venue_Restaurant)
- Source/Hearth/Venues/VenuesModule.cs (lines 111-116, SyncForbidden)
- Source/Hearth/Venues/CompVenueSignHH.cs (line 247)
What goes wrong for players:
First, in Restaurant.Spots(Map map) (Restaurant.cs, lines 88-99), if the map has zero HH_Venue_Restaurant signs, every vanilla Dining Room on the map becomes a fallback RestaurantSpot. Any HH_Plate_* dish cooked by a colonist and placed in a dining room will be bought and eaten by visitors even when the player has never built a single Venue Sign (violating Rule 8 / N08).
Second, VenuesModule never assigns Restaurant.OpenGate = VenueApi.IsOpenForBusiness or Restaurant.VenuePriceFactor = VenueApi.PriceMultiplier. As a result, a Restaurant sign displays "open, but nobody on duty" in its inspect pane and requires a Serving colonist, yet guests eat and buy plates there while nobody is on duty, night-bell surcharges are ignored, and Restaurant.OnPlateEaten / OnVanillaEaten never call (spot.Venue?.Comp as CompVenueSignHH)?.RecordSale(...), so Restaurant signs always show 0 sales and 0 silver today.
Third, HH_Venue_Restaurant in Venues_Types.xml (line 53) has <sells>None</sells>. Because VenueApi.SellsOf(v) is SellKind.None, VenueMapComp.SyncForbidden (VenuesModule.cs, line 113) never forbids dishes in a Restaurant room for colonists (colonists eat the restaurant's expensive guest dishes for free), and CompVenueSignHH (line 247) hides the "Restock" and "Colony may take goods" gizmos on Restaurant signs.
Concrete fix:
- Remove the vanilla dining-room fallback in Restaurant.Spots (lines 88-99) so food is only sold in rooms with a Restaurant or Tavern venue sign.
- In VenuesModule.OnDefsLoaded, assign Restaurant.OpenGate = VenueApi.IsOpenForBusiness; and Restaurant.VenuePriceFactor = VenueApi.PriceMultiplier;.
- In Restaurant.OnPlateEaten and OnVanillaEaten, call (spot.Venue?.Comp as CompVenueSignHH)?.RecordSale(paid, dish.label, eater.LabelShort).
- Treat HH_Venue_Restaurant in CompVenueSignHH and VenueSales.Stock as holding cooked plates/meals so SyncForbidden and Restock work for restaurants.

7. Stove bill menu bloat (~300 generated recipes) and double-counting of plated dishes on shelves in "Do until you have X" bills
Where in the code:
- Source/Hearth/Kitchen/RecipeBuilder.cs (lines 19-34, RecipeWorkerCounter_Dish.CountProducts; lines 94-204, BuildAll)
- Source/Hearth/Kitchen/DishKnowledge.cs (lines 44-62)
What goes wrong for players:
First, RecipeWorkerCounter_Dish.CountProducts (RecipeBuilder.cs, lines 21-33) counts all spawned plates via bill.Map.listerThings.ThingsOfDef(plateDef) AND THEN loops over bill.Map.haulDestinationManager.AllHaulSourcesListForReading (every shelf and storage building on the map) and adds their inner things again. In RimWorld, items stored on a Building_Storage (shelf) are already spawned on the map and are already inside listerThings.ThingsOfDef(plateDef). Therefore, every plate stored on a shelf is counted twice by "Do until you have X" bills (1 plate on a shelf counts as 2), and scanning every haul source on the map on every bill check wastes CPU on large bases.
Second, RecipeBuilder.BuildAll injects ~300 dish recipes (100 DishDefs x 3 variants) directly into ElectricStove, FueledStove, Campfire, and TableButcher. Even when a player has not built any custom kitchen stations (PrepCounter, StockPot, ColdStation, etc.), ~50 Level-1 recipes appear immediately in the vanilla stove's "Add Bill" menu (and all ~300 appear once research completes or when Research mode is Off). If a player clicks one whose side station is not in the room, the bill sits silently suspended ("Missing: Prep counter").
Concrete fix:
- In RecipeWorkerCounter_Dish.CountProducts (RecipeBuilder.cs, lines 24-32), remove the AllHaulSourcesListForReading loop (base listerThings.ThingsOfDef(plateDef) already includes items on shelves).
- In Patch_RecipeAvailableNow (Source/Hearth/Kitchen/KitchenPatches.cs, line 23), hide recipes when !Modules.On(ModuleId.ShopsFood), and consider adding a setting ("kitchen.hideMissingStationRecipes") to hide dish bills from the stove float menu until at least one of each required side station exists on the map.

8. Content intensity "Dark" and "Grim" lack an immediate confirmation dialog when clicked in Mod Settings
Where in the code:
- Source/Hearth/UI/SettingsWindow.cs (lines 316-320)
- Source/Hearth/Underworld/Discovery.cs (lines 139-157, UwWarnings.ContentWarning)
- Source/Hearth/Underworld/UwTicker.cs (lines 35-42)
What goes wrong for players:
HearthHarbor.bbcode.txt (line 74) states: "Content intensity is a setting: Cozy, Standard, Dark or Grim. Dark and Grim add hunting people and organ trade, and only apply after a warning you confirm."
In SettingsWindow.DrawModules (SettingsWindow.cs, line 318), clicking the "Dark" or "Grim" button executes target.Content = c; immediately without showing any confirmation dialog. The warning dialog (UwWarnings.ContentWarning) only fires later inside UwTicker.Tick while an active game is unpaused/ticking AND Underworld.ModuleActive is true. If a player changes the template at the main menu, or switches to Dark/Grim in Mod Settings before Underworld is discovered, no confirmation dialog appears when clicking the button. Furthermore, Kitchen's "Dark menu (long pork)" (kitchen.darkMenu in Source/Hearth/Kitchen/KitchenModule.cs, line 47) is a separate boolean setting rather than being gated by ContentIntensity.Dark/Grim.
Concrete fix:
- In SettingsWindow.DrawModules (SettingsWindow.cs, line 318), when c >= ContentIntensity.Dark and target.Content < ContentIntensity.Dark, open a Dialog_MessageBox confirmation before setting target.Content = c (and if confirmed in-game, set Underworld.Comp.ContentWarningAck = true).
- Also gate KitchenModule.DarkMenu (KitchenModule.cs, line 59) on HearthProfile.Current?.Content >= ContentIntensity.Dark || HSettings.GetBool("kitchen.darkMenu").

9. Venues.RenownGetter is never wired to GuestApi.Renown(), breaking renown gating for Market Days and Colony Lottery
Where in the code:
- Source/Hearth/Contracts/Venues.cs (lines 254-255, Venues.RenownGetter)
- Source/Hearth/Venues/VenueMarket.cs (line 41, float renown = Venues.Renown();)
- Source/Hearth/Guests/InnModule.cs (lines 56-87, WireOtherModules)
- Source/Silverline/Lottery/LotteryGame.cs (line 204, Renown = 50)
What goes wrong for players:
Contracts/Venues.cs defines Venues.RenownGetter (returning 50f when null), while Contracts/GuestApi.cs defines IGuestRenownImpl (GuestApi.Renown()). InnModule implements IGuestRenownImpl on GuestApi.Impl, but never sets Venues.RenownGetter = () => GuestApi.Renown();.
Consequently:
- VenueMarket.Blocker (VenueMarket.cs, line 41) always sees renown == 50f instead of the colony's actual renown (which starts at 10f in GuestRegistry.StartRenown, below the default venues.marketRenown = 20 threshold). Market Days fire even when a brand-new 1-star inn has 10 renown or 0 renown.
- LotteryGame.DemandFor (LotteryGame.cs, line 204) hardcodes Renown = 50 instead of reading colony renown, so colony renown never scales lottery ticket demand.
Concrete fix:
- In Venues.Renown() (Source/Hearth/Contracts/Venues.cs, line 255), fall back to GuestApi.Renown() (or set Venues.RenownGetter = GuestApi.Renown in InnModule.WireOtherModules).
- Expose a Renown getter on BankApi or HearthGame so LotteryGame.DemandFor uses the live colony renown when the Inn module is active.

10. Preview version strings in release metadata and TaxiService.TryArriveByTaxi despawning guests mid-Lord
Where in the code:
- About/About.xml (line 19: "Preview build 0.9.0 – not yet released.")
- Source/Hearth.Core/Foundation/HearthMod.cs (line 58: public const string Version = "0.9.0-preview";)
- Source/Hearth/City/Taxi.cs (lines 155-184, TryArriveByTaxi and Tick)
What goes wrong for players:
First, About/About.xml still starts with "Preview build 0.9.0 – not yet released." and HearthMod.Version is "0.9.0-preview", which appears in the mod manager, settings header, and support report.
Second, in TaxiService.Tick (Taxi.cs, line 181), every 60 ticks it scans guests who arrived within the last 400 ticks near the map edge and calls TryArriveByTaxi(g). TryArriveByTaxi (lines 160-161) calls guest.DeSpawn() and wraps the pawn in a Skyfaller (HH_TaxiIncoming) AFTER the guest has already been spawned and added to a Lord. In RimWorld, calling DeSpawn() on a pawn notifies Lord.Notify_PawnLost; for vanilla visitors (when Inn is off and vanilla LordJob_VisitColony is used) or during lord transitions, despawning a pawn removes it from its Lord so that when the taxi skyfaller impacts 2.2 seconds later and respawns the pawn, the pawn has no Lord and wanders aimlessly on the landing pad.
Concrete fix:
- Update About/About.xml and HearthMod.Version to "1.0.0" and remove the "Preview build 0.9.0 – not yet released." line.
- In TaxiService.TryArriveByTaxi, remember guest.GetLord() before calling guest.DeSpawn(), and inside Skyfaller_Taxi.Impact (or an arrival callback after base.Impact() respawns the inner pawn), re-add the pawn to that Lord if pawn.GetLord() == null.


(2) PROBLEMS IN THE WORKSHOP DESCRIPTIONS

Files reviewed:
- ../_Release/Beschreibungen/final/HearthHarbor.bbcode.txt
- ../_Release/Beschreibungen/final/Silverline.bbcode.txt

A. Claims that the code does not currently back
1. "Can I turn the casino off? Yes, per save, like every module. Off means no buildings, no research, no events." (HearthHarbor.bbcode.txt, line 83) and "Use the bank inside Hearth & Harbor and switch all guest modules off" (Silverline.bbcode.txt, line 5)
- As detailed in Risk #2 above, only the Underworld module hides its buildings and research when off. Switching off Casino, Wellness, CityLife, ShopsFood, Inn, or Bank leaves all of their buildings in the Architect menu and all of their research in the Research tree. Either apply the concrete fix in Risk #2 before upload or change the description to say "Off means no events, no guest desires, and no background ticks."

2. "Hospitality active as well? Hearth & Harbor takes over, Hospitality goes quiet, and a dialog offers to disable it. Old Hospitality saves load with guest beds, fees and guest areas kept." (HearthHarbor.bbcode.txt, line 70)
- Three issues in current code:
  a) About/About.xml marks Orion.Hospitality under <incompatibleWith>, blocking users from loading both mods together to get the takeover dialog.
  b) Rental fees from old Hospitality beds are NOT applied to bed pricing: InnModule.AdoptImportedBed (Source/Hearth/Guests/InnModule.cs, line 96) logs "old fee {fee} silver; our own pricing applies" and leaves the bed on automatic room-score pricing (even if the Hospitality bed was set to 0 silver / free). Either convert fee == 0 to BedPriceMode.Free and fee > 0 to BedPriceMode.Manual, or change "guest beds, fees and guest areas kept" to "guest beds and guest areas kept (bed prices switch to Hearth & Harbor's room-score pricing)".
  c) Running guest visits in an imported Hospitality save are cleanly sent home on load (HospitalityImportStubs.cs, line 40) rather than continuing mid-stay; mentioning "guests currently on the map walk home and new groups arrive under Hearth & Harbor" will prevent players from reporting departing guests as a bug.

3. "Royalty: nobles book stays and want rooms fit for their title... Biotech: new genes, xenotypes with their own taste. Plus 14 traits, no DLC needed." (HearthHarbor.bbcode.txt, line 53)
- As detailed in Risks #3 and #4, Royalty noble stays currently fire even in colonies without guest beds, visiting nobles spawn as vanilla visitors who cannot claim guest beds, Biotech's XenotypeHospitality is never called during gameplay, and most of the 14 traits and 9 genes have their DlcApi/LuckApi effects unwired outside DlcTests.cs. These must be wired before making this claim on the Workshop page.

4. "Content intensity is a setting: Cozy, Standard, Dark or Grim. Dark and Grim add hunting people and organ trade, and only apply after a warning you confirm." (HearthHarbor.bbcode.txt, line 74)
- Clicking Dark or Grim in Mod Settings (SettingsWindow.cs, line 318) applies immediately without a confirmation dialog (the warning dialog only pops up later during an unpaused game tick while Underworld is active).

B. Unclear wording
1. Underworld default state (HearthHarbor.bbcode.txt, line 47 vs README.md, line 16)
- HearthHarbor.bbcode.txt line 47 says: "By default you do not switch this on. One night a stranger asks whether there is a back room." Meanwhile README.md line 16 says: "Underworld (off by default)". In code (Modules.DefaultOn), Underworld is ON by default in "Discover" mode (hidden until day 25+ when a shady guest makes an offer). Saying "By default you do not switch this on" makes players think the module checkbox in Settings is unchecked by default, or confuses players who want zero underworld offers and need to know they should uncheck Underworld (or set Content Intensity to Cozy). Clarify: "In default Discover mode, its buildings and research stay hidden until a shady guest makes a quiet offer—which you can decline, or disable completely in Settings."

2. Standalone Silverline removal instructions (Silverline.bbcode.txt, line 58)
- HearthHarbor.bbcode.txt (line 68) gives clear removal steps ("To remove: Settings, Diagnosis, Prepare removal, save, then unsubscribe"). Silverline.bbcode.txt (line 58) only says "Safe to add mid-game" and omits how to remove it. In standalone Silverline (where Hearth.dll is absent and SimpleSettingsWindow.cs is used), there is no "Diagnosis" tab—instead, players trigger PrepareRemoval by unchecking the Bank, Insurance, Investments, and Lottery module checkboxes in Mod Settings inside their loaded save and clicking "Prepare removal now". Explain this explicitly in Silverline.bbcode.txt.

3. "100 dishes across five cooking levels" on vanilla stoves (HearthHarbor.bbcode.txt, line 23)
- Players reading "100 dishes across five cooking levels" and seeing 50+ new bills on their electric stove at game start will wonder why bills say "Missing: Prep counter". One sentence explaining that recipes are cooked at a Stove while nearby side stations (Prep Counter, Stock Pot, Oven, Cold Station, Dessert Bar) unlock specific dish bills in that kitchen room will save dozens of confused Workshop comments.

C. Missing info players expect on the Steam Workshop
1. Compatibility with popular RimWorld mods (especially ones you already coded support for!):
- Dubs Bad Hygiene: You already built soft integration in GuestBedPricing.BathModActive (Source/Hearth/Guests/GuestBeds.cs, line 361) giving +10 room score for attached bathrooms. Mentioning Dubs Bad Hygiene compatibility in the Workshop description is a major plus for Hospitality players.
- Vanilla Cooking Expanded / Vanilla Brewing Expanded / mods adding ingredients: You already built cross-mod ingredient matching in Source/Hearth/Kitchen/Ingredients.cs and Common/Defs/Kitchen/Dishes_Mods.xml. Players always ask if a cooking mod works with VCE.
- Combat Extended: Mention that H&H adds no weapons or armor and uses CE-safe damage hooks (Source/Hearth/Underworld/UwDefs.cs, line 88).
- Other Hospitality add-ons (Storefront, Casino, Gastronomy, Spa, Vending Machines): HospitalityFamily.cs detects and silences them automatically—mentioning that H&H cleanly replaces/silences the entire Hospitality addon family will directly answer the #1 migration question.

2. Performance numbers / TPS design:
- RimWorld Workshop readers are notoriously skeptical of large all-in-one mods. You have an explicit performance architecture (rare ticks, cached map registries, circuit-breaker Guard, zero per-cell pathfinding hooks). Adding 2 lines in the FAQ about TPS/performance ("No per-tick pathfinding or hauling patches; guest logic runs on 250-tick intervals with cached venue lookups and an automatic circuit breaker") will reassure heavy modlist players.

3. Load order guidance:
- Add a one-line load order note ("Load after Core, DLCs, and Harmony. If migrating an old Hospitality save, place anywhere—Hearth & Harbor automatically loads before Hospitality.").


(3) THE 5 MOST VALUABLE NEXT FEATURES OR IMPROVEMENTS AFTER RELEASE

1. Complete the wiring of DlcApi, LuckApi, XenotypeHospitality, and DifficultyPanel special switches
Why it is #1:
You have already done 90% of the work—14 traits, 9 Biotech genes, 15 StatDefs, Xenotype taste profiles, LuckApi RTP/streak math, and 14 Difficulty Panel switches all exist with translations and passing unit tests in Source/Hearth/Dlc/DlcTests.cs, but are not connected to CasinoEngine, LotteryGame, GuestEconomy, VenueShopping, DishEffects, and GuestRegistry. Connecting those ~30 call sites turns dead tooltips into deep cross-module emergent gameplay (Impids tipping for spicy food, Highmates boosting room atmosphere, Lucky/Unlucky pawns having real casino streaks, Silver Tongue hosts soothing angry guests) with minimal new code.

2. True per-save module hiding for Architect designators, Research projects, Work types, and Stove bills
Why it is #2:
Modular RimWorld mods live or die by how clean they feel when a player only wants 2 out of 10 modules. Generalizing Underworld's PlaceWorker and ResearchProjectDef.IsHidden patches (Source/Hearth/Underworld/UwDefs.cs, lines 58-83) to every ModuleId—and hiding dish recipes from vanilla stoves until kitchen stations are built or when ShopsFood is off—will make the "Cozy Inn" preset and "Silverline-only" mode feel like purpose-built standalone mods with zero UI clutter.

3. First-class Restaurant & Tavern stock, staffing, and ledger integration
Why it is #3:
Right now Kitchen's Restaurant.cs and Venues' CompVenueSignHH.cs operate as two half-connected systems: Restaurant signs show staffing status and price multipliers that Restaurant.cs doesn't use, food sales don't show up in the Restaurant sign's "Today: X sales" counter, and HH_Venue_Restaurant cannot use the Venue Sign's Restock filter or colonist-forbid toggle because its <sells> is set to None in Common/Defs/Venues/Venues_Types.xml. Unifying Restaurant.cs with VenueApi so players can set a menu/stock filter on the Restaurant sign, forbid colonists from eating guest dishes, require a server on duty (or use the Night Bell), and track daily covers and revenue on the sign will make running a restaurant one of the strongest loops in the mod.

4. Per-shelf sales filters, price multipliers, and multi-item guest shopping baskets in Shops & Food
Why it is #4:
Currently VenueSales.Stock(Venue v) (Source/Hearth/Venues/VenueSales.cs, line 23) puts every item on every shelf or stockpile inside a shop room up for sale at one room-wide price multiplier, and guests buy one item stack per activity run. Adding a lightweight ThingComp or gizmo on shelves inside a venue room ("For sale / Display only / Backstock", plus an optional shelf price markup) and allowing guests with high budgets to fill a small shopping basket across 2-3 shelves before checking out at the counter will prevent guests from buying raw crafting inputs left in a workshop-shop and make retail design much more expressive.

5. Direct Silverline bank card payment in vanilla TradeSession (caravans, visiting traders, and orbital trade ships)
Why it is #5:
Currently BankTrade.cs (Source/Silverline/Bank/BankTrade.cs) can auto-deposit silver earned from selling goods when bank.tradeDeposit is on, and orbital traders accept silver located near a Trade Beacon, but players cannot spend their Silverline checking/savings balance directly inside the vanilla Trade dialog without first withdrawing physical silver at an ATM or waiting 1-4 hours for a drop pod delivery (BankBuildings.cs). Adding a "Pay / Receive via Silverline Account (1% card fee)" toggle or virtual silver line inside Dialog_Trade—settled instantaneously through Payments.ColonyTill and Ledger.Current.Post—would make Silverline indispensable in mid-to-late game colonies where keeping 50,000 physical silver on the floor bloats raid wealth.
