// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Kitchen
{
    /// <summary>One place that sells dishes: the room of a Restaurant or Tavern sign.</summary>
    public sealed class RestaurantSpot
    {
        public Room Room;
        public Venue Venue;         // null for the fallback
        public Map Map;
        public float PriceFactor = 1f;
        public string Name;
        public string Account => Venue != null ? Venue.Account : "house:restaurant:" + Room.ID;
        /// <summary>Venues (restaurant, tavern) also sell the colony's plain vanilla meals; a spot without a venue does not (N08).</summary>
        public bool SellsVanillaMeals => Venue != null;
    }

    /// <summary>A plate a guest has picked from the shelf: paid when the plate is eaten.</summary>
    public sealed class PlateSale
    {
        public int GuestId, PlateId, Tick;
        public Money Price;
        public string Account, Dish;
        /// <summary>The restaurant or tavern that sold it (its sign records the sale); null in tests.</summary>
        public Venue Venue;
        /// <summary>Set for a plain vanilla meal (paid when the guest eats a thing of this def).</summary>
        public ThingDef VanillaDef;
    }

    /// <summary>
    /// The restaurant (6.4): a hungry guest goes to a restaurant, takes a plate from the room's stock, eats it at a table and pays the menu price
    /// (dish × quality × form × venue factor). Registered as guest activity "kitchen.eat". Only rooms with a Restaurant or Tavern sign sell food
    /// (N08: a plain dining room stays a dining room), and only while the venue is open for business (hours, staff, night bell).
    /// </summary>
    public static class Restaurant
    {
        public const string ActivityId = "kitchen.eat";
        public const string VenueDefName = "HH_Venue_Restaurant";
        public const string TavernDefName = "HH_Venue_Tavern";
        /// <summary>Menu price of plain vanilla meals by preferability (B8/C6): simple 10, fine 16, lavish 26, before the venue factor and the menu-price setting.</summary>
        public static readonly Dictionary<FoodPreferability, float> VanillaMealBase = new Dictionary<FoodPreferability, float>
        {
            { FoodPreferability.MealSimple, 10f }, { FoodPreferability.MealFine, 16f }, { FoodPreferability.MealLavish, 26f },
        };
        /// <summary>Vanilla meals picked by a guest, keyed by the guest (the eaten thing is split off the stack, so its id is new).</summary>
        static readonly Dictionary<int, PlateSale> vanillaSales = new Dictionary<int, PlateSale>();
        static readonly Dictionary<int, PlateSale> sales = new Dictionary<int, PlateSale>();
        static readonly Dictionary<int, (int tick, List<RestaurantSpot> spots)> cache = new Dictionary<int, (int, List<RestaurantSpot>)>();
        public static int TotalSales, UnpaidMeals;
        public static Money TotalTakings = Money.Zero;

        static RoomRoleDef roleDining;
        static RoomRoleDef RoleDining => roleDining ?? (roleDining = DefDatabase<RoomRoleDef>.GetNamedSilentFail("DiningRoom"));
        /// <summary>Is the venue open for guests? The venue gate: hours, somebody on duty, night bell. Replaceable for tests.</summary>
        public static Func<Venue, bool> OpenGate = Hearth.ShopsFood.VenueApi.IsOpenForBusiness;
        /// <summary>Everything that multiplies the menu price of a venue: type and sign factor, price setting, night-bell surcharge. Replaceable for tests.</summary>
        public static Func<Venue, float> VenuePriceFactor = Hearth.ShopsFood.VenueApi.PriceMultiplier;

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
                    if (v.Room != null && Guard.Run(ModuleId.ShopsFood, "Restaurant.OpenGate", () => OpenGate(v), false))
                        list.Add(new RestaurantSpot { Room = v.Room, Venue = v, Map = map, PriceFactor = Math.Max(0.1f, Guard.Run(ModuleId.ShopsFood, "Restaurant.PriceFactor", () => VenuePriceFactor(v), 1f)), Name = v.Comp.DisplayName });
            }
            var tavern = DefDatabase<VenueTypeDef>.GetNamedSilentFail(TavernDefName);
            if (tavern != null)
            {
                foreach (var v in Venues.OfType(map, tavern))
                    if (v.Room != null && list.All(x => x.Room != v.Room) && Guard.Run(ModuleId.ShopsFood, "Restaurant.OpenGate", () => OpenGate(v), false))
                        list.Add(new RestaurantSpot { Room = v.Room, Venue = v, Map = map, PriceFactor = Math.Max(0.1f, Guard.Run(ModuleId.ShopsFood, "Restaurant.PriceFactor", () => VenuePriceFactor(v), 1f)), Name = v.Comp.DisplayName });
            }
            cache[map.uniqueID] = (now, list);
            return list;
        }

        public static void ClearCache() { cache.Clear(); }

        /// <summary>A plain vanilla meal a venue may sell: simple, fine or lavish, freshly cooked (no survival packs, pemmican, paste or plates).</summary>
        public static bool IsVanillaMeal(ThingDef d)
        {
            var ing = d?.ingestible;
            if (ing == null || !d.IsNutritionGivingIngestible || !ing.HumanEdible) return false;
            if (!VanillaMealBase.ContainsKey(ing.preferability)) return false;
            // fresh meals only: survival packs never rot, pemmican keeps for months
            var rot = d.GetCompProperties<CompProperties_Rottable>();
            if (rot == null || rot.daysToRotStart > 15f) return false;
            if (d.comps != null && d.comps.Any(c => c.compClass == typeof(CompDish))) return false;
            return d.defName.IndexOf("NutrientPaste", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>Menu price of a vanilla meal at this spot.</summary>
        public static Money VanillaPrice(ThingDef d, RestaurantSpot spot)
        {
            float b = VanillaMealBase.TryGetValue(d.ingestible.preferability, out var v) ? v : 10f;
            return Money.FromSilver(Math.Max(1.0, Math.Round(b * spot.PriceFactor * HSettings.Get("kitchen.menuPrice"), 1)));
        }

        /// <summary>Plates for sale: fresh, not forbidden, not yet picked by someone else, lying in the room of the restaurant.
        /// Restaurants and taverns also sell plain vanilla meals (see <see cref="IsVanillaMeal"/>).</summary>
        public static IEnumerable<Thing> Stock(RestaurantSpot spot)
        {
            foreach (var t in spot.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                var c = t.TryGetComp<CompDish>();
                if (c == null || c.dish == null)
                {
                    if (!spot.SellsVanillaMeals || !IsVanillaMeal(t.def)) continue;
                    if (t.TryGetComp<CompRottable>() is CompRottable rot && rot.Stage != RotStage.Fresh) continue;
                }
                else if (c.IsStale) continue;
                if (!t.Spawned || ForbiddenForSale(t, spot.Map) || sales.ContainsKey(t.thingIDNumber)) continue;
                if (t.Position.GetRoom(spot.Map) != spot.Room) continue;
                yield return t;
            }
        }

        /// <summary>Forbidden for the guests' shelf? Goods the venues module forbade for the colony (so nobody carries the stock away) are still for sale; anything the player forbade is not.</summary>
        static bool ForbiddenForSale(Thing t, Map map)
        {
            var cf = t.TryGetComp<CompForbiddable>();
            if (cf != null && cf.Forbidden)
            {
                var vc = map.GetComponent<Hearth.ShopsFood.VenueMapComp>();
                return vc == null || !vc.IsForbiddenByUs(t);
            }
            return t.IsForbidden(Faction.OfPlayer);
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
                bool plate = c?.dish != null;
                var p = plate ? DishEffects.MenuPrice(c, spot.PriceFactor) : VanillaPrice(t.def, spot);
                if (p > budget) continue;
                if (!guest.CanReserve(t) || !guest.WillEat(t, guest, careIfNotAcceptableForTitle: false)) continue;
                // a cooked dish beats a plain meal of the same price (the kitchen is the better business, 6.4)
                float score = plate
                    ? (int)c.Quality * 6f + c.dish.level * 3f + c.FreshFraction * 2f - (float)p.Silver * 0.05f + Rand.Range(0f, 3f)
                    : ((int)t.def.ingestible.preferability - (int)FoodPreferability.MealSimple) * 4f + 4f - (float)p.Silver * 0.05f + Rand.Range(0f, 3f);
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
                return NoteSale(guest, plate, price, spot);
            }
            return null;
        }

        /// <summary>Books the pick (paid when eaten) and returns the ingest job for it.</summary>
        public static Job NoteSale(Pawn guest, Thing plate, Money price, RestaurantSpot spot)
        {
            var job = JobMaker.MakeJob(JobDefOf.Ingest, plate);
            job.count = 1;
            var dish = plate.TryGetComp<CompDish>()?.dish;
            var sale = new PlateSale { GuestId = guest.thingIDNumber, PlateId = plate.thingIDNumber, Tick = Find.TickManager.TicksGame, Price = price, Account = spot.Account, Dish = dish != null ? dish.label : plate.def.label, VanillaDef = dish == null ? plate.def : null, Venue = spot.Venue };
            if (dish != null) sales[plate.thingIDNumber] = sale;
            else vanillaSales[guest.thingIDNumber] = sale;
            return job;
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
                (sale.Venue?.Comp as Hearth.ShopsFood.CompVenueSignHH)?.RecordSale(sale.Price, 1);
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

        /// <summary>A guest ate a thing: pays a pending vanilla-meal sale of that def (Thing.Ingested postfix).</summary>
        public static void OnVanillaEaten(Thing food, Pawn eater)
        {
            if (food == null || eater == null || !vanillaSales.TryGetValue(eater.thingIDNumber, out var sale) || sale.VanillaDef != food.def) return;
            vanillaSales.Remove(eater.thingIDNumber);
            string memo = "Restaurant: " + sale.Dish;
            if (GuestApi.ChargeGuest(eater, sale.Price, "food", memo))
            {
                TotalSales++; TotalTakings += sale.Price;
                (sale.Venue?.Comp as Hearth.ShopsFood.CompVenueSignHH)?.RecordSale(sale.Price, 1);
                float sat = food.def.ingestible.preferability >= FoodPreferability.MealLavish ? 0.08f : food.def.ingestible.preferability >= FoodPreferability.MealFine ? 0.05f : 0.03f;
                GuestApi.AddSatisfaction(eater, sat, "had a " + sale.Dish.ToLowerInvariant());
                HLog.Debug("Kitchen", $"{eater.LabelShort} paid {sale.Price} for {sale.Dish}");
            }
            else
            {
                UnpaidMeals++;
                GuestApi.AddSatisfaction(eater, -0.2f, "could not pay for the meal");
            }
        }

        internal static bool AnyVanillaSales => vanillaSales.Count > 0;

        /// <summary>Forget picked plates that were never eaten (the guest left, the job failed).</summary>
        public static void Expire(int now)
        {
            if (sales.Count == 0 && vanillaSales.Count == 0) return;
            List<int> dead = null;
            foreach (var kv in sales) if (now - kv.Value.Tick > 6000) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) foreach (var k in dead) sales.Remove(k);
            dead = null;
            foreach (var kv in vanillaSales) if (now - kv.Value.Tick > 6000) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) foreach (var k in dead) vanillaSales.Remove(k);
        }

        public static int PendingSales => sales.Count + vanillaSales.Count;
        /// <summary>Forget the sale of a plate (the guest changed their mind).</summary>
        public static void ForgetSale(Thing plate) { if (plate != null) sales.Remove(plate.thingIDNumber); }
        /// <summary>Forget a guest's pending vanilla-meal sale.</summary>
        public static void ForgetVanillaSale(Pawn guest) { if (guest != null) vanillaSales.Remove(guest.thingIDNumber); }
    }

    /// <summary>Plain vanilla meals carry no CompDish, so their sale is paid when the guest actually eats one (once per meal, not a hot path).</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    static class Patch_Thing_Ingested_VanillaMeal
    {
        static void Postfix(Thing __instance, Pawn ingester)
        {
            if (!Restaurant.AnyVanillaSales || ingester == null || __instance == null || !Modules.On(ModuleId.ShopsFood)) return;
            var food = __instance;
            Guard.Run(ModuleId.ShopsFood, "Restaurant.VanillaMealPaid", () => Restaurant.OnVanillaEaten(food, ingester));
        }
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

#if HH_TESTS
namespace Hearth.Kitchen.Tests
{
    using Hearth.Testing;
    using Verse.AI.Group;

    /// <summary>B8/C6: restaurants and taverns also sell plain vanilla meals (not paste, not survival packs), paid when eaten.</summary>
    public sealed class VanillaMealSuite : HearthTestSuite
    {
        public override string Name => "kitchen.vanillameals";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(Restaurant.IsVanillaMeal(ThingDefOf.MealSimple), "simple meal is a vanilla meal");
            var fine = DefDatabase<ThingDef>.GetNamedSilentFail("MealFine"); var lavish = DefDatabase<ThingDef>.GetNamedSilentFail("MealLavish");
            t.Check(fine == null || Restaurant.IsVanillaMeal(fine), "fine meal is a vanilla meal");
            t.Check(lavish == null || Restaurant.IsVanillaMeal(lavish), "lavish meal is a vanilla meal");
            t.Check(!Restaurant.IsVanillaMeal(ThingDefOf.MealNutrientPaste), "nutrient paste is not sold");
            t.Check(!Restaurant.IsVanillaMeal(ThingDefOf.MealSurvivalPack), "survival packs are not sold");
            t.Check(!Restaurant.IsVanillaMeal(DefDatabase<ThingDef>.GetNamedSilentFail("Pemmican")), "pemmican is not sold");
            var map = t.Map; var center = map.Center + new IntVec3(-16, 0, 14);
            KT.Clear(map, center, 8, floor: true);
            KT.FreeUp(map);
            KT.Room(map, center, 7);
            var table = KT.SpawnDef(map, "Table2x2c", center);
            var rect = table.OccupiedRect();
            KT.SpawnDef(map, "DiningChair", new IntVec3(rect.minX - 1, 0, rect.minZ));
            yield return 10;
            var room = center.GetRoom(map);
            var meal = KT.Spawn(map, ThingDefOf.MealSimple, center + new IntVec3(-2, 0, 2));
            meal.stackCount = 5;
            var fallback = new RestaurantSpot { Room = room, Map = map, PriceFactor = 1f, Name = "dining room" };
            t.Check(!Restaurant.Stock(fallback).Contains(meal), "a dining room without a sign does not sell the colony's meals (N08)");
            // a venue spot (restaurant or tavern): stands in for a placed sign, only Venue != null matters for the stock rule
            var spot = new RestaurantSpot { Room = room, Map = map, PriceFactor = 1.2f, Name = "test restaurant", Venue = new Venue { Sign = table } };
            t.Check(Restaurant.Stock(spot).Contains(meal), "a restaurant venue sells simple meals from its room");
            var price = Restaurant.VanillaPrice(ThingDefOf.MealSimple, spot);
            t.Check(Math.Abs(price.Silver - 12.0) < 0.05, "simple meal costs 10 × venue factor 1.2 = 12 silver: " + price);
            if (fine != null) t.Check(Restaurant.VanillaPrice(fine, spot) > price, "fine meals cost more");

            t.Note("visitors spawned: " + Hearth.Testing.GuestTestKit.SpawnVisitors(map, 1).Count);
            Pawn guest = null;
            foreach (var w in KT.WaitUntil(() => (guest = map.mapPawns.AllPawnsSpawned.FirstOrDefault(GuestApi.IsGuest)) != null, 900, 30)) yield return w;
            t.Check(guest != null, "a visitor counts as a guest");
            if (guest == null) { KT.Cleanup(map, center, 8); yield break; }
            var info = GuestApi.Info(guest);
            KT.ClearJob(guest); KT.Teleport(guest, center + new IntVec3(0, 0, -2));
            info.Budget = Money.FromSilver(100);
            guest.needs.food.CurLevelPercentage = 0.2f;
            var picked = Restaurant.PickPlate(guest, spot, info.Budget, out var p);
            t.Check(picked == meal && Math.Abs(p.Silver - price.Silver) < 0.01, "the hungry guest picks the simple meal at the menu price");
            if (picked == null) { KT.Cleanup(map, center, 8); yield break; }
            var till = Payments.ColonyTill(map);
            var before = Ledger.Current.Balance(till); int sales0 = Restaurant.TotalSales;
            var job = Restaurant.NoteSale(guest, picked, p, spot);
            t.Check(Restaurant.PendingSales >= 1, "the sale is noted");
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            foreach (var w in KT.WaitUntil(() => Restaurant.TotalSales > sales0, 5000, 30)) yield return w;
            var after = Ledger.Current.Balance(till);
            t.Check(Restaurant.TotalSales == sales0 + 1, "the meal was eaten and paid");
            t.Check((after - before).Silver >= price.Silver - 0.01, "the till got the menu price: " + before + " -> " + after);
            t.Check(meal.Destroyed || meal.stackCount == 4, "one meal of the stack was eaten (left: " + (meal.Destroyed ? 0 : meal.stackCount) + ")");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            KT.ClearJob(guest); KT.Cleanup(map, center, 9);
            foreach (var g in map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsGuest).ToList()) { g.GetLord()?.lordManager.RemoveLord(g.GetLord()); g.Destroy(); }
        }
    }
}
#endif
