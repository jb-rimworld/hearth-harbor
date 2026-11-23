// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Hearth.Testing;

namespace Hearth.Kitchen.Tests
{
    /// <summary>F11: the slots of every recipe allow different things, finishing touches suit the plate and never repeat the dish's own ingredients.</summary>
    public sealed class RecipeDistinctSuite : HearthTestSuite
    {
        public override string Name => "kitchen.recipes.distinct";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 20000;

        static readonly System.Reflection.MethodInfo tryFind = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");

        static ThingDef Berries => DefDatabase<ThingDef>.GetNamed("RawBerries");
        static ThingDef Corn => DefDatabase<ThingDef>.GetNamed("RawCorn");
        static ThingDef Spuds => DefDatabase<ThingDef>.GetNamed("RawPotatoes");
        static HashSet<ThingDef> Allowed(IngredientCount ic) => new HashSet<ThingDef>(ic.filter.AllowedThingDefs);

        public override IEnumerable<int> Run(TestContext t)
        {
            var all = DefDatabase<DishDef>.AllDefsListForReading;
            int recipes = 0, bad = 0, sig = 0, fine = 0, withoutSig = 0;
            foreach (var d in all.Where(x => x.IsAvailable))
            {
                var own = new HashSet<ThingDef>(); var ownKeys = new HashSet<string>(d.ingredients.Select(i => i.cls));
                foreach (var s in d.ingredients) foreach (var th in IngredientClasses.Find(s.cls).Things) own.Add(th);
                foreach (var r in d.recipes)
                {
                    recipes++;
                    var ext = DishRecipeExtension.Of(r);
                    var sets = r.ingredients.Select(Allowed).ToList();
                    bool disjoint = true;
                    for (int i = 0; i < sets.Count && disjoint; i++) for (int j = i + 1; j < sets.Count; j++) if (sets[i].Overlaps(sets[j])) { disjoint = false; t.Check(false, r.defName + ": slots " + i + " and " + j + " allow the same thing: " + sets[i].First(x => sets[j].Contains(x)).defName); break; }
                    if (!disjoint) bad++;
                    if (sets.Any(s => s.Count == 0)) { bad++; t.Check(false, r.defName + " has an empty slot"); }
                    int extra = ext.variant == 0 ? 0 : ext.variant == 1 ? 1 : 2;
                    if (r.ingredients.Count != d.ingredients.Count + extra) { bad++; t.Check(false, r.defName + ": " + r.ingredients.Count + " slots, expected " + (d.ingredients.Count + extra)); }
                    // the finishing slots never allow anything of the dish itself
                    for (int i = d.ingredients.Count; i < r.ingredients.Count; i++)
                        if (sets[i].Overlaps(own)) { bad++; t.Check(false, r.defName + ": finishing slot " + i + " allows " + sets[i].First(own.Contains).defName + " of the dish itself"); }
                    if (ext.variant == 1) fine++; if (ext.variant == 2) sig++;
                    // fixed filter = union of the slots
                    var union = new HashSet<ThingDef>(sets.SelectMany(s => s));
                    if (!union.SetEquals(r.fixedIngredientFilter.AllowedThingDefs)) { bad++; t.Check(false, r.defName + ": fixed ingredient filter is not the union of the slots"); }
                }
                if (d.recipes.Count == 2) withoutSig++;
                // save compatibility: all three recipe defs of an available dish exist (offered or hidden), so a saved bill of any form still loads
                for (int v = 0; v < 3; v++)
                {
                    string dn = "HH_Cook_" + d.Id + (v == 0 ? "" : "_" + RecipeBuilder.VariantName[v]);
                    if (DefDatabase<RecipeDef>.GetNamedSilentFail(dn) == null) { bad++; t.Check(false, dn + " does not exist (a saved bill of it would be dropped)"); }
                }
                // plate rules: chocolate only on desserts (or when the dish lists it itself), beer only on savory plates, nothing sweet-only on fish
                foreach (var r in d.recipes.Where(x => DishRecipeExtension.Of(x).variant > 0))
                    for (int i = d.ingredients.Count; i < r.ingredients.Count; i++)
                    {
                        var s = Allowed(r.ingredients[i]);
                        bool choc = s.Any(x => x.defName == "Chocolate"), beer = s.Any(x => x.defName == "Beer");
                        if (choc && d.plate != PlateKind.Dessert && !(d.finishers?.Contains("chocolate") ?? false)) { bad++; t.Check(false, r.defName + ": chocolate as finishing touch on a " + d.plate + " plate"); }
                        if (beer && (d.plate == PlateKind.Dessert || d.plate == PlateKind.Salad || d.plate == PlateKind.Raw || d.plate == PlateKind.Breakfast)) { bad++; t.Check(false, r.defName + ": beer as finishing touch on a " + d.plate + " plate"); }
                    }
            }
            t.Check(bad == 0, $"{recipes} recipes: every slot allows different things, finishing touches suit the plate ({bad} problems)");
            t.Note($"{fine} Fine and {sig} Signature recipes, {withoutSig} dishes without a Signature form, {RecipeBuilder.Retired} hidden recipes, overlap repairs: {RecipeBuilder.OverlapRepairs}");
            t.Check(all.Where(d => d.fromMod.NullOrEmpty() && !d.noVariants).All(d => d.recipes.Count == 3), "every base dish still has all three forms (the plate pools leave enough finishers)");
            t.Check(RecipeBuilder.OverlapRepairs == 0, "vanilla ingredient classes never overlap by themselves (repairs: " + RecipeBuilder.OverlapRepairs + ")");

            // the report of the owner: the fruit bowl
            var bowl = KT.Dish("FruitBowl");
            var bowlFine = bowl.recipes[1];
            var f = Allowed(bowlFine.ingredients[1]);
            t.Check(!f.Any(x => x.defName == "RawBerries") && !f.Any(x => x.defName == "Beer") && !f.Any(x => x.defName == "RawFungus"), "fruit bowl (fine): the finishing touch is not berries, beer or mushrooms");
            t.Check(f.Any(x => x.defName.IndexOf("Milk", StringComparison.OrdinalIgnoreCase) >= 0) && f.Any(x => x.defName == "Chocolate") && f.Any(x => x.defName == "RawAgave"), "fruit bowl (fine): milk, chocolate or agave");
            var bowlSig = bowl.recipes[2];
            t.Check(bowlSig.ingredients.Count == 3 && !Allowed(bowlSig.ingredients[1]).Overlaps(Allowed(bowlSig.ingredients[2])), "fruit bowl (signature): two different finishing touches");
            // dessert with all three creamy classes of its own
            var pud = KT.Dish("ChocolatePudding");
            var pudSig = pud.recipes[2];
            var pa = Allowed(pudSig.ingredients[3]); var pb = Allowed(pudSig.ingredients[4]);
            t.Check(pa.Count > 0 && pb.Count > 0 && !pa.Overlaps(pb) && pa.Concat(pb).All(x => x.defName == "RawBerries" || x.defName == "RawAgave"), "chocolate pudding (signature): berries and agave, never milk, eggs or chocolate again");
            var plateOnly = KT.Dish("PlainSteamedRice");
            t.Check(plateOnly.recipes.Count == 1, "plain steamed rice stays plain (no fine/signature form)");
            var hidden = DefDatabase<RecipeDef>.GetNamedSilentFail("HH_Cook_PlainSteamedRice_Fine");
            t.Check(hidden != null && DishRecipeExtension.Of(hidden)?.retired == true && !hidden.AvailableNow, "...but its old fine recipe still exists as a hidden recipe, so saved bills load");
            var mp = KT.Dish("MushroomPlate");
            t.Check(mp.recipes.Count == 3, "mushroom plate (uses both salad finishers) still gets a signature form via milk and eggs");
            var seven = KT.Dish("SevenCourseColonyMenu");
            t.Check(seven.stations.Contains(StationKind.Oven) && seven.stations.Contains(StationKind.Stove) && seven.MainStation == StationKind.ColdStation && seven.steps.Contains(StepKind.Roast), "seven-course menu is heated now (stove, oven) and is still served from the cold station");
            t.Check(KT.Dish("PadThrumbo").label == "Pad bug" && KT.Dish("MuffaloRamen").label == "Hearty meat ramen", "dish names no longer name an animal the recipe does not require");

            // config guard
            t.Check(all.All(d => !d.ConfigErrors().Any()), "no config errors");
            var twice = new DishDef { defName = "HH_Dish_TestTwice", label = "t", description = "d", plate = PlateKind.Stew, stations = new List<StationKind> { StationKind.StockPot }, steps = new List<StepKind> { StepKind.Simmer }, ingredients = new List<IngredientShare> { new IngredientShare("meat", 1), new IngredientShare("meat", 1) }, finishers = new List<string> { "meat", "nonsense" } };
            var errs = twice.ConfigErrors().ToList();
            t.Check(errs.Any(e => e.Contains("listed twice")) && errs.Any(e => e.Contains("itself")) && errs.Any(e => e.Contains("unknown finisher")), "ConfigErrors catches a class listed twice, an own class as finisher and an unknown finisher: " + string.Join(" | ", errs));

            // slot separation itself
            var a = new IngredientCount { filter = new ThingFilter() }; var b = new IngredientCount { filter = new ThingFilter() };
            a.filter.SetAllow(Berries, true); a.filter.SetAllow(Spuds, true);
            b.filter.SetAllow(Berries, true); b.filter.SetAllow(Corn, true);
            int removed = RecipeBuilder.MakeDisjoint(new List<IngredientCount> { a, b });
            t.Check(removed == 1 && a.filter.Allows(Berries) && !b.filter.Allows(Berries) && b.filter.Allows(Corn), "MakeDisjoint: the first slot keeps the shared thing");

            // ---- the bill solver: more berries are not a second ingredient ---------------------------------------------------------
            if (tryFind == null || tryFind.GetParameters().Length < 5) { t.Note("WorkGiver_DoBill.TryFindBestBillIngredients not found with the expected shape: the runtime part is skipped (the filter checks above prove the same)"); yield break; }
            var map = t.Map; var area = map.Center + new IntVec3(18, 0, -14);
            KT.Clear(map, area, 7);
            var cook = KT.Cook(map);
            if (cook == null) { t.Note("no colonist who may cook"); yield break; }
            KT.ClearJob(cook); KT.Teleport(cook, area + new IntVec3(0, 0, -2));
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 12;
            var prep = KT.SpawnDef(map, "HH_Station_PrepCounter", area, Rot4.North);
            Stations.ClearCache();
            var bill = (Bill_Production)bowlFine.MakeNewBill(); ((IBillGiver)prep).BillStack.AddBill(bill);
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 1;
            var berries = ThingMaker.MakeThing(Berries); berries.stackCount = 75;
            GenPlace.TryPlaceThing(berries, area + new IntVec3(2, 0, -1), map, ThingPlaceMode.Near);
            yield return 5;
            bool Try(out List<ThingCount> chosen)
            {
                chosen = new List<ThingCount>(); var missing = new List<IngredientCount>();
                var args = new object[] { bill, cook, prep, chosen, missing };
                var res = (bool)tryFind.Invoke(null, args);
                return res;
            }
            bool onlyBerries = Try(out var c1);
            t.Check(!onlyBerries, "75 berries alone cannot start a fruit bowl (fine): the second ingredient is missing");
            var milkDef = DefDatabase<ThingDef>.GetNamedSilentFail("Milk");
            if (milkDef != null)
            {
                var milk = ThingMaker.MakeThing(milkDef); milk.stackCount = 20;
                GenPlace.TryPlaceThing(milk, area + new IntVec3(-2, 0, -1), map, ThingPlaceMode.Near);
                yield return 5;
                bool withMilk = Try(out var c2);
                t.Check(withMilk, "berries and milk start it");
                if (withMilk)
                {
                    int nb = c2.Where(c => c.Thing.def == Berries).Sum(c => c.Count), nm = c2.Where(c => c.Thing.def == milkDef).Sum(c => c.Count);
                    t.Check(nm >= 1 && nb >= 10 && nb <= 20, $"it takes both: {nb} berries and {nm} milk");
                }
                milk.Destroy();
            }
            berries.Destroy();
            KT.Cleanup(map, area, 7);
        }
    }

