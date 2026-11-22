// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>A decision letter of the Claims Department with buttons: accept, negotiate, open the desk.</summary>
    public class ClaimLetter : ChoiceLetter
    {
        public int claimId;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var claim = InsuranceComponent.Instance?.GetClaim(claimId);
                if (claim == null || ArchivedOnly || !claim.Open) { yield return Option_Close; yield break; }
                if (claim.state == ClaimState.Decided && claim.outcome != ClaimOutcome.Denied)
                {
                    var accept = new DiaOption(Ins.T("Btn.Accept")) { action = () => { InsuranceComponent.Instance?.Accept(claimId); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                    yield return accept;
                }
                if (claim.state == ClaimState.Decided && claim.outcome != ClaimOutcome.Approved && !claim.negotiationClosed && HSettings.GetBool(InsSettings.Negotiation))
                {
                    var neg = new DiaOption(Ins.T("Btn.Negotiate")) { action = () => Find.WindowStack.Add(new Dialog_Negotiate(claimId)), resolveTree = true };
                    yield return neg;
                }
                yield return new DiaOption(Ins.T("Btn.OpenDesk")) { action = () => Window_Clawback.Open(), resolveTree = true };
                yield return Option_Postpone;
                yield return new DiaOption(Ins.T("Btn.Dismiss")) { action = () => Find.LetterStack.RemoveLetter(this), resolveTree = true };
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref claimId, "claimId");
        }
    }

    /// <summary>All letters of Clawback Mutual: dry, friendly, and always one footnote too small.</summary>
    public static class ClaimLetters
    {
        /// <summary>True for letters of ours: the decision letter class and every letter that uses one of the SL_ letter defs.</summary>
        public static bool IsOurs(Letter l) => l is ClaimLetter || (l?.def != null && l.def.defName != null && l.def.defName.StartsWith("SL_"));

        /// <summary>Module removal: takes our letters out of the letter stack and the message archive (a save without the mod cannot read them). Returns how many.</summary>
        public static int RemoveAll()
        {
            int n = 0;
            foreach (var l in Find.LetterStack.LettersListForReading.ToList()) if (IsOurs(l)) { Find.LetterStack.RemoveLetter(l); n++; }
            var arch = Find.Archive;
            if (arch != null) foreach (var a in arch.ArchivablesListForReading.ToList()) if (a is Letter al && IsOurs(al)) { arch.Remove(a); n++; }
            return n;
        }

        public static string Footer(bool neutral)
        {
            if (neutral) return "";
            int i = Ins.Now / 60000 + (InsuranceComponent.Instance?.letterCounter ?? 0);
            return "\n\n<color=#8a96a8><size=11>" + Ins.Slogan(i) + "</size></color>";
        }

        static void Send(string title, string text, LetterDef def, LookTargets look = null, bool neutral = false)
        {
            if (!HSettings.GetBool(InsSettings.ClaimLetters)) { Messages.Message(title, MessageTypeDefOf.NeutralEvent, false); return; }
            var comp = InsuranceComponent.Instance; if (comp != null) comp.letterCounter++;
            Find.LetterStack.ReceiveLetter(title, text + Footer(neutral), def ?? SLInsuranceDefOf.SL_ClawbackNotice, look != null && look.IsValid ? look : LookTargets.Invalid);
        }

        public static void Notice(string title, string text, bool neutral = false, bool bad = false, LookTargets look = null)
            => Send(title, text, bad ? SLInsuranceDefOf.SL_ClawbackBad : SLInsuranceDefOf.SL_ClawbackNotice, look, neutral);

        public static void Welcome(PolicyContract c, Quote q, string paidFrom)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Ins.T("Letter.Welcome.Body", c.Def?.LabelCap, Ins.TierName(c.tier), Ins.Silver(q.billing > 0 ? q.billing : q.travelPerHeadDay), InsSettings.Billing));
            sb.AppendLine();
            sb.AppendLine(Ins.T("Letter.Welcome.Waiting", InsSettings.Waiting));
            if (c.preTickedKept.Count > 0) sb.AppendLine(Ins.T("Letter.Welcome.Riders", string.Join(", ", c.preTickedKept.Select(k => RiderLabel(c.Def, k)))));
            sb.AppendLine(Ins.T("Letter.Welcome.Paid", paidFrom));
            Notice(Ins.T("Letter.Welcome.Title", c.Label), sb.ToString().TrimEnd());
        }

        public static string RiderLabel(SL_PolicyDef def, string key) => def?.riders?.FirstOrDefault(r => r.key == key)?.LabelCap ?? key;

        public static void Filed(Claim cl)
        {
            var comp = InsuranceComponent.Instance; if (comp == null) return;
            int day = Ins.Now / 60000;
            if (comp.lastFiledLetterDay == day) { cl.silentFiled = true; return; }
            comp.lastFiledLetterDay = day;
            bool neutral = cl.death;
            string body = neutral ? Ins.T("Letter.Filed.Death", cl.subject) : Ins.T("Letter.Filed.Body", cl.id, cl.subject, cl.Def?.LabelCap);
            if (cl.trigger == ClaimTrigger.LimbLoss && cl.assessTick > cl.filedTick)
                body += "\n\n" + Ins.T("Letter.Filed.Assessment", (cl.assessTick - cl.filedTick).ToStringTicksToPeriod(), Ins.Silver(cl.offered));
            if (cl.Hint("Waiting")) body += "\n\n" + Ins.T("Letter.Filed.Waiting");
            LookTargets look = cl.pawn != null && cl.pawn.Spawned ? new LookTargets(cl.pawn) : LookTargets.Invalid;
            Notice(Ins.T("Letter.Filed.Title", cl.id), body, neutral, false, look);
        }

        public static string Explain(Claim cl)
        {
            var sb = new StringBuilder();
            foreach (var l in cl.breakdown) sb.AppendLine("• " + l);
            return sb.ToString().TrimEnd();
        }

        public static void Decision(Claim cl)
        {
            var comp = InsuranceComponent.Instance; if (comp == null) return;
            bool neutral = cl.death;
            var sb = new StringBuilder();
            string who = neutral ? Ins.T("Letter.From.Neutral") : Ins.T("Letter.From.Claims");
            sb.AppendLine(who);
            sb.AppendLine();
            string head = cl.outcome == ClaimOutcome.Approved ? Ins.T("Letter.Decision.Approved", Ins.Silver(cl.offered))
                        : cl.outcome == ClaimOutcome.Partial ? Ins.T("Letter.Decision.Partial", Ins.Silver(cl.offered), Ins.Silver(cl.gross))
                        : Ins.T("Letter.Decision.Denied");
            sb.AppendLine(head);
            sb.AppendLine();
            sb.AppendLine(Explain(cl));
            var clause = cl.Clause;
            if (clause != null)
            {
                sb.AppendLine();
                if (neutral) sb.AppendLine(Ins.T("Letter.Clause.Neutral", clause.Ref));
                else
                {
                    bool abbreviated = InsSettings.Pressure >= 0.6;
                    sb.AppendLine(Ins.T("Letter.Clause.Cited", clause.Print(abbreviated)));
                    if (!cl.clauseNote.NullOrEmpty()) sb.AppendLine(cl.clauseNote);
                }
            }
            sb.AppendLine();
            if (cl.outcome == ClaimOutcome.Denied) sb.AppendLine(Ins.T("Letter.Decision.DeniedHow", HSettings.GetBool(InsSettings.Negotiation) ? Ins.T("Letter.Decision.CanNegotiate") : ""));
            else
            {
                int ticks = Math.Max(0, cl.payTick - Ins.Now);
                sb.AppendLine(ticks <= 0 ? Ins.T("Letter.Decision.PayNow") : Ins.T("Letter.Decision.PayLater", ticks.ToStringTicksToPeriod()));
                if (cl.outcome == ClaimOutcome.Partial && HSettings.GetBool(InsSettings.Negotiation)) sb.AppendLine(Ins.T("Letter.Decision.CanNegotiate"));
                if (cl.materials.Count > 0 && cl.tier != CoverTier.BareMinimum) sb.AppendLine(Ins.T("Letter.Decision.InKind"));
            }
            string title = Ins.T(cl.outcome == ClaimOutcome.Approved ? "Letter.Decision.TitleApproved" : cl.outcome == ClaimOutcome.Partial ? "Letter.Decision.TitlePartial" : "Letter.Decision.TitleDenied", cl.id);
            var look = cl.pawn != null && cl.pawn.Spawned ? new LookTargets(cl.pawn) : LookTargets.Invalid;
            if (!HSettings.GetBool(InsSettings.ClaimLetters)) { Messages.Message(title, MessageTypeDefOf.NeutralEvent, false); return; }
            comp.letterCounter++;
            var letter = (ClaimLetter)LetterMaker.MakeLetter(SLInsuranceDefOf.SL_ClawbackLetter);
            letter.Label = title; letter.title = title; letter.Text = sb.ToString().TrimEnd() + Footer(neutral);
            letter.claimId = cl.id; letter.lookTargets = look;
            letter.relatedFaction = null;
            Find.LetterStack.ReceiveLetter(letter);
        }

        public static void Paid(Claim cl, string how)
        {
            bool neutral = cl.death;
            string body = neutral ? Ins.T("Letter.Paid.Neutral", Ins.Silver(cl.paid), how) : Ins.T("Letter.Paid.Body", cl.id, Ins.Silver(cl.paid), how);
            Notice(Ins.T("Letter.Paid.Title", cl.id), body, neutral);
        }

        public static void RemoveFor(int claimId)
        {
            if (Find.LetterStack == null) return;
            foreach (var l in Find.LetterStack.LettersListForReading.OfType<ClaimLetter>().Where(x => x.claimId == claimId).ToList()) Find.LetterStack.RemoveLetter(l);
        }
    }
}
