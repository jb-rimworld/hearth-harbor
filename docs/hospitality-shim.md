# Hospitality compatibility: takeover, save import and the read-only shim

KONZEPT 3.8, 12.6 (N61), 12.8 (S7), 15.7 (D12). Code: `Source/Hearth/Compat/Hospitality/` (in `Hearth.dll`, part of the core,
always on). Texts: `Common/Languages/English/Keyed/HH_Compat.xml`. Only *names* of Hospitality and its add-ons are used
(package ids, class names, save keys, def names); no GPL code was copied.

## 1. What is built and tested

| Part | Where | State |
|---|---|---|
| Family detection (package ids, roles Replaced / Companion / MentionOnly, Steam `_steam` postfix) | `HospitalityFamily.cs` | done, tested (`hospcompat.detect`) |
| Takeover when Hospitality runs alongside us | `HospitalityTakeover.cs` | done, tested with Hospitality Continued 3509486825 (`hospcompat.takeover`) |
| Main-menu dialog Yes / Later / Don't ask again, manual 3-step fallback | `Dialog_HospitalityTakeover.cs` | done; Yes-path (`TryDisable`) tested without restart; dialog drawn once in the test |
| Save import (BackCompatibilityConverter + stand-ins + post-load) | `HospitalityImporter.cs`, `HospitalityImportStubs.cs`, `HospitalityDefTables.cs` | done, tested with a synthetic and a real Hospitality save (`hospcompat.import`) |
| Import record + public API for the Inn module | `ImportedGuestBeds.cs` (`ImportedGuestBeds` GameComponent, `HospitalityImport` static API) | done |
| Read-only shim assembly (S7) | this document, section 4 | **design only** (needs a project + LoadFolders change outside this module) |

### Takeover (N61), run once after every other mod's `StaticConstructorOnStartup` (`LongEventHandler.ExecuteWhenFinished`)
1. **Harmony:** every patch (all patch lists of `Harmony.GetPatchInfo`, read as fields *and* properties so finalizers /
   IL manipulators are included) whose patch method's assembly belongs to a *replaced* family mod is removed with
   `Harmony.Unpatch(original, patchMethod)`. Assemblies are matched by object and by full name (on 1.7 CoreCLR the objects
   can differ). Measured: 76 of 76 Hospitality Continued patches removed. Swept again at every game start/load.
2. **Incidents:** family IncidentDefs → `baseChance = 0` (and `baseChanceWithRoyalty`). Hospitality *replaces* vanilla's
   `VisitorGroup` def (same defName, own worker, chance 0); that one is restored to Core's values (read from
   `Core/Defs/Storyteller/Incidents_Map_Special.xml`, fallback 1.7 values) because without the Inn module vanilla visitors
   are our guests.
3. **Architect:** family Thing/Terrain defs lose their `designationCategory`; categories are re-resolved.
4. **Main tab:** family `MainButtonDef`s hidden. Hospitality re-shows its tab from `Hospitality_MapComponent.FinalizeInit`
   (`Mod_Hospitality.ToggleTabIfNeeded`) → that method is skipped while taken over, and the tab is re-hidden after game start.
5. **Injected content:** inspector tabs (`ITab_Pawn_Guest`) and comps (`CompGuest`) the family injected into *other* mods'
   defs are removed; family work givers removed from work types; family joy givers at chance 0.
6. **Components and alerts:** tick/update/OnGUI of family Map/Game/World components and `GetReport` of family alerts get a
   prefix that skips them while the takeover is active.
7. Each step is guarded on its own and logged (channel `Compat`; summary line "Takeover done: …"; diagnosis lines via the module).

The dialog is queued for the main menu (`ProgramState.Entry`) unless "Don't ask again" (`compat.hospitalityNeverAsk`,
global) is set. Yes → `ModsConfig.SetActive(id,false)` for every Replaced + Companion mod, `ModsConfig.Save()`, verification
(in memory and in `ModsConfig.xml`), then `GenCommandLine.Restart()`. If the mod list came from the command line or the
verification fails, the window shows the three manual steps and a button to open the mod list. MentionOnly mods (Hotels and
Recreation, Tourism, RimSim Hospitality) are listed but never touched (12.6, last paragraph).

