# City Life (ModuleId.CityLife)

Cinema, beauty salon, gallery, park bench, taxi stand and personal security. KONZEPT 6.9, 5.19 (N43), 7, N47.
Code: `Source/Hearth/City/`, defs: `Common/Defs/City/`, texts: `Common/Languages/English/Keyed/HH_City.xml`, tests: `Source/Hearth/City/CityTests.cs` (suites `city.*`).

## What a player sees
| Thing | Needs | Colonists | Guests |
|---|---|---|---|
| **Projector screen** (`HH_ProjectorScreen`, 3x1, powered) | `HH_CityLife_1`, chairs/couches in front | watch alone (joy giver `HH_WatchScreeningJoy`) or hold a **movie night** (gathering `HH_Screening`: button on the screen, plus a dice roll every evening at 18:00) | `city.cinema`: ticket 12, one film per 4 h; a screening starts when the first viewer sits down and runs `city.cinemaShowTicks` |
| **Salon chair** (`HH_SalonChair`, a vanilla styling station subclass) | `HH_CityLife_1`; venue sign `HH_Venue_Salon` for guests | free new look (Ideology: vanilla style dialog; otherwise a random haircut option) | `city.salon`: sits down, a colonist with **HH_Serving** walks up (work giver `HH_SalonStyleWork`), is charged when the work starts: cut 25, +10 colour (Biotech/Ideology), +20 tattoo (Ideology), "Fresh look" +4 |
| **Gallery** (room with sculptures + sign `HH_Venue_Gallery`) | `HH_CityLife_1`, at least `city.galleryMinArt` (4) artworks | - | `city.gallery`: entry 10, walks past up to 5 pieces; satisfaction from the vanilla **Beauty** stat of the pieces |
| **Park bench** (`HH_ParkBench`) | nothing (free) | joy giver (open air only) | `city.park`: free, small satisfaction, more with a nice view (vanilla beauty around the bench) |
| **Taxi stand** (`HH_TaxiStand`, open sky) | `HH_CityLife_2` | - | departing guests hail a cab (fare 15, chance rises with wealth); arriving guests are set down at the stand (landing fee 6); the cab is a pod-like skyfaller (`HH_TaxiLeaving` / `HH_TaxiIncoming`) |
| **Personal security** (service, no building) | `HH_CityLife_2`, guests with wealth >= 60, an armed colonist with **HH_Hosting** | work giver `HH_BodyguardWork`: follow the guest | hire chance 0.3 (+0.4 at wealth 80+), fee = 15 % of the day budget per day, billed hourly (min 2/h); guest hurt while protected: fees refunded, satisfaction -0.3 |

Research mode "off" (`city.researchMode`, also a shared `core.researchMode`/`core.researchOff` if another module adds it) finishes `HH_CityLife_1` and `_2` quietly.
Nothing happens on its own until something is built (N08): activities need a screen/chair/gallery/bench, taxis a stand, bodyguards a venue sign or stand on the map.

## Guest pricing (KONZEPT 6.1)
`CityPricing`: tolerance `T(W) = 0.85 + 1.65 (W/100)^1.3` times the difficulty setting `city.priceTolerance` (tier anchors 3.0 / 1.3 / 1.15 / 1.0 / 0.9 / 0.8 / 0.65),
buy chance `sigma((T q (1+eps) - m)/0.12)`, price thought after `rho = m / (T q)` (Bargain +3, Good value +1, Pricey -2, Ripped off -5; 1 point = 0.02 satisfaction).

## Money and saving
All charges go through `GuestApi.ChargeGuest` (colony till). Takings per service are kept by `CityGameComponent` (also last visit per guest and service, bodyguard contracts).
The screen's film state and the taxi stand's counters are saved in their comps. Removing the module leaves ordinary buildings behind.

## Contracts used
`GuestApi` (IsGuest/Info/Budget/ChargeGuest/AddSatisfaction), `GuestActivities.Register`, `Venues.All/At`, `CompVenueSign`, `VenueTypeDef`, `HearthTabs`, `Payments.ColonyTill`, `Ledger`, `JRand`, `HSettings`, `Caretaker`, `Guard`.
Soft: `HH_Venue_Lounge` (a lounge with a projector screen is a cinema too), `HH_VenueSign` (tests fall back to a stand-in sign when the Venues module is absent).
