# Underworld module (ModuleId.Underworld)

Code `Source/Hearth/Underworld`, defs `Common/Defs/Underworld`, texts `Keyed/HH_Underworld.xml`, pure maths `Source/Hearth.Core/Pure/UnderworldMath.cs`
(+ xunit tests in `Source/Tests/UnderworldMathTests.cs`). Design brief of the 1.2.0 rework: `_Planung_Hospitality_2026-11-22/v1.2.0/AUFTRAG.md`
(it wins over KONZEPT 6.11 where they differ).

Motto since 1.2.0: the back room just works. It is open from the start, there is no stranger to find, no warning dialog and no research for the
basics. Nothing of it happens until the player builds one of its facilities (N08).

## Gates
- `Underworld.Visible` = module on and content intensity not Cozy. `Underworld.Unlocked == Underworld.Visible` (discovery is gone).
- `Underworld.Effective == Underworld.Intensity`: Dark and Grim take effect at once, without a confirmation. The tooltips of the Dark and Grim
  chips in the settings say what each level adds.
- `Underworld.InUse(map)` = Unlocked and the map has at least one underworld facility (back room door, pit marker, fence counter, dock, hunting
  ground, organ counter or a drug lounge). Shady Inn stays, whisper-based arrival weights, gang visits, scandals and smuggler landings need it.
  Cached per map with a dirty flag (`Underworld.MarkInUseDirty`, set by `CompUwFacility` on spawn and despawn).
- Every def of the dark side carries `Hearth.Underworld.UwGateExtension` (`minContent`; `needsDiscovery` is still parsed and ignored,
  `alwaysHidden` hides the two old research projects). Buildings use `PlaceWorker_UnderworldGate` (architect + placement), research is hidden
  through a postfix on `ResearchProjectDef.IsHidden`. Other modules: `UwDefs.Open(def.GetModExtension<UwGateExtension>())`.
- Old saves: `HearthProfile.UnderworldDiscovered`, the three `…WarningAck` fields and the stranger fields stay readable and are ignored. The
  settings `underworld.mode` and `underworld.stranger*` are gone; stored values stay unread in the store (silent).

## What needs which content level
| Level | Adds |
|---|---|
| Cozy | nothing: the underworld is hidden (architect, research, tab, events) |
| Standard (default) | back rooms, fight pit (scored bouts; real but non-lethal bouts of prisoners and animals), fence, drug lounge, smuggler's dock, gang visits, protection demands, questioning of prisoners (setting), hunting ground with animals (research `HH_TheHunt`) |
| Dark | prisoners and animals may fight to the death (toggle on the marker), harder questioning, prisoners as quarry with a fitting ideology, the organ clinic (research `HH_BlackClinic`) |
| Grim | prisoners as quarry without an ideology precept, organ orders with deadlines |

The setting `core.peacefulUnderworld` takes away everything violent at any level (no bouts, no questioning, no hunting of people, no clinic, no
protection racket); back rooms, fence and drug lounge stay. Children never take part in any of it, at any level. No sexual content.

## How each facility is used
- **Back room**: a room whose only doors are back room doors (`Building_BackRoomDoor`; only colonists and shady guests may open it). Gangs meet
  there and pay the back room fee; respectable guests may stumble on it (`underworld.exposureChance`). It does not count against the house rating.
  The door says whether its room is a back room and why not (`BackRoom.StatusLine`).
- **Fight pit**: a fight pit marker inside a closed fence (a gate is fine) of at least 3 by 3 and at most 700 tiles. See the next section.
- **Fence**: a fence counter. Gangs sell their loot to it (`underworld.fence.buyPercent` of the value) and buy from its stock
  (`underworld.fence.sellPercent`). It pays from its own cash box (`underworld.fence.capital`), topped up from the till; surplus goes back.
  "Take goods out" empties the shelf; a deconstructed counter drops its goods. An owner may find out (`underworld.fence.exposureChance`).
- **Drug lounge**: a venue sign of type drug lounge in a room with two couches or armchairs. Guests pay `underworld.lounge.price` per session,
  and only take what their ideology allows.
- **Smuggler's dock**: now and then at night a boat lands (`underworld.smuggler.intervalDays`). A standard letter tells the player; the dock
  carries "Buy" and "Send them away". An unanswered boat leaves after `underworld.smuggler.offerHours`, nothing else happens.
