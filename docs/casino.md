# Casino module (ModuleId.Casino)

Guests gamble, colonists gamble for joy, the house keeps books. KONZEPT 6.6, 6.12–6.14, 9.2, K10–K18, K28, N47, N49.

## Layout
- `Source/Hearth.Core/Pure/Casino/` – maths without game references (unit tested in `Source/Tests/CasinoTests.cs`):
  `SlotMath` (paytable, exact RTP by enumeration, three volatilities, scale factor per target RTP), `RouletteMath`
  (European/American wheel, la partage, 154 bets, every bet gets exactly the target edge), `BlackjackMath` (infinite deck DP,
  accuracy-mixed policy, exact edge and σ, interactive API), `PokerMath` (Rimdeck: rake, skill-tilted winner), `GamblingMath`
  (addiction chance, stages, need fall) and `HouseRisk` (max bet from house cash, ruin estimate).
- `Source/Hearth/Casino/` – game code: `CasinoModule`, `CS` (settings), `CasinoState` (saved sessions, records, day book),
  `CompCasinoGame` (runs rounds, books, gizmos), `CasinoEngine` (rounds), `CasinoBank` (buy-in, cash-out, float, closing sweep),
  `CasinoJobs` (play, croupier, joy giver), `Addiction` (hediff, need, thought, policy), `CasinoUI` (help hint, practice window, main tab page).
- `Common/Defs/Casino/*.xml`, `Common/Languages/English/Keyed/HH_Casino.xml`.

## Money flow
Guest buy-in = `Payments.TryCharge` into the house account (`Venue.Account` in a Casino venue, else `house:casino:<map>`). Rounds move
only a virtual purse; cash-out returns whole coins (stochastic rounding, expectation exact) to the guest's inventory and books
house → world. Cage present: guests bring 5 % more and keep 1–3 % of winnings as worthless chips. When the venue closes (or at
midnight without a venue) everything above float + current purses goes to the colony till. Colonists use free chips: no ledger entry.

## Tests
`Source/Tests/run.sh` (unit) · `tools/run_test.sh smoke,casino.defs,casino.slots,casino.tables,casino.joy,casino.addiction,casino.hint <label>`

## The Lucky Thrumbo (player against a foreign house, KONZEPT 6.13)
`Source/Hearth/Casino/PlayerHouse/`: `ThrumboHouse` (engine: stake, sealed draw, payout), `ThrumboState` (save data: history, totals, change owed, open blackjack hand as seed + actions),
`ThrumboSettings` (`TS`, `ThrumboOdds`: base edges slots 8 %, roulette 2.7 %, blackjack 3:2 S17 + `thrumbo.edgeShift` per 9.2 "Hausvorteil extern"; blackjack shift = factor on winning hands), `ThrumboWindow`, `ThrumboComm`, `ThrumboCard`.
Entry points: comms console (`Silverline.ConsoleComms.Register`, rides on the one GetCommTargets patch) and a card on the Casino page. Money: account via `BankApi.TryPay/Receive`; storage silver via `BankSilver.TakeCoins` (stake, whole coins) and `Payments.TryPayout` (payout to the colonist); fractions of a coin stay as `ChangeMicro`. House ledger account `house:thrumbo`.
Streams: `JRand.Sealed("thrumbo.slots|roulette|blackjack")`. Logo `UI/HH/Casino/LuckyThrumbo` (tools/art). Test: `casino.thrumbo`. Not built: K15 "Raise the stakes" (items/animals/prisoners), research gate `SL_HighStakes` (no such def exists).