### Save import (D12, "alles, was geht")
`HospitalityBackCompatibilityConverter` is appended to `BackCompatibility.conversionChain` (private list, reflection; 1.7, 1.6,
1.5 compile). Vanilla only consults it when the save's mod list differs from the running one, and it only acts on family
names whose class/def is **missing** in this session, so normal saves and the takeover case are untouched.

| Hospitality data | Handling |
|---|---|
| `Hospitality.Building_GuestBed` + defs `<Bed>Guest` | class → `Building_Bed`, def → base bed (`DoubleBedGuest` → `DoubleBed`, also `Blueprint_`/`Frame_`); thing id number and `RentalFee` read from the XML node; after load stored in `ImportedGuestBeds` and handed to the Inn module (event + optional comp by name). Non-colonist owners are unassigned. |
| `Hospitality_MapComponent` | stand-in `HospitalityMapStub` reads `guestsAreWelcome`, `refuseGuestsUntilWeHaveBeds`, `guestsCanTakeFoodForFree`, `defaultEntertain`, `defaultMakeFriends`, `defaultAreaRestriction`, `defaultAreaShopping` → per-map settings record + area records; then removed from the map. Base class `CustomMapComponent` (1.6+), so vanilla never auto-creates it in normal games. |
| other family map components (Storefront, Gastronomy …) | `HospitalityDroppedMapComponent`, counted as dropped, removed |
| `Hospitality_GameComponent` (guest food policy) | our `ImportedGuestBeds` instance stands in (detects the foreign `Class` attribute), reads nothing, removed after load — a separate stub GameComponent would be auto-created and saved in *every* game |
| `LordJob_VisitColony` + toil/trigger data | `HospitalityLordJobStub` reports `LostImportantReferenceDuringLoading`, so vanilla removes the lord cleanly at PostLoadInit; toil/trigger data stubs are discarded. Afterwards the former guests get a vanilla `LordJob_ExitMapBest` (walk) per map and faction: "neutral journey home". |
| CompGuest keys on pawns (`arrived`, `chat`, `recruit`, `guestArea`, `shoppingArea`, `bed`, `lord`) | peeked from every `Pawn` node while loading; non-player humanlikes with guest data become **known visitors** (id, name, faction, entertain / make-friends flags); their guest/shopping areas are recorded too |
| guest areas (vanilla `Area_Allowed`) | kept as they are; recorded; if `Hearth.Guests.Area_HH_GuestWing` exists (Inn module), a guest wing with the same cells is created by reflection (ctor `(AreaManager,string)` or `(AreaManager)`) |
| Hospitality jobs / job drivers | JobDef → `Wait`, driver → `HospitalityJobDriverStub` that ends at once; counted as dropped |
| duties, interaction log entries, queued incidents, letters | `Relax`/`CasinoVisitor` → `ExitMapBest`; interactions → `Chitchat`; incidents → `VisitorGroup`; `HappyGuestJoins` letter → `PositiveEvent` |
| work types | `Diplomat` → `HH_Hosting`, `Gastronomy_Waiting`/`Storefront_Selling` → `HH_Serving` (only where saved by name, see limits) |
| thoughts, history events, room-service hediff, trader kind `Guest`, joy kinds, work givers | registered as removed defs with vanilla → dropped silently, counted |

After loading, one letter lists what was imported and dropped (setting `compat.importLetter`), e.g. *"Imported: 2 guest
beds, 1 guest area, 1 known visitor, 1 map's guest settings, 1 running visit sent home in peace. Dropped: 1 detail, 1 add-on
record."* The same line is kept in `ImportedGuestBeds.History`.

**Takeover case:** while Hospitality is still installed, its live `Building_GuestBed`s are noted in `ImportedGuestBeds` on
every load (`RegisterLiveGuestBeds`), so the information survives when Hospitality is disabled later.

