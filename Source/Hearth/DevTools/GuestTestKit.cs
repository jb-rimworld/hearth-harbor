// SPDX-License-Identifier: MIT
// Shared visitor set-up for in-game suites (built only with HH_TESTS).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.ShopsFood;
using RimWorld;
using Verse;

namespace Hearth.Testing
{
    /// <summary>
    /// The vanilla VisitorGroup incident is not reliable on its own: it picks a faction by the outdoor temperature range, goodwill and tech level, and needs a free
    /// map-edge entry cell, so on some random quicktest maps it spawns nothing (or a group of a faction that is hostile by now). Suites that need "a visitor"
    /// call this instead: a friendly faction is forced (relations repaired if need be), the incident is retried with the next faction, and the result is returned.
    /// </summary>
    static class GuestTestKit
    {
        /// <summary>Spawns at least <paramref name="minCount"/> vanilla visitors (a group may be bigger) and returns the pawns that are now vanilla visitors on the map.</summary>
        /// <param name="spawn">Where the group enters; default is a random map-edge cell that can reach the colony.</param>
        public static List<Pawn> SpawnVisitors(Map map, int minCount = 1, IntVec3 spawn = default, int maxTries = 8)
        {
            var factions = VenueTestKit.Friendly(4, false);
            var made = new List<Pawn>();
            for (int i = 0; i < maxTries && made.Count < minCount; i++)
            {
                var parms = new IncidentParms
                {
                    target = map, forced = true, sendLetter = false, points = 120f,
                    faction = factions.Count > 0 ? factions[i % factions.Count] : null,
                    spawnCenter = spawn.IsValid ? spawn : EntryCell(map),
                    storeGeneratedNeutralPawns = new List<Pawn>()
                };
                try { IncidentDefOf.VisitorGroup.Worker.TryExecute(parms); }
                catch (Exception e) { HLog.Warn("Test", "VisitorGroup incident threw (try " + (i + 1) + "): " + e.Message); }
                foreach (var p in parms.storeGeneratedNeutralPawns) if (p.Spawned && GuestApi.IsVanillaVisitor(p) && !made.Contains(p)) made.Add(p);
                if (made.Count < minCount) HLog.Warn("Test", "VisitorGroup try " + (i + 1) + " gave " + made.Count + "/" + minCount + " visitors (faction " + (parms.faction?.Name ?? "none") + ")");
            }
            return made;
        }

        /// <summary>The first visitor of <see cref="SpawnVisitors"/>, or null.</summary>
        public static Pawn SpawnVisitor(Map map) => SpawnVisitors(map, 1).FirstOrDefault();

        /// <summary>A walkable map-edge cell connected to the colony (the vanilla entry cell search can fail on maps with blocked edges).</summary>
        public static IntVec3 EntryCell(Map map)
        {
            if (RCellFinder.TryFindRandomPawnEntryCell(out var c, map, CellFinder.EdgeRoadChance_Neutral)) return c;
            if (CellFinder.TryFindRandomEdgeCellWith(x => x.Standable(map) && !x.Fogged(map) && map.reachability.CanReachColony(x), map, CellFinder.EdgeRoadChance_Neutral, out c)) return c;
            return map.Center;
        }

        /// <summary>Runs a suite body with the vanilla-visitor guest brain (GuestFallback) switched off: visitors then stay where the suite put them instead of walking off to any
        /// activity of any module, which made position and reservation checks depend on luck. Switched back on when the suite ends, however it ends.</summary>
        public static IEnumerable<int> WithoutGuestBrain(IEnumerable<int> body)
        {
            bool was = GuestFallback.Enabled; GuestFallback.Enabled = false;
            try { foreach (var x in body) yield return x; }
            finally { GuestFallback.Enabled = was; }
        }

        /// <summary>Gives vanilla visitors a fixed purse and wealth so purchases do not depend on the random budget (60 to 150 silver) and price tolerance of the group.</summary>
        public static void FundVisitors(IEnumerable<Pawn> visitors, int silver = 300, int wealth = 60)
        {
            foreach (var v in visitors) { var gi = GuestApi.Info(v); if (gi != null) { gi.Budget = Money.FromSilver(silver); gi.Wealth = wealth; } }
        }
    }
}
#endif
