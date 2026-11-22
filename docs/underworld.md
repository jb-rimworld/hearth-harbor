# Underworld module (ModuleId.Underworld)

Code `Source/Hearth/Underworld`, defs `Common/Defs/Underworld`, texts `Keyed/HH_Underworld.xml`, pure maths `Source/Hearth.Core/Pure/UnderworldMath.cs` (+ xunit tests in `Source/Tests/UnderworldMathTests.cs`).

## Gates
`Underworld.Visible` = module on and content intensity not Cozy. `Underworld.Unlocked` = Visible and `HearthProfile.UnderworldDiscovered`.
Dark/Grim only count after the one-time warning dialog (`UnderworldComponent.ContentWarningAck`); until then `Underworld.Effective` is Standard.
Every def of the dark side carries `Hearth.Underworld.UwGateExtension` (minContent, needsDiscovery). Buildings use `PlaceWorker_UnderworldGate` (architect + placement), research is hidden through a postfix on `ResearchProjectDef.IsHidden`.
Other modules: `UwDefs.Open(def.GetModExtension<UwGateExtension>())` (e.g. for the drug lounge venue type in the venue sign list).

## API for other modules
- `Underworld.Whispers`, `AddWhispers`, `RespectableArrivalFactor`, `OutlawArrivalFactor`, `HighRollersActive`
- `Underworld.IsShady(pawn)` / `MarkShady(pawn)` (Inn: call for its Shady stays; shady guests may open back room doors, use fence and holding cells)
- `BackRoom.IsBackRoom(room)` / `IsBackRoomCell(cell, map)`: rooms behind only back room doors; skip them when rating the house
- `Underworld.RenownDelta` event (map, delta already capped by the tier, reason): the Inn module owns renown and subscribes
- Guest activities `underworld.fightpit`, `underworld.lounge`, `underworld.fence` are registered in `GuestActivities`; shady groups are also driven by the module's own director (`UwDirector`), so gangs work without Inn.

## Money
All silver moves through `Payments` and `Ledger`. Accounts: `house:pit:<map>` (stakes, always swept: payouts + house take = pool exactly), `house:fence:<map>` (fence cash box, capped by setting, surplus to the till). Caretakers sweep leftovers.
