// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Silverline
{
    /// <summary>
    /// Intake of damage events for Clawback Mutual (8.13 "schmale Haken"). The Harmony patches call the specific methods;
    /// other modules (inspections, guests, casino …) report fines, liability and closures through the Report* methods.
    /// </summary>
    public static class ClawbackApi
    {
        public static bool Ready => Modules.On(ModuleId.Bank) && InsuranceComponent.Instance != null && InsuranceComponent.Instance.Started;

        /// <summary>Which policy tiers pay for which trigger (Wanderlust: Bare Minimum only death, Premium every scratch).</summary>
        public static bool TierCovers(SL_PolicyDef def, CoverTier tier, ClaimTrigger trig)
        {
            if (def.kind != PolicyKind.Travel) return true;
            switch (tier)
            {
                case CoverTier.BareMinimum: return trig == ClaimTrigger.TravelDeath;
                case CoverTier.GoodEnough: return trig == ClaimTrigger.TravelDeath || trig == ClaimTrigger.TravelLimb;
                default: return true;
            }
        }

        static IEnumerable<PolicyContract> Contracts(ClaimTrigger trig)
        {
            var comp = InsuranceComponent.Instance; if (comp == null) yield break;
            foreach (var c in comp.contracts)
                if (c.Active && c.Def != null && c.Def.HasTrigger(trig) && TierCovers(c.Def, c.tier, trig)) yield return c;
        }

        static bool IsFireDamage(DamageDef d) => d != null && (d == DamageDefOf.Flame || d == DamageDefOf.Bomb || d.isExplosive || d.defName == "Burn");

        // ---- buildings ---------------------------------------------------------------------------------------------------------------
        public static void BuildingDestroyed(Building b, DamageInfo? dinfoN)
        {
            if (!Ready || b?.def?.building == null || b.Faction != Faction.OfPlayer || !b.Spawned || !dinfoN.HasValue) return;
            if (b.def.IsFrame || b.def.building.isNaturalRock || b.def.destroyable == false) return;
            var dinfo = dinfoN.Value;
            var map = b.Map;
            var hints = new List<string>();
            ClaimTrigger trig;
            if (dinfo.Instigator is Pawn inst)
            {
                if (inst.Faction == Faction.OfPlayer) { trig = IsFireDamage(dinfo.Def) ? ClaimTrigger.FireBuilding : ClaimTrigger.RaidBuilding; hints.Add("Self"); }
                else if (inst.HostileTo(Faction.OfPlayer))
                {
                    trig = ClaimTrigger.RaidBuilding;
                    if (inst.Faction?.def?.defName == "Entities") trig = ClaimTrigger.AnomalyBuilding;
                    else if (IsVehicleLike(b.def)) trig = ClaimTrigger.VehicleBuilding;
                    var lord = inst.GetLord();
                    if (inst.RaceProps.IsMechanoid || (lord != null && lord.LordJob != null && lord.LordJob.GetType().Name.Contains("Siege"))) hints.Add("Siege");
                }
                else return;
            }
            else
            {
                if (!IsFireDamage(dinfo.Def)) return;
                trig = IsVehicleLike(b.def) ? ClaimTrigger.VehicleBuilding : ClaimTrigger.FireBuilding;
                string w = map.weatherManager?.curWeather?.defName ?? "";
                if (w.Contains("Thunder") && dinfo.Def == DamageDefOf.Flame) hints.Add("Weather");
            }
            if (b is Building_Door) hints.Add("Door");
            double value = b.def.GetStatValueAbstract(StatDefOf.MarketValue, b.Stuff);
            if (value < 0.5) return;
            List<ThingDefCountClass> cost = null;
            try { cost = b.def.CostListAdjusted(b.Stuff, false); } catch { }
            var chain = new List<ClaimTrigger> { trig };
            if (trig == ClaimTrigger.VehicleBuilding || trig == ClaimTrigger.AnomalyBuilding) chain.Add(ClaimTrigger.RaidBuilding);
            if (hints.Contains("Self")) chain.Add(trig == ClaimTrigger.RaidBuilding ? ClaimTrigger.FireBuilding : ClaimTrigger.RaidBuilding);
            PolicyContract contract = null; ClaimTrigger used = trig;
            foreach (var t in chain) { contract = Contracts(t).FirstOrDefault(); if (contract != null) { used = t; break; } }
            if (contract == null) { InsuranceComponent.Instance.CouldHaveBeen(trig, b.LabelNoCount); return; }
            ClaimEngine.FileProperty(contract, used, value, b.LabelNoCount, map, hints, cost, Ins.Now);
        }

        static bool IsVehicleLike(ThingDef def)
        {
            string n = def.thingClass?.Name ?? "";
            return n.Contains("Shuttle") || n.Contains("Vehicle") || def.defName.Contains("Shuttle");
        }

        // ---- pawns --------------------------------------------------------------------------------------------------------------------
        public static void PawnDied(Pawn p, DamageInfo? dinfo, Hediff culprit, bool wasCaravan)
        {
            var comp = InsuranceComponent.Instance;
            if (comp == null || p == null || p.Faction != Faction.OfPlayer || !p.RaceProps.Humanlike) return;
            comp.lastDeathTick = Ins.Now;
            if (!Ready) return;
            bool illness = !dinfo.HasValue || (culprit != null && !(culprit is Hediff_Injury));
            foreach (var c in Contracts(ClaimTrigger.PawnDeath).Where(x => x.holder == p).ToList())
            {
                if (comp.claims.Any(x => x.contractId == c.id && x.death)) continue;
                var cl = ClaimEngine.FileDeath(c, p, illness, false);
                if (c.InWaitingPeriod(Ins.Now)) cl.AddHint("Waiting");
            }
            if (wasCaravan)
                foreach (var c in Contracts(ClaimTrigger.TravelDeath).ToList())
                {
                    if (comp.claims.Any(x => x.contractId == c.id && x.death && x.pawn == p)) continue;
                    ClaimEngine.FileDeath(c, p, illness, true);
                }
        }

        public static void BodyPartLost(Pawn p, Hediff_MissingPart mp, DamageInfo? dinfo)
        {
            if (!Ready || p == null || mp?.Part == null || p.Dead || p.Faction != Faction.OfPlayer || !p.RaceProps.Humanlike) return;
            if (dinfo.HasValue && dinfo.Value.Def == DamageDefOf.SurgicalCut) return;     // operations are excluded (8.13)
            var comp = InsuranceComponent.Instance;
            bool caravan = !p.Spawned && p.IsCaravanMember();
            foreach (var c in Contracts(ClaimTrigger.LimbLoss).Where(x => x.holder == p).ToList())
            {
                var cl = ClaimEngine.FileLimbLoss(c, p, mp.Part);
                if (dinfo.HasValue && dinfo.Value.Instigator is Pawn inst && inst.Faction == Faction.OfPlayer && inst != p) cl.AddHint("Self");
            }
            if (caravan) foreach (var c in Contracts(ClaimTrigger.TravelLimb).ToList()) ClaimEngine.FileTravelLimb(c, p, mp.Part);
        }

        public static void CaravanInjury(Pawn p, Hediff_Injury inj)
        {
            if (!Ready || p == null || p.Faction != Faction.OfPlayer || p.Spawned || !p.IsCaravanMember()) return;
            foreach (var c in Contracts(ClaimTrigger.TravelInjury).ToList())
            {
                double amount = Math.Max(1.0, c.Def.perInjuryFlat * (0.5 + Math.Min(2f, inj.Severity)));
                var comp = InsuranceComponent.Instance; int now = Ins.Now;
                var open = comp.claims.FirstOrDefault(x => x.contractId == c.id && x.trigger == ClaimTrigger.TravelInjury && x.state == ClaimState.Review && now - x.filedTick < 30000);
                if (open != null) { open.lossValue += amount; open.gross += amount; open.offered = open.gross; open.injuries++; open.subject = Ins.T("Claim.Scratches", open.injuries); }
                else { var cl = ClaimEngine.FileFlat(c, ClaimTrigger.TravelInjury, amount, Ins.T("Claim.Scratches", 1), p.MapHeld, amount); cl.injuries = 1; cl.pawn = p; cl.pawnName = p.LabelShortCap; }
            }
        }

        // ---- weather ------------------------------------------------------------------------------------------------------------------
        public static void ConditionStarted(GameCondition cond)
        {
            if (!Ready || cond?.def == null) return;
            string d = cond.def.defName;
            if (d != "ColdSnap" && d != "ToxicFallout") return;
            var map = cond.gameConditionManager?.ownerMap ?? InsurerBooks.HomeMap; if (map == null || !map.IsPlayerHome) return;
            double crops = 0;
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
                if (t is Plant pl && pl.IsCrop && pl.def.plant != null && pl.def.plant.Sowable) crops += pl.MarketValue;
            double loss = crops * (d == "ColdSnap" ? 0.6 : 0.4);
            if (loss < 5) return;
            ReportLoss(ClaimTrigger.Crop, map, Money.FromSilver(loss), Ins.T("Claim.Crop", cond.def.label), d == "ColdSnap" ? "Weather" : null);
        }

        // ---- public API for other modules ---------------------------------------------------------------------------------------------
        /// <summary>A fine (inspector penalty, licence trouble). Legal Shield pays a share.</summary>
        public static bool ReportFine(Map map, Money amount, string reason) => ReportLoss(ClaimTrigger.Fine, map, amount, reason);
        /// <summary>Damage to guests or traders' goods you are liable for (It Wasn't Me Liability).</summary>
        public static bool ReportLiability(Map map, Money amount, string reason) => ReportLoss(ClaimTrigger.Liability, map, amount, reason);
        /// <summary>Loss of income after a closure (Closed for Repairs).</summary>
        public static bool ReportClosure(Map map, Money lostIncome, string reason) => ReportLoss(ClaimTrigger.Closure, map, lostIncome, reason);

        /// <summary>Generic flat-loss claim. Returns true when a policy took it.</summary>
        public static bool ReportLoss(ClaimTrigger trig, Map map, Money loss, string subject, string hint = null)
        {
            if (!Ready || loss.Micro <= 0) return false;
            var c = Contracts(trig).FirstOrDefault();
            if (c == null) { InsuranceComponent.Instance.CouldHaveBeen(trig, subject); return false; }
            var hints = new List<string>(); if (hint != null) hints.Add(hint);
            ClaimEngine.FileFlat(c, trig, loss.Silver, subject, map, null, hints);
            return true;
        }
    }
}
