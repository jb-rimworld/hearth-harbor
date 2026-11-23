# Inn & Hotel module (Hearth.Guests)

Guest core of Hearth & Harbor (KONZEPT chapter 5). Code: `Source/Hearth/Guests`, defs `Common/Defs/Inn`, texts
`Common/Languages/English/Keyed/HH_Inn.xml` (regenerate with `python3 tools/inn/gen_keyed.py`). Settings keys start with `inn.`.

## Who drives which guest (one brain per guest)

| Guest | Brain | Where |
|---|---|---|
| Inn guest (booked group, `LordJob_HH_GuestStay`) | the duty think tree `HH_GuestStay`: check-in → sleep → packed food → `JobGiver_HH_GuestActivity` (asks `GuestActivities.TryPick`, at most every 600 ticks) → joy → wander in the wing | `Common/Defs/Inn/Inn_AI.xml`, `GuestJobs.cs` |
| Vanilla visitor / trader group (`LordJob_VisitColony`, `LordJob_TradeWithColony`) | `Hearth.ShopsFood.GuestFallback`: every 90 ticks, idle visitors in the lord's chill toil (`LordToil_DefendPoint`) get one `GuestActivities.TryPick` offer (every 240–600 ticks per pawn) | `Source/Hearth/Venues/GuestFallback.cs` |

The fallback is ticked by `GuestFallbackModule` (ModuleId Core, no save data), so it works with the Inn module on or off and with
any activity module. `GuestFallback.Drives(p)` is false for every Inn-registered guest, so nobody is offered activities twice.
`GuestFallback.Run(map)` is idempotent per tick (the old caller in `VenueMapComp` may stay until the Venues owner removes it).

## Contract additions (GuestApi, source compatible)

- `GuestInfo.Vip` (wealth ≥ `GuestInfo.VipWealth` = 60).
- `GuestApi.Renown(map)`, `RenownStars(map)`, `Whispers(map)`, `AddRenown(map, delta, reason)`, `HasRenown`, `NeutralRenown` (50 without the Inn).
  Implemented by the Inn through the optional interface `IGuestRenownImpl` (the bridge `GuestApiBridge`).
- Whispers: while the Underworld module runs, it owns them (`Underworld.Whispers`); the Inn forwards its own increments
  (guest killed +4, arrested +5) through `GuestRegistry.AddWhispers`. Without Underworld the Inn keeps its own count.
- `GuestApi.SatisfactionChanged(pawn, delta, reason)` event, raised after every `AddSatisfaction`.

## Hooks adopted from other modules (InnModule.WireOtherModules)

- Underworld: subscribes to `Underworld.RenownDelta` → `GuestRegistry.ChangeRenown(delta, reason)`; shady stays
  (`GuestRecord.Shady`, `GuestArrivalRequest.ForceShady`; rolled for rough/outlaw factions when the underworld is unlocked) call
  `Underworld.MarkShady`; faction pick weights use `RespectableArrivalFactor` / `OutlawArrivalFactor` while the underworld is visible.
- Hospitality import: `HospitalityImport.GuestBedImported` → `InnModule.AdoptImportedBed` (sets `CompHH_GuestBed`, `MarkAdopted`);
  pending beds are adopted again on game load. `Area_HH_GuestWing(AreaManager)` and `(AreaManager, string)` exist; a wing the importer
  adds to the vanilla area list is moved into `MapComponent_HH_GuestWings` within 250 ticks (`AdoptForeignWings`).
- UI shell: `HearthUI.GuestBedProbe`, `HearthUI.RenownLine` (if nobody set it), overview section `inn.wings` (wing status lines).
- DLC shrines: `GuestInfo.PlannedLeaveTick` is re-read every 1250 ticks, so extensions and shortenings are honoured (test `inn.contracts`).

## Features of this round

- **Door policy** (5.9, N28/N29): `DoorPolicy` (saved in the register), window `Dialog_HH_DoorPolicy` (front desk gizmo, overview
  button). Factions and tech level at planning, stay kinds at planning/arrival, gender and age in the `PawnGenerationRequest`,
  xenotype and royal title by validator (8 retries, then the slot stays empty), wealth band when wealth is rolled. Returners are checked
  first. Closing the window shows a one-liner aimed at the player ("Ugh. A hippie.", "Woke, but with limits, huh?", one per filter).
- **Wing status line** (N31): `WingStatus.Lines(map)` — Bedroom / Kitchen / Restaurant (/ Bathroom with a bath mod) · est. stars.
  Shown on guest beds, the front desk, the overview page and the HearthUI overview section.
