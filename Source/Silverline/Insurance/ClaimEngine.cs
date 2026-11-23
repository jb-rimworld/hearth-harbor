// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    public enum NegApproach { Decency = 0, Quote = 1, Lawyers = 2 }

    public sealed class NegotiationResult
    {
        public bool success, partial, cancelled;
        public double newOffer, chance;
        public string text;
    }

    /// <summary>Filing, deciding, paying and negotiating claims (8.13 "Schadensfall").</summary>
    public static class ClaimEngine
    {
        public const int Day = 60000;
        static RngStream Rng => JRand.Stream("insurance.claims");

        static int Rand(int min, int max) => max <= min ? min : Rng.Range(min, max + 1);

        // ---- filing ----------------------------------------------------------------------------------------------------------------
        static Claim Open(PolicyContract c, ClaimTrigger trig, string subject, Map map, int eventTick)
        {
            var comp = InsuranceComponent.Instance;
            var cl = new Claim
            {
                id = comp.nextClaimId++, contractId = c.id, policyDef = c.defName, tier = c.tier, trigger = trig,
                filedTick = Ins.Now, eventTick = eventTick, subject = subject, mapId = map?.uniqueID ?? -1, sumInsured = c.sum,
                state = ClaimState.Review, outcome = ClaimOutcome.Pending
            };
            if (eventTick < c.waitUntilTick) cl.AddHint("Waiting");
            comp.claims.Add(cl);
            c.claimThisQuadrum = true;
            comp.Log(Ins.T("Log.Filed", cl.id, subject));
            return cl;
        }

        static void ScheduleReview(Claim cl, int minDaysOverride = -1)
        {
            InsuranceMath.ReviewDays(cl.tier, out int min, out int max);
            int days = Rand(min, max);
            var cr = ClauseEngine.Evaluate(cl);
            days += cr.extraDays;
            if (InsurancePricing.DeskCount() > 0) days = Math.Max(0, days - 1);
            var ct = InsuranceComponent.Instance?.Get(cl.contractId);
            if (ct != null && ct.HasEffect("fastReview")) days = Math.Max(0, days - 1);
            if (minDaysOverride >= 0) days = Math.Max(days, minDaysOverride);
            cl.decideTick = Ins.Now + days * Day;
        }

        static ThingDefCountClass CostCount(ThingDef def, ThingDef stuff) => null;

        /// <summary>A building of the colony was destroyed. Merges losses of the same event window into one claim.</summary>
        public static Claim FileProperty(PolicyContract c, ClaimTrigger trig, double value, string label, Map map, IEnumerable<string> hints, List<ThingDefCountClass> cost, int eventTick)
        {
            var comp = InsuranceComponent.Instance;
            int now = Ins.Now;
            var cl = comp.claims.FirstOrDefault(x => x.contractId == c.id && x.trigger == trig && x.state == ClaimState.Review && now - x.filedTick < 30000 && x.mapId == (map?.uniqueID ?? -1));
            bool isNew = cl == null;
            if (isNew)
            {
                cl = Open(c, trig, label, map, eventTick);
                cl.realValue = c.Def.sumFollowsWealth ? InsurancePricing.RealValue(c.Def, map, null) : c.sum;
            }
            else cl.subject = Ins.T("Claim.Several", cl.items.Count + 1);
            cl.items.Add(label);
            cl.lossValue += value;
            if (hints != null) foreach (var h in hints) cl.AddHint(h);
            if (cost != null)
                foreach (var cc in cost)
                {
                    string prefix = cc.thingDef.defName + ":";
                    int idx = cl.materials.FindIndex(m => m.StartsWith(prefix));
                    int n = cc.count;
                    if (idx >= 0) { n += int.Parse(cl.materials[idx].Substring(prefix.Length)); cl.materials[idx] = prefix + n; }
                    else cl.materials.Add(prefix + n);
                }
            cl.gross = Math.Min(cl.lossValue, InsuranceMath.PropertyPayout(cl.lossValue, c.sum, cl.realValue, c.tier) * (1.0 + c.BoostShare));
            cl.offered = cl.gross;
            if (isNew) { ScheduleReview(cl); ClaimLetters.Filed(cl); }
            return cl;
        }

        /// <summary>A flat-benefit loss (fine, liability, crops, closure, accrued daily benefits).</summary>
        public static Claim FileFlat(PolicyContract c, ClaimTrigger trig, double loss, string subject, Map map, double? grossOverride, IEnumerable<string> hints = null)
        {
            var cl = Open(c, trig, subject, map, Ins.Now);
            cl.lossValue = loss; cl.realValue = loss;
            cl.gross = grossOverride ?? Math.Min(loss, loss * c.Def.ShareFor(c.tier) * (1.0 + c.BoostShare));
            cl.offered = cl.gross;
            if (hints != null) foreach (var h in hints) cl.AddHint(h);
            ScheduleReview(cl);
            ClaimLetters.Filed(cl);
            return cl;
        }

        public static Claim FileDeath(PolicyContract c, Pawn pawn, bool illness, bool travel)
        {
            var comp = InsuranceComponent.Instance;
            var cl = Open(c, travel ? ClaimTrigger.TravelDeath : ClaimTrigger.PawnDeath, Ins.T("Claim.Death", pawn.LabelShortCap), pawn.MapHeld ?? InsurerBooks.HomeMap, Ins.Now);
            cl.death = true; cl.pawn = pawn; cl.pawnName = pawn.LabelShortCap;
            double death = travel ? c.sum : InsuranceMath.DeathBenefit(c.sum, c.HasEffect("deathSum") ? 0.30 : 0.20);
            cl.lossValue = c.sum; cl.realValue = c.sum;
            if (illness) cl.AddHint("Illness");
            if (!travel)
            {
                // clause 23: dying before the assessment ends the assessment
                var open = comp.claims.FirstOrDefault(x => x.contractId == c.id && x.trigger == ClaimTrigger.LimbLoss && x.pawn == pawn && x.state == ClaimState.Review);
                if (open != null)
                {
                    double pending = open.offered;
                    cl.AddHint("DeathAssess");
                    death = InsuranceMath.DeathBeforeAssessment(death, pending, generous: !InsSettings.Hard);
                    open.state = ClaimState.Closed; open.closedTick = Ins.Now; open.outcome = ClaimOutcome.Denied;
                    cl.breakdown.Add(Ins.T("Bd.DeathAssess", open.id));
                }
            }
            cl.gross = death; cl.offered = death;
            ScheduleReview(cl);
            ClaimLetters.Filed(cl);
            return cl;
        }

        /// <summary>A body part was lost. Accident plans assess a quadrum later from the state of the body.</summary>
        public static Claim FileLimbLoss(PolicyContract c, Pawn pawn, BodyPartRecord part)
        {
            var comp = InsuranceComponent.Instance;
            var cl = comp.claims.FirstOrDefault(x => x.contractId == c.id && x.trigger == ClaimTrigger.LimbLoss && x.pawn == pawn && x.state == ClaimState.Review);
            bool isNew = cl == null;
            int now = Ins.Now;
            if (isNew)
            {
                cl = Open(c, ClaimTrigger.LimbLoss, Ins.T("Claim.Limb", pawn.LabelShortCap), pawn.MapHeld ?? InsurerBooks.HomeMap, now);
                cl.pawn = pawn; cl.pawnName = pawn.LabelShortCap; cl.preGrade = c.preexistingGrade;
                int wait = HSettings.GetInt(InsSettings.AssessmentDays) * Day;
                cl.assessTick = now + wait; cl.decideTick = cl.assessTick;
                if (c.tier == CoverTier.Premium) { /* Premium assesses as soon as it is asked to: still after the same quadrum, but the preview pays out generously */ }
            }
            Preview(cl, c);
            if (isNew) ClaimLetters.Filed(cl);
            return cl;
        }

        /// <summary>Preview of an accident claim (grade and benefit from the current state of the body). Does not change the outcome.</summary>
        public static void Preview(Claim cl, PolicyContract c)
        {
            if (cl.pawn == null || c == null) return;
            var detail = new List<LostPart>();
            double g = LimbGrader.StateGrade(cl.pawn, true, InsSettings.Hard, detail, out bool partialOnly, out _);
            double net = InsuranceMath.NetGrade(g, cl.preGrade);
            cl.grade = net; cl.lost = detail; cl.partialOnly = partialOnly;
            cl.contributed = LimbGrader.Contributed(cl.pawn, detail);
            double benefit = InsuranceMath.AccidentBenefit(c.sum, c.tier, net, c.HasEffect("plusProgression"), partialOnly);
            if (cl.contributed && InsSettings.Hard) benefit = InsuranceMath.ContributionCut(benefit, true);
            cl.gross = benefit; cl.offered = benefit;
            if (detail.Any(d => d.label.IndexOf("bionic", StringComparison.OrdinalIgnoreCase) >= 0)) cl.AddHint("Bionic");
        }

        /// <summary>Lost body part on a caravan with Wanderlust Travel Cover: pays the Schedule of Limbs percentage of the per-head sum, linear.</summary>
        public static Claim FileTravelLimb(PolicyContract c, Pawn pawn, BodyPartRecord part)
        {
            double v = LimbGrader.ValueOf(pawn, part, out bool lethal, out var entry, out var key);
            if (lethal || v <= 0) return null;
            var cl = Open(c, ClaimTrigger.TravelLimb, Ins.T("Claim.Limb", pawn.LabelShortCap), pawn.MapHeld ?? InsurerBooks.HomeMap, Ins.Now);
            cl.pawn = pawn; cl.pawnName = pawn.LabelShortCap; cl.grade = v;
            cl.lossValue = c.sum * v / 100.0; cl.realValue = cl.lossValue;
            cl.gross = c.sum * v / 100.0; cl.offered = cl.gross;
            cl.lost.Add(new LostPart { label = LimbGrader.LabelOf(part), key = key, comment = entry?.Comment ?? "", value = v });
            ScheduleReview(cl);
            ClaimLetters.Filed(cl);
            return cl;
        }

        // ---- decision -----------------------------------------------------------------------------------------------------------------
        public static void Decide(Claim cl)
        {
            var comp = InsuranceComponent.Instance; var c = comp.Get(cl.contractId);
            if (c == null || cl.state != ClaimState.Review) return;
            int now = Ins.Now;
            cl.breakdown.Clear();
            if (cl.trigger == ClaimTrigger.LimbLoss && cl.pawn != null)
            {
                Preview(cl, c);
                cl.amendedByAssessment = true;
                cl.breakdown.Add(Ins.T("Bd.Grade", cl.grade.ToString("0.#"), Ins.StageLabel(cl.grade)));
                foreach (var l in cl.lost) cl.breakdown.Add(Ins.T("Bd.Part", l.label, l.value.ToString("0.#"), l.comment));
                if (cl.preGrade > 0.01) cl.breakdown.Add(Ins.T("Bd.PreExisting", cl.preGrade.ToString("0.#")));
                if (cl.gross <= 0) cl.breakdown.Add(cl.partialOnly && cl.tier == CoverTier.BareMinimum ? Ins.T("Bd.PartialNotPaid") : Ins.T("Bd.BelowThreshold", InsuranceMath.InvalidityThreshold(cl.tier).ToString("0.#")));
                else cl.breakdown.Add(Ins.T("Bd.Progression", Ins.TierName(cl.tier), Ins.Pct(InsuranceMath.Progression(InsuranceMath.ModelFor(cl.tier, c.HasEffect("plusProgression")), cl.grade) / 100.0), Ins.Silver(c.sum), Ins.Silver(cl.gross)));
                if (cl.contributed && InsSettings.Hard && cl.gross > 0) cl.breakdown.Add(Ins.T("Bd.Contributed"));
            }
            else if (cl.trigger == ClaimTrigger.PawnDeath || cl.trigger == ClaimTrigger.TravelDeath)
            {
                cl.breakdown.Add(Ins.T("Bd.Death", Ins.Silver(cl.gross)));
            }
            else if (cl.items.Count > 0)
            {
                cl.breakdown.Add(Ins.T("Bd.Loss", cl.items.Count, Ins.Silver(cl.lossValue)));
                cl.breakdown.Add(Ins.T("Bd.Replacement", Ins.Pct(InsuranceMath.Replacement(cl.tier)), Ins.Pct(InsuranceMath.Deductible(cl.tier))));
                double uf = InsuranceMath.UnderInsuranceFactor(cl.sumInsured, cl.realValue);
                if (uf < 0.999) cl.breakdown.Add(Ins.T("Bd.UnderInsured", Ins.Silver(cl.sumInsured), Ins.Silver(cl.realValue), Ins.Pct(uf)));
                cl.breakdown.Add(Ins.T("Bd.Gross", Ins.Silver(cl.gross)));
            }
            else cl.breakdown.Add(Ins.T("Bd.Flat", Ins.Silver(cl.lossValue), Ins.Pct(c.Def.ShareFor(cl.tier)), Ins.Silver(cl.gross)));

            var cr = ClauseEngine.Evaluate(cl);
            cl.cutShare = 1.0; cl.clauseDef = null; cl.clauseNote = null;
            SL_ClauseDef cited = null;
            if (cl.gross <= 0.0001)
            {
                cl.outcome = ClaimOutcome.Denied; cl.offered = 0;
            }
            else if (cr.exclusion != null)
            {
                cl.outcome = ClaimOutcome.Denied; cl.offered = 0; cited = cr.exclusion;
            }
            else
            {
                bool legal = comp.HasActive("SL_LegalShield") && !(InsSettings.Hard && ClauseEngine.ByTrigger(ClauseTrigger.Conflict) is SL_ClauseDef cf && ClauseEngine.Active(cf, cl.tier));
                double p0 = InsuranceMath.DenialProbability(InsSettings.Attitude, cl.tier, cr.grey, false);
                double p = legal ? InsuranceMath.DenialProbability(InsSettings.Attitude, cl.tier, cr.grey, true) : p0;
                if (legal && p < p0 - 1e-9) cl.legalHelped = true;
                bool deathLike = cl.death;
                double roll = Rng.NextDouble();
                if (!deathLike && roll < p)
                {
                    if (cl.tier == CoverTier.Premium && c.kulanzYear != now / (Day * InsuranceMath.DaysPerYear))
                    {
                        c.kulanzYear = now / (Day * InsuranceMath.DaysPerYear);
                        cl.outcome = ClaimOutcome.Approved; cl.offered = cl.gross;
                        cl.breakdown.Add(Ins.T("Bd.Kulanz"));
                    }
                    else
                    {
                        cl.outcome = ClaimOutcome.Denied; cl.offered = 0;
                        cited = cr.Cited ?? ClauseEngine.Flavour(cl);
                    }
                }
                else
                {
                    double pPart = deathLike ? 0 : InsuranceMath.PartialProbability(p, cr.grey);
                    if (pPart > 0 && Rng.Chance(pPart))
                    {
                        cl.outcome = ClaimOutcome.Partial; cl.cutShare = 0.5; cl.offered = cl.gross * cl.cutShare;
                        cited = cr.Cited ?? ClauseEngine.Flavour(cl);
                    }
                    else { cl.outcome = ClaimOutcome.Approved; cl.offered = cl.gross; cited = cl.death ? null : cr.applied.FirstOrDefault(x => x.trigger == ClauseTrigger.PreExisting || x.trigger == ClauseTrigger.Bionics || x.trigger == ClauseTrigger.DeathBeforeAssessment); }
                }
                if (cl.legalHelped) cl.breakdown.Add(Ins.T("Bd.Legal"));
            }
            if (cited != null) { cl.clauseDef = cited.defName; cl.clauseNote = cited.exclusion ? Ins.T("Clause.Exclusion") : null; }
            else if (cl.gross <= 0 ) cl.clauseDef = null;

            // payout schedule
            cl.state = ClaimState.Decided;
            InsuranceMath.PayoutDelayDays(cl.tier, out int pmin, out int pmax);
            int delay = Rand(pmin, pmax);
            if (cl.outcome == ClaimOutcome.Denied) delay = 10;               // negotiation window
            else if (cl.tier == CoverTier.Premium && cl.outcome != ClaimOutcome.Approved) delay = 1;
            cl.payTick = now + delay * Day;
            comp.Log(Ins.T("Log.Decided", cl.id, Ins.T("Outcome." + cl.outcome), Ins.Silver(cl.offered)));
            ClaimLetters.Decision(cl);
        }

        // ---- payout ---------------------------------------------------------------------------------------------------------------
        public static void Pay(Claim cl)
        {
            var comp = InsuranceComponent.Instance; if (comp == null) return;
            var c = comp.Get(cl.contractId);
            double amount = cl.offered;
            if (cl.outcome == ClaimOutcome.Denied || amount <= 0.0001) { Close(cl); return; }
            if (c != null && c.owedClawback > 0.0001)
            {
                double off = Math.Min(c.owedClawback, amount);
                amount -= off; c.owedClawback -= off;
                cl.breakdown.Add(Ins.T("Bd.Offset", Ins.Silver(off)));
            }
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == cl.mapId) ?? InsurerBooks.HomeMap;
            string how;
            string memo = "Clawback Mutual claim #" + cl.id + " (" + (cl.Def?.label ?? cl.policyDef) + ")";
            if (cl.materialsChosen && cl.materials.Count > 0) how = DropMaterials(cl, map, amount);
            else how = InsurerBooks.Payout(map, Ins.M(amount), memo);
            cl.paid = amount; cl.state = ClaimState.Paid; cl.closedTick = Ins.Now; cl.paidGrade = cl.grade;
            if (c != null) { c.benefitPaid += amount; c.specialCancelUntil = Ins.Now + 5 * Day; c.damageThisQuadrum += cl.lossValue > 0 ? cl.lossValue : amount; }
            ClaimLetters.RemoveFor(cl.id);
            ClaimLetters.Paid(cl, how ?? "");
            comp.Log(Ins.T("Log.Paid", cl.id, Ins.Silver(amount)));
            if (cl.death && c != null && c.Def != null && c.Def.kind == PolicyKind.Person) comp.End(c, Ins.T("End.Holder"));
        }

        static string DropMaterials(Claim cl, Map map, double amount)
        {
            double factor = Math.Min(1.0, cl.lossValue > 0 ? amount / cl.lossValue : 1.0);
            var things = new List<Thing>();
            var parts = new List<string>();
            foreach (var m in cl.materials)
            {
                var split = m.Split(':'); if (split.Length != 2) continue;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(split[0]); if (def == null) continue;
                int n = (int)Math.Floor(int.Parse(split[1]) * factor);
                if (n <= 0) continue;
                parts.Add(n + "x " + def.label);
                while (n > 0)
                {
                    var t = ThingMaker.MakeThing(def); int take = Math.Min(n, Math.Max(1, def.stackLimit)); t.stackCount = take; n -= take; things.Add(t);
                }
            }
            if (things.Count == 0 || map == null) return InsurerBooks.Payout(map, Ins.M(amount), "Clawback Mutual claim #" + cl.id + " (materials fell through)");
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, things);
            return Ins.T("Source.PaidMaterials", string.Join(", ", parts));
        }

        public static void Close(Claim cl)
        {
            cl.state = ClaimState.Closed; cl.closedTick = Ins.Now;
            ClaimLetters.RemoveFor(cl.id);
        }

        // ---- negotiation (K8) -------------------------------------------------------------------------------------------------------------
        /// <summary>NegotiationAbility of a pawn, 0 for pawns who cannot do social work (the stat is disabled for them and asking it logs a red error).</summary>
        public static float NegotiationOf(Pawn p)
        {
            if (p == null) return 0f;
            var stat = StatDefOf.NegotiationAbility;
            if (stat.Worker.IsDisabledFor(p)) return 0f;
            return p.GetStatValue(stat);
        }

        public static Pawn BestNegotiator(Map map)
        {
            map = map ?? InsurerBooks.HomeMap;
            Pawn best = null; float bv = -1;
            foreach (var p in (map?.mapPawns?.FreeColonistsSpawned ?? new List<Pawn>()))
            {
                if (p.Downed || p.Dead || !p.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) continue;
                if (StatDefOf.NegotiationAbility.Worker.IsDisabledFor(p)) continue;
                float v = NegotiationOf(p);
                if (v > bv) { bv = v; best = p; }
            }
            return best;
        }

        public static double ApproachExtra(Claim cl, NegApproach a, out string hint)
        {
            var comp = InsuranceComponent.Instance;
            bool legal = comp != null && comp.HasActive("SL_LegalShield") && !(InsSettings.Hard && ClauseEngine.ByTrigger(ClauseTrigger.Conflict) is SL_ClauseDef cf && ClauseEngine.Active(cf, cl.tier));
            var clause = cl.Clause;
            double extra = 0; hint = "";
            if (clause != null && clause.exclusion) extra -= 0.6;
            switch (a)
            {
                case NegApproach.Decency: hint = Ins.T("Neg.HintDecency"); break;
                case NegApproach.Quote: extra += clause != null && !clause.exclusion ? 0.2 : -0.1; hint = Ins.T("Neg.HintQuote"); break;
                case NegApproach.Lawyers: extra += legal ? 0.35 : -0.25; hint = legal ? Ins.T("Neg.HintLawyers") : Ins.T("Neg.HintNoLawyers"); break;
            }
            return extra;
        }

        public static double RoundChance(Claim cl, NegApproach a, Pawn negotiator, out string hint)
        {
            double neg = negotiator != null ? NegotiationOf(negotiator) : 0.6;
            double slacker = InsSettings.Attitude * InsuranceMath.SlackerFactor(cl.tier);
            double extra = ApproachExtra(cl, a, out hint);
            return InsuranceMath.NegotiationChance(neg, slacker, cl.tier == CoverTier.Premium, 600.0, extra);
        }

        public static NegotiationResult Negotiate(Claim cl, NegApproach a, Pawn negotiator)
        {
            var res = new NegotiationResult();
            var comp = InsuranceComponent.Instance;
            if (comp == null || cl.state != ClaimState.Decided || cl.negotiationClosed || cl.outcome == ClaimOutcome.Approved) { res.text = Ins.T("Neg.NotPossible"); return res; }
            var rng = JRand.Sealed("insurance.negotiate");
            double p = RoundChance(cl, a, negotiator, out _);
            res.chance = p;
            cl.rounds++;
            double roll = rng.NextDouble();
            if (roll < p)
            {
                res.success = true; res.newOffer = cl.gross;
                cl.outcome = ClaimOutcome.Approved; cl.offered = cl.gross; cl.cutShare = 1.0; cl.clauseDef = cl.clauseDef; 
                res.text = Ins.T("Neg.Success", Ins.Silver(cl.offered));
            }
            else if (rng.NextDouble() < 0.5 * p + 0.1)
            {
                double share = InsuranceMath.PartialShare(rng.NextDouble());
                double offer = Math.Max(cl.offered, cl.gross * share);
                if (offer > cl.offered + 0.0001 || cl.outcome == ClaimOutcome.Denied)
                {
                    res.partial = true; res.newOffer = offer;
                    cl.outcome = ClaimOutcome.Partial; cl.offered = offer; cl.cutShare = cl.gross > 0 ? offer / cl.gross : 0;
                    res.text = Ins.T("Neg.Partial", Ins.Silver(offer), Ins.Pct(cl.cutShare));
                }
                else res.text = Ins.T("Neg.Fail");
            }
            else res.text = Ins.T("Neg.Fail");
            if (cl.outcome != ClaimOutcome.Denied && cl.payTick < Ins.Now + Day && cl.tier != CoverTier.Premium) cl.payTick = Ins.Now + Day; // paperwork
            if (cl.outcome != ClaimOutcome.Denied && cl.payTick - Ins.Now > 0 && res.success) { /* keep schedule */ }
            if (cl.outcome == ClaimOutcome.Denied && (res.success || res.partial)) cl.payTick = Math.Max(cl.payTick, Ins.Now + Day);
            if (cl.rounds >= 3 || res.success) cl.negotiationClosed = true;
            if (!res.success && !res.partial && cl.rounds >= 3 && InsSettings.Hard && Rng.Chance(0.4))
            {
                var c = comp.Get(cl.contractId);
                if (c != null && c.Active) { res.cancelled = true; res.text += "\n\n" + Ins.T("Neg.Cancelled"); comp.End(c, Ins.T("End.Insurer")); }
            }
            comp.Log(Ins.T("Log.Negotiated", cl.id, cl.rounds, res.success ? Ins.T("Neg.Won") : res.partial ? Ins.T("Neg.Part") : Ins.T("Neg.Lost")));
            ClaimLetters.RemoveFor(cl.id);
            if (cl.outcome != ClaimOutcome.Denied && cl.state == ClaimState.Decided) ClaimLetters.Decision(cl);
            return res;
        }
    }
}
