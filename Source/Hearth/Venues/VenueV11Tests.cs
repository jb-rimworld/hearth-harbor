// SPDX-License-Identifier: MIT
// In-game suites of the 1.1.0 venues package: setup guide (F12), work side (F13), serving (F14), honor system (F15),
// casino seats (F17, in the Casino folder), business names (F33) and the venue windows (F35). Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Hearth.Testing;

namespace Hearth.ShopsFood
{
    static class V11Kit
    {
        public static Thing Spawn(Map map, string defName, IntVec3 c, Rot4 rot)
        {
            var def = DefDatabase<ThingDef>.GetNamed(defName);
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            t.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(t, c, map, rot);
        }

        /// <summary>Counter (top right, worker below), a 1x2 table and two chairs (bottom left) in a 5x5 test room: what a tavern needs.</summary>
        public static List<Thing> FurnishTavern(Map map, VenueTestKit.TestRoom room)
        {
            var r = room.Inner; var list = new List<Thing>();
            list.Add(Spawn(map, "HH_ShopCounter", new IntVec3(r.minX + 2, 0, r.maxZ), Rot4.North));
            list.Add(Spawn(map, "Table1x2c", new IntVec3(r.minX + 1, 0, r.minZ + 1), Rot4.North));
            list.Add(Spawn(map, "DiningChair", new IntVec3(r.minX, 0, r.minZ + 1), Rot4.East));
            list.Add(Spawn(map, "DiningChair", new IntVec3(r.minX + 2, 0, r.minZ + 1), Rot4.West));
            room.Things.AddRange(list);
            return list;
        }

        public static Venue VenueOf(Map map, Thing sign) { Venues.MarkDirty(map); VenueSetup.MarkDirty(sign); return VenueApi.VenueOfSign(map, sign); }

        public static void SetBool(string key, bool v) => HSettings.Set(key, v ? 1f : 0f);
    }

    // ================================================================================================ F12
    public sealed class SetupSuite : HearthTestSuite
    {
        public override string Name => "venues.setup";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            bool ruleWas = HSettings.GetBool(VenueSetup.RequireKey);
            VenueTestKit.ClearGuests(map);
            V11Kit.SetBool(VenueSetup.RequireKey, true);

            // --- data: every venue type has a checklist, every requirement has a label, custom workers resolve
            foreach (var type in DefDatabase<VenueTypeDef>.AllDefs)
            {
                var reqs = VenueSetup.RequirementsOf(type);
                t.Check(reqs.Count > 0, type.defName + " has setup requirements (" + reqs.Count + ")");
                t.Check(reqs.All(r => !r.label.NullOrEmpty() && !r.id.NullOrEmpty()), type.defName + ": every requirement has an id and a label");
                t.Check(reqs.Any(r => r.Blocks), type.defName + ": at least one item is required to open");
                t.Check(reqs.Where(r => r.check == VenueCheck.Custom).All(r => r.Worker != null), type.defName + ": custom checks have a worker");
                var ext = type.GetModExtension<VenueSetupExtension>();
                t.Check(ext == null || !ext.ConfigErrors().Any(), type.defName + ": no config errors " + string.Join("; ", ext?.ConfigErrors() ?? Enumerable.Empty<string>()));
            }
            var fake = new VenueTypeDef { defName = "HH_TestType", needsStaff = true };
            t.Check(VenueSetup.RequirementsOf(fake).Any(r => r.check == VenueCheck.ServingWorker), "a type of another mod without a checklist gets the built-in staff row");
            var casinoGame = VenueSetup.RequirementsOf(HHVenueDefOf.HH_Venue_Casino).FirstOrDefault(r => r.id == "game");
            t.Check(casinoGame != null && VenueSetup.Counts(casinoGame, DefDatabase<ThingDef>.GetNamed("HH_PokerTable")) && VenueSetup.Counts(casinoGame, DefDatabase<ThingDef>.GetNamed("HH_SlotMachine")), "casino 'a game' matches every game by its comp");
            t.Check(casinoGame != null && !VenueSetup.Counts(casinoGame, DefDatabase<ThingDef>.GetNamed("DiningChair")), "a chair is not a game");
            var seats = VenueSetup.RequirementsOf(HHVenueDefOf.HH_Venue_Tavern).First(r => r.id == "seats");
            t.Check(VenueSetup.Counts(seats, ThingDef.Named("DiningChair")) && VenueSetup.Counts(seats, ThingDef.Named("Armchair")) && !VenueSetup.Counts(seats, ThingDef.Named("Table1x2c")), "seats: any sittable building, never a table");
            var gallery = DefDatabase<VenueTypeDef>.GetNamedSilentFail("HH_Venue_Gallery");
            if (gallery != null)
            {
                var art = VenueSetup.RequirementsOf(gallery).First(r => r.check == VenueCheck.Art);
                t.Check(art.minSetting == "city.galleryMinArt", "the gallery's art minimum follows the City Life setting");
            }

