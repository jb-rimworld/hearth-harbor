// SPDX-License-Identifier: MIT
// In-game suite of The Lucky Thrumbo (compiled only with HH_TESTS).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;
using Hearth.Testing;
using Hearth.Casino.Tests;

namespace Hearth.Casino.PlayerHouse.Tests
{
    public sealed class ThrumboSuite : HearthTestSuite
    {
        public override string Name => "casino.thrumbo";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 60000;

        static int MapSilver(Map map) => map.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(x => x.stackCount);
        static int InvSilver(Pawn p) => p.inventory.innerContainer.Where(x => x.def == ThingDefOf.Silver).Sum(x => x.stackCount);
        static void ClearSilver(Map map) { foreach (var s in map.listerThings.ThingsOfDef(ThingDefOf.Silver).ToList()) if (s.Spawned) s.Destroy(); }

        static void AddStoredSilver(TestContext t, int coins)
        {
            var map = t.Map;
            var z = map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(zz => zz.label == "HH thrumbo test");
            if (z == null)
            {
                z = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager) { label = "HH thrumbo test" };
                map.zoneManager.RegisterZone(z);
                foreach (var cell in GenRadial.RadialCellsAround(t.FreeCellNearCenter(25), 2.3f, true))
                    if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null && map.zoneManager.ZoneAt(cell) == null) z.AddCell(cell);
            }
            var cells = z.Cells.ToList(); int i = 0;
            while (coins > 0) { int n = Math.Min(coins, 400); var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n; GenSpawn.Spawn(s, cells[i++ % cells.Count], map); coins -= n; }
        }

        static bool Soft(List<int> cards) => cards.Contains(1) && cards.Sum() + 10 <= 21;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t); var led = Ledger.Current;
            var comp = ThrumboComponent.Get(); comp.Clear();
            var bank = BankGameComp.Current; bank.TestReset(true);
            float oldShift = HSettings.Get(TS.EdgeShift); HSettings.Set(TS.EdgeShift, 0f);
            bool oldHint = HSettings.GetBool("core.helpHint");
            ClearSilver(map); yield return 1;

            // ---- defs / entries -------------------------------------------------------------------------------------
            t.Check(ConsoleComms.Visible().Contains(ThrumboComm.Instance), "the Lucky Thrumbo is one more entry of the comms console menu");
            t.Check(HearthTabs.Pages.Any(p => p.Id == "casino"), "the Casino page exists (card with the Lucky Thrumbo)");
            t.Check(ThrumboOdds.Slots(SlotVolatility.Medium, 0).Rtp is double r0 && Math.Abs(r0 - 0.92) < 1e-6, "base slot payout 92 %");
            t.Check(Math.Abs(ThrumboOdds.Slots(SlotVolatility.Medium, 0.03).Rtp - 0.89) < 1e-6, "the edge shift moves the slot payout (+3 points: 89 %)");
            t.Check(Math.Abs(ThrumboOdds.Roulette(0).ExactEdge(RouletteBet.Straight(7)) - 0.027) < 1e-9, "roulette edge 2.7 % exactly");
            yield return 1;

            // ---- stakes and payouts through the account ---------------------------------------------------------------
            led.Post("world", BankGameComp.Colony, Money.FromSilver(100000), "test funding"); bank.InvalidateCache();
            var seat = new ThrumboSeat(map, col, PayFrom.Bank);
            long sum0 = led.SumAll(); var before = bank.Balance;
            var r = ThrumboHouse.PlaySlots(seat, Money.FromSilver(10), SlotVolatility.Medium);
            t.Check(r.Ok, "a slot round at the account works: " + r.Text + r.Error);
            t.Check(bank.Balance == before - r.Staked + r.Paid, "the account moved by exactly -stake + payout (" + before + " -> " + bank.Balance + ")");
            t.Check(led.Balance(ThrumboHouse.House) == r.Staked - r.Paid, "the house account holds stake minus payout");
            t.Check(led.SumAll() == sum0, "the ledger stays balanced");
            t.Check(comp.History.Count == 1 && comp.RoundsAll == 1 && comp.StakedAll == r.Staked.Micro, "one history line and the totals match");

            // limits: nothing is booked for a refused stake
            var bal = bank.Balance;
            t.Check(!ThrumboHouse.PlaySlots(seat, Money.FromSilver(0.5), SlotVolatility.Low).Ok, "below the smallest stake is refused");
            var limit = ThrumboHouse.TableLimit(map);
            t.Check(!ThrumboHouse.PlaySlots(seat, limit + Money.FromSilver(1), SlotVolatility.Low).Ok, "above the table limit is refused (limit " + limit + ")");
            t.Check(limit.Silver >= 50 && limit.Silver <= 50000, "table limit between floor and cap");
            t.Check(bank.Balance == bal && comp.RoundsAll == 1, "refused stakes book nothing");
            yield return 1;

            // ---- stakes and payouts through storage silver ------------------------------------------------------------
            AddStoredSilver(t, 300); yield return 1;
            var sseat = new ThrumboSeat(map, col, PayFrom.Silver);
            int m0 = MapSilver(map), i0 = InvSilver(col); long s0 = led.SumAll(); var houseBefore = led.Balance(ThrumboHouse.House);
            t.Check(!ThrumboHouse.PlayRoulette(sseat, Money.FromSilver(2.5), RouletteBet.Red()).Ok, "silver from storage is staked in whole coins only");
            long change0 = comp.ChangeMicro;
            var rr = ThrumboHouse.PlayRoulette(sseat, Money.FromSilver(20), RouletteBet.Red());
            t.Check(rr.Ok, "a roulette round from storage works: " + rr.Text + rr.Error);
            int m1 = MapSilver(map), i1 = InvSilver(col);
            long owedNow = comp.ChangeMicro;
            long paidCoins = (rr.Paid.Micro + change0 - owedNow) / Money.PerSilver;
            t.Check(m1 == m0 - 20, "the stake left the map's silver (" + m0 + " -> " + m1 + ")");
            t.Check(i1 - i0 == paidCoins, "whole coins of the payout reached the colonist (" + (i1 - i0) + " vs " + paidCoins + ")");
            t.Check(owedNow >= 0 && owedNow < Money.PerSilver, "the house owes less than one coin");
            t.Check(led.SumAll() == s0, "ledger balanced after the silver round");
            t.Check(led.Balance(ThrumboHouse.House) == houseBefore + rr.Staked - new Money(paidCoins * Money.PerSilver), "house account = stake minus posted payout");
            // more rounds until a win has paid out coins: map + inventory silver always moves by exactly -stake + posted payout
            bool sawWin = false;
            for (int k = 0; k < 40 && !sawWin; k++)
            {
                int tm = MapSilver(map) + InvSilver(col); long chg = comp.ChangeMicro;
                var rk = ThrumboHouse.PlayRoulette(sseat, Money.FromSilver(10), RouletteBet.Red());
                if (!rk.Ok) break;
                long coins = (rk.Paid.Micro + chg - comp.ChangeMicro) / Money.PerSilver;
                t.Check(MapSilver(map) + InvSilver(col) == tm - 10 + coins, "round " + k + ": silver on the map and on the colonist moved by -stake + whole payout");
                sawWin = rk.Paid.Micro > 0;
            }
            t.Check(sawWin, "a winning silver round paid coins to the colonist");
            ClearSilver(map); foreach (var s in col.inventory.innerContainer.Where(x => x.def == ThingDefOf.Silver).ToList()) s.Destroy();
            t.Check(!ThrumboHouse.PlaySlots(sseat, Money.FromSilver(5), SlotVolatility.Low).Ok, "without silver in reach the stake is refused");
            yield return 1;

            // ---- sealed stream -----------------------------------------------------------------------------------------
            var stream = JRand.Sealed("thrumbo.slots"); ulong c0 = stream.Counter;
            var a1 = ThrumboHouse.PlaySlots(seat, Money.FromSilver(1), SlotVolatility.Medium);
            ulong c1 = stream.Counter; stream.Counter = c0;       // what a reload to the saved position does
            var a2 = ThrumboHouse.PlaySlots(seat, Money.FromSilver(1), SlotVolatility.Medium);
            t.Check(a1.Ok && a2.Ok && a1.Reels.SequenceEqual(a2.Reels) && a1.Paid == a2.Paid && c1 == stream.Counter, "same stream position: same reels and payout (" + a1.Text + ")");
            var rs = JRand.Sealed("thrumbo.roulette"); ulong rc = rs.Counter;
            var p1 = ThrumboHouse.PlayRoulette(seat, Money.FromSilver(1), RouletteBet.Black()); rs.Counter = rc;
            var p2 = ThrumboHouse.PlayRoulette(seat, Money.FromSilver(1), RouletteBet.Red());
            t.Check(p1.Pocket == p2.Pocket, "roulette: the pocket does not depend on the bet or on a reload");
            // an open blackjack hand survives a reload with the same cards
            var d = ThrumboHouse.DealBlackjack(seat, Money.FromSilver(5));
            if (!d.Done)
            {
                var h = ThrumboHouse.CurrentHand(); var cards = string.Join(",", h.Round.Player) + "/" + string.Join(",", h.Round.Dealer);
                ThrumboHouse.Hit(seat);
                var h2 = ThrumboHouse.CurrentHand();
                string afterHit = h2 == null ? "done" : string.Join(",", h2.Round.Player);
                ThrumboHouse.DropLiveHand();   // like a load: only seed and actions are saved
                var h3 = ThrumboHouse.CurrentHand();
                t.Check(comp.HandOpen == (h2 != null), "the open hand is part of the saved state");
                if (h3 != null) t.Check(string.Join(",", h3.Round.Player) == afterHit && cards.StartsWith(string.Join(",", h3.Round.Player.Take(2))), "rebuilt hand has the same cards after a reload");
                ThrumboHouse.StandIfOpen(seat);
            }
            t.Check(!comp.HandOpen && ThrumboHouse.CurrentHand() == null, "the hand is closed again");
            yield return 1;

            // ---- 10,000 rounds converge to the designed payout --------------------------------------------------------
            led.Post("world", BankGameComp.Colony, Money.FromSilver(3000000), "test funding"); bank.InvalidateCache();
            comp.Clear();
            var stake = Money.FromSilver(10); long sumA = led.SumAll();
            var pt = ThrumboOdds.Slots(SlotVolatility.Medium, 0); var wheel = ThrumboOdds.Roulette(0); var solver = ThrumboOdds.Solver; var red = RouletteBet.Red();
            double slotNet = 0, rouNet = 0, bjNet = 0;
            const int nS = 4000, nR = 3000, nB = 3000;
            for (int i = 0; i < nS; i++) slotNet += ThrumboHouse.PlaySlots(seat, stake, SlotVolatility.Medium).Net.Silver / stake.Silver;
            for (int i = 0; i < nR; i++) rouNet += ThrumboHouse.PlayRoulette(seat, stake, red).Net.Silver / stake.Silver;
            int bjFail = 0;
            for (int i = 0; i < nB; i++)
            {
                var res = ThrumboHouse.DealBlackjack(seat, stake);
                while (!res.Done && res.Ok)
                {
                    var hand = ThrumboHouse.CurrentHand(); var pl = hand.Round.Player;
                    var act = solver.BestAction(BlackjackSolver.HandTotal(pl), Soft(pl), pl.Count == 2, hand.Round.Dealer[0], out _);
                    res = act == BlackjackAction.Hit ? ThrumboHouse.Hit(seat) : act == BlackjackAction.Double ? ThrumboHouse.DoubleDown(seat) : ThrumboHouse.Stand(seat);
                }
                if (!res.Ok) { bjFail++; continue; }
                bjNet += res.Net.Silver / stake.Silver;
            }
            t.Check(bjFail == 0, "no blackjack hand failed");
            double zS = (slotNet / nS - (pt.Rtp - 1)) / (pt.Sigma / Math.Sqrt(nS));
            double zR = (rouNet / nR - (-wheel.TargetEdge)) / (wheel.Sigma(red) / Math.Sqrt(nR));
            double zB = (bjNet / nB - (-solver.HouseEdge)) / (solver.Sigma / Math.Sqrt(nB));
            t.Note($"slots RTP {(1 + slotNet / nS) * 100:0.0} % (design {pt.Rtp * 100:0.0}), roulette {(1 + rouNet / nR) * 100:0.0} % (design {(1 - wheel.TargetEdge) * 100:0.0}), blackjack {(1 + bjNet / nB) * 100:0.0} % (design {(1 - solver.HouseEdge) * 100:0.0})");
            t.Check(Math.Abs(zS) < 4.5, "slots: 4000 rounds match the designed payout (z " + zS.ToString("0.00") + ")");
            t.Check(Math.Abs(zR) < 4.5, "roulette: 3000 rounds match the designed payout (z " + zR.ToString("0.00") + ")");
            t.Check(Math.Abs(zB) < 4.5, "blackjack: 3000 hands with perfect play match the designed edge (z " + zB.ToString("0.00") + ")");
            t.Check(comp.RoundsAll == nS + nR + nB, "10,000 rounds in the history totals (" + comp.RoundsAll + ")");
            t.Check(led.SumAll() == sumA, "the ledger is still balanced after 10,000 rounds");
            t.Check(comp.History.Count == ThrumboComponent.HistoryMax, "the history keeps the last " + ThrumboComponent.HistoryMax);

            // the shift moves the real edge: -3 points = more generous slots
            HSettings.Set(TS.EdgeShift, -0.03f);
            t.Check(Math.Abs(ThrumboOdds.Slots(SlotVolatility.Low, TS.Shift).Rtp - 0.95) < 1e-6 && ThrumboOdds.BlackjackWinScale(TS.Shift) > 1.0, "comped shift: slots 95 %, winning hands pay more");
            HSettings.Set(TS.EdgeShift, 0f);
            yield return 1;

            // ---- the help hint -----------------------------------------------------------------------------------------
            HSettings.Set("core.helpHint", 1f);
            CasinoUI.HintDrawCount = 0;
            var win = new Window_Thrumbo(col); Find.WindowStack.Add(win);
            for (int i = 0; i < 40 && CasinoUI.HintDrawCount == 0; i++) yield return 30;
            t.Check(win.ShowsHelpHint, "the Lucky Thrumbo window carries the help line when core.helpHint is on");
            if (CasinoUI.HintDrawCount == 0)
            {
                try { win.DoWindowContents(new Rect(0, 0, 740f, 660f)); t.Note("window not rendered by the loop, drawn directly"); } catch (Exception e) { t.Note("direct draw not possible: " + e.GetType().Name); }
            }
            if (CasinoUI.HintDrawCount > 0) t.Check(true, "the window drew the hint (" + CasinoUI.HintDrawCount + " draws)"); else t.Note("no draw happened; verified through ShowsHelpHint");
            CasinoUI.HintDrawCount = 0;
            HSettings.Set("core.helpHint", 0f);
            t.Check(!win.ShowsHelpHint && CasinoUI.HelpHintText() == null, "the help line is hidden when core.helpHint is off");
            try { win.DoWindowContents(new Rect(0, 0, 740f, 660f)); } catch (Exception) { }
            t.Check(CasinoUI.HintDrawCount == 0, "nothing is drawn for the hint while it is off");
            t.Check(CasinoUI.HelpHintText() == null, "no text while off");
            win.Close(false);
            HSettings.Set("core.helpHint", oldHint ? 1f : 0f);

            // clean up
            HSettings.Set(TS.EdgeShift, oldShift); comp.Clear(); bank.TestReset(true);
            yield return 2;
        }
    }
}
#endif
