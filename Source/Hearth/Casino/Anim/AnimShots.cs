// SPDX-License-Identifier: MIT
// Screenshots of the casino animations for looking at them by eye (explicit only: tools/run_test.sh casino.anim.shots <label>).
// Frames go to /private/tmp/claude-501/hh_tests/anim_shots.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;
using Hearth.Casino.PlayerHouse;
using Hearth.Casino.Tests;
using Hearth.Testing;
using Hearth.Shell.Tests;

namespace Hearth.Casino.Anim.Tests
{
    public sealed class AnimShots : HearthTestSuite
    {
        public override string Name => "casino.anim.shots";
        public override ModuleId Module => ModuleId.Casino;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 900000;
        const string OutDir = "/private/tmp/claude-501/hh_tests/anim_shots";
        static float clock = 500f;
        readonly List<(string file, string name)> renames = new List<(string, string)>();

        void Tidy()
        {
            try { Messages.Clear(); } catch { }
            try { foreach (var l in Find.LetterStack.LettersListForReading.ToList()) Find.LetterStack.RemoveLetter(l); } catch { }
            try { foreach (var w in Find.WindowStack.Windows.Where(w => w.GetType().Name.StartsWith("EditWindow")).ToList()) Find.WindowStack.TryRemove(w, false); } catch { }
        }

        bool noRender;
        IEnumerable<int> Shot(TestContext t, string name)
        {
            if (noRender) yield break;     // the game window does not render (hidden or occluded): no point in waiting for every shot
            Tidy();
            // wait real time, not ticks: the test game runs many ticks per rendered frame
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.6f) yield return 1;
            string path = Path.Combine(OutDir, name + ".png");
            string found = null;
            for (int attempt = 0; attempt < 2 && found == null; attempt++)
            {
                try { ScreenCapture.CaptureScreenshot(path); } catch (Exception e) { t.Note("capture failed: " + e.Message); }
                t0 = Time.realtimeSinceStartup;
                while (found == null && Time.realtimeSinceStartup - t0 < 3f) { yield return 1; if (File.Exists(path)) found = path; }
            }
            if (found == null) { t.Note("no screenshot for " + name + " (Thrumbo window draws so far: " + Window_Thrumbo.DrawCount + ", frames " + Time.frameCount + ")"); noRender = true; yield break; }
            renames.Add((found, name));
        }