            // --- a shop without storage is not ready; a stockpile makes it ready
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            yield return 10;
            var v = V11Kit.VenueOf(map, sign);
            t.Check(v != null, "shop venue exists");
            if (v == null) { VenueTestKit.Teardown(map, room); yield break; }
            var rep = VenueSetup.Report(v, 0);
            t.Check(rep.Row("storage") != null && !rep.Row("storage").Ok && rep.Row("storage").Blocking, "shop: storage is missing and blocks");
            t.Check(!rep.Complete && VenueApi.Gate(v) == VenueGate.NotReady, "shop without storage: gate NotReady (" + VenueApi.Gate(v) + ")");
            t.Check(!VenueApi.IsOpenForBusiness(v) && !v.OpenAndStaffed, "a venue that is not ready is not open for business");
            string inspect = comp.CompInspectStringExtra() ?? "";
            t.Check(inspect.Contains("Not ready"), "inspect string says Not ready: " + inspect.Replace("\n", " | "));
            var gizmos = comp.CompGetGizmosExtra().ToList();
            var setupGizmo = gizmos.OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel != null && g.defaultLabel.StartsWith("Setup"));
            t.Check(setupGizmo != null && setupGizmo.defaultLabel.Contains("missing"), "setup gizmo names what is missing: " + setupGizmo?.defaultLabel);
            var shelfDefs = VenueSetup.BuildDefs(rep.Row("storage").Req);
            t.Check(shelfDefs.Contains(ThingDef.Named("Shelf")), "storage offers the shelf as a build option");
            // F12: every buildable thing the matcher accepts is offered (any mod), curated ones first, cached
            var storageReq = rep.Row("storage").Req;
            var auto = VenueSetup.AutoDefs(storageReq);
            t.Check(ReferenceEquals(auto, VenueSetup.AutoDefs(storageReq)), "the discovered list is cached per requirement");
            t.Check(auto.Count > 0 && auto.All(d => d.designationCategory != null && VenueSetup.Counts(storageReq, d)), "discovered storage things all have a designator category and count for the requirement: " + auto.Count);
            var allowed = DefDatabase<ThingDef>.AllDefs.Where(d => d.designationCategory != null && d.category == ThingCategory.Building && !d.IsBlueprint && !d.IsFrame && VenueSetup.Counts(storageReq, d)).ToList();
            t.Check(allowed.All(d => shelfDefs.Contains(d)), "BuildDefs offers every buildable thing that counts (" + allowed.Count + ")");
            var curatedIdx = storageReq.BuildOptions.Where(d => d != null && d.designationCategory != null && shelfDefs.Contains(d)).Select(d => shelfDefs.IndexOf(d)).ToList();
            t.Check(shelfDefs.Distinct().Count() == shelfDefs.Count, "no def twice");
            bool lockedAfter = true, seenLocked = false;
            foreach (var d in shelfDefs) { bool ok = VenueSetup.DesignatorFor(d, out _) != null; if (!ok) seenLocked = true; else if (seenLocked) lockedAfter = false; }
            t.Check(lockedAfter, "buildable ones come before the locked ones");
            if (shelfDefs.Count > 0) t.Check(VenueSetup.DesignatorFor(shelfDefs[0], out _) != null || shelfDefs.All(d => VenueSetup.DesignatorFor(d, out _) == null), "the first option is buildable when any is");
            // a modded-style def that only the matcher knows: a seat with a designator is found for the seats requirement
            var seatsReq = new VenueRequirement { id = "t_seats", check = VenueCheck.Seats };
            var seatDefs = VenueSetup.AutoDefs(seatsReq);
            t.Check(seatDefs.Contains(ThingDef.Named("Stool")) && seatDefs.Contains(ThingDef.Named("DiningChair")) && seatDefs.Contains(ThingDef.Named("Armchair")), "seats: stool, chair and armchair found by the matcher alone, without a curated list");
            var thingsReq = new VenueRequirement { id = "t_things", check = VenueCheck.Things, thingClass = typeof(Building_Bed) };
            t.Check(VenueSetup.AutoDefs(thingsReq).Contains(ThingDef.Named("Bed")), "a thing-class requirement finds beds");
            t.Check(VenueSetup.AutoDefs(new VenueRequirement { id = "t_stock", check = VenueCheck.Stock }).Count == 0, "stock requirements discover nothing to build");
            t.Check(!VenueSetup.CostText(ThingDef.Named("Shelf")).NullOrEmpty(), "cost text for the build menu: " + VenueSetup.CostText(ThingDef.Named("Shelf")));
            var des = VenueSetup.DesignatorFor(ThingDef.Named("Shelf"), out string why);
            t.Check(des != null || !why.NullOrEmpty(), "a build option has a designator or a reason: " + (why ?? "buildable"));
            bool buildGizmo = gizmos.OfType<Command_Action>().Any(g => g.defaultLabel != null && g.defaultLabel.StartsWith("Build"));
            t.Check(buildGizmo == (des != null), "a build button sits on the sign exactly when the shelf can be built (" + buildGizmo + ")");
            if (des != null)
            {
                t.Check(VenueSetup.StartBuilding(ThingDef.Named("Shelf")) && Find.DesignatorManager.SelectedDesignator == des, "the build button selects the architect tool");
                Find.DesignatorManager.Deselect();
            }

            var cells = room.Inner.Cells.Where(c => c != sign.Position).Take(3).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            v = V11Kit.VenueOf(map, sign);
            rep = VenueSetup.Report(v, 0);
            t.Check(rep.Row("storage").Ok && rep.Complete, "a stockpile in the room completes the shop");
            t.Check(VenueApi.Gate(v) == VenueGate.Open, "the ready shop opens (" + VenueApi.Gate(v) + ")");
            t.Check(rep.Row("stock") != null && !rep.Row("stock").Ok && !rep.Row("stock").Blocking, "no goods yet: an open point, not a blocker");
            t.Check(Hearth.Shell.GuideChecks.Eval("venueReady"), "guide step 'venueReady' is done");

