// SPDX-License-Identifier: MIT
#if HH_TESTS
using System.Collections.Generic;
using System.Linq;
using Hearth.Testing;
using RimWorld;
using Verse;

namespace Hearth.Kitchen.Tests
{
    /// <summary>"Do until you have X" of a dish: a plate on a shelf counts once, "count only in" a shelf counts only that shelf.</summary>
    public sealed class CounterSuite : HearthTestSuite
    {
        public override string Name => "kitchen.counter";
        public override ModuleId Module => ModuleId.ShopsFood;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var area = map.Center + new IntVec3(14, 0, -14);
            KT.Clear(map, area, 6);
            var dish = KT.Dish("BerryCompote");
            var recipe = dish.recipes[0];
            var shelfDef = DefDatabase<ThingDef>.GetNamedSilentFail("Shelf");
            t.Check(shelfDef != null, "vanilla shelf exists");
            if (shelfDef == null) yield break;
            var shelf = (Building_Storage)KT.Spawn(map, shelfDef, area, Rot4.North);
            shelf.settings.filter.SetAllowAll(null);
            yield return 1;

            var onShelf = ThingMaker.MakeThing(dish.plateDef);
            onShelf.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
            onShelf.TryGetComp<CompDish>().SetDish(dish, 0);
            onShelf.stackCount = 1;
            GenSpawn.Spawn(onShelf, shelf.Position, map);
            yield return 1;
            var group = shelf.GetSlotGroup();
            t.Check(group != null && group.HeldThings.Contains(onShelf), "the plate lies on the shelf");

            var bill = (Bill_Production)recipe.MakeNewBill();
            ((IBillGiver)KT.SpawnDef(map, "HH_Station_StockPot", area + new IntVec3(-3, 0, 0), Rot4.North)).BillStack.AddBill(bill);
            bill.repeatMode = BillRepeatModeDefOf.TargetCount; bill.targetCount = 5;
            var counter = recipe.WorkerCounter;
            int n = counter.CountProducts(bill);
            t.Check(n == 1, "a shelved plate is counted once: " + n);

            // a second plate on the floor, "count only in" the shelf
            var onFloor = KT.MakePlate(map, dish, area + new IntVec3(0, 0, 3));
            yield return 1;
            n = counter.CountProducts(bill);
            t.Check(n == 2, "shelf plate + floor plate: " + n);
            bill.SetIncludeGroup(group);
            n = counter.CountProducts(bill);
            t.Check(n == 1, "'count only in' the shelf counts only the shelf: " + n);
            bill.SetIncludeGroup(null);

            // a plate of another dish on the shelf does not count
            var other = ThingMaker.MakeThing(KT.Dish("ScrambledEggs").plateDef);
            other.TryGetComp<CompDish>().SetDish(KT.Dish("ScrambledEggs"), 0);
            GenSpawn.Spawn(other, shelf.Position, map);
            n = counter.CountProducts(bill);
            t.Check(n == 2, "another dish on the shelf is not counted: " + n);

            foreach (var th in new Thing[] { onShelf, onFloor, other, shelf }) if (th != null && !th.Destroyed) th.Destroy();
            KT.Cleanup(map, area, 6);
        }
    }
}
#endif
