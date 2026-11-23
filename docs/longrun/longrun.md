# Hearth & Harbor long run

Final report. Showcase hotel (lobby inn with 3 guest rooms, restaurant with kitchen stations, casino, spa, Silverline branch, insurance) built by `ShowcaseBuilder`, then **15 in-game days at maximum speed**, normal guest arrivals, colonists working (hosting, serving, cooking at priority 1). Threats were switched off (threatScale 0). Only on day 0 two guest groups were forced; every later guest came from the scheduler.

Day 0 is the partial build day, days 1..N are full ledger days (60000 ticks). Money in silver.

| day | present | arrived | left | nights | income | till | bank | renown | whispers | satisf. | casino house | cooked | sold | premium | claims | ledger | errors | TPS | pawns |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 2 | 5 | 3 | 2 | 576.8 | 56 | 4786 | 11.8 | 0.0 | 0.74 | -2.0 | 5 | 15 | 0.0 | 0 | 0 | 0 | 899 | 56 |
| 1 | 3 | 2 | 1 | 3 | 206.9 | 0 | 5063 | 12.6 | 0.0 | 0.72 | -6.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 900 | 57 |
| 2 | 0 | 2 | 5 | 0 | 17.0 | 0 | 5096 | 15.7 | 0.0 | 0.60 | 7.4 | 4 | 0 | 0.0 | 0 | 0 | 0 | 900 | 49 |
| 3 | 0 | 2 | 2 | 0 | 58.0 | 0 | 5169 | 16.4 | 0.0 | 0.41 | 0.0 | 6 | 2 | 0.0 | 0 | 0 | 0 | 900 | 49 |
| 4 | 0 | 4 | 4 | 0 | 77.0 | 0 | 5262 | 18.2 | 0.0 | 0.44 | 0.0 | 2 | 1 | 0.0 | 0 | 0 | 0 | 897 | 48 |
| 5 | 1 | 1 | 0 | 1 | 72.0 | 24 | 5280 | 18.2 | 0.0 | 0.45 | 2.2 | 6 | 3 | 17.0 | 0 | 0 | 0 | 895 | 49 |
| 6 | 0 | 5 | 6 | 2 | 258.0 | 0 | 5577 | 21.0 | 0.0 | 0.48 | 6.1 | 1 | 6 | 0.0 | 0 | 0 | 0 | 896 | 47 |
| 7 | 0 | 0 | 0 | 0 | 0.0 | 0 | 5594 | 21.0 | 0.0 | - | -7.0 | 4 | 0 | 0.0 | 0 | 0 | 0 | 896 | 45 |
| 8 | 3 | 5 | 2 | 3 | 182.0 | 0 | 5793 | 21.6 | 0.0 | 0.46 | 0.0 | 1 | 1 | 0.0 | 0 | 0 | 0 | 895 | 46 |
| 9 | 0 | 2 | 5 | 2 | 108.0 | 0 | 5918 | 24.0 | 0.0 | 0.48 | -7.7 | 1 | 0 | 0.0 | 0 | 0 | 0 | 896 | 42 |
| 10 | 1 | 1 | 0 | 1 | 56.0 | 3 | 5943 | 24.0 | 0.0 | 0.69 | 0.0 | 0 | 0 | 17.0 | 0 | 0 | 0 | 895 | 42 |
| 11 | 0 | 3 | 4 | 3 | 233.2 | 0 | 6197 | 25.8 | 0.0 | 0.46 | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 895 | 39 |
| 12 | 0 | 7 | 7 | 0 | 177.0 | 0 | 6392 | 27.7 | 0.0 | 0.33 | -12.9 | 0 | 0 | 0.0 | 0 | 0 | 0 | 896 | 39 |
| 13 | 0 | 5 | 5 | 0 | 137.0 | 0 | 6549 | 29.6 | 0.0 | 0.40 | 0.0 | 0 | 0 | 0.0 | 0 | 0 | 0 | 896 | 38 |
| 14 | 0 | 3 | 3 | 0 | 113.0 | 0 | 6681 | 30.4 | 0.0 | 0.36 | 8.9 | 0 | 2 | 0.0 | 0 | 0 | 0 | 895 | 38 |
| 15 | 2 | 5 | 3 | 3 | 267.8 | 55 | 6867 | 31.3 | 0.0 | 0.43 | 0.0 | 1 | 0 | 17.0 | 0 | 0 | 0 | 895 | 40 |

## Income by category (silver per day)

