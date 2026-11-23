// SPDX-License-Identifier: MIT
// In-game suite of the guest seats of games (F17, 1.1.0). Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Testing;

namespace Hearth.Casino.Tests
{
    public sealed class SeatsSuite : HearthTestSuite
    {
        public override string Name => "casino.seats";
        public override ModuleId Module => ModuleId.Casino;

        static Thing Chair(Map map, IntVec3 c, string def = "DiningChair")
        {
            var d = ThingDef.Named(def);
            var ch = ThingMaker.MakeThing(d, GenStuff.DefaultStuffFor(d)); ch.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(ch, c, map, Rot4.North);
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            // --- the plan, pure
            var cands = new List<IntVec3>();
            for (int x = 0; x < 3; x++) { cands.Add(new IntVec3(x, 0, -1)); cands.Add(new IntVec3(x, 0, 2)); }
            cands.Add(new IntVec3(-1, 0, 0)); cands.Add(new IntVec3(-1, 0, 1)); cands.Add(new IntVec3(3, 0, 0)); cands.Add(new IntVec3(3, 0, 1));
            var none = cands.Select(_ => -1f).ToList();
            var plan = CasinoSeats.Plan(cands, none, 4);
            t.Check(plan.Count == 4 && plan.Distinct().Count() == 4 && plan.All(cands.Contains), "four distinct seats out of ten cells");
            int minD = int.MaxValue;
            for (int i = 0; i < plan.Count; i++) for (int j = i + 1; j < plan.Count; j++) minD = Math.Min(minD, (plan[i] - plan[j]).LengthHorizontalSquared);
            t.Check(minD >= 4, "players are spread around the table (closest pair " + minD + ")");
            t.Check(CasinoSeats.Plan(cands, none, 4).SequenceEqual(plan), "the plan is deterministic");
            var withChairs = none.ToList(); withChairs[5] = 0.7f; withChairs[2] = 0.9f;
            var plan2 = CasinoSeats.Plan(cands, withChairs, 4);
            t.Check(plan2[0] == cands[2] && plan2[1] == cands[5], "chairs come first, the most comfortable first");
            t.Check(CasinoSeats.Plan(cands, none, 20).Count == cands.Count && CasinoSeats.Plan(cands, none, 0).Count == 0, "never more seats than cells");

            // --- placed tables in all four rotations: what is drawn is what is used
            var around = t.FreeCellNearCenter(30);
            foreach (var def in new[] { CasinoDefOf.HH_RouletteTable, CasinoDefOf.HH_BlackjackTable, CasinoDefOf.HH_PokerTable })
                foreach (var rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                {
                    var table = Kit.Spawn(t, def, rot, around);
                    var comp = table.TryGetComp<CompCasinoGame>();
                    CasinoSeats.MarkDirty(table);
                    var seats = CasinoSeats.SeatCells(comp);
                    var play = comp.PlayCells().ToList();
                    t.Check(seats.Count == Math.Min(comp.Props.seats, play.Count) && seats.All(play.Contains) && !seats.Contains(comp.CroupierCell), $"{def.defName} {rot}: {seats.Count} seats, all next to the table, none on the croupier's cell");
                    var ghost = CasinoSeats.PlanAt(def, table.Position, table.Rotation, map, out var croupier);
                    t.Check(ghost.OrderBy(c => c.x).ThenBy(c => c.z).SequenceEqual(seats.OrderBy(c => c.x).ThenBy(c => c.z)) && croupier == comp.CroupierCell, $"{def.defName} {rot}: the placement preview shows exactly these seats and the croupier");
                    table.Destroy();
                }

            // --- chairs: the plan takes them, the inspect line counts them, a seated guest stakes more
            var tab = Kit.Spawn(t, CasinoDefOf.HH_PokerTable, Rot4.North, around);
            var tc = tab.TryGetComp<CompCasinoGame>();
            var free = tc.PlayCells().ToList();
            var chairCells = free.Where(c => !CasinoSeats.SeatCells(tc).Contains(c)).Take(2).ToList();
            if (chairCells.Count < 2) chairCells = free.Take(2).ToList();
            var chairs = chairCells.Select(c => Chair(map, c, "Armchair")).ToList();
            CasinoSeats.MarkDirty(tab);
            var seated = CasinoSeats.SeatCells(tc);
            t.Check(chairCells.All(seated.Contains) && CasinoSeats.ChairsAt(tc) == 2, "chairs become seats even where the plan had nobody before");
            string insp = tab.GetInspectString();
            t.Check(insp.Contains("Guest seats") && insp.Contains("2 with a chair"), "the table tells about its seats: " + insp.Replace("\n", " | "));
            t.Check(CasinoSeats.SpendBonus(0f) == 0f && Math.Abs(CasinoSeats.SpendBonus(1f) - HSettings.Get(CasinoSeats.ComfortSpendKey)) < 0.0001f && CasinoSeats.SpendBonus(2f) == CasinoSeats.SpendBonus(1f), "spending bonus: 0 standing, the setting at comfort 100 %, capped");

            var pawn = TestSetup.Colonist(map);
            if (pawn != null)
            {
                pawn.jobs.StopAll(); pawn.DeSpawn(); GenSpawn.Spawn(pawn, chairCells[0], map);
                float comfort = CasinoSeats.SeatComfort(pawn);
                t.Check(comfort > 0.5f, "an armchair is comfortable (" + comfort.ToString("0.00") + ")");
                int now = Find.TickManager.TicksGame;
                var s = new GamblerSession { Kind = SessionKind.Guest, StakeShare = 0.10f, EndTick = now + 2000, StartTick = now };
                CasinoSeats.ApplyComfort(s, pawn);
                t.Check(s.StakeShare > 0.10f && s.StakeShare <= 0.10f * (1f + CasinoSeats.SpendBonus(1f)) + 0.0001f, "seated guest stakes more: " + s.StakeShare.ToString("0.000"));
                t.Check(s.EndTick > now + 2000, "and stays longer (" + (s.EndTick - now) + " ticks)");
                var colonistSession = new GamblerSession { Kind = SessionKind.Colonist, StakeShare = 0.012f, EndTick = now + 9000 };
                CasinoSeats.ApplyComfort(colonistSession, pawn);
                t.Check(colonistSession.StakeShare == 0.012f, "colonists playing with house chips are not affected");
                var standCell = free.First(c => c.GetEdifice(map) == null);
                pawn.DeSpawn(); GenSpawn.Spawn(pawn, standCell, map);
                var s2 = new GamblerSession { Kind = SessionKind.Guest, StakeShare = 0.10f, EndTick = now + 2000 };
                CasinoSeats.ApplyComfort(s2, pawn);
                t.Check(s2.StakeShare == 0.10f && s2.EndTick == now + 2000, "a standing guest plays as before");
            }

            // --- drawing: preview and selection overlay run without errors
            new PlaceWorker_CasinoSeats().DrawGhost(CasinoDefOf.HH_RouletteTable, tab.Position + new IntVec3(6, 0, 0), Rot4.East, Color.white);
            Find.Selector.ClearSelection(); Find.Selector.Select(tab, false, false);
            yield return 30;
            Find.Selector.ClearSelection();
            t.Check(CasinoDefOf.HH_PokerTable.PlaceWorkers.Any(p => p is PlaceWorker_CasinoSeats) && CasinoDefOf.HH_SlotMachine.PlaceWorkers.Any(p => p is PlaceWorker_CasinoSeats), "tables and slot machines show their seats while placing");
            foreach (var c in chairs) if (!c.Destroyed) c.Destroy();
            tab.Destroy();

            // --- the casino's setup row: chairs at the game seats
            var room = Hearth.ShopsFood.VenueTestKit.Build(map, 7);
            var sign = Hearth.ShopsFood.VenueTestKit.SpawnSign(map, room, Hearth.ShopsFood.HHVenueDefOf.HH_VenueSign, CasinoDefOf.HH_Venue_Casino);
            var slot = GenSpawn.Spawn(ThingMaker.MakeThing(CasinoDefOf.HH_SlotMachine), room.Inner.CenterCell, map, Rot4.South);
            slot.SetFaction(Faction.OfPlayer); room.Things.Add(slot);
            yield return 5;
            Venues.MarkDirty(map); Hearth.ShopsFood.VenueSetup.MarkDirty(sign);
            var v = Hearth.ShopsFood.VenueApi.VenueOfSign(map, sign);
            var rep = Hearth.ShopsFood.VenueSetup.Report(v, 0);
            var row = rep.Row("chairs");
            t.Check(rep.Row("game")?.Ok == true && row != null && row.Need == 1 && row.Have == 0, $"casino with a slot: game ok, chairs 0/1 ({row?.Have}/{row?.Need})");
            t.Check(rep.Row("power") != null && !rep.Row("power").Blocking, "an unpowered slot is an open point, it does not block the casino");
            var seatCell = slot.InteractionCell;
            t.Check(seatCell == room.Inner.CenterCell + IntVec3.South, "a slot facing south is played from the south (art and seat agree)");
            room.Things.Add(Chair(map, seatCell, "Stool"));
            CasinoSeats.MarkDirty(slot); Hearth.ShopsFood.VenueSetup.MarkDirty(sign);
            row = Hearth.ShopsFood.VenueSetup.Report(v, 0).Row("chairs");
            t.Check(row != null && row.Ok, "a stool at the slot fills the chairs row");
            Hearth.ShopsFood.VenueTestKit.Teardown(map, room);
        }
    }
}
#endif
