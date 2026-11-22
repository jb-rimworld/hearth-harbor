# Silverline finance products (module Bank)

Investments, Omen Exchange, faction projects with Longs and Project Shorts, patience bonds, lotteries. KONZEPT 8.5–8.8, 8.11, 8.14, 9.2, K17, K28.

## Layout
| Path | What |
|---|---|
| `Source/Hearth.Core/Pure/Markets/` | pure maths, no game references (also compiled into `Source/Tests`): `DetMath` (exp/ln/pow from + − × ÷), `MarketModel` (jump diffusion with exact drift compensation), `OmenMath`, `ProjectMath`, `LotteryMath` |
| `Source/Silverline/Investments/` | `AssetDef` (data), `FinanceGame` (save state, GameComponent), `MarketEngine` (hourly step, events, news), `Signals` (system B), `Portfolio` (buy/sell, NFT queue, patience bonds), `OmenExchange`, `FactionProjects`, `InvestTab` (UI), `FinanceModule` (entry, caretaker, fallback window, comms console patch), `FinanceText` (English texts, single source) |
| `Source/Silverline/Lottery/` | `LotteryGame` (global + colony lottery), `LotteryTab` (UI) |
| `Common/Defs/Finance/Assets.xml` | the products (`<Silverline.AssetDef>`, defName `SL_Asset_*`) |
| `Common/Languages/English/Keyed/HH_Finance.xml` | generated from `FinanceText.cs` with `python3 tools/gen_finance_keyed.py` |

## Maths in one paragraph
Hourly step, `ln S += (drift + signal)·dt + σ√dt·Z + Σ load_k·Y_k + own jump + hazard event`. The drift is computed so that `E[S_T] = S_0·M_a^(T/60)` exactly,
with `M_a = M^β·(1+adjust)` (M = profitability, multiple per game year of 60 days); clamped jumps are compensated by quadrature, so crashes, pumps, rug pulls and defaults never
move the long-run mean, they only decide who wins. Systemic shocks (crash, rally, regulator raid) are rolled once per hour from a sealed stream and hit every asset with its own load.
Memecoins: mean positive, median far below it (pumps λ 0.1/day, rug pull 0.02/day, delisted after 3 days; holders are paid out at the last price).
System B (game signals: goodwill, settlements, colony wealth, raids survived) nudges prices by at most ±0.5 σ per day and decays 20 % per day.

## Money
Everything through the ledger: `bank:colony` (or `BankApi.Impl.ColonyAccount`; a local `bank:colony` when the bank module is absent) ⇄ `market:<asset>` / `market:omen` / `market:projects` / `market:patience` / `lottery:*`; fees to `bank:fees:finance`.
`market:*` accounts may go negative (they are the world market). Lottery pots never do: payouts are clamped to the pot (`LotteryMath.Allocate`, `PayFromPots`), the caretaker repairs anything else.

## Random
All player-facing draws use `JRand.Sealed` (`finance.events`, `finance.px.<asset>`, `finance.project`, `lottery.global.draw` …). Pre-roll of 60 days of history at game start uses local streams seeded from the world seed.

## For other modules
- `LotteryGame.Current.SellColonyTickets(n, who)` sells tickets of the colony lottery (e.g. a guest bought one after the guest module charged them).
- `FinanceGame.Current.ProjectHelp(projectId)` / `ProjectSabotageStrike(projectId, guardsEscaped)` are the hooks for the world-map actions.
- `BankTabs`: tabs `invest` (order 40) and `lottery` (order 50). Without a bank window the comms console float menu offers "Silverline investments" (`Window_Silverline`).
- New products: any mod can add `<Silverline.AssetDef>` defs (defName starts with `SL_Asset_`).

## Tests
`Source/Tests/run.sh` (pure maths, 70+ tests) · in game: `finance.math`, `finance.buy`, `finance.signals`, `finance.omen`, `finance.projects`, `finance.lottery`, `finance.save`, `finance.ui`.
