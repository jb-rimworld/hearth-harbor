// SPDX-License-Identifier: MIT
// Takeover when Hospitality runs alongside Hearth & Harbor (KONZEPT 12.6, N61). Only names of the other mods are used.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Compat.Hospitality
{
    /// <summary>
    /// Makes Hospitality and its add-ons inert after all mods are loaded, independent of the load order:
    /// removes their Harmony patches (recognised by the assembly of the patch method, not by Harmony id),
    /// sets their incidents to chance 0, hides their buildings and main tab, strips the tabs and comps they inject into
    /// other mods' defs, and silences their map/game components and alerts. Every step is guarded and logged on its own.
    /// </summary>
    public static class HospitalityTakeover
    {
        public static bool Active { get; private set; }
        public static bool Ran { get; private set; }
        public static readonly List<string> Report = new List<string>();
        public static int PatchesRemoved, IncidentsSilenced, BuildablesHidden, MainButtonsHidden, TabsRemoved, CompsRemoved, WorkGiversRemoved, JoyGiversSilenced, MethodsSilenced;
        public static bool VisitorGroupRestored;
        /// <summary>Failures of single steps (the step is skipped, the rest keeps going).</summary>
        public static readonly List<string> StepFailures = new List<string>();

        /// <summary>Runs the takeover once per session. Called after all StaticConstructorOnStartup classes have run.</summary>
        public static void Run()
        {
            if (Ran) return;
            Ran = true;
            if (!HospitalityFamily.AnyReplacedActive)
            {
                HLog.Debug("Compat", "No Hospitality family mod active, nothing to take over");
                return;
            }
#if HH_TESTS
            if (Environment.GetEnvironmentVariable("HH_HSP_NOTAKEOVER") == "1")
            {
                HLog.Warn("Compat", "Test switch HH_HSP_NOTAKEOVER=1: Hospitality takeover skipped (fixture creation)");
                return;
            }
#endif
            Active = true;
            HLog.Info("Compat", "Hospitality family active alongside Hearth & Harbor: " + string.Join(", ", HospitalityFamily.Detected.Select(d => d.ToString()))
                + ". Taking over (KONZEPT 12.6).");
            Step("Harmony patches", () => UnpatchFamily("startup"));
            Step("Incidents", SilenceIncidents);
            Step("Architect menu", HideBuildables);
            Step("Main tab", HideMainButtons);
            Step("Inspector tabs", RemoveInjectedTabs);
            Step("Injected comps", RemoveInjectedComps);
            Step("Work givers", RemoveWorkGivers);
            Step("Joy givers", SilenceJoyGivers);
            Step("Components and alerts", SilenceComponents);
            string sum = $"Takeover done: {PatchesRemoved} patches removed, {IncidentsSilenced} incidents at chance 0{(VisitorGroupRestored ? " (vanilla visitor group restored)" : "")}, "
                + $"{BuildablesHidden} buildings hidden, {MainButtonsHidden} main tabs hidden, {TabsRemoved} inspector tabs and {CompsRemoved} comps removed from other defs, "
                + $"{WorkGiversRemoved} work givers and {JoyGiversSilenced} joy givers off, {MethodsSilenced} component/alert methods silenced, {StepFailures.Count} steps failed.";
            Report.Add(sum);
            HLog.Info("Compat", sum);
        }

        static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                StepFailures.Add(name + ": " + e.GetType().Name + ": " + e.Message);
                HLog.Error("Compat", "Hospitality takeover step '" + name + "' failed; the other steps continue", e);
            }
        }

        static void Note(string line) { Report.Add(line); HLog.Info("Compat", line); }

        // ---------------------------------------------------------------- 1. Harmony
        /// <summary>
        /// Removes every Harmony patch (prefix, postfix, transpiler, finalizer, IL manipulator) whose patch method lives in a
        /// replaced family assembly. Safe to call again (e.g. after a game load, in case a family mod patched late).
        /// </summary>
        public static int UnpatchFamily(string when)
        {
            var asms = HospitalityFamily.TakeoverAssemblies;
            if (asms.Count == 0) return 0;
            int removed = 0, failed = 0, sample = 3;
            var methods = Harmony.GetAllPatchedMethods().ToList();
            var owners = new Dictionary<string, int>();
            foreach (var m in methods)
            {
                Patches pi = null; try { pi = Harmony.GetPatchInfo(m); } catch { }
                if (pi == null) continue;
                foreach (var o in pi.Owners) { owners.TryGetValue(o, out int c); owners[o] = c + 1; }
            }
            HLog.Info("Compat", $"Harmony ({when}): {methods.Count} patched methods, owners: {string.Join(", ", owners.Select(kv => kv.Key + "=" + kv.Value))}");
            foreach (var original in methods)
            {
                Patches info;
                try { info = Harmony.GetPatchInfo(original); } catch { continue; }
                if (info == null) continue;
                foreach (var patch in AllPatches(info))
                {
                    var pm = patch?.PatchMethod;
                    if (pm?.DeclaringType == null || !HospitalityFamily.IsTakeoverAssembly(pm.DeclaringType.Assembly))
                    {
                        if (pm != null && sample-- > 0 && HospitalityFamily.Detected.Any(d => d.Pack.PackageIdPlayerFacing == patch.owner))
                            HLog.Warn("Compat", $"Patch by family owner {patch.owner} not matched by assembly: {pm.DeclaringType?.FullName}.{pm.Name} in {pm.DeclaringType?.Assembly.FullName ?? pm.Module?.Assembly.FullName} ({pm.GetType().Name})");
                        continue;
                    }
                    try
                    {
                        HearthMod.Harmony.Unpatch(original, pm);
                        removed++;
                        HLog.Debug("Compat", $"Unpatched {pm.DeclaringType.FullName}.{pm.Name} (owner {patch.owner}) from {original.DeclaringType?.FullName}.{original.Name}");
                    }
                    catch (Exception e)
                    {
                        failed++;
                        HLog.Warn("Compat", $"Could not remove patch {pm.DeclaringType.FullName}.{pm.Name} on {original.DeclaringType?.FullName}.{original.Name}: {e.Message}");
                    }
                }
            }
            PatchesRemoved += removed;
            if (removed > 0 || failed > 0) Note($"Harmony ({when}): removed {removed} Hospitality family patches{(failed > 0 ? ", " + failed + " could not be removed" : "")}");
            return removed;
        }

        /// <summary>All patch lists of a PatchInfo, including ones newer Harmony versions add (read by reflection).</summary>
        static IEnumerable<Patch> AllPatches(Patches info)
        {
            // Harmony exposes the lists as public fields (Prefixes, Postfixes, Transpilers, Finalizers, ILManipulators …);
            // read every field or property of a Patch collection type so newer patch kinds are covered too.
            var seen = new HashSet<Patch>();
            foreach (var f in info.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IEnumerable<Patch>).IsAssignableFrom(f.FieldType)) continue;
                if (f.GetValue(info) is IEnumerable<Patch> list) foreach (var x in list.ToList()) if (x != null && seen.Add(x)) yield return x;
            }
            foreach (var p in info.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IEnumerable<Patch>).IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length > 0) continue;
                IEnumerable<Patch> list = null;
                try { list = p.GetValue(info) as IEnumerable<Patch>; } catch { }
                if (list != null) foreach (var x in list.ToList()) if (x != null && seen.Add(x)) yield return x;
            }
        }

        /// <summary>Number of patches from replaced family assemblies still applied anywhere (0 after the takeover).</summary>
        public static int RemainingFamilyPatches()
        {
            var asms = HospitalityFamily.TakeoverAssemblies;
            int n = 0;
            foreach (var original in Harmony.GetAllPatchedMethods().ToList())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                foreach (var patch in AllPatches(info))
                    if (patch?.PatchMethod?.DeclaringType != null && HospitalityFamily.IsTakeoverAssembly(patch.PatchMethod.DeclaringType.Assembly)) n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- 2. Incidents
        static readonly FieldInfo workerIntField = AccessTools.Field(typeof(IncidentDef), "workerInt");

        static void SilenceIncidents()
        {
            foreach (var def in DefDatabase<IncidentDef>.AllDefsListForReading)
            {
                bool familyWorker = HospitalityFamily.IsTakeoverType(def.workerClass);
                if (!familyWorker && !HospitalityFamily.IsTakeoverDef(def)) continue;
                if (def.defName == "VisitorGroup")
                {
                    // Hospitality replaces vanilla's VisitorGroup def (same defName, its own worker, chance 0 + own queue).
                    // Without the Inn module, vanilla visitors are our guests (GuestApi fallback), so vanilla behaviour comes back.
                    RestoreVanillaVisitorGroup(def);
                    continue;
                }
                def.baseChance = 0f;
                if (def.baseChanceWithRoyalty >= 0f) def.baseChanceWithRoyalty = 0f;
                IncidentsSilenced++;
                HLog.Debug("Compat", "Incident " + def.defName + " set to chance 0");
            }
            Note($"Incidents: {IncidentsSilenced} Hospitality family incidents set to chance 0");
        }

        /// <summary>Puts the values of Core's VisitorGroup back (read from the Core XML, fallback to the 1.7 values).</summary>
        static void RestoreVanillaVisitorGroup(IncidentDef def)
        {
            float baseChance = 4f, minRefire = 0f; bool requireColonists = true; Type worker = typeof(IncidentWorker_VisitorGroup);
            try
            {
                var core = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.IsCoreMod);
                string path = core == null ? null : Path.Combine(core.RootDir, "Defs", "Storyteller", "Incidents_Map_Special.xml");
                if (path != null && File.Exists(path))
                {
                    var doc = new XmlDocument(); doc.Load(path);
                    var node = doc.SelectSingleNode("/Defs/IncidentDef[defName='VisitorGroup']");
                    if (node != null)
                    {
                        float.TryParse(node["baseChance"]?.InnerText ?? "4", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out baseChance);
                        if (node["minRefireDays"] != null) float.TryParse(node["minRefireDays"].InnerText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minRefire);
                        requireColonists = node["requireColonistsPresent"] == null || node["requireColonistsPresent"].InnerText.Trim().ToLowerInvariant() == "true";
                        var wn = node["workerClass"]?.InnerText;
                        var wt = wn == null ? null : GenTypes.GetTypeInAnyAssembly(wn, "RimWorld");
                        if (wt != null && typeof(IncidentWorker).IsAssignableFrom(wt)) worker = wt;
                    }
                }
            }
            catch (Exception e) { HLog.Warn("Compat", "Could not read Core's VisitorGroup def, using built-in values: " + e.Message); }
            def.workerClass = worker;
            def.baseChance = baseChance;
            def.minRefireDays = minRefire;
            def.requireColonistsPresent = requireColonists;
            var allLayers = AccessTools.Field(typeof(IncidentDef), "canOccurOnAllPlanetLayers");
            allLayers?.SetValue(def, false);
            workerIntField?.SetValue(def, null);
            VisitorGroupRestored = true;
            Note($"Incidents: VisitorGroup restored to vanilla ({worker.Name}, chance {baseChance})");
        }

        // ---------------------------------------------------------------- 3. Architect menu
        static readonly MethodInfo resolveDesignators = AccessTools.Method(typeof(DesignationCategoryDef), "ResolveDesignators");

        static bool IsFamilyBuildable(BuildableDef d)
        {
            if (HospitalityFamily.IsTakeoverDef(d)) return true;
            return d is ThingDef td && HospitalityFamily.IsTakeoverType(td.thingClass);
        }

        static void HideBuildables()
        {
            var touched = new HashSet<DesignationCategoryDef>();
            foreach (var td in DefDatabase<ThingDef>.AllDefsListForReading)
                if (td.designationCategory != null && IsFamilyBuildable(td)) { touched.Add(td.designationCategory); td.designationCategory = null; BuildablesHidden++; }
            foreach (var tr in DefDatabase<TerrainDef>.AllDefsListForReading)
                if (tr.designationCategory != null && IsFamilyBuildable(tr)) { touched.Add(tr.designationCategory); tr.designationCategory = null; BuildablesHidden++; }
            foreach (var cat in touched)
            {
                if (resolveDesignators != null) resolveDesignators.Invoke(cat, null);
                else cat.AllResolvedDesignators.RemoveAll(des => des is Designator_Build b && IsFamilyBuildable(b.PlacingDef));
                cat.DirtyCache();
            }
            // a family designation category that is now empty is hidden too
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
                if (HospitalityFamily.IsTakeoverDef(cat)) { cat.AllResolvedDesignators.Clear(); cat.DirtyCache(); }
            Note($"Architect menu: {BuildablesHidden} Hospitality family buildings hidden");
        }

        /// <summary>Family buildables still placeable from the architect menu (0 after the takeover).</summary>
        public static int VisibleFamilyDesignators()
        {
            int n = 0;
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
                foreach (var des in cat.AllResolvedDesignators)
                {
                    if (des is Designator_Build b && IsFamilyBuildable(b.PlacingDef)) n++;
                    else if (des is Designator_Dropdown dd) n += dd.Elements.Count(e => e is Designator_Build eb && IsFamilyBuildable(eb.PlacingDef));
                }
            return n;
        }

        // ---------------------------------------------------------------- 4. Main tab
        static void HideMainButtons()
        {
            foreach (var mb in DefDatabase<MainButtonDef>.AllDefsListForReading)
            {
                if (!HospitalityFamily.IsTakeoverDef(mb) && !HospitalityFamily.IsTakeoverType(mb.tabWindowClass) && !HospitalityFamily.IsTakeoverType(mb.workerClass)) continue;
                if (!mb.buttonVisible) continue;
                mb.buttonVisible = false;
                MainButtonsHidden++;
            }
            Note($"Main tab: {MainButtonsHidden} Hospitality family main tabs hidden");
        }

        // ---------------------------------------------------------------- 5. Inspector tabs injected into other defs
        static void RemoveInjectedTabs()
        {
            foreach (var td in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (HospitalityFamily.IsTakeoverDef(td)) continue;   // their own buildings keep their own tabs
                if (td.inspectorTabs != null) TabsRemoved += td.inspectorTabs.RemoveAll(HospitalityFamily.IsTakeoverType);
                if (td.inspectorTabsResolved != null) td.inspectorTabsResolved.RemoveAll(tab => tab != null && HospitalityFamily.IsTakeoverType(tab.GetType()));
            }
            Note($"Inspector tabs: {TabsRemoved} Hospitality family tabs removed from other mods' things (no second guest tab)");
        }

        // ---------------------------------------------------------------- 6. Comps injected into other defs (CompGuest on every humanlike …)
        static void RemoveInjectedComps()
        {
            foreach (var td in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (td.comps == null || td.comps.Count == 0 || HospitalityFamily.IsTakeoverDef(td)) continue;
                // generated guest beds are clones made by Hospitality itself; they are family content and stay untouched
                if (HospitalityFamily.IsTakeoverType(td.thingClass)) continue;
                CompsRemoved += td.comps.RemoveAll(c => c != null && (HospitalityFamily.IsTakeoverType(c.GetType()) || HospitalityFamily.IsTakeoverType(c.compClass)));
            }
            Note($"Comps: {CompsRemoved} Hospitality family comps removed from other mods' defs (new things only; loaded ones keep their data unread)");
        }

        // ---------------------------------------------------------------- 7. Work and joy givers
        static void RemoveWorkGivers()
        {
            foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (wt.workGiversByPriority == null) continue;
                WorkGiversRemoved += wt.workGiversByPriority.RemoveAll(g => g != null && (HospitalityFamily.IsTakeoverDef(g) || HospitalityFamily.IsTakeoverType(g.giverClass)));
            }
            Note($"Work: {WorkGiversRemoved} Hospitality family work givers removed");
        }

        static void SilenceJoyGivers()
        {
            foreach (var jg in DefDatabase<JoyGiverDef>.AllDefsListForReading)
            {
                if (!HospitalityFamily.IsTakeoverDef(jg) && !HospitalityFamily.IsTakeoverType(jg.giverClass)) continue;
                jg.baseChance = 0f;
                JoyGiversSilenced++;
            }
            Note($"Joy: {JoyGiversSilenced} Hospitality family joy givers set to chance 0");
        }

        // ---------------------------------------------------------------- 8. Map/game/world components and alerts
        static readonly string[] ComponentLoopMethods =
        {
            "MapComponentTick", "MapComponentUpdate", "MapComponentOnGUI",
            "GameComponentTick", "GameComponentUpdate", "GameComponentOnGUI",
            "WorldComponentTick", "WorldComponentUpdate", "WorldComponentOnGUI",
        };

        /// <summary>Hospitality's map component shows its main tab again in FinalizeInit; these calls are skipped while taken over.</summary>
        static readonly (string, string)[] KnownUndoers =
        {
            ("Hospitality.Mod_Hospitality", "ToggleTabIfNeeded"),
            ("Hospitality.Mod_Hospitality", "UpdateMainButtonIcon"),
        };

        /// <summary>Hides family main tabs again (after a game start, in case a family component showed them).</summary>
        public static void RehideMainButtons()
        {
            if (!Active) return;
            foreach (var mb in DefDatabase<MainButtonDef>.AllDefsListForReading)
                if (mb.buttonVisible && (HospitalityFamily.IsTakeoverDef(mb) || HospitalityFamily.IsTakeoverType(mb.tabWindowClass)))
                { mb.buttonVisible = false; HLog.Info("Compat", "Main tab " + mb.defName + " was shown again by its mod; hidden"); }
        }

        static readonly HashSet<MethodBase> silenced = new HashSet<MethodBase>();
        public static bool IsSilenced(MethodBase m) => silenced.Contains(m) || (m is MethodInfo mi && mi.DeclaringType?.GetMethod(mi.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null) is MethodInfo d && silenced.Contains(d));

        static void SilenceComponents()
        {
            var skipVoid = new HarmonyMethod(typeof(HospitalityTakeover), nameof(SkipPrefix)) { priority = Priority.First };
            var skipAlert = new HarmonyMethod(typeof(HospitalityTakeover), nameof(SkipAlertPrefix)) { priority = Priority.First };
            foreach (var asm in HospitalityFamily.TakeoverAssemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    bool comp = typeof(MapComponent).IsAssignableFrom(t) || typeof(GameComponent).IsAssignableFrom(t) || typeof(RimWorld.Planet.WorldComponent).IsAssignableFrom(t);
                    if (comp)
                    {
                        foreach (var name in ComponentLoopMethods)
                        {
                                            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                            // only methods the family itself declares (inherited vanilla no-ops stay as they are)
                            if (m == null || m.IsAbstract || !HospitalityFamily.IsTakeoverType(m.DeclaringType)) continue;
                            if (TryPatch(m, skipVoid)) MethodsSilenced++;
                        }
                    }
                    else if (typeof(Alert).IsAssignableFrom(t) && !t.IsAbstract)
                    {
                        var m = t.GetMethod("GetReport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        if (m != null && !m.IsAbstract && HospitalityFamily.IsTakeoverType(m.DeclaringType) && TryPatch(m, skipAlert)) MethodsSilenced++;
                    }
                }
            }
            // known family methods that undo the takeover from inside their components (by name only)
            foreach (var (typeName, method) in KnownUndoers)
            {
                var t = GenTypes.GetTypeInAnyAssembly(typeName);
                if (t == null || !HospitalityFamily.IsTakeoverType(t)) continue;
                var m = t.GetMethod(method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null && m.ReturnType == typeof(void) && TryPatch(m, skipVoid)) MethodsSilenced++;
            }
            Note($"Components: {MethodsSilenced} tick/update/alert methods of Hospitality family components silenced");
        }

        static bool TryPatch(MethodInfo m, HarmonyMethod prefix)
        {
            // Harmony wants the method object of the declaring type, not the one reflected through a subclass
            var declared = m.DeclaringType?.GetMethod(m.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (declared != null) m = declared;
            if (silenced.Contains(m)) return false;
            try { HearthMod.Harmony.Patch(m, prefix: prefix); silenced.Add(m); return true; }
            catch (Exception e) { HLog.Warn("Compat", $"Could not silence {m.DeclaringType?.FullName}.{m.Name}: {e.Message}"); return false; }
        }

        // Applied only to family component/alert methods and only while the takeover is active. Kept trivially cheap.
        static bool SkipPrefix() => !Active;

        static bool SkipAlertPrefix(ref AlertReport __result)
        {
            if (!Active) return true;
            __result = AlertReport.Inactive;
            return false;
        }

        public static IEnumerable<string> Diagnosis()
        {
            if (!HospitalityFamily.Detected.Any()) { yield return "Hospitality family: none active"; yield break; }
            yield return "Hospitality family: " + string.Join(", ", HospitalityFamily.Detected.Select(d => d.Label + " [" + d.Entry.Role + "]"));
            yield return "Takeover: " + (Active ? "active" : "not active");
            foreach (var r in Report) yield return "  " + r;
            foreach (var f in StepFailures) yield return "  FAILED " + f;
        }
    }
}
