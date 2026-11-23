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
    /// <summary>
    /// "Cover everybody" (1.1.0, F25): pick the policies and the colonists once and sign everything in one go. Quotes use the progressive bundle discount,
    /// the result is one summary letter. No pre-ticked add-ons here.
    /// </summary>
    public sealed class Dialog_CoverEveryone : Window
    {
        sealed class Row { public SL_PolicyDef def; public bool on; public CoverTier tier = CoverTier.GoodEnough; public double sum; }

        readonly List<Row> rows = new List<Row>();
        readonly HashSet<int> excluded = new HashSet<int>();
        List<InsuranceComponent.BulkItem> plan = new List<InsuranceComponent.BulkItem>();
        double firstTotal, billingTotal;
        bool dirty = true; int lastCalc = -9999;
        Vector2 scrollL, scrollR, scrollS;
        string message;

        public Dialog_CoverEveryone(string preselect = null)
        {
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; resizeable = true;
            var comp = InsuranceComponent.Instance; var map = InsurerBooks.HomeMap;
            foreach (var d in InsurancePricing.AllPolicies)
            {
                var row = new Row { def = d, tier = CoverTier.GoodEnough };
                InsurancePricing.SumRange(d, map, null, out _, out _);
                row.sum = InsurancePricing.SuggestSum(d, map, null);
                bool held = d.kind != PolicyKind.Person && comp != null && comp.HasActive(d.defName);
                row.on = d.defName == preselect && !held;
                rows.Add(row);
            }
        }
        public override Vector2 InitialSize => HUI.Size(900f, 720f, 760f, 580f);

        public static void Open(string preselect = null)
        {
            foreach (var w in Find.WindowStack.Windows.OfType<Dialog_CoverEveryone>().ToList()) Find.WindowStack.TryRemove(w, false);
            Find.WindowStack.Add(new Dialog_CoverEveryone(preselect));
        }

        static List<Pawn> Colonists()
        {
            var map = InsurerBooks.HomeMap;
            return InsuranceStatus.AllColonists().OrderByDescending(p => p.ageTracker.AgeBiologicalYearsFloat).ToList();
        }

        List<InsuranceComponent.BulkItem> BuildItems()
        {
            var items = new List<InsuranceComponent.BulkItem>();
            var cols = Colonists().Where(p => !excluded.Contains(p.thingIDNumber)).ToList();
            foreach (var r in rows.Where(x => x.on))
            {
                if (r.def.kind == PolicyKind.Person) foreach (var p in cols) items.Add(new InsuranceComponent.BulkItem { def = r.def, tier = r.tier, holder = p, sum = r.sum });
                else items.Add(new InsuranceComponent.BulkItem { def = r.def, tier = r.tier, holder = null, sum = r.sum });
            }
            return items;
        }

        void Recalc(InsuranceComponent comp)
        {
            plan = comp.PlanBulk(BuildItems(), out firstTotal, out billingTotal);
            dirty = false; lastCalc = Find.TickManager?.TicksGame ?? 0;
        }

        public override void DoWindowContents(Rect inRect) => Guard.Run(ModuleId.Bank, "Insurance.BulkUI", () => Body(inRect));

        void Body(Rect inRect)
        {
            var comp = InsuranceComponent.Instance;
            if (comp == null) { Close(); return; }
            if (dirty || (Find.TickManager?.TicksGame ?? 0) - lastCalc > 300) Recalc(comp);
            const float bandH = 48f, sumH = 170f;
            var band = new Rect(inRect.x, inRect.yMax - bandH, inRect.width, bandH);
            InsDlg.Title(new Rect(inRect.x, inRect.y, inRect.width, 36f), Ins.T("Bulk.Title"), Ins.T("Bulk.Spendable", Ins.Silver(InsurerBooks.Spendable(InsurerBooks.HomeMap).Silver)));
            float top = inRect.y + 42f;
            float lead = InsDlg.Para(new Rect(inRect.x, top, inRect.width, 0), top, Ins.T("Bulk.Lead"), InsuranceUI.Grey, GameFont.Small, 6f);
            top += lead;
            var middle = new Rect(inRect.x, top, inRect.width, band.y - sumH - top - 8f);
            var cols = HUI.Cols(middle, 10f, 0.58f, 0.42f);
            DrawPolicies(cols[0], comp);
            DrawColonists(cols[1]);
            DrawSummary(new Rect(inRect.x, band.y - sumH - 4f, inRect.width, sumH), comp);

            int signable = plan.Count(i => i.skip == null);
            if (!message.NullOrEmpty()) { GUI.color = InsuranceUI.Good; Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(band.x, band.y, band.width - 340f, band.height), message); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }
            if (Widgets.ButtonText(new Rect(band.xMax - 330f, band.y + 7f, 150f, 34f), Ins.T("Btn.Close"))) Close();
            var go = new Rect(band.xMax - 170f, band.y + 7f, 170f, 34f);
            string why = signable == 0 ? Ins.T("Bulk.NothingToSign") : null;
            if (Widgets.ButtonText(go, Ins.T("Bulk.Sign", signable), true, true, why == null))
            {
                int n = signable; double total = firstTotal;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(Ins.T("Bulk.Confirm", n, Ins.Silver(total), Ins.Silver(billingTotal), InsSettings.Billing), () =>
                {
                    var res = comp.SignMany(BuildItems());
                    message = Ins.T("Bulk.Done", res.signed.Count, res.signed.Count + res.skipped.Count) + (res.stoppedForMoney ? " " + Ins.T("Bulk.Stopped") : "");
                    if (res.signed.Count > 0) Messages.Message(message, MessageTypeDefOf.PositiveEvent, false);
                    dirty = true;
                }));
            }
            if (why != null) TooltipHandler.TipRegion(go, why);
        }

        void DrawPolicies(Rect r, InsuranceComponent comp)
        {
            InsDlg.Panel(r, InsuranceUI.Blue);
            var inner = r.ContractedBy(8f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "<b>" + Ins.T("Bulk.Policies") + "</b>");
            var view = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
            float cw = view.width - 18f;
            float total = 0f;
            foreach (var row in rows) total += RowHeight(row, cw) + 6f;
            Widgets.BeginScrollView(view, ref scrollL, new Rect(0, 0, cw, total));
            float y = 0f;
            foreach (var row in rows) { float h = RowHeight(row, cw); DrawRow(new Rect(0, y, cw, h), row, comp); y += h + 6f; }
            Widgets.EndScrollView();
        }

        float RowHeight(Row row, float w)
        {
            if (!row.on) return 30f;
            InsurancePricing.SumRange(row.def, InsurerBooks.HomeMap, null, out var mn, out var mx);
            return 30f + 30f + (mx > mn + 1 ? 28f : 0f) + 6f;
        }

        void DrawRow(Rect r, Row row, InsuranceComponent comp)
        {
            var def = row.def;
            bool held = def.kind != PolicyKind.Person && comp.HasActive(def.defName);
            Widgets.DrawBoxSolid(r, row.on ? InsuranceUI.BlueMid : new Color(0f, 0f, 0f, 0.15f));
            bool on = row.on;
            string kind = def.kind == PolicyKind.Person ? Ins.T("Bulk.PerPerson") : Ins.T("Bulk.Colony");
            var lab = new Rect(r.x + 6f, r.y + 3f, r.width - 12f, 24f);
            if (held) { GUI.color = InsuranceUI.Grey; Widgets.Label(lab, def.LabelCap + "  ✓ " + Ins.T("UI.Signed")); GUI.color = Color.white; row.on = false; return; }
            Widgets.CheckboxLabeled(lab, def.LabelCap + "  <color=#8a96a8>(" + kind + ")</color>", ref on);
            TooltipHandler.TipRegion(lab, def.description);
            if (on != row.on) { row.on = on; dirty = true; }
            if (!row.on) return;
            float y = r.y + 30f;
            float cw = (r.width - 12f - 12f) / 3f;
            for (int t = 0; t < 3; t++)
            {
                var tr = (CoverTier)t; bool unlocked = InsurancePricing.TierUnlocked(def, tr, out var why);
                var b = new Rect(r.x + 6f + t * (cw + 6f), y, cw, 26f);
                bool sel = row.tier == tr;
                if (sel) Widgets.DrawBoxSolid(b, new Color(InsuranceUI.Blue.r, InsuranceUI.Blue.g, InsuranceUI.Blue.b, 0.6f));
                if (Widgets.ButtonText(b, Ins.TierName(tr), !sel, true, unlocked)) { row.tier = tr; dirty = true; }
                if (sel) { GUI.color = InsuranceUI.Gold; Widgets.DrawBox(b); GUI.color = Color.white; }
                if (!unlocked) TooltipHandler.TipRegion(b, why);
            }
            y += 30f;
            InsurancePricing.SumRange(def, InsurerBooks.HomeMap, null, out var mn, out var mx);
            if (mx > mn + 1)
            {
                string l = def.kind == PolicyKind.Person ? Ins.T("Bulk.SumPerHead", Ins.Silver(row.sum)) : Ins.T("UI.SumProperty", Ins.Silver(row.sum));
                Widgets.Label(new Rect(r.x + 6f, y, 230f, 24f), l);
                double ns = Math.Round(Widgets.HorizontalSlider(new Rect(r.x + 240f, y + 5f, r.width - 252f, 18f), (float)row.sum, (float)mn, (float)mx, true, null, null, null, 50f) / 50.0) * 50.0;
                ns = Math.Max(mn, Math.Min(mx, ns));
                if (Math.Abs(ns - row.sum) > 0.5) { row.sum = ns; dirty = true; }
            }
        }

        void DrawColonists(Rect r)
        {
            InsDlg.Panel(r, InsuranceUI.Blue);
            var inner = r.ContractedBy(8f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 150f, 24f), "<b>" + Ins.T("Bulk.Who") + "</b>");
            var cols = Colonists();
            if (Widgets.ButtonText(new Rect(inner.xMax - 146f, inner.y, 70f, 22f), Ins.T("Bulk.All"))) { excluded.Clear(); dirty = true; }
            if (Widgets.ButtonText(new Rect(inner.xMax - 72f, inner.y, 70f, 22f), Ins.T("Bulk.None"))) { foreach (var p in cols) excluded.Add(p.thingIDNumber); dirty = true; }
            var view = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
            float cw = view.width - 18f;
            var personRows = rows.Where(x => x.on && x.def.kind == PolicyKind.Person).ToList();
            Widgets.BeginScrollView(view, ref scrollR, new Rect(0, 0, cw, cols.Count * 26f + 4f));
            float y = 0f;
            foreach (var p in cols)
            {
                bool inc = !excluded.Contains(p.thingIDNumber);
                var rr = new Rect(0, y, cw, 24f);
                string label = p.LabelShortCap + "  <color=#8a96a8>" + p.ageTracker.AgeBiologicalYears + " y</color>";
                bool was = inc;
                Widgets.CheckboxLabeled(rr, label, ref inc);
                if (inc != was) { if (inc) excluded.Remove(p.thingIDNumber); else excluded.Add(p.thingIDNumber); dirty = true; }
                // reasons: why this colonist cannot be signed for a ticked policy
                var reasons = plan.Where(i => i.holder == p && i.skip != null).Select(i => i.def.LabelCap + ": " + i.skip).ToList();
                if (reasons.Count > 0 && inc) { GUI.color = InsuranceUI.Gold; Text.Anchor = TextAnchor.MiddleRight; Widgets.Label(new Rect(rr.x, rr.y, rr.width - 4f, rr.height), "⚠"); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; TooltipHandler.TipRegion(rr, string.Join("\n", reasons)); }
                y += 26f;
            }
            Widgets.EndScrollView();
        }

        void DrawSummary(Rect r, InsuranceComponent comp)
        {
            InsDlg.Panel(r, InsuranceUI.Gold);
            var inner = r.ContractedBy(8f);
            int ok = plan.Count(i => i.skip == null), skipped = plan.Count(i => i.skip != null);
            double spend = InsurerBooks.Spendable(InsurerBooks.HomeMap).Silver;
            string head = Ins.T("Bulk.Summary", ok, Ins.Silver(firstTotal), Ins.Silver(billingTotal), InsSettings.Billing);
            float hh = InsDlg.TextH(head, inner.width);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, hh), head);
            float y = inner.y + hh + 4f;
            if (firstTotal > spend + 0.001) y += InsDlg.Para(new Rect(inner.x, 0, inner.width, 0), y, Ins.T("Bulk.TooExpensive", Ins.Silver(spend)), InsuranceUI.Bad, GameFont.Small, 2f);
            y += InsDlg.Para(new Rect(inner.x, 0, inner.width, 0), y, Ins.T("Bulk.NoRiders", Ins.Pct(1 - InsuranceMath.BundleFactor(4))), InsuranceUI.Grey, GameFont.Tiny, 2f);
            if (skipped > 0)
            {
                var sk = new Rect(inner.x, y, inner.width, inner.yMax - y);
                var lines = plan.Where(i => i.skip != null).Select(i => "• " + i.def.LabelCap + (i.holder != null ? " (" + i.holder.LabelShortCap + ")" : "") + ": " + i.skip).ToList();
                float th = lines.Sum(l => InsDlg.TextH(l, sk.width - 18f, GameFont.Tiny));
                Widgets.BeginScrollView(sk, ref scrollS, new Rect(0, 0, sk.width - 18f, th));
                float yy = 0f; Text.Font = GameFont.Tiny; GUI.color = InsuranceUI.Grey;
                foreach (var l in lines) { float lh = InsDlg.TextH(l, sk.width - 18f, GameFont.Tiny); Widgets.Label(new Rect(0, yy, sk.width - 18f, lh), l); yy += lh; }
                Text.Font = GameFont.Small; GUI.color = Color.white;
                Widgets.EndScrollView();
            }
        }
    }
}