    /// <summary>F31 (kitchen part): sound and effecter of every technique, completion cues, plate and eating sounds.</summary>
    public sealed class CookSoundsSuite : HearthTestSuite
    {
        public override string Name => "kitchen.sounds";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var vanilla = DefDatabase<SoundDef>.GetNamed(CookSounds.VanillaSound);
            // every step kind has an own working sound and it exists
            foreach (StepKind k in Enum.GetValues(typeof(StepKind)))
            {
                string n = CookSounds.SoundNameOf(k);
                var s = n == null ? null : DefDatabase<SoundDef>.GetNamedSilentFail(n);
                t.Check(s != null && s.sustain, "step " + k + " -> sound " + n + " exists and is a sustainer");
                string fn = CookSounds.EffecterNameOf(k);
                t.Check(fn == null || DefDatabase<EffecterDef>.GetNamedSilentFail(fn) != null, "step " + k + " -> effecter " + (fn ?? "(vanilla)") + " exists");
            }
            // every sound def has clips on disk (an empty folder logs an error, so also count them)
            int defs = 0, empty = 0;
            foreach (var s in DefDatabase<SoundDef>.AllDefs.Where(d => d.defName.StartsWith("HH_Cook_") || d.defName.StartsWith("HH_Plate_Drop_") || d.defName.StartsWith("HH_Eat_")))
            {
                defs++;
                if (s.subSounds == null || s.subSounds.Count == 0 || s.subSounds.All(ss => ss.grains == null || ss.grains.Count == 0 || ss.grains.All(g => !g.GetResolvedGrains().Any()))) { empty++; t.Check(false, s.defName + " has no clips"); }
                foreach (var start in new[] { s.sustainStartSound, s.sustainStopSound }) if (start != null && DefDatabase<SoundDef>.GetNamedSilentFail(start.defName) == null) t.Check(false, s.defName + " references an unknown start/stop sound");
            }
            t.Check(defs >= 20 && empty == 0, defs + " kitchen sound defs, all with clips");

