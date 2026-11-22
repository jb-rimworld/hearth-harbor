# UI shell (Source/Hearth/UI, namespace Hearth.Shell)

Player-facing parts of the Core module, built into Hearth.dll.

- **Main tab** `HH_MainTab` (Common/Defs/UI/MainButtons.xml): worker `MainButtonWorker_HH` hides the button until the colony uses the mod
  (`HearthUI.IsUsedInColony`: venues, bank account, `HearthUI.GuestBedProbe`, `HearthUI.UsageProbes`, guests tracked by the Inn module). The state is sticky (`ui.used`).
  Pages come from `HearthTabs.VisiblePages`; this module registers `overview` and `guests`.
- **Guest tab** `ITab_HH_Guest` is added to every humanlike race in `UiModule.OnDefsLoaded`; extra blocks via `HearthUI.AddGuestTabSection`.
- **Settings window** `HearthSettingsWindow` (installed as `HearthMod.SettingsWindowOverride`): sidebar, template/save switch, modules, difficulty, one page per module, diagnosis, about.
  Rows are drawn by `SettingRowUI` against an explicit `HearthProfile`, so the template can be edited while a game runs.
- **Difficulty** `DifficultyPanel` (settings page and `Dialog_HostingDifficulty`): tiers, slider 0-500 %, calculator `TierTable` (reads registered settings, falls back to the 9.2 reference anchors),
  consequence ladder, special switches (`core.*` bools). The storyteller page gets one line (`HostingLine`); its choice is held in `UiState.PendingProfile` and applied to the new colony in `OnGameStarted`.
- **First-start card** `Dialog_FirstStartCard` + `FirstStartCardTrigger` (every 30 ticks from `UiModule.GameTick`).
- **Guide** `GuideTopicDef` (XML `<Hearth.GuideTopicDef>`), `GuideChecks` (named conditions, `GuideChecks.Register`), `GuideState`, `GuidePanel` (drawn after vanilla's learning helper via a postfix on `Tutor.TutorOnGUI`).
- **State** lives in the profile settings store under `ui.*` keys (`UiState`), so it is saved with the colony and is simply ignored without the mod.
- **Texts** `Common/Languages/English/Keyed/HH_UI.xml` is generated: `python3 tools/gen_ui_keyed.py Source/Hearth/UI Common/Languages/English/Keyed/HH_UI.xml` (also checks every static key used in code).
- **Tests** `ui.windows ui.firstcard ui.guide ui.tab ui.calc ui.settings ui.diag` (Source/Hearth/UI/UiTests.cs).