            // --- tavern: counter, table and seats; planned furniture is counted as planned
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern); comp.alwaysOpen = true;
            v = V11Kit.VenueOf(map, sign);
            rep = VenueSetup.Report(v, 0);
            t.Check(!rep.Complete && rep.Blockers.Select(b => b.Req.id).OrderBy(x => x).SequenceEqual(new[] { "counter", "seats", "table" }), "empty tavern misses counter, table and seats: " + string.Join(",", rep.Blockers.Select(b => b.Req.id)));
            t.Check(VenueApi.Gate(v) == VenueGate.NotReady, "empty tavern: NotReady");
            var chairCell = new IntVec3(room.Inner.minX + 3, 0, room.Inner.minZ + 2);
            var bp = GenConstruct.PlaceBlueprintForBuild(ThingDef.Named("DiningChair"), chairCell, map, Rot4.North, Faction.OfPlayer, GenStuff.DefaultStuffFor(ThingDef.Named("DiningChair")));
            room.Things.Add(bp);
            v = V11Kit.VenueOf(map, sign);
            rep = VenueSetup.Report(v, 0);
            t.Check(rep.Row("seats").Planned == 1 && rep.Row("seats").Have == 0, "a chair blueprint counts as planned, not as there (" + rep.Row("seats").Planned + ")");
            bp.Destroy();
            V11Kit.FurnishTavern(map, room);
            v = V11Kit.VenueOf(map, sign);
            rep = VenueSetup.Report(v, 0);
            t.Check(rep.Row("counter").Have == 1 && rep.Row("table").Have == 1 && rep.Row("seats").Have == 2, $"furnished: counter {rep.Row("counter").Have}, table {rep.Row("table").Have}, seats {rep.Row("seats").Have}");
            t.Check(rep.Complete, "a furnished tavern is complete");
            t.Check(VenueApi.Gate(v) == VenueGate.WaitingForStaff, "a ready tavern waits for its bartender (" + VenueApi.Gate(v) + ")");
            t.Check(VenueSetup.ThingsFor(v, rep.Row("seats").Req).Count == 2, "Show finds the two chairs");
            t.Check(rep.Row("crowd") != null && !rep.Row("crowd").Req.required && !rep.Row("crowd").Ok, "six seats are recommended, not required");

            // --- the rule can be switched off
            comp.SetType(HHVenueDefOf.HH_Venue_Restaurant); comp.alwaysOpen = true;
            v = V11Kit.VenueOf(map, sign);
            V11Kit.SetBool(VenueSetup.RequireKey, false);
            t.Check(VenueApi.Gate(v) != VenueGate.NotReady && VenueSetup.IsReady(v), "rule off: missing items do not keep the doors shut");
            V11Kit.SetBool(VenueSetup.RequireKey, true);
            // --- Shops & Food off for this save: the setup rule belongs to it, so it holds no venue back (the contract answers the same)
            comp.SetType(HHVenueDefOf.HH_Venue_Casino);   // a casino without a game: not ready
            v = V11Kit.VenueOf(map, sign);
            t.Check(!Venues.IsReady(v) && !VenueSetup.IsReady(v) && !Venues.ReadySummary(v).NullOrEmpty(), "contract: a casino without a game is not ready (" + Venues.ReadySummary(v) + ")");
            HearthProfile.Current.SetModule(ModuleId.ShopsFood, false);
            bool offReady = VenueSetup.IsReady(v) && Venues.IsReady(v) && Venues.ReadySummary(v) == null;
            HearthProfile.Current.SetModule(ModuleId.ShopsFood, true);
            t.Check(offReady, "Shops & Food off: no venue is 'Not ready' and the overview shows no setup line");
            comp.SetType(HHVenueDefOf.HH_Venue_Restaurant);

            // --- a not-ready tavern is never rung at night (the bell is only for a closed, ready venue)
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern);
            int h = VenueTestKit.HourNow(map);
            comp.alwaysOpen = false; comp.openHour = (h + 6) % 24; comp.closeHour = (h + 9) % 24;
            comp.priceFactor = 0.25f;   // cheap enough for any guest even with the bell surcharge
            var beer = VenueTestKit.Stock(map, ThingDef.Named("Beer"), 10, cells[0], room);
            var guests = Hearth.Casino.Tests.Kit.Visitors(t, 1, 300, VenueTestKit.OutsideCell(map, room));
            var drink = GuestActivities.All.FirstOrDefault(a => a.Id == "venues.drink");
            if (guests.Count > 0 && drink != null && VS.B(VS.NightBell))
            {
                var g = guests[0]; var info = GuestApi.Info(g);
                GuestMemory.Clear(); VenueSales.ClearCaches();
                if (info != null && info.Budget.Silver < 50) info.Budget = Money.FromSilver(50);
                v = V11Kit.VenueOf(map, sign);
                var job = info == null ? null : drink.TryGiveJob(g, info);
                t.Check(VenueApi.Gate(v) == VenueGate.ClosedHours && job != null && job.def == HHVenueDefOf.HH_WaitAtVenue, "closed but ready tavern: the guest rings the bell (" + VenueApi.Gate(v) + ", " + job?.def.defName + ")");
                foreach (var th in room.Things.Where(x => x.def.defName == "HH_ShopCounter").ToList()) th.Destroy();
                v = V11Kit.VenueOf(map, sign);
                job = info == null ? null : drink.TryGiveJob(g, info);
                t.Check(VenueApi.Gate(v) == VenueGate.NotReady && job == null, "not ready: nobody rings the bell (" + VenueApi.Gate(v) + ")");
                foreach (var p in guests) if (!p.Destroyed) p.Destroy();
            }
            else t.Note("no visitor or no drink activity: bell check skipped");

            // --- honor system (F15): an unattended tavern needs no bartender's counter
            foreach (var th in room.Things.Where(x => x.def.defName == "HH_ShopCounter" && !x.Destroyed).ToList()) th.Destroy();
            comp.honorSystem = false; VenueSetup.MarkDirty(sign);
            v = V11Kit.VenueOf(map, sign);
            t.Check(!VenueSetup.Report(v, 0).Row("counter").Ok && !VenueSetup.IsReady(v), "tavern without a counter: not ready");
            comp.honorSystem = true; VenueSetup.MarkDirty(sign);
            v = V11Kit.VenueOf(map, sign);
            var counterRow = VenueSetup.Report(v, 0).Row("counter");
            t.Check(counterRow.Ok && !counterRow.Note.NullOrEmpty() && VenueSetup.IsReady(v), "honor system: the counter is not needed (" + counterRow.Note + ")");
            comp.honorSystem = false; VenueSetup.MarkDirty(sign);

            // --- the setup tab draws (any exception is a red error in the log)
            comp.alwaysOpen = true;
            Find.Selector.ClearSelection(); Find.Selector.Select(sign, false, false);
            comp.OpenSetupTab();
            yield return 60;
            var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            t.Check(pane?.OpenTabType == typeof(ITab_VenueSetup), "the setup gizmo opens the Setup tab (" + pane?.OpenTabType?.Name + ")");
            comp.Highlight(room.Things.Where(x => x.def.defName == "DiningChair"));
            yield return 30;
            Find.Selector.ClearSelection();
            Find.MainTabsRoot.EscapeCurrentTab(false);

            V11Kit.SetBool(VenueSetup.RequireKey, ruleWas);
            VenueTestKit.Teardown(map, room);
        }
    }

    // ================================================================================================ F33
    public sealed class NamesSuite : HearthTestSuite
    {
        public override string Name => "venues.names";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            bool autoWas = VS.B(VS.AutoNames);
            V11Kit.SetBool(VS.AutoNames, true);
            foreach (var type in DefDatabase<VenueTypeDef>.AllDefs)
            {
                t.Check(VenueNames.HasGenerator(type), type.defName + " has name rules");
                if (!VenueNames.HasGenerator(type)) continue;
                var names = new List<string>();
                for (int i = 0; i < 24; i++) names.Add(VenueNames.Generate(type, map, 1000 + i * 37));
                t.Check(names.All(n => !n.NullOrEmpty() && !n.Contains("[") && !n.Contains("ERR") && n.Length <= 40), type.defName + ": 24 clean names, e.g. " + string.Join(" / ", names.Take(4)));
                t.Check(names.Distinct().Count() >= 8, type.defName + ": varied (" + names.Distinct().Count() + " different of 24)");
                t.Check(VenueNames.Generate(type, map, 4242) == VenueNames.Generate(type, map, 4242), type.defName + ": the same seed gives the same name");
            }

            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            yield return 5;
            // SpawnSign switches the fresh shop sign to the requested type: shop → shop, so the name is the shop's
            t.Check(!comp.customName.NullOrEmpty() && comp.nameAuto, "a new sign gets a generated name: " + comp.customName);
            t.Check(comp.DisplayName == comp.customName, "the generated name is what the sign shows");
            string shopName = comp.customName;
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern);
            t.Check(comp.nameAuto && comp.customName != shopName, "a generated name follows a new type: " + shopName + " → " + comp.customName);
            string again = comp.customName;
            comp.SetType(HHVenueDefOf.HH_Venue_Shop); comp.SetType(HHVenueDefOf.HH_Venue_Tavern);
            t.Check(comp.customName == again, "the same sign, type and roll give the same name back");
            comp.AutoName(reroll: true);
            t.Check(comp.nameRoll == 1 && comp.customName != again, "rolling again gives another name: " + comp.customName);
            comp.customName = "Uncle Bo's"; comp.nameAuto = false;
            comp.SetType(HHVenueDefOf.HH_Venue_Shop);
            t.Check(comp.customName == "Uncle Bo's", "a typed name survives a type change");

            // two signs of the same type on one map never share a generated name
            var room2 = VenueTestKit.Build(map);
            var sign2 = VenueTestKit.SpawnSign(map, room2, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern); comp.AutoName();
            var comp2 = sign2.TryGetComp<CompVenueSignHH>();
            Venues.MarkDirty(map);
            for (int i = 0; i < 6; i++) { comp2.SetType(HHVenueDefOf.HH_Venue_Tavern); comp2.AutoName(reroll: true); t.Check(comp2.customName != comp.customName, "second tavern is not called like the first: " + comp2.customName); }

            // setting off: no name
            V11Kit.SetBool(VS.AutoNames, false);
            var room3 = VenueTestKit.Build(map);
            var sign3 = VenueTestKit.SpawnSign(map, room3, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            t.Check(sign3.TryGetComp<CompVenueSignHH>().customName.NullOrEmpty(), "with automatic names off a new sign shows its type");
            V11Kit.SetBool(VS.AutoNames, autoWas);

            // the rename window: suggest, accept → auto; typed → manual
            var dlg = new Dialog_RenameVenue(comp);
            Find.WindowStack.Add(dlg);
            yield return 30;
            var suggest = typeof(Dialog_RenameVenue).GetMethod("Suggest", BindingFlags.NonPublic | BindingFlags.Instance);
            var accept = typeof(Dialog_RenameVenue).GetMethod("Accept", BindingFlags.NonPublic | BindingFlags.Instance);
            string before = comp.customName;
            suggest.Invoke(dlg, null);
            accept.Invoke(dlg, null);
            t.Check(comp.nameAuto && comp.customName != before && !comp.customName.NullOrEmpty(), "rename window: a suggested name is taken as generated: " + comp.customName);
            var dlg2 = new Dialog_RenameVenue(comp);
            typeof(Dialog_RenameVenue).GetField("text", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(dlg2, "  The Quiet Corner ");
            accept.Invoke(dlg2, null);
            t.Check(!comp.nameAuto && comp.customName == "The Quiet Corner", "rename window: a typed name is trimmed and kept as typed");
            Find.WindowStack.TryRemove(typeof(Dialog_RenameVenue));
            // a name the player cleared stays cleared when the sign is reinstalled (no fresh generated name)
            V11Kit.SetBool(VS.AutoNames, true);
            comp2.customName = null; comp2.nameAuto = false;
            var pos2 = sign2.Position; var rot2 = sign2.Rotation;
            sign2.DeSpawn();
            GenSpawn.Spawn(sign2, pos2, map, rot2);
            yield return 2;
            t.Check(comp2.customName.NullOrEmpty(), "a reinstalled sign keeps the cleared name: " + (comp2.customName ?? "(type name)"));
            V11Kit.SetBool(VS.AutoNames, autoWas);
            VenueTestKit.Teardown(map, room3); VenueTestKit.Teardown(map, room2); VenueTestKit.Teardown(map, room);
        }
    }

    // ================================================================================================ F15
    public sealed class HonorSuite : HearthTestSuite
    {
        public override string Name => "venues.honor";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override int MaxTicks => 9000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            // --- the roll: chances hold over many sales
            var rng = new RngStream(777UL, 3UL);
            int none = 0, part = 0, n = 4000; double paidShare = 0; bool partRange = true;
            for (int i = 0; i < n; i++)
            {
                var o = VenueHonor.Roll(0.10f, 0.25f, rng, out float share);
                if (o == HonorOutcome.PaidNothing) none++;
                else if (o == HonorOutcome.PaidPart) { part++; if (share < 0.3f || share > 0.8f) partRange = false; }
                paidShare += share;
            }
            t.Check(partRange, "a part payment is always 30-80 % of the price");
            t.Check(Math.Abs(none / (double)n - 0.10) < 0.02, "pay nothing ≈ 10 %: " + (none / (double)n).ToString("P1"));
            t.Check(Math.Abs(part / (double)n - 0.25) < 0.025, "pay too little ≈ 25 %: " + (part / (double)n).ToString("P1"));
            t.Check(Math.Abs(paidShare / n - (0.65 + 0.25 * 0.55)) < 0.03, "average paid share ≈ 79 %: " + (paidShare / n).ToString("P1"));
            var none0 = VenueHonor.Roll(0f, 0f, rng, out float full);
            t.Check(none0 == HonorOutcome.PaidFull && full == 1f, "zero chances: everybody pays in full");

            // --- the venue side
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            var comp = sign.TryGetComp<CompVenueSignHH>(); comp.alwaysOpen = true;
            V11Kit.FurnishTavern(map, room);
            var cells = room.Inner.Cells.Where(c => c.GetFirstBuilding(map) == null && c != sign.Position).Take(3).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            yield return 10;
            var v = V11Kit.VenueOf(map, sign);
            t.Check(v != null && VenueApi.NeedsStaff(v), "a tavern needs a bartender");
            t.Check(VenueHonor.Offered(HHVenueDefOf.HH_Venue_Tavern) && VenueHonor.Offered(HHVenueDefOf.HH_Venue_Shop) && !VenueHonor.Offered(HHVenueDefOf.HH_Venue_Restaurant) && !VenueHonor.Offered(HHVenueDefOf.HH_Venue_Casino), "honor system: taverns and shops only");
            float selfService = VenueApi.PriceMultiplier(v);
            comp.honorSystem = true; Venues.MarkDirty(map);
            v = V11Kit.VenueOf(map, sign);
            t.Check(VenueHonor.IsOn(v) && !VenueApi.NeedsStaff(v) && VenueApi.Gate(v) == VenueGate.Open, "honor system on: open without anybody on duty (" + VenueApi.Gate(v) + ")");
            t.Check(!WorkGiver_ServeVenue.WantsTender(v), "nobody is sent to tend an honor-system venue");
            t.Check(VenueApi.PriceMultiplier(v) >= selfService - 0.0001f, "the honor box asks the full price, no self-service discount");
            t.Check(comp.CompGetGizmosExtra().OfType<Command_Toggle>().Any(g => g.defaultLabel == "Honor system"), "the sign offers the honor system switch");
            comp.SetType(HHVenueDefOf.HH_Venue_Restaurant);
            t.Check(!comp.honorSystem, "switching to a type without honor system switches it off");
            comp.SetType(HHVenueDefOf.HH_Venue_Tavern); comp.honorSystem = true;

            // --- a real purchase: whatever the guest pays plus what is missing is the list price, and only the paid part moves
            var guests = Hearth.Casino.Tests.Kit.Visitors(t, 1, 500, VenueTestKit.OutsideCell(map, room));
            var beer = VenueTestKit.Stock(map, ThingDef.Named("Beer"), 20, cells[0], room);
            yield return 5;
            v = V11Kit.VenueOf(map, sign);
            // a colonist who started tending before the switch leaves the counter (the honor box is only used while nobody is on duty)
            for (int i = 0; i < 12 && VenueApi.IsStaffed(v); i++) yield return 30;
            t.Check(!VenueApi.IsStaffed(v) && VenueHonor.Applies(v), "after the switch nobody stays on duty: the honor box is used");
            if (guests.Count == 0 || GuestApi.Info(guests[0]) == null) { t.Note("no visitor: purchase skipped"); }
            else
            {
                var g = guests[0]; var info = GuestApi.Info(g);
                GuestMemory.Clear();
                var budgetBefore = info.Budget; var tillBefore = VenueTestKit.Till(map);
                Job job = null;
                for (int i = 0; i < 5 && job == null; i++) job = VenueShopping.TryBuyJob(g, info, v, HHVenueDefOf.HH_BuyGoods);
                t.Check(job != null, "the guest finds something to buy");
                if (job != null)
                {
                    var listPrice = VenueApi.PriceOf(job.targetA.Thing, job.count, v);
                    g.jobs.StartJob(job, JobCondition.InterruptForced);
                    for (int i = 0; i < 60 && comp.honorSales == 0; i++) yield return 50;
                    t.Check(comp.honorSales == 1, "the sale went through the honor box (" + comp.honorSales + ")");
                    var paid = comp.HonorPaid; var missing = comp.HonorShort;
                    t.Check(Math.Abs((paid + missing).Silver - listPrice.Silver) < 0.011, $"paid {paid} + missing {missing} = list price {listPrice}");
                    t.Check(Math.Abs((budgetBefore - info.Budget).Silver - paid.Silver) < 0.011 || info.Budget >= budgetBefore - paid, "the guest's budget went down by what was paid (" + (budgetBefore - info.Budget) + ")");
                    t.Check(comp.HonorReportLine().Contains("1 sales"), "the sign reports the honor sale: " + comp.HonorReportLine());
                    t.Check(comp.CompInspectStringExtra().Contains("Honor"), "the inspect pane shows the honor box");
                }
                foreach (var p in guests) if (!p.Destroyed) p.Destroy();
            }
            comp.honorSystem = false;
            VenueTestKit.Teardown(map, room);
        }
    }

    // ================================================================================================ F14
    public sealed class ServingSuite : HearthTestSuite
    {
        public override string Name => "venues.serving";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            t.Check(HHVenueDefOf.HH_ServedBusy?.stages?.Count == 3 && HHVenueDefOf.HH_ServedIdle?.stages?.Count == 3, "busy and idle thoughts have three stages");
            t.Check(HHVenueDefOf.HH_ServedBusy.stages.All(s => s.baseMoodEffect > 0) && HHVenueDefOf.HH_ServedIdle.stages.All(s => s.baseMoodEffect < 0), "busy pleases, idle bores");
            // a colonist who can talk to people at all (Social not disabled by the backstory)
            var pawn = TestSetup.Colonist(map, 0, p => p.skills?.GetSkill(SkillDefOf.Social) != null && !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled && p.needs?.mood != null);
            t.Check(pawn != null, "a colonist who can do social work");
            if (pawn == null) yield break;
            var sk = pawn.skills.GetSkill(SkillDefOf.Social);
            int lvl = sk.Level; var pas = sk.passion;

            // --- Social drives the quality
            sk.passion = Passion.None;
            sk.Level = 0; float q0 = VenueService.Quality(pawn);
            sk.Level = 10; float q10 = VenueService.Quality(pawn);
            sk.Level = 20; float q20 = VenueService.Quality(pawn);
            sk.passion = Passion.Major; float q20p = VenueService.Quality(pawn);
            t.Check(q0 < q10 && q10 < q20 && q20 < q20p, $"quality grows with Social and passion: {q0:0.00} < {q10:0.00} < {q20:0.00} < {q20p:0.00}");
            float wWas = VS.F(VS.ServiceSkill);
            HSettings.Set(VS.ServiceSkill, 0f);
            sk.Level = 0; float flat0 = VenueService.Quality(pawn); sk.Level = 20; float flat20 = VenueService.Quality(pawn);
            t.Check(Math.Abs(flat0 - flat20) < 0.001f, "weight 0: skill does not matter");
            HSettings.Set(VS.ServiceSkill, wWas);
            t.Check(VenueService.SatisfactionBonus(q20) > VenueService.SatisfactionBonus(q0) && VenueService.SpendFactor(q20) > VenueService.SpendFactor(q0) && VenueService.BrowseFactor(q20) < VenueService.BrowseFactor(q0),
                "a skilled tender pleases more, sells more and sells faster");
            var rng = new RngStream(99UL, 5UL);
            int tips = 0; bool capped = true;
            for (int i = 0; i < 400; i++)
            {
                var tip = VenueService.RollTip(1f, Money.FromSilver(100), rng);
                if (tip.IsPositive) tips++;
                if (tip.Silver > 100 * VS.F(VS.TipShare) + 0.001) capped = false;
            }
            t.Check(tips > 100 && capped, "an excellent tender gets tips (" + tips + " of 400), never above the tip share");
            int poorTips = 0;
            for (int i = 0; i < 400; i++) if (VenueService.RollTip(0.15f, Money.FromSilver(100), rng).IsPositive) poorTips++;
            t.Check(poorTips == 0, "a poor tender gets no tips");
            t.Check(VenueService.BusyStage(0.5f, 1.5f) == -1 && VenueService.BusyStage(1.5f, 1.5f) == 0 && VenueService.BusyStage(3f, 1.5f) == 1 && VenueService.BusyStage(5f, 1.5f) == 2, "busy stages by guests per hour");

            // --- a busy shift
            bool moodWas = VS.B(VS.ShiftThoughts);
            V11Kit.SetBool(VS.ShiftThoughts, true);
            sk.passion = Passion.None; sk.Level = 10;
            var mem = pawn.needs?.mood?.thoughts?.memories;
            if (mem == null) { t.Note("colonist without mood: thoughts skipped"); }
            else
            {
                mem.RemoveMemoriesOfDef(HHVenueDefOf.HH_ServedBusy); mem.RemoveMemoriesOfDef(HHVenueDefOf.HH_ServedIdle);
                var room = VenueTestKit.Build(map);
                var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
                VenueService.EndShift(pawn);
                VenueService.BeginShift(pawn, sign);
                for (int i = 0; i < 6; i++) VenueService.OnServedSale(pawn, null);
                t.Check(VenueService.ServedThisShift(pawn) == 6, "six guests served this shift");
                VenueService.BackdateShift(pawn, 2 * GenDate.TicksPerHour, alsoLastServed: false);
                VenueService.EndShift(pawn);
                var busy = mem.GetFirstMemoryOfDef(HHVenueDefOf.HH_ServedBusy);
                t.Check(busy != null && busy.CurStageIndex == 1, "three guests an hour: 'a busy shift' (stage " + busy?.CurStageIndex + ")");

                // --- a boring shift: nobody for longer than the setting, the yawn comes while tending; a guest ends the boredom
                VenueService.BeginShift(pawn, sign);
                int idle = Mathf.RoundToInt(VS.F(VS.IdleHours) * GenDate.TicksPerHour);
                VenueService.BackdateShift(pawn, idle + 10);
                VenueService.TendTick(pawn);
                var bored = mem.GetFirstMemoryOfDef(HHVenueDefOf.HH_ServedIdle);
                t.Check(bored != null && bored.CurStageIndex == 0, "a quiet stretch: 'bored at the counter'");
                t.Check(mem.GetFirstMemoryOfDef(HHVenueDefOf.HH_ServedBusy) != null, "the busy memory of the last shift stays");
                VenueService.BackdateShift(pawn, idle);
                VenueService.TendTick(pawn);
                t.Check(mem.GetFirstMemoryOfDef(HHVenueDefOf.HH_ServedIdle)?.CurStageIndex == 1, "longer still: 'stood around all shift'");
                VenueService.NoteServed(pawn);
                VenueService.TendTick(pawn);
                t.Check(mem.GetFirstMemoryOfDef(HHVenueDefOf.HH_ServedIdle)?.CurStageIndex == 1, "a guest stops it getting worse");
                VenueService.EndShift(pawn);

                // --- setting off: no thoughts
                mem.RemoveMemoriesOfDef(HHVenueDefOf.HH_ServedBusy); mem.RemoveMemoriesOfDef(HHVenueDefOf.HH_ServedIdle);
                V11Kit.SetBool(VS.ShiftThoughts, false);
                VenueService.BeginShift(pawn, sign);
                for (int i = 0; i < 6; i++) VenueService.NoteServed(pawn);
                VenueService.BackdateShift(pawn, 2 * GenDate.TicksPerHour, alsoLastServed: false);
                VenueService.EndShift(pawn);
                t.Check(mem.GetFirstMemoryOfDef(HHVenueDefOf.HH_ServedBusy) == null, "shift moods off: no thought");
                // other modules (spa, salon, casino) report served guests through the contract
                VenueService.EndShift(pawn);
                VenueService.BeginShift(pawn, sign);
                Venues.NoteServed(pawn); Venues.NoteServed(pawn);
                t.Check(VenueService.ServedThisShift(pawn) == 2, "Venues.NoteServed (contract) counts for the shift: " + VenueService.ServedThisShift(pawn));
                VenueService.EndShift(pawn);
                // the tender line shows the skill
                VenueService.BeginShift(pawn, sign); VenueService.NoteServed(pawn);
                string line = VenueService.TenderLine(pawn);
                t.Check(line.Contains(pawn.LabelShort) && line.Contains("10") && line.Contains("1 served"), "tender line: " + line);
                VenueService.EndShift(pawn);
                VenueTestKit.Teardown(map, room);
            }
            V11Kit.SetBool(VS.ShiftThoughts, moodWas);
            sk.Level = lvl; sk.passion = pas;
            yield return 1;
        }
    }

    // ================================================================================================ F35
    public sealed class DialogsSuite : HearthTestSuite
    {
        public override string Name => "venues.dialogs";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var room = VenueTestKit.Build(map);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Shop);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            var cells = room.Inner.Cells.Where(c => c != sign.Position).Take(4).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            VenueTestKit.Stock(map, ThingDefOf.Steel, 30, cells[0], room);
            VenueTestKit.Stock(map, ThingDefOf.Cloth, 30, cells[1], room);
            yield return 10;
            Find.CameraDriver.JumpToCurrentMapLoc(sign.Position);
            var windows = new Window[] { new Dialog_VenueHours(comp), new Dialog_VenuePrice(comp), new Dialog_RenameVenue(comp), new Dialog_VenueRestock(comp) };
            var old = new[] { new Vector2(480f, 360f), new Vector2(480f, 330f), new Vector2(360f, 170f), new Vector2(460f, 640f) };
            for (int i = 0; i < windows.Length; i++)
            {
                var w = windows[i];
                var sz = w.InitialSize;
                float maxH = UI.screenHeight * 0.92f - 35f;
                t.Check(sz.x >= old[i].x - 0.5f && (sz.y >= old[i].y - 0.5f || sz.y >= maxH - 1f), w.GetType().Name + " is at least as large as in 1.0 (" + sz + ")");
                Find.WindowStack.Add(w);
                yield return 45;
                t.Check(Find.WindowStack.IsOpen(w), w.GetType().Name + " stays open and draws");
                Find.WindowStack.TryRemove(w);
                yield return 2;
            }
            // presets in the hours window set the hours
            comp.alwaysOpen = true;
            var hours = new Dialog_VenueHours(comp);
            typeof(Dialog_VenueHours).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hours, new object[] { 9, 17, false });
            t.Check(!comp.alwaysOpen && comp.openHour == 9 && comp.closeHour == 17, "hours apply");
            comp.alwaysOpen = true;
            VenueTestKit.Teardown(map, room);
        }
    }

    // ================================================================================================ screenshots (explicit only)
    /// <summary>Screenshots of the 1.1.0 venue UI for a visual check: setup tab (not ready / ready), the four sign windows, casino seats. HH_TEST=venues.shots.</summary>
    public sealed class VenueShotsSuite : HearthTestSuite
    {
        public override string Name => "venues.shots";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 60000;

        /// <summary>Waits real time (frames), not ticks: a screenshot needs drawn frames, and the test game runs many ticks per frame.</summary>
        static IEnumerable<int> Wait(float seconds) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < seconds) yield return 1; }
        static IEnumerable<int> Shot()
        {
            foreach (var w in Wait(1.2f)) yield return w;
            ScreenshotTaker.TakeNonSteamShot();
            foreach (var w in Wait(1.2f)) yield return w;
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var room = VenueTestKit.Build(map, 7);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = false; comp.openHour = 16; comp.closeHour = 2;
            var r = room.Inner;
            V11Kit.Spawn(map, "Table2x2c", new IntVec3(r.minX + 2, 0, r.minZ + 2), Rot4.North);
            V11Kit.Spawn(map, "DiningChair", new IntVec3(r.minX + 1, 0, r.minZ + 2), Rot4.East);
            yield return 20;
            foreach (var c in room.Outer.ExpandedBy(3)) if (c.InBounds(map)) map.fogGrid.Unfog(c);   // test rooms may stand in fogged rock
            Find.CameraDriver.SetRootPosAndSize(sign.DrawPos, 14f);   // close enough to see the room
            Find.Selector.ClearSelection(); Find.Selector.Select(sign, false, false);
            comp.OpenSetupTab();
            foreach (var w in Shot()) yield return w;
            V11Kit.Spawn(map, "HH_ShopCounter", new IntVec3(r.minX + 4, 0, r.maxZ), Rot4.North);
            V11Kit.Spawn(map, "DiningChair", new IntVec3(r.minX + 4, 0, r.minZ + 2), Rot4.West);
            var cells = r.Cells.Where(c => c.GetFirstBuilding(map) == null && c != sign.Position).Take(4).ToList();
            VenueTestKit.Stockpile(map, room, cells);
            VenueTestKit.Stock(map, ThingDef.Named("Beer"), 25, cells[0], room);
            VenueTestKit.Stock(map, ThingDefOf.MealSimple, 10, cells[1], room);
            VenueSetup.MarkDirty(sign);
            yield return 60;
            comp.OpenSetupTab();
            foreach (var w in Shot()) yield return w;
            foreach (var win in new Window[] { new Dialog_VenueHours(comp), new Dialog_VenuePrice(comp), new Dialog_RenameVenue(comp), new Dialog_VenueRestock(comp) })
            {
                Find.WindowStack.Add(win);
                foreach (var w in Shot()) yield return w;
                win.Close(false);
                yield return 5;
            }
            // casino table with its seats
            var table = Hearth.Casino.Tests.Kit.Spawn(t, Hearth.Casino.CasinoDefOf.HH_PokerTable, Rot4.North, room.Outer.CenterCell + new IntVec3(12, 0, 0));
            var play = table.TryGetComp<Hearth.Casino.CompCasinoGame>().PlayCells().Take(2).ToList();
            foreach (var c in play) V11Kit.Spawn(map, "Armchair", c, Rot4.North);
            Hearth.Casino.CasinoSeats.MarkDirty(table);
            Find.Selector.ClearSelection(); Find.Selector.Select(table, false, false);
            foreach (var c in CellRect.CenteredOn(table.Position, 6)) if (c.InBounds(map)) map.fogGrid.Unfog(c);
            Find.CameraDriver.SetRootPosAndSize(table.DrawPos, 9f);   // the seat outlines are one cell wide
            foreach (var w in Shot()) yield return w;
            Find.Selector.ClearSelection();
            foreach (var w in Wait(4f)) yield return w;
            string outDir = "/private/tmp/claude-501/hh_tests/venues_shots";
            System.IO.Directory.CreateDirectory(outDir);
            try { foreach (var f in System.IO.Directory.GetFiles(GenFilePaths.ScreenshotFolderPath, "*.png")) System.IO.File.Copy(f, System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(f)), true); }
            catch (Exception e) { t.Note("copy failed: " + e.Message); }
            t.Note("screenshots in " + outDir);
            t.Check(true, "screenshots taken");
        }
    }
}

