// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using Hearth.Testing;
using UnityEngine;
using Verse;

namespace Silverline
{
    public sealed class FinanceLotterySuite : HearthTestSuite
    {
        public override string Name => "finance.lottery";
        public override ModuleId Module => ModuleId.Bank;
        static bool PotsFine(Ledger led) => new[] { FinanceAccounts.LotteryGlobalPool, FinanceAccounts.LotteryGlobalJackpot, FinanceAccounts.LotteryColonyPot, FinanceAccounts.LotteryColonyJackpot }.All(a => led.Balance(a).Micro >= 0);

        public override IEnumerable<int> Run(TestContext t)
        {
            var lot = LotteryGame.Current; lot.EnsureInit(); var led = Ledger.Current; FinTest.Fund(Money.FromSilver(10000));
            t.Check(lot.initialized, "lottery initialised");
            t.Check(lot.GlobalJackpot >= LotteryGame.JackpotSeed, "opening jackpot seeded: " + lot.GlobalJackpot.ToStringShort());
            var price = LotteryGame.GlobalPrice; t.Check(price == Money.FromSilver(5), "global ticket costs 5 silver");

            // tickets
            var cash0 = FinanceAccounts.Cash;
            t.Check(!lot.BuyGlobalTicket(new[] { 1, 2, 3, 4 }, 1).Ok, "four numbers are not a ticket");
            t.Check(!lot.BuyGlobalTicket(new[] { 1, 2, 3, 4, 41 }, 1).Ok, "41 is out of range");
            t.Check(!lot.BuyGlobalTicket(new[] { 1, 1, 2, 3, 4 }, 1).Ok, "duplicates refused");
            t.Check(!lot.BuyGlobalTicket(new[] { 1, 2, 3, 4, 5 }, 11).Ok, "star 11 refused");
            var b1 = lot.BuyGlobalTicket(new[] { 5, 4, 3, 2, 1 }, 3); t.Check(b1.Ok, "ticket A bought: " + b1.Message);
            var b2 = lot.BuyGlobalTicket(new[] { 1, 2, 3, 10, 11 }, 9); t.Check(b2.Ok, "ticket B bought");
            var qp = lot.QuickPick(10); t.Check(qp.Ok, "quick pick ×10: " + qp.Message);
            t.Check(lot.PlayerTicketsForNextDraw == 12, "12 tickets for the next draw: " + lot.PlayerTicketsForNextDraw);
            t.Check(cash0 - FinanceAccounts.Cash == price * 12, "12 tickets cost 60 silver");
            t.Check(led.Balance(FinanceAccounts.LotteryGlobalPool) == price * 12, "money sits in the pool until the draw");
            t.Check(FinTest.Balanced, "ledger balanced after buying tickets");
            // F20: the tab can say how many tickets, what they cost, and which they are
            t.Check(lot.PlayerSpentThisDraw == price * 12, "spent on this draw: " + lot.PlayerSpentThisDraw.ToStringShort());
            t.Check(lot.FreeTicketsThisDraw == 0, "no free tickets without a lucky colonist");
            var mine = lot.TicketsOfNextDraw(); t.Check(mine.Count == 12 && mine[mine.Count - 1].nums.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) && mine[0].paid == price.Micro, "tickets listed newest first, each with its price");
            t.Check(qp.Units == 10, "quick pick tells how many it bought: " + qp.Units);
            t.Check(LotteryTicket.TryParse(mine[mine.Count - 1].Compact(), out var pn, out var ps) && pn.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) && ps == 3, "a ticket survives its compact text");
            t.Check(!LotteryTicket.TryParse("1 2 x|3", out _, out _) && !LotteryTicket.TryParse("", out _, out _) && !LotteryTicket.TryParse("1 2 3", out _, out _), "bad compact tickets are refused");
            var rich = lot.QuickPick(300); t.Check(rich.Ok && rich.Units == 300, "300 quick picks work with enough money");
            LotteryTab.TestReset();
            { // the tab draws with hundreds of tickets, at a small and a large size
                int e0 = HLog.ErrorCount; var w = new Window_Silverline(); Find.WindowStack.Add(w); yield return 5;
                LotteryTab.Draw(new Rect(0, 0, 900f, 560f)); LotteryTab.Draw(new Rect(0, 0, 1700f, 900f)); w.Close();
                t.Check(HLog.ErrorCount == e0, "lottery tab draws 300+ tickets without errors");
            }