            // recipes carry the sound of their last step
            int wrong = 0, total = 0;
            CookSounds.Sync(true);
            foreach (var d in DefDatabase<DishDef>.AllDefs.Where(x => x.IsAvailable))
                foreach (var r in d.recipes)
                {
                    total++;
                    var expect = DefDatabase<SoundDef>.GetNamed(CookSounds.SoundNameOf(d.steps.Last()));
                    if (r.soundWorking != expect || r.effectWorking == null) { wrong++; t.Check(false, r.defName + " has sound " + r.soundWorking?.defName + ", expected " + expect.defName); }
                }
            t.Check(wrong == 0, total + " recipes carry the sound of their last step");
            var kinds = DefDatabase<DishDef>.AllDefs.Where(x => x.IsAvailable).Select(x => x.recipes[0].soundWorking.defName).Distinct().Count();
            t.Check(kinds >= 9, kinds + " different working sounds in use");

            // the setting switches back to the vanilla sound
            bool before = HSettings.GetBool(CookSounds.SettingKey);
            HSettings.Set(CookSounds.SettingKey, 0f); CookSounds.Sync();
            var any = KT.Dish("BerryCompote").recipes[0];
            t.Check(any.soundWorking == vanilla && any.effectWorking?.defName == CookSounds.VanillaEffecter, "setting off: vanilla pan-frying sound and effect");
            HSettings.Set(CookSounds.SettingKey, 1f); CookSounds.Sync();
            t.Check(any.soundWorking?.defName == "HH_Cook_Simmer", "setting on again: own sound (berry compote simmers)");
            HSettings.Set(CookSounds.SettingKey, before ? 1f : 0f); CookSounds.Sync();

