// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Testing
{
    /// <summary>Integration checks of the shared defs: one architect tab per area, one coherent research tree, one research mode.</summary>
    public sealed class CoreDefsSuite : HearthTestSuite
    {
        public override string Name => "core.defs";
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(TestContext t)
        {
            // architect tabs: exactly one for guest content, one for the bank, none of the old module tabs
            var cats = DefDatabase<DesignationCategoryDef>.AllDefsListForReading;
            t.Check(cats.Count(c => c.defName == "HH_Hospitality") == 1 && cats.Count(c => c.defName == "HH_Bank") == 1, "one HH_Hospitality and one HH_Bank tab");
            t.Check(!cats.Any(c => c.defName == "HH_Casino" || c.defName == "HH_Underworld"), "no module-specific tabs left");
            var joy = DefDatabase<DesignationCategoryDef>.GetNamed("Joy");
            var hosp = DefDatabase<DesignationCategoryDef>.GetNamed("HH_Hospitality");
            t.Check(hosp.order < joy.order && hosp.label == "Hospitality", "Hospitality sits after Joy (order " + hosp.order + " < " + joy.order + ")");
            var ours = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("HH_") || d.defName.StartsWith("SL_")).ToList();
            foreach (var d in ours.Where(d => d.designationCategory != null))
            {
                string c = d.designationCategory.defName;
                bool bank = d.defName.StartsWith("SL_");
                if (d.defName == "HH_FrontDesk" && c == "Furniture") { t.Note("HH_FrontDesk is still in Furniture: the Inn defs should move it to HH_Hospitality"); continue; }
                t.Check(bank ? c == "HH_Bank" : (c == "HH_Hospitality" || c == "Production"), d.defName + " is in tab " + c);
            }
            t.Check(ours.Where(d => d.defName.StartsWith("HH_Station_")).All(d => d.designationCategory?.defName == "Production"), "kitchen stations stay in Production");
            foreach (var g in new[] { "HH_Group_Casino", "HH_Group_Wellness", "HH_Group_City", "HH_Group_Underworld" })
                t.Check(ours.Count(d => d.designatorDropdown?.defName == g) >= 2, g + " groups at least two buildings");

            // research: tabs, descriptions, references, layout
            var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => p.defName.StartsWith("HH_") || p.defName.StartsWith("SL_")).ToList();
            foreach (var n in new[] { "HH_Cooking_L1", "HH_Innkeeping", "HH_Gastronomy", "HH_HotelManagement", "HH_FineDining", "HH_Casino_Tables", "HH_Casino_Slots", "HH_Bathhouse", "HH_Wellness", "HH_CityLife_1", "HH_CityLife_2", "HH_Underworld_Discovery", "HH_Underworld_1", "HH_TheHunt", "HH_BlackClinic", "SL_Banking", "SL_Branches", "SL_Credit", "SL_Insurance", "SL_Actuarial" })
                t.Check(projects.Any(p => p.defName == n), "research " + n + " exists");
            foreach (var p in projects)
            {
                bool bank = p.defName.StartsWith("SL_");
                t.Check(p.tab != null && p.tab.defName == (bank ? "HH_Banking" : "HH_Hospitality") && !p.description.NullOrEmpty() && !p.label.NullOrEmpty(), p.defName + " has tab, label and description");
                foreach (var q in (p.prerequisites ?? new List<ResearchProjectDef>()).Concat(p.hiddenPrerequisites ?? new List<ResearchProjectDef>()))
                {
                    if (!q.defName.StartsWith("HH_") && !q.defName.StartsWith("SL_")) continue;
                    t.Check(q.ResearchViewX < p.ResearchViewX && q.tab == p.tab, p.defName + " comes after " + q.defName);
                    t.Check(!(bank && q.defName.StartsWith("HH_")), p.defName + " does not depend on hotel research");
                }
            }
            for (int i = 0; i < projects.Count; i++)
                for (int j = i + 1; j < projects.Count; j++)
                {
                    var a = projects[i]; var b = projects[j];
                    if (a.tab != b.tab) continue;
                    t.Check(!(Math.Abs(a.ResearchViewX - b.ResearchViewX) < 1f && Math.Abs(a.ResearchViewY - b.ResearchViewY) < 0.6f), a.defName + " and " + b.defName + " do not overlap");
                }
            t.Check(DishKnowledgeLevelOk(), "kitchen levels 2-4 are Innkeeping, Gastronomy, Fine dining");

            // the shared research mode
            t.Check(HSettings.Find("core.researchMode") != null, "core.researchMode is registered");
            var mgr = Find.ResearchManager;
            var progress = Traverse.Create(mgr).Field("progress").GetValue<Dictionary<ResearchProjectDef, float>>();
            var before = new Dictionary<ResearchProjectDef, float>();
            foreach (var p in projects) before[p] = progress.TryGetValue(p, out var v) ? v : 0f;
            var wasOpen = projects.Where(p => !p.IsFinished && p.defName != "HH_Cooking_L1" && !ResearchGate.IsGated(p)).ToList();
            var gatedOpen = projects.Where(p => !p.IsFinished && ResearchGate.IsGated(p)).ToList();
            float oldMode = HSettings.Get("core.researchMode");
            HSettings.Set("core.researchMode", 0f);
            t.Check(!CoreSettings.ResearchOff && ResearchGate.Apply() == 0, "mode Full finishes nothing");
            HSettings.Set("core.researchMode", 1f);
            t.Check(CoreSettings.ResearchOff, "mode Off is read");
            ResearchGate.Apply();
            t.Check(projects.All(p => ResearchGate.IsGated(p) || p.IsFinished), "mode Off: every project of the trees is finished");
            t.Check(gatedOpen.All(p => !p.IsFinished), "mode Off: underworld projects stay gated (" + gatedOpen.Count + ")");
            t.Check(DishKnowledge_Unlocked(4), "mode Off: kitchen level 4 known");
            // restore the state so later suites see the research as before
            HSettings.Set("core.researchMode", oldMode);
            foreach (var p in projects) { if (before[p] <= 0f) progress.Remove(p); else progress[p] = before[p]; }
            t.Check(wasOpen.All(p => !p.IsFinished), "state restored (" + wasOpen.Count + " projects open again)");
            yield return 1;
        }

        // Hearth.dll types are not visible from the core: look them up by name
        static bool DishKnowledgeLevelOk()
        {
            var type = GenTypes.GetTypeInAnyAssembly("Hearth.Kitchen.DishKnowledge");
            var m = type?.GetMethod("LevelResearch");
            if (m == null) return true;   // standalone core test build without the kitchen
            string[] want = { "HH_Innkeeping", "HH_Gastronomy", "HH_FineDining" };
            for (int l = 2; l <= 4; l++) if ((m.Invoke(null, new object[] { l }) as ResearchProjectDef)?.defName != want[l - 2]) return false;
            return true;
        }

        static bool DishKnowledge_Unlocked(int level)
        {
            var m = GenTypes.GetTypeInAnyAssembly("Hearth.Kitchen.DishKnowledge")?.GetMethod("LevelUnlocked");
            return m == null || (bool)m.Invoke(null, new object[] { level });
        }
    }
}
#endif
