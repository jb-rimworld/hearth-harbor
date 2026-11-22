// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Silverline
{
    public sealed class SignResult
    {
        public PolicyContract contract;
        public string error;
        public bool Ok => contract != null;
    }

    public sealed class CancelPlan
    {
        public bool special;
        public long effectiveTick;
        public int daysUntil;
        public double bundleLoss;       // share by which the other premiums rise
        public double formErrorChance;
        public int retentionDialogs;
    }

    /// <summary>
    /// Everything Clawback Mutual knows about the colony: contracts, claims, the log. A GameComponent of its own, so removing
    /// Silverline (or this module) leaves a loadable save (KONZEPT 3.7). Ticks only while the Bank module is on.
    /// </summary>
    public sealed class InsuranceComponent : GameComponent
    {
        public List<PolicyContract> contracts = new List<PolicyContract>();
        public List<Claim> claims = new List<Claim>();
        public int nextContractId = 1, nextClaimId = 1;
        public int firstPolicyTick = -1, lastDay = -1, lastHour = -1, lastFiledLetterDay = -1, lastUpsellTick = -9999999, lastDeathTick = -9999999, yearStamp = -1, lastQuadrumNoticed = -1;
        public int letterCounter;
        public double debt;                 // clawback owed after cancellation (clause 22)
        public double courtesyTotal;
        public List<string> log = new List<string>();

        static InsuranceComponent cached;
        public static InsuranceComponent Instance
        {
            get
            {
                var g = Current.Game; if (g == null) return null;
                if (cached == null || Current.Game.components == null || !Current.Game.components.Contains(cached)) cached = g.GetComponent<InsuranceComponent>();
                return cached;
            }
        }

        public InsuranceComponent(Game game) { cached = this; }

        // ---- lookup ----------------------------------------------------------------------------------------------------------------
        public PolicyContract Get(int id) => contracts.FirstOrDefault(c => c.id == id);
        public Claim GetClaim(int id) => claims.FirstOrDefault(c => c.id == id);
        public IEnumerable<PolicyContract> Active => contracts.Where(c => c.Active);
        public bool HasActive(string defName) => contracts.Any(c => c.Active && c.defName == defName);
        public bool Has(SL_PolicyDef def, Pawn holder) => contracts.Any(c => c.Active && c.defName == def.defName && c.holder == holder);
        public IEnumerable<Claim> OpenClaims => claims.Where(c => c.Open);
        public bool Started => firstPolicyTick >= 0;

        public void Log(string line)
        {
            log.Add(Find.TickManager.TicksGame.ToStringTicksToPeriod() + "  " + line);
            if (log.Count > 80) log.RemoveRange(0, log.Count - 80);
            HLog.Debug("Bank", "Clawback: " + line);
        }

        // ---- signing ----------------------------------------------------------------------------------------------------------------
        public static bool ThreatNow(Map map)
        {
            if (map == null) return false;
            try { if (GenHostility.AnyHostileActiveThreatToPlayer(map)) return true; } catch { }
            try { if (map.fireWatcher != null && map.fireWatcher.LargeFireDangerPresent) return true; } catch { }
            return false;
        }

        public string CanSign(SL_PolicyDef def, CoverTier tier, Pawn holder)
        {
            if (!Modules.On(ModuleId.Bank)) return Ins.T("Sign.Off");
            if (!InsurancePricing.TierUnlocked(def, tier, out var why)) return why;
            if (def.kind == PolicyKind.Person && (holder == null || holder.Dead || !holder.IsColonist || !holder.RaceProps.Humanlike)) return Ins.T("Sign.NoHolder");
            if (def.kind == PolicyKind.Person && def.defName == "SL_GoldenYears" && holder.ageTracker.AgeBiologicalYears < 60 && tier != CoverTier.BareMinimum) { /* sold early, pays from 60 */ }
            if (Has(def, def.kind == PolicyKind.Person ? holder : null)) return Ins.T("Sign.Duplicate");
            if (debt > 0.01) return Ins.T("Sign.Debt", Ins.Silver(debt));
            var map = InsurerBooks.HomeMap;
            if (map == null) return Ins.T("Sign.NoMap");
            if (ThreatNow(map)) return Ins.T("Sign.Threat");
            return null;
        }

        public SignResult Sign(SL_PolicyDef def, CoverTier tier, Pawn holder, double sum, IEnumerable<string> riders, IEnumerable<string> preTicked)
        {
            var res = new SignResult();
            string err = CanSign(def, tier, holder);
            if (err != null) { res.error = err; return res; }
            var map = InsurerBooks.HomeMap;
            InsurancePricing.SumRange(def, map, holder, out var min, out var max);
            sum = Math.Max(min, Math.Min(max, sum));
            var riderList = (riders ?? Enumerable.Empty<string>()).Where(k => def.riders.Any(r => r.key == k)).Distinct().ToList();
            var kept = (preTicked ?? Enumerable.Empty<string>()).Where(k => riderList.Contains(k)).ToList();
            int now = Ins.Now;
            var c = new PolicyContract
            {
                id = nextContractId++, defName = def.defName, tier = tier, holder = def.kind == PolicyKind.Person ? holder : null,
                holderName = holder?.LabelShortCap, sum = sum, startTick = now, waitUntilTick = now + InsSettings.Waiting * ClaimEngine.Day,
                riders = riderList, preTickedKept = kept, quadrumStamp = (int)InsuranceTime.QuadrumIndex()
            };
            if (def.kind == PolicyKind.Person && holder != null) c.preexistingGrade = LimbGrader.StateGrade(holder, true, InsSettings.Hard);
            var q = InsurancePricing.QuoteFor(def, tier, holder, sum, riderList, InsurancePricing.BundleCountForNew, null);
            c.lastPremiumQuadrum = q.premiumQuadrum;
            string src = "free";
            if (q.billing > 0)
            {
                if (!InsurerBooks.TryPay(map, Ins.M(q.billing), "Clawback Mutual premium: " + def.label, out src))
                {
                    nextContractId--; res.error = Ins.T("Sign.CannotPay", Ins.Silver(q.billing)); return res;
                }
                c.premiumPaid += q.billing; c.premiumYear += q.billing;
                if (kept.Count > 0) c.trickCostYear += q.billing * InsurancePricing.RiderShare(def, kept) / q.riders;
            }
            c.lastBilledTick = now; c.nextBillTick = now + InsSettings.Billing * ClaimEngine.Day;
            contracts.Add(c);
            if (firstPolicyTick < 0) { firstPolicyTick = now; yearStamp = now / (ClaimEngine.Day * InsuranceMath.DaysPerYear); }
            Log(Ins.T("Log.Signed", c.Label, Ins.TierName(tier)));
            ClaimLetters.Welcome(c, q, src == "free" ? Ins.T("Source.Free") : src);
            res.contract = c;
            return res;
        }

        public void End(PolicyContract c, string reason)
        {
            if (c == null || c.ended) return;
            c.ended = true; c.endedTick = Ins.Now; c.endReason = reason;
            if (c.owedClawback > 0.001) { debt += c.owedClawback; c.owedClawback = 0; }
            Log(Ins.T("Log.Ended", c.Label, reason));
            ClaimLetters.Notice(Ins.T("Letter.Ended.Title", c.Label), Ins.T("Letter.Ended.Body", c.Label, reason, debt > 0.01 ? Ins.T("Letter.Ended.Debt", Ins.Silver(debt)) : ""), neutral: false);
        }

        public bool SettleDebt()
        {
            if (debt <= 0.001) return true;
            if (!InsurerBooks.TryPay(InsurerBooks.HomeMap, Ins.M(debt), "Clawback Mutual: clause 22 settlement", out _)) return false;
            Log(Ins.T("Log.Settled", Ins.Silver(debt)));
            debt = 0; return true;
        }

        // ---- cancelling (B.6) ----------------------------------------------------------------------------------------------------------
        public CancelPlan PlanCancel(PolicyContract c)
        {
            int now = Ins.Now;
            var plan = new CancelPlan { special = now <= c.specialCancelUntil };
            long q = GenDate.TicksPerQuadrum;
            long eff = InsuranceMath.CancelEffectiveTick(now, InsuranceTime.TicksIntoQuadrum(), q, (long)InsSettings.Notice * ClaimEngine.Day, plan.special);
            if (InsSettings.Hard && c.discountUntilTick > now && !plan.special) while (eff < c.discountUntilTick) eff += q;
            plan.effectiveTick = eff; plan.daysUntil = (int)Math.Ceiling((eff - now) / (double)ClaimEngine.Day);
            plan.formErrorChance = InsSettings.FormErrorChance;
            plan.retentionDialogs = c.retentionStage >= InsSettings.RetentionCount ? 0 : InsSettings.RetentionCount - c.retentionStage;
            int n = contracts.Count(x => x.Active);
            plan.bundleLoss = n >= 2 ? Math.Max(0, InsuranceMath.BundleFactor(n - 1) / InsuranceMath.BundleFactor(n) - 1.0) : 0;
            return plan;
        }

        /// <summary>Files the cancellation (form C-7). Rolls the form error; returns what happened.</summary>
        public string FileCancel(PolicyContract c)
        {
            if (c == null || !c.Active) return null;
            int now = Ins.Now;
            var plan = PlanCancel(c);
            c.cancelRequestedTick = now;
            long eff = plan.effectiveTick;
            string msg;
            bool legal = HasActive("SL_LegalShield") && !(InsSettings.Hard && ClauseEngine.ByTrigger(ClauseTrigger.Conflict) is SL_ClauseDef cf && ClauseEngine.Active(cf, c.tier));
            double p = legal ? 0 : plan.formErrorChance;
            c.formErrorHit = false;
            if (!plan.special && p > 0 && JRand.Stream("insurance.form").Chance(p))
            {
                c.formErrorHit = true;
                eff = InsuranceMath.CancelEffectiveAfterFormError(eff, GenDate.TicksPerQuadrum, resubmitKeepsDeadline: !InsSettings.Hard);
                msg = InsSettings.Hard ? Ins.T("Cancel.FormErrorHard") : Ins.T("Cancel.FormErrorSoft");
            }
            else msg = plan.special ? Ins.T("Cancel.Special") : Ins.T("Cancel.Filed", Ins.Duration((int)(eff - now)));
            c.cancelEffectiveTick = (int)Math.Min(int.MaxValue, eff);
            Log(Ins.T("Log.CancelFiled", c.Label, Ins.Duration((int)Math.Max(0, eff - now))));
            if (c.cancelEffectiveTick <= now) { FinalizeCancel(c); }
            return msg;
        }

        public void WithdrawCancel(PolicyContract c) { if (c != null) { c.cancelEffectiveTick = -1; c.cancelRequestedTick = -1; c.formErrorHit = false; } }

        void FinalizeCancel(PolicyContract c)
        {
            int nOthers = contracts.Count(x => x.Active && x != c);
            double retro = 0;
            if (InsSettings.Hard && nOthers >= 1)
            {
                // the bundle discount of the running quadrum is charged back (B.6, Overbooked and up)
                double before = InsuranceMath.BundleFactor(nOthers + 1), after = InsuranceMath.BundleFactor(nOthers);
                double sumPrem = contracts.Where(x => x.Active && x != c).Sum(x => InsurancePricing.QuoteFor(x).premiumQuadrum);
                retro = Math.Max(0, sumPrem * (after / before - 1.0)) * Math.Max(0.2, 1.0 - InsuranceTime.TicksIntoQuadrum() / (double)GenDate.TicksPerQuadrum);
            }
            End(c, Ins.T("End.Cancelled"));
            if (retro > 0.01)
            {
                if (InsurerBooks.TryPay(InsurerBooks.HomeMap, Ins.M(retro), "Clawback Mutual: bundle discount recalculation", out _))
                    ClaimLetters.Notice(Ins.T("Letter.Retro.Title"), Ins.T("Letter.Retro.Body", Ins.Silver(retro)));
                else debt += retro;
            }
        }

        public void RetentionAccept(PolicyContract c, double discount, int quadrums)
        {
            c.retentionStage++;
            c.discountShare = Math.Max(c.discountShare, discount);
            c.discountUntilTick = Ins.Now + quadrums * GenDate.TicksPerQuadrum;
            WithdrawCancel(c);
            Log(Ins.T("Log.Retained", c.Label, Ins.Pct(discount)));
        }

        /// <summary>"You could have been covered." (N51): after damage nobody paid for. At most one per quadrum, never within five days of a death.</summary>
        public void CouldHaveBeen(ClaimTrigger trig, string what)
        {
            if (!Started || !InsSettings.TrickActive(InsSettings.TrickUpsell)) return;
            int now = Ins.Now;
            if (now - lastDeathTick < 5 * ClaimEngine.Day || now - lastUpsellTick < GenDate.TicksPerQuadrum / 3) return;
            var def = InsurancePricing.AllPolicies.FirstOrDefault(d => d.HasTrigger(trig) && InsurancePricing.TierUnlocked(d, CoverTier.GoodEnough, out _));
            if (def == null) return;
            lastUpsellTick = now;
            ClaimLetters.Notice(Ins.T("Letter.Uncovered.Title"), Ins.T("Letter.Uncovered.Body", what, def.LabelCap), false, false);
        }

        public void Accept(int claimId)
        {
            var cl = GetClaim(claimId); if (cl == null) return;
            cl.negotiationClosed = true;
            ClaimLetters.RemoveFor(claimId);
        }

        // ---- tick ------------------------------------------------------------------------------------------------------------------------
        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 11) return;
            if (!Modules.On(ModuleId.Bank)) return;
            Guard.Run(ModuleId.Bank, "Insurance.Tick", () => Process(now));
        }

        /// <summary>One pass of the bookkeeping at the given tick (also called directly by the tests).</summary>
        public void Process(int now)
        {
            ProcessClaims(now);
            for (int i = 0; i < contracts.Count; i++)
            {
                var c = contracts[i];
                if (!c.Active) continue;
                if (c.CancelPending && now >= c.cancelEffectiveTick) { FinalizeCancel(c); continue; }
                if (c.holder != null && c.holder.Dead && !contracts[i].ended) { if (!claims.Any(x => x.contractId == c.id && x.death && x.Open)) End(c, Ins.T("End.Holder")); continue; }
                if (now >= c.nextBillTick && c.nextBillTick >= 0) Bill(c, now);
                if (!c.Active) continue;
                RolloverQuadrum(c, now);
            }
            if (now / 2500 != lastHour) { lastHour = now / 2500; HourlyChecks(now); }
            if (now / ClaimEngine.Day != lastDay) { lastDay = now / ClaimEngine.Day; DailyChecks(now); }
            Prune(now);
        }

        void ProcessClaims(int now)
        {
            foreach (var cl in claims.ToList())
            {
                if (cl.state == ClaimState.Review && now >= cl.decideTick) Guard.Run(ModuleId.Bank, "Insurance.Decide", () => ClaimEngine.Decide(cl));
                else if (cl.state == ClaimState.Decided && now >= cl.payTick) Guard.Run(ModuleId.Bank, "Insurance.Pay", () => ClaimEngine.Pay(cl));
            }
        }

        void Bill(PolicyContract c, int now)
        {
            var def = c.Def; if (def == null) { End(c, "unknown policy"); return; }
            var q = InsurancePricing.QuoteFor(c);
            double amt = def.riskModel == RiskModel.PerTravelDay ? q.travelPerHeadDay * c.travelHeadDays : q.billing;
            var map = InsurerBooks.HomeMap;
            if (amt <= 0.0001) { c.nextBillTick = now + InsSettings.Billing * ClaimEngine.Day; c.travelHeadDays = 0; return; }
            if (InsurerBooks.TryPay(map, Ins.M(amt), "Clawback Mutual premium: " + def.label, out _))
            {
                c.premiumPaid += amt; c.premiumYear += amt;
                if (c.preTickedKept.Count > 0) c.trickCostYear += amt * InsurancePricing.RiderShare(def, c.preTickedKept) / q.riders;
                c.missedBills = 0; c.lastBilledTick = now; c.nextBillTick = now + InsSettings.Billing * ClaimEngine.Day; c.travelHeadDays = 0;
            }
            else
            {
                c.missedBills++;
                c.nextBillTick = now + ClaimEngine.Day;
                if (c.missedBills >= 3) { End(c, Ins.T("End.Lapsed")); ClaimLetters.Notice(Ins.T("Letter.Lapsed.Title", c.Label), Ins.T("Letter.Lapsed.Body", c.Label, Ins.Silver(amt)), false, true); }
                else if (c.missedBills == 1) ClaimLetters.Notice(Ins.T("Letter.Missed.Title", c.Label), Ins.T("Letter.Missed.Body", c.Label, Ins.Silver(amt)), false, true);
            }
        }

        void RolloverQuadrum(PolicyContract c, int now)
        {
            int qi = (int)InsuranceTime.QuadrumIndex();
            if (c.quadrumStamp < 0) { c.quadrumStamp = qi; return; }
            if (qi == c.quadrumStamp) return;
            c.quadrumStamp = qi;
            c.noClaimsClass = InsuranceMath.NextNoClaimsClass(c.noClaimsClass, c.claimThisQuadrum);
            c.damageHistory.Add(c.damageThisQuadrum); while (c.damageHistory.Count > 4) c.damageHistory.RemoveAt(0);
            c.claimThisQuadrum = false; c.damageThisQuadrum = 0;
            if (c.hazSamples > 0) { c.lastShareB = c.hazB / c.hazSamples; c.lastShareC = c.hazC / c.hazSamples; }
            c.hazSamples = c.hazB = c.hazC = 0;
            // premium change: a rise opens a special cancellation window (5 days, any tier)
            var q = InsurancePricing.QuoteFor(c);
            if (c.lastPremiumQuadrum > 0 && q.premiumQuadrum > c.lastPremiumQuadrum * 1.03)
            {
                c.specialCancelUntil = now + 5 * ClaimEngine.Day;
                ClaimLetters.Notice(Ins.T("Letter.Premium.Title", c.Label), Ins.T("Letter.Premium.Body", c.Label, Ins.Silver(c.lastPremiumQuadrum), Ins.Silver(q.premiumQuadrum)));
            }
            c.lastPremiumQuadrum = q.premiumQuadrum;
            if (c.discountUntilTick >= 0 && now >= c.discountUntilTick) { c.discountShare = 0; c.discountUntilTick = -1; }
            MaybeUpsell(c, now);
        }

        void MaybeUpsell(PolicyContract c, int now)
        {
            if (!InsSettings.TrickActive(InsSettings.TrickUpsell)) return;
            if (now - lastDeathTick < 5 * ClaimEngine.Day) return;       // never within five days of a death (B.6)
            if (now - lastUpsellTick < GenDate.TicksPerQuadrum - 1000) return;
            var def = c.Def; if (def == null) return;
            bool upgrade = c.tier != CoverTier.Premium && InsurancePricing.TierUnlocked(def, CoverTier.Premium, out _);
            if (!upgrade) return;
            lastUpsellTick = now;
            string title = Ins.T("Upsell.Title"), text = Ins.T("Upsell.Body", def.LabelCap, Ins.TierName(c.tier));
            if (InsSettings.Hard) ClaimLetters.Notice(title, text); else Messages.Message(title + ": " + text, MessageTypeDefOf.NeutralEvent, false);
        }

        void HourlyChecks(int now)
        {
            foreach (var c in contracts)
            {
                if (!c.Active || c.holder == null || c.Def == null || c.Def.kind != PolicyKind.Person) continue;
                if (c.holder.Dead) continue;
                var g = InsurancePricing.HazardOf(c.holder);
                c.hazSamples++; if (g == HazardGroup.B) c.hazB++; else if (g == HazardGroup.C) c.hazC++;
            }
        }

        public void DailyChecks(int now)
        {
            // caravan head-days for travel cover
            int heads = 0;
            try { foreach (var car in Find.World.worldObjects.Caravans) if (car.IsPlayerControlled) heads += car.PawnsListForReading.Count(p => p.IsColonist && !p.Dead); } catch { }
            foreach (var c in contracts.Where(x => x.Active && x.Def != null && x.Def.riskModel == RiskModel.PerTravelDay)) c.travelHeadDays += heads;

            AccrueBenefits(now);
            CheckRegrowth(now);
            ClauseNotices(now);
            YearEnd(now);
        }

        void AccrueBenefits(int now)
        {
            var map = InsurerBooks.HomeMap;
            foreach (var c in contracts.Where(x => x.Active && x.Def != null).ToList())
            {
                var def = c.Def;
                if (c.InWaitingPeriod(now)) continue;
                if (def.dailyBenefit > 0 && def.HasTrigger(ClaimTrigger.Illness) && map != null)
                {
                    int bedridden = 0;
                    foreach (var p in map.mapPawns.FreeColonistsSpawned)
                        if (p.InBed() && (p.health.HasHediffsNeedingTend() || p.health.hediffSet.HasImmunizableNotImmuneHediff())) bedridden++;
                    if (bedridden > 0)
                    {
                        double mult = c.tier == CoverTier.BareMinimum ? 1.0 : c.tier == CoverTier.GoodEnough ? 1.5 : 2.5;
                        c.accrued += bedridden * def.dailyBenefit * mult; if (c.accruedSince < 0) c.accruedSince = now;
                    }
                }
                if (def.dailyAnnuity > 0 && c.holder != null && !c.holder.Dead && c.holder.ageTracker.AgeBiologicalYears >= 60)
                {
                    if (c.tier == CoverTier.BareMinimum)
                    {
                        if (c.accruedSince < 0 && c.accrued <= 0 && c.benefitPaid <= 0.001 && !claims.Any(x => x.contractId == c.id)) { c.accrued = c.sum * def.lumpSum; c.accruedSince = now - InsSettings.Billing * ClaimEngine.Day; }
                    }
                    else { c.accrued += c.sum * def.dailyAnnuity * (c.tier == CoverTier.Premium ? 1.5 : 1.0); if (c.accruedSince < 0) c.accruedSince = now; }
                }
                if (c.accrued > 0.001 && c.accruedSince >= 0 && now - c.accruedSince >= InsSettings.Billing * ClaimEngine.Day)
                {
                    var trig = def.HasTrigger(ClaimTrigger.Retirement) ? ClaimTrigger.Retirement : ClaimTrigger.Illness;
                    string subj = trig == ClaimTrigger.Retirement ? Ins.T("Claim.Retirement", c.holder?.LabelShortCap ?? "") : Ins.T("Claim.BedRest");
                    double amount = c.accrued; c.accrued = 0; c.accruedSince = -1;
                    var cl = ClaimEngine.FileFlat(c, trig, amount, subj, map, amount);
                    cl.pawn = c.holder;
                }
            }
        }

        void CheckRegrowth(int now)
        {
            foreach (var cl in claims.Where(x => x.state == ClaimState.Paid && x.trigger == ClaimTrigger.LimbLoss && x.paidGrade > 0.5 && x.pawn != null && !x.pawn.Dead).ToList())
            {
                var c = Get(cl.contractId); if (c == null) continue;
                double g = LimbGrader.StateGrade(cl.pawn, true, InsSettings.Hard, null, out bool po, out _);
                double net = InsuranceMath.NetGrade(g, cl.preGrade);
                if (net < cl.paidGrade - 0.5)
                {
                    double newBenefit = InsuranceMath.AccidentBenefit(c.sum, c.tier, net, c.HasEffect("plusProgression"), po);
                    double back = Math.Max(0, cl.paid - newBenefit);
                    cl.paid = newBenefit; cl.paidGrade = net;
                    if (back > 0.01)
                    {
                        if (c.Active) c.owedClawback += back; else debt += back;
                        var clause = ClauseEngine.ByTrigger(ClauseTrigger.Regrowth);
                        ClaimLetters.Notice(Ins.T("Letter.Regrowth.Title", cl.pawnName), Ins.T("Letter.Regrowth.Body", cl.pawnName, Ins.Silver(back), clause?.Print(InsSettings.Pressure >= 0.6) ?? "", c.Active ? Ins.T("Letter.Regrowth.Offset") : Ins.T("Letter.Regrowth.Debt")), false, true);
                        Log(Ins.T("Log.Regrowth", cl.id, Ins.Silver(back)));
                    }
                }
            }
        }

        void ClauseNotices(int now)
        {
            // clause 31: renewal notice three days before each policy anniversary (B.7)
            foreach (var c in contracts.Where(x => x.Active))
            {
                int age = now - c.startTick, year = ClaimEngine.Day * InsuranceMath.DaysPerYear;
                if (age >= year - 3 * ClaimEngine.Day && age < year - 2 * ClaimEngine.Day || (age > year && (age % year) >= year - 3 * ClaimEngine.Day && (age % year) < year - 2 * ClaimEngine.Day))
                {
                    var cl = ClauseEngine.ByTrigger(ClauseTrigger.AutoRenew);
                    ClaimLetters.Notice(Ins.T("Letter.Renew.Title", c.Label), Ins.T("Letter.Renew.Body", c.Label, cl?.Print(InsSettings.Pressure >= 0.6) ?? ""));
                }
            }
        }

        void YearEnd(int now)
        {
            if (firstPolicyTick < 0) return;
            int y = now / (ClaimEngine.Day * InsuranceMath.DaysPerYear);
            if (y == yearStamp) return;
            yearStamp = y;
            var act = contracts.Where(c => c.Active || c.premiumYear > 0).ToList();
            double cost = act.Sum(c => c.trickCostYear), prem = act.Sum(c => c.premiumYear);
            if (prem <= 0.001) { foreach (var c in contracts) { c.trickCostYear = 0; c.premiumYear = 0; } return; }
            double cap = HSettings.Get(InsSettings.TrickCap);
            double credit = InsuranceMath.CourtesyCredit(cost, prem, cap);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Ins.T("Letter.Year.Intro", Ins.Silver(prem), act.Count(c => c.Active)));
            sb.AppendLine();
            foreach (var c in act.Where(c => c.trickCostYear > 0.001))
                sb.AppendLine("• " + Ins.T("Letter.Year.Trick", c.Label, string.Join(", ", c.preTickedKept.Select(k => ClaimLetters.RiderLabel(c.Def, k))), Ins.Silver(c.trickCostYear)));
            sb.AppendLine(Ins.T("Letter.Year.Total", Ins.Silver(cost), Ins.Pct(prem > 0 ? cost / prem : 0), Ins.Pct(cap)));
            if (credit > 0.01)
            {
                string how = InsurerBooks.Refund(InsurerBooks.HomeMap, Ins.M(credit), "Clawback Mutual: courtesy credit");
                courtesyTotal += credit;
                sb.AppendLine(); sb.AppendLine(Ins.T("Letter.Year.Credit", Ins.Silver(credit), how ?? ""));
            }
            else sb.AppendLine(Ins.T("Letter.Year.NoCredit"));
            ClaimLetters.Notice(Ins.T("Letter.Year.Title"), sb.ToString().TrimEnd());
            foreach (var c in contracts) { c.trickCostYear = 0; c.premiumYear = 0; }
        }

        void Prune(int now)
        {
            if (claims.Count < 60) return;
            int keep = 0;
            foreach (var cl in claims.OrderByDescending(x => x.filedTick).ToList())
            {
                if (cl.Open) continue;
                keep++;
                bool needed = cl.state == ClaimState.Paid && cl.trigger == ClaimTrigger.LimbLoss && cl.paidGrade > 0.5 && cl.pawn != null && !cl.pawn.Dead;
                if (keep > 40 && !needed) claims.Remove(cl);
            }
            contracts.RemoveAll(c => c.ended && now - c.endedTick > 120 * ClaimEngine.Day && !claims.Any(x => x.contractId == c.id));
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref contracts, "contracts", LookMode.Deep);
            Scribe_Collections.Look(ref claims, "claims", LookMode.Deep);
            Scribe_Values.Look(ref nextContractId, "nextContract", 1); Scribe_Values.Look(ref nextClaimId, "nextClaim", 1);
            Scribe_Values.Look(ref firstPolicyTick, "firstPolicy", -1); Scribe_Values.Look(ref lastDay, "lastDay", -1); Scribe_Values.Look(ref lastHour, "lastHour", -1);
            Scribe_Values.Look(ref lastFiledLetterDay, "lastFiledDay", -1); Scribe_Values.Look(ref lastUpsellTick, "lastUpsell", -9999999);
            Scribe_Values.Look(ref lastDeathTick, "lastDeath", -9999999); Scribe_Values.Look(ref yearStamp, "yearStamp", -1);
            Scribe_Values.Look(ref letterCounter, "letterCounter"); Scribe_Values.Look(ref debt, "debt"); Scribe_Values.Look(ref courtesyTotal, "courtesy");
            Scribe_Collections.Look(ref log, "log", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                contracts = contracts ?? new List<PolicyContract>(); claims = claims ?? new List<Claim>(); log = log ?? new List<string>();
                int before = contracts.Count;
                contracts.RemoveAll(c => c == null || c.Def == null && !c.ended);
                claims.RemoveAll(c => c == null);
                if (before != contracts.Count) HLog.Warn("Bank", "Insurance: dropped " + (before - contracts.Count) + " contracts of unknown policy definitions");
            }
        }
    }

    /// <summary>Quadrum arithmetic in the colony's local calendar.</summary>
    public static class InsuranceTime
    {
        public static int TicksIntoQuadrum()
        {
            var map = Find.AnyPlayerHomeMap;
            if (map != null) { try { return GenLocalDate.DayOfQuadrum(map) * GenDate.TicksPerDay + GenLocalDate.DayTick(map); } catch { } }
            return (int)(GenTicks.TicksAbs % GenDate.TicksPerQuadrum);
        }
        public static long QuadrumIndex() => (GenTicks.TicksAbs - TicksIntoQuadrum()) / GenDate.TicksPerQuadrum;
    }
}
