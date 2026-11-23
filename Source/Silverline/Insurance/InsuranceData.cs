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
    public enum ClaimState { Review = 0, Decided = 1, Paid = 2, Closed = 3 }
    public enum ClaimOutcome { Pending = 0, Approved = 1, Partial = 2, Denied = 3 }

    /// <summary>A policy the player holds with Clawback Mutual. Saved inside <see cref="InsuranceComponent"/>.</summary>
    public sealed class PolicyContract : IExposable
    {
        public int id;
        public string defName;
        public CoverTier tier = CoverTier.GoodEnough;
        public Pawn holder;                 // Person policies
        public string holderName;
        public double sum;                  // silver
        public int startTick, waitUntilTick, nextBillTick = -1, lastBilledTick = -1;
        public int noClaimsClass;
        public int quadrumStamp = -1;
        public bool claimThisQuadrum;
        public List<string> riders = new List<string>();
        public List<string> preTickedKept = new List<string>();   // riders that were pre-ticked and left on (counted as sales-trick cost)
        public int cancelRequestedTick = -1, cancelEffectiveTick = -1;
        public int retentionStage;
        public int discountUntilTick = -1;
        public double discountShare;
        public int specialCancelUntil = -1;
        public double lastPremiumQuadrum;
        public int missedBills;
        public bool ended;
        public int endedTick = -1;
        public string endReason;
        public double preexistingGrade;
        public double hazSamples, hazB, hazC;
        public double lastShareB, lastShareC;
        public double owedClawback;
        public double premiumPaid, benefitPaid, trickCostYear, premiumYear;
        public List<double> damageHistory = new List<double>();   // damage per quadrum, newest last (max 4)
        public double damageThisQuadrum;
        public bool formErrorHit;
        public string payMemo;
        public double travelHeadDays, accrued;
        public int accruedSince = -1, kulanzYear = -1;
        /// <summary>Paid sickbed days of the Rim Health Plan (hospital-indemnity cap): parallel lists of the colonist's thingIDNumber and the game day.</summary>
        public List<int> bedLogPawn = new List<int>(), bedLogDay = new List<int>();

        /// <summary>Counts one more paid sickbed day for this colonist. False (nothing counted) when the colonist already had <paramref name="cap"/> paid days inside the last <paramref name="window"/> days.</summary>
        public bool TryCountBedDay(int pawnId, int day, int cap, int window)
        {
            for (int i = bedLogDay.Count - 1; i >= 0; i--) if (day - bedLogDay[i] >= window || bedLogDay[i] > day) { bedLogDay.RemoveAt(i); bedLogPawn.RemoveAt(i); }
            int n = 0; for (int i = 0; i < bedLogPawn.Count; i++) if (bedLogPawn[i] == pawnId) n++;
            if (n >= cap) return false;
            bedLogPawn.Add(pawnId); bedLogDay.Add(day);
            return true;
        }

        SL_PolicyDef def;
        public SL_PolicyDef Def => def ?? (def = DefDatabase<SL_PolicyDef>.GetNamedSilentFail(defName));
        public bool Active => !ended;
        public bool InWaitingPeriod(int now) => now < waitUntilTick;
        public bool CancelPending => cancelEffectiveTick >= 0 && !ended;
        public string Label => (Def?.LabelCap ?? defName) + (holder != null ? " (" + (holder.LabelShortCap) + ")" : !holderName.NullOrEmpty() ? " (" + holderName + ")" : "");

        /// <summary>Rider effects held by this contract (payoutBoost, fastReview, plusProgression, deathSum).</summary>
        public bool HasEffect(string effect) => Def?.riders != null && riders.Any(k => Def.riders.Any(r => r.key == k && r.effect == effect));
        public double BoostShare => Def?.riders == null ? 0 : riders.Sum(k => Def.riders.Where(r => r.key == k && r.effect == "payoutBoost").Sum(r => (double)r.premiumShare));
        public double HistoryPerQuadrum => damageHistory.Count == 0 ? 0 : damageHistory.Average();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref defName, "def");
            Scribe_Values.Look(ref tier, "tier", CoverTier.GoodEnough);
            Scribe_References.Look(ref holder, "holder");
            Scribe_Values.Look(ref holderName, "holderName");
            Scribe_Values.Look(ref sum, "sum");
            Scribe_Values.Look(ref startTick, "start");
            Scribe_Values.Look(ref waitUntilTick, "waitUntil");
            Scribe_Values.Look(ref nextBillTick, "nextBill", -1);
            Scribe_Values.Look(ref lastBilledTick, "lastBilled", -1);
            Scribe_Values.Look(ref noClaimsClass, "ncc");
            Scribe_Values.Look(ref quadrumStamp, "quadrumStamp", -1);
            Scribe_Values.Look(ref claimThisQuadrum, "claimQ");
            Scribe_Collections.Look(ref riders, "riders", LookMode.Value);
            Scribe_Collections.Look(ref preTickedKept, "preTicked", LookMode.Value);
            Scribe_Values.Look(ref cancelRequestedTick, "cancelReq", -1);
            Scribe_Values.Look(ref cancelEffectiveTick, "cancelEff", -1);
            Scribe_Values.Look(ref retentionStage, "retention");
            Scribe_Values.Look(ref discountUntilTick, "discUntil", -1);
            Scribe_Values.Look(ref discountShare, "disc");
            Scribe_Values.Look(ref specialCancelUntil, "specialUntil", -1);
            Scribe_Values.Look(ref lastPremiumQuadrum, "lastPrem");
            Scribe_Values.Look(ref missedBills, "missed");
            Scribe_Values.Look(ref ended, "ended");
            Scribe_Values.Look(ref endedTick, "endedTick", -1);
            Scribe_Values.Look(ref endReason, "endReason");
            Scribe_Values.Look(ref preexistingGrade, "preGrade");
            Scribe_Values.Look(ref hazSamples, "hazN"); Scribe_Values.Look(ref hazB, "hazB"); Scribe_Values.Look(ref hazC, "hazC");
            Scribe_Values.Look(ref lastShareB, "shareB"); Scribe_Values.Look(ref lastShareC, "shareC");
            Scribe_Values.Look(ref owedClawback, "owed");
            Scribe_Values.Look(ref premiumPaid, "premPaid"); Scribe_Values.Look(ref benefitPaid, "benPaid"); Scribe_Values.Look(ref trickCostYear, "trickYear"); Scribe_Values.Look(ref premiumYear, "premYear");
            Scribe_Collections.Look(ref damageHistory, "dmgHist", LookMode.Value);
            Scribe_Values.Look(ref damageThisQuadrum, "dmgQ");
            Scribe_Values.Look(ref formErrorHit, "formErr");
            Scribe_Values.Look(ref travelHeadDays, "travelDays"); Scribe_Values.Look(ref accrued, "accrued");
            Scribe_Values.Look(ref accruedSince, "accruedSince", -1); Scribe_Values.Look(ref kulanzYear, "kulanzYear", -1);
            Scribe_Collections.Look(ref bedLogPawn, "bedLogPawn", LookMode.Value); Scribe_Collections.Look(ref bedLogDay, "bedLogDay", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                bedLogPawn = bedLogPawn ?? new List<int>(); bedLogDay = bedLogDay ?? new List<int>();
                if (bedLogPawn.Count != bedLogDay.Count) { bedLogPawn.Clear(); bedLogDay.Clear(); }
                riders = riders ?? new List<string>(); preTickedKept = preTickedKept ?? new List<string>(); damageHistory = damageHistory ?? new List<double>();
            }
        }
    }

    /// <summary>A body part reported lost in an accident (kept for the assessment and for clause 22).</summary>
    public sealed class LostPart : IExposable
    {
        public string label, key, comment;
        public double value;
        public int partIndex = -1;
        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label"); Scribe_Values.Look(ref key, "key"); Scribe_Values.Look(ref comment, "comment");
            Scribe_Values.Look(ref value, "value"); Scribe_Values.Look(ref partIndex, "idx", -1);
        }
    }

    /// <summary>A claim at Clawback Mutual: filed → review → decision (letter) → payout.</summary>
    public sealed class Claim : IExposable
    {
        public int id, contractId;
        public string policyDef;
        public CoverTier tier;
        public ClaimTrigger trigger;
        public int filedTick, decideTick, payTick = -1, closedTick = -1, assessTick = -1;
        public ClaimState state;
        public ClaimOutcome outcome;
        public string clauseDef, clauseNote;
        public string subject;                 // "3 wooden walls, a cooler"
        public List<string> items = new List<string>();
        public double lossValue;               // silver, value of what was lost
        public double realValue;               // value of everything insurable, for under-insurance
        public double sumInsured;
        public double gross;                   // benefit before denial / cuts
        public double offered;                 // what is paid if nothing changes
        public double paid;
        public double cutShare = 1.0;          // 1 = full
        public int rounds;
        public bool negotiationClosed;
        public int mapId = -1;
        public Pawn pawn; public string pawnName;
        public double grade, preGrade, paidGrade;
        public bool partialOnly;
        public bool contributed;
        public List<LostPart> lost = new List<LostPart>();
        public List<string> breakdown = new List<string>();
        public List<string> materials = new List<string>();   // "Steel:20" in-kind offer
        public bool materialsChosen;
        public bool death;
        public bool amendedByAssessment;
        public bool silentFiled;
        public bool legalHelped;
        public int bedDays, injuries;
        public string fineReason;
        public List<string> hints = new List<string>();      // clause hints: Waiting, Illness, Weather, Siege, Door, Self, Bionic …
        public int eventTick;

        public bool Hint(string h) => hints != null && hints.Contains(h);
        public void AddHint(string h) { if (hints == null) hints = new List<string>(); if (!hints.Contains(h)) hints.Add(h); }
        public bool Open => state == ClaimState.Review || state == ClaimState.Decided;
        public SL_PolicyDef Def => DefDatabase<SL_PolicyDef>.GetNamedSilentFail(policyDef);
        public SL_ClauseDef Clause => clauseDef.NullOrEmpty() ? null : DefDatabase<SL_ClauseDef>.GetNamedSilentFail(clauseDef);

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref contractId, "contract");
            Scribe_Values.Look(ref policyDef, "policy"); Scribe_Values.Look(ref tier, "tier"); Scribe_Values.Look(ref trigger, "trigger");
            Scribe_Values.Look(ref filedTick, "filed"); Scribe_Values.Look(ref decideTick, "decide"); Scribe_Values.Look(ref payTick, "pay", -1);
            Scribe_Values.Look(ref closedTick, "closed", -1); Scribe_Values.Look(ref assessTick, "assess", -1);
            Scribe_Values.Look(ref state, "state"); Scribe_Values.Look(ref outcome, "outcome");
            Scribe_Values.Look(ref clauseDef, "clause"); Scribe_Values.Look(ref clauseNote, "clauseNote");
            Scribe_Values.Look(ref subject, "subject");
            Scribe_Collections.Look(ref items, "items", LookMode.Value);
            Scribe_Values.Look(ref lossValue, "loss"); Scribe_Values.Look(ref realValue, "real"); Scribe_Values.Look(ref sumInsured, "sum");
            Scribe_Values.Look(ref gross, "gross"); Scribe_Values.Look(ref offered, "offered"); Scribe_Values.Look(ref paid, "paid");
            Scribe_Values.Look(ref cutShare, "cut", 1.0); Scribe_Values.Look(ref rounds, "rounds"); Scribe_Values.Look(ref negotiationClosed, "negClosed");
            Scribe_Values.Look(ref mapId, "map", -1);
            Scribe_References.Look(ref pawn, "pawn"); Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref grade, "grade"); Scribe_Values.Look(ref preGrade, "preGrade"); Scribe_Values.Look(ref paidGrade, "paidGrade");
            Scribe_Values.Look(ref partialOnly, "partialOnly"); Scribe_Values.Look(ref contributed, "contributed");
            Scribe_Collections.Look(ref lost, "lost", LookMode.Deep);
            Scribe_Collections.Look(ref breakdown, "breakdown", LookMode.Value);
            Scribe_Collections.Look(ref materials, "materials", LookMode.Value);
            Scribe_Values.Look(ref materialsChosen, "materialsChosen"); Scribe_Values.Look(ref death, "death");
            Scribe_Values.Look(ref amendedByAssessment, "amended"); Scribe_Values.Look(ref silentFiled, "silent");
            Scribe_Values.Look(ref legalHelped, "legal"); Scribe_Values.Look(ref bedDays, "bedDays"); Scribe_Values.Look(ref injuries, "injuries");
            Scribe_Values.Look(ref fineReason, "fine");
            Scribe_Collections.Look(ref hints, "hints", LookMode.Value);
            Scribe_Values.Look(ref eventTick, "eventTick");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hints = hints ?? new List<string>(); items = items ?? new List<string>(); lost = lost ?? new List<LostPart>(); breakdown = breakdown ?? new List<string>(); materials = materials ?? new List<string>();
            }
        }
    }
}
