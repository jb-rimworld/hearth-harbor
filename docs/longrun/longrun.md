# Hearth & Harbor long run

Final report. Showcase hotel (lobby inn with 3 guest rooms, restaurant with kitchen stations, casino, spa, Silverline branch, insurance) built by `ShowcaseBuilder`, then **15 in-game days at maximum speed**, normal guest arrivals, colonists working (hosting, serving, cooking at priority 1). Threats were switched off (threatScale 0). Only on day 0 two guest groups were forced; every later guest came from the scheduler.

Day 0 is the partial build day, days 1..N are full ledger days (60000 ticks). Money in silver.

| day | present | arrived | left | nights | income | till | bank | renown | whispers | satisf. | casino house | cooked | sold | premium | claims | ledger | errors | TPS | pawns |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 2 | 5 | 3 | 2 | 505.8 | 0 | 4773 | 12.3 | 0.0 | 0.79 | 14.1 | 5 | 5 | 0.0 | 0 | 0 | 0 | 900 | 89 |
| 1 | 2 | 0 | 0 | 2 | 210.0 | 0 | 4997 | 12.3 | 0.0 | 0.91 | -9.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 900 | 84 |
| 2 | 0 | 3 | 5 | 0 | 0.0 | 0 | 5012 | 15.5 | 0.0 | 0.67 | 0.8 | 0 | 0 | 0.0 | 0 | 0 | 0 | 900 | 80 |
| 3 | 0 | 0 | 0 | 0 | 60.0 | 60 | 5028 | 15.5 | 0.0 | - | 0.0 | 9 | 4 | 0.0 | 0 | 0 | 0 | 900 | 76 |
| 4 | 0 | 1 | 1 | 0 | 15.0 | 0 | 5043 | 16.2 | 0.0 | 0.44 | -17.4 | 2 | 1 | 0.0 | 0 | 0 | 0 | 896 | 73 |
| 5 | 2 | 5 | 3 | 2 | 132.4 | 7 | 5014 | 17.6 | 0.0 | 0.57 | 5.9 | 0 | 0 | 15.6 | 0 | 0 | 0 | 848 | 77 |
| 6 | 2 | 2 | 2 | 2 | 155.0 | 0 | 5191 | 18.9 | 0.0 | 0.59 | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 895 | 75 |
| 7 | 2 | 0 | 0 | 2 | 155.0 | 0 | 5361 | 18.9 | 0.0 | 0.37 | -3.2 | 0 | 0 | 0.0 | 0 | 0 | 0 | 896 | 75 |
| 8 | 0 | 0 | 2 | 0 | 0.0 | 0 | 5377 | 19.4 | 0.0 | 0.26 | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 896 | 85 |
| 9 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5394 | 19.4 | 0.0 | - | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 895 | 75 |
| 10 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5367 | 19.4 | 0.0 | - | 0.0 | 0 | 0 | 14.4 | 0 | 0 | 0 | 895 | 73 |
| 11 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5383 | 19.4 | 0.0 | - | 12.7 | 0 | 0 | 0.0 | 0 | 0 | 0 | 895 | 73 |
| 12 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5400 | 19.4 | 0.0 | - | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 894 | 72 |
| 13 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5416 | 19.4 | 0.0 | - | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 843 | 71 |
| 14 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5432 | 19.4 | 0.0 | - | 0.0 | 0 | 0 | 0.0 | 1 | 0 | 0 | 894 | 68 |
| 15 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5406 | 19.4 | 0.0 | - | 28.5 | 1 | 0 | 14.4 | 0 | 0 | 0 | 895 | 67 |

## Income by category (silver per day)

