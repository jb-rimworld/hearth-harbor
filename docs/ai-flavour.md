# AI flavour module (1.1.0, F05)

Optional module `AiFlavour` (default off, per save). It only writes **text**. No mechanic, number or outcome ever depends on it, and every text has an offline version
that is used whenever the AI is off, unavailable, resting, the player has not opted in, or a request fails.

## Opt-in
- `Dialog_AiConsent` (HUI-sized window) states what is sent, what is not, where it goes and what is stored; the player agrees or says "Not now".
- Consent is stored **per installation** in the mod template (`HearthTemplate.AiConsentVersion/AiConsentDate`, text version `AiConsent.CurrentVersion`). A new text version asks again.
- Without consent nothing is sent, not even a status check (`AiService.Enabled = module on AND consent`).
- The dialog opens once per session when the module is switched on without consent (`ModuleGate.Changed`, never in automated tests). The settings page of the module
  (`AiSettingsPanel`) and the Ask Ottoline tab offer it again; "Forget my consent" withdraws it and switches the module off.

## Relay
`https://hh-relay.jberger.de`, API in `22_RimWorld/hearth-relay/README.md`. A developer can point the game at another relay with a one-line file `Config/HearthHarbor_AI.relay`
(`https://...` or `http://localhost...`); without that file the default is used.
- `GET /v1/status` after consent, at game start and every 10 minutes while something cares (UI open or a request made). `enabled:false` (e.g. `no_key`) means: no AI, offline texts.
- `POST /v1/ai/<function>` with header `X-HH-Client: <HearthMod.Version>`; functions `ottoline`, `headline`, `claim-letter`, `review`, `concierge`.
- Errors: `budget` = "AI is resting" for 10 minutes (not a failure), `busy` 30 s, `disabled` re-checks the status; everything else counts as a failure, five in a row pause the AI for ten minutes.
- Max 2 requests in flight, 8 queued, timeout 35 s (reviews 25 s). All asynchronous: `UnityWebRequest`, polled from a postfix on `Game.UpdatePlay` (every frame, also while paused); callbacks run on the main thread.
- Every payload is repaired to the relay's limits first (`AiSchema`, pure and unit tested), key-like words and user paths are replaced by `[removed]`, no colony or save name is sent.

## Features (all with offline fallback)
| Feature | Where | With AI | Without AI |
|---|---|---|---|
| Ask Ottoline | bank window, tab "Ask Ottoline" (BankTabs, module AiFlavour) | answer from the relay using a read-only snapshot of the books | five quick questions from the books, typed questions matched by keyword |
| Market headlines | Invest tab news ticker | new `NewsItem.text` replaced (batch of 5, or after 60 s) | stock line stays |
| Guest reviews | letter after a group's departure report (Harmony postfix on `DepartureReport.Send`) | review in the guest's voice | stock line of the same mood |
| Claim letters | `AiApi.TryClaimLetter` (insurance package calls it) | letter text reacts to the claim form's free text | caller keeps its offline text |
| Concierge | Overview page, box "Ask the concierge" | tip phrased from the game's own facts | the game's own list of problems |

Saves: nothing new is saved by the module (the module has no saved component). Replaced headlines are plain strings inside the existing news items, reviews are vanilla letters.

## Tests
- `Source/Tests/AiTests.cs` (xunit, `Source/Tests/run.sh`): JSON, schema repair, status/reply parsing, back-off, text cleaning.
- In game (fake transport, no network): `ai.service`, `ai.features`, `ai.ui`. `ai.live` (explicit only) checks the real relay's `/v1/status` over https and never sends a question.
- Texts: `python3 tools/gen_ai_keyed.py` regenerates `Common/Languages/English/Keyed/HH_Ai.xml` from the `AiT.T("Key", "English")` calls.

## Review round notes (1.1.0)
- Consent text says the relay host's web server may briefly log the player's IP. Orchestrator: add `access_log off;` for `hh-relay.jberger.de` in the nginx vhost (hearth-relay/deploy is outside this worktree), then the sentence can stay as is or be dropped.
- Only market-wide or per-asset price-move news kinds are sent for AI headlines; player-caused items and Lottery/Listing/Delisting/ProjectEnd/SolarHalt/Signal/BankRun keep the game's own text. Headlines carry no mark (the `ai.showBadge` setting covers reviews, chat and concierge).
- The relay status is re-polled every 10 minutes while it reports disabled or budget used up, with no AI screen open.
- AI is only available where the Hearth (guest) assembly is loaded: Silverline standalone has no AI. Mention in the Steam text.
- Integration: bump `HearthMod.Version` to 1.1.0 (sent as X-HH-Client; relay min_client is 1.1.0); insurance package must call `AiApi.TryClaimLetter` (no caller in this branch).
