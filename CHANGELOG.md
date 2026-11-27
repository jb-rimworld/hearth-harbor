# Changelog

Hearth & Harbor and Silverline (the standalone bank) are built from this repository and share one version number.

## 1.2.1 (2026-11-27)
- Fixes the download of 1.2.0 on Windows. Some translation files had paths longer than Windows allows (the folder and file names carried the full ids of the DLCs and mods they belong to), so Steam stopped the download with "File commit failed: staging file is missing" and left a broken mod folder without `About/About.xml`; RimSort and the game then reported the mod as invalid. The folders in `Compat/` now have short names (`Compat/Anomaly`, `Compat/Fishing`, `Compat/VCookEBakery` …) and so do the files in them (`HH_Kitchen__Anomaly.xml`). Nothing else changes: the same translations load with the same DLCs and mods. Silverline standalone gets the same short names.
- For contributors: `tools/package.py` stops a release when any file in either package has a relative path longer than 140 characters, and `tools/i18n/split_optional.py` refuses to write one and renames the old long folders and files itself.

## 1.2.0 (2026-11-26)
The Underworld is reworked so that it just works.
- Open from the start. There is no stranger to wait for, no "Show me", no warning dialog and no research for the basics: back room doors, the fight pit marker, the fence counter, the smuggler's dock and the drug lounge can be built at once. The Hunt and the black clinic keep their research and follow the research setting like every other project. The module switch is the one on/off; Cozy content still hides everything. Nothing happens until you build something.
- Dark and Grim content take effect as soon as you pick them. The tooltips of the two chips say what each level adds.
- The fight pit is reworked:
  - Bouts with a guest or a colonist are fought for points. Hits are counted, not felt: nobody gets injured or downed, nothing drops, and nobody holds a grudge or loses goodwill. The loser walks away with a bruise or two. A usual bout lasts half a minute.
  - Guests who like a brawl challenge your fighters: shady guests and gang members, and other guests with Bloodlust, Brawler or good melee who are not kind. A winning guest gets a small purse out of the house take.
  - Colonists and slaves fight when you tick them in the new "Fighters…" window or right-click the marker with them ("Fight in the pit"). No random roll, no trait needed.
  - Pits from old saves keep an off switch for the former volunteer rule ("Volunteers (old saves)" on the marker).
  - Alone? "Call an opponent" hires a pit beast no stronger than your fighter. The first one is free, later ones cost 40 silver. The colony pays: the till first, then silver in a stockpile or the home area, then a Silverline account. With a single colonist the button picks him; you do not need to tick him first. The beast leaves after the bout.
  - "Start the bout now" is greyed out with the reason when only one fighter is ready, and the Fighters window has its own "Call an opponent" button. The status line only suggests what can help right now.
  - After a bout the two fighters do not start a brawl with each other or anyone else for an in-game hour.
  - "Start a bout" works without guests. Prisoners are brought into the arena and back, and do not run off on the way.
  - The marker always says what it waits for, who is ready and how the last bout ended. Every bout ends with a message.
- Every underworld building says why it is waiting: the back room door, the fence counter, the dock and the hunting ground marker.
- Smuggler crates and protection money are paid by the colony the same way as a pit beast (till, stock silver, bank), and every greyed-out button says how much is missing.
- Pawns under 18 never take part in the Underworld: no pit, bets, drug lounge, prisoner hunts or sales to gangs (before, 13-year-olds counted as adults). Gangs and hunting parties are generated as adults.
- Smuggler boats and protection demands arrive as letters. You answer with the buttons on the dock or on the gang leader, or on the Underworld tab. An unanswered boat leaves; an unanswered demand counts as a refusal. No more pop-up dialogs.
- The first gang comes about a day after your first back room, fence counter or fight pit. A gang visit that cannot happen is tried again a few hours later, and the Underworld tab says why. Gang members spend the silver they carry.
- The fence reports its deals, says when the till is empty and has "Take goods out". A deconstructed fence counter drops its goods.
- The Underworld tab shows every part of the back room with a coloured dot (not built, waiting, working), what it is doing, and a Build button that picks the building for you. Open offers wait there with Accept and Decline.
- New guide topic "The back room": fence and pit marker, the first bout, a back room, the first gang, a fence counter.
- The building descriptions say how to use each thing.
- Old saves: the discovery and its settings are gone; values stored for them are ignored. A save with Dark or Grim content but no confirmed warning now plays at that level. Research of the old "the back room" project stays in the save but is hidden and unlocks nothing.
- All new and changed texts are translated into the ten languages, including the old texts that still described the former volunteer rule.
- Removed: the shady stranger, the "Discover / On / Off" choice, the "Underworld already discovered" scenario rule (old scenarios still load), the content, hunt and clinic warnings, and the research "the back room".

## 1.1.2 (2026-11-25)
- Fixes a translation fault of 1.1.1: with a DLC active, texts that had been translated since 1.0 were English again in all ten languages. Up to 270 texts per language: the names and descriptions of all base dishes, the insurance policies, the investments, the banks, the kinds of business and six thoughts. 1.1.1 moved the translations of optional content into their own load folders but kept the file names, and the game reads a language file only once per name inside a mod, so the main file of the same name was skipped without any message. The files in `Compat/` now carry the ids in their name (`HH_Insurance__Ludeon.RimWorld.Anomaly.xml`). Silverline standalone had the same fault and gets the same fix.
- The remaining English texts in the ten translations are translated: 438 per language (German, French, Spanish, Brazilian Portuguese, Russian, Ukrainian, Czech, Simplified Chinese, Japanese, Korean). The translation tool had never collected them, so they stayed English without any error:
  - the setup tab of a venue sign: what each kind of business needs and why (108 texts);
  - the 14 traits (label and description);
  - Clawback Mutual: the riders of the policies, what each policy covers and what each tier pays, the small print of the 15 clauses, and the three claim forms with their questions and choices;
  - Silverline: the perks of the six banks and the taglines of the 17 investments;
  - the origin line of the dishes in the cookbook, the condition lines of the Ideology precepts (indulgences, paid ascension), the two creator backstory titles, the movie night messages, the recovery message of the gambling spree, "Study recipe book", and the work names used in "prioritize" menus.
- For contributors: `tools/i18n/extract.py` now has three tables that say what is collected: `FIELDS` (vanilla text fields), `TYPE_FIELDS` (text fields of our own def types) and `NESTED` (texts inside lists of a def, addressed by position such as `HH_Venue_Tavern.modExtensions.1.requirements.0.label`, or by handle for the comps of a ThingDef). It stops when a list cannot be addressed by position (items inherited from a parent def of ours, or an item with `MayRequire`). `tools/i18n/delta.py` reads the same tables instead of keeping its own copy, and now recognises defs of our own types (`Silverline.SL_PolicyDef/…`): the def type was cut at its first dot, so stale keys of those types were never removed. `tools/i18n/split_optional.py` gives every file in `Compat/` a name no other load folder uses, renames the ones written by 1.1.1 and stops when two load folders hold a language file with the same path; `smoke` checks the same in the game for every language and shows the label of a venue type and a policy.

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
