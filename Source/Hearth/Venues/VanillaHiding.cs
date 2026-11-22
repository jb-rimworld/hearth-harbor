// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// Replaced vanilla furniture leaves the architect menu (6.1, N49). The def stays in the game and buildings already placed stay;
    /// only its designator is taken out of the category's list, and put back when the replacement is not available (researched),
    /// the setting is off or the module is off. Other modules add pairs in XML (VanillaReplacementDef) or with <see cref="Register"/>.
    /// </summary>
    public static class VanillaHiding
    {
        sealed class Hidden { public DesignationCategoryDef Category; public Designator Removed; public Designator_Dropdown Dropdown; public int Index; }
        static readonly Dictionary<ThingDef, Hidden> hidden = new Dictionary<ThingDef, Hidden>();
        static readonly List<KeyValuePair<string, string>> runtimePairs = new List<KeyValuePair<string, string>>();
        static readonly System.Reflection.FieldInfo dropdownElements = AccessTools.Field(typeof(Designator_Dropdown), "elements");
        static bool loggedOnce;

        /// <summary>Add a pair in code (for example from a module's OnDefsLoaded). Safe to call twice.</summary>
        public static void Register(string vanillaDefName, string replacementDefName)
        {
            runtimePairs.RemoveAll(p => p.Key == vanillaDefName);
            runtimePairs.Add(new KeyValuePair<string, string>(vanillaDefName, replacementDefName));
        }

        public static void Unregister(string vanillaDefName) => runtimePairs.RemoveAll(p => p.Key == vanillaDefName);

        public static IEnumerable<KeyValuePair<ThingDef, ThingDef>> Pairs()
        {
            var names = new List<KeyValuePair<string, string>>(runtimePairs);
            foreach (var d in DefDatabase<VanillaReplacementDef>.AllDefs) names.Add(new KeyValuePair<string, string>(d.vanillaDefName, d.replacementDefName));
            foreach (var kv in names)
            {
                var v = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                var r = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Value);
                if (v == null || r == null) continue;
                yield return new KeyValuePair<ThingDef, ThingDef>(v, r);
            }
        }

        public static bool IsHidden(ThingDef vanilla) => hidden.ContainsKey(vanilla);

        static bool ReplacementAvailable(ThingDef repl)
        {
            if (repl.designationCategory == null) return false;
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.ResearchManager == null)
                return repl.researchPrerequisites.NullOrEmpty();
            return repl.IsResearchFinished;
        }

        /// <summary>Bring the menu in line with setting, module and research state. Cheap; called at start, on load, after research and every 2500 ticks.</summary>
        public static void Refresh()
        {
            bool allowed = Modules.On(ModuleId.ShopsFood) && VS.B(VS.HideVanilla);
            var wanted = new HashSet<ThingDef>();
            foreach (var kv in Pairs())
                if (allowed && ReplacementAvailable(kv.Value)) wanted.Add(kv.Key);
            foreach (var d in wanted) if (!hidden.ContainsKey(d)) Hide(d);
            foreach (var d in hidden.Keys.ToList()) if (!wanted.Contains(d)) Show(d);
        }

        static void Hide(ThingDef def)
        {
            var cat = def.designationCategory;
            if (cat == null) return;
            var list = cat.AllResolvedDesignators;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d is Designator_Build b && b.PlacingDef == def)
                {
                    list.RemoveAt(i);
                    hidden[def] = new Hidden { Category = cat, Removed = d, Index = i };
                    HLog.Info("Venues", "Hidden from architect menu: " + def.defName);
                    return;
                }
                if (d is Designator_Dropdown dd && dropdownElements != null && dropdownElements.GetValue(dd) is List<Designator> elems)
                {
                    int j = elems.FindIndex(e => e is Designator_Build eb && eb.PlacingDef == def);
                    if (j >= 0)
                    {
                        hidden[def] = new Hidden { Category = cat, Removed = elems[j], Dropdown = dd, Index = j };
                        elems.RemoveAt(j);
                        HLog.Info("Venues", "Hidden from architect menu (dropdown): " + def.defName);
                        return;
                    }
                }
            }
            if (!loggedOnce) { loggedOnce = true; HLog.Warn("Venues", "Could not find a designator to hide for " + def.defName + " (not resolved yet or not buildable)"); }
        }

        static void Show(ThingDef def)
        {
            if (!hidden.TryGetValue(def, out var h)) return;
            hidden.Remove(def);
            if (h.Dropdown != null && dropdownElements?.GetValue(h.Dropdown) is List<Designator> elems)
                elems.Insert(Math.Min(h.Index, elems.Count), h.Removed);
            else
            {
                var list = h.Category.AllResolvedDesignators;
                list.Insert(Math.Min(h.Index, list.Count), h.Removed);
            }
            HLog.Info("Venues", "Back in the architect menu: " + def.defName);
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    static class Patch_ResearchFinished
    {
        static void Postfix()
        {
            if (!Modules.On(ModuleId.ShopsFood)) return;
            Guard.Run(ModuleId.ShopsFood, "VanillaHiding.Refresh(research)", VanillaHiding.Refresh);
        }
    }
}
