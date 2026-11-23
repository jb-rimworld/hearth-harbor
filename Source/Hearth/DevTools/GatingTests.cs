// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Testing
{
    /// <summary>
    /// Module gate (N08): switching a module off hides its buildings (architect + placement), its research (tab, start) and its recipes,
    /// takes its work givers out of the work lists and keeps placed buildings and research progress; switching it on brings everything back.
    /// </summary>
    public sealed class GatingSuite : HearthTestSuite
    {
        public override string Name => "core.gating";

        static Designator_Build FindDesignator(BuildableDef def)
        {
            var cat = def.designationCategory;
            if (cat == null) return null;
            foreach (var d in cat.AllResolvedDesignators)
            {
                if (d is Designator_Build b && b.PlacingDef == def) return b;
                if (d is Designator_Dropdown dd) foreach (var e in dd.Elements) if (e is Designator_Build eb && eb.PlacingDef == def) return eb;
            }
            return null;
        }

        static List<ResearchProjectDef> TabProjects() => (MainButtonDefOf.Research.TabWindow as MainTabWindow_Research)?.VisibleResearchProjects;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var prof = HearthProfile.Current;
            t.Check(ModuleGate.Ready && ModuleGate.OwnedCount > 0, "module gate table built: " + ModuleGate.OwnedCount + " defs");
            var slot = DefDatabase<ThingDef>.GetNamedSilentFail("HH_SlotMachine");
            var resSlots = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Casino_Slots");
            var resTables = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Casino_Tables");
            var wgDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HH_RunCasinoTable");
            var sign = DefDatabase<ThingDef>.GetNamedSilentFail("HH_VenueSign");
            var shelfLike = DefDatabase<ThingDef>.GetNamedSilentFail("HH_ShopCounter");
            t.Check(slot != null && resSlots != null && resTables != null && wgDef != null && sign != null, "casino defs present");
            if (slot == null || resSlots == null || resTables == null || wgDef == null || prof == null) yield break;

            // the table
            t.Check(ModuleGate.Owner(slot) == ModuleId.Casino, "slot machine belongs to Casino (folder table)");
            t.Check(ModuleGate.Owner(resSlots) == ModuleId.Casino && ModuleGate.Owner(wgDef) == ModuleId.Casino, "casino research and work giver belong to Casino");
            t.Check(ModuleGate.Owner(sign) == ModuleId.Core, "venue sign serves every module (extension overrides the Venues folder)");
            if (shelfLike != null) t.Check(ModuleGate.Owner(shelfLike) == ModuleId.ShopsFood, "shop counter belongs to Shops & Food");
            t.Check(ModuleGate.Owner(ThingDefOf.Wall) == ModuleId.Core && !ModuleGate.IsOff(ThingDefOf.Wall), "vanilla defs are never gated");
            t.Check(slot.PlaceWorkers != null && slot.PlaceWorkers.Any(p => p is PlaceWorker_ModuleGate), "slot machine carries the gate place worker");

            bool wasGod = DebugSettings.godMode; DebugSettings.godMode = false;
            bool casinoWas = prof.ModuleOn(ModuleId.Casino), foodWas = prof.ModuleOn(ModuleId.ShopsFood);
            prof.SetModule(ModuleId.Casino, true);
            if (!resSlots.IsFinished) Find.ResearchManager.FinishProject(resSlots, doCompletionDialog: false, researcher: null, doCompletionLetter: false);
            float progressBefore = 0f;
            if (!resTables.IsFinished) { Find.ResearchManager.AddProgress(resTables, 37f); progressBefore = Find.ResearchManager.GetProgress(resTables); }

            var des = FindDesignator(slot);
            t.Check(des != null, "slot machine has a designator in its category");
            var cell = t.FreeCellNearCenter(25);
            if (des != null)
            {
                t.Check(des.Visible, "Casino on: slot machine designator visible");
                var rep = des.CanDesignateCell(cell);
                t.Check(rep.Accepted || !rep.Reason.NullOrEmpty() && !rep.Reason.Contains(Modules.Label(ModuleId.Casino)), "Casino on: placement not refused by the gate (" + rep.Reason + ")");
            }
            t.Check(!resTables.IsHidden, "Casino on: research visible");
            var tab = TabProjects();
            t.Check(tab != null && tab.Contains(resTables), "Casino on: research in the research tab");

            // a colonist with the work type of the casino table
            var pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.WorkTypeIsDisabled(wgDef.workType));
            if (pawn != null)
            {
                pawn.workSettings.SetPriority(wgDef.workType, 3);
                t.Check(pawn.workSettings.WorkGiversInOrderNormal.Any(w => w.def == wgDef), "Casino on: casino work giver in the work list");
            }
            else t.Note("no colonist can do " + wgDef.workType.defName + ": work list check skipped");

            // a placed machine stays when the module goes off
            var placed = ThingMaker.MakeThing(slot, slot.MadeFromStuff ? GenStuff.DefaultStuffFor(slot) : null);
            placed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(placed, cell, map, Rot4.North);
            yield return 1;

            // ---- off
            prof.SetModule(ModuleId.Casino, false);
            yield return 1;
            t.Check(!Modules.On(ModuleId.Casino) && ModuleGate.IsOff(slot), "Casino off");
            if (des != null)
            {
                t.Check(!des.Visible, "Casino off: slot machine designator hidden");
                var rep = des.CanDesignateCell(t.FreeCellNearCenter(30));
                t.Check(!rep.Accepted, "Casino off: placement refused (" + rep.Reason + ")");
            }
            t.Check(resTables.IsHidden && !resTables.CanStartNow, "Casino off: research hidden and cannot start");
            tab = TabProjects();
            t.Check(tab != null && !tab.Contains(resTables) && !tab.Contains(resSlots), "Casino off: research not in the research tab");
            t.Check(tab != null && tab.Contains(DefDatabase<ResearchProjectDef>.GetNamed("Electricity")), "Casino off: vanilla research still in the tab");
            if (progressBefore > 0f) t.Check(Math.Abs(Find.ResearchManager.GetProgress(resTables) - progressBefore) < 0.01f, "Casino off: research progress kept (" + Find.ResearchManager.GetProgress(resTables) + ")");
            if (pawn != null) t.Check(!pawn.workSettings.WorkGiversInOrderNormal.Any(w => w.def == wgDef), "Casino off: casino work giver out of the work list");
            t.Check(pawn == null || pawn.workSettings.GetPriority(wgDef.workType) == 3, "Casino off: the work type itself stays");
            t.Check(placed.Spawned && !placed.Destroyed, "Casino off: placed slot machine not deleted");
            string insp = placed.GetInspectString() ?? "";
            t.Check(insp.Contains(Modules.Label(ModuleId.Casino)), "Casino off: inspect says module off: " + insp.Replace("\n", " | "));
            var casinoRecipe = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r => ModuleGate.Owner(r) == ModuleId.Casino);
            if (casinoRecipe != null) t.Check(!casinoRecipe.AvailableNow, "Casino off: casino recipe unavailable: " + casinoRecipe.defName);
            else t.Note("no casino recipe to check");

            // ---- on again
            prof.SetModule(ModuleId.Casino, true);
            yield return 1;
            if (des != null) t.Check(des.Visible, "Casino on again: designator back");
            t.Check(!resTables.IsHidden, "Casino on again: research visible");
            tab = TabProjects();
            t.Check(tab != null && tab.Contains(resTables), "Casino on again: research back in the tab");
            if (pawn != null) t.Check(pawn.workSettings.WorkGiversInOrderNormal.Any(w => w.def == wgDef), "Casino on again: work giver back");
            insp = placed.GetInspectString() ?? "";
            t.Check(!insp.Contains(ModuleGate.OffText(ModuleId.Casino)), "Casino on again: no module-off line");

            // ---- Shops & Food off: dish bills wait, the inn research does not wait for the kitchen research
            var dishRecipe = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r => r.defName.StartsWith("HH_Cook_") && r.modContentPack == null || r.defName.StartsWith("HH_Cook_") && r.fileName.NullOrEmpty());
            var inn = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Innkeeping");
            var cook1 = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Cooking_L1");
            t.Check(dishRecipe != null, "a generated dish recipe exists: " + dishRecipe?.defName);
            t.Check(ModuleGate.Owner(DefDatabase<ThingDef>.GetNamed("HH_Station_StockPot")) == ModuleId.ShopsFood, "kitchen stations belong to Shops & Food");
            prof.SetModule(ModuleId.ShopsFood, false);
            yield return 1;
            if (dishRecipe != null)
            {
                t.Check(ModuleGate.Owner(dishRecipe) == ModuleId.ShopsFood, "dish recipe belongs to Shops & Food (by its plate)");
                t.Check(!dishRecipe.AvailableNow, "Shops & Food off: dish recipe not offered");
                var bill = (Bill_Production)dishRecipe.MakeNewBill();
                t.Check(!bill.ShouldDoNow(), "Shops & Food off: an existing dish bill waits");
            }
            // the prerequisite postfix, with the research progress reset so that neither project is finished
            var progressField = HarmonyLib.AccessTools.Field(typeof(ResearchManager), "progress");
            var progressDict = progressField?.GetValue(Find.ResearchManager) as Dictionary<ResearchProjectDef, float>;
            if (inn != null && cook1 != null && progressDict != null)
            {
                var savedProgress = new Dictionary<ResearchProjectDef, float>();
                foreach (var rp in new[] { inn, cook1 }) { progressDict.TryGetValue(rp, out var v); savedProgress[rp] = v; progressDict[rp] = 0f; }
                prof.SetModule(ModuleId.ShopsFood, true);
                yield return 1;
                t.Check(!inn.IsFinished && !cook1.IsFinished, "prerequisite test: both projects reset to unfinished");
                t.Check(!inn.PrerequisitesCompleted, "Shops & Food on: Innkeeping needs the kitchen research");
                prof.SetModule(ModuleId.ShopsFood, false);
                yield return 1;
                t.Check(!cook1.CanStartNow && cook1.IsHidden, "Shops & Food off: the kitchen research itself is hidden and cannot start");
                t.Check(inn.PrerequisitesCompleted, "Shops & Food off: Innkeeping does not wait for the hidden kitchen research");
                t.Check(inn.CanStartNow == (!inn.IsFinished && inn.PrerequisitesCompleted && inn.TechprintRequirementMet), "Shops & Food off: Innkeeping can start (" + inn.CanStartNow + ")");
                prof.SetModule(ModuleId.ShopsFood, true);
                yield return 1;
                t.Check(!inn.PrerequisitesCompleted, "Shops & Food on again: Innkeeping needs the kitchen research again");
                foreach (var kv in savedProgress) { if (kv.Value <= 0f) progressDict.Remove(kv.Key); else progressDict[kv.Key] = kv.Value; }
            }
            else t.Note("research progress table not reachable here: prerequisite check skipped");

            // the venues module keeps its own buildings; no other module uses them
            foreach (var n in new[] { "HH_ShopCounter", "HH_MarketStall", "HH_VendingMachine" })
            {
                var vd = DefDatabase<ThingDef>.GetNamedSilentFail(n);
                if (vd != null) t.Check(ModuleGate.Owner(vd) == ModuleId.ShopsFood, n + " belongs to Shops & Food only");
            }
            t.Check(ModuleGate.Owner(DefDatabase<ThingDef>.GetNamed("HH_VenueSignWall")) == ModuleId.Core, "wall sign stays Core");

            // right-click orders of an off module: the provider hands back nothing
            prof.SetModule(ModuleId.Casino, false);
            yield return 1;
            var fm = HarmonyLib.AccessTools.TypeByName("RimWorld.FloatMenuOptionProvider_WorkGivers");
            var gm = fm == null ? null : HarmonyLib.AccessTools.Method(fm, "GetWorkGiverOption");
            if (gm != null && gm.ReturnType == typeof(FloatMenuOption))
            {
                object inst = null;
                try { if (!gm.IsStatic) inst = Activator.CreateInstance(fm, true); } catch (Exception) { }
                var ps = gm.GetParameters(); var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (pt == typeof(WorkGiverDef)) args[i] = wgDef;
                    else if (pt == typeof(Pawn)) args[i] = pawn;
                    else if (pt.IsValueType) args[i] = Activator.CreateInstance(pt);
                }
                if (gm.IsStatic || inst != null)
                {
                    object res = "unset";
                    try { res = gm.Invoke(inst, args); } catch (Exception e) { t.Check(false, "right-click provider threw: " + (e.InnerException ?? e).Message); }
                    if (!(res is string)) t.Check(res == null, "Casino off: right-click provider offers nothing for a casino work giver");
                }
                else t.Note("right-click provider cannot be instantiated here: check skipped");
            }
            else t.Note("right-click provider not present in this game version (patch skips itself)");

            // blueprints of an off module are not delivered to (positive control with the module on)
            if (pawn != null && slot.blueprintDef != null && slot.CostList != null)
            {
                prof.SetModule(ModuleId.Casino, true);
                yield return 1;
                var bpCell = t.FreeCellNearCenter(40);
                var stuff = slot.MadeFromStuff ? GenStuff.DefaultStuffFor(slot) : null;
                var bp = GenConstruct.PlaceBlueprintForBuild(slot, bpCell, map, Rot4.North, Faction.OfPlayer, stuff) as Thing;
                var spawnedRes = new List<Thing>();
                if (bp != null)
                {
                    foreach (var cc in slot.CostListAdjusted(stuff))
                    {
                        var th = ThingMaker.MakeThing(cc.thingDef); th.stackCount = cc.count;
                        var near = CellFinder.StandableCellNear(bpCell + new IntVec3(2, 0, 0), map, 6);
                        GenSpawn.Spawn(th, near, map); spawnedRes.Add(th);
                    }
                    yield return 1;
                    var wg = new WorkGiver_ConstructDeliverResourcesToBlueprints { def = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints") };
                    Job jobOn = null; try { jobOn = wg.JobOnThing(pawn, bp, false); } catch (Exception) { }
                    prof.SetModule(ModuleId.Casino, false);
                    yield return 1;
                    Job jobOff = null; try { jobOff = wg.JobOnThing(pawn, bp, false); } catch (Exception) { }
                    if (jobOn != null) t.Check(jobOff == null, "Casino off: blueprint of a casino building gets no delivery job");
                    else t.Note("blueprint delivery positive control gave no job (resources not reachable?): check skipped");
                    bp.Destroy();
                    foreach (var th in spawnedRes) if (!th.Destroyed) th.Destroy();
                }
                else t.Note("blueprint could not be placed: construction check skipped");
            }

            // restore
            if (!placed.Destroyed) placed.Destroy();
            prof.SetModule(ModuleId.Casino, casinoWas);
            prof.SetModule(ModuleId.ShopsFood, foodWas);
            DebugSettings.godMode = wasGod;
        }
    }
}
#endif
