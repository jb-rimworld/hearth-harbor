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

    /// <summary>
    /// "A claim is possible" (1.1.0, F27): the letter of its own kind that tells the colony an insured event can now be claimed, and by when.
    /// Dismissing it does not close the filing window (the claims page lists it with its countdown).
    /// </summary>
    public class ClaimPossibleLetter : ChoiceLetter
    {
        public int oppId;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var o = InsuranceComponent.Instance?.GetOpportunity(oppId);
                if (o == null || ArchivedOnly || !o.Open) { yield return Option_Close; yield break; }
                int id = oppId;
                yield return new DiaOption(Ins.T("Btn.FillForm")) { action = () => Dialog_ClaimForm.Open(id), resolveTree = true };
                yield return new DiaOption(Ins.T("Btn.OpenDesk")) { action = () => { InsuranceUI.ShowClaimsPage(); Window_Clawback.Open(); }, resolveTree = true };
                yield return Option_Postpone;
                yield return new DiaOption(Ins.T("Btn.Dismiss")) { action = () => Find.LetterStack.RemoveLetter(this), resolveTree = true };
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref oppId, "oppId");
        }
    }

    /// <summary>Sounds of the desk. Looked up by name so a missing def never causes an error.</summary>
    public static class InsSounds
    {
        public static void Play(string defName)
        {
            try { var d = DefDatabase<Verse.SoundDef>.GetNamedSilentFail(defName); if (d != null) Verse.Sound.SoundStarter.PlayOneShotOnCamera(d); } catch (Exception) { /* a sound must never break the desk */ }
        }
    }

    /// <summary>All letters of Clawback Mutual: dry, friendly, and always one footnote too small.</summary>
    public static class ClaimLetters
    {
        /// <summary>True for letters of ours: the decision letter class and every letter that uses one of the SL_ letter defs.</summary>
        public static bool IsOurs(Letter l) => l is ClaimLetter || l is ClaimPossibleLetter || (l?.def != null && l.def.defName != null && l.def.defName.StartsWith("SL_"));

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

        /// <summary>One summary letter for a bulk purchase (F25).</summary>
        public static void WelcomeBatch(List<KeyValuePair<PolicyContract, Quote>> signed, List<InsuranceComponent.BulkItem> skipped, string paidFrom)
        {
            if (signed == null || signed.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendLine(Ins.T("Letter.Batch.Body", signed.Count));
            sb.AppendLine();
            double total = 0;
            foreach (var kv in signed)
            {
                var q = kv.Value; double b = q.billing > 0 ? q.billing : 0; total += b;
                sb.AppendLine("• " + kv.Key.Label + " (" + Ins.TierName(kv.Key.tier) + ", " + Ins.Silver(kv.Key.sum) + "): " + (q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), InsSettings.Billing)));
            }
            sb.AppendLine();
            sb.AppendLine(Ins.T("Letter.Batch.Total", Ins.Silver(total), InsSettings.Billing, paidFrom ?? ""));
            sb.AppendLine(Ins.T("Letter.Welcome.Waiting", InsSettings.Waiting));
            if (skipped != null && skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Ins.T("Letter.Batch.Skipped", skipped.Count));
                foreach (var it in skipped.Take(8)) sb.AppendLine("• " + (it.def?.LabelCap ?? "?") + (it.holder != null ? " (" + it.holder.LabelShortCap + ")" : "") + ": " + (it.skip ?? ""));
                if (skipped.Count > 8) sb.AppendLine("…");
            }
            Notice(Ins.T("Letter.Batch.Title", signed.Count), sb.ToString().TrimEnd());
        }

        // ---- filing windows (F27) -----------------------------------------------------------------------------------------------------
        static string DeadlineText(ClaimOpportunity o)
        {
            int left = Math.Max(0, o.deadlineTick - Ins.Now);
            return Ins.T("Letter.Possible.Deadline", Ins.Duration(left), Ins.Duration(left));
        }

        public static void OpportunityPossible(ClaimOpportunity o)
        {
            var comp = InsuranceComponent.Instance; var c = o.Contract; if (comp == null || c == null) return;
            bool neutral = o.kind == OppKind.Death;
            var sb = new StringBuilder();
            sb.AppendLine(neutral ? Ins.T("Letter.Possible.Death", o.pawnName ?? "?", c.Label) : Ins.T("Letter.Possible.Body", o.Summary, c.Label));
            sb.AppendLine();
            sb.AppendLine(DeadlineText(o));
            if (o.eventTick < c.waitUntilTick && !neutral) { sb.AppendLine(); sb.AppendLine(Ins.T("Letter.Possible.Waiting")); }
            sb.AppendLine();
            sb.AppendLine(Ins.T("Letter.Possible.How"));
            string title = Ins.T("Letter.Possible.Title", c.Def?.LabelCap ?? c.Label);
            LookTargets look = o.pawn != null && o.pawn.Spawned ? new LookTargets(o.pawn) : LookTargets.Invalid;
            if (!HSettings.GetBool(InsSettings.ClaimLetters)) { Messages.Message(title, MessageTypeDefOf.NeutralEvent, false); return; }
            comp.letterCounter++;
            var letter = (ClaimPossibleLetter)LetterMaker.MakeLetter(SLInsuranceDefOf.SL_ClaimPossible);
            letter.Label = title; letter.title = title; letter.Text = sb.ToString().TrimEnd() + Footer(neutral);
            letter.oppId = o.id; letter.lookTargets = look; letter.relatedFaction = null;
            Find.LetterStack.ReceiveLetter(letter);
            try { letter.StartTimeout(Math.Max(1, o.deadlineTick - Ins.Now)); } catch (Exception e) { HLog.Warn("Bank", "claim letter timeout: " + e.Message); }
        }

        public static void OpportunityReminder(ClaimOpportunity o)
        {
            var c = o.Contract; if (c == null) return;
            Notice(Ins.T("Letter.Reminder.Title", c.Label), Ins.T("Letter.Reminder.Body", o.Summary, Ins.Duration(Math.Max(0, o.deadlineTick - Ins.Now))), o.kind == OppKind.Death);
            InsSounds.Play("SL_ClaimReminder");
        }

        public static void OpportunityExpired(ClaimOpportunity o)
        {
            var c = o.Contract;
            Notice(Ins.T("Letter.Expired.Title", c?.Label ?? "?"), Ins.T("Letter.Expired.Body", o.Summary, c?.Label ?? "?"), o.kind == OppKind.Death, o.kind != OppKind.Death);
        }

        public static void RemoveOpportunityLetters(int oppId)
        {
            if (Find.LetterStack == null) return;
            foreach (var l in Find.LetterStack.LettersListForReading.OfType<ClaimPossibleLetter>().Where(x => x.oppId == oppId).ToList()) Find.LetterStack.RemoveLetter(l);
        }

        public static string RiderLabel(SL_PolicyDef def, string key) => def?.riders?.FirstOrDefault(r => r.key == key)?.LabelCap ?? key;

        /// <summary>The statement of the form being filed right now (the "claim filed" letter quotes it before the claim object carries it).</summary>
        public static string PendingStatement;

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
            string statement = cl.formText.NullOrEmpty() ? PendingStatement : cl.formText;
            if (!statement.NullOrEmpty()) body += "\n\n" + Ins.T("Letter.Filed.Statement", AiApi.Trim(statement, 120) + (statement.Length > 120 ? "…" : ""));
            LookTargets look = cl.pawn != null && cl.pawn.Spawned ? new LookTargets(cl.pawn) : LookTargets.Invalid;
            Notice(Ins.T("Letter.Filed.Title", cl.id), body, neutral, false, look);
        }

        public static string Explain(Claim cl)
        {
            var sb = new StringBuilder();
            foreach (var l in cl.breakdown) sb.AppendLine("• " + l);
            return sb.ToString().TrimEnd();
        }

        // ---- the decision letter, with an optional AI answer to the claim statement (F27) ---------------------------------------------------
        sealed class AiWait { public float deadline; public string reply; }
        static readonly Dictionary<int, AiWait> aiWaits = new Dictionary<int, AiWait>();
        /// <summary>Real-time seconds the decision letter waits for the AI before it is sent with the offline text (tests shorten it).</summary>
        public static float AiTimeoutSeconds = 8f;

        static ClaimLetters() { HearthGame.OnGameInit += () => aiWaits.Clear(); }

        public static bool AiWaiting(int claimId) => aiWaits.ContainsKey(claimId);

        static bool WantsAi(Claim cl)
            => HSettings.GetBool(InsSettings.FormTextAi) && !cl.formText.NullOrEmpty() && cl.aiReply == null && !aiWaits.ContainsKey(cl.id) && AiApi.Available("claim-letter");

        /// <summary>Real-time poll (every frame, also while paused): delivers a decision letter when the AI has answered or its few seconds are up. Never blocks the game.</summary>
        public static void PollAi()
        {
            if (aiWaits.Count == 0) return;
            var comp = InsuranceComponent.Instance;
            foreach (var kv in aiWaits.ToList())
            {
                if (kv.Value.reply == null && UnityEngine.Time.realtimeSinceStartup < kv.Value.deadline) continue;
                aiWaits.Remove(kv.Key);
                var cl = comp?.GetClaim(kv.Key); if (cl == null) continue;
                if (kv.Value.reply != null) cl.aiReply = kv.Value.reply;
                SendDecision(cl, kv.Value.reply);
            }
        }

        static void StartAi(Claim cl)
        {
            var choices = new Dictionary<string, string>();
            foreach (var a in cl.formAnswers ?? new List<string>()) { int i = a.IndexOf('='); if (i > 0) choices[a.Substring(0, i)] = a.Substring(i + 1); }
            string outcome = cl.outcome == ClaimOutcome.Approved ? "approved" : cl.outcome == ClaimOutcome.Partial ? "partial" : "denied";
            int id = cl.id;
            var wait = new AiWait { deadline = UnityEngine.Time.realtimeSinceStartup + AiTimeoutSeconds };
            aiWaits[id] = wait;
            cl.decisionLetterPending = true;
            bool ok = AiApi.TryClaimLetter(cl.Def?.label ?? cl.policyDef, cl.pawnName ?? "", cl.subject ?? "", outcome, (float)cl.offered, cl.clauseDef ?? "", choices, cl.formText, text =>
            {
                text = ClaimOpportunities.Sanitize(text);
                if (text.NullOrEmpty()) return;
                var claim = InsuranceComponent.Instance?.GetClaim(id); if (claim == null) return;
                claim.aiReply = text;                       // late replies stay visible in the claim details
                if (aiWaits.TryGetValue(id, out var w)) w.reply = text;
            });
            if (!ok) { aiWaits.Remove(id); cl.decisionLetterPending = false; }
        }

        public static void Decision(Claim cl)
        {
            var comp = InsuranceComponent.Instance; if (comp == null) return;
            if (HSettings.GetBool(InsSettings.ClaimLetters) && WantsAi(cl)) { StartAi(cl); if (aiWaits.ContainsKey(cl.id)) return; }
            SendDecision(cl, cl.aiReply);
        }

        /// <summary>Builds and sends the decision letter. <paramref name="aiReply"/> (the answer to the statement) is only a paragraph: figures and outcome stay ours.</summary>
        public static void SendDecision(Claim cl, string aiReply)
        {
            var comp = InsuranceComponent.Instance; if (comp == null) return;
            cl.decisionLetterPending = false;
            if (!cl.Open) return;
            bool neutral = cl.death;
            var sb = new StringBuilder();
            string who = neutral ? Ins.T("Letter.From.Neutral") : Ins.T("Letter.From.Claims");
            sb.AppendLine(who);
            sb.AppendLine();
            if (!aiReply.NullOrEmpty()) { sb.AppendLine(Ins.T("Letter.Decision.Statement", aiReply)); sb.AppendLine(); }
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
