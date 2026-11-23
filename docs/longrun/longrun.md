# Hearth & Harbor long run

Final report. Showcase hotel (lobby inn with 3 guest rooms, restaurant with kitchen stations, casino, spa, Silverline branch, insurance) built by `ShowcaseBuilder`, then **15 in-game days at maximum speed**, normal guest arrivals, colonists working (hosting, serving, cooking at priority 1). Threats were switched off (threatScale 0). Only on day 0 two guest groups were forced; every later guest came from the scheduler.

Day 0 is the partial build day, days 1..N are full ledger days (60000 ticks). Money in silver.

| day | present | arrived | left | nights | income | till | bank | renown | whispers | satisf. | casino house | cooked | sold | premium | claims | ledger | errors | TPS | pawns |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 2 | 5 | 3 | 2 | 446.4 | 0 | 4711 | 12.1 | 0.0 | 0.75 | 0.0 | 5 | 13 | 0.0 | 0 | 0 | 0 | 883 | 21 |
| 1 | 3 | 1 | 0 | 3 | 89.4 | 14 | 4801 | 12.2 | 0.0 | 0.79 | 0.0 | 5 | 1 | 0.0 | 0 | 0 | 0 | 900 | 22 |
| 2 | 2 | 2 | 3 | 2 | 200.2 | 0 | 5029 | 14.8 | 0.0 | 0.61 | 8.3 | 14 | 11 | 0.0 | 0 | 0 | 0 | 900 | 21 |
| 3 | 3 | 1 | 0 | 3 | 94.1 | 0 | 5139 | 14.8 | 0.0 | 0.50 | -1.6 | 0 | 0 | 0.0 | 0 | 0 | 0 | 900 | 21 |
| 4 | 0 | 0 | 3 | 1 | 33.4 | 0 | 5187 | 16.5 | 0.0 | 0.50 | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 897 | 18 |
| 5 | 0 | 6 | 6 | 0 | 0.0 | 0 | 5156 | 18.7 | 0.0 | 0.46 | -0.1 | 13 | 0 | 17.8 | 0 | 0 | 0 | 848 | 18 |
| 6 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5172 | 18.7 | 0.0 | - | -3.0 | 7 | 0 | 0.0 | 0 | 0 | 0 | 895 | 17 |
| 7 | 2 | 2 | 0 | 2 | 103.0 | 0 | 5291 | 18.7 | 0.0 | 0.58 | 0.0 | 4 | 4 | 0.0 | 0 | 0 | 0 | 896 | 19 |
| 8 | 0 | 0 | 2 | 1 | 167.0 | 0 | 5473 | 20.0 | 0.0 | 0.69 | 0.0 | 7 | 7 | 0.0 | 0 | 0 | 0 | 896 | 17 |
| 9 | 1 | 1 | 0 | 1 | 25.0 | 0 | 5515 | 20.0 | 0.0 | 0.55 | 1.1 | 0 | 0 | 0.0 | 0 | 0 | 0 | 892 | 18 |
| 10 | 0 | 0 | 1 | 0 | 0.0 | 0 | 5485 | 20.9 | 0.0 | 0.71 | 4.2 | 1 | 0 | 17.8 | 0 | 0 | 0 | 895 | 17 |
| 11 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5501 | 20.9 | 0.0 | - | 0.0 | 19 | 0 | 0.0 | 0 | 0 | 0 | 895 | 17 |
| 12 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5518 | 20.9 | 0.0 | - | 6.5 | 5 | 0 | 0.0 | 0 | 0 | 0 | 896 | 36 |
| 13 | 2 | 2 | 0 | 2 | 0.0 | 0 | 5535 | 20.9 | 0.0 | 0.43 | 8.6 | 3 | 6 | 0.0 | 1 | 0 | 0 | 848 | 20 |
| 14 | 2 | 6 | 6 | 2 | 50.0 | 0 | 5602 | 22.9 | 0.0 | 0.45 | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 896 | 19 |
| 15 | 1 | 1 | 2 | 3 | 75.0 | 0 | 5648 | 23.7 | 0.0 | 0.41 | 0.0 | 0 | 0 | 16.9 | 0 | 0 | 0 | 896 | 18 |

## Income by category (silver per day)

