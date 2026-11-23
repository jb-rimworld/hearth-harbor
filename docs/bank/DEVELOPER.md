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
Daily work (`DailyStep`): account interest `M_acc^(1/60)` with `M_acc = clamp(1 + bank.accountShare * interestMod * (M - 1), 0.90, 3.0)` since 1.1.0 (a savings formula: +4 % at M 1.2, below Mattress Plus +8 %, the bond and the index; the old key `bank.accountBeta` stays registered but is unused; M = setting `bank.profit`, log scale, follows the difficulty
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


## 1.1.0 additions (bank package)
- **Net worth** = account + deposits in transit + silver at home + investments at market value - debts (`BankGameComp.NetWorth`, cached 250 ticks, `FinanceAccounts.Post` invalidates it).
  Investments come from `BankGameComp.InvestmentsProvider` (set by `FinanceModule`; securities, patience bonds, Omen/project stakes at cost). Credit limits count only `bank.loanPortfolioShare` (50 %) of them (`LoanNetWorth`).
  Not changed: the storyteller wealth patch (`Patch_PlayerWealth`) still counts only the balance.
- **Terms** live in the mod settings only; the Terms tab and the colony lottery column show them read-only and call `HearthMod.OpenSettings("mod:Bank")`.
- **Lobby transfer**: slider = percent of the silver the colony holds (deposit) or can withdraw (`MaxWithdrawCoins`), typed amount and presets are absolute, no upper cap (`TransferMath`).
- **Sounds**: `BankSound.Play(name)` -> SoundDef `HH_Bank_<name>` (Common/Defs/Bank/Bank_Sounds.xml, files Common/Sounds/HH/Bank, list in docs/v110/sfx_bank.json), setting `bank.sounds`.
- Tab order is fixed in `BankTabs.Register` (lobby, loans, invest, insurance, lottery, history, terms).
- New suites: `bank.networth`, `bank.transfer`, `bank.windows`, `bank.terms`, `bank.sounds`, `bank.settingsjump`; `finance.lottery` extended.

### 1.1.0 review follow-up (changelog notes for players)
- Account interest is now capped at the savings product Mattress Plus at the investment M (`BankSettings.AccountMultiple`, `AccountCeiling`), so the two profitability sliders cannot make the account beat investing.
- Drop pods follow `bank.podMaxLoad` (at most 10 pods per withdrawal, `BankSilver.PodPlan`); counter withdrawals place at most 100 stacks, the rest comes by pod.
- Withdrawals in a drop pod count as "in transit" (net worth and storyteller bank share no longer dip while the pod flies).
- A quick-pick batch posts one ledger entry; the lottery tab reads cached counters; the draw record stores the best match over ALL tickets (`bestMatches`/`bestStar`, additive scribe keys `bm`/`bs`) and keeps the 100 best tickets.
- Lottery win/lose sound plays when the draw resolves (also with the tab closed). Direct deposit, trade deposit and the "direct deposit at account opening" choice are read-only terms with a settings button.
- Side effects of the net worth change (F22) for the 1.1.0 changelog: lower account interest on existing saves (+4 % at M 1.2 instead of the old M^beta); higher wealth tier, mood and Thrumbo stake limit because investments count; fewer bailouts (net worth is less often negative);
  the bailout peak window (`samples`, 60 days) mixes the old and new net worth definition, so it jumps once. The storyteller wealth patch (`Patch_PlayerWealth`) still ignores investments (by design, scope).
