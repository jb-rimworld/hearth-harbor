Reading all files under [Source/Hearth.Core](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core), [Source/Hearth/Contracts](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts), and [Source/Silverline/Contracts](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Silverline/Contracts) to inspect the foundation, contracts, lifecycle, serialization, and testing infrastructure.
### (1) Bugs, Crashes, Save/Load & Lifecycle Failures

1. **`Venues.All` returns dead objects from the previous save after loading an earlier save or starting a new game**
   - **Where:** [Venues.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/Venues.cs#L124-L145) (`Venues.cache`, `Venues.builtTick`, `Venues.All(Map)`)
   - **Why it matters:** `cache` and `builtTick` are static dictionaries keyed by `map.uniqueID` (always `0` for the first map) and are never cleared on load/new game. Line 133 checks `now - b < 250`. If a player plays to tick `50,000` (`b = 50000`) and then loads a save or starts a new game at tick `1,000` (`now = 1000`), `now - b` is `-49000 < 250`, returning destroyed/unloaded `Thing`, `CompVenueSign`, and `Room` instances from the old save for 49,250 ticks.
   - **Exact fix:** Add `Venues.Clear()` and call it from `HearthGame.StartedNewGame()` and `HearthGame.LoadedGame()`. Change line 133 to `now >= b && now - b < 250`, and verify `list.All(v => v.Sign != null && v.Sign.Spawned && v.Sign.Map == map)`.

2. **Adding the mod to an existing save never clones `HearthMod.Template.Profile`**
   - **Where:** [HearthGame.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthGame.cs#L17-L86) (`HearthGame.Profile`, `HearthGame.ExposeData`, `HearthGame.LoadedGame`)
   - **Why it matters:** `Profile` has field initializer `= new HearthProfile()`. When loading an existing save that did not have the mod, `Scribe_Collections.Look` loads saved `GameComponent`s first, and `Game.FillComponents()` instantiates missing `GameComponent`s via `new HearthGame(game)` **after** `LoadingVars`. `HearthGame.ExposeData()` is never called when the component is absent from the save XML. Thus `Profile` is never `null`, line 82 never runs, and the save gets an unconfigured default `HearthProfile` instead of `HearthMod.Template.Profile.CloneForNewGame()`.
   - **Exact fix:** Add `bool exposed;` to `HearthGame`, set `exposed = true;` inside `ExposeData()`, and in `LoadedGame()` check:
     ```csharp
     if (!exposed)
     {
         Profile = HearthMod.Template.Profile.CloneForNewGame();
         Profile.FirstStartCardDone = false;
         HLog.Info("Core", "Mod added to an existing save: new profile from template");
     }
     ```

3. **`SettingRow.Draw` silently overwrites and locks tier-linked settings on the first GUI frame**
   - **Where:** [SimpleSettingsWindow.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/SimpleSettingsWindow.cs#L98-L120) (`SettingRow.Draw`), [HSettings.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HSettings.cs#L134-L139) (`HSettings.Get(HSetting)`)
   - **Why it matters:**
     - `HSettings.Get(HSetting s)` (line 138) returns `s.DefaultFor(Tier)` **without** calling `s.Clamp(...)`. For `Int`/`Money` settings with `TierAnchors` at non-integer tiers (or fractional anchor interpolation), `Get(s)` returns an unrounded float (e.g. `12.5f`) and does not enforce `EffectiveHardMax`.
     - In `SettingRow.Draw` (line 100), `Widgets.HorizontalSlider` rounds its return value to `roundTo` (`1f` or `s.Step`) on **every** `OnGUI` frame even without user input. Comparing `Math.Abs(nv - clampedVal) > 1e-6f` triggers on frame 1 and calls `HSettings.Set(s, nv)`, marking the setting as user-overridden (`Store.Has(s.Key) == true`) and detaching it from the difficulty slider just by scrolling past it.
     - In free-text mode (lines 107–113), `float.TryParse` and `HSettings.Set(s, c)` run on every frame even when `nb == buf`, immediately locking any non-overridden setting the moment `Free` is checked.
   - **Exact fix:**
     - In `HSettings.Get(HSetting s)`, return `s.Clamp(s.DefaultFor(Tier))`.
     - In `SettingRow.Draw`, round the input value passed to `Widgets.HorizontalSlider` by the same `roundTo` step before comparing `Math.Abs(nv - roundedInput) > 1e-6f`.
     - In the `free` branch, only parse and call `HSettings.Set(s, c)` when `nb != buf`. Clear `buffers` whenever `prof.Tier` changes.

4. **`GuestApi.fallbackInfo` leaks `Pawn`/`Map`/`Game` across reloads and collides on `thingIDNumber`**
   - **Where:** [GuestApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/GuestApi.cs#L51-L76) (`GuestApi.fallbackInfo`, `GuestApi.Info(Pawn)`)
   - **Why it matters:** `fallbackInfo` is a static `Dictionary<int, GuestInfo>` keyed by `p.thingIDNumber` that is never cleared on load/new game or pawn despawn. Each `GuestInfo` holds a strong reference to `Pawn` (and transitively `Map` and `Game`), leaking entire game instances across reloads. In a new or reloaded save, a different pawn with the same `thingIDNumber` receives the previous save's `GuestInfo` (`gi.Pawn != p`, depleted `Budget`, wrong `ArrivedTick`).
   - **Exact fix:** Check `if (!fallbackInfo.TryGetValue(p.thingIDNumber, out var gi) || gi.Pawn != p)` in `GuestApi.Info`, clear `fallbackInfo` on `StartedNewGame`/`LoadedGame`, and periodically prune entries where `gi.Pawn == null || !gi.Pawn.Spawned || gi.Pawn.Dead`.

5. **`JRandState` streams accessed before `StartedNewGame`/`LoadedGame` cache `Seed = 0` for the entire session**
   - **Where:** [HearthGame.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthGame.cs#L24-L45) and [HearthGame.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthGame.cs#L96-L108) (`JRandState.Init`, `JRandState.Stream`)
   - **Why it matters:** `new HearthGame(game)` sets `Instance = this` with `Rand.Seed == 0`. In RimWorld, map generation, `ThingComp.PostSpawnSetup`, and `GameComponent.FinalizeInit()` all execute **before** `StartedNewGame()` and `LoadedGame()`. Any call to `JRand.Stream` or `JRand.Sealed` during those phases caches an `RngStream` with `Seed = 0` in `JRandState.live`. When `Rand.Init(...)` later sets `Seed`, it does not clear `live`, so those streams remain seeded with `0`.
   - **Exact fix:** In `JRandState.Init(int seed)`, clear `live` (saving counters first if any), and in `JRandState.Stream`, lazily initialize `if (Seed == 0) Init(GenText.StableStringHash(Find.World?.info?.seedString ?? "hh"));`.

6. **`Tills.FlushAll` assigns `stackCount > stackLimit` to one `Thing` and duplicates silver on partial placement failure**
   - **Where:** [Payments.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Contracts/Payments.cs#L123-L131) (`Tills.FlushAll`)
   - **Why it matters:** Line 125 sets `silver.stackCount = coins` on a single `Thing` even when `coins > ThingDefOf.Silver.stackLimit` (500). `GenPlace.TryPlaceThing` splits off stacks in a loop; if the first N stacks are placed and the last stack fails (e.g. full room), `TryPlaceThing` returns `false`, skipping `l.Post(acc, "world", ...)` while leaving the already-placed stacks on the map, duplicating silver on the next flush.
   - **Exact fix:** Loop while `coins > 0` in chunks of `int stack = Math.Min(coins, ThingDefOf.Silver.stackLimit)`, and call `l.Post(acc, "world", Money.FromSilver(stack), "Till emptied as silver")` for each placed stack.

7. **`RngStream.Range(int min, int maxExclusive)` signed 32-bit overflow when `maxExclusive - min > int.MaxValue`**
   - **Where:** [JRandCore.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Pure/JRandCore.cs#L68-L73) (`RngStream.Range(int, int)`)
   - **Why it matters:** `maxExclusive - min` is computed as a signed `int` before casting to `ulong`. When `min < 0` and the span exceeds `int.MaxValue`, the subtraction overflows to a negative `int`, sign-extending to `> 2^63` in `ulong` and overflowing back to `(int)`.
   - **Exact fix:** Replace line 71 with `ulong span = (ulong)((long)maxExclusive - min);`.

8. **`Money.ToString()` vs `Money.Parse()` incompatibility breaks Scribe if `Money` is ever used with `Scribe_Values`**
   - **Where:** [Money.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Pure/Money.cs#L51-L59) (`Money.ToString`, `Money.Parse`), [Ledger.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/Ledger.cs#L28) (`Ledger.Entry.ExposeData`)
   - **Why it matters:** `Money.ToString()` formats as `"#,##0.00"` (losing 3 decimal places and inserting commas), while `Money.Parse` calls `long.Parse(s, CultureInfo.InvariantCulture)` expecting raw integer `Micro`. Any module author using `Scribe_Values` or `ParseHelper` on `Money` will hit `FormatException`.
   - **Exact fix:** Add a helper `ScribeMoney.Look(ref Money value, string label, Money defaultValue = default)` that scribes `long micro = value.Micro` via `Scribe_Values.Look(ref micro, label, defaultValue.Micro)`, and use it in `Ledger.Entry.ExposeData()`.

9. **Static events `Ledger.Changed` and `Payments.Paid` leak subscribers across saves and can throw mid-transaction**
   - **Where:** [Ledger.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/Ledger.cs#L61-L66), [Payments.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Contracts/Payments.cs#L59-L65)
   - **Why it matters:** Both events are `static` and never cleared on load/new game, leaking any `GameComponent`/`MapComponent`/UI subscribers from previous saves and invoking dead callbacks. Also, `Changed?.Invoke(...)` in `Ledger.Post` is not wrapped in `try/catch`; a throwing subscriber aborts the caller **after** `balances`, `journal`, and `daily` have already been mutated.
   - **Exact fix:** Wrap each subscriber invocation in `try/catch` (logging via `HLog.Error`), or make `Changed` an instance event on `Ledger`.

---

### (2) RimWorld API Misuse & Pitfalls

1. **Harmony patches are applied in the `Mod` constructor instead of `[StaticConstructorOnStartup]`**
   - **Where:** [HearthMod.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthMod.cs#L90-L117) (`HearthMod` ctor, `ApplyPatches`)
   - **Why it matters:** `Mod` constructors execute **before** XML `Def`s are loaded, before `[DefOf]` fields are populated, and before other mods finish loading. Any `[HarmonyPatch]` class whose `Prepare()`, `TargetMethod()`, or static initializer touches `DefOf` or `DefDatabase<T>` will see `null`/empty databases and fail.
   - **Exact fix:** Move `Harmony = new Harmony(HarmonyId); ApplyPatches(asms);` from `HearthMod` constructor into `HearthStartup` (`[StaticConstructorOnStartup]`) before calling `m.OnDefsLoaded()`.

2. **`GuestApi.IsVanillaVisitor` hijacks quest Bestowers, Imperial guards, military aid, beggars, and travelers, and drops downed visitors**
   - **Where:** [GuestApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/GuestApi.cs#L60-L62) (`GuestApi.IsVanillaVisitor`)
   - **Why it matters:**
     - `p.GetLord() != null` is true for almost every friendly NPC group on a player home map: Royal Bestowing Ceremony pawns (`LordJob_BestowingCeremony`), military aid (`LordJob_AssistColony`), beggars (`LordJob_BegForItems`), and passing travelers (`LordJob_TravelAndExit`). When `Inn` is off, `GuestActivities.TryPick` will pull Bestowers or military aid soldiers out of their duties to gamble or shop.
     - `!p.Downed` makes `IsGuest(p)` and `Info(p)` return `false`/`null` the instant a guest is downed, breaking medical rescue, insurance, and caretaker checks for downed guests.
   - **Exact fix:** Check `var lj = p.GetLord()?.LordJob; bool isVisitorLord = lj is LordJob_VisitColony || lj is LordJob_TradeWithColony;` in `IsVanillaVisitor`, and in `Info(p)` still return existing `fallbackInfo` for downed pawns that were already registered as visitors.

3. **Midnight till flush mixes UTC tick day (`t / 60000`) with local timezone hour (`GenLocalDate.HourOfDay(home)`) and does not save `lastTillDay`**
   - **Where:** [HearthGame.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthGame.cs#L22) and [HearthGame.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthGame.cs#L67-L72)
   - **Why it matters:**
     - `lastTillDay` is not saved in `ExposeData()`. Saving and reloading during local hour 0 (`00:00–00:59`) resets `lastTillDay = -1` and flushes tills a second time.
     - `t / 60000` is the raw game-tick day, whereas `GenLocalDate.HourOfDay(home) == 0` depends on map longitude and `gameStartAbsTick`. If `t / 60000` rolls over during local hour 0, or if the player has multiple home maps at different longitudes, flushes can double-fire or skip.
   - **Exact fix:** Persist `lastTillDay` in `HearthGame.ExposeData()` via `Scribe_Values.Look(ref lastTillDay, "lastTillDay", -1)` and compute the local day via `GenDate.DaysPassedAt(GenLocalDate.TickAbs(home))` (or track per-map last flush day).

4. **`SettingRow.Draw` reads/writes `freeText` in the per-save `Store` even for `s.Global == true` settings**
   - **Where:** [SimpleSettingsWindow.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/SimpleSettingsWindow.cs#L95) (`SettingRow.Draw`)
   - **Why it matters:** `HSettings.Store(s)` uses `HearthMod.Template.Store` when `s.Global` is true, but line 95 of `SimpleSettingsWindow.cs` uses `HearthProfile.Current?.Store ?? HearthMod.Template.Store`, reading/writing `freeText` in the wrong store for global settings while a game is loaded.
   - **Exact fix:** Make `HSettings.Store(HSetting s)` `internal` and call `HSettings.Store(s)` on line 95.

5. **`About.xml` lists `Orion.Hospitality` in both `<loadBefore>` and `<incompatibleWith>`, and `LoadFolders.xml` includes `<li>/</li>`**
   - **Where:** [About.xml](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/About/About.xml#L38-L48), [LoadFolders.xml](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/LoadFolders.xml#L3-L5)
   - **Why it matters:** Listing an incompatible mod in `<loadBefore>` is contradictory metadata. In `LoadFolders.xml`, `<li>/</li>` makes RimWorld scan the mod root directory for `Defs/`, `Assemblies/`, etc. in addition to `Common` and `1.7`.
   - **Exact fix:** Remove `<li>Orion.Hospitality</li>` from `<loadBefore>` in `About.xml` and remove `<li>/</li>` from `LoadFolders.xml`.

6. **`Common/Defs/Core/Core_WorkAndResearch.xml` puts hospitality work types and research tab in `Core`**
   - **Where:** [Core_WorkAndResearch.xml](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Common/Defs/Core/Core_WorkAndResearch.xml#L4-L32)
   - **Why it matters:** When `Silverline` ships standalone (only `Core` + `Bank`), loading `Common/Defs/Core` injects `HH_Hosting` ("Host"), `HH_Serving` ("Serve"), and `HH_Hospitality` into the game even though no guest or venue modules exist.
   - **Exact fix:** Move `HH_Hosting`, `HH_Serving`, and `HH_Hospitality` out of `Common/Defs/Core` into a guest-only Def folder (and `HH_Banking` into `Common/Defs/Bank`).

---

### (3) Performance Risks

1. **`HLog.Debug` performs synchronous disk I/O (`new FileInfo` + `File.AppendAllText`) and evicts `HLog.Recent` even when `HLog.Verbose == false`**
   - **Where:** [HLog.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HLog.cs#L56-L95) (`HLog.Write`, `HLog.AppendFile`)
   - **Why it matters:** Line 69 (`if (level == LogLevel.Debug && !Verbose) return;`) runs **after** string formatting, `Recent.AddLast(line)`, and `AppendFile(line)`. Every `HLog.Debug` call during gameplay opens, writes, and closes `HearthHarbor.log` on the main thread and pushes debug lines into the 300-line `Recent` diagnosis buffer (evicting real warnings and errors) even when `Verbose` is off.
   - **Exact fix:** Move `if (level == LogLevel.Debug && !Verbose) return;` to the first line of `HLog.Write`, and keep a persistent `StreamWriter` open instead of calling `new FileInfo` + `File.AppendAllText` on every log entry.

2. **`Venues.All(Map)` scans every artificial building on the map with `TryGetComp<CompVenueSign>()` every 250 ticks**
   - **Where:** [Venues.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/Venues.cs#L135-L142) (`Venues.All`)
   - **Why it matters:** `ThingRequestGroup.BuildingArtificial` contains every wall, door, conduit, and furniture piece on the map (often 10,000+ buildings). Scanning all of them with `TryGetComp<CompVenueSign>()` every 250 ticks (or whenever `MarkDirty` is called) causes unnecessary main-thread spikes.
   - **Exact fix:** Track spawned signs in `static readonly Dictionary<int, HashSet<CompVenueSign>> signsByMap`, adding in `CompVenueSign.PostSpawnSetup` and removing in `CompVenueSign.PostDeSpawn`. `Venues.All(map)` then iterates only actual signs.

3. **`JRand.Stream` allocates a new `RngStream` and runs two setting lookups on every call when `core.sealedFate` is false (the default)**
   - **Where:** [HearthGame.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthGame.cs#L124-L143) (`JRand.Stream`, `JRand.VanillaStream`), [JRandCore.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Pure/JRandCore.cs#L102-L117) (`RngStream.ChaChaLite`)
   - **Why it matters:** `core.sealedFate` defaults to `false`. Every call to `JRand.Stream(name)` performs two dictionary lookups (`HSettings.GetBool("core.sealedFate")` and `HSettings.GetInt("core.rngAlgorithm")`) and allocates `new RngStream(...)`. Also, `ChaChaLite` allocates two `uint[16]` arrays on the heap on every `NextULong()` call.
   - **Exact fix:** Reuse a static `RngStream` instance in `JRand` (reseeding it in place) or cache the `HSetting` references directly (`static readonly HSetting SealedFateSetting, AlgoSetting`), and use `stackalloc uint[16]` (or local `uint` variables) in `ChaChaLite`.

4. **`GuestActivities.TryPick` allocates closures, concatenated site strings, and a `List` on every think-tree evaluation**
   - **Where:** [GuestApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/GuestApi.cs#L140-L159) (`GuestActivities.TryPick`)
   - **Why it matters:** Called frequently by guest think nodes. Allocates `new List<(IGuestActivity, float)>()`, concatenates `"Activity.Desire." + a.Id` and `"Activity.Job." + pick.a.Id`, allocates lambda closures for `Guard.Run`, and uses LINQ `options.Sum(o => o.w)`.
   - **Exact fix:** Reuse a static `List<(IGuestActivity a, float w)>` buffer, accumulate `float sum` in a loop without LINQ, and pre-cache site names on registration.

5. **`SimpleSettingsWindow.Draw` runs LINQ `.Where().OrderBy().ThenBy()` and 3 `.ToLowerInvariant()` allocations per setting on every `OnGUI` event**
   - **Where:** [SimpleSettingsWindow.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/SimpleSettingsWindow.cs#L46-L64) (`SimpleSettingsWindow.Draw`, `Match`)
   - **Why it matters:** `OnGUI` runs multiple times per rendered frame. Re-sorting all settings and allocating lowercase strings on every GUI event causes constant GC allocations while settings are open.
   - **Exact fix:** Cache the filtered/sorted `List<HSetting>` and rebuild it only when `search`, `tpl.ShowNerd`, or the active game changes; use `string.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0`.

---

### (4) Design Problems in Contracts

1. **`Guard` tracks `consecutive` failures per `ModuleId` instead of per `(ModuleId, site)` and does not short-circuit when paused**
   - **Where:** [Guard.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/Guard.cs#L19-L68) (`Guard.consecutive`, `Guard.Run`, `Guard.Ok`, `Guard.Fail`)
   - **Why it matters:**
     - `Ok(ModuleId m)` resets `consecutive[m] = 0` for the entire module. If one site in a module throws 100% of the time (e.g. one activity's `Desire` or one patch), and any other `Guard.Run` in the same module succeeds (e.g. a second activity in the same `GuestActivities.TryPick` loop), `consecutive[m]` is reset to `0` every tick and **never** reaches `MaxConsecutive = 3`.
     - `Guard.Run` does not check `if (IsPaused(m)) return;`. Call sites that invoke `Guard.Run(ModuleId.Bank, ...)` without first checking `Modules.On(ModuleId.Bank)` (e.g. `Tills.FlushAll` line 122 and `BankTabs.VisibleTabs` line 46) continue executing paused or disabled modules.
     - `paused`, `consecutive`, and `PauseReasons` are static and never cleared on game load/new game, and `SimpleSettingsWindow` has no UI to view `PauseReasons` or call `Guard.Resume(m)` (despite the pause letter instructing the player to open Mod Settings to restart it).
   - **Exact fix:** Key `consecutive` by `string site` (or `(ModuleId, string)`), check `if (IsPaused(m)) return;` (and `return fallback;`) at the top of `Guard.Run`, clear `paused`/`consecutive` on `Resume` or new session as intended, and draw a "Paused modules / Resume" banner at the top of `SimpleSettingsWindow.Draw`.

2. **`Ledger` daily reporting (`DayTotal` / `DayTotalPrefix`) and income/expense categories are broken by double-entry till flushing**
   - **Where:** [Ledger.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/Ledger.cs#L11-L70) (`Ledger.Post`, `Ledger.DayTotal`, `Ledger.DayTotalPrefix`), [Payments.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Contracts/Payments.cs#L100-L128), [GuestApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/GuestApi.cs#L104-L116)
   - **Why it matters:**
     - `Ledger.cs` line 14 documents `"colony:income:<module>" / "colony:expense:<module>"` as bookkeeping categories for reports. However, `Payments.TryCharge`, `InventorySilverProvider.TryPay`, and `GuestApi.ChargeGuest` always book `from: "world"` (or guest account) `to: "colony:till:<mapId>"`. `GuestApi.ChargeGuest` never passes `category` to `Payments.TryCharge` or `Ledger.Post`. Nothing ever posts to `"colony:income:<module>"`.
     - `Ledger.Post` updates `daily[day][to] += amount` and `daily[day][from] -= amount`. When guests pay into `"colony:till:0"` during the day and `Tills.FlushAll()` posts `from: "colony:till:0", to: "world"` at midnight, `daily[day]["colony:till:0"]` is reduced back to **zero**. Consequently, `DayTotal(day, Payments.ColonyTill(map))` is zeroed every night and `DayTotalPrefix(day, "colony:income:")` is always `0`.
   - **Exact fix:** Book external income from `"colony:income:" + category` (allowing negative balances on `"colony:income:*"` accounts, which represents cumulative credit revenue while `"world"` is only used when physical silver is spawned/destroyed, or add an explicit `string category = null` parameter to `Ledger.Post` and `Payments.TryCharge` that records category totals in `daily[day]["colony:income:" + category]` independently of till flushes).

3. **`Payments` and `GuestApi` lack any payout/refund contract (`TryPayout` / `CreditGuest`), lack module-gating on `IPaymentProvider`, and mismatch `Available` vs `TryCharge`**
   - **Where:** [Payments.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Contracts/Payments.cs#L13-L103) (`IPaymentProvider`, `Payments`, `InventorySilverProvider`), [GuestApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/GuestApi.cs#L35-L116) (`IGuestApiImpl`, `GuestApi`)
   - **Why it matters:**
     - `AGENTS.md` Rule 5 forbids modules from spawning or destroying silver directly, yet `Payments` and `GuestApi` only support charging a pawn (`TryCharge` / `ChargeGuest`). Modules have no contract method to pay casino winnings, buy items at a fence, pay insurance claims, or refund guests in `PrepareRemoval`.
     - `IPaymentProvider` has no `ModuleId Module { get; }` property. Providers registered by `Bank` or `Casino` remain active in `Payments.providers` even when their module is disabled, and `Payments` wraps them in `Guard.Run(ModuleId.Core, ...)` instead of `p.Module`.
     - `Payments.Available(payer)` sums across all providers, whereas `Payments.TryCharge` requires a single provider to cover the entire `amount` (`p.Available(payer) >= amount`). A pawn with 15 cash + 15 card has `Available == 30`, but `TryCharge` for `20` fails.
     - `InventorySilverProvider.TryPay` (line 88) uses floating-point `Math.Ceiling(amount.Silver - 1e-9)` (which rounds `1.00001` silver up to `2` coins) and posts `Money.FromSilver(coins)` to `toAccount`, while `Payments.Paid` and `GuestApi.Spend` record the unrounded `amount`.
     - `GuestApi.ChargeGuest` fallback (lines 110–113) posts directly to `Ledger` when a vanilla visitor lacks physical silver, skipping `Payments.Paid` (contradicting its XML doc comment).
   - **Exact fix:**
     - Add `ModuleId Module { get; }` to `IPaymentProvider`; filter by `Modules.On(p.Module)` and pass `p.Module` to `Guard.Run` in `Payments.Available` and `Payments.TryCharge`.
     - Add `Payments.TryPayout(Pawn recipient, Money amount, string fromAccount, string memo)` and `GuestApi.CreditGuest(Pawn guest, Money amount, string category, string memo)`.
     - Support splitting across providers in `Payments.TryCharge` when `Payments.Available(payer) >= amount` (or make `InventorySilverProvider` give change / credit fractional remainder).
     - Replace `(int)Math.Ceiling(amount.Silver - 1e-9)` with integer ceiling `(int)((amount.Micro + Money.PerSilver - 1) / Money.PerSilver)`.
     - Fire `Payments.Paid` (or register a low-priority `VanillaVisitorPaymentProvider`) in the vanilla visitor fallback path.

4. **`Venues` and `CompVenueSign` ignore `Modules.On(type.module)`, cache dereferenced `Room` instances, and ignore `CompProperties_VenueSign.wallMounted`**
   - **Where:** [Venues.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/Venues.cs#L36-L167) (`Venue`, `CompVenueSign`, `Venues`)
   - **Why it matters:**
     - `Venues.All(map)` does not check `Modules.On(c.type.module)`. Disabling `Casino` or `ShopsFood` leaves existing signs active in `Venues.All`, `Venues.OfType`, and `Venues.At`.
     - `CompVenueSign.PostSpawnSetup` (line 75) falls back to `DefDatabase<VenueTypeDef>.AllDefs.FirstOrDefault()` without filtering by `Modules.On(d.module)`.
     - `Venue.Room` caches a `Verse.Room` reference for up to 250 ticks. When any door/wall changes, RimWorld marks the old `Room` as `Dereferenced` and creates a new `Room` object, so `Venue.Contains(c)` and `Venues.At(c, map)` (`v.Room == room`) fail for up to 250 ticks.
     - `CompProperties_VenueSign.wallMounted` (line 52) is never read; `Venues.RoomOfSign` (line 153) only checks `sign.def.building?.isAttachment == true`.
     - `Venues.All(map)` returns the mutable cached `List<Venue>` directly.
   - **Exact fix:** Filter `Venues.All` and `CompVenueSign.PostSpawnSetup` by `Modules.On(type.module)`, make `Venue.Room` evaluate `Venues.RoomOfSign(Sign)` (or refresh if `room.Dereferenced`), check `c.Props.wallMounted` in `RoomOfSign`, and return `IReadOnlyList<Venue>` from `Venues.All`.

5. **`IBankImpl` cannot distinguish till direct-deposit from world payouts, lacks electronic expense debiting, and `BankTabs.VisibleTabs` ignores `Modules.On(ModuleId.Bank)`**
   - **Where:** [BankApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Silverline/Contracts/BankApi.cs#L15-L47) (`IBankImpl`, `BankTab`, `BankTabs`)
   - **Why it matters:**
     - `IBankImpl.Deposit(Map map, Money amount, string memo)` has no `fromAccount` parameter, making it ambiguous whether it debits `Payments.ColonyTill(map)`, `"world"`, or physical silver on `map`.
     - `IBankImpl` has `Withdraw` (spawning physical silver), but no method to debit the colony bank account electronically to another ledger account (`TryDebit(Money amount, string toAccount, string memo)`).
     - `BankTabs.VisibleTabs` (line 46) does not check `Modules.On(ModuleId.Bank)` (nor does `BankTab` carry a `ModuleId` for tabs added by sub-modules).
   - **Exact fix:** Add `string fromAccount` to `Deposit` (or add `bool TryTransfer(string fromAccount, string toAccount, Money amount, string memo)`), add `ModuleId Module = ModuleId.Bank` to `BankTab`, and check `Modules.On(t.Module)` in `BankTabs.VisibleTabs`.

6. **Disabling a module in `SimpleSettingsWindow` / `HearthProfile.SetModule` never invokes `HearthModuleBase.PrepareRemoval`**
   - **Where:** [HearthProfile.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HearthProfile.cs#L34) (`HearthProfile.SetModule`), [SimpleSettingsWindow.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/SimpleSettingsWindow.cs#L38-L39)
   - **Why it matters:** `SimpleSettingsWindow` calls `prof.SetModule(m, on)` on every GUI frame, and toggling a module from `true` to `false` during an active game never calls `PrepareRemoval` on its `HearthModuleBase`, leaving active guest lords, jobs, and hediffs behind.
   - **Exact fix:** Only call `prof.SetModule(m, on)` when `on != wasOn`, and when transitioning `true -> false` in a loaded game (`HearthProfile.Current == prof`), invoke `Guard.Run(m, "PrepareRemoval", () => mod.PrepareRemoval(report))`.

---

### (5) Concrete Improvements

1. **Harden `HearthTestRunner` and `SmokeSuite` against false positives and state pollution**
   - **Where:** [HearthTests.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Testing/HearthTests.cs#L80-L149) (`HearthTestRunner.GameComponentTick`, `WriteResults`, `SmokeSuite.Run`)
   - **Why it matters:**
     - Passing `HH_TEST=smoke,misspelled_suite` silently skips `misspelled_suite` and reports `RESULT PASS`.
     - `WriteResults()` reports `RESULT PASS` even when `HLog.ErrorCount > 0`, violating the `AGENTS.md` definition of done.
     - `r.It` (`IEnumerator<int>`) is never `.Dispose()`d when a suite finishes or times out.
     - `SmokeSuite` mutates `Ledger.Current` with `"test:a"` and `"test:b"` entries and leaves them behind for subsequent suites.
   - **Exact fix:** Record a failure for any name in `wanted` (other than `"all"`) not found in `all`, require `fails == 0 && HLog.ErrorCount == 0` for `PASS`, call `r.It?.Dispose()` when `r.Done` is set, and use a dedicated `new Ledger()` instance in `SmokeSuite`.

2. **Prune zero-balance ephemeral accounts in `Ledger` and guard against null/empty account IDs**
   - **Where:** [Ledger.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/Ledger.cs#L46-L85) (`Ledger.Post`, `Ledger.DailyCheck`)
   - **Why it matters:** Ephemeral accounts (`"guest:<pawnThingId>"`) remain in `balances` with value `0` forever after guests leave, bloating save files and slowing down `Accounts()` and `SumAll()`. Passing `null` for `from` or `to` throws `ArgumentNullException`.
   - **Exact fix:** Add `if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return false;` at the start of `Ledger.Post`, and in `DailyCheck()` remove zero-balance keys starting with `"guest:"`.

3. **Unify `Caretaker.TotalFixes` with `Check.TotalFixes` and honor `force` in `Caretaker.RunAll`**
   - **Where:** [Guard.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/Guard.cs#L82-L116) (`Caretaker`)
   - **Why it matters:** `Caretaker.TotalFixes` is only incremented in `Caretaker.Fixed(...)`, whereas `c.TotalFixes` is incremented by the return value of `c.Run()`, allowing the two totals to diverge. In `Caretaker.RunAll(bool force = false)`, `force` is unused.
   - **Exact fix:** Change line 101 to `if (!force && !Modules.On(c.Module)) continue;` and increment `Caretaker.TotalFixes += n;` only when `Caretaker.Fixed` was not already called (or compute `TotalFixes` from `c.TotalFixes` + ad-hoc fixes).

4. **Guard `GuestActivities.TryPick` against `null` `guest`/`info` and `NaN`/`Infinity` weights, and use `JRand.Stream`**
   - **Where:** [GuestApi.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth/Contracts/GuestApi.cs#L140-L159) (`GuestActivities.TryPick`)
   - **Why it matters:**
     - Uses `Verse.Rand.Value` instead of `JRand.Stream("guest.activity").NextFloat()`, violating `AGENTS.md` Rule 6 and breaking `core.sealedFate`.
     - If `info == null` is passed, `TryPick` calls `a.Desire(guest, null)` on every activity, causing every module to throw `NullReferenceException` and trip `Guard`.
     - If `a.Desire` returns `float.PositiveInfinity`, `w > 0` is true and `roll = Rand.Value * sum` becomes `Infinity` or `NaN`.
   - **Exact fix:** Add `if (guest == null || (info ??= GuestApi.Info(guest)) == null) return null;` at the top of `TryPick`, validate `if (w > 0f && !float.IsNaN(w) && !float.IsInfinity(w))`, and replace `Rand.Value` with `JRand.Stream("guest.activity").NextFloat()`.

5. **Fix `HSetting.Format` for non-contiguous enums and thread-safe access to `HLog.Recent`**
   - **Where:** [HSettings.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HSettings.cs#L77-L79) (`HSetting.Format`), [HLog.cs](file:///Users/julius/Documents/Coding/22_RimWorld/HearthHarbor/Source/Hearth.Core/Foundation/HLog.cs#L28) (`HLog.Recent`)
   - **Why it matters:** `HSetting.Format` indexes `Enum.GetNames(EnumType)[(int)v]`, which fails or returns the wrong name if enum values are not `0..N-1`. `HLog.Recent` is a public `LinkedList<string>` mutated under a private lock; iterating it from the diagnosis UI while a background thread (e.g. `AiFlavour`) logs throws `InvalidOperationException`.
   - **Exact fix:** In `HSetting.Format`, use `string name = Enum.GetName(EnumType, (int)v); if (name != null) return ("HH.Enum." + EnumType.Name + "." + name).TranslateOr(name);`. In `HLog`, add `public static string[] RecentSnapshot() { lock (Lock) return Recent.ToArray(); }`.

---

The full review and remediation plan is also available in [implementation_plan.md](file:///Users/julius/.gemini/antigravity-cli/brain/8068463d-65c3-4ccc-a8ca-5f931423f90f/implementation_plan.md). Let me know if you want to proceed with applying any of these fixes.