| day | casino | food | inn.room | shop | spa |
|---|---|---|---|---|---|
| 0 | 0 | 328.8 | 50.0 | 0 | 67.6 |
| 1 | 0 | 14.4 | 75.0 | 0 | 0 |
| 2 | 0 | 147.2 | 50.0 | 3.0 | 0 |
| 3 | 0 | 0 | 75.0 | 19.1 | 0 |
| 4 | 0 | 0 | 25.0 | 8.4 | 0 |
| 5 | 0 | 0 | 0 | 0 | 0 |
| 6 | 0 | 0 | 0 | 0 | 0 |
| 7 | 0 | 53.0 | 50.0 | 0 | 0 |
| 8 | 0 | 142.0 | 25.0 | 0 | 0 |
| 9 | 0 | 0 | 25.0 | 0 | 0 |
| 10 | 0 | 0 | 0 | 0 | 0 |
| 11 | 0 | 0 | 0 | 0 | 0 |
| 12 | 0 | 0 | 0 | 0 | 0 |
| 13 | -171.4 | 94.5 | 50.0 | 26.9 | 0 |
| 14 | 0 | 0 | 50.0 | 0 | 0 |
| 15 | 0 | 0 | 75.0 | 0 | 0 |

## Balance

* Average income per full day: **55.8** (first 5 days: 83.4); target KONZEPT D20 early game: 60-150 per day.
* Guests seen: 12, nights sold: 22, plates cooked (lower bound, polled): 83, plates sold: 42.
* Renown 12.1 to 23.7, whispers 0.0; TPS average 890.

## Checks

