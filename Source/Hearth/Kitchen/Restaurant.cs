// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Kitchen
{
    /// <summary>One place that sells dishes: a Restaurant venue (venues module) or, without it, any dining room.</summary>
    public sealed class RestaurantSpot
    {
        public Room Room;
        public Venue Venue;         // null for the fallback
        public Map Map;
        public float PriceFactor = 1f;
        public string Name;
        public string Account => Venue != null ? Venue.Account : "house:restaurant:" + Room.ID;
    }

    /// <summary>A plate a guest has picked from the shelf: paid when the plate is eaten.</summary>
    public sealed class PlateSale
    {
        public int GuestId, PlateId, Tick;
        public Money Price;
        public string Account, Dish;
    }

    /// <summary>
    /// The restaurant (6.4): a hungry guest goes to a restaurant, takes a plate from the room's stock, eats it at a table and pays the menu price
    /// (dish × quality × form × venue factor). Registered as guest activity "kitchen.eat". Without a venue sign the dining rooms of the colony
    /// count as restaurants, so the loop also works before the venues module is on.
    /// </summary>
    public static class Restaurant
    {
        public const string ActivityId = "kitchen.eat";
        public const string VenueDefName = "HH_Venue_Restaurant";
        static readonly Dictionary<int, PlateSale> sales = new Dictionary<int, PlateSale>();
        static readonly Dictionary<int, (int tick, List<RestaurantSpot> spots)> cache = new Dictionary<int, (int, List<RestaurantSpot>)>();
        public static int TotalSales, UnpaidMeals;
        public static Money TotalTakings = Money.Zero;

        static RoomRoleDef roleDining;
        static RoomRoleDef RoleDining => roleDining ?? (roleDining = DefDatabase<RoomRoleDef>.GetNamedSilentFail("DiningRoom"));
        public static VenueTypeDef VenueType => DefDatabase<VenueTypeDef>.GetNamedSilentFail(VenueDefName);

        // ---- finding places ---------------------------------------------------------------------------------------------------
        public static List<RestaurantSpot> Spots(Map map)
        {
            if (map == null) return new List<RestaurantSpot>();
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(map.uniqueID, out var c) && now - c.tick < 250) return c.spots;
            var list = new List<RestaurantSpot>();
            var vt = VenueType;
            if (vt != null)
            {
                foreach (var v in Venues.OfType(map, vt))
                    if (v.Room != null && v.IsOpenNow)
                        list.Add(new RestaurantSpot { Room = v.Room, Venue = v, Map = map, PriceFactor = Math.Max(0.1f, vt.priceFactor * v.Comp.priceFactor), Name = v.Comp.DisplayName });
            }
            if (vt == null || !Venues.All(map).Any(v => v.Type == vt))
            {
                // fallback: dining rooms count as a restaurant until a venue sign says otherwise
                #if RW15
                var allRooms = map.regionGrid.allRooms;
#else
                var allRooms = map.regionGrid.AllRooms;
#endif
                foreach (var room in allRooms)
                    if (room.Role == RoleDining && !room.PsychologicallyOutdoors && room.ContainedAndAdjacentThings.Count > 0)
                        list.Add(new RestaurantSpot { Room = room, Map = map, PriceFactor = 1f, Name = "dining room" });
            }
            cache[map.uniqueID] = (now, list);
            return list;
        }

        public static void ClearCache() { cache.Clear(); }

        /// <summary>Plates for sale: fresh, not forbidden, not yet picked by someone else, lying in the room of the restaurant.</summary>
        public static IEnumerable<Thing> Stock(RestaurantSpot spot)
        {
            foreach (var t in spot.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                var c = t.TryGetComp<CompDish>();
                if (c == null || c.dish == null || c.IsStale) continue;
                if (!t.Spawned || t.IsForbidden(Faction.OfPlayer) || sales.ContainsKey(t.thingIDNumber)) continue;
                if (t.Position.GetRoom(spot.Map) != spot.Room) continue;
                yield return t;
            }
        }

        public static bool HasFreeSeat(RestaurantSpot spot)
        {
            foreach (var t in spot.Room.ContainedAndAdjacentThings)
                if (t.def.building != null && t.def.building.isSittable && !t.IsForbidden(Faction.OfPlayer))
                    return true;
            return false;
        }

        // ---- picking a plate ---------------------------------------------------------------------------------------------------
        public static Thing PickPlate(Pawn guest, RestaurantSpot spot, Money budget, out Money price)
        {
            Thing best = null; float bestScore = float.MinValue; price = Money.Zero;
            foreach (var t in Stock(spot))
            {
                var c = t.TryGetComp<CompDish>();
                var p = DishEffects.MenuPrice(c, spot.PriceFactor);
                if (p > budget) continue;
                if (!guest.CanReserve(t) || !guest.WillEat(t, guest, careIfNotAcceptableForTitle: false)) continue;
                float score = (int)c.Quality * 6f + c.dish.level * 3f + c.FreshFraction * 2f - (float)p.Silver * 0.05f + Rand.Range(0f, 3f);
                if (score > bestScore) { bestScore = score; best = t; price = p; }
            }
            return best;
        }

        public static Job MakeJob(Pawn guest)
        {
            var map = guest.Map;
            var budget = GuestApi.Budget(guest);
            foreach (var spot in Spots(map))
            {
                if (!HasFreeSeat(spot)) continue;
                var plate = PickPlate(guest, spot, budget, out var price);
                if (plate == null) continue;
                var job = JobMaker.MakeJob(JobDefOf.Ingest, plate);
                job.count = 1;
                sales[plate.thingIDNumber] = new PlateSale { GuestId = guest.thingIDNumber, PlateId = plate.thingIDNumber, Tick = Find.TickManager.TicksGame, Price = price, Account = spot.Account, Dish = plate.TryGetComp<CompDish>().dish.label };
                return job;
            }
            return null;
        }

        /// <summary>Is any restaurant open with something to eat and a seat? Cheap enough for the guest brain (cached spots, one scan of food things).</summary>
        public static bool AnythingToEat(Map map)
        {
            foreach (var spot in Spots(map)) if (HasFreeSeat(spot) && Stock(spot).Any()) return true;
            return false;
        }

        // ---- paying -----------------------------------------------------------------------------------------------------------
        public static void OnPlateEaten(CompDish c, Pawn eater)
        {
            if (!sales.TryGetValue(c.parent.thingIDNumber, out var sale)) return;
            sales.Remove(c.parent.thingIDNumber);
            if (sale.GuestId != eater.thingIDNumber) return;
            string memo = "Restaurant: " + sale.Dish;
            if (GuestApi.ChargeGuest(eater, sale.Price, "food", memo))
            {
                TotalSales++; TotalTakings += sale.Price;
                float sat = Mathf.Clamp(0.05f + 0.04f * ((int)c.Quality - 2) + (c.dish.level - 1) * 0.01f, -0.1f, 0.3f);
                GuestApi.AddSatisfaction(eater, sat, "enjoyed a " + sale.Dish.ToLowerInvariant());
                HLog.Debug("Kitchen", $"{eater.LabelShort} paid {sale.Price} for {sale.Dish}");
            }
            else
            {
                UnpaidMeals++;
                GuestApi.AddSatisfaction(eater, -0.2f, "could not pay for the meal");
                HLog.Info("Kitchen", $"{eater.LabelShort} could not pay {sale.Price} for {sale.Dish}");
            }
        }

        /// <summary>Forget picked plates that were never eaten (the guest left, the job failed).</summary>
        public static void Expire(int now)
        {
            if (sales.Count == 0) return;
            List<int> dead = null;
            foreach (var kv in sales) if (now - kv.Value.Tick > 6000) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) foreach (var k in dead) sales.Remove(k);
        }

        public static int PendingSales => sales.Count;
        /// <summary>Forget the sale of a plate (the guest changed their mind).</summary>
        public static void ForgetSale(Thing plate) { if (plate != null) sales.Remove(plate.thingIDNumber); }
    }

    public sealed class RestaurantActivity : IGuestActivity
    {
        public string Id => Restaurant.ActivityId;
        public ModuleId Module => ModuleId.ShopsFood;

        public float Desire(Pawn guest, GuestInfo info)
        {
            if (guest?.needs?.food == null || guest.Map == null) return 0f;
            if (info == null || info.Budget < Money.FromSilver(6)) return 0f;
            if (!Restaurant.AnythingToEat(guest.Map)) return 0f;
            float f = guest.needs.food.CurLevelPercentage;
            return f < 0.35f ? 3f : f < 0.6f ? 1.5f : f < 0.85f ? 0.4f : 0.1f;   // hungry guests come first, others fancy a meal now and then
        }

        public Job TryGiveJob(Pawn guest, GuestInfo info) => Restaurant.MakeJob(guest);
    }
}
