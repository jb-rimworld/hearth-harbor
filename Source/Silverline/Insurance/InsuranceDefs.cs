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
    /// <summary>What a policy insures. Colony policies cover the colony as a whole, Person policies one colonist, Travel policies caravan members.</summary>
    public enum PolicyKind { Colony, Person, Travel }

    /// <summary>How the expected damage per quadrum of a policy is estimated (K5).</summary>
    public enum RiskModel
    {
        SumRate,       // baseRisk = share of the sum insured per quadrum
        PerColonist,   // baseRisk = silver per colonist per quadrum
        PerTravelDay,  // baseRisk = silver per caravan member per travel day
        Flat           // baseRisk = silver per quadrum
    }

    /// <summary>The narrow hooks through which damage reaches Clawback Mutual (8.13: "schmale Haken").</summary>
    public enum ClaimTrigger
    {
        None = 0,
        RaidBuilding,       // own building destroyed by hostiles
        FireBuilding,       // own building destroyed by fire/explosion/lightning without an enemy hand
        VehicleBuilding,    // shuttles, drop pods, vehicles
        AnomalyBuilding,    // building destroyed by an anomaly entity
        PawnDeath,          // colonist died
        LimbLoss,           // colonist lost body parts
        Injury,             // injury (caravan: every scratch)
        Illness,            // illness / bed rest of a colonist
        Fine,               // penalties (Legal Shield)
        Liability,          // damage to guests and traders' goods
        Crop,               // blight, cold snap, toxic fallout, animal disease, predators
        Closure,            // loss of income after a closure
        Retirement,         // colonist aged 60 or more
        TravelDeath,
        TravelInjury,      // every scratch (Premium)
        TravelLimb,        // lost body part on a caravan
    }

    /// <summary>One optional add-on ("Zusatz") of a policy. Pre-ticked by the sales pressure (N51).</summary>
    public sealed class PolicyRider
    {
        public string key;
        public string label;
        public string description;
        /// <summary>Extra premium as a share of the base premium (e.g. 0.12 = +12 %).</summary>
        public float premiumShare = 0.1f;
        /// <summary>Effect keys understood by the claims engine: plusProgression, annuity, deathSum, hazardous, hospitalDaily, newValue.</summary>
        public string effect;
        public string LabelCap => label.CapitalizeFirst();
    }

    /// <summary>A policy of Clawback Mutual (twelve in total, 8.13). All numbers are starting values.</summary>
    public sealed class SL_PolicyDef : Def
    {
        public PolicyKind kind = PolicyKind.Colony;
        public RiskModel riskModel = RiskModel.SumRate;
        public float baseRisk = 0.02f;
        public int minSum = 100, maxSum = 25000, defaultSum = 1000;
        /// <summary>The sum insured of a Colony property policy must not exceed the real value of the covered things.</summary>
        public bool sumFollowsWealth;
        public List<ClaimTrigger> triggers = new List<ClaimTrigger>();
        public string slogan;
        public string coversText;
        public string benefitBare, benefitGood, benefitPremium;
        public List<PolicyRider> riders = new List<PolicyRider>();
        /// <summary>Wanderlust Travel Cover: all tiers need SL_Actuarial.</summary>
        public bool needsActuarial;
        public int order = 100;
        public string iconPath;
        /// <summary>Daily payment to retired colonists (Golden Years): silver per day at Good Enough, x1.5 Premium.</summary>
        public float dailyAnnuity;
        /// <summary>Lump sum of the Bare Minimum retirement/health plan, in silver.</summary>
        public float lumpSum;
        /// <summary>Flat benefit per bedridden day (Rim Health Plan): silver per day at Bare Minimum.</summary>
        public float dailyBenefit;
        /// <summary>Flat benefit per injury on a caravan at Premium (Wanderlust).</summary>
        public float perInjuryFlat;
        /// <summary>Fixed share of the loss that is paid per tier (Legal Shield 50/80/100 %); 0 = use the generic formula.</summary>
        public float shareBare, shareGood, sharePremium;

        public string BenefitFor(CoverTier t) => t == CoverTier.BareMinimum ? benefitBare : t == CoverTier.GoodEnough ? benefitGood : benefitPremium;
        public double ShareFor(CoverTier t)
        {
            float s = t == CoverTier.BareMinimum ? shareBare : t == CoverTier.GoodEnough ? shareGood : sharePremium;
            return s > 0 ? s : InsuranceMath.Replacement(t) * (1.0 - InsuranceMath.Deductible(t));
        }
        public bool HasTrigger(ClaimTrigger t) => triggers != null && triggers.Contains(t);
        public bool PerPerson => kind == PolicyKind.Person;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (triggers == null || triggers.Count == 0) yield return "no triggers";
            if (maxSum < minSum) yield return "maxSum < minSum";
        }
    }

    /// <summary>What a clause of Anhang B.7 does when it applies.</summary>
    public enum ClauseTrigger
    {
        Waiting,        // 1: waiting period
        NotAccident,    // 3.2: illness and food poisoning are not accidents
        Weather,        // 4: lightning, meteor, solar flare
        Siege,          // 7b: sieges and mechanoid clusters
        PreExisting,    // 9: pre-existing conditions
        SelfInflicted,  // 11: damage by your own people
        BrokenDoor,     // 14b: a wall with a door in it is a door
        Review,         // 17: one more day of review
        Bionics,        // 21: bionic parts are equipment
        Regrowth,       // 22: regrown parts are claimed back
        DeathBeforeAssessment, // 23
        RiskChange,     // 26: change in risk
        CancelForm,     // 30: form C-7
        AutoRenew,      // 31
        Conflict        // 40: Legal Shield does not help against Clawback
    }

    /// <summary>One of the fifteen clauses of the fine print (B.7). Moddable.</summary>
    public sealed class SL_ClauseDef : Def
    {
        public string number;           // "1", "3.2", "7b" …
        public string text;             // the clause as printed
        public ClauseTrigger trigger;
        /// <summary>Counts as a grey zone (denial more likely) when it applies.</summary>
        public bool greyZone;
        /// <summary>The clause excludes the claim outright when it applies (waiting period, self-inflicted).</summary>
        public bool exclusion;
        /// <summary>Extra review days the clause adds.</summary>
        public int extraReviewDays;
        /// <summary>On Standard Room (tier below 3.5): active for everybody / only on Bare Minimum / not at all.</summary>
        public ClauseScope standardScope = ClauseScope.All;
        /// <summary>From Overbooked on.</summary>
        public ClauseScope hardScope = ClauseScope.All;
        /// <summary>The clause only counts as a grey zone from Overbooked on.</summary>
        public bool greyOnlyHard;
        public string Ref => "§" + number;
        public string Title => "§" + number + " " + LabelCap;
        public string Print(bool abbreviated) => abbreviated ? "(see " + Ref + ")" : "“" + text + "” (" + Ref + ")";
    }

    public enum ClauseScope { Off, All, BareOnly }

    /// <summary>A line of the Schedule of Limbs defined in XML (mods add lines for their own body parts, B.3).</summary>
    public sealed class LimbRow
    {
        public string key;
        public float percent;
        public bool lethal;
        public string comment;
        public string tag;
        public bool shared;
        public bool family;
    }

    public sealed class LimbTagRow { public string tag; public float total; }

    public sealed class SL_LimbScheduleDef : Def
    {
        public List<LimbRow> rows = new List<LimbRow>();
        public List<LimbTagRow> tagTotals = new List<LimbTagRow>();

        public void Apply()
        {
            foreach (var r in rows ?? new List<LimbRow>())
                LimbSchedule.Set(new LimbEntry { Key = r.key, Percent = r.percent, Lethal = r.lethal, Comment = r.comment ?? "", Tag = r.tag, SharedByTag = r.shared, LimbFamily = r.family });
            foreach (var t in tagTotals ?? new List<LimbTagRow>()) LimbSchedule.SetTagTotal(t.tag, t.total);
        }
    }

    [DefOf]
    public static class SLInsuranceDefOf
    {
        public static LetterDef SL_ClawbackLetter;
        public static LetterDef SL_ClawbackNotice;
        public static LetterDef SL_ClawbackBad;
        static SLInsuranceDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(SLInsuranceDefOf)); }
    }
}
