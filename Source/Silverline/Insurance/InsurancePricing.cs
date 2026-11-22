// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>One price quote: every factor of the premium formula (K5) so the UI can show the maths.</summary>
    public sealed class Quote
    {
        public double expectedDamage, payoutRatio, loading, noClaims = 1, bundle = 1, riders = 1, discount;
        public double premiumQuadrum, billing, travelPerHeadDay;
        public int billingDays;
        public bool perTravelDay;
        public List<string> lines = new List<string>();
    }

    /// <summary>Prices, availability and sum limits of the policies (game side; the maths is in Hearth.Insurance.InsuranceMath).</summary>
    public static class InsurancePricing
    {
        public static IEnumerable<SL_PolicyDef> AllPolicies => DefDatabase<SL_PolicyDef>.AllDefsListForReading.OrderBy(p => p.order).ThenBy(p => p.defName);

        static bool ResearchDone(string defName)
        {
            if (!HSettings.GetBool(InsSettings.RequireResearch)) return true;
            var r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            return r == null || r.IsFinished;   // research def not present (standalone/early build): do not lock the player out
        }

        public static bool BaseUnlocked => ResearchDone("SL_Insurance");
        public static bool ActuarialUnlocked => ResearchDone("SL_Actuarial");

        public static bool TierUnlocked(SL_PolicyDef def, CoverTier tier, out string reason)
        {
            reason = null;
            if (def.needsActuarial || tier == CoverTier.Premium)
            {
                if (!ActuarialUnlocked) { reason = Ins.T("Locked.Actuarial"); return false; }
            }
            if (!BaseUnlocked) { reason = Ins.T("Locked.Insurance"); return false; }
            return true;
        }

        // ---- sums ----------------------------------------------------------------------------------------------------------------
        public static double RealValue(SL_PolicyDef def, Map map, Pawn pawn)
        {
            if (def.kind == PolicyKind.Colony) return def.sumFollowsWealth && map != null ? Math.Max(1.0, map.wealthWatcher.WealthBuildings) : Math.Max(def.defaultSum, 1);
            if (pawn != null) return Math.Max(1.0, pawn.MarketValue);
            return def.defaultSum;
        }

        public static void SumRange(SL_PolicyDef def, Map map, Pawn pawn, out double min, out double max)
        {
            min = def.minSum;
            if (def.kind == PolicyKind.Colony)
            {
                max = Math.Min(def.maxSum, HSettings.Get(InsSettings.MaxPropertySum));
                if (def.sumFollowsWealth && map != null) max = Math.Min(max, Math.Max(def.minSum, RealValue(def, map, pawn)));
            }
            else
            {
                double cap = HSettings.Get(InsSettings.MaxPersonSum);
                max = Math.Min(Math.Min(def.maxSum, cap), InsuranceMath.MaxPersonalSum(pawn?.MarketValue ?? def.maxSum));
                max = Math.Max(max, def.minSum);
            }
            if (max < min) min = max;
        }

        public static double SuggestSum(SL_PolicyDef def, Map map, Pawn pawn)
        {
            SumRange(def, map, pawn, out var min, out var max);
            double s = def.kind == PolicyKind.Colony && def.sumFollowsWealth ? max : Math.Min(max, Math.Max(min, def.defaultSum));
            double r = Math.Round(Math.Max(min, Math.Min(max, s)) / 50.0) * 50.0;
            return Math.Max(min, Math.Min(max, r));
        }

        // ---- risk ----------------------------------------------------------------------------------------------------------------
        public static double DangerMult(Map map)
        {
            if (map?.dangerWatcher == null) return 1.0;
            switch (map.dangerWatcher.DangerRating)
            {
                case StoryDanger.None: return 0.8;
                case StoryDanger.Low: return 1.0;
                default: return 1.5;
            }
        }

        public static double RiderShare(SL_PolicyDef def, IEnumerable<string> riders)
        {
            double mult = InsSettings.Tier >= 5.5f ? 1.5 : InsSettings.Tier >= 3.5f ? 1.2 : 1.0;
            double s = 0;
            foreach (var key in riders ?? Enumerable.Empty<string>())
            {
                var r = def.riders?.FirstOrDefault(x => x.key == key);
                if (r != null) s += r.premiumShare * mult;
            }
            return s;
        }

        public static double ExpectedDamage(SL_PolicyDef def, Map map, Pawn pawn, double sum, double hazard, double history)
        {
            switch (def.riskModel)
            {
                case RiskModel.PerColonist:
                    return (map?.mapPawns?.FreeColonistsCount ?? 3) * def.baseRisk;
                case RiskModel.Flat:
                    return def.baseRisk;
                case RiskModel.PerTravelDay:
                    return def.baseRisk * InsuranceMath.DaysPerQuadrum;   // one head travelling the whole quadrum
                default:
                    if (def.ageSensitive && pawn != null) { float age = pawn.ageTracker.AgeBiologicalYears; hazard *= age >= 60 ? 1.0 : age >= 50 ? 0.6 : 0.25; }
                    return InsuranceMath.ExpectedDamage(sum, def.baseRisk, hazard, def.kind == PolicyKind.Colony ? DangerMult(map) : 1.0, history);
            }
        }

        /// <summary>Insurance desks of the colony (a building of Clawback Mutual): each shortens every review by one day (B.1).</summary>
        public static int DeskCount()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("SL_InsuranceDesk"); if (def == null) return 0;
            int n = 0; foreach (var m in Find.Maps) if (m.IsPlayerHome) n += m.listerBuildings.AllBuildingsColonistOfDef(def).Count();
            return Math.Min(1, n);
        }

        public static int ActiveCount => InsuranceComponent.Instance?.contracts.Count(c => c.Active) ?? 0;

        /// <summary>Quote for a new or existing policy. bundleCount = number of active policies including this one.</summary>
        public static Quote QuoteFor(SL_PolicyDef def, CoverTier tier, Pawn pawn, double sum, IEnumerable<string> riders, int bundleCount, PolicyContract existing = null)
        {
            var map = InsurerBooks.HomeMap;
            var q = new Quote { billingDays = InsSettings.Billing };
            double hazard = existing != null ? InsuranceMath.HazardFactor(existing.lastShareB, existing.lastShareC) : 1.0;
            double hist = existing?.HistoryPerQuadrum ?? 0.0;
            q.expectedDamage = ExpectedDamage(def, map, pawn, sum, hazard, hist);
            q.payoutRatio = InsuranceMath.ExpectedPayoutRatio(tier, InsSettings.Attitude);
            q.loading = InsSettings.LoadingValue;
            q.noClaims = InsuranceMath.NoClaimsFactor(existing?.noClaimsClass ?? 0);
            q.bundle = InsuranceMath.BundleFactor(bundleCount);
            q.riders = 1.0 + RiderShare(def, riders);
            q.discount = existing != null && existing.discountUntilTick > Ins.Now ? existing.discountShare : 0.0;
            double basePrem = InsuranceMath.Premium(q.expectedDamage, q.payoutRatio, q.loading, q.noClaims, q.bundle);
            q.premiumQuadrum = basePrem * q.riders * (1.0 - q.discount);
            q.billing = InsuranceMath.BillingAmount(q.premiumQuadrum, q.billingDays);
            if (def.riskModel == RiskModel.PerTravelDay)
            {
                q.perTravelDay = true;
                q.travelPerHeadDay = q.premiumQuadrum / InsuranceMath.DaysPerQuadrum;
                q.billing = 0; // billed by actual travel days
            }
            q.lines.Add(Ins.T("Quote.Damage", Ins.Silver(q.expectedDamage)));
            q.lines.Add(Ins.T("Quote.Payout", Ins.Pct(q.payoutRatio)));
            q.lines.Add(Ins.T("Quote.Loading", Ins.Pct(q.loading)));
            if (q.noClaims < 0.9999) q.lines.Add(Ins.T("Quote.NoClaims", Ins.Pct(1 - q.noClaims)));
            if (q.bundle < 0.9999) q.lines.Add(Ins.T("Quote.Bundle", Ins.Pct(1 - q.bundle)));
            if (q.riders > 1.0001) q.lines.Add(Ins.T("Quote.Riders", Ins.Pct(q.riders - 1)));
            if (q.discount > 0.0001) q.lines.Add(Ins.T("Quote.Discount", Ins.Pct(q.discount)));
            return q;
        }

        public static Quote QuoteFor(PolicyContract c)
        {
            var comp = InsuranceComponent.Instance;
            int count = Math.Max(1, comp?.contracts.Count(x => x.Active) ?? 1);
            return QuoteFor(c.Def, c.tier, c.holder, c.sum, c.riders, count, c);
        }

        /// <summary>Bundle count a new policy would join: all active policies plus the new one.</summary>
        public static int BundleCountForNew => ActiveCount + 1;

        // ---- hazard (8.13) ------------------------------------------------------------------------------------------------------
        public static HazardGroup HazardOf(Pawn p)
        {
            if (p == null || p.Dead) return HazardGroup.A;
            if (p.Drafted) return HazardGroup.C;
            var job = p.CurJob?.def;
            if (job == null) return HazardGroup.A;
            if (job == JobDefOf.Mine || job == JobDefOf.Hunt || job == JobDefOf.FinishFrame || job == JobDefOf.Deconstruct || job == JobDefOf.BuildRoof || job == JobDefOf.RemoveFloor || job == JobDefOf.Repair)
                return HazardGroup.B;
            string n = job.defName;
            if (n == "Mine" || n == "Hunt" || n == "FinishFrame" || n == "ConstructFinishFrame") return HazardGroup.B;
            return HazardGroup.A;
        }
    }
}
