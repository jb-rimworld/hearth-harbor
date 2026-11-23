// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Story
{
    /// <summary>One incident with the chance that it stays in the pool when the comp picks (cheap weighting without touching IncidentChanceFinal).</summary>
    public class HHIncidentChance
    {
        public string incident;
        public float chance = 1f;
    }

    /// <summary>Shared filter of Hetty's comps: incidents that never fire, and incidents that only stay in the pool with a chance.
    /// Defs are named by string, so DLC incidents can be listed without MayRequire and missing ones are simply skipped.</summary>
    internal static class IncidentFilter
    {
        public static List<IncidentDef> Apply(IEnumerable<IncidentDef> pool, List<string> neverFire, List<HHIncidentChance> keepChance)
        {
            var list = new List<IncidentDef>();
            foreach (var d in pool)
            {
                if (d == null) continue;
                if (neverFire != null && neverFire.Contains(d.defName)) continue;
                if (keepChance != null)
                {
                    bool drop = false;
                    for (int i = 0; i < keepChance.Count; i++)
                        if (keepChance[i].incident == d.defName) { drop = !Rand.Chance(keepChance[i].chance); break; }
                    if (drop) continue;
                }
                list.Add(d);
            }
            return list;   // materialised: the callers weigh the list twice (sum, then pick), a lazy random filter would differ between the passes
        }
    }

    // ------------------------------------------------------------------------------------------------ threats
    public class StorytellerCompProperties_HH_GentleCycle : StorytellerCompProperties_OnOffCycle
    {
        public List<string> neverFire = new List<string>();
        public List<HHIncidentChance> keepChance = new List<HHIncidentChance>();
        public StorytellerCompProperties_HH_GentleCycle() { compClass = typeof(StorytellerComp_HH_GentleCycle); }
    }

    /// <summary>On/off threat cycle with gentle picks: mech clusters and ship parts never come, infestations and manhunter packs rarely.</summary>
    public class StorytellerComp_HH_GentleCycle : StorytellerComp_OnOffCycle
    {
        StorytellerCompProperties_HH_GentleCycle P => (StorytellerCompProperties_HH_GentleCycle)props;

        protected override IEnumerable<IncidentDef> UsableIncidentsInCategory(IncidentCategoryDef cat, System.Func<IncidentDef, IncidentParms> parmsGetter)
            => IncidentFilter.Apply(base.UsableIncidentsInCategory(cat, parmsGetter), P.neverFire, P.keepChance);

#if HH_TESTS
        /// <summary>Tests: the pool this comp would pick from right now.</summary>
        public List<IncidentDef> PoolForTests(IncidentCategoryDef cat, IIncidentTarget target)
        {
            var parms = GenerateParms(cat, target);
            return UsableIncidentsInCategory(cat, _ => parms).ToList();
        }
#endif
    }

    // ------------------------------------------------------------------------------------------------ misc events
    public class StorytellerCompProperties_HH_KindMisc : StorytellerCompProperties_CategoryMTB
    {
        public float positiveFactor = 3f, neutralFactor = 1f, negativeFactor = 0.2f;
        /// <summary>Factor for Hearth &amp; Harbor's own incidents in the category (noble stays …).</summary>
        public float hhFactor = 2f;
        public List<string> positive = new List<string>();
        public List<string> neutral = new List<string>();
        public List<string> negative = new List<string>();
        public List<string> neverFire = new List<string>();
        public StorytellerCompProperties_HH_KindMisc() { compClass = typeof(StorytellerComp_HH_KindMisc); }
    }

    public enum IncidentMood { Positive, Neutral, Negative, Ours }

    /// <summary>
    /// Misc events with a kind hand: the same timer as the vanilla CategoryMTB comp, but good news is weighted up (×3), bad luck down (×0.2)
    /// and the cruellest events never come. The vanilla population logic (wanderers when the colony is small) is kept.
    /// </summary>
    public class StorytellerComp_HH_KindMisc : StorytellerComp_CategoryMTB
    {
        StorytellerCompProperties_HH_KindMisc P => (StorytellerCompProperties_HH_KindMisc)props;

        public IncidentMood MoodOf(IncidentDef d)
        {
            var p = P;
            if (p.positive.Contains(d.defName)) return IncidentMood.Positive;
            if (p.negative.Contains(d.defName)) return IncidentMood.Negative;
            if (p.neutral.Contains(d.defName)) return IncidentMood.Neutral;
            if (d.defName.StartsWith("HH_")) return IncidentMood.Ours;
            var l = d.letterDef;
            if (l == null || l == LetterDefOf.NeutralEvent) return IncidentMood.Neutral;
            if (l == LetterDefOf.PositiveEvent) return IncidentMood.Positive;
            return IncidentMood.Negative;   // threat and negative letters of other mods' incidents
        }

        public float FactorOf(IncidentDef d)
        {
            switch (MoodOf(d))
            {
                case IncidentMood.Positive: return P.positiveFactor;
                case IncidentMood.Negative: return P.negativeFactor;
                case IncidentMood.Ours: return P.hhFactor;
                default: return P.neutralFactor;
            }
        }

        protected override IEnumerable<IncidentDef> UsableIncidentsInCategory(IncidentCategoryDef cat, System.Func<IncidentDef, IncidentParms> parmsGetter)
            => IncidentFilter.Apply(base.UsableIncidentsInCategory(cat, parmsGetter), P.neverFire, null);

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            var p = P;
            float mtb = p.mtbDays;
            if (p.mtbDaysFactorByDaysPassedCurve != null) mtb *= p.mtbDaysFactorByDaysPassedCurve.Evaluate(GenDate.DaysPassedSinceSettleFloat);
            if (!Rand.MTBEventOccurs(mtb, 60000f, 1000f)) yield break;
            var parms = GenerateParms(p.category, target);
            var def = Pick(UsableIncidentsInCategory(p.category, parms).ToList(), target);
            if (def != null) yield return new FiringIncident(def, this, parms);
        }

        /// <summary>Weighted pick: vanilla chance × mood factor, with vanilla's population preference first (TrySelectRandomIncident).</summary>
        public IncidentDef Pick(List<IncidentDef> pool, IIncidentTarget target)
        {
            if (pool.Count == 0) return null;
            IncidentDef found = null;
            if (Rand.Chance(IncreasesPopChanceByPopIntentCurve.Evaluate(StorytellerUtilityPopulation.PopulationIntent)))
                found = pool.Where(i => i.populationEffect != IncidentPopulationEffect.None).RandomElementByWeightWithFallback(i => IncidentChanceFinal(i, target) * FactorOf(i));
            else
                found = pool.Where(i => i.populationEffect == IncidentPopulationEffect.None).RandomElementByWeightWithFallback(i => IncidentChanceFinal(i, target) * FactorOf(i));
            return found ?? pool.RandomElementByWeightWithFallback(i => IncidentChanceFinal(i, target) * FactorOf(i));
        }

#if HH_TESTS
        public List<IncidentDef> PoolForTests(IIncidentTarget target)
        {
            var parms = GenerateParms(P.category, target);
            return UsableIncidentsInCategory(P.category, _ => parms).ToList();
        }
#endif
    }
}
