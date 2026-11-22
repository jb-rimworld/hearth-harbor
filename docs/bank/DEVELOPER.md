# Silverline: notes for developers and AI agents

The bank lives in `Source/Silverline` (namespace `Silverline`), the pure maths in `Source/Hearth.Core/Pure/Finance`
(unit tests in `Source/Tests/FinanceTests.cs`, run `Source/Tests/run.sh`). The bank never references `Hearth.dll`.

## Public face
- `BankApi.Impl` (`IBankImpl`): `HasAccount`, `ColonyAccount` (`"bank:colony"`), `ColonyBalance`, `DirectDepositOn`,
  `Deposit(map, amount, memo)` (till or world into the account), `Withdraw(map, amount, memo)` (physical silver by pod or at a bank building).
- `BankApi.Ready` is false until the player opened an account: the bank is invisible before that (N08).
- `BankTabs.Register(new BankTab { Id, LabelKey, LabelEn, Order, Draw = rect => ..., Visible = () => ... })` adds a page to the bank window.
  Built-in orders: lobby 10, loans 20, history 30, terms 40. Use 50 and up. Wrap nothing: the window already guards `Draw`.
- `Tills.BankSink` is set by the bank. With "direct deposit" on, the midnight till flush books the money onto the account (fee setting `bank.directDepositFee`).

## Ledger accounts
`bank:colony` (the account), `bank:transit` (money on its way: deposits waiting for the delay, withdrawals waiting for the drop pod),
`bank:loans` (always negative: minus the sum of all loan balances). Interest, fees and silver leaving or entering the world are booked against `world`.
The invariant (sum of all accounts is 0) holds after every operation; tests check it. A caretaker check repairs the loan account if it ever drifts.

## State (one GameComponent, `BankGameComp`)
Account, chosen bank, pending transfers, loans, credit score, 120 days of history, ATM limit counter, bailout date, the visiting collectors.
Daily work (`DailyStep`): account interest `M_acc^(1/60)` with `M_acc = M^(beta * bank.interestMod)` (M = setting `bank.profit`, log scale, follows the difficulty
tier until overridden; see `BankSettings.ProfitM`), loan interest, quadrum fee, Black Ledger risk, collection ladder, bailout, history sample.

## Collection ladder (KONZEPT 8.9)
`FinanceMath.CollectionStage(daysOverdue, cap)`, cap = setting `bank.maxCollectionStage` (0,1,2,4,5,6,7 by difficulty, Standard Room 4).
Stage effects live in `Collections` (letters, late fee, offset in `BankGameComp.Credit`, visitors, seizure dialog, raid).
`Collections.TestMode` records stages 5 to 7 instead of spawning raiders.

## Adding a bank
Add an `Silverline.SL_BankDef` to `Common/Defs/Bank/Bank_Banks.xml` (fees, `interestMod`, `loanSpread`, `iconPath` = `UI/HH/Bank/Logo_<defName>`, perks).
`MayRequire` on the def handles DLC; `requiresModule` handles Hearth modules.

## Texts
Write `L.T("Key", "English text", args)` in code. `python3 tools/gen_bank_keys.py` regenerates `Keyed/HH_Bank.xml`. Placeholders are `{0}`.

## Tests
`tools/run_test.sh smoke,bank.account,bank.interest,bank.withdraw,bank.sink,bank.loan,bank.collect,bank.wealth,bank.mood,bank.ui,bank.save,bank.buildings,bank.banks <label>`.
`bank.shots` takes screenshots of every page into `/private/tmp/claude-501/hh_tests/bank_shots` (manual visual check, not part of normal runs).
A yield after `DebugSetTicksGame` is measured from before the jump: write `BT.Jump(n); yield return 1; yield return m;`.