| day | food | inn.room | spa |
|---|---|---|---|
| 0 | 241.8 | 210.0 | 54.0 |
| 1 | 0 | 210.0 | 0 |
| 2 | 0 | 0 | 0 |
| 3 | 60.0 | 0 | 0 |
| 4 | 15.0 | 0 | 0 |
| 5 | 0 | 100.0 | 32.4 |
| 6 | 0 | 155.0 | 0 |
| 7 | 0 | 155.0 | 0 |
| 8 | 0 | 0 | 0 |
| 9 | 0 | 0 | 0 |
| 10 | 0 | 0 | 0 |
| 11 | 0 | 0 | 0 |
| 12 | 0 | 0 | 0 |
| 13 | 0 | 0 | 0 |
| 14 | 0 | 0 | 0 |
| 15 | 0 | 0 | 0 |

## Balance

* Average income per full day: **48.5** (first 5 days: 83.5); target KONZEPT D20 early game: 60-150 per day.
* Guests seen: 11, nights sold: 10, plates cooked (lower bound, polled): 17, plates sold: 10.
* Renown 12.3 to 19.4, whispers 0.0; TPS average 890.

## Checks

* ok   hotel furniture spawned: 44 powered buildings, 0 without power
* ok   colonists on the map: 3
* ok   bank account opened
* ok   at least one insurance policy signed
* ok   entrance door reachable from the plaza ((121, 0, 101), standable=True)
* ok   lobby reachable from the plaza ((121, 0, 107), standable=True)
* ok   restaurant reachable from the plaza ((108, 0, 106), standable=True)
* ok   restaurant stations row reachable from the plaza ((108, 0, 110), standable=True)
* ok   casino reachable from the plaza ((135, 0, 106), standable=True)
* ok   corridor reachable from the plaza ((121, 0, 113), standable=True)
* ok   room 1 reachable from the plaza ((104, 0, 118), standable=True)
* ok   room 2 reachable from the plaza ((110, 0, 118), standable=True)
* ok   room 3 reachable from the plaza ((116, 0, 118), standable=True)
* ok   spa reachable from the plaza ((125, 0, 118), standable=True)
* ok   bank reachable from the plaza ((136, 0, 118), standable=True)
* ok   HH_FrontDesk at (121, 0, 109) rot 0: interaction cell (121, 0, 108) reachable (standable=True, blockers=[])
* ok   HH_Station_PrepCounter at (102, 0, 111) rot 0: interaction cell (102, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_StockPot at (104, 0, 111) rot 0: interaction cell (104, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_Grill at (105, 0, 111) rot 0: interaction cell (105, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_Oven at (107, 0, 111) rot 0: interaction cell (107, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_WokStation at (109, 0, 111) rot 0: interaction cell (109, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_DeepFryer at (110, 0, 111) rot 0: interaction cell (110, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_Smoker at (111, 0, 111) rot 0: interaction cell (111, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_Steamer at (112, 0, 111) rot 0: interaction cell (112, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_ColdStation at (113, 0, 111) rot 0: interaction cell (113, 0, 110) reachable (standable=True, blockers=[])
* ok   HH_Station_DoughBench at (102, 0, 108) rot 3: interaction cell (103, 0, 108) reachable (standable=True, blockers=[DiningChair])
* ok   HH_ShopCounter at (113, 0, 104) rot 3: interaction cell (114, 0, 104) reachable (standable=True, blockers=[])
* ok   at least one dish bill could be set up
* ok   friendly faction for guests: Terbinunt
* ok   all days played: 15/15
* ok   ledger sums to zero all the time
* ok   no HLog errors during the run: 0
* FAIL guests arrived on at least 5 days (shortened runs: a third): 4 of 15
* ok   income > 0: 1233.2 silver over 16 ledger days
* ok   no guest stuck for more than 3 days: ok (11 guests seen)
* ok   no module paused by Guard

## Notes

* research of the mod finished up front (hospitality + banking tabs): 18 projects (the run measures the finished hotel, not the research path)
* building origin (101, 0, 101) on map (250, 1, 250)
* carpets: CarpetRed / CarpetBlue / CarpetWhite
* lone powered buildings re-wired: 0
* policy SL_RoofOverHead: signed
* policy SL_RimHealth: signed
* policy SL_LegalShield: signed
* raids, mad animals and other threat incidents switched off (threatScale 0, 34 incident defs zeroed) so the run measures the hotel, not defence
* cook check Pepper: Cooking disabled=False priority=3 skill=1 manhunter/incapable=False
* cook check Missy: Cooking disabled=False priority=3 skill=0 manhunter/incapable=False
* cook check Mariya: Cooking disabled=False priority=3 skill=1 manhunter/incapable=False
* kitchen bills added: 18 (do-until 3 plates, Everyday quality)
* forced guest groups on day 0: 2 (no forced guests after this)
* start tick 12776, first day boundary 60000, running 15 full days, colonists 3, map (250, 1, 250)
* bill diag @15296: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
*   workgiver HH_Station_PrepCounter for Pepper: JOB DoBill
*   workgiver HH_Station_PrepCounter for Missy: no job (has=False, forbidden=False, reach=True, reserve=True, cookDisabled=False, prio=3, curjob=GotoWander)
*   workgiver HH_Station_PrepCounter for Mariya: JOB DoBill
* bill diag @15296: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
*   workgiver HH_Station_StockPot for Pepper: JOB DoBill
*   workgiver HH_Station_StockPot for Missy: no job (has=False, forbidden=False, reach=True, reserve=True, cookDisabled=False, prio=3, curjob=GotoWander)
*   workgiver HH_Station_StockPot for Mariya: JOB DoBill
* bill diag @15296: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* colonist Pepper @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:2.0] hp=1.00
* colonist Missy @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0] hp=1.00
* colonist Mariya @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
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
* colonist Missy @83336: spawned=True downed=False mental=False starving=False exhausted=True hediffs=[Bite:4.0] hp=1.00
* colonist Missy @118616: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0] hp=1.00
* colonist Pepper @204296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:2.0,HH_WellFed:0.5] hp=1.00
* colonist Pepper @214376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:2.0] hp=1.00
* colonist Mariya @214376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Missy @219416: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,HH_WellFed:0.5] hp=1.00
* colonist Mariya @221936: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Missy @224456: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0] hp=1.00
* colonist Pepper @269816: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:2.0,HH_WellFed:0.5] hp=1.00
* colonist Missy @274856: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.0] hp=1.00
* colonist Pepper @277376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:2.0] hp=1.00
* colonist Missy @279896: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.1] hp=1.00
* colonist Missy @289976: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.2] hp=1.00
* colonist Missy @297536: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.3] hp=1.00
* colonist Missy @307616: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.4] hp=1.00
* colonist Missy @317696: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.5] hp=1.00
* colonist Missy @325256: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.6] hp=1.00
* colonist Missy @335336: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.7] hp=1.00
* colonist Missy @342896: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.8] hp=1.00
* colonist Missy @352976: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.9] hp=1.00
* colonist Mariya @352976: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:1.0] hp=1.00
* colonist Mariya @355496: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.9] hp=1.00
* colonist Missy @358016: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:0.9] hp=1.00
* colonist Missy @360536: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[Bite:4.0,Plague:1.0] hp=1.00
* colonist Mariya @363056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.8] hp=1.00
* colonist Missy @365576: DEAD: Bite 4.00, Plague 1.00
* colonist Mariya @365576: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.8] hp=1.00
* colonist Mariya @368096: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.7] hp=1.00
* colonist Mariya @373136: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.6] hp=1.00
* colonist Mariya @380696: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.5] hp=1.00
* colonist Mariya @385736: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.4] hp=1.00
* colonist Mariya @393296: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.3] hp=1.00
* colonist Mariya @398336: spawned=True downed=True mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.2] hp=1.00
* colonist Mariya @400856: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.2] hp=1.00
* colonist Mariya @403376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.1] hp=1.00
* colonist Mariya @410936: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[FoodPoisoning:0.0] hp=1.00
* colonist Mariya @413456: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
*   damage to Pepper: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Mariya: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Pepper: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Mariya: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Pepper: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Mariya: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Pepper: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Mariya: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Pepper: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Mariya: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Pepper: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
*   damage to Mariya: <color=#D09B61FF>Mariya</color>, using her left fist tentatively, hit <color=#D09B61FF>Pepper</color> in the right arm. | <color=#D09B61FF>Pepper</color> attempted to hit <color=#D09B61FF>Mariya</color>, but hit only air. | <color=#D09B61FF>Mariya</color> smashed <color=#D09B61FF>Pepper</color> in the right leg.
* return visits of known guests: 5; threat pawns removed: 3; heat/cold assists: 0
* colonists alive at the end: 2/3; upkeep: 28 generator refuels, 2 pantry restocks, 6 starvation feeds
* tick 15296: pantry had 6.3 nutrition, restocked (meals + raw food)
* tick 58136: Missy was starving, fed once
* tick 93416: Missy was starving, fed once
* tick 171536: Mariya was starving, fed once
* tick 320216: Pepper was starving, fed once
* tick 347936: Missy was starving, fed once
* tick 385736: Mariya was starving, fed once
* tick 491576: pantry had 11.1 nutrition, restocked (meals + raw food)
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Roof Over Your Head
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Rim Health Plan
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Legal Shield
* incident: tick 27896: letter [PositiveEvent] Guests left: Terbinunt
* incident: tick 111056: letter [PositiveEvent] Inspired creativity: Mariya
* incident: tick 131216: letter [PositiveEvent] Guests left: Terbinunt
* incident: tick 146336: letter [NeutralEvent] Guests: Treaty of Choque
* incident: tick 151376: letter [NeutralEvent] Visitor
* incident: tick 161456: letter [PositiveEvent] Guests left: Treaty of Choque
* incident: tick 174056: letter [PositiveEvent] Gift from Treaty of Choque
* incident: tick 226976: letter [NeutralEvent] Visitor
* incident: tick 264776: letter [NeutralEvent] Guests: Terbinunt
* incident: tick 274856: letter [NegativeEvent] Disease: Plague
* incident: tick 282416: letter [PositiveEvent] Guests left: Terbinunt
* incident: tick 307616: letter [PositiveEvent] Exotic goods trader from Terbinunt
* incident: tick 312656: letter [NeutralEvent] Guests: Terbinunt
* incident: tick 325256: letter [PositiveEvent] Guests left: Terbinunt
* incident: tick 325256: letter [ThreatBig] Raid: Trash Lineup
* incident: tick 325256: hostile on the map (removed): Trash Lineup/Drifter (e.g. Lance)
* incident: tick 363056: letter [NewQuest] Quest available: Mechanoid Signal
* incident: tick 365576: letter [ThreatSmall] Berserk: Lolly
* incident: tick 365576: letter [NeutralEvent] Requiem of Femininity opportunity for Missy
* incident: tick 365576: letter [Death] Death: Missy
* incident: tick 365576: hostile on the map (removed): New Arrivals/Megavole/Berserk (e.g. Lolly)
* incident: tick 370616: letter [PositiveEvent] Guests left: Terbinunt
* incident: tick 491576: letter [PositiveEvent] Guests left: Terbinunt
* incident: tick 526856: letter [NeutralEvent] Visitors
* incident: tick 587336: letter [NeutralEvent] Summer
* incident: tick 728456: letter [NeutralEvent] Wild woman wanders in
* incident: tick 811616: letter [ThreatBig] Raid: Trash Lineup
* incident: tick 826736: letter [NewQuest] Quest available: Pugobb's Refugees
* incident: tick 834296: letter [NeutralEvent] Disturbing vision
* incident: tick 882176: letter [NegativeEvent] Zzztt...
* incident: tick 884696: letter [SL_ClawbackNotice] Claim #1 received
* incident: tick 917456: letter [NeutralEvent] Blind healer
* incident: tick 927536: letter [NegativeEvent] Insulting spree: Mariya
