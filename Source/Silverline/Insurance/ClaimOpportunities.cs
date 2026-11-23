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
    /// <summary>
    /// Where the damage hooks hand their events (1.1.0, F27). Mode "Automatic": the claim is filed at once, exactly as before (with the cover of the day
    /// of the event, see <see cref="PolicyContract.ClaimView"/>). Mode "By form": the event becomes a <see cref="ClaimOpportunity"/>; the colony has a
    /// deadline to file it, and filing it calls the very same engine methods with the stored event.
    /// </summary>
    public static class ClaimIntake
    {
        public static void Property(PolicyContract c, ClaimTrigger trig, double value, string label, Map map, List<string> hints, List<ThingDefCountClass> cost)
        {
            if (InsSettings.ByForm) { ClaimOpportunities.AddItem(c, trig, OppKind.Property, value, label, map, hints, cost, null); return; }
            using (c.ClaimView(Ins.Now)) ClaimEngine.FileProperty(c, trig, value, label, map, hints, cost, Ins.Now);
        }

        public static void Flat(PolicyContract c, ClaimTrigger trig, double loss, string subject, Map map, List<string> hints)
        {
            if (InsSettings.ByForm) { ClaimOpportunities.AddItem(c, trig, OppKind.Flat, loss, subject, map, hints, null, null); return; }
            using (c.ClaimView(Ins.Now)) ClaimEngine.FileFlat(c, trig, loss, subject, map, null, hints);
        }

        public static void Death(PolicyContract c, Pawn p, bool illness, bool travel)
        {
            if (InsSettings.ByForm) { ClaimOpportunities.AddDeath(c, p, illness, travel); return; }
            using (c.ClaimView(Ins.Now)) ClaimEngine.FileDeath(c, p, illness, travel);
        }

        public static void Limb(PolicyContract c, Pawn p, BodyPartRecord part, bool self)
        {
            if (InsSettings.ByForm) { ClaimOpportunities.AddLimb(c, p, part, self, false); return; }
            using (c.ClaimView(Ins.Now)) { var cl = ClaimEngine.FileLimbLoss(c, p, part); if (self) cl.AddHint("Self"); }
        }

        public static void TravelLimb(PolicyContract c, Pawn p, BodyPartRecord part)
        {
            if (InsSettings.ByForm) { ClaimOpportunities.AddLimb(c, p, part, false, true); return; }
            using (c.ClaimView(Ins.Now)) ClaimEngine.FileTravelLimb(c, p, part);
        }

        /// <summary>True when a claim or an open filing window for this death already exists (no second claim for the same death).</summary>
        public static bool DeathKnown(InsuranceComponent comp, PolicyContract c, Pawn p, bool travel)
            => comp.claims.Any(x => x.contractId == c.id && x.death && (travel ? x.pawn == p : true))
            || comp.opportunities.Any(o => o.contractId == c.id && o.kind == OppKind.Death && o.pawn == p && o.state != OppState.Expired);
    }

    /// <summary>The filing windows of claim mode "By form".</summary>
    public static class ClaimOpportunities
    {
        public const int MergeWindow = 30000, LetterDelay = 2500;

        static InsuranceComponent Comp => InsuranceComponent.Instance;

        /// <summary>Filing deadline in days for this contract: policy value x setting x tier (Premium +50 %, Bare Minimum -30 %), at least one day.</summary>
        public static int DeadlineDays(PolicyContract c)
        {
            var def = c?.Def; if (def == null) return 1;
            double tier = c.tier == CoverTier.Premium ? 1.5 : c.tier == CoverTier.BareMinimum ? 0.7 : 1.0;
            return Math.Max(1, (int)Math.Round(def.claimDeadlineDays * HSettings.Get(InsSettings.DeadlineScale) * tier));
        }

        static ClaimOpportunity Make(PolicyContract c, ClaimTrigger trig, OppKind kind, Map map, int eventTick)
        {
            var comp = Comp;
            var o = new ClaimOpportunity
            {
                id = comp.nextOppId++, contractId = c.id, trigger = trig, kind = kind, state = OppState.Open, eventTick = eventTick, lastItemTick = eventTick,
                mapId = map?.uniqueID ?? -1, deadlineTick = eventTick + DeadlineDays(c) * ClaimEngine.Day
            };
            comp.opportunities.Add(o);
            comp.Log(Ins.T("Log.OppOpened", c.Label, DeadlineDays(c)));
            return o;
        }

        static string CostString(List<ThingDefCountClass> cost)
            => cost == null ? "" : string.Join(";", cost.Where(x => x?.thingDef != null && x.count > 0).Select(x => x.thingDef.defName + ":" + x.count));

        static List<ThingDefCountClass> ParseCost(string s)
        {
            var res = new List<ThingDefCountClass>();
            if (s.NullOrEmpty()) return res;
            foreach (var part in s.Split(';'))
            {
                var sp = part.Split(':'); if (sp.Length != 2) continue;
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(sp[0]); if (d == null || !int.TryParse(sp[1], out int n) || n <= 0) continue;
                res.Add(new ThingDefCountClass(d, n));
            }
            return res;
        }

        /// <summary>A building (Property) or a flat loss (Flat). Items of the same event window are merged into one filing window.</summary>
        public static ClaimOpportunity AddItem(PolicyContract c, ClaimTrigger trig, OppKind kind, double value, string label, Map map, IEnumerable<string> hints, List<ThingDefCountClass> cost, string subject)
        {
            var comp = Comp; int now = Ins.Now; int mapId = map?.uniqueID ?? -1;
            var o = comp.opportunities.FirstOrDefault(x => x.Open && x.contractId == c.id && x.trigger == trig && x.kind == kind && x.mapId == mapId && now - x.lastItemTick < MergeWindow);
            if (o == null) o = Make(c, trig, kind, map, now);
            o.itemLabels.Add(label); o.itemValues.Add(value); o.itemCosts.Add(CostString(cost));
            if (hints != null) foreach (var h in hints) if (!o.hints.Contains(h)) o.hints.Add(h);
            o.lastItemTick = now;
            comp.statusVersion++;
            return o;
        }

        public static ClaimOpportunity AddDeath(PolicyContract c, Pawn p, bool illness, bool travel)
        {
            var comp = Comp;
            // a limb loss of the same colonist that is still waiting for its form is filed now, so that clause 23 (death before the assessment) works as before
            foreach (var lo in comp.opportunities.Where(x => x.Open && x.contractId == c.id && x.kind == OppKind.Limb && x.pawn == p).ToList()) Submit(lo, null, null);
            var o = Make(c, travel ? ClaimTrigger.TravelDeath : ClaimTrigger.PawnDeath, OppKind.Death, p.MapHeld ?? InsurerBooks.HomeMap, Ins.Now);
            o.pawn = p; o.pawnName = p.LabelShortCap; o.illness = illness; o.travel = travel;
            return o;
        }

        public static ClaimOpportunity AddLimb(PolicyContract c, Pawn p, BodyPartRecord part, bool self, bool travel)
        {
            var comp = Comp; int now = Ins.Now;
            var kind = travel ? OppKind.TravelLimb : OppKind.Limb;
            var trig = travel ? ClaimTrigger.TravelLimb : ClaimTrigger.LimbLoss;
            double v = 0;
            if (travel)
            {
                v = LimbGrader.ValueOf(p, part, out bool lethal, out _, out _);
                if (lethal || v <= 0) return null;
            }
            var o = comp.opportunities.FirstOrDefault(x => x.Open && x.contractId == c.id && x.kind == kind && x.pawn == p && now - x.lastItemTick < MergeWindow * 4);
            if (o == null) { o = Make(c, trig, kind, p.MapHeld ?? InsurerBooks.HomeMap, now); o.pawn = p; o.pawnName = p.LabelShortCap; }
            if (!o.partIndices.Contains(part.Index)) { o.partIndices.Add(part.Index); o.itemLabels.Add(LimbGrader.LabelOf(part)); o.itemValues.Add(v); o.itemCosts.Add(""); }
            if (self && !o.hints.Contains("Self")) o.hints.Add("Self");
            o.lastItemTick = now;
            comp.statusVersion++;
            return o;
        }

        // ---- filing -----------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Files the claim for an open window: the same engine calls as in automatic mode, with the stored event time. The form is stored with the claim
        /// (log and claim details) and only its payout choice has an effect. Returns the claims it created, or null with <paramref name="error"/>.
        /// </summary>
        public static List<Claim> Submit(ClaimOpportunity o, Dictionary<string, string> answers, string text, out string error)
        {
            error = null;
            var comp = Comp; var res = new List<Claim>();
            if (o == null || comp == null || !o.Open) { error = Ins.T("Form.Closed"); return null; }
            var c = comp.Get(o.contractId);
            if (c == null) { Expire(o, true); error = Ins.T("Form.Closed"); return null; }
            if (Ins.Now > o.deadlineTick) { Expire(o, false); error = Ins.T("Form.Closed"); return null; }
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == o.mapId) ?? InsurerBooks.HomeMap;
            if (o.kind == OppKind.Death && o.pawn == null)
            {
                HLog.Warn("Insurance", "Death claim window " + o.id + " lost its pawn (after a load?); expired.");
                Expire(o, false); error = Ins.T("Form.Closed"); return null;
            }
            text = Sanitize(text);
            ClaimLetters.PendingStatement = text;
            try
            {
            using (c.ClaimView(o.eventTick))
            {
                switch (o.kind)
                {
                    case OppKind.Property:
                        for (int i = 0; i < o.itemLabels.Count; i++)
                            res.Add(ClaimEngine.FileProperty(c, o.trigger, o.itemValues[i], o.itemLabels[i], map, o.hints, ParseCost(o.itemCosts[i]), o.eventTick));
                        break;
                    case OppKind.Flat:
                        for (int i = 0; i < o.itemLabels.Count; i++)
                            res.Add(ClaimEngine.FileFlat(c, o.trigger, o.itemValues[i], o.itemLabels[i], map, null, o.hints, o.eventTick));
                        break;
                    case OppKind.Death:
                        if (o.pawn != null) res.Add(ClaimEngine.FileDeath(c, o.pawn, o.illness, o.travel, o.eventTick));
                        break;
                    case OppKind.Limb:
                        foreach (var idx in o.partIndices)
                        {
                            var part = o.pawn?.RaceProps?.body?.GetPartAtIndex(idx); if (part == null) continue;
                            var cl = ClaimEngine.FileLimbLoss(c, o.pawn, part, o.eventTick);
                            if (o.hints.Contains("Self")) cl.AddHint("Self");
                            if (!res.Contains(cl)) res.Add(cl);
                        }
                        break;
                    case OppKind.TravelLimb:
                        foreach (var idx in o.partIndices)
                        {
                            var part = o.pawn?.RaceProps?.body?.GetPartAtIndex(idx); if (part == null) continue;
                            var cl = ClaimEngine.FileTravelLimb(c, o.pawn, part, o.eventTick);
                            if (cl != null) res.Add(cl);
                        }
                        break;
                }
            }
            }
            finally { ClaimLetters.PendingStatement = null; }
            res.RemoveAll(x => x == null);
            string answerLog = AnswerSummary(c, answers);
            foreach (var cl in res.Distinct())
            {
                cl.opportunityId = o.id;
                cl.formAnswers = answers == null ? new List<string>() : answers.Select(kv => kv.Key + "=" + kv.Value).ToList();
                cl.formText = text;
                if (answers != null && answers.TryGetValue("payout", out var pay) && pay == "materials" && cl.materials.Count > 0 && cl.tier != CoverTier.BareMinimum) cl.materialsChosen = true;
            }
            o.state = OppState.Filed; o.claimId = res.FirstOrDefault()?.id ?? -1;
            ClaimLetters.RemoveOpportunityLetters(o.id);
            comp.Log(Ins.T("Log.OppFiled", c.Label, res.Count) + (answerLog.NullOrEmpty() ? "" : " " + answerLog) + (text.NullOrEmpty() ? "" : " \"" + AiApi.Trim(text, 60) + "\""));
            comp.statusVersion++;
            InsSounds.Play("SL_ClaimStamp");
            return res;
        }

        /// <summary>Files without form content (module removal, a death that needs its limb claim filed first).</summary>
        public static void Submit(ClaimOpportunity o, Dictionary<string, string> answers, string text) { Submit(o, answers, text, out _); }

        static string AnswerSummary(PolicyContract c, Dictionary<string, string> answers)
        {
            if (answers == null || answers.Count == 0) return "";
            var forms = FormsFor(c);
            var parts = new List<string>();
            foreach (var kv in answers)
            {
                var f = forms.SelectMany(x => x.fields).FirstOrDefault(x => x.key == kv.Key);
                var opt = f?.options?.FirstOrDefault(x => x.key == kv.Value);
                parts.Add((f?.label ?? kv.Key) + ": " + (opt?.label ?? kv.Value));
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        public static string Sanitize(string s)
        {
            if (s.NullOrEmpty()) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s) { if (ch == '\n' || ch == '\r') sb.Append(' '); else if (!char.IsControl(ch) && ch != '<' && ch != '>') sb.Append(ch); }
            return AiApi.Trim(sb.ToString().Trim(), 600);
        }

        /// <summary>The forms whose fields apply to this contract's triggers, in their order.</summary>
        public static List<SL_ClaimFormDef> FormsFor(PolicyContract c, ClaimTrigger? trigger = null)
        {
            var res = DefDatabase<SL_ClaimFormDef>.AllDefsListForReading.Where(f => trigger.HasValue ? f.AppliesTo(trigger.Value) : (c?.Def != null && (f.triggers == null || f.triggers.Count == 0 || c.Def.triggers.Any(f.AppliesTo)))).OrderBy(f => f.order).ToList();
            return res;
        }

        public static void Expire(ClaimOpportunity o, bool silent)
        {
            if (o == null || !o.Open) return;
            o.state = OppState.Expired;
            ClaimLetters.RemoveOpportunityLetters(o.id);
            var comp = Comp; comp?.Log(Ins.T("Log.OppExpired", comp.Get(o.contractId)?.Label ?? "?", o.Summary));
            if (!silent) ClaimLetters.OpportunityExpired(o);
            comp?.TouchStatus();
        }

        public static int OpenCount => Comp?.opportunities.Count(o => o.Open) ?? 0;
    }

    public sealed partial class InsuranceComponent
    {
        public IEnumerable<ClaimOpportunity> OpenOpportunities => opportunities.Where(o => o.Open);
        public ClaimOpportunity GetOpportunity(int id) => opportunities.FirstOrDefault(o => o.id == id);

        /// <summary>Letters, reminders and expiry of the filing windows; also delivers decision letters that waited for the AI and were left behind by a reload.</summary>
        void ProcessOpportunities(int now)
        {
            foreach (var o in opportunities.ToList())
            {
                if (!o.Open) continue;
                var c = Get(o.contractId);
                if (c == null) { ClaimOpportunities.Expire(o, true); continue; }
                if (now >= o.deadlineTick) { ClaimOpportunities.Expire(o, false); continue; }
                if (!o.letterSent && now - o.lastItemTick >= ClaimOpportunities.LetterDelay)
                {
                    var map = Find.Maps.FirstOrDefault(m => m.uniqueID == o.mapId);
                    if (!ThreatNow(map)) { o.letterSent = true; ClaimLetters.OpportunityPossible(o); }
                }
                if (o.letterSent && !o.remindSent && HSettings.GetBool(InsSettings.ClaimReminder) && o.deadlineTick - now <= 2 * ClaimEngine.Day && o.deadlineTick - o.eventTick > 3 * ClaimEngine.Day)
                { o.remindSent = true; ClaimLetters.OpportunityReminder(o); }
            }
            foreach (var cl in claims.Where(x => x.decisionLetterPending).ToList())
                if (!ClaimLetters.AiWaiting(cl.id)) ClaimLetters.SendDecision(cl, null);
        }

        public override void GameComponentUpdate() { if (Modules.On(ModuleId.Bank)) Guard.Run(ModuleId.Bank, "Insurance.PollAi", ClaimLetters.PollAi); }
    }
}
