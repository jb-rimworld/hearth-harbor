# AGENTS.md — how to work on Hearth & Harbor

Guide for humans and AI agents. Read this before changing anything. The design is in
`../_Planung_Hospitality_2026-11-22/KONZEPT_v2.md` (German; chapter numbers below refer to it).

## What this is
One RimWorld mod, **Hearth & Harbor** (`juliusberger00.hearthharbor`, def prefix `HH_`), split into **modules** that can be
switched on/off per save game, plus the bank **Silverline** (`SL_` defs) which ships inside it and later also as a standalone mod.
Main target RimWorld **1.7** (CoreCLR, net10); 1.6 and 1.5 build from the same source (net472, `#if RW15` etc.).
License: MIT, everything we write. Never copy code from Hospitality/GPL/unlicensed mods (see KONZEPT 15.6).

## Layout
```
About/ LoadFolders.xml           mod metadata (Common + version folder + conditional Compat folders; written by tools/i18n/split_optional.py)
Common/Defs/<Module>/*.xml       defs, one folder per module (Core, Inn, ShopsFood, Kitchen, Casino, Bank, Insurance, UI …)
Common/Languages/English/Keyed/HH_<Module>.xml   translation keys "HH.<Module>.<Thing>" — one file per module
Common/Textures/Things/HH/<Module>/…  Common/Textures/UI/HH/<Module>/…   our textures (never vanilla paths)
Common/Sounds/HH/…               sounds
Compat/<slug>/Languages/…        translations of defs that need a DLC or another mod (MayRequire); loaded only when that mod is active
1.7/Assemblies 1.6/Assemblies 1.5/Assemblies   build output (do not edit)
Source/Hearth.Core/   Foundation (settings, profile, ledger, log, guard, caretaker, random), Contracts (Payments), Pure (no game refs), Testing
Source/Silverline/    the bank (namespace Silverline); Contracts/BankApi.cs
Source/Hearth/        everything with guests (namespace Hearth.<Module>); Contracts/GuestApi.cs, Venues.cs, HearthTabs.cs
Source/Tests/         unit tests for Source/Hearth.Core/Pure (no game needed)
tools/                run_test.sh, placeholder_textures.py, art generator …
```
Dependency direction: `Hearth → Silverline → Hearth.Core`. Silverline must never reference Hearth.dll.

## Build and test
- `Source/build.sh 1.7 --tests` — builds the 1.7 DLLs with the in-game test harness (prints only errors).
- `Source/build.sh all` — release build for 1.7, 1.6, 1.5.
- `tools/run_test.sh <suite,suite|all> <label>` — builds with tests, starts RimWorld 1.7 `-quicktest` in an isolated profile
  (3 parallel slots, waits if busy), runs the in-game suites, quits, prints results. Exit 0 = pass. Takes 1–4 minutes.
  `MODDIR=<worktree root>` to test a git worktree. Extra mods to load: pass their packageIds after the label.
- `tools/run_saveload.sh <label> [roundtrip removal addition modules|all]` — save/load scenarios: one game saves (suite `saveload.build`),
  the next loads that save (vanilla dev-mode `autostart`), with or without Hearth & Harbor. A test-only probe mod (`tools/probe`) records every
  red error and drives the game when H&H is absent. `saveload.roundtrip` compares a reflection fingerprint of all our state at save time and
  right after the load: a field that differs is either a save bug or must be listed as a cache in `KnownTransient` (with the reason).
- Saving rule: never let our JobDef/DutyDef/LordJob/LetterDef sit unguarded in vanilla save data; `RemovalSafeSave` (Hearth.Core) writes
  vanilla stand-ins plus `hh…` nodes so a raw removal loads without per-tick errors. Doubles that must reload bit-exact: `ScribeMoney.LookExact`.
- Write in-game suites as `Hearth.Testing.HearthTestSuite` subclasses inside `#if HH_TESTS` (see `Source/Hearth.Core/Testing/HearthTests.cs`).
  Name them `<module>.<what>`, e.g. `bank.deposit`. A suite yields ticks to wait and calls `t.Check(cond, "what")`.