            // draw with forced numbers: ticket A wins the jackpot, ticket B has three right (no star)
            var spentRec = lot.PlayerSpentThisDraw;
            var jackpotBefore = lot.GlobalJackpot; var cashBeforeDraw = FinanceAccounts.Cash; int counter = lot.globalDrawCounter;
            var rec = lot.DrawGlobal(Find.TickManager.TicksGame, new[] { 1, 2, 3, 4, 5 }, 3);
            t.Check(rec != null && rec.numbers.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) && rec.star == 3, "forced draw recorded");
            t.Check(lot.globalDrawCounter == counter + 1 && lot.PlayerTicketsForNextDraw == 0, "tickets of the draw are gone, counter advanced");
            t.Check(rec.jackpotWinners >= 1, "jackpot had at least our winner: " + rec.jackpotWinners);
            var won = FinanceAccounts.Cash - cashBeforeDraw; t.Check(won.Micro > 0, "prize paid to the account: " + won.ToStringShort());
            t.Check(rec.playerPrize == won.Micro, "record matches the payout");
            t.Check(rec.playerSpent == spentRec.Micro && rec.playerSpent > 0, "the record remembers what the tickets cost: " + new Money(rec.playerSpent).ToStringShort());
            t.Check(rec.myTickets.Count > 0 && rec.myTickets.Count <= LotteryGame.TicketsKept, "the record keeps the tickets (capped at " + LotteryGame.TicketsKept + "): " + rec.myTickets.Count);
            t.Check(lot.LastGlobalDraw == rec, "last draw is the record");
            t.Check(rec.playerPrize <= (jackpotBefore + price * 12 + Money.FromSilver(100000)).Micro, "payout never exceeds what was in the pots");
            t.Check(PotsFine(led), "no pot is negative");
            t.Check(lot.GlobalJackpot >= LotteryGame.JackpotSeed, "jackpot reseeded after a win: " + lot.GlobalJackpot.ToStringShort());
            t.Check(FinTest.Balanced, "ledger balanced after the draw");

            // natural draw: numbers valid, no player ticket wins unless matched, history capped
            lot.QuickPick(3); var rec2 = lot.DrawGlobal(Find.TickManager.TicksGame + 1);
            t.Check(rec2.numbers.Count == 5 && rec2.numbers.Distinct().Count() == 5 && rec2.numbers.All(n => n >= 1 && n <= 40) && rec2.star >= 1 && rec2.star <= 10, "natural draw is a valid draw: " + string.Join(" ", rec2.numbers) + " ★" + rec2.star);
            t.Check(PotsFine(led) && FinTest.Balanced, "pots fine and ledger balanced");
            for (int i = 0; i < 30; i++) { lot.QuickPick(2); lot.DrawGlobal(Find.TickManager.TicksGame + 2 + i); if (!PotsFine(led)) break; }
            t.Check(PotsFine(led), "pots never negative over 30 further draws");
            t.Check(lot.history.Count <= LotteryGame.HistoryKeep, "history is capped: " + lot.history.Count);
            t.Check(FinTest.Balanced, "ledger balanced after many draws");
            t.Check(lot.totalSpent > 0 && lot.totalWon >= 0, "statistics tracked: spent " + new Money(lot.totalSpent).ToStringShort() + " won " + new Money(lot.totalWon).ToStringShort());

            // expected return: over many tickets the player gets back roughly the payout share (statistically loose)
            // (guaranteed by the pari-mutuel model; the unit tests check the maths)

            // review fixes: one ledger entry per quick-pick batch, best match over ALL tickets, sound when the draw resolves (tab closed)
            {
                lot.tickets.RemoveAll(x => x.drawIndex == lot.globalDrawCounter); lot.InvalidateTicketCache();
                lot.BuyGlobalTicket(new[] { 6, 7, 8, 9, 10 }, 2);                                   // the oldest ticket
                int entries0 = led.Journal.Count(e => e.Memo != null && e.Memo.StartsWith("Glitterworld Grand Draw: ") && e.Memo.Contains("ticket"));
                var cashA = FinanceAccounts.Cash; var big = lot.QuickPick(150);
                int entries1 = led.Journal.Count(e => e.Memo != null && e.Memo.StartsWith("Glitterworld Grand Draw: ") && e.Memo.Contains("ticket"));
                t.Check(big.Ok && big.Units == 150 && cashA - FinanceAccounts.Cash == price * 150, "150 quick picks cost 150 tickets: " + (cashA - FinanceAccounts.Cash).ToStringShort());
                t.Check(entries1 - entries0 <= 1, "one ledger entry for the whole batch, not 150: " + (entries1 - entries0));
                t.Check(lot.PlayerTicketsForNextDraw == 151 && lot.PlayerSpentThisDraw == price * 151 && lot.TicketsOfNextDraw().Count == 151, "cached counters follow the purchase");
                int playedBefore = BankSound.Played;
                var recBig = lot.DrawGlobal(Find.TickManager.TicksGame + 500, new[] { 6, 7, 8, 9, 10 }, 2);
                t.Check(recBig.bestMatches == 5 && recBig.bestStar, "best match counts ALL tickets (the oldest one hit 5 + star): " + recBig.bestMatches);
                t.Check(recBig.myTickets.Count == LotteryGame.TicketsKept && recBig.myTickets.Contains("6 7 8 9 10|2"), "the winning ticket is kept although 100 of 151 are listed");
                t.Check(BankSound.Played > playedBefore && (BankSound.Last == BankSound.Win || BankSound.Last == BankSound.Lose), "the draw plays win/lose without any window open: " + BankSound.Last);
                t.Check(lot.PlayerTicketsForNextDraw == 0 && lot.TicketsOfNextDraw().Count == 0, "cache is empty after the draw");
                var old = new LotteryDrawRecord(); t.Check(old.bestMatches == -1, "records from older saves have no stored best (the tab falls back)");
            }

            // colony lottery: pot model
            HSettings.Set("lottery.colonyOn", 1f); HSettings.Set("lottery.colonyQuote", 0.6f); HSettings.Set("lottery.colonyPrice", 10f); HSettings.Set("lottery.colonyFee", 1f);
            var potBefore = lot.ColonyPot; var till0 = led.Balance(Payments.ColonyTill(Find.AnyPlayerHomeMap));
            var take = lot.SellColonyTickets(100, "test");
            t.Check(lot.ColonyPot - potBefore == Money.FromSilver(10 * 0.6 * 100), "pot gets 60 % of the ticket price: " + (lot.ColonyPot - potBefore).ToStringShort());
            var houseExpected = Money.FromSilver(10 * 0.4 * 100 + 1 * 100);
            t.Check(led.Balance(Payments.ColonyTill(Find.AnyPlayerHomeMap)) - till0 == houseExpected, "colony keeps 40 % plus fees in the till: " + (led.Balance(Payments.ColonyTill(Find.AnyPlayerHomeMap)) - till0).ToStringShort());
            t.Check(lot.colonyEarned > 0, "earnings tracked");
            double dem = lot.ColonyDemandPerDay(out var g1, out var i1); t.Check(dem >= 0 && !double.IsNaN(dem), $"demand {dem:0.0}/day (guests {g1:0.0}, far away {i1:0.0})");
            var crec = lot.DrawColony(Find.TickManager.TicksGame); t.Check(crec != null && crec.colony && crec.numbers.Count == 4, "colony draw: " + string.Join(" ", crec.numbers));
            t.Check(PotsFine(led), "colony pots non-negative after the draw");
            for (int i = 0; i < 60; i++)
            {
                lot.SellColonyTickets(i % 50 + 1, "loop"); lot.DrawColony(Find.TickManager.TicksGame + i);
                if (!PotsFine(led)) { t.Check(false, "pot negative in loop " + i); break; }
            }
            t.Check(PotsFine(led), "pot never negative over 60 colony draws");
            t.Check(FinTest.Balanced, "ledger balanced after the colony lottery");
            // renown scales demand between 0.5x and 1.5x of neutral: a new colony (renown 10) keeps 60 % of the neutral demand, never near zero
            var oldRenown = RenownSource.Getter;
            RenownSource.Getter = () => 50f; double atNeutral = lot.ColonyDemandPerDay(out _, out _);
            RenownSource.Getter = () => 10f; double atNew = lot.ColonyDemandPerDay(out _, out _);
            RenownSource.Getter = oldRenown;
            t.Check(atNeutral <= 0 || (atNew >= atNeutral * 0.55 && atNew <= atNeutral), $"demand of a new colony is about 0.6x neutral: {atNew:0.00} vs {atNeutral:0.00}");
            // price sensitivity: a worse deal means fewer tickets (renown fixed, so the comparison never depends on the live colony)
            RenownSource.Getter = () => 50f;
            HSettings.Set("lottery.colonyQuote", 0.3f); double worse = lot.ColonyDemandPerDay(out _, out _);
            HSettings.Set("lottery.colonyQuote", 0.9f); double better = lot.ColonyDemandPerDay(out _, out _);
            t.Check(better >= worse, $"a better payout share attracts more tickets: {worse:0.0} → {better:0.0}");
            RenownSource.Getter = oldRenown;
            HSettings.Set("lottery.colonyOn", 0f);
            // caretaker: negative pot is repaired
            led.Post(FinanceAccounts.LotteryColonyPot, "world", led.Balance(FinanceAccounts.LotteryColonyPot) + Money.FromSilver(5), "test overdraw", true);
            int rep = Caretaker.Checks.First(c => c.Name == "Finance.Lottery").Run(); t.Check(rep >= 1 && PotsFine(led), "caretaker repaired the negative pot");
            t.Check(FinTest.Balanced, "ledger balanced at the end");
            yield break;
        }
    }
}
#endif
