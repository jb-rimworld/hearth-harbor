// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>Physical silver on the map: what a deposit can take and how a withdrawal arrives.</summary>
    public static class BankSilver
    {
        /// <summary>Silver a deposit may take: in range of a powered trade beacon (the vanilla trade way) or lying in storage (stockpile, shelf). Never forbidden silver.</summary>
        public static List<Thing> Depositable(Map map)
        {
            var res = new List<Thing>();
            if (map == null) return res;
            var seen = new HashSet<Thing>();
            try
            {
                foreach (var t in TradeUtility.AllLaunchableThingsForTrade(map))
                    if (t.def == ThingDefOf.Silver && t.stackCount > 0 && seen.Add(t)) res.Add(t);
            }
            catch (Exception e) { HLog.Once("bank.beaconSilver", LogLevel.Warn, "Bank", "Beacon silver scan failed, using storage only: " + e.Message); }
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (!t.Spawned || t.stackCount <= 0 || seen.Contains(t)) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                if (!t.IsInAnyStorage()) continue;
                seen.Add(t); res.Add(t);
            }
            return res;
        }

        public static int DepositableCoins(Map map) => Depositable(map).Sum(t => t.stackCount);

        static int cacheFrame = -999, cacheMapId = -1, cacheCoins;
        /// <summary>For the window: the scan runs at most every 30 frames.</summary>
        public static int DepositableCoinsCached(Map map)
        {
            if (map == null) return 0;
            if (cacheMapId != map.uniqueID || Time.frameCount - cacheFrame >= 30) { cacheCoins = DepositableCoins(map); cacheFrame = Time.frameCount; cacheMapId = map.uniqueID; }
            return cacheCoins;
        }

        /// <summary>Removes exactly <paramref name="coins"/> silver from the map (like a trade). Returns false and removes nothing if there is not enough.</summary>
        public static bool TakeCoins(Map map, int coins)
        {
            if (coins <= 0) return false;
            var things = Depositable(map);
            if (things.Sum(t => t.stackCount) < coins) return false;
            int left = coins;
            foreach (var t in things.OrderBy(t => t.stackCount))   // smallest stacks first: keep the big piles tidy
            {
                if (left <= 0) break;
                int take = Math.Min(left, t.stackCount);
                if (take == t.stackCount) t.Destroy(); else t.SplitOff(take).Destroy();
                left -= take;
            }
            return left == 0;
        }

        public static IntVec3 PodTarget(Map map)
        {
            var b = map.listerBuildings.allBuildingsColonist.FirstOrDefault(x => x is Building_SLTerminal tm && tm.Powered && !(x is Building_SLNightSafe));
            if (b != null) return b.InteractionCell.IsValid ? b.InteractionCell : b.Position;
            return DropCellFinder.TradeDropSpot(map);
        }

        /// <summary>Most stacks that are placed by hand at a counter; the rest of a huge withdrawal comes by drop pod.</summary>
        public const int PlaceMaxStacks = 100;
        /// <summary>Most drop pods one withdrawal may use (pods carry any number of stacks; pod spam would stall the game).</summary>
        public const int MaxPodsPerWithdrawal = 10;

        /// <summary>Pure: splits a withdrawal into pod loads. Each pod carries at most <paramref name="maxLoad"/> silver (bank.podMaxLoad) unless that would need more than
        /// <paramref name="maxPods"/> pods; then the loads grow evenly. The sum always equals <paramref name="coins"/>.</summary>
        public static List<int> PodPlan(int coins, int maxLoad, int maxPods)
        {
            var res = new List<int>();
            if (coins <= 0) return res;
            maxLoad = Math.Max(1, maxLoad); maxPods = Math.Max(1, maxPods);
            int pods = (int)Math.Min((long)maxPods, ((long)coins + maxLoad - 1) / maxLoad);
            int each = coins / pods, extra = coins % pods;
            for (int i = 0; i < pods; i++) res.Add(each + (i < extra ? 1 : 0));
            return res;
        }

        /// <summary>Makes <paramref name="coins"/> silver appear next to a bank building (at most <see cref="PlaceMaxStacks"/> stacks, the rest comes by pod).</summary>
        public static void PlaceAt(Map map, IntVec3 cell, int coins)
        {
            int limit = Math.Max(1, ThingDefOf.Silver.stackLimit);
            int stacks = 0;
            while (coins > 0)
            {
                if (stacks >= PlaceMaxStacks) { DropPods(map, cell, coins); return; }
                int n = Math.Min(coins, limit);
                var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n;
                if (!GenPlace.TryPlaceThing(s, cell, map, ThingPlaceMode.Near)) { s.Destroy(); HLog.Warn("Bank", "Could not place " + n + " silver at " + cell); break; }
                coins -= n; stacks++;
            }
        }

        /// <summary>Silver arrives by drop pod near <paramref name="cell"/>: pods hold up to bank.podMaxLoad silver, at most <see cref="MaxPodsPerWithdrawal"/> pods.</summary>
        public static void DropPods(Map map, IntVec3 cell, int coins)
        {
            int limit = Math.Max(1, ThingDefOf.Silver.stackLimit);
            var groups = new List<List<Thing>>();
            foreach (int load in PodPlan(coins, HSettings.GetInt("bank.podMaxLoad"), MaxPodsPerWithdrawal))
            {
                var g = new List<Thing>(); int left = load;
                while (left > 0)
                {
                    int n = Math.Min(left, limit);
                    var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n; g.Add(s); left -= n;
                }
                groups.Add(g);
            }
            DropPodUtility.DropThingGroupsNear(cell, map, groups, 110, false, false, false, false, true);
        }
    }
}