- `uv run --with pillow python3 tools/placeholder_textures.py` creates placeholder PNGs for every referenced texture path that
  has no file yet (no pink squares, no red errors). Real art replaces them later.
- **Definition of done for any change:** builds clean for 1.7, `tools/run_test.sh smoke,<your suites>` passes, no red errors
  in the game log caused by us, every new def has label/description, every new string is in the Keyed file.

## Rules (all modules)
1. **Module entry point:** one `HearthModuleBase` subclass per module (found by reflection). Register settings in `RegisterSettings`,
   activities/tabs/caretaker checks in `OnDefsLoaded`.
2. **Every Harmony patch** is a `[HarmonyPatch]` class (applied one by one; a failing class is skipped and logged). Its prefix/postfix
   must first check `Modules.On(ModuleId.X)` and return immediately when off. Never patch hot paths (pathfinding per cell,
   `ForbidUtility.IsForbidden`, `Thing.Tick`) without a very cheap early-out. Prefer postfixes. No transpilers unless unavoidable.
3. **Errors:** wrap ticks and UI of your module in `Guard.Run(ModuleId.X, "Site", …)`. Log with `HLog.Info/Warn/Error("<Module>", …)`,
   `HLog.Once` for fallbacks. Never swallow exceptions silently.
4. **Settings:** every number a player might want to tune is an `HSettings.*` registration (key `<module>.<name>`), with an English
   label and a one-sentence description. Difficulty-dependent values pass the 7 tier anchors from KONZEPT 9.2.
   Read with `HSettings.Get/GetBool/GetInt/GetSilver`. Values are per save automatically.
5. **Money:** only `Money` (micro-silver). Charges go through `Payments.TryCharge` / `GuestApi.ChargeGuest`, bookings through
   `Ledger.Current.Post(from, to, amount, memo)`. Never spawn or destroy silver to "pay" outside these.
6. **Random:** `JRand.Stream("module.purpose")` for gameplay, `JRand.Sealed(…)` for player bets/bank/lottery.
7. **Saving:** module state lives in the module's own `GameComponent`/`WorldComponent`/`MapComponent` or ThingComp. Never add fields
   to vanilla classes, never subclass `Pawn`. Removing the mod (or a module) must leave a loadable save (KONZEPT 3.7).
8. **Nothing forced (N08):** without guest beds, venue signs or a bank account, a module must have no visible effect.
9. **Defs:** prefix `HH_` (`SL_` for the bank), designators in our own categories (`HH_Hospitality`, `HH_Bank`), research in tabs
   `HH_Hospitality` / `HH_Banking`. DLC content uses `MayRequire="Ludeon.RimWorld.Royalty"` etc.
10. **Translations:** `tools/i18n/extract.py` → `delta.py plan <old src>` → translate `delta/<code>/*.json` into `delta_out/` → `delta.py apply`
    (writes only changed files, then runs `split_optional.py`). A def with `MayRequire`/`MayRequireAnyOf` keeps its translations in
    `Compat/<slug>`, never in `Common/Languages`: there the game reports "Found no <Def>" for every player without that DLC or mod and logs
    "Translation data for language X has N errors". `smoke` fails on any translation load error from our files; check a language with
    `GAME_LANG="German (Deutsch)" tools/run_test.sh smoke <label>` (and `tools/run_test_nodlc.sh` for a game without DLC).
    A file in `Compat/<slug>` must not have the name of a file in `Common` or in another Compat folder (same path below the language
    folder): the game reads only one of them and skips the rest silently, which turned 270 translated texts per language English in
    1.1.1. `split_optional.py` names them `<file>__<slug>.xml` and stops on a clash; `smoke` checks it in the game.
    `<slug>` is short (`Anomaly`, `VCookEBakery`; id sets with several ids get a name in `SET_NAMES`, e.g. `Fishing`); `LoadFolders.xml`
    keeps the real package ids in `IfModActive`. **Path length:** no packaged file may have a relative path longer than 140 characters
    (Windows MAX_PATH 260 with the Steam folder in front; 1.2.0 could not be downloaded on Windows). `split_optional.py` and
    `tools/package.py` stop on a longer one; keep new folder, def type and file names short.
    `extract.py` collects only what its three tables name: `FIELDS` (vanilla text fields of any def), `TYPE_FIELDS` (text fields of our
    own def types, e.g. `coversText`) and `NESTED` (texts inside lists, injected by position: `<defName>.steps.0.label`,
    `<defName>.modExtensions.1.requirements.0.hint`, `<defName>.fields.0.options.2.label`). A new text field or a new list with texts
    in a def class needs a line there, otherwise it stays English in every language without any error. `delta.py` reads the same tables.
    A list that also gets items from a parent def of ours cannot be addressed by position (`extract.py` stops); the comps of a ThingDef
    are addressed by handle (`comps.CompUsable.useLabel`) because vanilla parents bring comps of their own.