        static void Freeze(TableAnim a, float at) { if (a == null) return; a.Elapsed = Mathf.Min(a.Duration, at); }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t);
            try { if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true); } catch { }
            Directory.CreateDirectory(OutDir);
            var comp = ThrumboComponent.Get(); comp.Clear();
            var bank = BankGameComp.Current; bank.TestReset(true);
            Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(12000), "shots"); bank.InvalidateCache();
            HSettings.Set("core.helpHint", 1f);
            AnimPrefs.ForceOn = true;
            AnimClock.Override = () => clock;
            var seat = new ThrumboSeat(map, col, PayFrom.Bank);
            for (int i = 0; i < 4; i++) ThrumboHouse.PlaySlots(seat, Money.FromSilver(10), SlotVolatility.Medium);
            ThrumboHouse.PlayRoulette(seat, Money.FromSilver(5), RouletteBet.Red());

            var win = UiTestUtil.NoPause(new Window_Thrumbo(col)); win.HowToOpen = false; Find.WindowStack.Add(win); yield return 20;
            // slots: idle, spinning, stopping, a jackpot plate
            win.Game = ThrumboGame.Slots;
            foreach (var w in Shot(t, "01_slots_idle")) yield return w;
            win.TestPlay("slots", "25", animate: true); Freeze(win.Slots.Anim, 0.2f);
            foreach (var w in Shot(t, "02_slots_lever")) yield return w;
            Freeze(win.Slots.Anim, 0.85f);
            foreach (var w in Shot(t, "03_slots_spinning")) yield return w;
            Freeze(win.Slots.Anim, 1.6f);
            foreach (var w in Shot(t, "04_slots_stopping")) yield return w;
            AnimClock.Override = null; win.SkipAll(); yield return 4;

            foreach (var w in Shot(t, "05_slots_result")) yield return w;
            AnimClock.Override = () => clock;
            // a jackpot (display only: the view is told a result, nothing is played)
            win.Slots.Start(new[] { 6, 6, 6 }, 800, 99); AnimClock.Override = null;
            Freeze(win.Slots.Anim, win.Slots.Anim.WinAt - 0.05f);
            { float w0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - w0 < 0.9f) yield return 1; }
            foreach (var w in Shot(t, "06_slots_jackpot")) yield return w;
            AnimClock.Override = () => clock;
            win.SkipAll();
            // how to play
            win.HowToOpen = true; yield return 4;
            foreach (var w in Shot(t, "07_slots_howto")) yield return w;
            win.HowToOpen = false;
            // roulette
            win.Game = ThrumboGame.Roulette; yield return 4;
            win.TestPlay("roulette", "5", animate: true);
            Freeze(win.Wheel.Anim, 1.2f);
            foreach (var w in Shot(t, "08_roulette_spin")) yield return w;
            Freeze(win.Wheel.Anim, RouletteSpinAnim.SpinSeconds * 0.7f);
            foreach (var w in Shot(t, "09_roulette_drop")) yield return w;
            AnimClock.Override = null; win.SkipAll(); yield return 4;

            foreach (var w in Shot(t, "10_roulette_landed")) yield return w;
            AnimClock.Override = () => clock;
            // blackjack
            win.Game = ThrumboGame.Blackjack; yield return 4;
            var d = win.TestPlay("deal", "20", animate: true);
            Freeze(win.Cards.GetType().GetField("anim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(win.Cards) as TableAnim, 0.7f);
            foreach (var w in Shot(t, "11_blackjack_dealing")) yield return w;
            win.SkipAll(); yield return 4;
            foreach (var w in Shot(t, "12_blackjack_open")) yield return w;
            AnimClock.Override = null; if (ThrumboHouse.CurrentHand() != null) { win.TestPlay("stand", "20", animate: true); win.SkipAll(); }
            yield return 4;

            foreach (var w in Shot(t, "13_blackjack_result")) yield return w;
            AnimClock.Override = () => clock;
            win.Close(false); yield return 10;

            // the practice tables
            foreach (var def in new[] { CasinoDefOf.HH_SlotMachine, CasinoDefOf.HH_RouletteTable, CasinoDefOf.HH_BlackjackTable, CasinoDefOf.HH_PokerTable })
            {
                var dev = Kit.Spawn(t, def, Rot4.South, col.Position); var dc = dev.TryGetComp<CompCasinoGame>();
                var dlg = UiTestUtil.NoPause(new Dialog_CasinoDemo(dc)); Find.WindowStack.Add(dlg); yield return 10;
                dlg.TestSpin();
                if (def == CasinoDefOf.HH_PokerTable)
                {
                    var f = dlg.Poker.GetType().GetField("anim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Freeze(f?.GetValue(dlg.Poker) as TableAnim, 3.2f);
                    foreach (var w in Shot(t, "14_demo_poker_board")) yield return w;
                }
            AnimClock.Override = null; dlg.SkipAll(); yield return 6;

                foreach (var w in Shot(t, "15_demo_" + def.defName)) yield return w;
                AnimClock.Override = () => clock;
                dlg.Close(false); dev.Destroy(); yield return 5;
            }

            // the stake dialog
            var beacon = Kit.Spawn(t, ThingDefOf.OrbitalTradeBeacon, Rot4.North, t.FreeCellNearCenter(20)); Kit.Power(beacon);
            var cells = Building_OrbitalTradeBeacon.TradeableCellsAround(beacon.Position, map).Where(c => c != beacon.Position && c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstBuilding(map) == null).ToList();
            int k = 0;
            foreach (var td in new[] { ThingDefOf.Steel, ThingDefOf.ComponentIndustrial, ThingDefOf.MedicineIndustrial, ThingDefOf.Plasteel, ThingDefOf.Gold })
            { var x = ThingMaker.MakeThing(td); x.stackCount = Math.Min(td.stackLimit, 40); GenSpawn.Spawn(x, cells[k++], map); x.SetForbidden(true, false); }
            HSettings.Set(TS.AllIn, 1f);
            var sw = Window_ThrumboStakes.Open(map, PayFrom.Bank);
            if (sw != null)
            {
                sw.forcePause = false; sw.absorbInputAroundWindow = false;
                var st = sw.Offer.Lines.FirstOrDefault(l => l.Tradeable.ThingDef == ThingDefOf.Steel); if (st != null) { st.Count = 30; st.Buffer = "30"; }
                yield return 20;
                foreach (var w in Shot(t, "16_stakes")) yield return w;
                sw.Close(false);
            }

            // on the map: a small casino with players
            AnimClock.Override = null;
            var centre = t.FreeCellNearCenter(30);
            Hearth.Wellness.Tests.WT.Clear(map, centre, 7);
            var slot = Hearth.Wellness.Tests.WT.Spawn(map, CasinoDefOf.HH_SlotMachine, centre + new IntVec3(-4, 0, 2), Rot4.South); Kit.Power(slot);
            var rou = Hearth.Wellness.Tests.WT.Spawn(map, CasinoDefOf.HH_RouletteTable, centre + new IntVec3(1, 0, 2), Rot4.South); Kit.Power(rou);
            var bjt = Hearth.Wellness.Tests.WT.Spawn(map, CasinoDefOf.HH_BlackjackTable, centre + new IntVec3(1, 0, -3), Rot4.South); Kit.Power(bjt);
            var cs = CasinoComponent.Get(); var pawns = map.mapPawns.FreeColonistsSpawned.Take(3).ToList();
            var devs = new Thing[] { slot, rou, bjt }; var sess = new List<GamblerSession>();
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i]; var dvc = devs[i].TryGetComp<CompCasinoGame>(); var cell = dvc.PlayCells().FirstOrDefault();
                if (cell.IsValid) { p.Position = cell; p.Notify_Teleported(false); }
                var gs = new GamblerSession { Pawn = p, Device = devs[i], Kind = SessionKind.Colonist, StartTick = Find.TickManager.TicksGame, EndTick = Find.TickManager.TicksGame + 99999, HouseAccount = "chips:shots", BuyIn = Money.FromSilver(1000), Purse = Money.FromSilver(1000), StakeShare = 0.01f };
                cs.Sessions.Add(gs); sess.Add(gs);
            }
            Find.CameraDriver.JumpToCurrentMapLoc(centre); Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 9f);
            yield return 30;
            t.Note("camera at " + Find.CameraDriver.MapPosition + ", casino at " + centre + ", devices spawned: " + devs.Count(d => d != null && d.Spawned));
            for (int i = 0; i < sess.Count; i++) CasinoEngine.RunRound(devs[i].TryGetComp<CompCasinoGame>(), new List<GamblerSession> { sess[i] });
            yield return 12;
            { float w0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - w0 < 2.5f) { Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 9f); yield return 1; } }
            foreach (var w in Shot(t, "17_map_round")) yield return w;
            yield return 200;
            Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 9f);
            foreach (var w in Shot(t, "18_map_after")) yield return w;
            if (pawns.Count > 0) CasinoFx.React(pawns[0], slot.TryGetComp<CompCasinoGame>(), FxReaction.BigWin);
            yield return 20;
            Find.CameraDriver.SetRootPosAndSize(centre.ToVector3Shifted(), 9f);
            foreach (var w in Shot(t, "19_map_bigwin")) yield return w;
            foreach (var gs in sess) cs.Sessions.Remove(gs);

            t.Note("screenshots in " + OutDir + ": " + renames.Count);
            t.Check(renames.Count > 10, "screenshots taken (" + renames.Count + ")");
            AnimClock.Override = null; AnimPrefs.ForceOn = null; comp.Clear(); bank.TestReset(true);
        }
    }
}
#endif
