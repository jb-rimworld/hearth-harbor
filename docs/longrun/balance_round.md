# Balance round: guest arrivals and income

Long run (15 days, 3 guest rooms, tavern, restaurant, casino, spa, boutique), same showcase hotel, scheduler fix on/off.
Both runs: threats off, only day 0 forced. "arrived" counts guests (returners included), money in silver.

| | before (unchanged scheduler) | after |
|---|---|---|
| average income per full day | 31 | 131 |
| average income days 1-5 | 57 | 86 |
| days without any arrival (of 15) | 7 | 1 |
| longest run of such days | 5 | 1 |

## What was wrong

After the first fill of the 5-day forecast the plan almost never planned another group: the next candidate time was computed, and when it lay beyond the
forecast window it was thrown away (the next call drew a new gap from that point). A candidate then only landed inside the window if the gap was shorter
than the few hundred ticks by which the window had grown. In a diagnosed run of the unchanged scheduler (with only the diagnostics added) 5 groups were
planned on day 0 and not one more in 15 days; the guests of later days were return visits and the first fill. The fix keeps the drawn candidate until it
falls inside the window, thins candidates by the beds that will be free at their arrival time and adds a drought breaker (see `docs/inn.md`, chapter
"Arrival plan"). Drinks are now booked as the `drink` income category (taverns), the showcase hotel got a boutique (`shop`) and stocked shelves.

## Before (daily)

| day | groups/guests arrived | present (end) | nights | income | casino | food | inn.room | spa |
|---|---|---|---|---|---|---|---|---|
| 0 | 5 | 2 | 2 | 451 | 0 | 344 | 50 | 57 |
| 1 | 2 | 3 | 3 | 97 | 0 | 0 | 75 | 22 |
| 2 | 3 | 3 | 3 | 86 | 0 | 0 | 75 | 11 |
| 3 | 1 | 3 | 3 | 75 | 0 | 0 | 75 | 0 |
| 4 | 0 | 1 | 1 | 25 | 0 | 0 | 25 | 0 |
| 5 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 6 | 2 | 2 | 2 | 0 | -96 | 46 | 50 | 0 |
| 7 | 1 | 1 | 2 | 72 | -8 | 0 | 50 | 30 |
| 8 | 0 | 0 | 0 | 8 | 0 | 0 | 0 | 8 |
| 9 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 10 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 11 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 12 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 13 | 1 | 1 | 1 | 25 | 0 | 0 | 25 | 0 |
| 14 | 1 | 2 | 2 | 61 | -11 | 0 | 50 | 22 |
| 15 | 0 | 0 | 0 | 11 | 0 | 0 | 0 | 11 |

## After (daily)

| day | groups/guests arrived | present (end) | nights | income | casino | drink | food | inn.room | shop | spa |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 5 | 2 | 2 | 577 | 0 | 30 | 319 | 50 | 157 | 22 |
| 1 | 2 | 3 | 3 | 207 | -17 | 97 | 0 | 75 | 41 | 11 |
| 2 | 2 | 0 | 0 | 17 | 0 | 0 | 0 | 0 | 9 | 8 |
| 3 | 2 | 0 | 0 | 58 | 0 | 3 | 41 | 0 | 14 | 0 |
| 4 | 4 | 0 | 0 | 77 | 0 | 3 | 23 | 0 | 51 | 0 |
| 5 | 1 | 1 | 1 | 72 | 0 | 0 | 47 | 25 | 0 | 0 |
| 6 | 5 | 0 | 2 | 258 | 0 | 39 | 72 | 50 | 86 | 11 |
| 7 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 8 | 5 | 3 | 3 | 182 | 0 | 54 | 23 | 65 | 40 | 0 |
| 9 | 2 | 0 | 2 | 108 | 0 | 3 | 0 | 40 | 53 | 12 |
| 10 | 1 | 1 | 1 | 56 | 0 | 3 | 0 | 15 | 27 | 11 |
| 11 | 3 | 0 | 3 | 233 | 0 | 18 | 0 | 65 | 100 | 50 |
| 12 | 7 | 0 | 0 | 177 | 0 | 45 | 0 | 0 | 132 | 0 |
| 13 | 5 | 0 | 0 | 137 | 0 | 36 | 0 | 0 | 90 | 11 |
| 14 | 3 | 0 | 0 | 113 | 0 | 36 | 24 | 0 | 53 | 0 |
| 15 | 5 | 2 | 3 | 268 | 0 | 56 | 0 | 65 | 99 | 48 |
