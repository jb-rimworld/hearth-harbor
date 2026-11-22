// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// The one "guest brain" for vanilla visitor groups (KONZEPT 5: vanilla visitors act as day visitors, with or without the Inn module).
    /// Every 90 ticks it offers idle visitors that are chilling at their spot an activity of any enabled module via
    /// <see cref="GuestActivities.TryPick"/> (shops, food, casino, spa, city, underworld …).
    /// Inn-booked guests are never touched here: their own think tree (Hearth.Guests.JobGiver_HH_GuestActivity) asks the activities.
    /// Ticked by <see cref="GuestFallbackModule"/> (always on, no save data). Owner: Inn module.
    /// </summary>
    public static class GuestFallback
    {
        /// <summary>Set to false if another mod already drives vanilla visitors through GuestActivities.</summary>
        public static bool Enabled = true;
        public const int Interval = 90;
        static HashSet<JobDef> idleJobs;
        static readonly Dictionary<int, int> nextOffer = new Dictionary<int, int>();
        static readonly Dictionary<int, int> lastRun = new Dictionary<int, int>();
        /// <summary>Number of activity jobs started for vanilla visitors this game (tests, diagnosis).</summary>
        public static int JobsStarted;
        /// <summary>Last job started by the fallback (tests).</summary>
        public static Job LastJob;

        static GuestFallback() { HearthGame.OnGameInit += () => { nextOffer.Clear(); lastRun.Clear(); JobsStarted = 0; LastJob = null; }; }

        static bool Idle(Pawn p)
        {
            if (p.CurJob == null) return true;
            if (idleJobs == null) idleJobs = new HashSet<JobDef> { JobDefOf.Wait_Wander, JobDefOf.GotoWander, JobDefOf.Wait, JobDefOf.Wait_MaintainPosture };
            return idleJobs.Contains(p.CurJobDef) && !p.CurJob.playerForced;
        }

        /// <summary>True for a pawn this fallback may drive: a vanilla visitor or trader, not an Inn guest, chilling at the group's spot.</summary>
        public static bool Drives(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed) return false;
            if (Hearth.Guests.GuestRegistry.IsRegisteredGuest(p)) return false; // the Inn's own brain
            if (!GuestApi.IsVanillaVisitor(p)) return false;
            return p.GetLord()?.CurLordToil is LordToil_DefendPoint; // visit and trader-caravan "chill" toils, not travelling/leaving
        }

        /// <summary>Runs at most once per tick and map, whoever calls it (the Venues map component used to call it too).</summary>
        public static void Run(Map map)
        {
            if (map == null) return;
            int now = Find.TickManager.TicksGame;
            if (lastRun.TryGetValue(map.uniqueID, out int last) && last == now) return;
            lastRun[map.uniqueID] = now;
            if (!Enabled || !map.IsPlayerHome || GuestActivities.All.Count == 0) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p.Faction == null || p.Faction.IsPlayer || !p.RaceProps.Humanlike) continue;
                if (nextOffer.TryGetValue(p.thingIDNumber, out int next) && now < next) continue;
                if (!Drives(p) || !Idle(p) || !p.Awake()) continue;
                var info = GuestApi.Info(p); if (info == null) continue;
                nextOffer[p.thingIDNumber] = now + Rand.RangeInclusive(240, 600);
                var job = GuestActivities.TryPick(p, info);
                if (job == null) continue;
                p.jobs.StartJob(job, JobCondition.InterruptOptional);
                JobsStarted++; LastJob = job;
            }
            if (now % 2500 < Interval) Prune(map);
        }

        static void Prune(Map map)
        {
            List<int> gone = null;
            foreach (var kv in nextOffer) if (kv.Value < Find.TickManager.TicksGame - 60000) (gone ?? (gone = new List<int>())).Add(kv.Key);
            if (gone != null) foreach (var k in gone) nextOffer.Remove(k);
        }

        /// <summary>Tests: offer again at once.</summary>
        public static void ResetOffers() => nextOffer.Clear();
    }

    /// <summary>Always-on tick for <see cref="GuestFallback"/> (ModuleId.Core: works whether Inn or Venues is on or off). No settings, no save data.</summary>
    public sealed class GuestFallbackModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Core;

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % GuestFallback.Interval != 0) return;
            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                if (!map.IsPlayerHome) continue;
                Guard.Run(ModuleId.Core, "GuestFallback", () => GuestFallback.Run(map));
            }
        }
    }
}