11. **Texts:** English is the main language. No sexual content (no sex work, no erotic items). No real companies, persons or
    organisations in game texts. Gambling screens show the help hint when `core.helpHint` is on (KONZEPT K28).
12. **Other mods:** only soft (reflection / `MayRequire` / `ModsConfig.IsActive`), with a log line when the integration is active.
13. **Performance:** no LINQ in per-tick code paths, cache per map with a dirty flag or a 250-tick refresh, rare ticks (`TickRare`/`% 250`).

## Contracts between modules (do not change without telling the integrator)
- `Hearth.Core/Contracts/Payments.cs` — `Payments.TryCharge`, providers, tills (`Payments.ColonyTill(map)`), `Tills.BankSink`.
- `Hearth/Contracts/GuestApi.cs` — `GuestApi.IsGuest/Info/GuestsOn/AddSatisfaction/ChargeGuest`, `IGuestActivity` + `GuestActivities.Register`.
  Without the Inn module, vanilla visitors act as guests (so every venue can be tested with the `VisitorGroup` incident).
- `Hearth/Contracts/Venues.cs` — `VenueTypeDef`, `CompVenueSign`, `Venues.All/At/OfType`. A room with a Venue Sign is a business.
- `Hearth/Contracts/HearthTabs.cs` — pages of the main tab.
- `Silverline/Contracts/BankApi.cs` — `BankApi.Impl`, bank window tabs.
- Shared work types `HH_Hosting`, `HH_Serving` and research tab `HH_Hospitality` (Common/Defs/Hospitality); research tab `HH_Banking` (Common/Defs/Core).
- `Hearth.Core/Foundation/ModuleGate.cs` — per-save module hiding: def → module by folder (`Common/Defs/<Folder>`, table `FolderModules`)
  or `<li Class="Hearth.ModuleGateExtension"><module>X</module></li>`. A module switched off hides its buildings (architect + placement;
  placed ones stay and say so), research (tab; progress kept), recipes/bills, work givers (work types stay), joy givers and incidents.
  A new def folder of a switchable module must be added to `FolderModules`. `ModuleGate.Changed` fires after every switch.
  Details: a research prerequisite from an off module counts as done (only that one link, not its own prerequisites). Blueprints and frames of an
  off module are NOT built or supplied (the construction work givers return no job; they stay and resume when the module is on again).
  The Venues folder belongs to Shops & Food except the signs (extension, Core); no other module uses counters, stalls or vending machines
  (checked), so if one starts to, give the building a `ModuleGateExtension` for Core. Right-click filtering skips itself when the game version's
  `FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption` is missing or has another signature (logged). Module names: keys `HH.Module.<ModuleId>`.
- Also: `Payments.TryPayout` / `GuestApi.CreditGuest` (payouts, refunds), optional `IModuleScoped` on providers, `Ledger.Post(..., category)` + `Ledger.DayIncome`,
  `ScribeMoney.Look`, `HearthGame.OnGameInit` (clear static caches here), `BankTab.Module`.

## Using a single module elsewhere (N07)
Each module is a folder `Source/Hearth/<Module>` + `Common/Defs/<Module>` + `Keyed/HH_<Module>.xml` + textures under `…/HH/<Module>`.
To reuse one: copy those plus `Source/Hearth.Core` (and the contracts it uses) into your mod, keep the namespace, and either keep
`HearthModuleBase` (its settings then appear automatically) or call your module's `RegisterSettings/OnDefsLoaded` yourself.
Modules only talk through the contracts above, so nothing else is needed.
