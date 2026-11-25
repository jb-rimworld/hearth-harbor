# Changelog

Hearth & Harbor and Silverline (the standalone bank) are built from this repository and share one version number.

## 1.1.1 (2026-11-25)
- No more startup warnings. The game logged five lines of "probably needs a StaticConstructorOnStartup attribute" for Hearth & Harbor (three of them for Silverline standalone): the comms console marks of Silverline and The Lucky Thrumbo, the Clawback Mutual logo, the news icons of the investment tab and the sort arrows of the Guests page were loaded on first use. They are now loaded once at startup, on the main thread, like every other texture of the mod.
- The guide card at the top right stays behind dialogs. It used to be painted over every window, so on small screens (1280x720) it covered parts of settings windows, for example the search box of other mods' settings, and it still took clicks while a modal dialog was open. It now sits on the game's own UI layer: settings windows, the bank, trade dialogs and other mods' windows cover it, and a modal dialog blocks it like the rest of the screen.
- The checklist of the guide is translated. Its steps stayed English in all ten languages (only the topic titles were translated), because the translation tool never collected them. German, French, Spanish, Brazilian Portuguese, Russian, Ukrainian, Czech, Simplified Chinese, Japanese and Korean now have all 19 step texts.
- No more "Translation data for language ... has 50 errors" at startup in the ten translated languages. The translations of dishes that need one of seven cooking mods were always loaded, so the game looked for dishes that were not there. Translations of content that needs a DLC or another mod now sit in their own load folders (`Compat/`) and load only together with it; a game without DLC gets none from this mod either (the game's own German translation still reports a few of its own there). Silverline standalone: 2 such errors, from a translation the package took along by mistake.
- For contributors: `tools/i18n/extract.py` collects the `steps` of guide topics; `tools/i18n/delta.py apply` writes only the language files it changes (rewriting the others emptied the hand-made name lists); `tools/i18n/split_optional.py` keeps translations of optional content in `Compat/` and writes `LoadFolders.xml`; `ui.guide` checks that a dialog lies above the guide card; `smoke` fails on translation load errors from our files; the test probe lists them for runs without Hearth & Harbor; `tools/run_test_nodlc.sh` takes `GAME_LANG`.

## 1.1.0 (2026-11-23)
- Hearth & Harbor: animated Lucky Thrumbo, venue setup guide, couples, night bell, claim letters, AI flavour, storyteller Hetty Hearthwell, larger and resizable windows.
- Silverline: terms in the mod settings, net worth with securities, lottery ticket screen, bulk insurance, claim forms, pension rule, sounds.

## 1.0.0 (2026-11-22)
- First release.
