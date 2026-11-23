// SPDX-License-Identifier: MIT
// In-game suites of the casino animations and the casino life on the map (1.1.0, F29–F31, F35). Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Verse.Sound;
using Hearth.CasinoMath;
using Hearth.Casino.PlayerHouse;
using Hearth.Casino.Tests;
using Hearth.Testing;

namespace Hearth.Casino.Anim.Tests
{
    /// <summary>The Lucky Thrumbo and the practice tables play their rounds out: outcome first, money and history held back, skip, cues once, off switch.</summary>
    public sealed class AnimSuite : HearthTestSuite
    {
        public override string Name => "casino.anim";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 20000;

        static float clock;
        static void Advance(float s) { clock += s; }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t); var led = Ledger.Current;
            var comp = ThrumboComponent.Get(); comp.Clear();
            var bank = BankGameComp.Current; bank.TestReset(true);
            led.Post("world", BankGameComp.Colony, Money.FromSilver(50000), "test funding"); bank.InvalidateCache();
            float oldSpeed = HSettings.Get(CS.AnimSpeed); HSettings.Set(CS.AnimSpeed, 1f);
            clock = 1000f; AnimClock.Override = () => clock; AnimPrefs.ForceOn = true;
            Exception fail = null;
            var steps = new List<string>();
            try
            {
                var win = new Window_Thrumbo(col);
                // ---- sizes (F35): bigger than 1.0, never off screen ------------------------------------------------------
                var size = win.InitialSize;
                t.Check(size.x <= UI.screenWidth && size.y <= UI.screenHeight, "the Thrumbo window fits the screen (" + size + " on " + UI.screenWidth + "x" + UI.screenHeight + ")");
                t.Check(size.x >= Math.Min(820f, UI.screenWidth - 20f), "the Thrumbo window is at least 820 wide (" + size.x + ")");

                // ---- slots: settled at once, shown later ---------------------------------------------------------------------
                var funds0 = ThrumboHouse.Funds(new ThrumboSeat(map, col, PayFrom.Bank));
                var r = win.TestPlay("slots", "10", animate: true);
                t.Check(r != null && r.Ok && r.Reels != null, "a slot round is played: " + r?.Text + r?.Error);
                t.Check(win.Busy && win.Slots.Busy, "the reels are spinning");
                t.Check(bank.Balance == funds0 - r.Staked + r.Paid, "the money moved at once (outcome first)");
                t.Check(win.ShownFunds(PayFrom.Bank) == funds0 - Money.FromSilver(10), "the window shows the funds minus the stake while the reels spin (" + win.ShownFunds(PayFrom.Bank) + ")");
                t.Check(win.HeldEntry != null && ReferenceEquals(win.HeldEntry, comp.History.Last()), "the history line of the round is held back");
                t.Check(win.ResultLine == "HH.Thrumbo.Anim.Spinning".Translate().ToString(), "the result line waits: " + win.ResultLine);
                var a = win.Slots.Anim;
                a.Update();                                            // the first frame starts the clock
                for (int i = 0; i < 12; i++) { Advance(0.05f); a.Update(); }
                t.Check(!a.Stopped(0) && a.Elapsed > 0.5f, "after 0.6 s the first reel still runs (" + a.Elapsed + ")");
                for (int i = 0; i < 12; i++) { Advance(0.05f); a.Update(); }
                t.Check(a.Stopped(0) && !a.Stopped(2), "after 1.2 s the first reel has stopped, the third still runs");
                win.SkipAll();
                t.Check(!win.Busy && a.Done && a.Skipped, "a skip ends the spin");
                t.Check(win.Slots.Shown.SequenceEqual(r.Reels), "the reels show the drawn symbols (" + string.Join(",", win.Slots.Shown) + ")");
                for (int i = 0; i < 3; i++) t.Check(a.Strips[i][a.Strips[i].Length - 1] == r.Reels[i], "reel " + (i + 1) + " stops on its symbol");
                t.Check(win.ShownFunds(PayFrom.Bank) == ThrumboHouse.Funds(new ThrumboSeat(map, col, PayFrom.Bank), true), "after the spin the real funds show");
                t.Check(win.HeldEntry == null, "after the spin the history line shows");
                t.Check(win.ResultLine != null && win.ResultLine.StartsWith(r.Text), "after the spin the result line shows: " + win.ResultLine);

                // ---- every cue once, in time; a skip plays only the final ones ------------------------------------------------
                var r2 = win.TestPlay("slots", "5", animate: true); var a2 = win.Slots.Anim;
                int before = TableAnim.CuesFired, total = a2.Cues.Count;
                while (!a2.Done) { Advance(0.05f); a2.Update(); }
                for (int i = 0; i < 10; i++) { Advance(0.05f); a2.Update(); }
                t.Check(TableAnim.CuesFired - before == total, "a full spin fires each of its " + total + " cues exactly once (" + (TableAnim.CuesFired - before) + ")");
                var r3 = win.TestPlay("slots", "5", animate: true); var a3 = win.Slots.Anim;
                int finals = a3.Cues.Count(c => c.Final); before = TableAnim.CuesFired;
                win.SkipAll(); a3.Update(); a3.Update();
                t.Check(TableAnim.CuesFired - before == finals, "a skipped spin only plays its final cue(s) (" + (TableAnim.CuesFired - before) + " of " + finals + ")");

                // ---- roulette: the ball lands in the drawn pocket -----------------------------------------------------------------
                win.Game = ThrumboGame.Roulette;
                var rr = win.TestPlay("roulette", "5", animate: true);
                t.Check(rr != null && rr.Ok, "a roulette round is played: " + rr?.Text + rr?.Error);
                var wa = win.Wheel.Anim;
                t.Check(wa != null && wa.Plan.Pocket == rr.Pocket, "the spin is planned to pocket " + rr.Pocket);
                t.Check(win.Wheel.ShownPocket == -1, "while the ball rolls no pocket is shown");
                while (!wa.Done) { Advance(0.1f); wa.Update(); }
                t.Check(win.Wheel.ShownPocket == rr.Pocket && win.Wheel.Recent.Last() == rr.Pocket, "the ball rests in " + rr.Pocket + " and the last-numbers strip shows it");
                t.Check(wa.Plan.PocketUnderBall(1.0) == rr.Pocket, "geometry: the ball sits over the right pocket of the wheel");

                // ---- blackjack: cards fly, the hole card turns, the table keeps the hand -----------------------------------------
                win.Game = ThrumboGame.Blackjack;
                for (int tries = 0; tries < 6; tries++)
                {
                    var d = win.TestPlay("deal", "5", animate: true);
                    t.Check(d != null && d.Ok && d.PlayerCards != null && d.DealerCards != null, "a hand is dealt with its cards in the result");
                    win.SkipAll();
                    t.Check(win.Cards.CardsShown == d.PlayerCards.Count + d.DealerCards.Count, "the table shows all dealt cards (" + win.Cards.CardsShown + ")");
                    if (d.Done) { t.Check(win.Cards.Outcome == d.Outcome, "a natural is settled on the table: " + d.Outcome); continue; }
                    t.Check(win.Cards.VisibleTotal(0) == BlackjackSolver.HandTotal(new List<int> { d.DealerCards[0] }), "only the dealer's up card counts while the hole card is face down");
                    var s = win.TestPlay("stand", "5", animate: true);
                    t.Check(s != null && s.Ok && s.Done && s.Outcome != null, "stand settles the hand: " + s?.Text);
                    t.Check(win.Cards.Busy, "the dealer's play is animated");
                    t.Check(win.HeldEntry != null, "the settled hand's history line waits for the table");
                    win.SkipAll();
                    t.Check(win.Cards.CardsShown == s.PlayerCards.Count + s.DealerCards.Count, "after the dealer's play every card lies on the table (" + win.Cards.CardsShown + " of " + (s.PlayerCards.Count + s.DealerCards.Count) + ")");
                    t.Check(win.Cards.VisibleTotal(1) == BlackjackSolver.HandTotal(s.PlayerCards) && win.Cards.VisibleTotal(0) == BlackjackSolver.HandTotal(s.DealerCards), "the totals on the table match the hand");
                    t.Check(win.Cards.Outcome == s.Outcome && !win.Cards.Banner.NullOrEmpty(), "the result plate shows: " + win.Cards.Banner);
                    t.Check(ThrumboHouse.CurrentHand() == null && !win.Cards.Empty, "the hand is over but its cards stay on the table until the next deal");
                    break;
                }

                // ---- off switch: the final frame at once -------------------------------------------------------------------------
                AnimPrefs.ForceOn = false;
                win.Game = ThrumboGame.Slots;
                var ro = win.TestPlay("slots", "5", animate: true);
                t.Check(!win.Busy && win.Slots.Shown.SequenceEqual(ro.Reels) && win.HeldEntry == null, "animations off: the result shows at once");
                AnimPrefs.ForceOn = true;

                // ---- practice tables (Dialog_CasinoDemo) ----------------------------------------------------------------------------
                foreach (var def in new[] { CasinoDefOf.HH_SlotMachine, CasinoDefOf.HH_RouletteTable, CasinoDefOf.HH_BlackjackTable, CasinoDefOf.HH_PokerTable })
                {
                    var dev = Kit.Spawn(t, def, Rot4.South, col.Position); var dc = dev?.TryGetComp<CompCasinoGame>();
                    if (dc == null) { t.Check(false, "practice device " + def.defName); continue; }
                    var dlg = new Dialog_CasinoDemo(dc);
                    t.Check(dlg.InitialSize.x <= UI.screenWidth && dlg.InitialSize.y <= UI.screenHeight, def.defName + ": the practice window fits the screen");
                    double c0 = dlg.Chips;
                    dlg.TestSpin();
                    bool busy = dlg.Busy;
                    if (busy) t.Check(dlg.LastLine == null && dlg.ShownChips <= c0, def.defName + ": the practice result waits for the table (shown chips " + dlg.ShownChips + ")");
                    dlg.SkipAll();
                    t.Check(!dlg.Busy && Math.Abs(dlg.ShownChips - dlg.Chips) < 1e-9 && !dlg.LastLine.NullOrEmpty(), def.defName + ": after the table the line and chips show: " + dlg.LastLine);
                    if (def == CasinoDefOf.HH_PokerTable)
                    {
                        var deal = dlg.Poker.Deal;
                        t.Check(deal != null && deal.Board.Length == 5 && deal.Hole.Length == 4, "Rimdeck practice: four seats, five board cards");
                        if (deal != null) t.Check(!deal.Exact || PokerHands.Category(deal.WinnerScore) == deal.Category, "Rimdeck practice: the shown winner holds " + ("HH.Casino.Hand." + deal.Category).Translate());
                    }
                    dev.Destroy();
                }
            }
            catch (Exception e) { fail = e; }
            finally { AnimClock.Override = null; AnimPrefs.ForceOn = null; HSettings.Set(CS.AnimSpeed, oldSpeed); }
            t.Check(fail == null, "no exception: " + fail);
            comp.Clear(); bank.TestReset(true);
            yield return 1;
        }
    }

    /// <summary>Casino life on the map: sounds load, effecters, the device overlays, reactions with their throttle, the switches, the ambience.</summary>
    public sealed class FxSuite : HearthTestSuite
    {
        public override string Name => "casino.fx";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t);
            bool oldWorld = HSettings.GetBool(CS.WorldFx), oldReact = HSettings.GetBool(CS.Reactions), oldAmb = HSettings.GetBool(CS.Ambience);
            HSettings.Set(CS.WorldFx, 1f); HSettings.Set(CS.Reactions, 1f); HSettings.Set(CS.Ambience, 1f);
            CasinoFx.Clear();

            // ---- our sounds load on this engine (mod .ogg through AudioGrain_Clip) ------------------------------------------------
            var ours = DefDatabase<SoundDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("HH_Slot_") || d.defName.StartsWith("HH_World_") || d.defName.StartsWith("HH_Card_") || d.defName.StartsWith("HH_Casino_") || d.defName.StartsWith("HH_Roulette_") || d.defName.StartsWith("HH_Chips_")).ToList();
            t.Check(ours.Count >= 29, "the casino sound defs are loaded (" + ours.Count + ")");
            int clips = 0; var missing = new List<string>();
            foreach (var d in ours)
                foreach (var sub in d.subSounds)
                    foreach (var g in sub.grains)
                    {
                        if (!(g is AudioGrain_Clip ac)) continue;
                        if (ContentFinder<AudioClip>.Get(ac.clipPath, false) != null) clips++; else missing.Add(ac.clipPath);
                    }
            t.Check(missing.Count == 0, "every casino sound clip loads (" + clips + " clips" + (missing.Count > 0 ? "; missing " + string.Join(", ", missing) : "") + ")");
            foreach (var n in new[] { "HH_Slot_Lever", "HH_Card_Deal", "HH_Roulette_Spin" }) t.Check(Anim.CasinoSfx.Def(n)?.defName == n, n + " is our own sound, not the fallback");
            Exception playErr = null;
            try { Anim.CasinoSfx.Play(Anim.CasinoSfx.Chip); Anim.CasinoSfx.Play(Anim.CasinoSfx.ReelStop, 1.1f); } catch (Exception e) { playErr = e; }
            t.Check(playErr == null, "UI sounds play without errors");
            yield return 5;

            // ---- live drawing: the devices draw in real time, every rotation survives ---------------------------------------------
            t.Check(CasinoDefOf.HH_SlotMachine.drawerType == DrawerType.MapMeshAndRealTime && CasinoDefOf.HH_RouletteTable.drawerType == DrawerType.MapMeshAndRealTime, "casino devices draw their live parts every frame");
            var spawned = new List<Thing>();
            Exception drawErr = null;
            foreach (var def in new[] { CasinoDefOf.HH_SlotMachine, CasinoDefOf.HH_RouletteTable, CasinoDefOf.HH_BlackjackTable, CasinoDefOf.HH_PokerTable })
                foreach (var rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                {
                    var th = Kit.Spawn(t, def, rot, col.Position); if (th == null) continue; spawned.Add(th);
                    var cc = th.TryGetComp<CompCasinoGame>();
                    try
                    {
                        CasinoFx.NoteSlots(cc, 1, 2, 3, 0); CasinoFx.NoteRoulette(cc, 17, 1); CasinoFx.NoteCards(cc, 2); CasinoFx.DrawOverlay(cc); cc.PostDraw();
                        CasinoFx.NoteSlots(cc, 4, 4, 4, 50); CasinoFx.NoteRoulette(cc, 37, 2); CasinoFx.DrawOverlay(cc);   // a big win (marquee) and the double-zero wheel
                    }
                    catch (Exception e) { drawErr = drawErr ?? e; }
                }
            t.Check(drawErr == null, "the overlays draw in all four rotations: " + drawErr);
            // a minified or despawned device draws nothing and does not fail
            var lone = spawned.FirstOrDefault(); Exception despawnErr = null;
            if (lone != null) { var lc = lone.TryGetComp<CompCasinoGame>(); lone.DeSpawn(); try { CasinoFx.DrawOverlay(lc); } catch (Exception e) { despawnErr = e; } spawned.Remove(lone); lone.Destroy(); }
            t.Check(despawnErr == null, "a despawned device draws nothing");
            t.Check(lone == null || CasinoFx.ShowOf(lone.TryGetComp<CompCasinoGame>()) == null, "a despawned device's show is dropped (no stale entries)");
            t.Check(ContentFinder<Texture2D>.Get(CasinoFxTex.MiniWheel, false) != null && ContentFinder<Texture2D>.Get(CasinoFxTex.MiniWheelUS, false) != null, "both map wheels exist: single zero and double zero");
            var usTable = spawned.FirstOrDefault(x => x.def == CasinoDefOf.HH_RouletteTable);
            var usShow = usTable != null ? CasinoFx.ShowOf(usTable.TryGetComp<CompCasinoGame>()) : null;
            t.Check(usShow != null && usShow.Zeros == 2 && usShow.Pocket == 37 && usShow.Plan != null, "a double-zero round is shown on the double-zero wheel (pocket 00)");
            foreach (var x in spawned) if (!x.Destroyed) x.Destroy();
            spawned.Clear();
            yield return 5;

            // ---- the engine notes rounds; effecters per game -------------------------------------------------------------------------
            var machine = Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, col.Position); spawned.Add(machine); Kit.Power(machine);
            var mc = machine.TryGetComp<CompCasinoGame>();
            Kit.ResetGambler(col);
            var cs = CasinoComponent.Get();
            var s = new GamblerSession { Pawn = col, Device = machine, Kind = SessionKind.Colonist, StartTick = Find.TickManager.TicksGame, EndTick = Find.TickManager.TicksGame + 99999, HouseAccount = "chips:test", BuyIn = Money.FromSilver(1000), Purse = Money.FromSilver(1000), StakeShare = 0.01f };
            cs.Sessions.Add(s);
            var list = new List<GamblerSession> { s };
            for (int i = 0; i < 5; i++) CasinoEngine.RunRound(mc, list);
            var show = CasinoFx.ShowOf(mc);
            t.Check(show != null && show.Reels != null && show.Seq == 5 && show.RoundTick == Find.TickManager.TicksGame, "the slot machine shows the last reels of its rounds (" + (show?.Seq ?? 0) + " rounds)");
            t.Check(CasinoFx.PlayEffecter(mc) == null, "slots: the machine itself rattles (no effecter on the player)");
            var table = Kit.Spawn(t, CasinoDefOf.HH_RouletteTable, Rot4.South, col.Position); spawned.Add(table);
            var bj = Kit.Spawn(t, CasinoDefOf.HH_BlackjackTable, Rot4.South, col.Position); spawned.Add(bj);
            t.Check(CasinoFx.PlayEffecter(table.TryGetComp<CompCasinoGame>()) == CasinoFxDefOf.HH_Effecter_PlayRoulette, "roulette: chips effecter");
            t.Check(CasinoFx.PlayEffecter(bj.TryGetComp<CompCasinoGame>()) == CasinoFxDefOf.HH_Effecter_PlayCards && CasinoFx.CroupierEffecter(bj.TryGetComp<CompCasinoGame>()) == CasinoFxDefOf.HH_Effecter_Croupier, "card tables: cards for the players, shuffling for the croupier");

            // ---- reactions: throttled per pawn and per map -------------------------------------------------------------------------
            Find.CameraDriver.SetRootPosAndSize(col.Position.ToVector3Shifted(), 14f);
            yield return 2;
            int r0 = CasinoFx.Reactions;
            t.Check(CasinoFx.React(col, mc, FxReaction.SmallWin), "a small win shows a coin above the player");
            t.Check(!CasinoFx.React(col, mc, FxReaction.SmallWin), "a second reaction within 300 ticks is skipped");
            t.Check(CasinoFx.React(col, mc, FxReaction.BigWin), "a big win always shows");
            t.Check(CasinoFx.Reactions - r0 == 2, "two reactions counted");
            // big wins skip the per-pawn pause but not the per-map cap: a flood of jackpots in one second shows at most the cap
            int bigs = 0;
            for (int i = 0; i < 12; i++) if (CasinoFx.React(col, mc, FxReaction.Jackpot)) bigs++;
            t.Check(bigs > 0 && bigs <= CasinoFx.MapBigReactionsPerSecond - 2, "big wins are capped per map and second (" + bigs + " more shown, cap " + CasinoFx.MapBigReactionsPerSecond + ")");
            yield return 320;
            t.Check(CasinoFx.React(col, mc, FxReaction.Loss), "after 300 ticks the next reaction shows (a groan)");
            // results queue the reaction until the table shows them
            yield return 320;
            int q0 = CasinoFx.Reactions;
            int qn = CasinoFx.Queued;
            CasinoFx.NoteResult(mc, s, Money.FromSilver(1), Money.FromSilver(30));
            t.Check(CasinoFx.Reactions == q0 && CasinoFx.Queued == qn + 1, "a big slot win is not shown before the reels stop (queued " + CasinoFx.Queued + ")");
            yield return CasinoFx.SlotShowTicks + 5;
            // (the machine keeps playing the session on its own, so more reactions may have come)
            t.Check(CasinoFx.Reactions >= q0 + 1, "…and is shown when they have stopped (reactions " + (CasinoFx.Reactions - q0) + ", queued " + CasinoFx.Queued + ", spawned " + col.Spawned + ")");

            // ---- the switches -------------------------------------------------------------------------------------------------------
            HSettings.Set(CS.Reactions, 0f);
            yield return 320;
            t.Check(!CasinoFx.React(col, mc, FxReaction.BigWin), "reactions off: nothing shows");
            HSettings.Set(CS.Reactions, 1f);
            HSettings.Set(CS.WorldFx, 0f);
            int seq = CasinoFx.ShowOf(mc).Seq;
            CasinoEngine.RunRound(mc, list);
            t.Check(CasinoFx.ShowOf(mc).Seq == seq && CasinoFx.PlayEffecter(bj.TryGetComp<CompCasinoGame>()) == null, "map effects off: the tables show nothing new and have no effecters");
            HSettings.Set(CS.WorldFx, 1f);
            cs.Sessions.Remove(s);

            // ---- ambience: a busy casino room hums, an empty one is quiet ---------------------------------------------------------------
            var centre = t.FreeCellNearCenter(30);
            Hearth.Wellness.Tests.WT.Clear(map, centre, 6);
            Hearth.Wellness.Tests.WT.Room(map, centre, 9);
            yield return 5;
            var rt = Hearth.Wellness.Tests.WT.Spawn(map, CasinoDefOf.HH_PokerTable, centre); spawned.Add(rt); Kit.Power(rt);
            var cols = map.mapPawns.FreeColonistsSpawned.Take(2).ToList();
            var amb = new List<GamblerSession>();
            foreach (var p in cols) { var gs = new GamblerSession { Pawn = p, Device = rt, Kind = SessionKind.Colonist, StartTick = Find.TickManager.TicksGame, EndTick = Find.TickManager.TicksGame + 99999, HouseAccount = "chips:test", BuyIn = Money.FromSilver(100), Purse = Money.FromSilver(100) }; cs.Sessions.Add(gs); amb.Add(gs); }
            Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 14f);
            yield return 260;
            t.Check(cols.Count < 2 || CasinoFx.AmbiencePlaying, "two players in a closed casino room: the ambience plays (" + CasinoFx.AmbienceStarts + " starts)");
            foreach (var gs in amb) cs.Sessions.Remove(gs);
            yield return 260;
            t.Check(!CasinoFx.AmbiencePlaying, "an empty room is quiet again");
            int activeSessions = cs.Sessions.Count(x => x.Pawn != null && !x.Done);
            var tracked = CasinoFx.TrackedCounts;
            t.Check(tracked.streaks <= activeSessions && tracked.reactions <= map.mapPawns.AllPawnsSpawnedCount, "the transient lists are pruned (" + tracked.streaks + " loss streaks for " + activeSessions + " players, " + tracked.reactions + " reaction times, " + tracked.shows + " shows)");

            foreach (var x in spawned) if (!x.Destroyed) x.Destroy();
            HSettings.Set(CS.WorldFx, oldWorld ? 1f : 0f); HSettings.Set(CS.Reactions, oldReact ? 1f : 0f); HSettings.Set(CS.Ambience, oldAmb ? 1f : 0f);
            yield return 2;
        }
    }
}
#endif
