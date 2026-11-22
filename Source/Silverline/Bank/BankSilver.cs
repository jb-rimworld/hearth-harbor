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

        /// <summary>Makes <paramref name="coins"/> silver appear next to a bank building.</summary>
        public static void PlaceAt(Map map, IntVec3 cell, int coins)
        {
            int limit = Math.Max(1, ThingDefOf.Silver.stackLimit);
            while (coins > 0)
            {
                int n = Math.Min(coins, limit);
                var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n;
                if (!GenPlace.TryPlaceThing(s, cell, map, ThingPlaceMode.Near)) { s.Destroy(); HLog.Warn("Bank", "Could not place " + n + " silver at " + cell); break; }
                coins -= n;
            }
        }

        /// <summary>Silver arrives by drop pod near <paramref name="cell"/>: loads are split into pods of up to 4 stacks.</summary>
        public static void DropPods(Map map, IntVec3 cell, int coins)
        {
            int limit = Math.Max(1, ThingDefOf.Silver.stackLimit);
            var stacks = new List<Thing>();
            while (coins > 0)
            {
                int n = Math.Min(coins, limit);
                var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = n; stacks.Add(s); coins -= n;
            }
            var groups = new List<List<Thing>>();
            for (int i = 0; i < stacks.Count; i += 4) groups.Add(stacks.Skip(i).Take(4).ToList());
            DropPodUtility.DropThingGroupsNear(cell, map, groups, 110, false, false, false, false, true);
        }
    }
}