| day | casino | drink | food | inn.room | shop | spa |
|---|---|---|---|---|---|---|
| 0 | 0 | 30.0 | 318.6 | 50.0 | 156.6 | 21.6 |
| 1 | -17.0 | 97.1 | 0 | 75.0 | 41.0 | 10.8 |
| 2 | 0 | 0 | 0 | 0 | 9.0 | 8.0 |
| 3 | 0 | 3.0 | 41.0 | 0 | 14.0 | 0 |
| 4 | 0 | 3.0 | 23.0 | 0 | 51.0 | 0 |
| 5 | 0 | 0 | 47.0 | 25.0 | 0 | 0 |
| 6 | 0 | 39.0 | 72.0 | 50.0 | 86.0 | 11.0 |
| 7 | 0 | 0 | 0 | 0 | 0 | 0 |
| 8 | 0 | 54.0 | 23.0 | 65.0 | 40.0 | 0 |
| 9 | 0 | 3.0 | 0 | 40.0 | 53.0 | 12.0 |
| 10 | 0 | 3.0 | 0 | 15.0 | 27.0 | 11.0 |
| 11 | 0 | 18.0 | 0 | 65.0 | 100.4 | 49.8 |
| 12 | 0 | 45.0 | 0 | 0 | 132.0 | 0 |
| 13 | 0 | 36.0 | 0 | 0 | 90.0 | 11.0 |
| 14 | 0 | 36.0 | 24.0 | 0 | 53.0 | 0 |
| 15 | 0 | 55.8 | 0 | 65.0 | 99.0 | 48.0 |

## Arrival scheduler (per day)

Groups = planned groups that really came (the `arrived` column above counts guests, returners included). Free beds, rate and forecast are read at the end of the day.

| day | groups arrived | planned | thinned out | postponed | dropped | no faction | drought breaks | free beds | rate/day | forecast |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 1/3 | 1.29 | 8 |
| 1 | 2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/3 | 0.56 | 8 |
| 2 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 3/3 | 1.49 | 10 |
| 3 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 3/3 | 1.49 | 11 |
| 4 | 2 | 0 | 3 | 0 | 0 | 0 | 0 | 3/3 | 1.50 | 11 |
| 5 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 2/3 | 1.50 | 11 |
| 6 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 3/3 | 1.51 | 12 |
| 7 | 0 | 5 | 0 | 0 | 0 | 0 | 1 | 3/3 | 1.51 | 17 |
| 8 | 3 | 1 | 3 | 0 | 0 | 0 | 0 | 0/3 | 0.57 | 16 |
| 9 | 1 | 3 | 1 | 0 | 0 | 0 | 1 | 3/3 | 1.52 | 21 |
| 10 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 2/3 | 1.52 | 20 |
| 11 | 1 | 1 | 1 | 0 | 0 | 0 | 0 | 3/3 | 1.52 | 22 |
| 12 | 3 | 2 | 1 | 0 | 0 | 0 | 0 | 3/3 | 1.53 | 24 |
| 13 | 3 | 1 | 0 | 0 | 0 | 0 | 0 | 3/3 | 1.53 | 25 |
| 14 | 1 | 1 | 1 | 0 | 0 | 0 | 0 | 3/3 | 1.54 | 26 |
| 15 | 2 | 0 | 1 | 0 | 0 | 0 | 0 | 1/3 | 1.35 | 25 |

## Balance

* Average income per full day: **130.9** (first 5 days: 86.2); target KONZEPT D20 early game: 60-150 per day.
* Guests seen: 42, nights sold: 20, plates cooked (lower bound, polled): 31, plates sold: 30.
* Renown 11.8 to 31.3, whispers 0.0; TPS average 896.

## Checks

