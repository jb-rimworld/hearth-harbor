# DLC integration, traits and genes (module notes)

Code: `Source/Hearth/Dlc/*`, defs: `Common/Defs/Dlc/*`, patch: `Common/Patches/Dlc_IdeologyOffices.xml`, texts: `Keyed/HH_Dlc.xml`,
pure maths: `Source/Hearth.Core/Pure/DlcMath.cs` (unit tests `Source/Tests/DlcMathTests.cs`). KONZEPT chapter 11, 8.16.
Everything DLC is `MayRequire` in XML and guarded by `ModsConfig` in code. `tools/run_test_nodlc.sh` runs the in-game suites with no DLC
(`NO_DLC=1`, default) or a subset (`DLCS=royalty,biotech`).

## Hooks for other modules (all static, all null-safe)
| Who | Call | Meaning |
|---|---|---|
| Casino | `Hearth.Dlc.LuckApi.RtpShiftFor(pawn)` / `AdjustRtp(baseRtp, pawn)` | Lucky +1.5 pp, Unlucky -1.5 pp (setting `dlc.luckShift`); capped below 100 %, never lifts an already generous plan. Show the shifted plan openly in the Pit Boss view. Never bend the draw. |
| Casino | `LuckApi.WillGamble / JoyFactor / AddictionFactor / NoteResult(pawn, won)` | averse never plays; gambler/compulsive/high roller factors from stats; "Lucky streak" / "Of course I lost" thoughts |
| Bank (lotto) | `LuckApi.LottoTicketIsFree(pawn)` | call once per ticket bought; every 10th is free for Lucky |
| Inn / Kitchen / Shops | `DlcApi.ServiceQuality(host)`, `GuestExpectationOffset(guest)`, `HostExpectationOffset(host)`, `TipFactor(guest)`, `RecruitPriceFactor(host)`, `DishQualityLift(cook)`, `PriceFeelOffset(seller)`, `FamilyCheer(pawn)` | stat-based offsets of traits and genes |
| Casino / Bank | `DlcApi.TableSkillBonus(pawn)`, `DexterityBonus(pawn)`, `FraudDetection(pawn)`, `BankFeeFactor(pawn)`, `NegotiationBonus(pawn)` | croupier / cashier / clerk bonuses |
| Inn | `DlcApi.TheftFactor`, `BrawlRiskFactor`, `RumorFactor`, `CareProgressFactor` | multipliers for the trait effects that live in the guest module |
| Inn (bed button) | `NobleDemands.MaxTitleServedBy(room)` | "suitable up to: Baron" |
| Inn / Shops | `XenotypeHospitality.For(pawn)`, `RoomScore(def, room, map)`, `InterestWeight(pawn, thing)`, `SpendFactor`, `PriceToleranceOffset` | xenotype preferences (data: `Hearth.XenotypeHospitalityDef`) |
| UI | `IndulgenceApi` (`CanBuy`, `PriceFor`, `TryBuy`, `TempleBalance`), `ShrineApi`, `NobleStays` | bank tab "Faith" / altar windows |

Live effects already wired by this module (no other module needed): hosting XP multiplier (Harmony on `SkillRecord.Learn`, only while on a
Hosting work giver), tireless smile (`Need_Rest.NeedInterval`, only while Serving), service quality satisfaction on successful chats with guests
(`Pawn_InteractionsTracker.TryInteractWith`, throttled 1 per guest per hour), trait spawn commonality, xenotype room nudges every 6 hours
(via `GuestApi.AddSatisfaction`), guest prayer (`PrayActivity` registered with `GuestActivities`), noble stays (incidents + letter + settlement).

## Decisions worth knowing
- The money precepts use real vanilla machinery: `IssueDef` + five `PreceptDef`s each (Not a thing / Abhorrent / Disapproved / Accepted / Sacred), thoughts through
  `PreceptComp_SituationalThought` and `PreceptComp_SelfTookMemoryThought` with our history events `HH_BoughtIndulgence/HH_BoughtAscension`. Generation weights
  are flat (60/12/12/10/6); `DlcState.Maintain` re-rolls every non-player, non-fixed ideoligion once with meme weights (11.4), also in old saves.
- "Not a thing" is visible in the ideoligion editor (so a precept can always be switched away) but does not count toward the precept limit.
- Colonists pay from their pocket (Payments chain: cash, bank providers) or colony silver; guests through Payments/GuestApi. Fund destinations: deity account
  `faith:divine:<ideoId>`, faction account `faith:faction:<id>` (+goodwill), temple treasury `house:temple:<ideoId>`.
- The guest altar sits in the vanilla `Ideology` designation category until the Inn/Venues module provides `HH_Hospitality` (swap `designationCategory` then).
- Noble stays settle after the stay with `DlcMath.NoblePayment`; billeting compensation follows the tier (25 % at Overbooked, 10 % One-Star, 0 at Why?).
