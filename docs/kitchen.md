# Kitchen, dishes and restaurant (module Shops & Food, KONZEPT 6.4 / Anhang A)

Code: `Source/Hearth/Kitchen/` (namespace `Hearth.Kitchen`) · Defs: `Common/Defs/Kitchen/` · Texts: `Keyed/HH_Kitchen.xml` · Generator: `tools/dishes/make_dishes.py`.

## Pieces
| File | What |
|---|---|
| `DishDef.cs` | `DishDef` (the data of one dish), `DishRecipeExtension` (on every generated recipe), `StationExtension` (on our devices), enums |
| `Ingredients.cs` | ingredient classes (`meat`, `potatoes`, `cheese` …) resolved at startup against all loaded defs; foods of other mods by name and package |
| `RecipeBuilder.cs` | builds 3 recipes per dish (Everyday, Fine, Signature) as normal `RecipeDef`s; `RecipeWorkerCounter_Dish` for "do until" |
| `Stations.cs` | device kinds, "side devices in the same room within 8 cells", research gating, `CompStationInfo` |
| `CompDish.cs`, `DishPlate.cs`, `DishEffects.cs` | the plate: dish, form, freshness, label, eating (Well fed, mood thought), prices |
| `KitchenPatches.cs` | Harmony: recipe hidden until learned, bill waits for side devices, dish set on the product, poison rule, Well fed hunger, test kitchen |
| `DishKnowledge.cs`, `Cookbook.cs`, `CookbookPage.cs` | learning (levels by research, single dishes by books / test kitchen), the cookbook page |
| `Restaurant.cs` | guest activity `kitchen.eat`, plate stock, paying |
| `KitchenTests.cs` | in-game suites `kitchen.defs/rules/cook/eat/poison/restaurant/learn/save` |

## Data flow
`Dishes_L1..L4.xml` (generated from the Anhang A tables) → `DishDef`s → at startup `IngredientClasses.Resolve()` → `RecipeBuilder.BuildAll()` adds `HH_Cook_<Dish>[_Fine|_Signature]` to the `DefDatabase<RecipeDef>` with `recipeUsers` = main device (last device of the dish; both vanilla stoves for "Stove", the campfire, the butcher table or one of our ten).
Cooking is a normal bill, so no custom job: robots and Project RimFactory work. `GenRecipe.PostProcessProduct` (postfix) puts the dish on the plate and rerolls quality for Fine/Signature.

## Rules worth knowing
* Nutrition budget of an Everyday plate: 0.5 nutrition of raw food x 1.0/1.1/1.25/1.5 by level, split by the shares of the dish; Fine x1.5 + one finishing touch, Signature x2 + two. Beer and hemogen: one piece per share.
* Learned: level 1 always, 2-4 with `HH_Cooking_L2..L4` (HH_Hospitality tab; L1 is finished at game start), single dishes by recipe book (`HH_Cookbook`, caravans) or the test kitchen recipe `HH_TestKitchen` at the prep counter. Settings: all dishes known, dark menu (long pork), Fine/Signature on/off.
* Freshness: own clock per plate (3-24 h per dish x setting, half speed for Salad/Raw/Dessert when cooled), stale after one shelf life (still edible, no Well fed, no thought, never sold), vanilla rot after four. Vanilla `RotProgress` is driven from our clock.
* Poison: no roll at all when the cook has Cooking >= 4 (machines: their fixed level), the room has the Kitchen role and cleanliness >= -0.2 (a dirt floor is -1: lay a floor). Only our plates; vanilla meals unchanged.
* Well fed: hunger -40 % (x setting) for 4/6/8/10 h by level x 0.6..1.8 by quality, thought `HH_AteDish` stage = (level-1)*7 + quality.

## Hooks for the integrator
* `Restaurant.OpenGate` / `Restaurant.VenuePriceFactor` default to `VenueApi.IsOpenForBusiness` / `VenueApi.PriceMultiplier`, so hours, staff, night bell and price settings apply. Only rooms with a Restaurant or Tavern sign sell food (no dining-room fallback, N08). A Restaurant sells `SellKind.Dishes`: its plates are forbidden for the colony unless the sign says "colony may take goods", still sold to guests, restocked by the sign, and every sale is recorded on the sign (count, silver; ledger category food).
* `DishKnowledge.Learn(dish)` for quests/guests, `TestKitchen`, `HH_Cookbook` (`tradeTags HH_Cookbook`, patched into outlander traders).
* Research hints on devices (`StationExtension.researchHint`): `HH_Innkeeping`, `HH_Gastronomy`, `HH_HotelManagement`; if those do not exist the devices fall back to `HH_Cooking_L2/L3`.
* Foreign devices count as side devices by `buildingTags` `HH_Stove`, `HH_Oven`, `HH_Grill` … or a `Hearth.Kitchen.StationExtension`.
* The restaurant loop needs the Shops & Food module: with the module off, or without a Restaurant/Tavern sign, nothing is sold (N08). Stale plates are never for sale and never forbidden by us (the colony may clear them away); the sign checklist and the main-tab stock ignore them. A paid sale also gives the staffer Social XP and a small "Friendly service" satisfaction, like tavern and shop sales. `VenueMapComp` keeps its forbidden-by-us goods in a HashSet.
