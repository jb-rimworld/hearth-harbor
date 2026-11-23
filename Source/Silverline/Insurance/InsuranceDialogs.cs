// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Silverline
{
    /// <summary>Small drawing helpers shared by the insurance dialogs (1.1.0, F35): heights come from the text, nothing is clipped.</summary>
    static class InsDlg
    {
        public static float TextH(string text, float width, GameFont font = GameFont.Small)
        {
            var f = Text.Font; Text.Font = font;
            float h = Text.CalcHeight(text, width);
            Text.Font = f;
            return h;
        }

        /// <summary>Draws wrapped text at y and returns the height it used.</summary>
        public static float Para(Rect inner, float y, string text, Color color, GameFont font = GameFont.Small, float gap = 6f)
        {
            if (text.NullOrEmpty()) return 0f;
            float h = TextH(text, inner.width, font);
            var f = Text.Font; Text.Font = font; GUI.color = color;
            Widgets.Label(new Rect(inner.x, y, inner.width, h), text);
            GUI.color = Color.white; Text.Font = f;
            return h + gap;
        }

        public static void Title(Rect r, string text, string right = null)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(r.x, r.y, r.width, 34f), text); Text.Font = GameFont.Small;
            if (!right.NullOrEmpty())
            {
                GUI.color = InsuranceUI.Grey; Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(r.x, r.y + 2f, r.width, 30f), right);
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            Widgets.DrawLineHorizontal(r.x, r.y + 36f, r.width, new Color(InsuranceUI.Blue.r, InsuranceUI.Blue.g, InsuranceUI.Blue.b, 0.7f));
        }

        public static void Panel(Rect r, Color border)
        {
            Widgets.DrawBoxSolid(r, InsuranceUI.BlueDark);
            GUI.color = border; Widgets.DrawBox(r); GUI.color = Color.white;
        }

        public static void Chip(Rect r, string text, Color col)
        {
            Widgets.DrawBoxSolid(r, new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.95f));
            GUI.color = col; Widgets.DrawBox(r); Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny;
            Widgets.Label(r, text);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        public static void Frame(Rect inRect)
        {
            var r = new Rect(inRect.x - 8f, inRect.y - 8f, inRect.width + 16f, inRect.height + 16f);
            Widgets.DrawBoxSolid(r, new Color(0.05f, 0.08f, 0.13f, 0.55f));
        }
    }

    /// <summary>Negotiating a claim (K8): up to three rounds, two or three approaches each, chance-based. On Standard Room it never costs anything.</summary>
    public sealed class Dialog_Negotiate : Window
    {
        readonly int claimId;
        string result = "";
        Vector2 resultScroll;
        Pawn negotiator;

        public Dialog_Negotiate(int claimId)
        {
            this.claimId = claimId;
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true;
            negotiator = ClaimEngine.BestNegotiator(InsurerBooks.HomeMap);
        }
        public override Vector2 InitialSize => HUI.Size(680f, 560f, 560f, 470f);

        public override void DoWindowContents(Rect inRect) => Guard.Run(ModuleId.Bank, "Insurance.NegotiateUI", () => Body(inRect));

        void Body(Rect inRect)
        {
            var comp = InsuranceComponent.Instance; var cl = comp?.GetClaim(claimId);
            if (cl == null) { Close(); return; }
            float footerH = InsDlg.TextH(InsSettings.Hard ? Ins.T("Neg.RiskHard") : Ins.T("Neg.Free"), inRect.width, GameFont.Tiny) + 6f;
            var footer = new Rect(inRect.x, inRect.yMax - footerH, inRect.width, footerH);
            var body = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - footerH - 4f);
            float y = body.y;
            int roundsLeft = Math.Max(0, 3 - cl.rounds);
            InsDlg.Title(new Rect(body.x, y, body.width, 36f), Ins.T("Neg.Title", cl.id), Ins.T("Neg.RoundOf", Math.Min(3, cl.rounds + 1), 3));
            y += 42f;
            y += InsDlg.Para(body, y, Ins.T("Neg.Lead", cl.subject, Ins.Silver(cl.offered), Ins.Silver(cl.gross)), InsuranceUI.Grey);
            if (negotiator != null) y += InsDlg.Para(body, y, Ins.T("Neg.Negotiator", negotiator.LabelShortCap, ClaimEngine.NegotiationOf(negotiator).ToString("0%")), Color.white);
            else y += InsDlg.Para(body, y, Ins.T("Neg.NoNegotiator"), InsuranceUI.Bad);
            y += InsDlg.Para(body, y, Ins.T("Neg.Rounds", roundsLeft), InsuranceUI.Grey, GameFont.Small, 8f);

            string cannot = null;
            if (cl.state != ClaimState.Decided) cannot = Ins.T("Neg.Why.NotDecided");
            else if (cl.negotiationClosed) cannot = Ins.T("Neg.Why.Closed");
            else if (cl.outcome == ClaimOutcome.Approved) cannot = Ins.T("Neg.Why.Approved");
            else if (negotiator == null) cannot = Ins.T("Neg.NoNegotiator");
            bool can = cannot == null;
            foreach (NegApproach a in Enum.GetValues(typeof(NegApproach)))
            {
                double p = ClaimEngine.RoundChance(cl, a, negotiator, out var hint);
                float chipW = 96f, btnW = 120f, textW = body.width - chipW - btnW - 40f;
                float hintH = InsDlg.TextH(hint, textW, GameFont.Tiny);
                float rowH = Math.Max(60f, 30f + hintH + 6f);
                var row = new Rect(body.x, y, body.width, rowH);
                InsDlg.Panel(row, InsuranceUI.Blue);
                var f = Text.Font;
                Widgets.Label(new Rect(row.x + 10f, row.y + 4f, textW, 24f), "<b>" + Ins.T("Neg.Approach." + a) + "</b>");
                GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 10f, row.y + 28f, textW, hintH), hint);
                Text.Font = f; GUI.color = Color.white;
                var chip = new Rect(row.xMax - btnW - chipW - 22f, row.y + (rowH - 24f) / 2f, chipW, 24f);
                InsDlg.Chip(chip, OddsWord(p), p < 0.2f ? InsuranceUI.Bad : p < 0.4f ? InsuranceUI.Gold : InsuranceUI.Good);
                TooltipHandler.TipRegion(chip, Ins.T("Neg.OddsTip", Ins.Pct(p)));
                var btn = new Rect(row.xMax - btnW - 10f, row.y + (rowH - 30f) / 2f, btnW, 30f);
                if (Widgets.ButtonText(btn, Ins.T("Neg.Try"), true, true, can))
                {
                    var res = ClaimEngine.Negotiate(cl, a, negotiator);
                    result = res.text; resultScroll = Vector2.zero; (res.success ? SoundDefOf.Quest_Accepted : SoundDefOf.Click).PlayOneShotOnCamera();
                }
                if (!can) TooltipHandler.TipRegion(btn, cannot);
                y += rowH + 6f;
            }
            // result: its own boxed panel in the space that is left
            float resH = body.yMax - y;
            if (!result.NullOrEmpty() && resH > 40f)
            {
                var rp = new Rect(body.x, y, body.width, resH);
                InsDlg.Panel(rp, InsuranceUI.Gold);
                var inner = rp.ContractedBy(8f);
                float th = InsDlg.TextH(result, inner.width - 18f);
                var view = new Rect(0, 0, inner.width - 18f, th);
                Widgets.BeginScrollView(inner, ref resultScroll, view);
                GUI.color = InsuranceUI.PaleHi; Widgets.Label(new Rect(0, 0, view.width, th), result); GUI.color = Color.white;
                Widgets.EndScrollView();
            }
            GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(footer, InsSettings.Hard ? Ins.T("Neg.RiskHard") : Ins.T("Neg.Free"));
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        static string OddsWord(double p) => p < 0.2 ? Ins.T("Neg.Odds.Slim") : p < 0.4 ? Ins.T("Neg.Odds.Fair") : p < 0.65 ? Ins.T("Neg.Odds.Decent") : p < 0.85 ? Ins.T("Neg.Odds.Good") : Ins.T("Neg.Odds.Excellent");
    }

    /// <summary>
    /// The way out (B.6): Desmond Tally's retention offers (one honest dialog on Standard Room, up to three above), then Form C-7.
    /// The state machine is unchanged; the layout (1.1.0) takes its heights from the text and keeps the buttons in a band of their own.
    /// </summary>
    public sealed class Dialog_CancelPolicy : Window
    {
        readonly int contractId;
        int step;                   // 0.. retention dialogs, then final
        Vector2 cancelScroll;
        string done;
        public Dialog_CancelPolicy(int id)
        {
            contractId = id;
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true;
            step = 0;
        }
        public override Vector2 InitialSize => HUI.Size(620f, 480f, 540f, 420f);

        const float BandH = 46f;

        public override void DoWindowContents(Rect inRect) => Guard.Run(ModuleId.Bank, "Insurance.CancelUI", () => Body(inRect));

        void Body(Rect inRect)
        {
            var comp = InsuranceComponent.Instance; var c = comp?.Get(contractId);
            if (c == null || !c.Active) { Close(); return; }
            var band = new Rect(inRect.x, inRect.yMax - BandH, inRect.width, BandH);
            var body = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - BandH - 6f);
            float y = body.y;
            if (done != null)
            {
                InsDlg.Title(new Rect(body.x, y, body.width, 36f), Ins.T("Cancel.Title")); y += 44f;
                InsDlg.Para(body, y, done, Color.white);
                if (Widgets.ButtonText(new Rect(band.center.x - 80f, band.y + 8f, 160f, 32f), Ins.T("Btn.Close"))) Close();
                return;
            }
            var plan = comp.PlanCancel(c);
            int retention = InsSettings.RetentionCount;
            if (step < retention && c.retentionStage < retention)
            {
                string suffix = step == 0 ? "" : step == 1 ? "2" : "3";
                InsDlg.Title(new Rect(body.x, y, body.width, 36f), Ins.T("Retention.Title" + suffix), Ins.T("Cancel.Step", step + 1, retention + 1)); y += 44f;
                y += InsDlg.Para(body, y, Ins.T("Retention.From"), InsuranceUI.Grey, GameFont.Tiny, 8f);
                double disc = InsuranceMath.RetentionDiscount + step * 0.05;
                int quadrums = InsuranceMath.RetentionQuadrums + (InsSettings.Hard ? step : 0);
                y += InsDlg.Para(body, y, Ins.T("Retention.Body" + suffix, c.Label, Ins.Pct(disc), quadrums), Color.white, GameFont.Small, 10f);
                if (InsSettings.Hard) y += InsDlg.Para(body, y, Ins.T("Retention.MinTerm"), InsuranceUI.Grey, GameFont.Tiny);
                if (Widgets.ButtonText(new Rect(band.x, band.y + 6f, band.width / 2f - 6f, 34f), Ins.T("Retention.Keep", Ins.Pct(disc)))) { comp.RetentionAccept(c, disc, quadrums); done = Ins.T("Retention.Kept", Ins.Pct(disc), quadrums); }
                if (Widgets.ButtonText(new Rect(band.center.x + 6f, band.y + 6f, band.width / 2f - 6f, 34f), Ins.T("Retention.Still"))) step++;
                return;
            }
            InsDlg.Title(new Rect(body.x, y, body.width, 36f), Ins.T("Cancel.Title"), retention > 0 ? Ins.T("Cancel.Step", retention + 1, retention + 1) : null); y += 44f;
            string when = plan.special ? Ins.T("Cancel.When.Special") : Ins.T("Cancel.When", plan.daysUntil, InsSettings.Notice);
            var paras = new List<(string text, Color color, GameFont font, float gap)>
            {
                (c.Label + " · " + Ins.TierName(c.tier), Color.white, GameFont.Small, 6f),
                (when, Color.white, GameFont.Small, 8f),
            };
            if (plan.bundleLoss > 0.001) paras.Add((Ins.T("Cancel.Bundle", Ins.Pct(plan.bundleLoss), InsSettings.Hard ? Ins.T("Cancel.Bundle.Retro") : Ins.T("Cancel.Bundle.Future")), InsuranceUI.Gold, GameFont.Small, 8f));
            if (plan.formErrorChance > 0.001 && !plan.special) paras.Add((Ins.T("Cancel.FormNote", ClauseEngine.ByTrigger(ClauseTrigger.CancelForm)?.Print(InsSettings.Pressure >= 0.6) ?? ""), InsuranceUI.Grey, GameFont.Tiny, 8f));
            if (c.owedClawback > 0.01) paras.Add((Ins.T("Cancel.Owed", Ins.Silver(c.owedClawback)), InsuranceUI.Bad, GameFont.Small, 6f));
            // the body scrolls, so a long text never runs into the button band at the smallest window size
            var view = new Rect(body.x, y, body.width, body.yMax - y);
            float innerW = view.width - 18f, total = 0f;
            foreach (var p in paras) total += p.text.NullOrEmpty() ? 0f : InsDlg.TextH(p.text, innerW, p.font) + p.gap;
            var inner = new Rect(0, 0, innerW, total);
            Widgets.BeginScrollView(view, ref cancelScroll, inner);
            float yy = 0f;
            foreach (var p in paras) yy += InsDlg.Para(inner, yy, p.text, p.color, p.font, p.gap);
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(band.x, band.y + 6f, band.width / 2f - 6f, 34f), Ins.T("Cancel.Back"))) Close();
            if (Widgets.ButtonText(new Rect(band.center.x + 6f, band.y + 6f, band.width / 2f - 6f, 34f), Ins.T("Cancel.Confirm"))) done = comp.FileCancel(c) ?? Ins.T("Cancel.Filed", "");
        }
    }

    /// <summary>Changing a running policy (1.1.0, F24): tier, sum and add-ons, with old and new price side by side. Takes effect at the next billing.</summary>
    public sealed class Dialog_ChangePolicy : Window
    {
        readonly int contractId;
        CoverTier tier; double sum; readonly HashSet<string> riders = new HashSet<string>();
        string message; Vector2 scroll;

        public Dialog_ChangePolicy(PolicyContract c)
        {
            contractId = c.id; tier = c.tier; sum = c.sum;
            foreach (var r in c.riders) riders.Add(r);
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; resizeable = true;
        }
        public override Vector2 InitialSize => HUI.Size(760f, 680f, 640f, 560f);

        public static void Open(PolicyContract c)
        {
            foreach (var w in Find.WindowStack.Windows.OfType<Dialog_ChangePolicy>().ToList()) if (w.contractId == c.id) { Find.WindowStack.TryRemove(w, false); }
            Find.WindowStack.Add(new Dialog_ChangePolicy(c));
        }

        public override void DoWindowContents(Rect inRect) => Guard.Run(ModuleId.Bank, "Insurance.ChangeUI", () => Body(inRect));

        void Body(Rect inRect)
        {
            var comp = InsuranceComponent.Instance; var c = comp?.Get(contractId);
            if (c == null || !c.Active || c.Def == null) { Close(); return; }
            var def = c.Def;
            const float bandH = 48f;
            var band = new Rect(inRect.x, inRect.yMax - bandH, inRect.width, bandH);
            var area = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - bandH - 6f);
            InsDlg.Title(new Rect(area.x, area.y, area.width, 36f), Ins.T("Change.Title", c.Label), Ins.TierName(c.tier) + " · " + Ins.Silver(c.sum));
            var view = new Rect(area.x, area.y + 44f, area.width, area.height - 44f);
            float contentW = view.width - 18f;
            var inner = new Rect(0, 0, contentW, 2000f);
            // measure first with a generous height, then draw inside the scroll view
            float total = Draw(c, def, inner, true, comp, out var plan);
            var sv = new Rect(0, 0, contentW, total);
            Widgets.BeginScrollView(view, ref scroll, sv);
            Draw(c, def, new Rect(0, 0, contentW, total), false, comp, out plan);
            Widgets.EndScrollView();

            string why = plan.error ?? (plan.noChange ? Ins.T("Change.NoChange") : null);
            bool ok = plan.Ok;
            if (!message.NullOrEmpty())
            {
                GUI.color = InsuranceUI.Good; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(band.x, band.y, band.width - 330f, band.height), message);
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            else if (why != null)
            {
                GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(band.x, band.y, band.width - 330f, band.height), why);
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            if (Widgets.ButtonText(new Rect(band.xMax - 320f, band.y + 7f, 150f, 34f), Ins.T("Change.Close"))) Close();
            var apply = new Rect(band.xMax - 160f, band.y + 7f, 160f, 34f);
            GUI.color = ok ? Color.white : new Color(1f, 1f, 1f, 0.6f);
            if (Widgets.ButtonText(apply, Ins.T("Change.Apply"), true, true, ok))
            {
                if (comp.ApplyChange(plan, out var msg)) { message = msg; }
                else { message = null; Messages.Message(msg, MessageTypeDefOf.RejectInput, false); }
            }
            GUI.color = Color.white;
            if (!ok && why != null) TooltipHandler.TipRegion(apply, why);
        }

        float Draw(PolicyContract c, SL_PolicyDef def, Rect r, bool measure, InsuranceComponent comp, out ChangePlan plan)
        {
            float x = r.x, w = r.width, y = r.y;
            var map = InsurerBooks.HomeMap;
            // ---- tier ----
            y += InsDlg.Para(r, y, Ins.T("Change.Lead"), InsuranceUI.Grey, GameFont.Small, 8f);
            float colW = (w - 16f) / 3f, colH = 0f;
            var benefitH = new float[3];
            for (int t = 0; t < 3; t++) { benefitH[t] = InsDlg.TextH(def.BenefitFor((CoverTier)t) ?? "", colW - 16f, GameFont.Tiny); colH = Math.Max(colH, benefitH[t]); }
            colH = 24f + 24f + colH + 14f;
            for (int t = 0; t < 3; t++)
            {
                var tr = (CoverTier)t;
                var cr = new Rect(x + t * (colW + 8f), y, colW, colH);
                bool sel = tier == tr, unlocked = InsurancePricing.TierUnlocked(def, tr, out var lockWhy);
                if (!measure)
                {
                    Widgets.DrawBoxSolid(cr, sel ? new Color(InsuranceUI.Blue.r, InsuranceUI.Blue.g, InsuranceUI.Blue.b, 0.45f) : InsuranceUI.BlueMid);
                    GUI.color = sel ? InsuranceUI.Gold : InsuranceUI.Blue; Widgets.DrawBox(cr, sel ? 2 : 1); GUI.color = Color.white;
                    Widgets.Label(new Rect(cr.x + 8f, cr.y + 4f, cr.width - 16f, 22f), "<b>" + Ins.TierName(tr) + "</b>" + (c.tier == tr ? "  ✓" : ""));
                    var q = InsurancePricing.QuoteFor(def, tr, c.holder, sum, riders, Math.Max(1, comp.contracts.Count(k => k.Active)), c);
                    GUI.color = InsuranceUI.Pale;
                    Widgets.Label(new Rect(cr.x + 8f, cr.y + 26f, cr.width - 16f, 22f), q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), q.billingDays));
                    GUI.color = InsuranceUI.PaleHi; Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(cr.x + 8f, cr.y + 50f, cr.width - 16f, benefitH[t]), def.BenefitFor(tr) ?? "");
                    Text.Font = GameFont.Small; GUI.color = Color.white;
                    if (!unlocked) { TooltipHandler.TipRegion(cr, lockWhy); GUI.color = new Color(0, 0, 0, 0.35f); Widgets.DrawBoxSolid(cr, GUI.color); GUI.color = Color.white; }
                    else if (Widgets.ButtonInvisible(cr)) tier = tr;
                    Widgets.DrawHighlightIfMouseover(cr);
                }
            }
            y += colH + 10f;
            // ---- sum ----
            InsurancePricing.SumRange(def, map, c.holder, out double smin, out double smax);
            if (sum < smin) sum = smin; if (sum > smax) sum = smax;
            if (smax > smin + 1)
            {
                if (!measure)
                {
                    string label = def.kind == PolicyKind.Colony ? Ins.T("UI.SumProperty", Ins.Silver(sum)) : Ins.T("UI.SumPerson", Ins.Silver(sum));
                    Widgets.Label(new Rect(x, y, Math.Min(300f, w * 0.45f), 24f), label);
                    var sl = new Rect(x + Math.Min(310f, w * 0.47f), y + 5f, w - Math.Min(310f, w * 0.47f) - 4f, 18f);
                    sum = Math.Round(Widgets.HorizontalSlider(sl, (float)sum, (float)smin, (float)smax, true, null, null, null, 50f) / 50.0) * 50.0;
                    sum = Math.Max(smin, Math.Min(smax, sum));
                }
                y += 30f;
                y += InsDlg.Para(r, y, Ins.T("Change.SumRange", Ins.Silver(smin), Ins.Silver(smax)), InsuranceUI.Grey, GameFont.Tiny, 8f);
            }
            // ---- riders ----
            if (def.riders != null && def.riders.Count > 0)
            {
                y += InsDlg.Para(r, y, "<b>" + Ins.T("Change.Riders") + "</b>", Color.white, GameFont.Small, 2f);
                foreach (var rd in def.riders)
                {
                    bool on = riders.Contains(rd.key);
                    string label = rd.LabelCap + "  (+" + Ins.Pct(rd.premiumShare * (InsSettings.Tier >= 5.5f ? 1.5 : InsSettings.Tier >= 3.5f ? 1.2 : 1.0)) + ")" + (c.riders.Contains(rd.key) ? "  ✓" : "");
                    float lh = Math.Max(24f, InsDlg.TextH(label, w - 40f));
                    if (!measure)
                    {
                        var rr = new Rect(x, y, w, lh);
                        Widgets.CheckboxLabeled(rr, label, ref on);
                        TooltipHandler.TipRegion(rr, rd.description);
                        if (on) riders.Add(rd.key); else riders.Remove(rd.key);
                    }
                    y += lh + 2f;
                }
                y += 6f;
            }
            // ---- summary ----
            plan = comp.PlanChange(c, tier, sum, riders);
            float sumTop = y;
            var lines = SummaryLines(c, plan);
            float sh = 12f;
            foreach (var l in lines) sh += Math.Max(22f, InsDlg.TextH(l.Key, w - 24f, l.Value.size)) + 2f;
            if (!measure)
            {
                var box = new Rect(x, sumTop, w, sh);
                InsDlg.Panel(box, InsuranceUI.Blue);
                float yy = box.y + 6f;
                foreach (var l in lines)
                {
                    float hh = Math.Max(22f, InsDlg.TextH(l.Key, w - 24f, l.Value.size));
                    var f = Text.Font; Text.Font = l.Value.size; GUI.color = l.Value.color;
                    Widgets.Label(new Rect(box.x + 10f, yy, box.width - 20f, hh), l.Key);
                    Text.Font = f; GUI.color = Color.white;
                    yy += hh + 2f;
                }
            }
            y += sh + 8f;
            return y - r.y;
        }

        struct LineStyle { public Color color; public GameFont size; }

        List<KeyValuePair<string, LineStyle>> SummaryLines(PolicyContract c, ChangePlan p)
        {
            var res = new List<KeyValuePair<string, LineStyle>>();
            void Add(string t, Color col, GameFont f = GameFont.Small) => res.Add(new KeyValuePair<string, LineStyle>(t, new LineStyle { color = col, size = f }));
            string oldP = p.oldQuote.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(p.oldQuote.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(p.oldQuote.billing), p.oldQuote.billingDays);
            string newP = p.newQuote.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(p.newQuote.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(p.newQuote.billing), p.newQuote.billingDays);
            Add("<b>" + Ins.T("Change.Sum.Title") + "</b>", Color.white);
            Add(Ins.T("Change.Sum.Premium", oldP, newP), Color.white);
            double delta = p.newQuote.perTravelDay ? p.DeltaPerTravelDay : p.DeltaBilling;
            if (Math.Abs(delta) > 0.004) Add(delta > 0 ? Ins.T("Change.Sum.Dearer", Ins.Silver(delta)) : Ins.T("Change.Sum.Cheaper", Ins.Silver(-delta)), delta > 0 ? InsuranceUI.Bad : InsuranceUI.Good);
            if (!p.noChange)
            {
                Add(Ins.T("Change.Sum.When", Ins.Duration(Math.Max(0, p.effectiveTick - Ins.Now))), InsuranceUI.Pale);
                if (p.fee > 0.0001) Add(Ins.T("Change.Sum.Fee", Ins.Silver(p.fee)), InsuranceUI.Gold);
                if (p.increase && p.waitDays > 0) Add(Ins.T("Change.Sum.Wait", p.waitDays), InsuranceUI.Gold);
                else if (p.decrease && !p.increase) Add(Ins.T("Change.Sum.Lower"), InsuranceUI.Grey, GameFont.Tiny);
                var clause = ClauseEngine.ByTrigger(ClauseTrigger.RiskChange);
                if (clause != null && p.increase) Add(clause.Print(InsSettings.Pressure >= 0.6), InsuranceUI.Grey, GameFont.Tiny);
            }
            if (c.discountUntilTick > Ins.Now) Add(Ins.T("Change.Sum.Discount"), InsuranceUI.Grey, GameFont.Tiny);
            return res;
        }
    }

    /// <summary>The claim form (1.1.0, F27): a few selection fields from the form definitions and a free text. The outcome is decided as always; only the payout choice counts.</summary>
    public sealed class Dialog_ClaimForm : Window
    {
        readonly int oppId;
        readonly Dictionary<string, string> answers = new Dictionary<string, string>();
        string text = "";
        Vector2 scroll, textScroll;
        string error;

        public Dialog_ClaimForm(int oppId)
        {
            this.oppId = oppId;
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; resizeable = true;
        }
        public override Vector2 InitialSize => HUI.Size(760f, 700f, 640f, 560f);

        public static void Open(int oppId)
        {
            foreach (var w in Find.WindowStack.Windows.OfType<Dialog_ClaimForm>().ToList()) if (w.oppId == oppId) { w.error = null; return; }
            Find.WindowStack.Add(new Dialog_ClaimForm(oppId));
        }

        public override void DoWindowContents(Rect inRect) => Guard.Run(ModuleId.Bank, "Insurance.FormUI", () => Body(inRect));

        void Body(Rect inRect)
        {
            var comp = InsuranceComponent.Instance; var o = comp?.GetOpportunity(oppId);
            if (o == null || !o.Open) { Close(); return; }
            var c = o.Contract; if (c == null) { Close(); return; }
            const float bandH = 48f;
            var band = new Rect(inRect.x, inRect.yMax - bandH, inRect.width, bandH);
            var area = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - bandH - 6f);
            int left = Math.Max(0, o.deadlineTick - Ins.Now);
            bool urgent = left < 2 * ClaimEngine.Day;
            InsDlg.Title(new Rect(area.x, area.y, area.width, 36f), Ins.T("Form.Title", c.Def?.LabelCap ?? c.Label));
            var view = new Rect(area.x, area.y + 44f, area.width, area.height - 44f);
            float cw = view.width - 18f;
            float total = Draw(o, c, new Rect(0, 0, cw, 4000f), true, urgent, left);
            Widgets.BeginScrollView(view, ref scroll, new Rect(0, 0, cw, total));
            Draw(o, c, new Rect(0, 0, cw, total), false, urgent, left);
            Widgets.EndScrollView();

            if (!error.NullOrEmpty()) { GUI.color = InsuranceUI.Bad; Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(band.x, band.y, band.width - 340f, band.height), error); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }
            if (Widgets.ButtonText(new Rect(band.xMax - 330f, band.y + 7f, 150f, 34f), Ins.T("Form.Later"))) Close();
            if (Widgets.ButtonText(new Rect(band.xMax - 170f, band.y + 7f, 170f, 34f), Ins.T("Form.Submit")))
            {
                var claims = ClaimOpportunities.Submit(o, new Dictionary<string, string>(answers), text, out var err);
                if (claims == null) error = err;
                else { Messages.Message(Ins.T("Form.Sent", claims.Count), MessageTypeDefOf.PositiveEvent, false); Close(); }
            }
        }

        static void P(Rect r, ref float y, string t, Color col, GameFont f, float gap, bool measure)
        {
            float h = InsDlg.TextH(t, r.width, f);
            if (!measure) InsDlg.Para(r, y, t, col, f, 0f);
            y += h + gap;
        }

        float Draw(ClaimOpportunity o, PolicyContract c, Rect r, bool measure, bool urgent, int left)
        {
            float x = r.x, w = r.width, y = r.y;
            Color grey = InsuranceUI.Grey;
            P(r, ref y, Ins.T("Form.Event", o.Summary, c.Label, Ins.TierName(c.tier)), Color.white, GameFont.Small, 6f, measure);
            P(r, ref y, Ins.T("Form.Deadline", Ins.Duration(left)), urgent ? InsuranceUI.Bad : InsuranceUI.Gold, GameFont.Small, 8f, measure);
            if (o.itemLabels.Count > 1 && (o.kind == OppKind.Property || o.kind == OppKind.Flat))
                P(r, ref y, Ins.T("Form.Items", string.Join(", ", o.itemLabels.Take(6)) + (o.itemLabels.Count > 6 ? ", … (+" + (o.itemLabels.Count - 6) + ")" : ""), Ins.Silver(o.TotalValue)), grey, GameFont.Tiny, 8f, measure);
            else if (o.TotalValue > 0 && o.kind != OppKind.TravelLimb)
                P(r, ref y, Ins.T("Form.Value", Ins.Silver(o.TotalValue)), grey, GameFont.Tiny, 8f, measure);
            // fields
            var forms = ClaimOpportunities.FormsFor(c, o.trigger);
            foreach (var f in forms.SelectMany(fm => fm.fields))
            {
                if (f.kind == ClaimFieldKind.Payout && !(o.kind == OppKind.Property && o.itemCosts.Any(s => !s.NullOrEmpty()) && c.tier != CoverTier.BareMinimum)) continue;
                y += DrawField(f, new Rect(x, y, w, 0), measure, o) + 6f;
            }
            // statement
            P(r, ref y, "<b>" + Ins.T("Form.Statement") + "</b>", Color.white, GameFont.Small, 2f, measure);
            float taH = 190f;
            if (!measure)
            {
                var ta = new Rect(x, y, w, taH);
                Widgets.DrawBoxSolid(ta, new Color(0f, 0f, 0f, 0.3f)); GUI.color = InsuranceUI.Blue; Widgets.DrawBox(ta); GUI.color = Color.white;
                string t2 = Widgets.TextArea(ta.ContractedBy(3f), text);
                if (t2 != null && t2.Length > 600) t2 = t2.Substring(0, 600);
                text = t2 ?? "";
                if (text.NullOrEmpty()) { GUI.color = new Color(1f, 1f, 1f, 0.3f); Widgets.Label(ta.ContractedBy(6f), Ins.T("Form.Placeholder")); GUI.color = Color.white; }
                GUI.color = grey; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperRight; Widgets.Label(new Rect(x, y + taH + 2f, w, 16f), text.Length + " / 600"); Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            y += taH + 20f;
            P(r, ref y, AiApi.Available("claim-letter") && HSettings.GetBool(InsSettings.FormTextAi) ? Ins.T("Form.AiNote") : Ins.T("Form.NoAiNote"), grey, GameFont.Tiny, 4f, measure);
            return y - r.y + 6f;
        }

        float DrawField(ClaimFormField f, Rect r, bool measure, ClaimOpportunity o)
        {
            float w = r.width, lh = Math.Max(22f, InsDlg.TextH("<b>" + f.label + "</b>", w - 4f));
            float h = lh + 4f;
            var optsList = new List<ClaimFieldOption>();
            if (f.kind == ClaimFieldKind.Choice) optsList = f.options;
            else if (f.kind == ClaimFieldKind.Payout) optsList = new List<ClaimFieldOption> { new ClaimFieldOption { key = "silver", label = Ins.T("Form.Payout.Silver") }, new ClaimFieldOption { key = "materials", label = Ins.T("Form.Payout.Materials") } };
            if (f.kind == ClaimFieldKind.Pawn)
            {
                if (!measure)
                {
                    Widgets.Label(new Rect(r.x, r.y, w, lh), "<b>" + f.label + "</b>");
                    string cur = answers.TryGetValue(f.key, out var cv) ? cv : null;
                    var btn = new Rect(r.x, r.y + lh + 2f, Math.Min(260f, w), 26f);
                    if (Widgets.ButtonText(btn, (cur.NullOrEmpty() ? Ins.T("Form.Nobody") : cur) + " ▾"))
                    {
                        var opts = new List<FloatMenuOption> { new FloatMenuOption(Ins.T("Form.Nobody"), () => answers.Remove(f.key)) };
                        foreach (var p in InsurerBooks.HomeMap?.mapPawns?.FreeColonistsSpawned ?? new List<Pawn>()) { var pp = p; opts.Add(new FloatMenuOption(p.LabelShortCap, () => answers[f.key] = pp.LabelShortCap)); }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    if (!f.description.NullOrEmpty()) TooltipHandler.TipRegion(new Rect(r.x, r.y, w, lh + 28f), f.description);
                }
                return h + 30f;
            }
            // chips in rows
            float chipH = 26f, x = 0f, rows = 1f;
            var rects = new List<Rect>();
            foreach (var op in optsList)
            {
                float cwid = Mathf.Max(70f, Text.CalcSize(op.label).x + 22f);
                if (x + cwid > w && x > 0f) { x = 0f; rows++; }
                rects.Add(new Rect(x, (rows - 1f) * (chipH + 4f), cwid, chipH));
                x += cwid + 6f;
            }
            if (!measure)
            {
                Widgets.Label(new Rect(r.x, r.y, w, lh), "<b>" + f.label + "</b>");
                if (!f.description.NullOrEmpty()) TooltipHandler.TipRegion(new Rect(r.x, r.y, w, lh), f.description);
                answers.TryGetValue(f.key, out var cur);
                if (cur == null && f.kind == ClaimFieldKind.Payout) cur = "silver";
                for (int i = 0; i < optsList.Count; i++)
                {
                    var rr = new Rect(r.x + rects[i].x, r.y + lh + 2f + rects[i].y, rects[i].width, chipH);
                    bool sel = cur == optsList[i].key;
                    if (sel) Widgets.DrawBoxSolid(rr, new Color(InsuranceUI.Blue.r, InsuranceUI.Blue.g, InsuranceUI.Blue.b, 0.55f));
                    if (Widgets.ButtonText(rr, optsList[i].label, !sel)) answers[f.key] = optsList[i].key;
                    if (sel) { GUI.color = InsuranceUI.Gold; Widgets.DrawBox(rr); GUI.color = Color.white; }
                }
            }
            return h + rows * (chipH + 4f);
        }
    }
}
