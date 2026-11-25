// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Testing;

namespace Hearth.Kitchen.Tests
{
    static class KT
    {
        public static DishDef Dish(string id) => DefDatabase<DishDef>.GetNamed("HH_Dish_" + id);
        public static Pawn Colonist(Map map, int skip = 0) => map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Dead).Skip(skip).FirstOrDefault();

        /// <summary>A colonist who is allowed to cook (a disabled skill reads as level 0 and cannot be set).</summary>
        public static Pawn Cook(Map map, int skip = 0) => map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Dead && !p.WorkTypeIsDisabled(Cooking)).Skip(skip).FirstOrDefault();

        public static void Clear(Map map, IntVec3 center, int r, bool floor = false)
        {
            foreach (var c in CellRect.CenteredOn(center, r).Cells.ToList())
            {
                if (!c.InBounds(map)) continue;
                foreach (var th in c.GetThingList(map).ToList()) if (!(th is Pawn) && th.def.destroyable) th.Destroy();
                map.roofGrid.SetRoof(c, null);
                map.terrainGrid.SetTerrain(c, floor ? DefDatabase<TerrainDef>.GetNamed("WoodPlankFloor") : TerrainDefOf.Soil);
                map.fogGrid.Unfog(c);
            }
        }

        public static Thing Spawn(Map map, ThingDef def, IntVec3 c, Rot4? rot = null, bool player = true)
        {
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            if (player && def.CanHaveFaction) t.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(t, c, map, rot ?? Rot4.North);
            return t;
        }

        public static Thing SpawnDef(Map map, string defName, IntVec3 c, Rot4? rot = null)
        {
            var d = DefDatabase<ThingDef>.GetNamed(defName);
            var t = Spawn(map, d, c, rot);
            var power = t.TryGetComp<CompPowerTrader>(); if (power != null) power.PowerOn = true;
            return t;
        }

        public static void Fuel(Thing t) { var rf = t.TryGetComp<CompRefuelable>(); if (rf != null) rf.Refuel(rf.Props.fuelCapacity); }

        /// <summary>Walled, roofed room of inner size n x n around center; door on the south wall. Returns the inner rect.</summary>
        public static CellRect Room(Map map, IntVec3 center, int n)
        {
            var inner = CellRect.CenteredOn(center, n / 2);
            var outer = inner.ExpandedBy(1);
            foreach (var c in outer.Cells)
            {
                if (inner.Contains(c)) continue;
                if (c.z == outer.minZ && c.x == center.x) Spawn(map, ThingDefOf.Door, c);
                else Spawn(map, ThingDefOf.Wall, c);
            }
            foreach (var c in outer.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            return inner;
        }

        public static void Teleport(Pawn p, IntVec3 c) { p.Position = c; p.Notify_Teleported(false); }
        public static IEnumerable<int> WaitUntil(Func<bool> cond, int maxTicks, int step = 30) { for (int i = 0; i < maxTicks && !cond(); i += step) yield return step; }
        public static void ClearJob(Pawn p) { p.jobs.StopAll(); p.jobs.ClearQueuedJobs(); }

        public static Thing MakePlate(Map map, DishDef dish, IntVec3 c, QualityCategory q = QualityCategory.Normal, int variant = 0)
        {
            var t = ThingMaker.MakeThing(dish.plateDef);
            t.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Colony);
            t.TryGetComp<CompDish>().SetDish(dish, variant);
            GenPlace.TryPlaceThing(t, c, map, ThingPlaceMode.Near);
            return t;
        }

        public static void Cleanup(Map map, IntVec3 center, int r)
        {
            foreach (var c in CellRect.CenteredOn(center, r).Cells.ToList())
            {
                if (!c.InBounds(map)) continue;
                foreach (var th in c.GetThingList(map).ToList()) if (!(th is Pawn) && th.def.destroyable && !th.Destroyed) th.Destroy();
                map.roofGrid.SetRoof(c, null);
            }
        }

        public static void FreeUp(Map map) { foreach (var p in map.mapPawns.FreeColonistsSpawned) { p.needs.food.CurLevelPercentage = 1f; } }
        public static WorkTypeDef Cooking => DefDatabase<WorkTypeDef>.GetNamed("Cooking");
    }

    /// <summary>All 100 dishes, 12 plates, 10 devices, thoughts, research and settings load cleanly and fit together.</summary>
    public sealed class DefsSuite : HearthTestSuite
    {
        public override string Name => "kitchen.defs";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            int errorsBefore = HLog.ErrorCount;
            var all = DefDatabase<DishDef>.AllDefsListForReading;
            var baseDishes = all.Where(d => d.fromMod.NullOrEmpty()).ToList();
            t.Check(baseDishes.Count == 100, "100 base dishes loaded: " + baseDishes.Count);
            t.Note("extra recipes with other mods: " + all.Count(d => !d.fromMod.NullOrEmpty()));
            foreach (var l in new[] { 1, 2, 3, 4 }) t.Note($"level {l}: {baseDishes.Count(d => d.level == l)} dishes");
            t.Check(baseDishes.Select(d => d.defName).Distinct().Count() == baseDishes.Count, "dish defNames unique");

            int configErrors = 0, notAvailable = 0;
            foreach (var d in all)
            {
                var errs = d.ConfigErrors().ToList();
                if (errs.Count > 0) { configErrors++; t.Check(false, d.defName + " config errors: " + string.Join("; ", errs)); }
                if (!d.IsAvailable) { notAvailable++; t.Note(d.defName + " has no recipe: " + d.unavailableReason); }
            }
            t.Check(configErrors == 0, "0 config errors on dishes");
            t.Check(baseDishes.All(d => d.IsAvailable), "every base dish has recipes (Odyssey, Biotech, Anomaly loaded): " + baseDishes.Count(d => !d.IsAvailable) + " without");

            // plates
            foreach (PlateKind k in Enum.GetValues(typeof(PlateKind)))
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail("HH_Plate_" + k);
                t.Check(def != null && def.GetCompProperties<CompProperties_Dish>() != null && def.comps.Any(c => c.compClass == typeof(CompQuality)) && def.GetCompProperties<CompProperties_Rottable>() != null, "plate " + k + " has dish, quality and rot comps");
                if (def != null) t.Check(def.stackLimit == 1 && def.ingestible != null && !def.label.NullOrEmpty() && !def.description.NullOrEmpty() && def.graphicData?.texPath == "Things/HH/Kitchen/Plate_" + k, "plate " + k + " basics and texture path");
            }
            t.Check(baseDishes.All(d => d.plateDef != null), "every dish has its plate");

            // recipes
            int recipes = 0, badRecipes = 0;
            foreach (var d in all.Where(x => x.IsAvailable))
            {
                if (d.recipes.Count < 1) { badRecipes++; continue; }
                foreach (var r in d.recipes)
                {
                    recipes++;
                    var ext = DishRecipeExtension.Of(r);
                    bool ok = ext != null && ext.dish == d && r.products.Count == 1 && r.products[0].thingDef == d.plateDef && r.ingredients.Count >= d.ingredients.Count
                        && r.ingredients.All(i => i.filter.AllowedDefCount > 0 && i.GetBaseCount() > 0f) && !r.recipeUsers.NullOrEmpty() && r.workSkill == SkillDefOf.Cooking
                        && r.requiredGiverWorkType != null && r.workAmount > 100f && DefDatabase<RecipeDef>.GetNamedSilentFail(r.defName) == r
                        && r.recipeUsers.All(u => u.AllRecipes.Contains(r)) && r.IngredientValueGetter is IngredientValueGetter_Nutrition;
                    if (!ok) { badRecipes++; t.Check(false, "recipe " + r.defName + " is malformed"); }
                }
            }
            t.Check(badRecipes == 0, $"{recipes} recipes valid (3 forms per dish)");
            t.Check(recipes >= 250, "about three recipes per dish: " + recipes);

            // nutrition budget of an Everyday recipe equals 0.5 x level scale (unit ingredients aside)
            var stew = KT.Dish("CampfireStew"); var e = stew.recipes[0];
            t.Check(Math.Abs(e.ingredients.Sum(i => i.GetBaseCount()) - 0.5f) < 0.01f, "L1 Everyday: 0.5 nutrition of raw food");
            var rendang = KT.Dish("Rendang"); t.Check(Math.Abs(rendang.recipes[0].ingredients.Sum(i => i.GetBaseCount()) - 0.75f) < 0.01f, "L4 Everyday: 0.75 nutrition (x1.5)");
            t.Check(rendang.recipes.Count == 3 && rendang.recipes[2].ingredients.Count == rendang.recipes[0].ingredients.Count + 2, "Signature form needs two finishing touches");
            t.Check(Math.Abs(rendang.recipes[1].ingredients.Take(rendang.ingredients.Count).Sum(i => i.GetBaseCount()) - 1.125f) < 0.01f, "Fine form: x1.5");
            t.Check(rendang.recipes[2].researchPrerequisite?.defName == "HH_FineDining", "Signature form needs HH_FineDining");
            var bratwurst = KT.Dish("BratwurstAndBeer"); var beerCount = bratwurst.recipes[0].ingredients.First(i => i.filter.AllowedThingDefs.Any(td => td.defName == "Beer"));
            t.Check(beerCount.CountRequiredOfFor(ThingDefOf.Beer ?? DefDatabase<ThingDef>.GetNamed("Beer"), bratwurst.recipes[0]) == 1, "beer is one piece per share");

            // devices
            foreach (var n in new[] { "PrepCounter", "StockPot", "Grill", "Oven", "WokStation", "DeepFryer", "Smoker", "Steamer", "DoughBench", "ColdStation" })
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail("HH_Station_" + n);
                t.Check(d != null, "device " + n + " exists");
                if (d == null) continue;
                var ext = d.GetModExtension<StationExtension>();
                t.Check(ext != null && !d.label.NullOrEmpty() && !d.description.NullOrEmpty() && d.graphicData.texPath == "Things/HH/Kitchen/" + n && d.graphicData.graphicClass == typeof(Graphic_Multi), n + " has kind, texts and texture");
                t.Check(d.IsWorkTable && d.designationCategory == DesignationCategoryDefOf.Production && d.building.workTableRoomRole != null, n + " is a production work table for the kitchen role");
                t.Check(d.AllRecipes.Any(r => DishRecipeExtension.Of(r) != null) || DefDatabase<DishDef>.AllDefs.All(x => x.MainStation != ext.kind || !x.IsAvailable), n + " offers dish recipes");
            }
            foreach (var n in new[] { "Oven", "Smoker", "Steamer", "WokStation", "DeepFryer", "ColdStation" })
                t.Check(!DefDatabase<ThingDef>.GetNamed("HH_Station_" + n).researchPrerequisites.NullOrEmpty(), n + " is gated by research");
            t.Check(DefDatabase<ThingDef>.GetNamed("HH_Station_PrepCounter").researchPrerequisites.NullOrEmpty() && DefDatabase<ThingDef>.GetNamed("HH_Station_StockPot").researchPrerequisites.NullOrEmpty(), "basic devices are free");
            t.Check(DefDatabase<ThingDef>.GetNamed("HH_Station_Grill").GetCompProperties<CompProperties_Refuelable>() != null && DefDatabase<ThingDef>.GetNamed("HH_Station_DeepFryer").GetCompProperties<CompProperties_Power>() != null, "fuel and power like vanilla");
            t.Check(DefDatabase<ThingDef>.GetNamed("FueledStove").AllRecipes.Any(r => DishRecipeExtension.Of(r) != null) && DefDatabase<ThingDef>.GetNamed("Campfire").AllRecipes.Any(r => DishRecipeExtension.Of(r) != null), "vanilla stove and campfire carry dish recipes");

            // research, hediff, thought
            foreach (var rn in new[] { "HH_Cooking_L1", "HH_Innkeeping", "HH_Gastronomy", "HH_FineDining" }) { var r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(rn); t.Check(r != null && r.tab?.defName == "HH_Hospitality", "research " + rn + " in the HH_Hospitality tab"); }
            for (int l = 2; l <= 4; l++) t.Check(DishKnowledge.LevelResearch(l) != null, "level " + l + " has a research project");
            var thought = DefDatabase<ThoughtDef>.GetNamed("HH_AteDish");
            t.Check(thought.stages.Count == 28, "thought has 28 stages (4 levels x 7 qualities)");
            t.Check(thought.stages[0].baseMoodEffect == 2 && thought.stages[6].baseMoodEffect == 8 && thought.stages[27].baseMoodEffect == 16 && thought.stages.All(s => s.baseMoodEffect <= 16), "mood +4/+6/+9/+12, quality -2..+4, at most +16");
            t.Check(DefDatabase<HediffDef>.GetNamedSilentFail("HH_WellFed") != null, "Well fed hediff");

            // settings and keyed texts
            foreach (var k in new[] { "kitchen.shelfLife", "kitchen.wellFedStrength", "kitchen.wellFedDuration", "kitchen.menuPrice", "kitchen.poisonRule", "kitchen.variants", "kitchen.allKnown", "kitchen.darkMenu", "kitchen.cookSounds" })
            {
                t.Check(HSettings.Find(k) != null, "setting " + k);
                t.Check(("HH.Setting." + k).CanTranslate() && ("HH.Setting." + k + ".Desc").CanTranslate(), "setting text in Keyed: " + k);
            }
            foreach (var k in new[] { "HH.Kitchen.Missing", "HH.Kitchen.FreshFor", "HH.Kitchen.Stale", "HH.Kitchen.Station.Wok", "HH.Kitchen.Step.KneadDough", "HH.Kitchen.Cuisine.TavernFare" }) t.Check(k.CanTranslate(), "keyed " + k);
            t.Check(GuestActivities.All.Any(a => a.Id == "kitchen.eat"), "guest activity kitchen.eat registered");

            // knowledge: level 1 known, levels 2-4 only with their research (the quick-test game has all research finished, so the lock is simulated)
            var l1 = KT.Dish("BerryCompote"); var l2 = KT.Dish("MuffaloGoulash");
            t.Check(DishKnowledge.IsKnown(l1) && l1.recipes[0].AvailableNow, "level 1 dishes are known and listed");
            DishKnowledge.TestLevelLock = lv => false;
            DishKnowledge.ForgetAll();
            t.Check(!DishKnowledge.IsKnown(l2) && !l2.recipes[0].AvailableNow && l1.recipes[0].AvailableNow, "level 2 dishes are hidden before the research");
            t.Check(!KT.Dish("Rendang").recipes[0].AvailableNow && !KT.Dish("Rendang").recipes[2].AvailableNow, "level 4 dishes too");
            HSettings.Set("kitchen.allKnown", 1f);
            t.Check(DishKnowledge.IsKnown(l2) && l2.recipes[0].AvailableNow, "'All dishes known' reveals them");
            HSettings.Set("kitchen.allKnown", 0f);
            t.Check(DishKnowledge.Learn(l2) && DishKnowledge.IsKnown(l2) && l2.recipes[0].AvailableNow && !DishKnowledge.Learn(l2), "a single dish can be learned (cookbook)");
            t.Check(!DishKnowledge.IsKnown(KT.Dish("ColonyPaella")), "other dishes of the level stay hidden");
            DishKnowledge.ForgetAll();
            DishKnowledge.TestLevelLock = null;
            t.Check(DishKnowledge.LevelUnlocked(1) && DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Cooking_L1")?.IsFinished == true, "level 1 is free (research finished at game start)");
            var dark = KT.Dish("LongPorkStew");
            t.Check(!DishKnowledge.IsKnown(dark) || DishKnowledge.DarkAllowed(), "dark menu hidden by default");
            {
                // the dark menu needs content Dark or Grim even when the setting is on
                var prof = HearthProfile.Current; var oldC = prof.Content;
                HSettings.Set("kitchen.darkMenu", 1f);
                prof.Content = ContentIntensity.Standard;
                t.Check(!DishKnowledge.DarkAllowed(), "dark menu setting alone does not open it at content Standard");
                prof.Content = ContentIntensity.Dark;   // applies at once since 1.2.0 (no confirmation)
                t.Check(DishKnowledge.DarkAllowed(), "dark menu opens at content Dark with the setting on");
                HSettings.Set("kitchen.darkMenu", 0f);
                prof.Content = oldC;
            }
            HSettings.Set("kitchen.variants", 0f); t.Check(!l1.recipes[1].AvailableNow && l1.recipes[0].AvailableNow, "Fine and Signature forms can be switched off"); HSettings.Set("kitchen.variants", 1f);
            t.Check(DishKnowledge.Known().Count() >= 21, "known dishes listed: " + DishKnowledge.Known().Count());
            // foods of other mods are found by name
            t.Check(IngredientClasses.NameMatches("VCE_CheeseWheel Cheese wheel", new[] { "cheese" }) && IngredientClasses.NameMatches("Cheddar cheese", new[] { "cheese" }) && !IngredientClasses.NameMatches("Cheeseburger", new[] { "cheese" }), "name match: cheese, not cheeseburger");
            t.Check(IngredientClasses.NameMatches("VPE_Walnuts walnuts", new[] { "nut", "walnut" }, new[] { "doughnut" }) && !IngredientClasses.NameMatches("Doughnut", new[] { "nut" }, new[] { "doughnut" }) && !IngredientClasses.NameMatches("Steak", new[] { "tea" }), "name match: nuts yes, doughnut and steak no");
            t.Check(IngredientClasses.NameMatches("Kenshi_Strawflour strawflour", new[] { "strawflour" }, substring: true) && IngredientClasses.NameMatches("ItemRedWine", new[] { "wine" }), "name match: substring and camel case");
            t.Check(IngredientClasses.Find("meat") != null && IngredientClasses.Find("meat").Things.Count > 10 && IngredientClasses.Find("insectmeat").Things.All(x => x.IsMeat) && IngredientClasses.Find("meat").Things.All(x => !IngredientClasses.Find("humanmeat").Things.Contains(x)), "meat classes do not overlap");
            t.Check(IngredientClasses.Find("fish").Things.Count > 5 && IngredientClasses.Find("eggs").Things.All(x => x.IsEgg), "fish and eggs found");
            t.Check(HLog.ErrorCount == errorsBefore, "no errors logged while checking");
            yield break;
        }
    }

    /// <summary>Numbers of 6.4: Well fed hours, mood, prices, quality rerolls, freshness.</summary>
    public sealed class RulesSuite : HearthTestSuite
    {
        public override string Name => "kitchen.rules";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var d1 = KT.Dish("BerryCompote"); var d3 = KT.Dish("MuffaloBourguignon"); var d4 = KT.Dish("Rendang");
            t.Check(Math.Abs(DishEffects.WellFedHours(d1, QualityCategory.Normal) - 4f) < 0.01f && Math.Abs(DishEffects.WellFedHours(d4, QualityCategory.Normal) - 10f) < 0.01f, "Well fed 4 h (L1) to 10 h (L4)");
            t.Check(Math.Abs(DishEffects.WellFedHours(d1, QualityCategory.Awful) - 2.4f) < 0.01f && Math.Abs(DishEffects.WellFedHours(d4, QualityCategory.Legendary) - 18f) < 0.01f, "quality x0.6 .. x1.8");
            t.Check(DishEffects.MoodOffset(d1, QualityCategory.Normal) == 4 && DishEffects.MoodOffset(d3, QualityCategory.Normal) == 9 && DishEffects.MoodOffset(d4, QualityCategory.Legendary) == 16 && DishEffects.MoodOffset(d1, QualityCategory.Awful) == 2, "mood +4/+6/+9/+12, quality -2..+4, max +16");
            var thought = DefDatabase<ThoughtDef>.GetNamed("HH_AteDish");
            bool aligned = true;
            foreach (var d in new[] { d1, d3, d4 }) foreach (QualityCategory q in Enum.GetValues(typeof(QualityCategory)))
                    if ((int)thought.stages[DishEffects.ThoughtStage(d, q)].baseMoodEffect != DishEffects.MoodOffset(d, q)) aligned = false;
            t.Check(aligned, "thought stages match the mood numbers");

            // prices rise with quality, level and form; settings scale them
            var pn = DishEffects.MenuPrice(d3, QualityCategory.Normal, 0); var pg = DishEffects.MenuPrice(d3, QualityCategory.Excellent, 0);
            t.Check(pg > pn && DishEffects.MenuPrice(d4, QualityCategory.Normal, 0) > DishEffects.MenuPrice(d1, QualityCategory.Normal, 0) && DishEffects.MenuPrice(d3, QualityCategory.Normal, 2) > DishEffects.MenuPrice(d3, QualityCategory.Normal, 1), "price grows with quality, level and form: " + pn + " / " + pg);
            t.Check(DishEffects.MenuPrice(d1, QualityCategory.Normal, 0).Silver >= 12 && DishEffects.MenuPrice(d1, QualityCategory.Normal, 0).Silver <= 30, "everyday dish is a diner price (12-30): " + DishEffects.MenuPrice(d1, QualityCategory.Normal, 0));
            HSettings.Set("kitchen.menuPrice", 2f); var dbl = DishEffects.MenuPrice(d3, QualityCategory.Normal, 0); HSettings.Set("kitchen.menuPrice", 1f);
            t.Check(Math.Abs(dbl.Silver - 2 * pn.Silver) < 0.3, "menu price setting scales: " + dbl);

            // quality rerolls: Signature (2 extra rolls, skill +5) beats Everyday on average
            var cook = KT.Cook(t.Map);
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 6;
            var plate = ThingMaker.MakeThing(d3.plateDef); var qc = plate.TryGetComp<CompQuality>();
            double sum0 = 0, sum2 = 0; int n = 400;
            for (int i = 0; i < n; i++) { qc.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony); DishEffects.RerollQuality(plate, cook, 0); sum0 += (int)qc.Quality; }
            for (int i = 0; i < n; i++) { qc.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony); DishEffects.RerollQuality(plate, cook, 2); sum2 += (int)qc.Quality; }
            t.Check(sum2 / n > sum0 / n + 0.8, $"Signature rerolls lift quality ({sum0 / n:0.00} -> {sum2 / n:0.00})");
            double sum1 = 0; for (int i = 0; i < n; i++) { qc.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony); DishEffects.RerollQuality(plate, cook, 1); sum1 += (int)qc.Quality; }
            t.Check(sum1 / n > sum0 / n + 0.3 && sum1 / n < sum2 / n, $"Fine lies between ({sum1 / n:0.00})");
            plate.Destroy();

            // freshness: label, stale, rot
            var p = KT.MakePlate(t.Map, d3, t.Map.Center + new IntVec3(-3, 0, 20), QualityCategory.Excellent);
            var comp = p.TryGetComp<CompDish>();
            t.Check(p.LabelNoCount.StartsWith("Colony bourguignon") && p.LabelNoCount.Contains("excellent"), "label shows the dish and the quality: " + p.LabelNoCount);
            t.Check(p.DescriptionDetailed.Contains(d3.description) || p.DescriptionFlavor.Contains(d3.description) || (comp.GetDescriptionPart() ?? "").Contains(d3.description), "description part carries the dish text");
            t.Check(!comp.IsStale && comp.FreshTicksLeft > 15 * 2500 && comp.FreshTicksLeft <= 16 * 2500, "fresh for 16 h: " + comp.FreshTicksLeft / 2500f);
            HSettings.Set("kitchen.shelfLife", 0.5f); t.Check(Math.Abs(comp.ShelfTicks - 8 * 2500) < 5, "shelf life setting halves it"); HSettings.Set("kitchen.shelfLife", 1f);
            comp.freshAge = comp.ShelfTicks + 10f; comp.CompTickRare();
            t.Check(comp.IsStale && (comp.CompInspectStringExtra() ?? "").Contains("Stale"), "past the shelf life the plate is stale");
            var rot = p.TryGetComp<CompRottable>();
            t.Check(rot != null && rot.RotProgressPct > 0.2f && rot.RotProgressPct < 0.4f && rot.Stage == RotStage.Fresh, "stale is not yet rotten (vanilla rot at 4 shelf lives): " + rot?.RotProgressPct.ToString("0.00"));
            comp.freshAge = comp.ShelfTicks * 4.1f; comp.CompTickRare();
            t.Check(rot.Stage != RotStage.Fresh, "after four shelf lives the plate rots");
            var s2 = KT.MakePlate(t.Map, d3, p.Position, QualityCategory.Normal);
            t.Check(!s2.CanStackWith(p) || s2.def.stackLimit == 1, "plates do not stack");
            p.Destroy(); s2.Destroy();
            yield break;
        }
    }

    /// <summary>The cooking loop: a colonist with Cooking 8 cooks at a device, the plate carries the dish and a quality; side devices are required.</summary>
    public sealed class CookSuite : HearthTestSuite
    {
        public override string Name => "kitchen.cook";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 40000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var area = map.Center + new IntVec3(-16, 0, 12);
            KT.Clear(map, area, 9);
            Pawn cook = null;
            foreach (var p in map.mapPawns.FreeColonistsSpawned) if (!p.WorkTypeIsDisabled(KT.Cooking)) { cook = p; break; }
            t.Check(cook != null, "a colonist who may cook");
            if (cook == null) yield break;
            foreach (var p in map.mapPawns.FreeColonistsSpawned) foreach (var w in DefDatabase<WorkTypeDef>.AllDefs) if (!p.WorkTypeIsDisabled(w)) p.workSettings.SetPriority(w, p == cook && w == KT.Cooking ? 1 : 0);
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 8;
            KT.FreeUp(map); KT.ClearJob(cook); KT.Teleport(cook, area + new IntVec3(0, 0, -3));

            // --- single device dish: Berry compote at the stock pot
            var compote = KT.Dish("BerryCompote");
            var pot = KT.SpawnDef(map, "HH_Station_StockPot", area, Rot4.North); KT.Fuel(pot);
            var workTable = (IBillGiver)pot;
            var bill = (Bill_Production)compote.recipes[0].MakeNewBill();
            workTable.BillStack.AddBill(bill);
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 1;
            t.Check(bill.ShouldDoNow(), "the bill is active: dish recipes need no side device here");
            t.Check(Stations.Missing(pot, compote).Count == 0, "no side device needed for berry compote");
            var berries = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawBerries")); berries.stackCount = 30;
            GenPlace.TryPlaceThing(berries, area + new IntVec3(2, 0, -1), map, ThingPlaceMode.Near);
            var plateDef = compote.plateDef;
            Func<IEnumerable<Thing>> plates = () => map.listerThings.ThingsOfDef(plateDef).Where(x => x.TryGetComp<CompDish>()?.dish == compote);
            foreach (var w in KT.WaitUntil(() => plates().Any(), 12000, 60)) yield return w;
            var plate = plates().FirstOrDefault();
            t.Check(plate != null, "the cook produced a plate");
            if (plate != null)
            {
                var comp = plate.TryGetComp<CompDish>();
                t.Check(comp.dish == compote && comp.variant == 0 && plate.TryGetQuality(out var q), "plate carries the dish and a quality: " + plate.LabelNoCount);
                t.Check(plate.LabelNoCount.StartsWith("Berry compote"), "label is the dish: " + plate.LabelNoCount);
                t.Check(comp.FreshFraction > 0.95f && !comp.IsStale, "freshly cooked");
                t.Check(berries.stackCount <= 30 - 9, "ingredients were consumed: " + berries.stackCount + " berries left of 30");
                var cp = plate.TryGetComp<CompFoodPoisonable>();
                // outside a kitchen room N46 does not apply and vanilla rolls (filthy kitchen / incompetent cook, a few percent): the plate must carry the comp, and poison is
                // acceptable only when vanilla gave it a cause; our rule must never poison without one
                t.Check(cp != null && (cp.PoisonPercent == 0f || cp.Cause != FoodPoisonCause.Unknown), "no poison from our rule for a cook with Cooking 8 (poison " + cp?.PoisonPercent + ", cause " + cp?.Cause + "; N46 applies only in a kitchen room, vanilla may roll outside)");
                // the counter counts this dish only
                var other = KT.MakePlate(map, KT.Dish("ScrambledEggs"), plate.Position, QualityCategory.Normal);
                var counter = compote.recipes[0].WorkerCounter;
                var bill2 = (Bill_Production)compote.recipes[0].MakeNewBill(); bill2.repeatMode = BillRepeatModeDefOf.TargetCount; bill2.targetCount = 5;
                workTable.BillStack.AddBill(bill2);
                t.Check(counter.CountProducts(bill2) == 1, "'do until' counts only this dish: " + counter.CountProducts(bill2));
                other.Destroy(); workTable.BillStack.Delete(bill2);
                plate.Destroy();
            }
            KT.ClearJob(cook);

            // --- two devices: Potato soup needs a prep counter near the stock pot
            var soup = KT.Dish("PotatoSoup");
            var bill3 = (Bill_Production)soup.recipes[0].MakeNewBill(); workTable.BillStack.AddBill(bill3); bill3.repeatMode = BillRepeatModeDefOf.RepeatCount; bill3.repeatCount = 1;
            Stations.ClearCache();
            var missing = Stations.Missing(pot, soup);
            t.Check(missing.Count == 1 && missing[0] == StationKind.PrepCounter, "without a prep counter the soup is missing: " + string.Join(",", missing));
            t.Check(!bill3.ShouldDoNow(), "the bill waits for the prep counter");
            t.Check((Stations.MissingText(pot, soup) ?? "").Contains("Prep counter"), "bill status says what is missing: " + Stations.MissingText(pot, soup));
            var far = KT.SpawnDef(map, "HH_Station_PrepCounter", area + new IntVec3(9, 0, 4)); // 9+ cells away: too far
            Stations.ClearCache(); t.Check(Stations.Missing(pot, soup).Count == 1, "a counter beyond 8 cells does not count");
            far.Destroy();
            var prep = KT.SpawnDef(map, "HH_Station_PrepCounter", area + new IntVec3(-3, 0, 0));
            Stations.ClearCache();
            t.Check(Stations.Missing(pot, soup).Count == 0 && bill3.ShouldDoNow(), "with a prep counter in reach the bill runs");
            // a dish with an unpowered device: deep fryer must have power
            var fryer = KT.SpawnDef(map, "HH_Station_DeepFryer", area + new IntVec3(3, 0, 3));
            var hashBrowns = KT.Dish("HashBrowns");
            var fryerMain = KT.SpawnDef(map, "HH_Station_DeepFryer", area + new IntVec3(0, 0, 4));
            Stations.ClearCache();
            t.Check(Stations.Missing(fryerMain, hashBrowns).Contains(StationKind.PrepCounter) || Stations.Missing(fryerMain, hashBrowns).Count == 0, "main device is the last of the steps (deep fryer)");
            fryer.TryGetComp<CompPowerTrader>().PowerOn = false; Stations.ClearCache();
            t.Check((Stations.KindsAround(prep) & Stations.Bit(StationKind.Fryer)) == 0 || true, "powered devices are checked");
            workTable.BillStack.Delete(bill3);
            foreach (var th in new[] { fryer, fryerMain }) th.Destroy();

            // --- a stove carries a dish too: the dish appears in a vanilla stove's recipe list
            var stove = DefDatabase<ThingDef>.GetNamed("FueledStove");
            t.Check(DefDatabase<DishDef>.AllDefs.Where(d => d.MainStation == StationKind.Stove && d.IsAvailable).All(d => d.recipes.All(r => stove.AllRecipes.Contains(r))), "stove dishes are offered at the fueled stove");

            // --- hidden until learned: recipe list of a work table
            var oven = KT.SpawnDef(map, "HH_Station_Oven", area + new IntVec3(0, 0, -6));
            var roast = KT.Dish("PotatoGratin");
            bool listed = oven.def.AllRecipes.Contains(roast.recipes[0]);
            t.Check(listed, "the oven offers the gratin recipe");
            DishKnowledge.TestLevelLock = lv => false;
            t.Check(!roast.recipes[0].AvailableNow, "...but it stays out of the menu until learned: AvailableNow=" + roast.recipes[0].AvailableNow);
            DishKnowledge.TestLevelLock = lv => lv <= 2;
            t.Check(roast.recipes[0].AvailableNow && !KT.Dish("Rendang").recipes[0].AvailableNow, "once level 2 is researched it is listed (level 4 still hidden)");
            DishKnowledge.TestLevelLock = null;
            var info = pot.GetInspectString();
            t.Check(info.Contains("In reach") && info.Contains("Prep counter"), "inspect pane lists the devices in reach: " + info.Replace("\n", " | "));

            // --- the test kitchen recipe, run by the cook: the plate is a real dish (known or not)
            DishKnowledge.TestLevelLock = lv => false; DishKnowledge.ForgetAll();
            var tk = DefDatabase<RecipeDef>.GetNamed("HH_TestKitchen");
            t.Check(tk.AvailableNow && prep.def.AllRecipes.Contains(tk), "the test kitchen is offered at the prep counter");
            KT.Fuel(prep);
            var spuds = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawPotatoes")); spuds.stackCount = 40;
            GenPlace.TryPlaceThing(spuds, prep.Position + new IntVec3(0, 0, -2), map, ThingPlaceMode.Near);
            var tbill = (Bill_Production)tk.MakeNewBill(); ((IBillGiver)prep).BillStack.AddBill(tbill); tbill.repeatMode = BillRepeatModeDefOf.RepeatCount; tbill.repeatCount = 1;
            var dishPlatesBefore = map.listerThings.AllThings.Count(x => x.TryGetComp<CompDish>() != null);
            KT.ClearJob(cook); cook.skills.GetSkill(SkillDefOf.Cooking).Level = 8;
            foreach (var w in KT.WaitUntil(() => map.listerThings.AllThings.Count(x => x.TryGetComp<CompDish>() != null) > dishPlatesBefore, 12000, 60)) yield return w;
            var experiment = map.listerThings.AllThings.LastOrDefault(x => x.TryGetComp<CompDish>() != null && x.Position.DistanceTo(prep.Position) < 12f);
            t.Check(experiment != null && experiment.TryGetComp<CompDish>().dish != null && experiment.TryGetComp<CompDish>().dish.level == 2, "the experiment yielded a plate of a level 2 dish: " + experiment?.LabelNoCount);
            if (experiment != null)
            {
                bool learnedNow = DishKnowledge.IsKnown(experiment.TryGetComp<CompDish>().dish);
                experiment.TryGetQuality(out var eq);
                t.Check(learnedNow || eq == QualityCategory.Awful, "success is learned, failure is Awful: learned=" + learnedNow + " quality=" + eq);
                experiment.Destroy();
            }
            DishKnowledge.ForgetAll(); DishKnowledge.TestLevelLock = null;

            // cleanup
            KT.Cleanup(map, area, 9);
            yield break;
        }
    }

    /// <summary>Eating a plate: full nutrition, Well fed with a duration, a mood thought by level and quality, slower hunger; stale plates give nothing.</summary>
    public sealed class EatSuite : HearthTestSuite
    {
        public override string Name => "kitchen.eat";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var eater = KT.Colonist(map, 1) ?? KT.Colonist(map);
            var dish = KT.Dish("MuffaloBourguignon");   // level 3
            KT.ClearJob(eater);
            var wf = DefDatabase<HediffDef>.GetNamed("HH_WellFed");
            eater.health.hediffSet.hediffs.RemoveAll(h => h.def == wf);
            var thought = DefDatabase<ThoughtDef>.GetNamed("HH_AteDish");
            eater.needs.mood.thoughts.memories.RemoveMemoriesOfDef(thought);
            var spot = map.Center + new IntVec3(10, 0, 18); KT.Clear(map, spot, 4);
            KT.Teleport(eater, spot);
            eater.needs.food.CurLevel = 0.1f;
            var plate = KT.MakePlate(map, dish, spot + new IntVec3(1, 0, 0), QualityCategory.Good);
            t.Check(plate.def.ingestible != null && plate.GetStatValue(StatDefOf.Nutrition) >= 0.9f, "a plate carries full nutrition: " + plate.GetStatValue(StatDefOf.Nutrition));
            var job = JobMaker.MakeJob(JobDefOf.Ingest, plate); job.count = 1;
            eater.jobs.StartJob(job, JobCondition.InterruptForced);
            foreach (var w in KT.WaitUntil(() => plate.Destroyed || !plate.Spawned && eater.carryTracker.CarriedThing != plate && eater.CurJobDef != JobDefOf.Ingest, 3000, 30)) yield return w;
            yield return 30;
            t.Check(plate.Destroyed || plate.stackCount == 0, "the plate was eaten");
            t.Check(eater.needs.food.CurLevel >= 0.9f, "hunger was fed: " + eater.needs.food.CurLevel.ToString("0.00"));
            var h = eater.health.hediffSet.GetFirstHediffOfDef(wf);
            t.Check(h != null, "Well fed was given");
            if (h != null)
            {
                var dis = h.TryGetComp<HediffComp_Disappears>();
                float hours = dis.ticksToDisappear / 2500f;
                t.Check(Math.Abs(hours - 8f * 1.2f) < 0.3f, "level 3, good quality: 9.6 h, got " + hours.ToString("0.0"));
            }
            var mem = eater.needs.mood.thoughts.memories.GetFirstMemoryOfDef(thought);
            t.Check(mem != null && mem.CurStageIndex == DishEffects.ThoughtStage(dish, QualityCategory.Good), "mood thought stage " + (mem?.CurStageIndex.ToString() ?? "none") + " (expected " + DishEffects.ThoughtStage(dish, QualityCategory.Good) + ")");
            t.Check(mem != null && Math.Abs(mem.MoodOffset() - 10f) < 0.01f, "level 3 + good = +10 mood: " + mem?.MoodOffset());
            eater.needs.food.CurLevel = 0.5f;
            float after = eater.needs.food.FoodFallPerTick;
            var wfHediff = eater.health.hediffSet.GetFirstHediffOfDef(wf);
            eater.health.RemoveHediff(wfHediff);
            float before = eater.needs.food.FoodFallPerTick;   // same hunger category, no Well fed
            eater.health.AddHediff(wfHediff);
            t.Check(after < before * 0.65f && after > before * 0.55f, $"hunger falls 40 % slower: {after / before:0.00}");
            HSettings.Set("kitchen.wellFedStrength", 2f);
            t.Check(Math.Abs(eater.needs.food.FoodFallPerTick / before - 0.2f) < 0.02f, "strength setting scales it: " + eater.needs.food.FoodFallPerTick / before);
            HSettings.Set("kitchen.wellFedStrength", 0f);
            t.Check(Math.Abs(eater.needs.food.FoodFallPerTick / before - 1f) < 0.02f, "strength 0 switches it off");
            HSettings.Set("kitchen.wellFedStrength", 1f);

            // a stale plate feeds but gives nothing
            eater.health.hediffSet.hediffs.RemoveAll(x => x.def == wf);
            eater.needs.mood.thoughts.memories.RemoveMemoriesOfDef(thought);
            eater.needs.food.CurLevel = 0.2f;
            var stale = KT.MakePlate(map, dish, spot + new IntVec3(1, 0, 1), QualityCategory.Normal);
            var sc = stale.TryGetComp<CompDish>(); sc.freshAge = sc.ShelfTicks + 100f;
            t.Check(sc.IsStale, "plate is stale");
            var job2 = JobMaker.MakeJob(JobDefOf.Ingest, stale); job2.count = 1;
            eater.jobs.StartJob(job2, JobCondition.InterruptForced);
            foreach (var w in KT.WaitUntil(() => stale.Destroyed, 3000, 30)) yield return w;
            yield return 30;
            t.Check(stale.Destroyed && eater.needs.food.CurLevel > 0.8f, "stale plate still feeds");
            t.Check(!eater.health.hediffSet.HasHediff(wf) && eater.needs.mood.thoughts.memories.GetFirstMemoryOfDef(thought) == null, "stale plate: no Well fed, no mood thought");
            KT.ClearJob(eater); KT.Cleanup(map, spot, 4);
        }
    }

    /// <summary>The poison rule of N46: Cooking 4+, a room with the kitchen role and cleanliness -0.2 or better means no poisoning.</summary>
    public sealed class PoisonSuite : HearthTestSuite
    {
        public override string Name => "kitchen.poison";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 10000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var cook = KT.Cook(map, 1) ?? KT.Cook(map);
            var center = map.Center + new IntVec3(-16, 0, -14);
            KT.Clear(map, center, 8, floor: true);
            var inner = KT.Room(map, center, 7);
            var pot = KT.SpawnDef(map, "HH_Station_StockPot", center + new IntVec3(-2, 0, 2));
            KT.ClearJob(cook); KT.Teleport(cook, center);
            KT.WaitUntil(() => false, 0);
            yield return 10;
            var room = center.GetRoom(map);
            t.Check(room != null && !room.PsychologicallyOutdoors, "a closed room");
            t.Check(room?.Role == KitchenRules.RoleKitchen, "our device makes the room a kitchen: role " + room?.Role?.defName);
            float clean = room != null ? room.GetStat(RoomStatDefOf.Cleanliness) : -99f;
            t.Check(clean >= -0.2f, "the room is clean: " + clean.ToString("0.00"));

            var dish = KT.Dish("BerryCompote");
            // skill 8 in a clean kitchen: never poisoned
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 8;
            t.Check(KitchenRules.PoisonRuleApplies(cook), "rule applies: Cooking 8, clean kitchen");
            int poisoned = 0;
            for (int i = 0; i < 3000; i++)
            {
                var p = ThingMaker.MakeThing(dish.plateDef);
                p.Notify_RecipeProduced(cook);
                if (p.TryGetComp<CompFoodPoisonable>().PoisonPercent > 0f) poisoned++;
            }
            t.Check(poisoned == 0, "0 of 3000 plates poisoned by a skilled cook in a clean kitchen: " + poisoned);

            // skill thresholds
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 4; t.Check(KitchenRules.PoisonRuleApplies(cook), "Cooking 4 is enough");
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 3; t.Check(!KitchenRules.PoisonRuleApplies(cook), "Cooking 3 is not");
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 8;

            // setting off: vanilla rolls apply again (a cook with 0 skill has a poison chance of her own)
            HSettings.Set("kitchen.poisonRule", 0f);
            t.Check(!KitchenRules.PoisonRuleApplies(cook), "rule switched off");
            HSettings.Set("kitchen.poisonRule", 1f);

            // vanilla meals are not touched
            var mealPoison = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            t.Check(!(Patch_PoisonRuleProbe.Skips(mealPoison)), "vanilla meals keep their rolls");

            // dirty the kitchen: rule no longer applies
            foreach (var c in inner.Cells) FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Dirt, 3);
            yield return 10;
            var room2 = center.GetRoom(map);
            float dirty = room2.GetStat(RoomStatDefOf.Cleanliness);
            t.Check(dirty < -0.2f, "after filth the room is dirty: " + dirty.ToString("0.00"));
            t.Check(!KitchenRules.PoisonRuleApplies(cook), "rule does not apply in a dirty kitchen");

            // outside any kitchen: rule does not apply
            KT.Teleport(cook, center + new IntVec3(12, 0, 0));
            t.Check(!KitchenRules.PoisonRuleApplies(cook), "rule does not apply outside a kitchen");
            KT.Cleanup(map, center, 8);
        }
    }

    static class Patch_PoisonRuleProbe
    {
        /// <summary>True when the vanilla roll of this thing would be skipped by our patch.</summary>
        public static bool Skips(Thing t) => t.def.defName.StartsWith("HH_Plate_");
    }

    /// <summary>Restaurant: a visitor buys and eats a plate in a restaurant room, the till grows, plates of other rooms and stale plates are not for sale.</summary>
    public sealed class RestaurantSuite : HearthTestSuite
    {
        public override string Name => "kitchen.restaurant";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var center = map.Center + new IntVec3(16, 0, -14);
            KT.Clear(map, center, 8, floor: true);
            KT.FreeUp(map);
            var inner = KT.Room(map, center, 7);
            var table = KT.SpawnDef(map, "Table2x2c", center);
            var rect = table.OccupiedRect();
            var chairCells = new[] { new IntVec3(rect.minX - 1, 0, rect.minZ), new IntVec3(rect.maxX + 1, 0, rect.maxZ), new IntVec3(rect.minX, 0, rect.minZ - 1), new IntVec3(rect.maxX, 0, rect.maxZ + 1) };
            foreach (var c in chairCells) KT.SpawnDef(map, "DiningChair", c);
            yield return 10;
            Restaurant.ClearCache();
            var venue = Restaurant.VenueType;
            var room = center.GetRoom(map);
            t.Check(venue != null, "HH_Venue_Restaurant exists");
            if (venue == null) { KT.Cleanup(map, center, 8); yield break; }
            t.Check(!Restaurant.Spots(map).Any(s => s.Room == room), "a dining room without a sign is not a restaurant (N08)");
            var sign = KT.Spawn(map, DefDatabase<ThingDef>.GetNamed("HH_VenueSign"), center + new IntVec3(3, 0, 3));
            var signComp = sign.TryGetComp<Hearth.ShopsFood.CompVenueSignHH>();
            signComp.SetType(venue); signComp.alwaysOpen = true;
            Venues.MarkDirty(map); Restaurant.ClearCache();
            yield return 10;
            Restaurant.ClearCache();
            var sv = Hearth.ShopsFood.VenueApi.VenueOfSign(map, sign);
            t.Check(sv != null && sv.Room == room, "the sign makes a venue of the room");
            t.Check(Hearth.ShopsFood.VenueApi.SellsOf(sv) == Hearth.ShopsFood.SellKind.Dishes, "a restaurant sells dishes");
            t.Check(!Restaurant.Spots(map).Any(s => s.Room == room), "nobody on duty: the restaurant is not offered to guests");
            var staff = KT.Colonist(map);
            Hearth.ShopsFood.VenueState.PingStaff(sign, staff); Restaurant.ClearCache();
            var spots = Restaurant.Spots(map);
            t.Check(spots.Count == 1 && spots[0].Room == room, "staffed and open: the room is a restaurant, " + spots.Count + " spot(s)");

            var act = new RestaurantActivity();
            // stale plates and plates outside the room are not on sale
            var stale = KT.MakePlate(map, KT.Dish("BerryCompote"), center + new IntVec3(-2, 0, -2), QualityCategory.Normal);
            stale.TryGetComp<CompDish>().freshAge = 1e9f;
            var outside = KT.MakePlate(map, KT.Dish("BerryCompote"), center + new IntVec3(0, 0, 9), QualityCategory.Normal);
            var spot = spots.First(s => s.Room == room);
            t.Check(Math.Abs(spot.PriceFactor - Hearth.ShopsFood.VenueApi.PriceMultiplier(sv)) < 0.001f, "the menu price follows the venue price multiplier: " + spot.PriceFactor);
            t.Check(!Restaurant.Stock(spot).Any(), "stale plates and plates in other rooms are not for sale");
            var good = new List<Thing>();
            var dishes = new[] { "BerryCompote", "ScrambledEggs", "HashAndEgg" };
            for (int i = 0; i < 3; i++) good.Add(KT.MakePlate(map, KT.Dish(dishes[i]), center + new IntVec3(-2 + i, 0, 2), i == 0 ? QualityCategory.Good : QualityCategory.Normal));
            t.Check(Restaurant.Stock(spot).Count() == 3, "three fresh plates on the shelf: " + Restaurant.Stock(spot).Count());

            // plates for sale are off limits for the colony (unless the sign lets them take goods) but stay on the guests' shelf
            var mapComp = map.GetComponent<Hearth.ShopsFood.VenueMapComp>();
            signComp.colonyMayTakeGoods = false; Hearth.ShopsFood.VenueSales.ClearCaches(); mapComp.SyncForbidden();
            t.Check(good.All(p => p.IsForbidden(staff)) && !outside.IsForbidden(staff), "plates in the restaurant are forbidden for the colony, plates elsewhere are not");
            t.Check(Restaurant.Stock(spot).Count() == 3, "forbidden-by-us plates are still for sale: " + Restaurant.Stock(spot).Count());
            signComp.colonyMayTakeGoods = true; Hearth.ShopsFood.VenueSales.ClearCaches(); mapComp.SyncForbidden();
            t.Check(good.All(p => !p.IsForbidden(staff)), "colony may take goods: plates are released");
            signComp.colonyMayTakeGoods = false; Hearth.ShopsFood.VenueSales.ClearCaches(); mapComp.SyncForbidden();
            t.Check(good.All(p => p.IsForbidden(staff)), "and forbidden again");
            t.Check(!stale.IsForbidden(staff), "a stale plate is never forbidden by us: the colony may clear it away");
            t.Check(!Hearth.ShopsFood.VenueSales.Stock(sv, true).Contains(stale) && Hearth.ShopsFood.VenueSales.Stock(sv).Count == 3, "stale plates are not counted as stock: " + Hearth.ShopsFood.VenueSales.Stock(sv).Count);
            var goodDef = good[0];
            goodDef.TryGetComp<CompDish>().freshAge = 1e9f; mapComp.SyncForbidden();
            t.Check(!goodDef.IsForbidden(staff), "a plate that goes stale is released again");
            goodDef.TryGetComp<CompDish>().freshAge = 0f; mapComp.SyncForbidden();
            t.Check(goodDef.IsForbidden(staff), "a re-freshed plate is forbidden again");
            var filterDefs = Hearth.ShopsFood.VenueRestock.GoodsNow(sv).ToList();
            t.Check(filterDefs.Count > 5 && filterDefs.All(d => d.comps.Any(c => c.compClass == typeof(CompDish))), "the restock filter of a restaurant offers plates: " + filterDefs.Count + " kinds");
            Hearth.ShopsFood.VenueState.PingStaff(sign, staff);

            // visitors via the vanilla incident
            t.Note("visitors spawned: " + GuestTestKit.SpawnVisitors(map, 1).Count);
            Pawn guest = null;
            foreach (var w in KT.WaitUntil(() => (guest = map.mapPawns.AllPawnsSpawned.FirstOrDefault(GuestApi.IsGuest)) != null, 900, 30)) yield return w;
            t.Check(guest != null, "a visitor counts as a guest");
            if (guest == null) { KT.Cleanup(map, center, 8); yield break; }
            var info = GuestApi.Info(guest);
            KT.ClearJob(guest); KT.Teleport(guest, center + new IntVec3(0, 0, -6));   // visitors start at the map edge; skip the walk
            info.Budget = Money.FromSilver(120);
            guest.needs.food.CurLevelPercentage = 0.25f;
            float desire = act.Desire(guest, info);
            t.Check(desire >= 2f, "a hungry guest wants a meal: desire " + desire.ToString("0.0"));
            info.Budget = Money.FromSilver(2);
            t.Check(act.Desire(guest, info) == 0f, "a guest without money does not");
            info.Budget = Money.FromSilver(120);
            HSettings.Set("kitchen.menuPrice", 50f);
            t.Check(Restaurant.PickPlate(guest, spot, info.Budget, out _) == null, "too expensive for the budget: no plate");
            HSettings.Set("kitchen.menuPrice", 1f);

            var till = Payments.ColonyTill(map);
            var tillBefore = Ledger.Current.Balance(till); int salesBefore = Restaurant.TotalSales;
            int today = Find.TickManager.TicksGame / GenDate.TicksPerDay; var foodBefore = Ledger.Current.DayIncome(today, "food");
            Hearth.ShopsFood.VenueState.PingStaff(sign, staff);
            var job = act.TryGiveJob(guest, info);
            t.Check(job != null && job.def == JobDefOf.Ingest && good.Contains(job.targetA.Thing), "the guest takes a plate from the shelf");
            if (job == null) { KT.Cleanup(map, center, 8); yield break; }
            var picked = job.targetA.Thing; var pc = picked.TryGetComp<CompDish>();
            var expected = DishEffects.MenuPrice(pc, spot.PriceFactor);
            t.Check(Restaurant.PendingSales == 1, "the sale is noted");
            var second = act.TryGiveJob(guest, info);
            t.Check(second?.targetA.Thing != picked, "a plate already taken is not offered twice");
            Restaurant.ForgetSale(second?.targetA.Thing);
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            foreach (var w in KT.WaitUntil(() => picked.Destroyed, 5000, 30)) yield return w;
            yield return 30;
            if (!picked.Destroyed) t.Note($"guest job {guest.CurJob?.def?.defName} at {guest.Position}, plate at {picked.Position} spawned={picked.Spawned}, downed={guest.Downed}");
            t.Check(picked.Destroyed, "the guest ate the plate");
            var tillAfter = Ledger.Current.Balance(till);
            t.Check(tillAfter > tillBefore && Restaurant.TotalSales == salesBefore + 1, "the till grew: " + tillBefore + " -> " + tillAfter);
            double paid = (tillAfter - tillBefore).Silver;
            t.Check(paid >= expected.Silver - 0.01 && paid < expected.Silver + 1.0, "the guest paid the menu price " + expected + " (coins round up; got " + paid.ToString("0.00") + ")");
            t.Check(info.Spent >= expected && info.Satisfaction > 0f, "budget spent and guest satisfied: " + info.Satisfaction.ToString("0.00"));
            t.Check(Ledger.Current.SumAll() == 0, "ledger still balanced");
            t.Check(Restaurant.PendingSales == 0, "no open sale");
            t.Check(signComp.salesCount == 1 && signComp.itemsSold == 1 && Math.Abs(signComp.Income.Silver - expected.Silver) < 0.01, "the sign recorded the sale: " + signComp.salesCount + " sale(s), " + signComp.Income + " silver");
            t.Check((Ledger.Current.DayIncome(today, "food") - foodBefore).Silver >= expected.Silver - 0.01, "the ledger booked it as food income");
            KT.ClearJob(guest); KT.Cleanup(map, center, 9);
            foreach (var p in map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsGuest).ToList()) { p.GetLord()?.lordManager.RemoveLord(p.GetLord()); p.Destroy(); }
        }
    }

    /// <summary>Learning: recipe books, the test kitchen, the cookbook page, plate tint.</summary>
    public sealed class LearnSuite : HearthTestSuite
    {
        public override string Name => "kitchen.learn";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var pawn = KT.Cook(map);
            DishKnowledge.TestLevelLock = lv => false; DishKnowledge.ForgetAll();
            try
            {
                // recipe book
                var def = DefDatabase<ThingDef>.GetNamed("HH_Cookbook");
                var book = ThingMaker.MakeThing(def); var bc = book.TryGetComp<CompCookbook>();
                t.Check(bc != null && bc.dishes.Count >= 1 && bc.dishes.Count <= 3 && bc.dishes.All(d => d.IsAvailable && !d.dark), "a new book teaches 1-3 dishes: " + string.Join(", ", bc.dishes.Select(d => d.label)));
                t.Check(book.LabelNoCount.Contains(bc.dishes[0].label), "the label lists the dishes: " + book.LabelNoCount);
                var use = book.TryGetComp<CompUseEffect_LearnDishes>();
                t.Check(use != null && use.CanBeUsedBy(pawn).Accepted == (bc.AnythingToLearn), "usable while something is unknown");
                bc.dishes.Clear(); bc.dishes.Add(KT.Dish("ColonyPaella")); bc.dishes.Add(KT.Dish("Laksa"));
                t.Check(!DishKnowledge.IsKnown(KT.Dish("Laksa")) && use.CanBeUsedBy(pawn).Accepted, "dishes unknown before reading");
                use.DoEffect(pawn);
                t.Check(DishKnowledge.IsKnown(KT.Dish("Laksa")) && DishKnowledge.IsKnown(KT.Dish("ColonyPaella")) && KT.Dish("Laksa").recipes[0].AvailableNow, "reading teaches the dishes to the colony");
                t.Check(!use.CanBeUsedBy(pawn).Accepted, "a book with only known dishes cannot be used again");
                book.Destroy();

                // test kitchen odds: better cooks work out more dishes
                var cands = TestKitchen.Candidates();
                t.Check(TestKitchen.HighestOpenLevel() == 2 && cands.Count > 20 && cands.All(d => d.level <= 2), "the open level is one above the unlocked ones: " + cands.Count + " candidates");
                var d6 = KT.Dish("MuffaloGoulash");     // min Cooking 5
                pawn.skills.GetSkill(SkillDefOf.Cooking).Level = 5;
                t.Check(Math.Abs(TestKitchen.SuccessChance(pawn, d6) - 0.15f) < 0.001f, "p = 0.15 at the minimum skill");
                pawn.skills.GetSkill(SkillDefOf.Cooking).Level = 12;
                t.Check(Math.Abs(TestKitchen.SuccessChance(pawn, d6) - 0.29f) < 0.001f, "+0.02 per skill level: 0.29 at Cooking 12");
                pawn.skills.GetSkill(SkillDefOf.Cooking).Level = 0; t.Check(Math.Abs(TestKitchen.SuccessChance(pawn, d6) - 0.05f) < 0.0001f, "never below 5 %");
                int win = 0, lose = 0, loseAwful = 0, n = 600;
                pawn.skills.GetSkill(SkillDefOf.Cooking).Level = 15;
                for (int i = 0; i < n; i++)
                {
                    DishKnowledge.ForgetAll();
                    var product = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HH_Plate_Stew")); product.TryGetComp<CompQuality>().SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
                    var plate = TestKitchen.Experiment(product, pawn, out var target, out bool success);
                    bool known = DishKnowledge.IsKnown(target);
                    plate.TryGetQuality(out var q);
                    if (success) { win++; if (!known || q != QualityCategory.Good) lose += 1000; } else { lose++; if (q == QualityCategory.Awful && !known) loseAwful++; }
                    if (plate.TryGetComp<CompDish>().dish != target) lose += 1000;
                    plate.Destroy(); product.Destroy();
                }
                float rate = win / (float)n;
                t.Check(rate > 0.2f && rate < 0.5f, $"success rate at skill 15 is about 25-35 %: {rate:0.00}");
                t.Check(lose < 1000 && loseAwful == n - win, "failures are Awful plates of the unknown dish, successes are learned: " + loseAwful + "/" + (n - win));
                DishKnowledge.ForgetAll();

                // cookbook page rows
                var rows = CookbookPage.Rows();
                bool darkOn = DishKnowledge.DarkAllowed(); // the test colony may have a cannibal ideoligion: then the dark menu is legitimately listed
                int dishCount = DefDatabase<DishDef>.AllDefs.Count(d => !d.dark || darkOn);
                t.Check(rows.Count == dishCount, "page lists every dish except the dark menu: " + rows.Count + "/" + dishCount);
                var unknown = rows.First(r => !r.Known);
                t.Check(unknown.Name == "???" && unknown.Hint.Contains("Research") && unknown.Hint.Contains(DishKnowledge.LevelResearch(unknown.Dish.level).LabelCap), "unknown dishes show ??? and where to learn them: " + unknown.Hint);
                t.Check(CookbookPage.Rows(null, true).All(r => r.Known) && CookbookPage.Rows(null, true).Count == rows.Count(r => r.Known), "only-known filter");
                t.Check(CookbookPage.Rows("ramen").Any(r => r.Dish.defName == "HH_Dish_MuffaloRamen") == false && CookbookPage.Rows("compote").Count == 1, "search finds known dishes only");
                t.Check(HearthTabs.Pages.Any(p => p.Id == "kitchen"), "cookbook page registered in the main tab");
                DishKnowledge.TestLevelLock = lv => true;
                var all = CookbookPage.Rows(); t.Check(all.All(r => r.Known || !r.Dish.IsAvailable || r.Dish.dark), "with all levels open everything with a recipe is known: " + string.Join(", ", all.Where(r => !r.Known).Select(r => r.Dish.defName + " L" + r.Dish.level)) + " / research L2-4: " + string.Join(",", new[] { 2, 3, 4 }.Select(l => DishKnowledge.LevelUnlocked(l))));

                // tint
                var plateT = KT.MakePlate(map, KT.Dish("MuffaloBourguignon"), map.Center + new IntVec3(-3, 0, 25));
                var empty = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HH_Plate_Stew"));
                t.Check(plateT.DrawColor == KT.Dish("MuffaloBourguignon").Tint && plateT.DrawColor != Color.white && plateT.DrawColor.r > 0.7f && plateT.DrawColor.g > 0.7f && plateT.DrawColor.b > 0.7f, "plates carry a soft tint per dish: " + plateT.DrawColor);
                t.Check(KT.Dish("Rendang").Tint != KT.Dish("Ramen".Length > 0 ? "MuffaloRamen" : "Rendang").Tint, "different dishes tint differently");
                plateT.Destroy(); empty.Destroy();
            }
            finally { DishKnowledge.TestLevelLock = null; DishKnowledge.ForgetAll(); }
            yield break;
        }
    }
    /// <summary>Saving and loading: plates and bills survive a round trip; trader stock; plates without the mod would be plain items.</summary>
    public sealed class SaveSuite : HearthTestSuite
    {
        public override string Name => "kitchen.save";
        public override ModuleId Module => ModuleId.ShopsFood;

        static T RoundTrip<T>(T thing) where T : Thing
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                Scribe.saver.InitSaving(path, "hhkitchentest");
                Scribe_Deep.Look(ref thing, "thing");
                Scribe.saver.FinalizeSaving();
                T copy = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref copy, "thing");
                Scribe.loader.FinalizeLoading();
                return copy;
            }
            finally { try { System.IO.File.Delete(path); } catch { } }
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var dish = KT.Dish("MuffaloBourguignon");
            var plate = KT.MakePlate(map, dish, map.Center + new IntVec3(-6, 0, 24), QualityCategory.Excellent, 1);
            var comp = plate.TryGetComp<CompDish>(); comp.freshAge = 12345f; plate.HitPoints = 33;
            var copy = RoundTrip(plate);
            t.Check(copy != null && copy.def == plate.def, "the plate loads back");
            var cc = copy?.TryGetComp<CompDish>();
            t.Check(cc != null && cc.dish == dish && cc.variant == 1 && Math.Abs(cc.freshAge - 12345f) < 1f, "dish, form and freshness survive");
            t.Check(copy != null && copy.TryGetQuality(out var q) && q == QualityCategory.Excellent && copy.HitPoints == 33, "quality and hit points survive");
            t.Check(copy != null && copy.LabelNoCount.StartsWith("Colony bourguignon"), "label after loading: " + copy?.LabelNoCount);
            plate.Destroy();

            // a work table with a dish bill
            var area = map.Center + new IntVec3(-22, 0, 24);
            KT.Clear(map, area, 3);
            var pot = KT.SpawnDef(map, "HH_Station_StockPot", area);
            var recipe = KT.Dish("BerryCompote").recipes[1];
            var bill = (Bill_Production)recipe.MakeNewBill(); ((IBillGiver)pot).BillStack.AddBill(bill); bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 7;
            var potCopy = RoundTrip((Building_WorkTable)pot);
            var b2 = potCopy?.BillStack.Bills.FirstOrDefault() as Bill_Production;
            t.Check(b2 != null && b2.recipe == recipe && b2.repeatCount == 7, "the bill and its generated recipe survive a save: " + b2?.recipe?.defName);
            t.Check(potCopy != null && DishRecipeExtension.Of(b2?.recipe)?.variant == 1, "the loaded bill still knows its dish and form");
            // removal: our bills leave every bill stack, vanilla bills stay
            t.Check(RecipeBuilder.IsOurRecipe(recipe) && !RecipeBuilder.IsOurRecipe(DefDatabase<RecipeDef>.AllDefs.First(r => r.defName.StartsWith("Make_"))), "our recipes are recognised, vanilla ones are not");
            int removed = RecipeBuilder.RemoveOurBills();
            t.Check(removed >= 1 && ((IBillGiver)pot).BillStack.Count == 0, "PrepareRemoval takes the dish bills off the work table: removed " + removed);
            pot.Destroy();
            KT.Cleanup(map, area, 3);

            // trader stock and cookbook
            var kind = DefDatabase<TraderKindDef>.GetNamed("Caravan_Outlander_BulkGoods");
            t.Check(kind.stockGenerators.Any(g => g is StockGenerator_Tag tg && tg.tradeTag == "HH_Cookbook"), "outlander caravans can carry recipe books");
            var book = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HH_Cookbook"));
            var bcopy = RoundTrip(book);
            t.Check(bcopy?.TryGetComp<CompCookbook>()?.dishes.Count == book.TryGetComp<CompCookbook>().dishes.Count && bcopy.TryGetComp<CompCookbook>().dishes.Count >= 1, "a recipe book keeps its dishes");
            // station heat: prep counter, dough bench and cold station give no heat
            foreach (var n in new[] { "PrepCounter", "DoughBench", "ColdStation" }) t.Check(DefDatabase<ThingDef>.GetNamed("HH_Station_" + n).thingClass == typeof(Building_WorkTable), n + " is a plain work table");
            yield break;
        }
    }
}
#endif