* ok   all four venues are recognised: The Harbor Light, Hearth Kitchen, The Golden Anchor, Tidewater Spa
* ok   hotel furniture spawned: 58 powered buildings, 0 without power
* ok   colonists on the map: 3
* ok   bank account opened
* ok   at least one insurance policy signed
* ok   entrance door reachable from the plaza ((130, 0, 103), standable=True)
* ok   lobby reachable from the plaza ((130, 0, 109), standable=True)
* ok   restaurant reachable from the plaza ((117, 0, 108), standable=True)
* ok   restaurant stations row reachable from the plaza ((117, 0, 112), standable=True)
* ok   casino reachable from the plaza ((144, 0, 108), standable=True)
* ok   corridor reachable from the plaza ((130, 0, 115), standable=True)
* ok   room 1 reachable from the plaza ((113, 0, 120), standable=True)
* ok   room 2 reachable from the plaza ((119, 0, 120), standable=True)
* ok   room 3 reachable from the plaza ((125, 0, 120), standable=True)
* ok   spa reachable from the plaza ((134, 0, 120), standable=True)
* ok   bank reachable from the plaza ((145, 0, 120), standable=True)
* ok   HH_FrontDesk at (130, 0, 111) rot 0: interaction cell (130, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_PrepCounter at (111, 0, 113) rot 0: interaction cell (111, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_StockPot at (113, 0, 113) rot 0: interaction cell (113, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_Grill at (114, 0, 113) rot 0: interaction cell (114, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_Oven at (116, 0, 113) rot 0: interaction cell (116, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_WokStation at (118, 0, 113) rot 0: interaction cell (118, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_DeepFryer at (119, 0, 113) rot 0: interaction cell (119, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_Smoker at (120, 0, 113) rot 0: interaction cell (120, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_Steamer at (121, 0, 113) rot 0: interaction cell (121, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_ColdStation at (122, 0, 113) rot 0: interaction cell (122, 0, 112) reachable (standable=True, blockers=[])
* ok   HH_Station_DoughBench at (111, 0, 110) rot 3: interaction cell (112, 0, 110) reachable (standable=True, blockers=[DiningChair])
* ok   HH_ShopCounter at (122, 0, 106) rot 3: interaction cell (123, 0, 106) reachable (standable=True, blockers=[])
* ok   at least one dish bill could be set up
* ok   friendly faction for guests: Hefinistan Pact
* ok   all days played: 15/15
* ok   ledger sums to zero all the time
* ok   no HLog errors during the run: 0
* ok   guests arrived on at least 5 days (shortened runs: a third): 9 of 15
* ok   income > 0: 1283.5 silver over 16 ledger days
* ok   no guest stuck for more than 3 days: ok (12 guests seen)
* ok   no module paused by Guard

## Notes

* research of the mod finished up front (hospitality + banking tabs): 18 projects (the run measures the finished hotel, not the research path)
* building origin (110, 0, 103) on map (250, 1, 250)
* carpets: CarpetRed / CarpetBlue / CarpetWhite
* rooms set to 21 C: 17 (outdoors 4 C)
* lone powered buildings re-wired: 0
* sign The Harbor Light: type HH_Venue_Tavern, room 138 cells 120, module on True
* sign Hearth Kitchen: type HH_Venue_Restaurant, room 137 cells 130, module on True
* sign The Golden Anchor: type HH_Venue_Casino, room 139 cells 130, module on True
* sign Tidewater Spa: type HH_Venue_Spa, room 153 cells 60, module on True
* policy SL_RoofOverHead: signed
* policy SL_RimHealth: signed
* policy SL_LegalShield: signed
* raids, mad animals and other threat incidents switched off (threatScale 0, 34 incident defs zeroed) so the run measures the hotel, not defence
* cook check Espinoza: Cooking disabled=False priority=3 skill=3 manhunter/incapable=False
* cook check Andy: Cooking disabled=False priority=3 skill=2 manhunter/incapable=False
* cook check Tiny: Cooking disabled=False priority=3 skill=1 manhunter/incapable=False
* kitchen bills added: 18 (do-until 3 plates, Everyday quality)
* forced guest groups on day 0: 2 (no forced guests after this)
* start tick 12776, first day boundary 60000, running 15 full days, colonists 3, map (250, 1, 250)
* bill diag @15296: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
*   workgiver HH_Station_PrepCounter for Espinoza: JOB DoBill
*   workgiver HH_Station_PrepCounter for Andy: JOB DoBill
*   workgiver HH_Station_PrepCounter for Tiny: JOB DoBill
* bill diag @15296: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
*   workgiver HH_Station_StockPot for Espinoza: JOB DoBill
*   workgiver HH_Station_StockPot for Andy: JOB DoBill
*   workgiver HH_Station_StockPot for Tiny: JOB DoBill
* bill diag @15296: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* colonist Espinoza @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Andy @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Tiny @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:5.0] hp=1.00
* bill diag @30416: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @30416: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @30416: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @45536: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @45536: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @45536: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @60656: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @60656: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @60656: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @75776: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @75776: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
* bill diag @75776: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* colonist Espinoza @83336: spawned=True downed=False mental=False starving=False exhausted=True hediffs=[] hp=1.00
* colonist Andy @83336: spawned=True downed=False mental=False starving=False exhausted=True hediffs=[] hp=1.00
* colonist Espinoza @98456: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Andy @111056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Espinoza @118616: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Andy @118616: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Andy @123656: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Espinoza @126176: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Tiny @138776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:5.0,HH_WellFed:0.5] hp=1.00
* colonist Tiny @146336: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:5.0] hp=1.00
* colonist Espinoza @148856: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Andy @151376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Espinoza @156416: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Andy @158936: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Espinoza @259736: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:1.0] hp=1.00
* colonist Espinoza @264776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.9] hp=1.00
* colonist Espinoza @269816: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.8] hp=1.00
* colonist Espinoza @277376: spawned=True downed=False mental=True starving=False exhausted=False hediffs=[FoodPoisoning:0.7] hp=1.00
* colonist Espinoza @282416: spawned=True downed=False mental=True starving=False exhausted=False hediffs=[FoodPoisoning:0.6] hp=1.00
* colonist Espinoza @287456: spawned=True downed=False mental=True starving=False exhausted=False hediffs=[FoodPoisoning:0.5] hp=1.00
* colonist Espinoza @295016: spawned=True downed=False mental=True starving=False exhausted=False hediffs=[FoodPoisoning:0.4] hp=1.00
* colonist Espinoza @300056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.3] hp=1.00
* colonist Espinoza @305096: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.2] hp=1.00
* colonist Espinoza @312656: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.1] hp=1.00
* colonist Espinoza @317696: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.8] hp=1.00
* colonist Espinoza @322736: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.7] hp=1.00
* colonist Espinoza @327776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.6] hp=1.00
* colonist Espinoza @332816: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.5] hp=1.00
* colonist Espinoza @340376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.4] hp=1.00
* colonist Tiny @340376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:5.0,HH_WellFed:0.5] hp=1.00
* colonist Espinoza @345416: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.3] hp=1.00
* colonist Tiny @350456: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:5.0] hp=1.00
* colonist Espinoza @352976: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.2] hp=1.00
* colonist Espinoza @358016: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.1] hp=1.00
* colonist Espinoza @363056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.0] hp=1.00
* colonist Espinoza @365576: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Andy @390776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* return visits of known guests: 15; threat pawns removed: 6; heat/cold assists: 3
* colonists alive at the end: 3/3; upkeep: 44 generator refuels, 3 pantry restocks, 6 starvation feeds
* tick 15296: pantry had 6.3 nutrition, restocked (meals + raw food)
* tick 58136: Espinoza was starving, fed once
* tick 58136: Andy was starving, fed once
* tick 58136: Tiny was starving, fed once
* tick 93416: Espinoza was starving, fed once
* tick 93416: Andy was starving, fed once
* tick 297536: Espinoza was starving, fed once
* tick 317696: pantry had 11.1 nutrition, restocked (meals + raw food)
* tick 652856: pantry had 11.7 nutrition, restocked (meals + raw food)
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Roof Over Your Head
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Rim Health Plan
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Legal Shield
* incident: tick 25376: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 85856: letter [NeutralEvent] Guests: Hefinistan Pact
* incident: tick 131216: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 131216: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 146336: letter [NeutralEvent] Guests: Hefinistan Pact
* incident: tick 151376: letter [NeutralEvent] Visitor
* incident: tick 204296: letter [NeutralEvent] Guests: The Gubasea League
* incident: tick 252176: letter [PositiveEvent] Guests left: The Gubasea League
* incident: tick 264776: letter [PositiveEvent] Cargo pods
* incident: tick 272336: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 277376: letter [NegativeEvent] Insulting spree: Espinoza
* incident: tick 315176: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 320216: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 325256: letter [ThreatBig] Raid: Psyck Pillers
* incident: tick 325256: hostile on the map (removed): Psyck Pillers/Drifter (e.g. Veronica)
* incident: tick 363056: letter [NewQuest] Quest available: Mechanoid Signal
* incident: tick 481496: letter [NewQuest] Quest available: The Ambushed Praetor
* incident: tick 511736: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 557096: letter [NewQuest] Quest available: Jed's Sparrows
* incident: tick 612536: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 776336: letter [NeutralEvent] Cult escapee
* incident: tick 778856: letter [PositiveEvent] War merchant from The Gubasea League
* incident: tick 793976: letter [NeutralEvent] Transport pod crash
* incident: tick 793976: hostile on the map (removed): Psyck Pillers/SpaceRefugee (e.g. Fizzash)
* incident: tick 801536: letter [NeutralEvent] Disturbing vision
* incident: tick 826736: letter [ThreatBig] Raid: Psyck Pillers
* incident: tick 829256: letter [PositiveEvent] Gift from The Gubasea League
* incident: tick 836816: letter [ThreatBig] (*Name)Spine(/Name)'s howl
* incident: tick 836816: letter [ThreatBig] (*Name)Spine(/Name) hostile
* incident: tick 836816: hostile on the map (removed): Dark entities/CultEscapee (e.g. Spine)
* incident: tick 839336: letter [AcceptJoiner] Transport pod crash: (*Name)Diana(/Name)
* incident: tick 839336: letter [ThreatBig] Sightstealer revealed
* incident: tick 839336: letter [SL_ClawbackNotice] Claim #1 received
* incident: tick 839336: hostile on the map (removed): Dark entities/Sightstealer (e.g. sightstealer)
* incident: tick 841856: hostile on the map (removed): Pact of Flumouk/Refugee (e.g. Diana)
* incident: tick 864536: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 867056: letter [PositiveEvent] Guests left: Hefinistan Pact
* incident: tick 902336: letter [NegativeEvent] Faction base proximity
* incident: tick 952736: letter [PositiveEvent] Guests left: Hefinistan Pact