- **Gangs**: the first gang comes about `underworld.gang.firstDays` (1) day after the first back room, fence counter or fight pit, then every
  `underworld.gang.intervalDays`. A failed visit is tried again after `underworld.gang.retryHours`, and the Underworld tab says why. Gang members
  spend the silver they carry. A gang may demand protection money (`underworld.gang.intimidateChance`, by difficulty): a standard letter, "Pay" and
  "Show them the door" on the gang leader; an unanswered demand counts as a refusal after `underworld.gang.demandHours`.
- **Hunting ground** (research `HH_TheHunt`): a hunting ground marker inside a closed fence of at least 60 tiles. Hunting parties pay to shoot the
  animals they bring. Prisoners as quarry: Dark with a fitting ideology or Grim, switched on separately on the marker.
- **Organ clinic** (Dark, research `HH_BlackClinic`): a counter for harvested organs; anonymous buyers collect them at night and pay into the till.

Offers (smuggler landings, protection demands) are data in `UnderworldComponent.Offers` (`UwOffer`: kind, map, faction, leader, price, goods,
open/expiry tick, letter id) and survive save/load. No modal dialog and no letter class of our own.

## Fight pit: who fights and how bouts are decided
Who can fight (`PitFighters.Build`, cached per pit for 250 ticks, refreshed when the Fighters window opens or a box is ticked):
- colony animals (toggle "Animals", on by default);
- guests as challengers (toggle "Guests", on): adult, capable of violence, awake, idle, checked in, healthy, not in a mental state, no trader,
  no quest lodger, not leaving within the bout window. Shady guests and gang members need nothing else; other guests need Bloodlust, Brawler or
  Melee 6 and must not be Kind;
- prisoners (toggle "Prisoners", off): brought into the arena and back like animals, and they do not try to escape on the way;
- colonists and slaves only when the player ticks them under "Fighters…" or right-clicks the marker with them ("Fight in the pit"). No random roll,
  no trait requirement. A colonist who asks while a bout runs fights in the next one;
  pits from old saves keep the former volunteer rule (bloodlust or melee 8 fight without a tick, `allowVolunteers`, toggle
  "Volunteers (old saves)" on the marker) until the player switches it off;
