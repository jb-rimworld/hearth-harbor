# Removing Silverline (bank, insurance, investments, lottery)

What "Prepare removal" (module switch-off in the settings, or the button on the Diagnosis page) does, and what is left behind.
Scope: everything under `Source/Silverline`. The Hearth modules have their own notes.

## Cleaned by PrepareRemoval

| Thing | Where it lives | What happens |
|---|---|---|
| Collection agency faction `SL_CollectionsAgency` | `FactionManager` | Created as a **temporary** faction, so vanilla deletes it by itself once none of its people is on a map (`FactionManager.Remove`, also strips the faction from pawns and relations). PrepareRemoval sets the relation to neutral and queues it for that removal. Idle saves normally carry no agency at all. |
| Agency agents (visitors, seizure team, raiders) | map pawns, world pawns | Destroyed / discarded. |
| Agency lords (`LordJob_VisitColony`, `LordJob_AssaultColony`, `LordJob_ExitMapBest`) | `Map.lordManager` | Removed. These are vanilla lord job classes, so a save is loadable even if one were left; the lord only points at the agency faction. |
| `BankGameComp.collectors` (saved pawn references) | game component | Cleared. |
| Claim decision letters (`ClaimLetter`, letter defs `SL_Clawback*`) | `Find.LetterStack`, `Find.Archive` | Removed. `ClaimLetter` is our own `ChoiceLetter` class; a save without the mod cannot read it. |
| Open claims, policies | `InsuranceComponent` | Claims are decided and paid, policies are ended (existing behaviour). |

## Not cleaned, and why that is fine

* **Hediffs:** Silverline defines none. (The `HH_Soothed` and `HH_WellFed` hediffs belong to the Wellness and Kitchen modules.)
* **Thoughts:** `SL_Finances`, `SL_FinancesGreedy`, `SL_CollectorsNearby` are situational thoughts (computed by a `ThoughtWorker`, never stored in a pawn's memory), so nothing is saved.
* **Ledger accounts** (`bank:*`, insurer, lottery pots, market accounts): stay in the shared `Ledger` of Hearth.Core. Silver on the account is lost unless withdrawn first; PrepareRemoval says so in its report.
* **Buildings** (counter, ATM, night safe, insurance desk): stay on the map. Without the mod RimWorld drops buildings of unknown defs on load with a warning. Deconstruct them first if the log should stay clean.
* **The game components themselves** (`BankGameComp`, `InsuranceComponent`, `FinanceGame`, `LotteryGame`): an unknown `GameComponent` class is skipped by vanilla on load with a warning, the data in it is ignored.
* **Letters sent by Collections with vanilla defs** (reminders, seizure notice): plain `StandardLetter`s of vanilla `LetterDef`s, harmless.

## If the mod is removed without PrepareRemoval

Not tested in game (we cannot load a save without the mod in the test harness); what the vanilla code does, read from the 1.7 assembly:

* `FactionManager.ExposeData` removes factions whose `def` is null and logs "Some factions were null after loading." (one red line). So a leftover agency does not break the world load.
* A `ClaimLetter` with an unknown letter class / def is expected to produce load errors per letter; PrepareRemoval avoids that.
* Everything else behaves as in the previous section.
