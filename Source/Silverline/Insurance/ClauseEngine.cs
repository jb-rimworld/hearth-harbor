// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using Verse;

namespace Silverline
{
    public sealed class ClauseResult
    {
        public readonly List<SL_ClauseDef> applied = new List<SL_ClauseDef>();
        public SL_ClauseDef exclusion;
        public bool grey;
        public int extraDays;
        public SL_ClauseDef Cited => exclusion ?? applied.FirstOrDefault(c => c.greyZone && (!c.greyOnlyHard || InsSettings.Hard)) ?? applied.FirstOrDefault();
    }

    /// <summary>The fifteen clauses of the fine print (B.7): decides which ones apply to a claim.</summary>
    public static class ClauseEngine
    {
        public static IEnumerable<SL_ClauseDef> All => DefDatabase<SL_ClauseDef>.AllDefsListForReading.OrderBy(c => c.defName);

        public static SL_ClauseDef ByTrigger(ClauseTrigger t) => DefDatabase<SL_ClauseDef>.AllDefsListForReading.FirstOrDefault(c => c.trigger == t);

        public static bool Active(SL_ClauseDef c, CoverTier tier)
        {
            var scope = InsSettings.Hard ? c.hardScope : c.standardScope;
            if (scope == ClauseScope.Off) return false;
            if (scope == ClauseScope.BareOnly && tier != CoverTier.BareMinimum) return false;
            return true;
        }

        public static ClauseResult Evaluate(Claim claim)
        {
            var r = new ClauseResult();
            foreach (var c in All)
            {
                if (!Active(c, claim.tier)) continue;
                bool hit;
                switch (c.trigger)
                {
                    case ClauseTrigger.Waiting: hit = claim.Hint("Waiting"); break;
                    case ClauseTrigger.NotAccident: hit = claim.Hint("Illness"); break;
                    case ClauseTrigger.Weather: hit = claim.Hint("Weather"); break;
                    case ClauseTrigger.Siege: hit = claim.Hint("Siege"); break;
                    case ClauseTrigger.PreExisting: hit = claim.preGrade > 0.01 || (InsSettings.Hard && claim.contributed); break;
                    case ClauseTrigger.SelfInflicted: hit = claim.Hint("Self"); break;
                    case ClauseTrigger.BrokenDoor: hit = claim.Hint("Door"); break;
                    case ClauseTrigger.Review: hit = claim.trigger != ClaimTrigger.PawnDeath && claim.trigger != ClaimTrigger.TravelDeath; break;
                    case ClauseTrigger.Bionics: hit = claim.Hint("Bionic"); break;
                    case ClauseTrigger.DeathBeforeAssessment: hit = claim.Hint("DeathAssess"); break;
                    case ClauseTrigger.Regrowth: hit = claim.Hint("Regrowth"); break;
                    default: hit = false; break;
                }
                if (!hit) continue;
                r.applied.Add(c);
                if (c.exclusion && r.exclusion == null) r.exclusion = c;
                if (c.greyZone && (!c.greyOnlyHard || InsSettings.Hard)) r.grey = true;
                r.extraDays += c.extraReviewDays;
            }
            return r;
        }

        /// <summary>A clause to cite when the claims department simply does not feel like paying: chosen by what was lost.</summary>
        public static SL_ClauseDef Flavour(Claim claim)
        {
            ClauseTrigger t;
            switch (claim.trigger)
            {
                case ClaimTrigger.RaidBuilding: case ClaimTrigger.FireBuilding: case ClaimTrigger.VehicleBuilding: case ClaimTrigger.AnomalyBuilding: t = ClauseTrigger.BrokenDoor; break;
                case ClaimTrigger.LimbLoss: case ClaimTrigger.TravelLimb: t = ClauseTrigger.PreExisting; break;
                case ClaimTrigger.Illness: t = ClauseTrigger.NotAccident; break;
                case ClaimTrigger.Crop: t = ClauseTrigger.Weather; break;
                default: t = ClauseTrigger.Review; break;
            }
            return ByTrigger(t) ?? ByTrigger(ClauseTrigger.Review);
        }
    }
}
