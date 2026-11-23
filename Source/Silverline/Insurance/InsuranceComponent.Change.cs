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
    /// <summary>What a change of a running policy would do (1.1.0, F24): prices before and after, the fee, when it takes effect and how long an upgrade waits.</summary>
    public sealed class ChangePlan
    {
        public PolicyContract contract;
        public CoverTier newTier;
        public double newSum;
        public List<string> newRiders = new List<string>();
        public Quote oldQuote, newQuote;
        public double fee;
        public bool increase, decrease, noChange;
        public int effectiveTick, waitDays;
        public string error;
        public bool Ok => error == null && !noChange;
        /// <summary>Change of the premium per billing interval (positive = dearer).</summary>
        public double DeltaBilling => (newQuote?.billing ?? 0) - (oldQuote?.billing ?? 0);
        public double DeltaPerTravelDay => (newQuote?.travelPerHeadDay ?? 0) - (oldQuote?.travelPerHeadDay ?? 0);
    }

    public sealed partial class InsuranceComponent
    {
        // ---- changing a running policy (F24) ----------------------------------------------------------------------------------------
        /// <summary>Null when the contract may be changed now, otherwise the reason (shown on the button).</summary>
        public string CanChange(PolicyContract c)
        {
            if (!Modules.On(ModuleId.Bank)) return Ins.T("Sign.Off");
            if (c == null || !c.Active || c.Def == null) return Ins.T("Change.NotActive");
            if (c.CancelPending) return Ins.T("Change.CancelPending");
            if (c.HasPendingChange) return Ins.T("Change.AlreadyPending");
            if (debt > 0.01) return Ins.T("Sign.Debt", Ins.Silver(debt));
            if (ThreatNow(InsurerBooks.HomeMap)) return Ins.T("Sign.Threat");
            int limit = HSettings.GetInt(InsSettings.ChangeLimit);
            if (limit > 0 && c.changeStamp == (int)InsuranceTime.QuadrumIndex() && c.changesThisQuadrum >= limit) return Ins.T("Change.Limit", limit);
            return null;
        }

        public static double SnapSum(double sum) => Math.Round(sum / 50.0) * 50.0;

        /// <summary>Works out a change without doing anything. <see cref="ChangePlan.error"/> says why it is not possible.</summary>
        public ChangePlan PlanChange(PolicyContract c, CoverTier tier, double sum, IEnumerable<string> riders)
        {
            var plan = new ChangePlan { contract = c, newTier = tier };
            var def = c?.Def;
            if (def == null) { plan.error = Ins.T("Change.NotActive"); return plan; }
            var map = InsurerBooks.HomeMap;
            InsurancePricing.SumRange(def, map, c.holder, out var min, out var max);
            sum = Math.Max(min, Math.Min(max, sum));
            if (Math.Abs(sum - c.sum) > 1e-6 && Math.Abs(sum - Math.Round(sum)) > 1e-6) sum = Math.Round(sum);
            plan.newSum = sum;
            plan.newRiders = (riders ?? Enumerable.Empty<string>()).Where(k => def.riders.Any(r => r.key == k)).Distinct().OrderBy(k => def.riders.FindIndex(r => r.key == k)).ToList();
            bool sumUp = plan.newSum > c.sum + 0.5, sumDown = plan.newSum < c.sum - 0.5;
            bool tierUp = tier > c.tier, tierDown = tier < c.tier;
            bool addedRider = plan.newRiders.Any(k => !c.riders.Contains(k)), droppedRider = c.riders.Any(k => !plan.newRiders.Contains(k));
            plan.increase = sumUp || tierUp || addedRider;
            plan.decrease = sumDown || tierDown || droppedRider;
            plan.noChange = !plan.increase && !plan.decrease;
            plan.oldQuote = InsurancePricing.QuoteFor(c);
            plan.newQuote = InsurancePricing.QuoteFor(def, tier, c.holder, plan.newSum, plan.newRiders, Math.Max(1, ActiveCount()), c);
            plan.fee = HSettings.GetInt(InsSettings.ChangeFee);
            plan.waitDays = plan.increase ? HSettings.GetInt(InsSettings.ChangeWaitingDays) : 0;
            plan.effectiveTick = ChangeEffectiveTick(c);
            string why = CanChange(c);
            if (why != null) { plan.error = why; return plan; }
            if (tier != c.tier && !InsurancePricing.TierUnlocked(def, tier, out var lockWhy)) { plan.error = lockWhy; return plan; }
            if (plan.increase && def.kind == PolicyKind.Person && def.ageSensitive && c.holder != null && c.holder.ageTracker.AgeBiologicalYears >= 60) { plan.error = Ins.T("Change.TooOld"); return plan; }
            return plan;
        }

        int ActiveCount() => contracts.Count(x => x.Active);

        /// <summary>The next billing (Standard Room); harder levels: the next quadrum change, like a cancellation.</summary>
        int ChangeEffectiveTick(PolicyContract c)
        {
            int now = Ins.Now;
            int bill = c.nextBillTick >= 0 ? Math.Max(now, c.nextBillTick) : now;
            if (!InsSettings.Hard) return bill;
            int q = now + Math.Max(1, (int)(GenDate.TicksPerQuadrum - InsuranceTime.TicksIntoQuadrum()));
            return Math.Max(bill, q);
        }

        /// <summary>Files the change: charges the fee and parks the new terms until they take effect. Returns false with <paramref name="message"/> when it fails.</summary>
        public bool ApplyChange(ChangePlan plan, out string message)
        {
            message = null;
            if (plan == null || !plan.Ok) { message = plan?.error ?? Ins.T("Change.NoChange"); return false; }
            var c = plan.contract;
            string why = CanChange(c); if (why != null) { message = why; return false; }
            if (plan.fee > 0.0001 && !InsurerBooks.TryPay(InsurerBooks.HomeMap, Ins.M(plan.fee), "Clawback Mutual: change of " + c.Def.label, out _))
            { message = Ins.T("Change.CannotPayFee", Ins.Silver(plan.fee)); return false; }
            int now = Ins.Now, qi = (int)InsuranceTime.QuadrumIndex();
            if (c.changeStamp != qi) { c.changeStamp = qi; c.changesThisQuadrum = 0; }
            c.changesThisQuadrum++;
            c.pendingSum = plan.newSum; c.pendingTier = (int)plan.newTier; c.pendingRiders = new List<string>(plan.newRiders);
            c.pendingEffectiveTick = Math.Max(now, plan.effectiveTick);
            statusVersion++;
            var clause = ClauseEngine.ByTrigger(ClauseTrigger.RiskChange);
            Log(Ins.T("Log.ChangeFiled", c.Label, Ins.Duration(Math.Max(0, c.pendingEffectiveTick - now))));
            string body = Ins.T("Letter.Change.Filed.Body", c.Label, ChangeSummary(plan), Ins.Duration(Math.Max(0, c.pendingEffectiveTick - now)));
            if (plan.fee > 0.0001) body += "\n" + Ins.T("Letter.Change.Fee", Ins.Silver(plan.fee));
            if (plan.increase && plan.waitDays > 0) body += "\n" + Ins.T("Letter.Change.Wait", plan.waitDays) + (clause != null ? " " + clause.Print(InsSettings.Pressure >= 0.6) : "");
            ClaimLetters.Notice(Ins.T("Letter.Change.Filed.Title", c.Label), body);
            InsSounds.Play("SL_PolicyChanged");
            message = Ins.T("Change.Filed", Ins.Duration(Math.Max(0, c.pendingEffectiveTick - now)));
            return true;
        }

        public static string ChangeSummary(ChangePlan p)
        {
            var c = p.contract;
            var parts = new List<string>();
            if (p.newTier != c.tier) parts.Add(Ins.T("Change.Part.Tier", Ins.TierName(c.tier), Ins.TierName(p.newTier)));
            if (Math.Abs(p.newSum - c.sum) > 0.5) parts.Add(Ins.T("Change.Part.Sum", Ins.Silver(c.sum), Ins.Silver(p.newSum)));
            foreach (var k in p.newRiders.Where(k => !c.riders.Contains(k))) parts.Add(Ins.T("Change.Part.RiderOn", ClaimLetters.RiderLabel(c.Def, k)));
            foreach (var k in c.riders.Where(k => !p.newRiders.Contains(k))) parts.Add(Ins.T("Change.Part.RiderOff", ClaimLetters.RiderLabel(c.Def, k)));
            return string.Join("; ", parts);
        }

        /// <summary>Takes a filed change back (the fee is not refunded).</summary>
        public void WithdrawChange(PolicyContract c)
        {
            if (c == null || !c.HasPendingChange) return;
            c.pendingSum = -1; c.pendingTier = -1; c.pendingEffectiveTick = -1; c.pendingRiders = null; statusVersion++;
            Log(Ins.T("Log.ChangeWithdrawn", c.Label));
        }

        /// <summary>The filed change takes effect (called from <see cref="Process"/> just before the billing).</summary>
        public void ApplyPending(PolicyContract c, int now)
        {
            if (c == null || !c.HasPendingChange) return;
            var newRiders = c.pendingRiders ?? new List<string>();
            double newSum = c.pendingSum >= 0 ? c.pendingSum : c.sum;
            var newTier = c.pendingTier >= 0 ? (CoverTier)c.pendingTier : c.tier;
            bool increase = newSum > c.sum + 0.5 || newTier > c.tier || newRiders.Any(k => !c.riders.Contains(k));
            int wait = increase ? HSettings.GetInt(InsSettings.ChangeWaitingDays) : 0;
            // every change keeps the previous terms for events that happened before it (also wait = 0, also a decrease)
            c.prevSum = c.sum; c.prevTier = (int)c.tier; c.prevRiders = new List<string>(c.riders);
            c.changedTick = now;
            c.upgradeWaitUntilTick = increase && wait > 0 ? now + wait * ClaimEngine.Day : -1;
            c.sum = newSum; c.tier = newTier; c.riders = new List<string>(newRiders);
            c.preTickedKept.RemoveAll(k => !c.riders.Contains(k));
            c.pendingSum = -1; c.pendingTier = -1; c.pendingEffectiveTick = -1; c.pendingRiders = null;
            c.changeCount++;
            var q = InsurancePricing.QuoteFor(c);
            c.lastPremiumQuadrum = q.premiumQuadrum;      // our own change must not open the "premium rose" cancellation window
            statusVersion++;
            Log(Ins.T("Log.ChangeApplied", c.Label, Ins.TierName(c.tier), Ins.Silver(c.sum)));
            string price = q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), q.billingDays);
            string body = Ins.T("Letter.Change.Done.Body", c.Label, Ins.TierName(c.tier), Ins.Silver(c.sum), price);
            if (c.upgradeWaitUntilTick > now) body += "\n" + Ins.T("Letter.Change.Done.Wait", Ins.Duration(c.upgradeWaitUntilTick - now));
            ClaimLetters.Notice(Ins.T("Letter.Change.Done.Title", c.Label), body);
        }

        // ---- insuring everybody at once (F25) ---------------------------------------------------------------------------------------
        /// <summary>One policy for one holder (colony policies: no holder) in a bulk purchase.</summary>
        public sealed class BulkItem
        {
            public SL_PolicyDef def;
            public CoverTier tier;
            public Pawn holder;
            public double sum;
            public string skip;                 // reason it cannot be signed (null = fine)
            public double first, billing;       // quote with the progressive bundle count
        }

        public sealed class BulkResult
        {
            public List<PolicyContract> signed = new List<PolicyContract>();
            public List<BulkItem> skipped = new List<BulkItem>();
            public double firstTotal;
            public bool stoppedForMoney;
        }

        /// <summary>Colony policies first, then person policies, oldest colonist first (age-sensitive premiums and the age gate stay stable).</summary>
        static IEnumerable<BulkItem> BulkOrder(IEnumerable<BulkItem> items)
            => items.OrderBy(i => i.def.kind == PolicyKind.Person ? 1 : 0).ThenBy(i => i.def.order)
                    .ThenByDescending(i => i.holder?.ageTracker?.AgeBiologicalYearsFloat ?? 0f).ThenBy(i => i.holder?.thingIDNumber ?? 0);

        /// <summary>Quotes a bulk purchase (progressive bundle discount, reasons for items that cannot be signed). Changes nothing.</summary>
        public List<BulkItem> PlanBulk(IEnumerable<BulkItem> items, out double firstTotal, out double billingTotal)
        {
            firstTotal = billingTotal = 0;
            var list = BulkOrder(items).ToList();
            var map = InsurerBooks.HomeMap;
            int count = ActiveCount();
            foreach (var it in list)
            {
                it.skip = CanSign(it.def, it.tier, it.holder);
                InsurancePricing.SumRange(it.def, map, it.holder, out var min, out var max);
                it.sum = Math.Max(min, Math.Min(max, it.sum));
                if (it.skip != null) { it.first = it.billing = 0; continue; }
                count++;
                var q = InsurancePricing.QuoteFor(it.def, it.tier, it.holder, it.sum, null, count, null);
                it.billing = q.perTravelDay ? 0 : q.billing; it.first = it.billing;
                firstTotal += it.first; billingTotal += it.billing;
            }
            return list;
        }

        /// <summary>
        /// Signs many policies in one go with one summary letter. Stops (and says "k of N signed") when the colony cannot pay the next first premium
        /// instead of failing half-way unnoticed. No pre-ticked add-ons (a hidden sales trick multiplied by twelve colonists would be unfair).
        /// </summary>
        public BulkResult SignMany(IEnumerable<BulkItem> items)
        {
            var res = new BulkResult();
            var list = PlanBulk(items, out _, out _);
            var map = InsurerBooks.HomeMap;
            var signedQuotes = new List<KeyValuePair<PolicyContract, Quote>>();
            string source = null;
            foreach (var it in list)
            {
                if (it.skip != null) { res.skipped.Add(it); continue; }
                if (res.stoppedForMoney) { it.skip = Ins.T("Bulk.NoMoney"); res.skipped.Add(it); continue; }
                var q = InsurancePricing.QuoteFor(it.def, it.tier, it.holder, it.sum, null, InsurancePricing.BundleCountForNew, null);
                if (q.billing > 0 && InsurerBooks.Spendable(map) < Ins.M(q.billing)) { res.stoppedForMoney = true; it.skip = Ins.T("Bulk.NoMoney"); res.skipped.Add(it); continue; }
                var r = Sign(it.def, it.tier, it.holder, it.sum, null, null, true);
                if (!r.Ok) { it.skip = r.error; res.skipped.Add(it); continue; }
                res.signed.Add(r.contract); res.firstTotal += q.billing; source = r.paidFrom;
                signedQuotes.Add(new KeyValuePair<PolicyContract, Quote>(r.contract, r.quote ?? q));
            }
            if (res.signed.Count > 0)
            {
                Log(Ins.T("Log.BulkSigned", res.signed.Count, list.Count));
                ClaimLetters.WelcomeBatch(signedQuotes, res.skipped, source);
                InsSounds.Play("SL_BulkSign");
            }
            return res;
        }
    }
}
