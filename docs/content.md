# Content of 1.1.0: music, scenarios, storyteller, creator pawn, icons

Everything here belongs to the Core module (always on). Code: `Source/Hearth/Story/`, `Source/Hearth/Music/`, `Source/Hearth.Core/Scenario/`,
`Source/Silverline/Bank/ScenPart_SLStartingAccount.cs`. In-game tests: `content.*` (`tools/run_test.sh smoke,content.* <label>`).

## Music (F07/F32)
- Three calm instrumental songs, one per season: `HH_Song_HarborMorning` (spring/summer, day), `HH_Song_LanternsAtTheInn` (autumn),
  `HH_Song_SnowOnTheSign` (winter). Plain `SongDef`s in `Common/Defs/Music`, clips in `Common/Sounds/HH/Music`.
- They join the normal playlist on their own (vanilla, P-Music, Music Expanded's generated vanilla theme). No Harmony patch.
- `HearthMusic.Refresh()` sets `playOnMap`/`commonality` every 250 ticks from two global settings: `core.music` (Off / When hosting /
  Always, default When hosting = only colonies that use the mod, N08) and `core.musicShare` (weight, default 1; base weight 1.5 per song).
  Music Expanded keeps copies of every SongDef: the same values are written to those copies by reflection when it is loaded
  (`HearthMusic.SyncCopies`; layout checked against Music Expanded's source, original and "Continued": `public static List<TrackDef>
  TrackManager.tracks`, `TrackDef : SongDef`, copies keep `clipPath`; tested with stand-in copies, not yet with the real mod installed).
- Memory: RimWorld decodes mod audio on load, so each song takes about 21 MB (44.1 kHz stereo, 125 s), about 63 MB for all three.
- Volumes are matched to the vanilla relax songs (about −18.5 LUFS after the volume factor).

## Scenario parts (F08)
| Part | Def | What it does |
|---|---|---|
| `Hearth.ScenPart_HHRules` | `HH_ScenRules` (max 1) | modules on/off, hosting difficulty, content intensity, any per-colony settings; "More settings…" opens the full settings window against the part |
| `Hearth.ScenPart_HHSetting` | `HH_ScenSetting` | one setting = one value |
| `Hearth.ScenPart_HHStartingResearch` | `HH_ScenStartingResearch` | finishes one of our research projects at the start |
| `Silverline.ScenPart_SLStartingAccount` | `SL_ScenStartingAccount` (max 1) | account at a bank, opening balance, optional starting loan |

```xml
<li Class="Hearth.ScenPart_HHRules">
  <def>HH_ScenRules</def>
  <tier>2</tier>                       <!-- 0 Comped … 6; leave out = template -->
  <content>Cozy</content>              <!-- Cozy, Standard, Dark, Grim; leave out = template -->
  <modulesOn><li>Inn</li></modulesOn>
  <modulesOff><li>Underworld</li></modulesOff>
  <!-- <underworldDiscovered> of 1.1 scenarios is still read and ignored: since 1.2.0 the underworld is open whenever it is visible -->
  <settings><li><key>inn.arrivalRate</key><value>1.4</value></li></settings>
</li>
```
- Order: the storyteller page's scratch profile and `HearthGame.StartedNewGame` are both "template + scenario" (`ScenarioSeed.Apply`), so the
  player sees the scenario's values and can still change them. World effects (`IHearthScenarioStart`) run once after the whole new-game setup.
- Global preferences and unknown keys are never set by a scenario.
- "More settings…" shows default values for rows the scenario does not set (marked amber when it does); Reset on a row or a module hands it
  back to the player's template. All editor rows draw inside `ScenarioSeed.GuardedEdit` (Guard, Core), so they never break vanilla's editor.
- Removal safety: our parts are written in an `hhParts` node next to the vanilla part list (save games and `.rsc` files), never inside it.
- Shipped scenarios: `HH_Scen_HarborTown` (bright tourism start) and `HH_Scen_BackRoom` (dark underworld start), generated for every game
  version by `tools/scenarios/gen.py` (1.6/1.7 inherit `ScenarioBase`, 1.5 has no such parent). Edit the generator, not the XML.

## Storyteller Hetty Hearthwell (F09)
- `HH_Hetty` inherits vanilla's `BaseStoryteller` comps and replaces threats and misc events: `StorytellerComp_HH_GentleCycle`
  (raids every ~20 days from day 20, never mech clusters or ship parts, infestations/manhunters rarely) and `StorytellerComp_HH_KindMisc`
  (misc events every ~3 days, good news ×3, bad luck ×0.2, the cruellest never). Raid points 55 % → 95 % of the usual.
  People can fall ill, animals never (no DiseaseAnimal comp, on purpose). Anomaly threats come through vanilla's anomaly chance untouched.
- `Hearth.StorytellerHints` (DefModExtension, usable by any storyteller): `guestArrivalFactor` (Hetty 1.25, read by the guest scheduler) and
  `suggestedTier` (Hetty: Bed & Breakfast, shown as a one-click suggestion on the storyteller page).
- Saves carry her as Phoebe plus `hhStoryteller`: without the mod the colony simply continues with Phoebe.
- Portraits: `UI/HH/Storyteller/HettyLarge` (1160×1240) and `HettyTiny` (244×260), `tools/art/hhart/story.py`.

## Creator pawn (F18)
- One solid bio, "Julius Berger" (male, childhood *harbour tinkerer* → adulthood *wandering innkeeper*, kind and industrious), added to
  `SolidBioDatabase` after vanilla loads its bios. Vanilla does the rest: like the creator pawns of the base game it may turn up for a
  generated colonist, wanderer, trader or world pawn, never as a faction leader (vanilla's `IsBioUseable` refuses non-`pirateKing` bios for leader kinds; `content.creator` checks this for every leader kind and generates 50 leaders). Hidden nerd setting `core.creatorPawn` (default on).

## Icons (F34)
- Comms console entries carry their own square icon (`ConsoleComm.Icon`): the colony's bank logo (Silverline mark before an account),
  Clawback Mutual, the finance/lottery mark, The Lucky Thrumbo mark.
- Settings window: mod mark in the sidebar, page and module icons (`HHIcons`, literal paths for `tools/art/needs.py`), module icons on the
  modules page and in Silverline standalone's simple window. Mod list icons: `About/ModIcon.png` and `About/Silverline_ModIcon.png`
  (`tools/art/modicons.py`; `tools/package.py` copies the Silverline one).