            // completion cues, plates, eating
            t.Check(CookSounds.DoneSoundName(0) == null && CookSounds.DoneSoundName(1) == "HH_Cook_Done_Fine" && CookSounds.DoneSoundName(2) == "HH_Cook_Done_Signature", "cue by form: none, fine, signature");
            foreach (PlateKind k in Enum.GetValues(typeof(PlateKind)))
            {
                var p = DefDatabase<ThingDef>.GetNamed("HH_Plate_" + k);
                t.Check(p.soundDrop != null && p.soundDrop.defName.StartsWith("HH_Plate_Drop_"), "plate " + k + " has its own drop sound: " + p.soundDrop?.defName);
                t.Check(p.ingestible.ingestSound != null, "plate " + k + " has an eating sound: " + p.ingestible.ingestSound?.defName);
            }
            t.Check(DefDatabase<ThingDef>.GetNamed("HH_Plate_Soup").ingestible.ingestSound.defName == "HH_Eat_Slurp" && DefDatabase<ThingDef>.GetNamed("HH_Plate_Fry").ingestible.ingestSound.defName == "HH_Eat_Crunch", "soup slurps, fried food crunches");
            var map = t.Map; var plate = KT.MakePlate(map, KT.Dish("Rendang"), map.Center + new IntVec3(-14, 0, 22), QualityCategory.Excellent, 2);
            var comp = plate.TryGetComp<CompDish>();
            t.Check(CookSounds.EatReactionApplies(comp), "an excellent signature plate gets the happy reaction");
            comp.SetDish(KT.Dish("Rendang"), 0);
            t.Check(!CookSounds.EatReactionApplies(comp), "...an everyday plate does not");
            plate.Destroy();
            var cook = KT.Cook(map);
            if (cook != null) { CookSounds.PlayDone(1, cook); CookSounds.PlayDone(2, cook); t.Note("cues played without error"); }
            yield break;
        }
    }
}
#endif