* ok   all four venues are recognised: The Harbor Light, Hearth Kitchen, The Golden Anchor, Tidewater Spa, Corridor Boutique
* ok   hotel furniture spawned: 58 powered buildings, 0 without power
* ok   colonists on the map: 3
* ok   bank account opened
* ok   at least one insurance policy signed
* ok   entrance door reachable from the plaza ((141, 0, 104), standable=True)
* ok   lobby reachable from the plaza ((141, 0, 110), standable=True)
* ok   restaurant reachable from the plaza ((128, 0, 109), standable=True)
* ok   restaurant stations row reachable from the plaza ((128, 0, 113), standable=True)
* ok   casino reachable from the plaza ((155, 0, 109), standable=True)
* ok   corridor reachable from the plaza ((141, 0, 116), standable=True)
* ok   room 1 reachable from the plaza ((124, 0, 121), standable=True)
* ok   room 2 reachable from the plaza ((130, 0, 121), standable=True)
* ok   room 3 reachable from the plaza ((136, 0, 121), standable=True)
* ok   spa reachable from the plaza ((145, 0, 121), standable=True)
* ok   bank reachable from the plaza ((156, 0, 121), standable=True)
* ok   HH_FrontDesk at (141, 0, 112) rot 0: interaction cell (141, 0, 111) reachable (standable=True, blockers=[])
* ok   HH_Station_PrepCounter at (122, 0, 114) rot 0: interaction cell (122, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_StockPot at (124, 0, 114) rot 0: interaction cell (124, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_Grill at (125, 0, 114) rot 0: interaction cell (125, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_Oven at (127, 0, 114) rot 0: interaction cell (127, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_WokStation at (129, 0, 114) rot 0: interaction cell (129, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_DeepFryer at (130, 0, 114) rot 0: interaction cell (130, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_Smoker at (131, 0, 114) rot 0: interaction cell (131, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_Steamer at (132, 0, 114) rot 0: interaction cell (132, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_ColdStation at (133, 0, 114) rot 0: interaction cell (133, 0, 113) reachable (standable=True, blockers=[])
* ok   HH_Station_DoughBench at (122, 0, 111) rot 3: interaction cell (123, 0, 111) reachable (standable=True, blockers=[DiningChair])
* ok   HH_ShopCounter at (133, 0, 107) rot 3: interaction cell (134, 0, 107) reachable (standable=True, blockers=[])
* ok   at least one dish bill could be set up
* ok   friendly faction for guests: Asdona Moral Compact
* ok   all days played: 15/15
* ok   ledger sums to zero all the time
* ok   no HLog errors during the run: 0
* ok   guests arrived on at least 5 days (shortened runs: a third): 14 of 15
* ok   income > 0: 2539.6 silver over 16 ledger days
* ok   no guest stuck for more than 3 days: ok (42 guests seen)
* ok   no module paused by Guard

## Notes

* research of the mod finished up front (hospitality + banking tabs): 18 projects (the run measures the finished hotel, not the research path)
* building origin (121, 0, 104) on map (250, 1, 250)
* carpets: CarpetRed / CarpetBlue / CarpetWhite
* rooms set to 21 C: 17 (outdoors 14 C)
* lone powered buildings re-wired: 0
* sign The Harbor Light: type HH_Venue_Tavern, room 87 cells 120, module on True
* sign Hearth Kitchen: type HH_Venue_Restaurant, room 86 cells 130, module on True
* sign The Golden Anchor: type HH_Venue_Casino, room 88 cells 130, module on True
* sign Tidewater Spa: type HH_Venue_Spa, room 101 cells 60, module on True
* sign Corridor Boutique: type HH_Venue_Shop, room 92 cells 80, module on True
* policy SL_RoofOverHead: signed
* policy SL_RimHealth: signed
* policy SL_LegalShield: signed
* raids, mad animals and other threat incidents switched off (threatScale 0, 34 incident defs zeroed) so the run measures the hotel, not defence
* cook check Anita: Cooking disabled=False priority=3 skill=4 manhunter/incapable=False
* cook check Erisen: Cooking disabled=False priority=3 skill=0 manhunter/incapable=False
* cook check Handy: Cooking disabled=False priority=3 skill=4 manhunter/incapable=False
* kitchen bills added: 18 (do-until 3 plates, Everyday quality)
* forced guest groups on day 0: 2 (no forced guests after this)
* start tick 12776, first day boundary 60000, running 15 full days, colonists 3, map (250, 1, 250)
* bill diag @15296: HH_Station_PrepCounter 'HH_Cook_FruitBowl' shouldNow=True suspended=False products=0/3 powered= usable=True
*   workgiver HH_Station_PrepCounter for Anita: JOB DoBill
*   workgiver HH_Station_PrepCounter for Erisen: no job (has=False, forbidden=False, reach=True, reserve=True, cookDisabled=False, prio=3, curjob=HH_TendVenue)
*   workgiver HH_Station_PrepCounter for Handy: JOB DoBill
* bill diag @15296: HH_Station_StockPot 'HH_Cook_BerryCompote' shouldNow=True suspended=False products=0/3 powered= usable=True
*   workgiver HH_Station_StockPot for Anita: JOB DoBill
*   workgiver HH_Station_StockPot for Erisen: no job (has=False, forbidden=False, reach=True, reserve=True, cookDisabled=False, prio=3, curjob=HH_TendVenue)
*   workgiver HH_Station_StockPot for Handy: JOB DoBill
* bill diag @15296: HH_Station_Grill 'HH_Cook_RoastCornOnTheCob' shouldNow=True suspended=False products=0/3 powered= usable=True
* colonist Anita @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Erisen @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0] hp=1.00
* colonist Handy @15296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
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
* colonist Handy @83336: spawned=True downed=False mental=False starving=False exhausted=True hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
* colonist Handy @111056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
* colonist Handy @186656: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,HH_WellFed:0.5] hp=1.00
* colonist Erisen @194216: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0,HH_WellFed:0.5] hp=1.00
* colonist Handy @194216: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
* colonist Anita @196736: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Anita @204296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Erisen @204296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0] hp=1.00
* colonist Erisen @232016: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0,HH_WellFed:0.5] hp=1.00
* colonist Anita @239576: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Erisen @239576: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0] hp=1.00
* colonist Anita @247136: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Handy @247136: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,HH_WellFed:0.5] hp=1.00
* colonist Handy @257216: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
* colonist Erisen @262256: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0,HH_WellFed:0.5] hp=1.00
* colonist Erisen @269816: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0] hp=1.00
* colonist Handy @317696: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,HH_WellFed:0.5] hp=1.00
* colonist Handy @327776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
* colonist Erisen @332816: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0,HH_WellFed:0.5] hp=1.00
* colonist Anita @340376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Erisen @342896: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0] hp=1.00
* colonist Anita @350456: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Handy @390776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,HH_WellFed:0.5] hp=1.00
* colonist Handy @400856: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0] hp=1.00
* colonist Erisen @453776: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0,HH_WellFed:0.5] hp=1.00
* colonist Anita @456296: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Handy @461336: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:1.0,HH_WellFed:0.5] hp=1.00
* colonist Anita @463856: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* colonist Erisen @463856: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Stab:3.0] hp=1.00
* colonist Handy @466376: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:0.9,HH_WellFed:0.5] hp=1.00
* colonist Handy @471416: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:0.8] hp=1.00
* colonist Handy @476456: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:0.7] hp=1.00
* colonist Handy @484016: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:0.6] hp=1.00
* colonist Anita @489056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[HH_WellFed:0.5] hp=1.00
* colonist Handy @489056: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:0.5] hp=1.00
* colonist Handy @494096: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[Gunshot:6.0,Scratch:2.0,FoodPoisoning:0.4] hp=1.00
* colonist Anita @499136: spawned=True downed=False mental=False starving=False exhausted=False hediffs=[] hp=1.00
* return visits of known guests: 10; threat pawns removed: 0; heat/cold assists: 19
* colonists alive at the end: 3/3; upkeep: 42 generator refuels, 3 pantry restocks, 30 shelf stacks (tavern drinks, boutique goods), 7 starvation feeds
* tick 15296: pantry had 7.1 nutrition, restocked (meals + raw food)
* tick 58136: Handy was starving, fed once
* tick 93416: Handy was starving, fed once
* tick 302576: pantry had 11.9 nutrition, restocked (meals + raw food)
* tick 539456: Anita was starving, fed once
* tick 544496: Erisen was starving, fed once
* tick 667976: Handy was starving, fed once
* tick 688136: Erisen was starving, fed once
* tick 806576: pantry had 11.1 nutrition, restocked (meals + raw food)
* tick 859496: Handy was starving, fed once
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Roof Over Your Head
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Rim Health Plan
* incident: tick 15296: letter [SL_ClawbackNotice] Welcome to Clawback Mutual: Legal Shield
* incident: tick 27896: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 85856: letter [NeutralEvent] Guests: Vaszacama Union
* incident: tick 85856: letter [NeutralEvent] Guests: Trade Guild
* incident: tick 100976: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 131216: letter [PositiveEvent] Guests left: Vaszacama Union
* incident: tick 131216: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 143816: letter [NeutralEvent] Guests: Asdona Moral Compact
* incident: tick 153896: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 204296: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 262256: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 269816: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 370616: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 385736: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 408416: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 521816: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 531896: letter [Death] Death: Kazuki (stallion)
* incident: tick 552056: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 552056: letter [PositiveEvent] Guests left: Asdona Moral Compact
* incident: tick 587336: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 597416: letter [NegativeEvent] Sad wander: Handy
* incident: tick 612536: letter [NegativeEvent] Insulting spree: Anita
* incident: tick 662936: letter [NegativeEvent] Insulting spree: Erisen
* incident: tick 673016: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 703256: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 748616: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 748616: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 758696: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 804056: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 806576: letter [PositiveEvent] Guests left: Trade Guild
* incident: tick 809096: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 819176: letter [NegativeEvent] Daze: Handy
* incident: tick 869576: letter [PositiveEvent] Guests left: The Shattered Empire
* incident: tick 917456: letter [NeutralEvent] Disturbing vision
* incident: tick 940136: letter [PositiveEvent] Guests left: Vaszacama Union