namespace Hearth.Tests
{
    using Hearth.ShopsFood;

    // ================================================================================================ F13
    public sealed class FlipSideSuite : HearthTestSuite
    {
        public override string Name => "core.flipside";
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var flipDefs = DefDatabase<ThingDef>.AllDefs.Where(d => !d.IsBlueprint && !d.IsFrame && d.comps != null && d.comps.Any(c => c is CompProperties_FlipWorkSide)).ToList();
            t.Check(flipDefs.Count >= 15, "switch work side on counters, desks, tables and stations: " + flipDefs.Count + " defs");
            foreach (var n in new[] { "HH_ShopCounter", "HH_MarketStall", "HH_FrontDesk", "SL_BankCounter", "HH_MassageTable", "HH_CashierCage", "HH_RouletteTable", "HH_PokerTable", "HH_BlackjackTable", "HH_Station_PrepCounter", "HH_Station_Oven", "SL_ATM", "HH_SlotMachine" })
                t.Check(flipDefs.Any(d => d.defName == n), n + " can switch its work side");
            foreach (var d in flipDefs) t.Check(d.comps.Count(c => c is CompProperties_FlipWorkSide) == 1, d.defName + ": exactly one switch comp");

            // --- the art fix: defs whose front faces their rotation put the worker in front
            foreach (var n in new[] { "HH_SlotMachine", "HH_Station_Oven", "HH_Station_Smoker", "SL_ATM" })
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(n); if (d == null) { t.Note(n + " missing"); continue; }
                t.Check(d.interactionCellOffset == new IntVec3(0, 0, 1) && d.HasModExtension<LegacyWorkSideExtension>() && d.defaultPlacingRot == Rot4.South, n + ": worker in front, placed facing south, old saves turned");
                // the one-time turn of an old save keeps the worker's side and the cells
                foreach (var rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                {
                    var th = ThingMaker.MakeThing(d, d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null);
                    var pos = map.Center;
                    th.Position = pos; th.Rotation = rot;
                    var rect0 = GenAdj.OccupiedRect(pos, rot, d.size);
                    var oldIc = pos + new IntVec3(0, 0, -1).RotatedBy(rot);
                    CompFlipWorkSide.ApplyLegacyTurn(th);
                    var rect1 = GenAdj.OccupiedRect(th.Position, th.Rotation, d.size);
                    var newIc = ThingUtility.InteractionCellWhenAt(d, th.Position, th.Rotation, map);
                    bool sameSide = rot.IsHorizontal ? newIc.x == oldIc.x && Math.Abs(newIc.z - oldIc.z) <= 1 : newIc.z == oldIc.z && Math.Abs(newIc.x - oldIc.x) <= 1;
                    t.Check(rect0 == rect1 && th.Rotation == rot.Opposite && sameSide && (d.size.x > 1 || newIc == oldIc), $"{n} {rot}: old save turned on its cells, worker keeps the side ({oldIc} → {newIc})");
                    th.Destroy();
                }
            }

