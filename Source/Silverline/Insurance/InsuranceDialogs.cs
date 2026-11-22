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
    /// <summary>Negotiating a claim (K8): up to three rounds, two or three approaches each, chance-based. On Standard Room it never costs anything.</summary>
    public sealed class Dialog_Negotiate : Window
    {
        readonly int claimId;
        string result = "";
        Pawn negotiator;

        public Dialog_Negotiate(int claimId)
        {
            this.claimId = claimId;
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false;
            negotiator = ClaimEngine.BestNegotiator(InsurerBooks.HomeMap);
        }
        public override Vector2 InitialSize => new Vector2(560f, 470f);

        public override void DoWindowContents(Rect inRect)
        {
            var comp = InsuranceComponent.Instance; var cl = comp?.GetClaim(claimId);
            if (cl == null) { Close(); return; }
            float y = inRect.y;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, y, inRect.width, 32f), Ins.T("Neg.Title", cl.id)); Text.Font = GameFont.Small; y += 34f;
            GUI.color = InsuranceUI.Grey;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 44f), Ins.T("Neg.Lead", cl.subject, Ins.Silver(cl.offered), Ins.Silver(cl.gross))); GUI.color = Color.white; y += 48f;
            if (negotiator != null) { Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), Ins.T("Neg.Negotiator", negotiator.LabelShortCap, ClaimEngine.NegotiationOf(negotiator).ToString("0%"))); }
            else { GUI.color = InsuranceUI.Bad; Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), Ins.T("Neg.NoNegotiator")); GUI.color = Color.white; }
            y += 26f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), Ins.T("Neg.Rounds", 3 - cl.rounds));
            y += 28f;
            bool can = cl.state == ClaimState.Decided && !cl.negotiationClosed && cl.outcome != ClaimOutcome.Approved && negotiator != null;
            foreach (NegApproach a in Enum.GetValues(typeof(NegApproach)))
            {
                double p = ClaimEngine.RoundChance(cl, a, negotiator, out var hint);
                var row = new Rect(inRect.x, y, inRect.width, 54f);
                Widgets.DrawBoxSolid(row, InsuranceUI.BlueDark);
                GUI.color = InsuranceUI.Blue; Widgets.DrawBox(row); GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 8f, row.y + 3f, row.width - 160f, 22f), "<b>" + Ins.T("Neg.Approach." + a) + "</b>");
                GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 8f, row.y + 24f, row.width - 160f, 28f), hint + "  —  " + Ins.T("Neg.Odds", OddsWord(p)));
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(row.xMax - 140f, row.y + 12f, 130f, 30f), Ins.T("Neg.Try"), true, true, can))
                {
                    var res = ClaimEngine.Negotiate(cl, a, negotiator);
                    result = res.text; (res.success ? SoundDefOf.Quest_Accepted : SoundDefOf.Click).PlayOneShotOnCamera();
                }
                y += 60f;
            }
            if (!result.NullOrEmpty())
            {
                GUI.color = InsuranceUI.PaleHi;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 70f), result); GUI.color = Color.white;
            }
            GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, inRect.yMax - 18f, inRect.width, 18f), InsSettings.Hard ? Ins.T("Neg.RiskHard") : Ins.T("Neg.Free"));
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        static string OddsWord(double p) => p < 0.2 ? Ins.T("Neg.Odds.Slim") : p < 0.4 ? Ins.T("Neg.Odds.Fair") : p < 0.65 ? Ins.T("Neg.Odds.Decent") : p < 0.85 ? Ins.T("Neg.Odds.Good") : Ins.T("Neg.Odds.Excellent");
    }

    /// <summary>
    /// The way out (B.6): Desmond Tally's retention offers (one honest dialog on Standard Room, up to three above), then Form C-7.
    /// </summary>
    public sealed class Dialog_CancelPolicy : Window
    {
        readonly int contractId;
        int step;                   // 0.. retention dialogs, then final
        string done;
        public Dialog_CancelPolicy(int id)
        {
            contractId = id;
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false;
            step = 0;
        }
        public override Vector2 InitialSize => new Vector2(540f, 420f);

        public override void DoWindowContents(Rect inRect)
        {
            var comp = InsuranceComponent.Instance; var c = comp?.Get(contractId);
            if (c == null || !c.Active) { Close(); return; }
            float y = inRect.y;
            if (done != null)
            {
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, y, inRect.width, 32f), Ins.T("Cancel.Title")); Text.Font = GameFont.Small; y += 36f;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 160f), done);
                if (Widgets.ButtonText(new Rect(inRect.center.x - 70f, inRect.yMax - 36f, 140f, 30f), Ins.T("Btn.Close"))) Close();
                return;
            }
            var plan = comp.PlanCancel(c);
            int retention = InsSettings.RetentionCount;
            if (step < retention && c.retentionStage < retention)
            {
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, y, inRect.width, 32f), Ins.T("Retention.Title" + (step == 0 ? "" : step == 1 ? "2" : "3"))); Text.Font = GameFont.Small; y += 34f;
                GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(inRect.x, y, inRect.width, 18f), Ins.T("Retention.From")); Text.Font = GameFont.Small; GUI.color = Color.white; y += 22f;
                double disc = InsuranceMath.RetentionDiscount + step * 0.05;
                int quadrums = InsuranceMath.RetentionQuadrums + (InsSettings.Hard ? step : 0);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 150f), Ins.T("Retention.Body" + (step == 0 ? "" : step == 1 ? "2" : "3"), c.Label, Ins.Pct(disc), quadrums));
                if (InsSettings.Hard) { GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(inRect.x, y + 118f, inRect.width, 36f), Ins.T("Retention.MinTerm")); Text.Font = GameFont.Small; GUI.color = Color.white; }
                float by = inRect.yMax - 40f;
                if (Widgets.ButtonText(new Rect(inRect.x, by, inRect.width / 2f - 6f, 32f), Ins.T("Retention.Keep", Ins.Pct(disc)))) { comp.RetentionAccept(c, disc, quadrums); done = Ins.T("Retention.Kept", Ins.Pct(disc), quadrums); }
                if (Widgets.ButtonText(new Rect(inRect.center.x + 6f, by, inRect.width / 2f - 6f, 32f), Ins.T("Retention.Still"))) step++;
                return;
            }
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, y, inRect.width, 32f), Ins.T("Cancel.Title")); Text.Font = GameFont.Small; y += 36f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), c.Label + " · " + Ins.TierName(c.tier)); y += 28f;
            string when = plan.special ? Ins.T("Cancel.When.Special") : Ins.T("Cancel.When", plan.daysUntil, InsSettings.Notice);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 50f), when); y += 54f;
            if (plan.bundleLoss > 0.001) { GUI.color = InsuranceUI.Gold; Widgets.Label(new Rect(inRect.x, y, inRect.width, 44f), Ins.T("Cancel.Bundle", Ins.Pct(plan.bundleLoss), InsSettings.Hard ? Ins.T("Cancel.Bundle.Retro") : Ins.T("Cancel.Bundle.Future"))); GUI.color = Color.white; y += 46f; }
            if (plan.formErrorChance > 0.001 && !plan.special) { GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(inRect.x, y, inRect.width, 36f), Ins.T("Cancel.FormNote", ClauseEngine.ByTrigger(ClauseTrigger.CancelForm)?.Print(InsSettings.Pressure >= 0.6) ?? "")); Text.Font = GameFont.Small; GUI.color = Color.white; y += 36f; }
            if (c.owedClawback > 0.01) { GUI.color = InsuranceUI.Bad; Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), Ins.T("Cancel.Owed", Ins.Silver(c.owedClawback))); GUI.color = Color.white; }
            float by2 = inRect.yMax - 40f;
            if (Widgets.ButtonText(new Rect(inRect.x, by2, inRect.width / 2f - 6f, 32f), Ins.T("Cancel.Back"))) Close();
            if (Widgets.ButtonText(new Rect(inRect.center.x + 6f, by2, inRect.width / 2f - 6f, 32f), Ins.T("Cancel.Confirm"))) done = comp.FileCancel(c) ?? Ins.T("Cancel.Filed", "");
        }
    }
}