- a lone fighter: "Call an opponent" hires a pit beast no stronger than him (fee `underworld.pit.opponentFee`, 40 silver; the first hire of a game
  is free and is given back if its bout is called off). The colony pays (`UwPay.PayFromColony`): the till first, then silver in a stockpile or the home
  area, then the Silverline account; all or nothing. The gizmo, the right-click and the Fighters window say what is short ("the colony has 12 of 40
  silver"). With exactly one free colonist, the button ticks him itself. Smuggler crates and protection money are paid the same way.
- after a bout the two fighters do not start a vanilla social fight with each other or anyone for 2500 ticks (`Patch_PitNoQuarrel`).
- humanlike pawns under 18 biological years are minors for the whole Underworld (`Underworld.AdultAge`): no pit, bets, lounge, hunts or gang sales. The beast is gone after the bout, also after an abort, a removed pit or a save/load in between.
  A humanlike prizefighter is not built (left out of package B).

Automatic bouts start every `underworld.pit.intervalHours` when two fighters are ready and an adult guest is on the map (bets are the point).
"Start a bout" in the Fighters window needs neither guests nor a fair pair. The status line of the marker always says what the pit waits for.

Scoring (`FightBout`, the damage guard in `PitSafety`):
- A bout in which at least one fighter is a free humanlike (guest, colonist, slave) is **scored**. Every hit between the two fighters is
  absorbed and counted as points: the damage it would have done, at most 60 per hit. Nobody is injured or downed, nothing drops, no goodwill
  changes, no lord signal, no guilt, no "harmed me" memory, no panic flee.
- A fighter loses when his points reach `underworld.pit.scoreLimit` (100, nerd setting) times his health scale (1 for a human, more for big
  animals). After `underworld.pit.maxBoutTicks` the fighter with the smaller share of his limit wins; equal shares are a draw and every stake
  is returned. With fists a human deals about 3 damage per second, so a usual bout lasts 20 to 40 seconds at speed 1 (observed 17 to 37 s).
- The loser gets one or two small bruises at the end. The winner gets no memory (no fitting vanilla one exists).
- Bouts of prisoners and animals among themselves are fought for real, but stop at `underworld.pit.yieldHealth`, unless "To the death" is on
  (Dark and up). `Faction.Notify_MemberTookDamage` skips a bout pair as a safety net.
- Guests bet `underworld.pit.betMin` to `underworld.pit.betMax` (never more than a third of their budget); the house keeps
  `underworld.pit.houseCut` of the pool. A winning guest gets `underworld.pit.guestPurse`, paid only out of that bout's house take.

## Underworld tab and guide
- `UnderworldPage` (main tab page `underworld`, visible whenever the underworld is visible): content level with a Settings button, open offers
  (Accept, disabled with the reason, and Decline), one row per facility in the order back room, fight pit, fence, lounge, dock, gangs, hunting
  ground, clinic, then whispers and statistics. Each row: a state dot (grey not built, amber waiting, green working), the facility's own status
  sentence (`FacilityStatus.All`, cached 250 ticks; the pit row from `CompFightPit.StatusLine/ReadyLine/LastBoutLine/PageState`), last/next lines.
  "Build" closes the tab and selects the designator (`UwBuild.DesignatorFor`, the module's own copy of the lookup, cached a second); a built pit
  has "Fighters…" and "Show". Rows the save does not allow are left out (a built pit under the peaceful setting stays and says why). The rows are
  rebuilt at most twice a second; `Draw` never scans pawns or things.
- Guide topic `HH_Guide_BackRoom` (`Common/Defs/UI/GuideTopics.xml`), unlocked by the check `underworldVisible`. Steps and their checks:
  `uwPitArena` (a pit marker in a valid arena), `uwBoutSettled` (`PitRecords.EverSettled`), `uwBackRoom`, `uwGangVisit`, `uwFence`
  (registered in `UwGuide.Register`).

## Settings (all `HSettings`, per save; category in brackets)
- Whispers: `underworld.whispersDecay`, `scandalCooldownDays`, `exposureChance`, `whispers.<pit|fence|lounge|gang|tour|hunt|organ>` (nerd).
- Fight pit: `underworld.pit.houseCut`, `intervalHours`, `betMin`, `betMax`, `fairRatio` (nerd), `yieldHealth` (nerd), `maxBoutTicks` (nerd),
  `guestPurse`, `opponentFee`, `scoreLimit` (nerd).
- Fence: `underworld.fence.buyPercent`, `sellPercent`, `capital`, `exposureChance`, `goodwillHit`.
- Gangs: `underworld.gang.intervalDays`, `firstDays` (nerd), `retryHours` (nerd), `demandHours`, `feeMin`, `feeMax`, `sizeMax`,
  `intimidateChance` (by difficulty), `protectionFee`, `prisonAccess`, `tourFee`.
- Drug lounge: `underworld.lounge.price`. Smuggler's dock: `underworld.smuggler.intervalDays`, `offerHours`, `priceFactor`, `inspectChance`, `maxValue`.
- The Hunt: `underworld.hunt.feeMin`, `feeMax`, `quarryCount`, `intervalDays`, `quarryHours`. Organ clinic: `underworld.organ.priceFactor`,
  `orderPremium`, `orderDays`.
- Elsewhere: content intensity (settings, page Modules), `core.peacefulUnderworld`, `core.researchMode` (the hunt and the clinic follow it like
  every other project).

## API for other modules
- `Underworld.Whispers`, `AddWhispers`, `RespectableArrivalFactor`, `OutlawArrivalFactor`, `HighRollersActive`, `InUse(map)`
- `Underworld.IsShady(pawn)` / `MarkShady(pawn)` (Inn: call for its Shady stays; shady guests may open back room doors, use fence and holding cells)
- `BackRoom.IsBackRoom(room)` / `IsBackRoomCell(cell, map)`: rooms behind only back room doors; skip them when rating the house
- `Underworld.RenownDelta` event (map, delta already capped by the tier, reason): the Inn module owns renown and subscribes
- `FacilityStatus.All/Get/OpenOffers/OfferLine/CanAccept/Accept/Decline`, `PitRecords.Settled/EverSettled/LastBoutLine/StatusLine`
- Guest activities `underworld.fightpit`, `underworld.lounge`, `underworld.fence` are registered in `GuestActivities`; shady groups are also driven
  by the module's own director (`UwDirector`), so gangs work without Inn.

## Money
All silver moves through `Payments` and `Ledger`. Accounts: `house:pit:<map>` (stakes, always swept: payouts + house take = pool exactly),
`house:fence:<map>` (fence cash box, capped by setting, surplus to the till). Caretakers sweep leftovers. Gang fees use `UwPay.Budget(pawn)`.

## Removal
`PrepareRemoval` aborts every bout (stakes refunded), closes open offers with their letters; hunting ground quarry stays as ordinary animals.
