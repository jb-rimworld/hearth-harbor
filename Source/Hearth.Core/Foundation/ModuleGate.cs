// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Optional owner of one def, for the few defs whose folder does not say which module they belong to
    /// (the venue sign serves every module, the guest shrine belongs to City Life). Overrides the folder table of <see cref="ModuleGate"/>.
    /// </summary>
    public class ModuleGateExtension : DefModExtension
    {
        public ModuleId module = ModuleId.Core;
    }

    /// <summary>
    /// Per-save module hiding (N08): one central table def → module, built once after loading from the def folder (Common/Defs/&lt;Folder&gt;)
    /// or a <see cref="ModuleGateExtension"/>. While a module is switched off in the save:
    /// its buildings leave the architect menu and cannot be placed (already placed ones stay and say "module off" when inspected),
    /// its research projects leave the research tab (progress is kept), its recipes cannot be added and existing bills wait,
    /// its work givers are not in any work list (the work types themselves stay, vanilla needs stable work types) and its incidents do not fire.
    /// The error barrier (Guard pause) does not hide anything: only the player's switch does.
    /// </summary>
    public static class ModuleGate
    {
        /// <summary>Def folder → module. Folders not listed (Core, Hospitality, UI, Dlc …) belong to Core and are never hidden.</summary>
        public static readonly Dictionary<string, ModuleId> FolderModules = new Dictionary<string, ModuleId>(StringComparer.OrdinalIgnoreCase)
        {
            { "Inn", ModuleId.Inn },
            { "Kitchen", ModuleId.ShopsFood }, { "Venues", ModuleId.ShopsFood },
            { "Casino", ModuleId.Casino },
            { "Wellness", ModuleId.Wellness },
            { "City", ModuleId.CityLife },
            { "Underworld", ModuleId.Underworld },
            { "Bank", ModuleId.Bank }, { "Insurance", ModuleId.Bank }, { "Finance", ModuleId.Bank },
        };

        /// <summary>
        /// By reference: Def.GetHashCode/Equals go by the def name hash, and defs made in code (the dish recipes) never resolve it, so they
        /// all hash to 0 and would be "equal" to each other and to any other mod's generated def.
        /// </summary>
        sealed class ByRef : IEqualityComparer<Def>
        {
            public static readonly ByRef Instance = new ByRef();
            public bool Equals(Def a, Def b) => ReferenceEquals(a, b);
            public int GetHashCode(Def d) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d);
        }
        static readonly Dictionary<Def, ModuleId> owner = new Dictionary<Def, ModuleId>(ByRef.Instance);
        static readonly HashSet<Def> withGatedPrereq = new HashSet<Def>(ByRef.Instance);
        static readonly List<ThingDef> gatedBuildings = new List<ThingDef>();
        static readonly List<ResearchProjectDef> gatedResearch = new List<ResearchProjectDef>();
        static bool initDone;
        static int lastMask = -1;

        /// <summary>Raised after the shown modules changed (and the caches were refreshed). Argument: the new mask (bit = module shown).</summary>
        public static event Action<int> Changed;

        public static bool Ready => initDone;
        public static int OwnedCount => owner.Count;
        public static IReadOnlyList<ThingDef> GatedBuildings => gatedBuildings;
        public static IReadOnlyList<ResearchProjectDef> GatedResearch => gatedResearch;

        /// <summary>The module a def belongs to (Core for everything not ours or not gated).</summary>
        public static ModuleId Owner(Def d) => d != null && owner.TryGetValue(d, out var m) ? m : ModuleId.Core;

        /// <summary>Is the module switched on for this save (template outside a game)? Unlike Modules.On, an error pause does not count.</summary>
        public static bool Shown(ModuleId m)
        {
            if (m == ModuleId.Core) return true;
            if (!Modules.Available(m)) return false;
            var p = HearthProfile.Current;
            return p == null ? HearthMod.Template.ModuleOn(m) : p.ModuleOn(m);
        }

        /// <summary>True when the def belongs to a module that is switched off. One hash lookup for defs that are not ours.</summary>
        public static bool IsOff(Def d)
        {
            if (d == null || owner.Count == 0) return false;
            return owner.TryGetValue(d, out var m) && !Shown(m);
        }

        public static int CurrentMask()
        {
            int mask = 0;
            var all = Modules.All;
            for (int i = 0; i < all.Length; i++) if (Shown(all[i])) mask |= 1 << (int)all[i];
            return mask;
        }

        /// <summary>Builds the table. Called once after all defs are loaded (HearthStartup, after the modules' OnDefsLoaded built their generated defs).</summary>
        public static void Init()
        {
            if (initDone) return;
            initDone = true;
            var pack = HearthMod.Instance?.Content;
            if (pack == null) { HLog.Warn("Core", "Module gate: no content pack, nothing is hidden"); return; }
            var fileFolder = ScanDefFolders(pack);

            void Take(Def d)
            {
                if (d == null) return;
                ModuleId m = ModuleId.Core; bool found = false;
                var ext = d.GetModExtension<ModuleGateExtension>();
                if (ext != null) { m = ext.module; found = true; }
                else if (d.modContentPack == pack && d.fileName != null && fileFolder.TryGetValue(d.fileName, out var folder) && FolderModules.TryGetValue(folder, out var fm)) { m = fm; found = true; }
                if (found && m != ModuleId.Core) owner[d] = m;
            }
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading) Take(d);
            foreach (var d in DefDatabase<TerrainDef>.AllDefsListForReading) Take(d);
            foreach (var d in DefDatabase<ResearchProjectDef>.AllDefsListForReading) Take(d);
            foreach (var d in DefDatabase<WorkGiverDef>.AllDefsListForReading) Take(d);
            foreach (var d in DefDatabase<JoyGiverDef>.AllDefsListForReading) Take(d);
            foreach (var d in DefDatabase<IncidentDef>.AllDefsListForReading) Take(d);
            foreach (var d in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                Take(d);
                if (owner.ContainsKey(d) || d.GetModExtension<ModuleGateExtension>() != null) continue;
                // generated recipes (dishes, "make X" of a recipeMaker) have no file: they belong to the module of their product
                if (d.products != null)
                    for (int i = 0; i < d.products.Count; i++)
                    {
                        var pd = d.products[i]?.thingDef;
                        if (pd != null && owner.TryGetValue(pd, out var pm)) { owner[d] = pm; break; }
                    }
            }

            // buildings: a place worker hides the designator and blocks placement, a comp tells "module off" on placed ones
            var pwCache = AccessTools.Field(typeof(BuildableDef), "placeWorkersInstantiatedInt");
            foreach (var kv in owner)
            {
                if (!(kv.Key is ThingDef td) || td.IsBlueprint || td.IsFrame) continue;
                if (td.designationCategory == null && td.blueprintDef == null) continue;
                gatedBuildings.Add(td);
                if (td.placeWorkers == null) td.placeWorkers = new List<Type>();
                if (!td.placeWorkers.Contains(typeof(PlaceWorker_ModuleGate)))
                {
                    td.placeWorkers.Add(typeof(PlaceWorker_ModuleGate));
                    pwCache?.SetValue(td, null);
                }
                if (td.thingClass != null && typeof(ThingWithComps).IsAssignableFrom(td.thingClass))
                {
                    if (td.comps == null) td.comps = new List<CompProperties>();
                    if (!td.comps.Any(c => c?.compClass == typeof(CompModuleGate))) td.comps.Add(new CompProperties(typeof(CompModuleGate)));
                }
            }
            foreach (var kv in owner) if (kv.Key is ResearchProjectDef rp) gatedResearch.Add(rp);
            foreach (var r in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (HasGatedPrereq(r, r.prerequisites) || HasGatedPrereq(r, r.hiddenPrerequisites)) withGatedPrereq.Add(r);
            }
            var counts = owner.GroupBy(kv => kv.Value).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count());
            HLog.Info("Core", $"Module gate: {owner.Count} defs of switchable modules ({string.Join(", ", counts)}); {gatedBuildings.Count} buildings, {gatedResearch.Count} research projects, {withGatedPrereq.Count} projects with a switchable prerequisite");
        }

        static bool HasGatedPrereq(ResearchProjectDef r, List<ResearchProjectDef> list)
        {
            if (list == null) return false;
            var mine = Owner(r);
            for (int i = 0; i < list.Count; i++) { var o = Owner(list[i]); if (o != ModuleId.Core && o != mine) return true; }
            return false;
        }

        /// <summary>XML file name → first folder under Defs, for every Defs folder of our content pack (Common, version folders).</summary>
        static Dictionary<string, string> ScanDefFolders(ModContentPack pack)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var roots = new List<string>();
                try { roots.AddRange(pack.foldersToLoadDescendingOrder); } catch (Exception) { }
                if (roots.Count == 0) roots.Add(pack.RootDir);
                foreach (var root in roots)
                {
                    var defs = Path.Combine(root, "Defs");
                    if (!Directory.Exists(defs)) continue;
                    foreach (var file in Directory.GetFiles(defs, "*.xml", SearchOption.AllDirectories))
                    {
                        var rel = file.Substring(defs.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        int cut = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                        if (cut <= 0) continue;   // files directly in Defs belong to Core
                        var name = Path.GetFileName(file);
                        if (!map.ContainsKey(name)) map[name] = rel.Substring(0, cut);
                    }
                }
            }
            catch (Exception e) { HLog.Warn("Core", "Module gate: could not scan the def folders: " + e.Message); }
            return map;
        }

        // ---- prerequisites -------------------------------------------------------------------------------------------------
        /// <summary>A project whose prerequisite belongs to a module that is off: that prerequisite counts as done (it cannot be researched now).</summary>
        internal static bool PrerequisitesDoneIgnoringOff(ResearchProjectDef r)
        {
            if (!withGatedPrereq.Contains(r)) return false;
            return AllDoneOrOff(r.prerequisites) && AllDoneOrOff(r.hiddenPrerequisites);
        }
        static bool AllDoneOrOff(List<ResearchProjectDef> list)
        {
            if (list == null) return true;
            for (int i = 0; i < list.Count; i++) if (!list[i].IsFinished && !IsOff(list[i])) return false;
            return true;
        }

        // ---- refresh ---------------------------------------------------------------------------------------------------------
        /// <summary>Cheap check (a few hash lookups): refreshes the caches when the shown modules changed. Called on every switch, at game init and every 60 ticks.</summary>
        public static void Poll(bool force = false)
        {
            if (!initDone) return;
            int mask = CurrentMask();
            if (!force && mask == lastMask) return;
            lastMask = mask;
            Guard.Run(ModuleId.Core, "ModuleGate.Refresh", () => Refresh(mask));
        }

        static void Refresh(int mask)
        {
            ResearchVisibleFilter.Invalidate();
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null) { lastMask = -1; return; }   // while loading: the next tick poll does the rest
            // architect: other architect mods cache the designators of a category; a selected designator of a hidden building is dropped
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading) cat.DirtyCache();
            var sel = Find.DesignatorManager?.SelectedDesignator;
            if (sel is Designator_Place place && IsOff(place.PlacingDef)) Find.DesignatorManager.Deselect();
            // research: a running project of a hidden module stops (its progress stays)
            var rm = Find.ResearchManager;
            if (rm != null)
            {
                var cur = rm.GetProject();
                if (cur != null && IsOff(cur)) { try { rm.StopProject(cur); } catch (Exception e) { HLog.Warn("Core", "Module gate: could not stop " + cur.defName + ": " + e.Message); } }
            }
            // work: every pawn rebuilds its work giver list (the filter below leaves out work givers of hidden modules)
            foreach (var map in Find.Maps)
                foreach (var p in map.mapPawns.AllPawnsSpawned) p.workSettings?.Notify_UseWorkPrioritiesChanged();
            foreach (var c in Find.WorldObjects.Caravans) foreach (var p in c.PawnsListForReading) p.workSettings?.Notify_UseWorkPrioritiesChanged();
            HLog.Info("Core", "Module gate: shown modules now " + string.Join(", ", Modules.All.Where(m => (mask & (1 << (int)m)) != 0)));
            var h = Changed;
            if (h != null) foreach (var d in h.GetInvocationList()) { try { ((Action<int>)d)(mask); } catch (Exception e) { HLog.Error("Core", "Subscriber of ModuleGate.Changed threw (ignored)", e); } }
        }

        internal static string OffText(ModuleId m) =>
            "HH.Gate.ModuleOff".TranslateOr("Module off: {0}").Replace("{0}", Modules.Label(m));
    }

    /// <summary>Added to every building of a switchable module at startup: hides its designator and blocks new placement while the module is off.</summary>
    public class PlaceWorker_ModuleGate : PlaceWorker
    {
        public override bool IsBuildDesignatorVisible(BuildableDef def) => !ModuleGate.IsOff(def);

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // reinstalling a building that already exists stays possible; only new ones are refused
            if (thing != null || thingToIgnore != null) return true;
            if (!ModuleGate.IsOff(checkingDef)) return true;
            return new AcceptanceReport("HH.Gate.CannotPlace".TranslateOr("Not available: {0} is switched off in this game.").Replace("{0}", Modules.Label(ModuleGate.Owner(checkingDef))));
        }
    }

    /// <summary>Inspect line on placed buildings of a module that is off. Saves nothing.</summary>
    public class CompModuleGate : ThingComp
    {
        public override string CompInspectStringExtra()
        {
            if (parent == null || !ModuleGate.IsOff(parent.def)) return null;
            return ModuleGate.OffText(ModuleGate.Owner(parent.def)) + " – " + "HH.Gate.ModuleOffInspect".TranslateOr("this does nothing until the module is switched on again.");
        }
    }

    // ---- research ---------------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsHidden), MethodType.Getter)]
    static class Patch_GateResearchHidden
    {
        static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            if (!__result && ModuleGate.IsOff(__instance)) __result = true;
        }
    }

    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.PrerequisitesCompleted), MethodType.Getter)]
    static class Patch_GateResearchPrerequisites
    {
        static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            if (!__result && ModuleGate.PrerequisitesDoneIgnoringOff(__instance)) __result = true;
        }
    }

    /// <summary>The research tab draws only the projects of its visible list: projects of a module that is off are left out (no "unknown" boxes).</summary>
    [HarmonyPatch]
    static class ResearchVisibleFilter
    {
        static List<ResearchProjectDef> source, filtered;
        static int version, filteredVersion = -1;
        internal static void Invalidate() => version++;

        static bool Prepare() => AccessTools.PropertyGetter(typeof(MainTabWindow_Research), "VisibleResearchProjects") != null;
        static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(MainTabWindow_Research), "VisibleResearchProjects");

        static void Postfix(ref List<ResearchProjectDef> __result)
        {
            if (__result == null || ModuleGate.GatedResearch.Count == 0) return;
            if (!ReferenceEquals(__result, source) || filteredVersion != version)
            {
                source = __result; filteredVersion = version;
                bool any = false;
                for (int i = 0; i < __result.Count && !any; i++) any = ModuleGate.IsOff(__result[i]);
                filtered = any ? __result.Where(p => !ModuleGate.IsOff(p)).ToList() : null;
            }
            if (filtered != null) __result = filtered;
        }
    }

    // ---- recipes and bills ----------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    static class Patch_GateRecipeAvailable
    {
        static void Postfix(RecipeDef __instance, ref bool __result)
        {
            if (__result && ModuleGate.IsOff(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    static class Patch_GateBillShouldDoNow
    {
        static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (__result && ModuleGate.IsOff(__instance.recipe)) __result = false;
        }
    }

    [HarmonyPatch]
    static class Patch_GateBillStatus
    {
        static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(Bill_Production), "StatusString");
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Bill_Production __instance, ref string __result)
        {
            if (__instance?.recipe != null && ModuleGate.IsOff(__instance.recipe)) __result = ModuleGate.OffText(ModuleGate.Owner(__instance.recipe));
        }
    }

    // ---- work, joy, incidents ------------------------------------------------------------------------------------------------------
    /// <summary>Work givers of a module that is off are left out of every pawn's work list (rebuilt on each switch). Not a hot path: runs only when the list is dirty.</summary>
    [HarmonyPatch(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder")]
    static class Patch_GateWorkGivers
    {
        static readonly AccessTools.FieldRef<Pawn_WorkSettings, List<WorkGiver>> normal = AccessTools.FieldRefAccess<Pawn_WorkSettings, List<WorkGiver>>("workGiversInOrderNormal");
        static readonly AccessTools.FieldRef<Pawn_WorkSettings, List<WorkGiver>> emerg = AccessTools.FieldRefAccess<Pawn_WorkSettings, List<WorkGiver>>("workGiversInOrderEmerg");
        static void Postfix(Pawn_WorkSettings __instance)
        {
            if (ModuleGate.OwnedCount == 0) return;
            normal(__instance)?.RemoveAll(w => ModuleGate.IsOff(w?.def));
            emerg(__instance)?.RemoveAll(w => ModuleGate.IsOff(w?.def));
        }
    }

    /// <summary>Right-click orders: a work giver of a module that is off offers nothing.</summary>
    [HarmonyPatch]
    static class Patch_GateWorkGiverFloatMenu
    {
        static MethodBase Target() => AccessTools.TypeByName("RimWorld.FloatMenuOptionProvider_WorkGivers") is Type t ? AccessTools.Method(t, "GetWorkGiverOption") : null;
        static bool Prepare() => Target() != null;
        static MethodBase TargetMethod() => Target();
        static bool Prefix(WorkGiverDef workGiver, ref FloatMenuOption __result)
        {
            if (!ModuleGate.IsOff(workGiver)) return true;
            __result = null; return false;
        }
    }

    [HarmonyPatch(typeof(JoyGiver), nameof(JoyGiver.CanBeGivenTo))]
    static class Patch_GateJoyGiver
    {
        static void Postfix(JoyGiver __instance, ref bool __result)
        {
            if (__result && ModuleGate.IsOff(__instance.def)) __result = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow))]
    static class Patch_GateIncident
    {
        static void Postfix(IncidentWorker __instance, ref bool __result)
        {
            if (__result && ModuleGate.IsOff(__instance.def)) __result = false;
        }
    }
}