            // --- switching on placed buildings: same cells, the worker changes side, twice = back
            var around = t.FreeCellNearCenter(30);
            foreach (var d in flipDefs.Where(x => x.BuildableByPlayer && x.hasInteractionCell).Take(30))
            {
                var spot = TestSetup.FindOrMakeSpot(map, around, d.size.x + 2, d.size.z + 2, 2);
                var th = V11Kit.Spawn(map, d.defName, spot, Rot4.North);
                var rect0 = th.OccupiedRect(); var ic0 = th.InteractionCell; var pos0 = th.Position;
                bool can = CompFlipWorkSide.CanFlip(th, out string why);
                t.Check(can, d.defName + " can switch (" + why + ")");
                if (can)
                {
                    CompFlipWorkSide.Flip(th);
                    var ic1 = th.InteractionCell;
                    t.Check(th.OccupiedRect() == rect0 && th.Rotation == Rot4.South, d.defName + ": same cells after the switch " + th.OccupiedRect() + " vs " + rect0);
                    bool across = (ic0.z < rect0.minZ && ic1.z > rect0.maxZ) || (ic0.z > rect0.maxZ && ic1.z < rect0.minZ);
                    t.Check(across, $"{d.defName}: the worker moved to the opposite side ({ic0} → {ic1})");
                    t.Check(rect0.Cells.All(c => th.Map.thingGrid.ThingsListAt(c).Contains(th)), d.defName + ": registered on every one of its cells");
                    CompFlipWorkSide.Flip(th);
                    t.Check(th.Position == pos0 && th.Rotation == Rot4.North && th.InteractionCell == ic0, d.defName + ": switching twice puts it back");
                }
                th.Destroy();
                yield return 1;
            }