- **Front desk hours** (5.13): `inn.deskOpenHour`/`inn.deskCloseHour` (6–22). Outside them and without a host by the desk, check-in
  works as an honesty box (`inn.honestyDiscount`, −10 %).
- **Waiting and night bell thoughts** (5.13): `HH_GuestWaited`, `HH_GuestBellNobody`, `HH_GuestWaitingRoom`; given by
  `GuestThoughts.Waited` (direct call) and by the bridge on the Venues module's "nobody came" outcomes. A waiting room (seats or a
  lounge venue) lowers the waiting thought one stage and adds +1 … +4 by impressiveness.
- **Day visitors** leave at "last orders": when no venue is open now and in 30 minutes (`GuestRegistry.AnyVenueOpenSoon`).
- **Return visits and leaving gift** (5.14): `GuestReturns.ReturnDays`, planned at departure into the arrival forecast
  (`PlannedGroup.Returning`, pawns pinned in `WorldPawns.ForcefullyKeptPawns` until they arrive or the plan is dropped);
  gift chance `Lerp(0.2, 0.9, V)`, a small item at the front desk.

## Arrival plan (balance round)

`GuestScheduler` plans groups into a 5-day forecast (`inn.forecastDays`). Rules, in the order they matter:

- **Rate** = `BaseRatePerDay (1.0) × RenownCurve × capacity × inn.arrivalRate`. Capacity is the night share (`NightCapacity(freeBeds)`: any free
  bed 0.8, two or more 1.0) plus the day-visitor share (0.3 per open venue, at most 0.6). The renown curve is flat at the low end (0.9 at renown 0,
  0.95 at 25) and grows from 50 on (1.0, 1.4 at 75, 1.8 at 100).
- **Planning by thinning.** Candidate groups fall at the rate of a house with every bed free; each is kept with the probability
  `rate(ProjectedFreeBeds(arrival)) / maxRate`. `ProjectedFreeBeds(t)` = all guest beds minus overnight guests still booked at `t` minus planned
  overnight groups (return visits included) that cover `t`. A group for tomorrow is therefore not judged by tonight's full house, and the forecast
  never books more beds than exist. The kind of a kept group (night or day) follows each side's share of the capacity at that moment.
- **Drought breaker.** If the plan has been awake for more than `clamp(1.5 / rate, 1, 15)` days without a real arrival, free capacity exists and
  nobody is due within 12 hours, one group is planned for the next hours (`Counters.DroughtBreaks`, log line `Drought breaker on …`). Pure chance or a
  plan that lost its group must not leave free beds empty for days. With a low `inn.arrivalRate` the tolerated quiet time grows with it.
- **Arrival.** A group that finds no free bed (`NoBed`), closed venues (`Closed`) or no faction waits 8 hours and tries again, three times, then it is
  dropped (and the thinning plans new ones). `SchedulerStats` counts planned / thinned / arrived / postponed / dropped; the long run prints them per day.
- The Overview page shows "Arrivals, next 24 h" (planned groups and the current rate).

Diagnostics: `MapComponent_HH_GuestScheduler.DiagLog = new List<string>()` collects one line per decision (the long run writes it to
`docs/longrun/longrun_sched.txt`). Suite `inn.forecast` also tests the projection, the drought breaker and the overview line.

## Tests

`tools/run_test.sh 'smoke,inn.*' <label>`: cleanup, contracts, dayvisit, door, enemies, fallback, forecast, handling, math, night,
nothing, payment, returns, stay, wing, wingstatus.

### The "inn.night hang" (integration round)

Not a deadlock and not Inn logic. The test profile copies Julius' prefs (autosave every day). The vanilla autosave is a synchronous
long event that only starts after its "Autosaving" screen has been drawn once; a test window that is hidden (another space,
covered by the other slots, minimized) draws no GUI frames, so the save never starts and the game idles (main thread in `kevent`)
until the window becomes visible again or the runner's 900 s timeout kills it. `inn.night` is the first suite whose waits cross the
one-day mark, so it was always the one that "hung" (venues hung the same way in m1). Proof in run `innI3`: the autosave file
appeared at the very second the stalled game was brought to the front and the suite continued.
Fix: `InnTestKit.FastTime()` (start of every inn suite) sets `Prefs.AutosaveIntervalDays = 1000` for the test profile and
`DebugViewSettings.neverForceNormalSpeed` (combat would otherwise force 1× speed). The runner should do this for all modules.
