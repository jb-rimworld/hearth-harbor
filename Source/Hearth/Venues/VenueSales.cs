// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Kitchen;

namespace Hearth.ShopsFood
{
    /// <summary>Short-lived, unsaved per-guest memory for shopping (counts per stay, cooldowns).</summary>
    public static class GuestMemory
    {
        sealed class Mem { public int Purchases, LastDrinkTick = -999999, NoServiceUntil, LastVendingTick = -999999, NextOffer; }
        static readonly Dictionary<int, Mem> mem = new Dictionary<int, Mem>();
        static Mem Of(Pawn p)
        {
            if (!mem.TryGetValue(p.thingIDNumber, out var m)) mem[p.thingIDNumber] = m = new Mem();
            return m;
        }
        public static void Clear() => mem.Clear();
        public static int Purchases(Pawn p) => mem.TryGetValue(p.thingIDNumber, out var m) ? m.Purchases : 0;
        public static void AddPurchase(Pawn p) => Of(p).Purchases++;
        public static int LastDrink(Pawn p) => mem.TryGetValue(p.thingIDNumber, out var m) ? m.LastDrinkTick : -999999;
        public static void SetDrink(Pawn p) => Of(p).LastDrinkTick = Find.TickManager.TicksGame;
        public static int LastVending(Pawn p) => mem.TryGetValue(p.thingIDNumber, out var m) ? m.LastVendingTick : -999999;
        public static void SetVending(Pawn p) => Of(p).LastVendingTick = Find.TickManager.TicksGame;
        public static bool InNoServiceCooldown(Pawn p) => mem.TryGetValue(p.thingIDNumber, out var m) && m.NoServiceUntil > Find.TickManager.TicksGame;
        public static void SetNoService(Pawn p, int ticks) => Of(p).NoServiceUntil = Find.TickManager.TicksGame + ticks;
        public static int NextOffer(Pawn p) => mem.TryGetValue(p.thingIDNumber, out var m) ? m.NextOffer : 0;
        public static void SetNextOffer(Pawn p, int tick) => Of(p).NextOffer = tick;
        /// <summary>Forget guests that left the map.</summary>
        public static void Prune(Func<int, bool> isGone)
        {
            List<int> dead = null;
            foreach (var k in mem.Keys) if (isGone(k)) (dead ?? (dead = new List<int>())).Add(k);
            if (dead != null) foreach (var k in dead) mem.Remove(k);
        }
    }

    /// <summary>What is for sale where (6.8, N45): items on stockpile cells and in storage buildings inside a selling venue's room.</summary>
    public static class VenueSales
    {
        sealed class StockCache { public int Tick; public readonly List<Thing> Items = new List<Thing>(); }
        static readonly Dictionary<int, StockCache> cache = new Dictionary<int, StockCache>();
        public static void ClearCaches() => cache.Clear();

        public static bool IsDrink(ThingDef d)
        {
            var ing = d.ingestible;
            if (ing == null) return false;
            return (ing.foodType & (FoodTypeFlags.Liquor | FoodTypeFlags.Fluid)) != 0 && !d.IsCorpse;
        }

        public static bool IsSellable(Thing t, SellKind kind)
        {
            if (kind == SellKind.None || t == null || t.Destroyed || t.def.category != ThingCategory.Item) return false;
            if (t is Corpse || t.def == ThingDefOf.Silver || t.def.IsCorpse) return false;
            if (kind == SellKind.Dishes)
            {
                // cooked, still fresh plates only: the colony's plain meals stay free to eat, stale plates are never forbidden (nobody can sell them, so the colony may clear them away)
                var plate = t.TryGetComp<CompDish>();
                return plate?.dish != null && !plate.IsStale;
            }
            if (t.MarketValue <= 0.001f) return false;
            if (kind == SellKind.Drinks) return IsDrink(t.def);
            return true;
        }

        /// <summary>Items for sale in a venue's room, refreshed at most every 250 ticks (or when forced).</summary>
        public static List<Thing> Stock(Venue v, bool force = false)
        {
            var kind = VenueApi.SellsOf(v);
            int id = v.Sign.thingIDNumber, now = Find.TickManager.TicksGame;
            if (!cache.TryGetValue(id, out var c)) cache[id] = c = new StockCache { Tick = -9999 };
            if (!force && now - c.Tick < 250)
            {
                // drop things that left since the last refresh
                for (int i = c.Items.Count - 1; i >= 0; i--) if (c.Items[i].Destroyed || !c.Items[i].Spawned) c.Items.RemoveAt(i);
                return c.Items;
            }
            c.Tick = now; c.Items.Clear();
            if (kind == SellKind.None || v.Room == null) return c.Items;
            var map = v.Map;
            foreach (var cell in v.Room.Cells)
            {
                if (kind != SellKind.Dishes && cell.GetSlotGroup(map) == null) continue;   // plates lie on tables and counters as well as on shelves
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (IsSellable(things[i], kind)) c.Items.Add(things[i]);
            }
            return c.Items;
        }

        /// <summary>Items in powered or unpowered vending machines of a map; only machines that can sell right now.</summary>
        public static IEnumerable<Building> VendingMachines(Map map)
        {
            var list = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b.def != HHVenueDefOf.HH_VendingMachine || !b.Spawned) continue;
                yield return b;
            }
        }

        public static bool MachineWorks(Building b)
        {
            var power = b.GetComp<CompPowerTrader>();
            return power == null || power.PowerOn;
        }

        public static IEnumerable<Thing> MachineStock(Building b)
        {
            var sb = b as Building_Storage; if (sb == null) yield break;
            var held = sb.GetSlotGroup()?.HeldThings; if (held == null) yield break;
            foreach (var t in held)
                if (t.Spawned && !t.Destroyed && t.def.category == ThingCategory.Item && t.MarketValue > 0.001f) yield return t;
        }

        /// <summary>The price a guest sees for a vending machine item.</summary>
        public static Money VendingPrice(Thing t, int count) => Money.FromSilver((double)t.MarketValue * count * VS.F(VS.VendingPrice));
    }
}