            // --- blocked other side; bills survive; the gizmo
            var spot2 = TestSetup.FindOrMakeSpot(map, around, 4, 4, 2);
            var prep = (Building_WorkTable)V11Kit.Spawn(map, "HH_Station_PrepCounter", spot2, Rot4.North);
            var recipe = prep.def.AllRecipes.FirstOrDefault();
            if (recipe != null) prep.BillStack.AddBill(recipe.MakeNewBill());
            int bills = prep.BillStack.Count;
            var blockCell = CompFlipWorkSide.InteractionCellAfterFlip(prep);
            var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); wall.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(wall, blockCell, map);
            t.Check(!CompFlipWorkSide.CanFlip(prep, out string blocked) && !blocked.NullOrEmpty(), "a wall on the other side blocks the switch: " + blocked);
            var gz = prep.GetGizmos().OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel == "Switch work side");
            t.Check(gz != null && gz.Disabled, "the gizmo is there and disabled while blocked");
            wall.Destroy();
            // busy: a pawn whose job targets the station (a cook at work) keeps it from turning under them
            var worker = TestSetup.Colonist(map, 0, p => p.Spawned && !p.Downed && p.jobs != null);
            if (worker != null)
            {
                var job = JobMaker.MakeJob(JobDefOf.Wait, prep); job.expiryInterval = 2000;
                worker.jobs.StartJob(job, JobCondition.InterruptForced);
                t.Check(!CompFlipWorkSide.CanFlip(prep, out string busy) && busy != null && busy.Contains(worker.LabelShort), "a pawn using it: the switch waits (" + busy + ")");
                worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            else t.Note("no colonist for the busy check");
            t.Check(CompFlipWorkSide.Flip(prep) && prep.BillStack.Count == bills, "after the switch the bills are still there (" + prep.BillStack.Count + ")");
            prep.Destroy();

            // --- plans of an older save (blueprints, frames) are turned once on load like the buildings
            var oven = DefDatabase<ThingDef>.GetNamedSilentFail("HH_Station_Oven");
            if (oven != null)
            {
                t.Check(oven.blueprintDef?.comps?.Any(c => c is CompProperties_LegacyWorkSidePlan) == true && oven.frameDef?.comps?.Any(c => c is CompProperties_LegacyWorkSidePlan) == true,
                    "the oven's blueprint and frame watch for pre-1.1.0 plans");
                var planField = typeof(CompLegacyWorkSidePlan).GetField("sideChecked", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var pd in new[] { oven.blueprintDef, oven.frameDef })
                {
                    if (pd == null) continue;
                    var plan = ThingMaker.MakeThing(pd, null);
                    var pos = map.Center; plan.Position = pos; plan.Rotation = Rot4.North;
                    var rect0 = GenAdj.OccupiedRect(pos, Rot4.North, oven.size);
                    var pc = plan.TryGetComp<CompLegacyWorkSidePlan>();
                    t.Check(pc != null && !(bool)planField.GetValue(pc), pd.defName + ": a plan from an old save is not checked yet");
                    if (pc == null) continue;
                    var modeWas = Scribe.mode;
                    try { Scribe.mode = LoadSaveMode.PostLoadInit; pc.PostExposeData(); }
                    finally { Scribe.mode = modeWas; }
                    t.Check(plan.Rotation == Rot4.South && GenAdj.OccupiedRect(plan.Position, plan.Rotation, oven.size) == rect0 && (bool)planField.GetValue(pc),
                        pd.defName + ": turned once on its cells (" + plan.Rotation + ")");
                    plan.Destroy();
                }
                // a new plan is never turned
                var fresh = GenConstruct.PlaceBlueprintForBuild(oven, TestSetup.FindOrMakeSpot(map, around, 4, 3, 2), map, Rot4.North, Faction.OfPlayer, oven.MadeFromStuff ? GenStuff.DefaultStuffFor(oven) : null);
                var fc = fresh?.TryGetComp<CompLegacyWorkSidePlan>();
                t.Check(fc != null && (bool)planField.GetValue(fc) && fresh.Rotation == Rot4.North, "a blueprint placed now keeps its rotation and is marked checked");
                fresh?.Destroy();
            }
        }
    }

    /// <summary>
    /// Load a save made with 1.0 (the showcase of saveload.build) and check the 1.1.0 upgrade (explicit; run with LOAD=&lt;1.0 save&gt;):
    /// the oven, smoker, slot machines and ATM were turned once on their cells so the worker keeps the side (showcase: oven/smoker north → south,
    /// slots west/south → east/north, ATM west → east); old signs keep their (custom) names; nothing is switched on that the player did not choose.
    /// </summary>
    public sealed class LegacyLoadSuite : HearthTestSuite
    {
        public override string Name => "saveload.v11legacy";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            t.Note("loaded save: " + (SaveLoadKit.LoadedSave ?? "(none: quicktest)"));
            var field = typeof(CompFlipWorkSide).GetField("sideChecked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var legacy = map.listerThings.AllThings.Where(x => x.def.HasModExtension<LegacyWorkSideExtension>()).ToList();
            t.Check(legacy.Count > 0, "the save holds buildings whose work side changed: " + legacy.Count);
            foreach (var th in legacy)
            {
                var c = th.TryGetComp<CompFlipWorkSide>();
                t.Check(c != null && (bool)field.GetValue(c), th.def.defName + " at " + th.Position + ": marked as checked after loading");
            }
            foreach (var th in legacy.Where(x => x.def.defName == "HH_Station_Oven" || x.def.defName == "HH_Station_Smoker"))
                t.Check(th.Rotation == Rot4.South && th.InteractionCell.z == th.Position.z - 1, th.def.defName + " (placed facing north in 1.0) now faces south, cook in front at " + th.InteractionCell);
            foreach (var th in legacy.Where(x => x.def.defName == "HH_SlotMachine"))
                t.Check(th.Rotation == Rot4.East || th.Rotation == Rot4.North, "slot machine (placed west/south in 1.0) now faces " + th.Rotation + ", player at " + th.InteractionCell);
            foreach (var th in legacy.Where(x => x.def.defName == "SL_ATM"))
                t.Check(th.Rotation == Rot4.East && th.InteractionCell == th.Position + IntVec3.East, "ATM (placed west in 1.0) now faces east; the customer keeps the east cell");
            foreach (var th in legacy) t.Check(th.OccupiedRect().Cells.All(c => map.thingGrid.ThingsListAt(c).Contains(th)), th.def.defName + ": registered on its cells");
            foreach (var v in Venues.All(map))
            {
                var c = v.Comp as Hearth.ShopsFood.CompVenueSignHH;
                t.Check(c != null && !c.honorSystem && !c.nameAuto, v.DisplayNameOf() + ": honor system off, the old name was not replaced");
                t.Check(Hearth.ShopsFood.VenueSetup.Report(v, 0).Rows.Count > 0, v.DisplayNameOf() + ": setup checklist evaluates (" + (Hearth.ShopsFood.VenueSetup.Summary(v) ?? "") + ")");
            }
            yield return 600;
            t.Check(true, "the loaded game ran 600 ticks");
        }
    }
}
#endif