### API for the Inn module (contract request, see report)
```csharp
Hearth.Compat.Hospitality.HospitalityImport.GuestBedImported += (Building_Bed bed, int feeSilver) => …;  // subscribe in OnDefsLoaded
HospitalityImport.IsImportedGuestBed(thing); HospitalityImport.RentalFee(thing);
HospitalityImport.PendingBeds;      // thing id numbers not yet adopted
HospitalityImport.MarkAdopted(bed); // after turning it into an Inn guest bed
HospitalityImport.Areas; HospitalityImport.KnownVisitors; HospitalityImport.SettingsFor(map);
```
If a comp of type `Hearth.Guests.CompGuestBed` is on the bed, the importer also tries `SetGuestBed(bool)` or a bool
`IsGuestBed/Enabled/Active` member and a `Fee/RentalFee/Price` member (int, float or `Money`) by reflection.

## 2. Tests

| Suite | Mods | What it checks |
|---|---|---|
| `hospcompat.detect` | ours only | no takeover, no dialog, vanilla `VisitorGroup` untouched, importer in the chain, removed-def registration, no stand-in components in a normal game, family lookup rules |
| `hospcompat.convert` | ours only | converter rules on synthetic XML nodes (25 checks) |
| `hospcompat.import` | ours only | builds a save that looks like a Hospitality save (Hospitality in the mod list, guest beds with fee, map + add-on map component, game component, visit lord with Hospitality job/toil/trigger classes, CompGuest keys, a Hospitality thought), loads it in the same run, checks import, letter, lords, no red errors while loading and for 2500 ticks, re-save contains no stub classes |
| `hospcompat.import` + `HH_HSP_REAL=1` | ours only | the same against a **real** Hospitality Continued save (fixture) |
| `hospcompat.takeover` | + Hospitality Continued | all takeover steps, 0 family patches left, incidents 0, VisitorGroup vanilla, tab hidden, no guest tab/comp on humans, dialog draws, vanilla visitors arrive and are our guests, 3000 ticks without red errors, live guest bed noted, `TryDisable` writes `ModsConfig.xml` (restored afterwards), never-ask setting |
| `hospcompat.makefixture` | + Hospitality Continued, `HH_HSP_NOTAKEOVER=1` | makes the real fixture: 2 guest beds (fees 17/25 via Hospitality's `SetRentalFee`), default guest area, a real Hospitality visit; writes `<fixture>.expect` |

Commands (from the worktree root):
```sh
MODDIR=$PWD tools/run_test.sh smoke,hospcompat.detect,hospcompat.convert,hospcompat.import hospcompatA
# with Hospitality: link it into the game's Mods folder first and remove the link afterwards
G="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
ln -s ~/Library/Caches/RimlingModCompat/dl/steamapps/workshop/content/294100/3509486825 "$G/Mods/_hsp_3509486825"
MODDIR=$PWD tools/run_test.sh smoke,hospcompat.detect,hospcompat.convert,hospcompat.import,hospcompat.takeover hospcompatB orion.hospitality
HH_HSP_NOTAKEOVER=1 MODDIR=$PWD tools/run_test.sh hospcompat.makefixture hospcompatC orion.hospitality   # optional: new fixture
rm "$G/Mods/_hsp_3509486825"
# real save import (fixture in the repo: tools/fixtures/hospcompat_fixture.rws.gz)
gunzip -c tools/fixtures/hospcompat_fixture.rws.gz > /private/tmp/claude-501/hh_tests/hospcompat_fixture.rws
cp tools/fixtures/hospcompat_fixture.rws.expect /private/tmp/claude-501/hh_tests/
HH_HSP_REAL=1 MODDIR=$PWD tools/run_test.sh smoke,hospcompat.import hospcompatD
```
`HH_HSP_FIXTURE=<path>` overrides the fixture path. `HH_HSP_NOTAKEOVER` only exists in test builds (`#if HH_TESTS`).

## 3. Known limits (not done, with reasons)
- **Work priorities** `Diplomat → Hosting`: vanilla saves work priorities as a `DefMap` *by def index*, not by name, so the
  old index of `Diplomat` is unknowable after Hospitality is gone (vanilla shifts all later work types too). Needs a
  per-pawn heuristic or reading the old def order from the save's mod list; not attempted.
- **Add-on buildings** (vending machines, cash register, dining spots, slot machines, Storefront counters): their ThingDefs
  are gone, so vanilla drops those things with its usual "could not load reference" error. Mapping them to our counterparts or
  refunding material needs the Venues/Kitchen/Casino defs (not visible in this worktree). The class names are recognised
  (`UnknownClasses` → listed in the letter).
- **"Import Hospitality settings" button** (Hospitality's `ModSettings`: group size, silver multiplier …): not built; the
  values are known by name (`ModSettings_Hospitality` keys) and would map to Inn settings once those exist.
- **Guest food policy** (`Hospitality_GameComponent.defaultFoodRestriction`): dropped, the vanilla food policy database keeps
  its own copy.
- Known visitors are recorded, but no world-pawn retention (`WorldPawnGC` postfix, 15.7) — belongs to the Inn module.
- Takeover leaves *existing* Hospitality things (live guest beds) as Hospitality's classes; their draw/gizmo code runs
  unpatched (tested: no errors for 3000 ticks with visitors present).
- Tested against Hospitality Continued only; the add-ons are covered by package id, namespace and def-name tables but were
  not loaded in a test.

## 4. Read-only shim (S7, KONZEPT 12.8) — design
Goal: mods that *ask* Hospitality questions ("is this a guest?", "guest bed?", "guest area?") keep working when Hospitality
is replaced by Hearth & Harbor, without us shipping any Hospitality code.

**Why not in this module:** it must be a *separate* assembly named `Hospitality` (so `Type.GetType("Hospitality.GuestUtility,
Hospitality")`, `AccessTools.TypeByName` and compiled references resolve) and it must be loaded only when the real mod is
absent. That needs a new project and a `LoadFolders.xml` entry — both outside this module's files (contract request).

**Layout**
```
Source/HospitalityShim/HospitalityShim.csproj   AssemblyName=Hospitality, references Hearth.dll (not the other way round)
Compat/HospitalityShim/1.7/Assemblies/Hospitality.dll   (and 1.6, 1.5)
LoadFolders.xml (per version):  <li IfModNotActive="Orion.Hospitality">Compat/HospitalityShim/1.7</li>
```
**Surface** (names only, read-only, every member forwards to `GuestApi` / `HospitalityImport` / the Inn module):
- `namespace Hospitality.Utilities { public static class GuestUtility { bool IsGuest(this Pawn); bool IsArrivedGuest(this Pawn, out CompGuest); Area GetGuestArea(this Pawn); Area GetShoppingArea(this Pawn); } }`
  — the queries Hospitality add-ons and third-party mods actually call (Smarter Visitors / Visitor Spot test class names
  by suffix; Room Service, Invite to Stay and Silver Patch compile against `GuestUtility.IsGuest`).
- `namespace Hospitality { public class CompGuest : ThingComp { … } }` as an empty marker type with read-only
  properties (`arrived`, `GuestArea`, `ShoppingArea`) computed from `GuestApi.Info(pawn)`; **never added to any def** and
  never saved (no `PostExposeData`), so it cannot appear in a save.
- **No `Building_GuestBed` type**, not even an empty one for `is`-checks: vanilla would then instantiate it from old saves
  and the import above would never run. The shim must *not* define
  `Building_GuestBed`, `Hospitality_MapComponent`, `LordJob_VisitColony` or any other saved class, otherwise the save
  importer (which only acts on missing classes) is bypassed. Guest-bed questions go through
  `GuestUtility.IsGuestBed(this Building_Bed)` → `HospitalityImport.IsImportedGuestBed` / Inn guest bed registry.
- No write methods, no defs, no Harmony patches, no components (12.8).

**Add-ons that patch Hospitality internals** (Storefront, Gastronomy, Casino, Spa, Vending machines, Room Service …) are not
supported by the shim: their patches target methods that do not exist in it and would fail. Their features are built in;
the startup dialog (12.6) names them as superseded and offers to disable them.

**Tests for the shim (when built):** `hospcompat.shim` — without Hospitality, `AccessTools.TypeByName("Hospitality.Utilities.GuestUtility")`
resolves to the shim, `IsGuest` agrees with `GuestApi.IsGuest` for a vanilla visitor group, the shim assembly defines none of the
saved class names above (reflection scan), and loading the real fixture still imports 2 guest beds (proves the shim does not
shadow the importer); with Hospitality active the shim assembly is not loaded.
