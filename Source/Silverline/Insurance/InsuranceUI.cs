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
    /// The Clawback Mutual desk (N51): friendly blue, a Premium column that glows, boxes that are already ticked for you,
    /// an easy "Sign now" and a very small "Cancel policy". Drawn the same way inside the bank window tab and in the stand-alone window.
    /// </summary>
    public static class InsuranceUI
    {
        enum Page { Shop, Mine, Claims, FinePrint, Cover }
        static readonly Page[] TabOrder = { Page.Shop, Page.Mine, Page.Cover, Page.Claims, Page.FinePrint };

        sealed class CardState
        {
            public HashSet<string> riders = new HashSet<string>();
            public HashSet<string> preTicked = new HashSet<string>();
            public Pawn holder;
            public double sum = -1;
            public bool fine, customize, init;
        }

        static Page page = Page.Shop;
        static Vector2 scrollShop, scrollMine, scrollClaims, scrollFine, scrollCover;
        static readonly Dictionary<string, CardState> cards = new Dictionary<string, CardState>();
        static readonly HashSet<int> expandedClaims = new HashSet<int>();

        // palette: Clawback Mutual blue in its own window; inside the bank window the lobby look (walnut, brass, green) so the bank stays one place (KONZEPT 8.4)
        [ThreadStatic] static bool bankSkin;
        public static Color Blue => bankSkin ? BankUI.GreenLight : new Color(0.20f, 0.42f, 0.74f);
        public static Color BlueDark => bankSkin ? new Color(BankUI.Green.r, BankUI.Green.g, BankUI.Green.b, 0.92f) : new Color(0.09f, 0.15f, 0.25f, 0.92f);
        public static Color BlueMid => bankSkin ? BankUI.WalnutLight : new Color(0.14f, 0.24f, 0.40f, 0.92f);
        public static Color Gold => bankSkin ? BankUI.Brass : new Color(0.96f, 0.78f, 0.25f);
        public static Color Grey => bankSkin ? BankUI.CreamDim : new Color(0.62f, 0.66f, 0.72f);
        public static Color Good => bankSkin ? BankUI.Good : new Color(0.45f, 0.82f, 0.52f);
        public static Color Bad => bankSkin ? BankUI.Bad : new Color(0.92f, 0.45f, 0.40f);
        /// <summary>Light text tint (was a pale blue).</summary>
        public static Color Pale => bankSkin ? BankUI.Cream : new Color(0.78f, 0.86f, 0.97f);
        public static Color PaleHi => bankSkin ? BankUI.Cream : new Color(0.9f, 0.92f, 0.96f);
        public static Color PaleDim => bankSkin ? BankUI.CreamDim : new Color(0.5f, 0.52f, 0.56f);

        /// <summary>Draw inside the bank window: same pages, the bank's colours.</summary>
        public static void DrawInBank(Rect rect)
        {
            bool was = bankSkin; bankSkin = true;
            try { Draw(rect); } finally { bankSkin = was; }
        }

        public static Texture2D Logo => SilverlineTex.ClawbackLogo;

        /// <summary>Selects the page of the desk (0 policies, 1 my policies, 2 claims, 3 fine print) and optionally expands cards. Used by tests and screenshots.</summary>
        public static void ShowPage(int p, bool expandAll = false)
        {
            page = (Page)Math.Max(0, Math.Min(4, p));
            if (expandAll) foreach (var d in InsurancePricing.AllPolicies) Card(d).fine = true;
            scrollShop = scrollMine = scrollClaims = scrollFine = scrollCover = Vector2.zero;
        }
        public static void ShowClaimsPage() { page = Page.Claims; scrollClaims = Vector2.zero; }
        /// <summary>Opens the desk at the colonists overview (status per pawn) and optionally keeps the pawn in mind.</summary>
        public static void OpenOverview(Pawn p) { page = Page.Cover; Window_Clawback.Open(); }
        /// <summary>Opens the desk at the shop card of a policy with the colonist preselected (the "Buy" button of the pawn tab).</summary>
        public static void OpenShopFor(SL_PolicyDef def, Pawn holder)
        {
            page = Page.Shop; scrollShop = Vector2.zero;
            var cs = Card(def); if (holder != null && def.kind == PolicyKind.Person) cs.holder = holder;
            // bring the card to the top of the list
            float y = 0f; foreach (var p in InsurancePricing.AllPolicies) { if (p == def) break; y += CardHeight(p, Card(p), 800f) + 8f; }
            scrollShop.y = y + 50f;
            Window_Clawback.Open();
        }
        public static void ScrollShop(float y) { scrollShop.y = y; }

        // ---- entry --------------------------------------------------------------------------------------------------------------------
        public static void Draw(Rect rect)
        {
            Guard.Run(ModuleId.Bank, "Insurance.UI", () => DrawInner(rect));
        }

        static void DrawInner(Rect rect)
        {
            var comp = InsuranceComponent.Instance;
            if (comp == null) { Widgets.Label(rect, Ins.T("UI.NoGame")); return; }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            float y = rect.y;
            y += DrawHeader(new Rect(rect.x, y, rect.width, 74f), comp);
            y += 4f;
            DrawTabs(new Rect(rect.x, y, rect.width, 28f), comp); y += 32f;
            var body = new Rect(rect.x, y, rect.width, rect.yMax - y);
            switch (page)
            {
                case Page.Shop: DrawShop(body, comp); break;
                case Page.Mine: DrawMine(body, comp); break;
                case Page.Claims: DrawClaims(body, comp); break;
                case Page.Cover: DrawCover(body, comp); break;
                default: DrawFinePrint(body); break;
            }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        // ---- header -----------------------------------------------------------------------------------------------------------------------
        static float DrawHeader(Rect r, InsuranceComponent comp)
        {
            Widgets.DrawBoxSolid(r, BlueDark);
            GUI.color = Blue; Widgets.DrawBox(r); GUI.color = Color.white;
            var logoRect = new Rect(r.x + 8f, r.y + 5f, 64f, 64f);
            if (Logo != null) GUI.DrawTexture(logoRect, Logo);
            else { Widgets.DrawBoxSolid(logoRect, Blue); Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(logoRect, "CM"); Text.Anchor = TextAnchor.UpperLeft; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x + 82f, r.y + 4f, r.width - 330f, 32f), "Clawback Mutual");
            Text.Font = GameFont.Small;
            GUI.color = Pale;
            Widgets.Label(new Rect(r.x + 82f, r.y + 34f, r.width - 330f, 24f), "“" + Ins.Slogan(Ins.Now / 60000) + "”");
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(r.x + 82f, r.y + 54f, r.width - 330f, 18f), Ins.T("UI.Footer"));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            // account line
            var map = InsurerBooks.HomeMap;
            var right = new Rect(r.xMax - 240f, r.y + 6f, 232f, r.height - 12f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(right.x, right.y, right.width, 22f), Ins.T("UI.Spendable", Ins.Silver(InsurerBooks.Spendable(map).Silver)));
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(right.x, right.y + 22f, right.width, 18f), Ins.T("UI.PayFrom", InsurerBooks.SourceLabel(map)));
            double perDay = comp.Active.Sum(c => { var q = InsurancePricing.QuoteFor(c); return q.billing / Math.Max(1, q.billingDays); });
            Widgets.Label(new Rect(right.x, right.y + 38f, right.width, 18f), Ins.T("UI.Running", Ins.Silver(perDay)));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            return r.height;
        }

        static void DrawTabs(Rect r, InsuranceComponent comp)
        {
            int openClaims = comp.OpenClaims.Count() + comp.OpenOpportunities.Count();
            var labels = new Dictionary<Page, string>
            {
                { Page.Shop, Ins.T("Tab.Shop") }, { Page.Mine, Ins.T("Tab.Mine", comp.Active.Count()) }, { Page.Cover, Ins.T("Tab.Cover") },
                { Page.Claims, Ins.T("Tab.Claims", openClaims) }, { Page.FinePrint, Ins.T("Tab.FinePrint") }
            };
            float need = 0f; foreach (var pg in TabOrder) need += Text.CalcSize(labels[pg]).x + 30f + 4f;
            float w = Math.Min(190f, (r.width - 4f * TabOrder.Length) / TabOrder.Length);
            bool fit = need <= r.width;
            float x = r.x;
            for (int i = 0; i < TabOrder.Length; i++)
            {
                var pg = TabOrder[i]; string lab = labels[pg];
                float bw = fit ? Text.CalcSize(lab).x + 30f : w;
                var br = new Rect(x, r.y, bw, r.height);
                bool sel = page == pg;
                if (sel) { Widgets.DrawBoxSolid(br, Blue); }
                if (Widgets.ButtonText(br, lab, !sel)) { page = pg; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                if (pg == Page.Claims && comp.OpenOpportunities.Any()) { GUI.color = Gold; Widgets.DrawBox(br); GUI.color = Color.white; }
                x += bw + 4f;
            }
        }

        // ---- shop ---------------------------------------------------------------------------------------------------------------------------
        static CardState Card(SL_PolicyDef def)
        {
            if (!cards.TryGetValue(def.defName, out var cs)) cards[def.defName] = cs = new CardState();
            if (!cs.init)
            {
                cs.init = true;
                var riders = def.riders ?? new List<PolicyRider>();
                int n = InsSettings.PreTickedCount(riders.Count);
                for (int i = 0; i < n; i++) { cs.riders.Add(riders[i].key); cs.preTicked.Add(riders[i].key); }
            }
            return cs;
        }

        /// <summary>Height of the tier columns: title, price, 3 lines of benefit, 3 lines of mechanics and the button (B5.1: the old 176 cut the last lines).</summary>
        const float TierColH = 200f;

        /// <summary>The "Covers:" paragraph grows with its text instead of being clipped at two lines.</summary>
        static float CoversHeight(SL_PolicyDef def, float width)
        {
            var f = Text.Font; Text.Font = GameFont.Small;
            float h = Text.CalcHeight(Ins.T("UI.Covers") + " " + def.coversText, width);
            Text.Font = f;
            return Math.Max(40f, h + 2f);
        }

        static float CardHeight(SL_PolicyDef def, CardState cs, float width)
        {
            float h = 30f + CoversHeight(def, width - 20f) + TierColH + 8f;
            h += 30f; // sum / holder row
            int rn = def.riders?.Count ?? 0;
            bool showRiders = rn > 0 && (!InsSettings.Hard || cs.customize || InsSettings.Pressure < 0.6);
            if (rn > 0) h += showRiders ? 26f * rn + 6f : 24f;
            h += 24f;
            if (cs.fine) h += 118f;
            return h + 12f;
        }

        static void DrawShop(Rect r, InsuranceComponent comp)
        {
            var policies = InsurancePricing.AllPolicies.ToList();
            float banner = Math.Max(46f, InsDlg.TextH(BannerText(comp), r.width - 270f) + 14f);
            DrawBundleBanner(new Rect(r.x, r.y, r.width, banner), comp);
            var outR = new Rect(r.x, r.y + banner + 4f, r.width, r.height - banner - 4f);
            float total = 0f; foreach (var p in policies) total += CardHeight(p, Card(p), outR.width - 18f) + 8f;
            var view = new Rect(0, 0, outR.width - 18f, total);
            Widgets.BeginScrollView(outR, ref scrollShop, view);
            float y = 0;
            foreach (var p in policies)
            {
                var cs = Card(p);
                float h = CardHeight(p, cs, view.width);
                DrawCard(new Rect(0, y, view.width, h), p, cs, comp);
                y += h + 8f;
            }
            Widgets.EndScrollView();
        }

        static string BannerText(InsuranceComponent comp)
        {
            int n = comp.Active.Count();
            string text;
            double cur = 1 - InsuranceMath.BundleFactor(n);
            if (n == 0) text = Ins.T("UI.Bundle.None");
            else if (n < 3) text = Ins.T("UI.Bundle.Some", n, Ins.Pct(cur), Ins.Pct(1 - InsuranceMath.BundleFactor(3)));
            else text = Ins.T("UI.Bundle.Full", n, Ins.Pct(cur));
            if (InsSettings.Hard && n > 0) text += "  " + Ins.T("UI.Bundle.Retro");
            return "<b>" + Ins.T("UI.Bundle.Title") + "</b>  " + text;
        }

        static void DrawBundleBanner(Rect r, InsuranceComponent comp)
        {
            Widgets.DrawBoxSolid(r, new Color(0.15f, 0.28f, 0.20f, 0.9f));
            GUI.color = Good; Widgets.DrawBox(r); GUI.color = Color.white;
            string text = BannerText(comp);
            Text.Anchor = TextAnchor.MiddleLeft;
            float btnW = Math.Min(230f, Text.CalcSize(Ins.T("UI.CoverAll")).x + 30f);
            Widgets.Label(new Rect(r.x + 10f, r.y, r.width - btnW - 30f, r.height), text);
            Text.Anchor = TextAnchor.UpperLeft;
            var allBtn = new Rect(r.xMax - btnW - 8f, r.y + (r.height - 28f) / 2f, btnW, 28f);
            if (Widgets.ButtonText(allBtn, Ins.T("UI.CoverAll"))) Dialog_CoverEveryone.Open();
            TooltipHandler.TipRegion(allBtn, Ins.T("UI.CoverAll.Tip"));
        }

        static void DrawCard(Rect r, SL_PolicyDef def, CardState cs, InsuranceComponent comp)
        {
            Widgets.DrawBoxSolid(r, BlueDark);
            GUI.color = new Color(Blue.r, Blue.g, Blue.b, 0.8f); Widgets.DrawBox(r); GUI.color = Color.white;
            var map = InsurerBooks.HomeMap;
            float x = r.x + 10f, w = r.width - 20f, y = r.y + 6f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, w * 0.6f, 30f), def.LabelCap);
            Text.Font = GameFont.Small;
            if (!def.slogan.NullOrEmpty())
            {
                GUI.color = Pale; Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(x + w * 0.45f, y + 4f, w * 0.55f, 24f), "“" + def.slogan + "”");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            y += 30f;
            GUI.color = Grey;
            float coversH = CoversHeight(def, w);
            Widgets.Label(new Rect(x, y, w, coversH), Ins.T("UI.Covers") + " " + def.coversText);
            GUI.color = Color.white; y += coversH;

            // sum / holder first: the tier quotes depend on them
            Pawn holder = null; double sum = cs.sum;
            InsurancePricing.SumRange(def, map, null, out _, out _);
            if (def.kind == PolicyKind.Person)
            {
                var cands = InsuranceStatus.AllColonists();
                if (cs.holder == null || cs.holder.Dead || !cands.Contains(cs.holder)) cs.holder = cands.FirstOrDefault(p => !comp.Has(def, p)) ?? cands.FirstOrDefault();
                holder = cs.holder;
            }
            InsurancePricing.SumRange(def, map, holder, out double smin, out double smax);
            if (cs.sum < 0 || cs.sum < smin || cs.sum > smax) cs.sum = InsurancePricing.SuggestSum(def, map, holder);
            sum = cs.sum;

            // tier columns
            float colW = (w - 16f) / 3f; float colH = TierColH;
            int bundleCount = InsurancePricing.BundleCountForNew;
            for (int t = 0; t < 3; t++)
            {
                var tier = (CoverTier)t;
                var cr = new Rect(x + t * (colW + 8f), y, colW, colH);
                DrawTier(cr, def, tier, holder, sum, cs, comp, bundleCount);
            }
            y += colH + 8f;

            // options row
            var optRow = new Rect(x, y, w, 26f);
            DrawOptions(optRow, def, cs, comp, holder, smin, smax);
            y += 30f;

            // riders
            int rn = def.riders?.Count ?? 0;
            if (rn > 0)
            {
                bool hidden = InsSettings.Hard && InsSettings.Pressure >= 0.6 && !cs.customize;
                if (hidden)
                {
                    GUI.color = Grey; Widgets.Label(new Rect(x, y, w - 160f, 22f), Ins.T("UI.Riders.Hidden", cs.riders.Count)); GUI.color = Color.white;
                    if (Widgets.ButtonText(new Rect(x + w - 150f, y, 150f, 22f), Ins.T("UI.Customize") + " ▾")) cs.customize = true;
                    y += 24f;
                }
                else
                {
                    var total = InsurancePricing.RiderShare(def, cs.riders);
                    foreach (var rd in def.riders)
                    {
                        bool on = cs.riders.Contains(rd.key);
                        var rr = new Rect(x, y, w - 190f, 24f);
                        Widgets.CheckboxLabeled(rr, rd.LabelCap + (cs.preTicked.Contains(rd.key) && on ? "  " + Ins.T("UI.Riders.Pre") : "") + "  (+" + Ins.Pct(rd.premiumShare * (InsSettings.Tier >= 5.5f ? 1.5 : InsSettings.Tier >= 3.5f ? 1.2 : 1.0)) + ")", ref on);
                        TooltipHandler.TipRegion(rr, rd.description);
                        if (on) cs.riders.Add(rd.key); else cs.riders.Remove(rd.key);
                        y += 26f;
                    }
                    var br = new Rect(x + w - 180f, y - 26f * rn, 180f, 24f);
                    GUI.color = Grey; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(new Rect(br.x, br.y, br.width, 20f), Ins.T("UI.Riders.Sum", Ins.Pct(total)));
                    Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                    if (cs.riders.Count > 0 && Widgets.ButtonText(new Rect(br.x, br.y + 22f, br.width, 24f), Ins.T("UI.Riders.BasicOnly"))) cs.riders.Clear();
                    y += 6f;
                }
            }

            // fine print
            var fp = new Rect(x, y, w, 22f);
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            if (Widgets.ButtonText(new Rect(x, y, 200f, 20f), (cs.fine ? "▾ " : "▸ ") + Ins.T("UI.FinePrint"), false)) cs.fine = !cs.fine;
            Text.Font = GameFont.Small; GUI.color = Color.white;
            if (InsSettings.Hard && InsSettings.TrickActive(InsSettings.TrickBadge, 0.02))
            {
                var pp = new Rect(x + w - 230f, y, 230f, 20f);
                GUI.color = new Color(0.45f, 0.47f, 0.5f); Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(pp, Ins.T("UI.PremiumPlus"));
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            y += 24f;
            if (cs.fine)
            {
                var fr = new Rect(x, y, w, 112f);
                Widgets.DrawBoxSolid(fr, new Color(0f, 0f, 0f, 0.28f));
                GUI.color = Grey; Text.Font = GameFont.Tiny;
                bool abbr = InsSettings.Pressure >= 0.6;
                var lines = new List<string> { Ins.T("UI.Fine.Intro", def.LabelCap) };
                foreach (var cl in ClauseEngine.All.Where(c => ClauseEngine.Active(c, CoverTier.GoodEnough)).Take(4)) lines.Add(cl.Print(abbr));
                lines.Add(Ins.T("UI.Fine.Tail"));
                Widgets.Label(new Rect(fr.x + 6f, fr.y + 4f, fr.width - 12f, fr.height - 8f), string.Join("\n", lines));
                Text.Font = GameFont.Small; GUI.color = Color.white;
            }
        }

        static void DrawOptions(Rect r, SL_PolicyDef def, CardState cs, InsuranceComponent comp, Pawn holder, double smin, double smax)
        {
            float x = r.x;
            if (def.kind == PolicyKind.Person)
            {
                var map = InsurerBooks.HomeMap;
                if (Widgets.ButtonText(new Rect(x, r.y, 210f, 24f), Ins.T("UI.For", holder?.LabelShortCap ?? "-") + " ▾"))
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (var p in InsuranceStatus.AllColonists())
                    {
                        var pp = p;
                        opts.Add(new FloatMenuOption(p.LabelShortCap + (comp.Has(def, p) ? " (" + Ins.T("UI.Insured") + ")" : ""), () => cs.holder = pp));
                    }
                    if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
                }
                x += 220f;
                string allLabel = Ins.T("UI.CoverAllOne");
                float allW = Math.Min(200f, Text.CalcSize(allLabel).x + 24f);
                var allBtn = new Rect(x, r.y, allW, 24f);
                if (Widgets.ButtonText(allBtn, allLabel)) Dialog_CoverEveryone.Open(def.defName);
                TooltipHandler.TipRegion(allBtn, Ins.T("UI.CoverAll.Tip"));
                x += allW + 10f;
            }
            if (def.riskModel == RiskModel.PerColonist || def.riskModel == RiskModel.Flat) { GUI.color = Grey; Widgets.Label(new Rect(x, r.y + 2f, r.width - x, 22f), Ins.T("UI.SumCap", Ins.Silver(cs.sum))); GUI.color = Color.white; }
            if (smax > smin + 1)
            {
                string label = def.kind == PolicyKind.Colony ? Ins.T("UI.SumProperty", Ins.Silver(cs.sum)) : Ins.T("UI.SumPerson", Ins.Silver(cs.sum));
                Widgets.Label(new Rect(x, r.y + 2f, 230f, 22f), label);
                var sl = new Rect(x + 236f, r.y + 6f, Math.Max(100f, r.width - x - 250f), 18f);
                cs.sum = Widgets.HorizontalSlider(sl, (float)cs.sum, (float)smin, (float)smax, true, null, null, null, 50f);
                cs.sum = Math.Round(cs.sum / 50.0) * 50.0;
            }
        }

        static void DrawTier(Rect r, SL_PolicyDef def, CoverTier tier, Pawn holder, double sum, CardState cs, InsuranceComponent comp, int bundleCount)
        {
            bool premium = tier == CoverTier.Premium;
            bool badge = premium && InsSettings.TrickActive(InsSettings.TrickBadge, 0.02);
            Widgets.DrawBoxSolid(r, premium && badge ? new Color(0.24f, 0.20f, 0.08f, 0.95f) : BlueMid);
            GUI.color = premium && badge ? Gold : new Color(Blue.r, Blue.g, Blue.b, 0.9f);
            Widgets.DrawBox(r, premium && badge ? 2 : 1); GUI.color = Color.white;
            float x = r.x + 8f, w = r.width - 16f, y = r.y + 4f;
            if (badge)
            {
                var ribbon = new Rect(r.x + 1f, r.y - 0f, r.width - 2f, 16f);
                Widgets.DrawBoxSolid(ribbon, Gold);
                GUI.color = new Color(0.15f, 0.1f, 0f); Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(ribbon, Ins.T("UI.Badge"));
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                y += 14f;
            }
            Widgets.Label(new Rect(x, y, w, 22f), "<b>" + Ins.TierName(tier) + "</b>");
            y += 22f;

            string lockReason = null; bool unlocked = InsurancePricing.TierUnlocked(def, tier, out lockReason);
            var contract = comp.contracts.FirstOrDefault(c => c.Active && c.defName == def.defName && c.holder == (def.kind == PolicyKind.Person ? holder : null));
            var q = InsurancePricing.QuoteFor(def, tier, holder, sum, cs.riders, bundleCount, null);
            string price = q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), q.billingDays);
            GUI.color = premium && badge ? Gold : new Color(0.78f, 0.86f, 0.97f);
            Widgets.Label(new Rect(x, y, w, 22f), price);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(x, y, w, 22f), Ins.T("UI.Price.Tip", string.Join("\n", q.lines), Ins.Silver(q.premiumQuadrum)));
            y += 22f;

            Text.Font = GameFont.Tiny;
            GUI.color = PaleHi;
            string benefit = def.BenefitFor(tier) ?? "";
            float btnTop = r.yMax - 34f;                                    // the button row keeps its place; text gets what is left above it
            float benefitH = Math.Min(54f, Math.Max(14f, Text.CalcHeight(benefit, w)));
            BankUI.FitLabel(new Rect(x, y, w, benefitH), benefit, GameFont.Tiny);
            y += benefitH + 2f;
            GUI.color = Grey;
            InsuranceMath.PayoutDelayDays(tier, out int dmin, out int dmax);
            string mech = def.kind == PolicyKind.Colony || def.kind == PolicyKind.Travel && def.riskModel != RiskModel.PerTravelDay
                ? Ins.T("UI.Mech.Property", Ins.Pct(InsuranceMath.Deductible(tier)), Ins.Pct(InsuranceMath.Replacement(tier)))
                : Ins.T("UI.Mech.Person", InsuranceMath.InvalidityThreshold(tier).ToString("0"), Ins.Pct(InsuranceMath.Progression(InsuranceMath.ModelFor(tier), 100) / 100.0));
            BankUI.FitLabel(new Rect(x, y, w, Math.Max(14f, btnTop - y)), mech + "  " + (dmax == 0 ? Ins.T("UI.Pay.Instant") : Ins.T("UI.Pay.Days", dmin, dmax)), GameFont.Tiny);
            Text.Font = GameFont.Small; GUI.color = Color.white;

            var btn = new Rect(x, r.yMax - 30f, w, 24f);
            if (contract != null)
            {
                bool same = contract.tier == tier;
                GUI.color = same ? Good : Grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(btn, same ? "✓ " + Ins.T("UI.Signed") : Ins.T("UI.SignedOther", Ins.TierName(contract.tier)));
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            else if (!unlocked)
            {
                GUI.color = Grey; Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny;
                Widgets.Label(btn, "⚿ " + lockReason);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            else
            {
                string err = comp.CanSign(def, tier, holder);
                GUI.color = premium && badge ? Gold : Color.white;
                if (Widgets.ButtonText(btn, Ins.T("UI.SignNow"), true, true, err == null))
                {
                    var res = comp.Sign(def, tier, holder, sum, cs.riders, cs.preTicked);
                    if (res.Ok) { SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); Messages.Message(Ins.T("UI.SignedMsg", def.LabelCap, Ins.TierName(tier)), MessageTypeDefOf.PositiveEvent, false); }
                    else Messages.Message(res.error, MessageTypeDefOf.RejectInput, false);
                }
                GUI.color = Color.white;
                if (err != null) TooltipHandler.TipRegion(btn, err);
            }
        }

        // ---- my policies -------------------------------------------------------------------------------------------------------------------------
        static void DrawMine(Rect r, InsuranceComponent comp)
        {
            var list = comp.contracts.OrderByDescending(c => c.Active).ThenBy(c => c.id).ToList();
            if (list.Count == 0) { GUI.color = Grey; Widgets.Label(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 60f), Ins.T("UI.Mine.None")); GUI.color = Color.white; return; }
            float vw = r.width - 18f;
            float total = 80f + (comp.debt > 0.01 ? 40f : 0f);
            var heights = new List<float>();
            foreach (var c in list) { float h = ContractHeight(c, vw); heights.Add(h); total += h + 6f; }
            var view = new Rect(0, 0, vw, total);
            Widgets.BeginScrollView(r, ref scrollMine, view);
            float y = 0;
            if (comp.debt > 0.01)
            {
                var dr = new Rect(0, y, view.width, 34f);
                Widgets.DrawBoxSolid(dr, new Color(0.35f, 0.12f, 0.1f, 0.9f));
                Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(dr.x + 8f, dr.y, dr.width - 180f, dr.height), Ins.T("UI.Debt", Ins.Silver(comp.debt))); Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonText(new Rect(dr.xMax - 160f, dr.y + 4f, 152f, 26f), Ins.T("UI.Debt.Settle"))) { if (!comp.SettleDebt()) Messages.Message(Ins.T("UI.Debt.Cannot"), MessageTypeDefOf.RejectInput, false); }
                y += 40f;
            }
            for (int i = 0; i < list.Count; i++) { DrawContract(new Rect(0, y, view.width, heights[i]), list[i], comp); y += heights[i] + 6f; }
            y += 6f;
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, view.width, 60f), Ins.T("UI.Mine.Foot"));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            Widgets.EndScrollView();
        }

        sealed class ContractText { public string main, line2, pending, wait, pension; public bool pensionRisk; }

        static ContractText TextOf(PolicyContract c)
        {
            var t = new ContractText();
            var q = c.Active ? InsurancePricing.QuoteFor(c) : null;
            var sb = new List<string>();
            sb.Add(Ins.T("UI.Mine.Sum", Ins.Silver(c.sum)));
            if (q != null) sb.Add(q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), q.billingDays));
            sb.Add(Ins.T("UI.Mine.NoClaims", c.noClaimsClass, Ins.Pct(1 - InsuranceMath.NoClaimsFactor(c.noClaimsClass))));
            if (c.Active && c.InWaitingPeriod(Ins.Now)) sb.Add(Ins.T("UI.Mine.Waiting", (c.waitUntilTick - Ins.Now).ToStringTicksToPeriod()));
            if (c.discountUntilTick > Ins.Now) sb.Add(Ins.T("UI.Mine.Discount", Ins.Pct(c.discountShare)));
            if (c.owedClawback > 0.01) sb.Add(Ins.T("UI.Mine.Owed", Ins.Silver(c.owedClawback)));
            t.main = string.Join("  ·  ", sb);
            string line2 = Ins.T("UI.Mine.Paid", Ins.Silver(c.premiumPaid), Ins.Silver(c.benefitPaid));
            if (c.riders.Count > 0) line2 += "  ·  " + string.Join(", ", c.riders.Select(k => ClaimLetters.RiderLabel(c.Def, k)));
            if (!c.Active) line2 = Ins.T("UI.Mine.Ended", c.endReason ?? "") + "  ·  " + line2;
            t.line2 = line2;
            if (c.Active && c.HasPendingChange)
            {
                var parts = new List<string>();
                if (c.pendingTier >= 0 && c.pendingTier != (int)c.tier) parts.Add(Ins.T("Change.Part.Tier", Ins.TierName(c.tier), Ins.TierName((CoverTier)c.pendingTier)));
                if (c.pendingSum >= 0 && Math.Abs(c.pendingSum - c.sum) > 0.5) parts.Add(Ins.T("Change.Part.Sum", Ins.Silver(c.sum), Ins.Silver(c.pendingSum)));
                if (c.pendingRiders != null && (c.pendingRiders.Except(c.riders).Any() || c.riders.Except(c.pendingRiders).Any())) parts.Add(Ins.T("Change.Part.Riders"));
                t.pending = Ins.T("UI.Mine.PendingChange", (c.pendingEffectiveTick - Ins.Now).ToStringTicksToPeriod()) + (parts.Count > 0 ? ": " + string.Join("; ", parts) : "");
            }
            if (c.Active && c.upgradeWaitUntilTick > Ins.Now) t.wait = Ins.T("UI.Mine.UpgradeWait", (c.upgradeWaitUntilTick - Ins.Now).ToStringTicksToPeriod());
            if (c.Active && PensionRule.IsPension(c) && c.tier != CoverTier.BareMinimum && PensionRule.Enabled && c.holder.ageTracker.AgeBiologicalYears >= 60)
            {
                int h = PensionRule.WorkHours(c.holder);
                t.pensionRisk = h > InsSettings.PensionHours;
                t.pension = c.pensionRevoked ? Ins.T("UI.Mine.PensionLost")
                    : c.pensionSuspendedUntil > Ins.Now ? Ins.T("UI.Mine.PensionSuspended", (c.pensionSuspendedUntil - Ins.Now).ToStringTicksToPeriod())
                    : Ins.T(t.pensionRisk ? "UI.Mine.PensionRisk" : "UI.Mine.PensionOk", h, InsSettings.PensionHours, PensionRule.AnythingHours(c.holder)) + (c.pensionStrikes > 0 && InsSettings.PensionMode == PensionRuleMode.Enforce ? "  " + Ins.T("Status.Strikes", Math.Max(0, 3 - c.pensionStrikes)) : "");
            }
            return t;
        }

        static float ContractHeight(PolicyContract c, float w)
        {
            var t = TextOf(c);
            float tw = w - 20f;
            float h = 4f + 28f + InsDlg.TextH(t.main, tw) + InsDlg.TextH(t.line2, tw, GameFont.Tiny) + 4f;
            if (t.pending != null) h += Math.Max(28f, InsDlg.TextH(t.pending, tw - 200f)) + 2f;
            if (t.wait != null) h += InsDlg.TextH(t.wait, tw, GameFont.Tiny) + 2f;
            if (t.pension != null) h += Math.Max(26f, InsDlg.TextH(t.pension, tw - 190f)) + 2f;
            if (c.Active) h += 32f;      // button row
            return Math.Max(h + 8f, 84f);
        }

        static void DrawContract(Rect r, PolicyContract c, InsuranceComponent comp)
        {
            Widgets.DrawBoxSolid(r, c.Active ? BlueDark : new Color(0.12f, 0.12f, 0.12f, 0.8f));
            GUI.color = c.Active ? Blue : Grey; Widgets.DrawBox(r); GUI.color = Color.white;
            float x = r.x + 10f, w = r.width - 20f, y = r.y + 4f;
            var t = TextOf(c);
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(x, y, w * 0.6f, 28f), c.Label); Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight; Widgets.Label(new Rect(x + w * 0.5f, y + 2f, w * 0.5f, 24f), "<b>" + Ins.TierName(c.tier) + "</b>"); Text.Anchor = TextAnchor.UpperLeft;
            y += 28f;
            float h1 = InsDlg.TextH(t.main, w);
            Widgets.Label(new Rect(x, y, w, h1), t.main); y += h1;
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            float h2 = InsDlg.TextH(t.line2, w, GameFont.Tiny);
            Widgets.Label(new Rect(x, y, w, h2), t.line2); y += h2 + 4f;
            Text.Font = GameFont.Small; GUI.color = Color.white;
            if (!c.Active) return;
            if (t.pending != null)
            {
                float ph = Math.Max(28f, InsDlg.TextH(t.pending, w - 200f));
                GUI.color = Gold; Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(x, y, w - 200f, ph), t.pending); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(x + w - 190f, y + (ph - 24f) / 2f, 190f, 24f), Ins.T("UI.Mine.WithdrawChange"))) comp.WithdrawChange(c);
                y += ph + 2f;
            }
            if (t.wait != null) { GUI.color = Grey; Text.Font = GameFont.Tiny; float wh = InsDlg.TextH(t.wait, w, GameFont.Tiny); Widgets.Label(new Rect(x, y, w, wh), t.wait); Text.Font = GameFont.Small; GUI.color = Color.white; y += wh + 2f; }
            if (t.pension != null)
            {
                float ph = Math.Max(26f, InsDlg.TextH(t.pension, w - 190f));
                GUI.color = t.pensionRisk ? Bad : c.pensionRevoked || c.pensionSuspendedUntil > Ins.Now ? Bad : Good;
                Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(x, y, w - 190f, ph), t.pension); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(x, y, w - 190f, ph), Ins.T("UI.Mine.PensionTip", InsSettings.PensionHours));
                if (t.pensionRisk && Widgets.ButtonText(new Rect(x + w - 180f, y + (ph - 24f) / 2f, 180f, 24f), Ins.T("Pension.FixSchedule")))
                { int n = PensionRule.FixSchedule(c.holder, InsSettings.PensionHours); Messages.Message(Ins.T("Pension.Fixed", c.holder.LabelShortCap, n), MessageTypeDefOf.PositiveEvent, false); }
                y += ph + 2f;
            }
            // button row
            if (c.CancelPending)
            {
                GUI.color = Bad;
                Widgets.Label(new Rect(x, y, w - 200f, 22f), Ins.T("UI.Mine.CancelPending", (c.cancelEffectiveTick - Ins.Now).ToStringTicksToPeriod()) + (c.formErrorHit ? "  " + Ins.T("UI.Mine.FormError") : ""));
                GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(x + w - 190f, y - 2f, 190f, 24f), Ins.T("UI.Mine.Withdraw"))) comp.WithdrawCancel(c);
            }
            else
            {
                string why = comp.CanChange(c);
                var changeBtn = new Rect(x, y, 170f, 26f);
                if (Widgets.ButtonText(changeBtn, Ins.T("UI.Mine.Change"), true, true, why == null)) Dialog_ChangePolicy.Open(c);
                if (why != null) TooltipHandler.TipRegion(changeBtn, why); else TooltipHandler.TipRegion(changeBtn, Ins.T("UI.Mine.ChangeTip"));
                // upgrade nudge (Standard: one calm line; Overbooked: a button)
                if (c.tier != CoverTier.Premium && InsSettings.TrickActive(InsSettings.TrickUpsell) && InsurancePricing.TierUnlocked(c.Def, CoverTier.Premium, out _))
                { GUI.color = Gold; Widgets.Label(new Rect(x + 180f, y + 2f, w - 180f - 110f, 22f), Ins.T("UI.Mine.Upsell")); GUI.color = Color.white; }
                // the small, quiet link (unchanged: the owner likes it hidden)
                string txt = Ins.T("UI.Mine.CancelLink");
                Text.Font = GameFont.Tiny;
                var cr = new Rect(r.xMax - 10f - 90f, r.yMax - 20f, 90f, 16f);
                GUI.color = Mouse.IsOver(cr) ? PaleHi : new Color(0.5f, 0.52f, 0.56f);
                Text.Anchor = TextAnchor.MiddleRight; Widgets.Label(cr, txt);
                if (Mouse.IsOver(cr)) { Widgets.DrawLineHorizontal(cr.xMax - Text.CalcSize(txt).x, cr.yMax - 1f, Text.CalcSize(txt).x); }
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(cr)) Find.WindowStack.Add(new Dialog_CancelPolicy(c.id));
            }
        }

        // ---- claims -------------------------------------------------------------------------------------------------------------------------------
        static float SectionHeader(float y, float w, string text, int count)
        {
            GUI.color = Gold; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, y, w, 30f), text + (count >= 0 ? "  (" + count + ")" : ""));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0, y + 30f, w, new Color(Blue.r, Blue.g, Blue.b, 0.6f));
            return 36f;
        }

        static void DrawClaims(Rect r, InsuranceComponent comp)
        {
            var opps = comp.OpenOpportunities.OrderBy(o => o.deadlineTick).ToList();
            var open = comp.claims.Where(c => c.Open).OrderByDescending(c => c.filedTick).ToList();
            var hist = comp.claims.Where(c => !c.Open).OrderByDescending(c => c.filedTick).ToList();
            float vw = r.width - 18f;
            float total = 16f; var heights = new Dictionary<int, float>();
            foreach (var c in open.Concat(hist)) { float h = ClaimHeight(c, vw); heights[c.id] = h; total += h + 6f; }
            var oppH = new Dictionary<int, float>();
            foreach (var o in opps) { float h = OppHeight(o, vw); oppH[o.id] = h; total += h + 6f; }
            if (opps.Count > 0) total += 44f;
            if (open.Count > 0) total += 44f;
            if (hist.Count > 0) total += 44f;
            total += 30f + comp.log.Count * 16f;
            if (opps.Count == 0 && open.Count == 0 && hist.Count == 0) { GUI.color = Grey; Widgets.Label(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 40f), Ins.T("UI.Claims.None")); GUI.color = Color.white; }
            var view = new Rect(0, 0, vw, Math.Max(total, 40f));
            Widgets.BeginScrollView(r, ref scrollClaims, view);
            float y = 0;
            if (opps.Count > 0)
            {
                y += SectionHeader(y, view.width, Ins.T("UI.Claims.Windows"), opps.Count);
                foreach (var o in opps) { DrawOpportunity(new Rect(0, y, view.width, oppH[o.id]), o, comp); y += oppH[o.id] + 6f; }
            }
            if (open.Count > 0)
            {
                y += SectionHeader(y, view.width, Ins.T("UI.Claims.Open"), open.Count);
                foreach (var c in open) { DrawClaim(new Rect(0, y, view.width, heights[c.id]), c, comp); y += heights[c.id] + 6f; }
            }
            if (hist.Count > 0)
            {
                y += SectionHeader(y, view.width, Ins.T("UI.Claims.History"), hist.Count);
                foreach (var c in hist) { DrawClaim(new Rect(0, y, view.width, heights[c.id]), c, comp); y += heights[c.id] + 6f; }
            }
            if (comp.log.Count > 0)
            {
                y += 6f; GUI.color = Grey; Widgets.Label(new Rect(0, y, view.width, 24f), "<b>" + Ins.T("UI.Claims.Log") + "</b>"); y += 24f; Text.Font = GameFont.Tiny;
                foreach (var l in Enumerable.Reverse(comp.log).Take(24)) { Widgets.Label(new Rect(8f, y, view.width - 16f, 16f), l); y += 16f; }
                Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            Widgets.EndScrollView();
        }

        const float ActionW = 150f;

        static float OppHeight(ClaimOpportunity o, float w)
        {
            string text = OppText(o);
            return Math.Max(64f, 12f + 24f + InsDlg.TextH(text, w - ActionW - 30f, GameFont.Tiny) + 6f);
        }

        static string OppText(ClaimOpportunity o)
        {
            var c = o.Contract;
            return Ins.T("UI.Claims.WindowLine", c?.Label ?? "?", Ins.TierName(c?.tier ?? CoverTier.GoodEnough), o.TotalValue > 0 ? Ins.Silver(o.TotalValue) : "-", o.itemLabels.Count > 0 ? o.itemLabels.Count : 1);
        }

        static void DrawOpportunity(Rect r, ClaimOpportunity o, InsuranceComponent comp)
        {
            int left = Math.Max(0, o.deadlineTick - Ins.Now);
            bool urgent = left < 2 * ClaimEngine.Day;
            Widgets.DrawBoxSolid(r, BlueDark);
            GUI.color = urgent ? Bad : Gold; Widgets.DrawBox(r); GUI.color = Color.white;
            float x = r.x + 10f, w = r.width - 20f - ActionW;
            Widgets.Label(new Rect(x, r.y + 4f, w, 24f), "<b>" + o.Summary + "</b>");
            GUI.color = urgent ? Bad : Gold; Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(r.xMax - 10f - ActionW - 230f, r.y + 4f, 230f, 24f), Ins.T("UI.Claims.Left", Ins.Duration(left)));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x, r.y + 28f, w, r.height - 30f), OppText(o));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(r.xMax - 10f - ActionW, r.y + (r.height - 30f) / 2f, ActionW, 30f), Ins.T("Btn.FillForm"))) Dialog_ClaimForm.Open(o.id);
        }

        static float ClaimHeight(Claim c, float w)
        {
            float h = 58f;
            if (expandedClaims.Contains(c.id)) { Text.Font = GameFont.Tiny; h += Text.CalcHeight(ClaimLetters.Explain(c) + (c.Clause != null ? "\n" + c.Clause.Print(false) : "") + FormDetail(c), w - 30f) + 10f; Text.Font = GameFont.Small; }
            if (c.state == ClaimState.Decided) h += 34f;
            return h;
        }

        static string FormDetail(Claim c)
        {
            if (c.opportunityId < 0 && c.formText.NullOrEmpty()) return "";
            var sb = new System.Text.StringBuilder();
            if (c.formAnswers != null && c.formAnswers.Count > 0) sb.Append("\n" + Ins.T("UI.Claim.Form", string.Join(", ", c.formAnswers)));
            if (!c.formText.NullOrEmpty()) sb.Append("\n" + Ins.T("UI.Claim.Statement", c.formText));
            if (!c.aiReply.NullOrEmpty()) sb.Append("\n" + Ins.T("UI.Claim.AiReply", c.aiReply));
            return sb.ToString();
        }

        static void DrawClaim(Rect r, Claim c, InsuranceComponent comp)
        {
            Widgets.DrawBoxSolid(r, c.Open ? BlueDark : new Color(0.12f, 0.12f, 0.12f, 0.8f));
            Color border = c.outcome == ClaimOutcome.Approved ? Good : c.outcome == ClaimOutcome.Denied ? Bad : c.outcome == ClaimOutcome.Partial ? Gold : Blue;
            GUI.color = new Color(border.r, border.g, border.b, 0.85f); Widgets.DrawBox(r); GUI.color = Color.white;
            float x = r.x + 10f, w = r.width - 20f, y = r.y + 4f;
            Widgets.Label(new Rect(x, y, w * 0.62f, 24f), "<b>#" + c.id + "</b>  " + (c.Def?.LabelCap ?? c.policyDef) + "  ·  " + c.subject);
            string st = c.state == ClaimState.Review ? Ins.T("UI.Claim.Review", Math.Max(0, c.decideTick - Ins.Now).ToStringTicksToPeriod())
                      : c.state == ClaimState.Decided ? Ins.T("Outcome." + c.outcome) + " · " + (c.outcome == ClaimOutcome.Denied ? Ins.T("UI.Claim.Window", Math.Max(0, c.payTick - Ins.Now).ToStringTicksToPeriod()) : Ins.T("UI.Claim.PaysIn", Math.Max(0, c.payTick - Ins.Now).ToStringTicksToPeriod()))
                      : c.state == ClaimState.Paid ? Ins.T("UI.Claim.Paid", Ins.Silver(c.paid)) : Ins.T("UI.Claim.Closed");
            Text.Anchor = TextAnchor.UpperRight; GUI.color = border; Widgets.Label(new Rect(x + w * 0.55f, y, w * 0.45f, 24f), st); GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            y += 24f;
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            string l2 = Ins.T("UI.Claim.Line", Ins.TierName(c.tier), Ins.Silver(c.lossValue > 0 ? c.lossValue : c.gross), Ins.Silver(c.gross), Ins.Silver(c.offered));
            if (c.state == ClaimState.Review && c.trigger == ClaimTrigger.LimbLoss) l2 = Ins.T("UI.Claim.Preview", c.grade.ToString("0.#"), Ins.Silver(c.offered), Math.Max(0, c.assessTick - Ins.Now).ToStringTicksToPeriod());
            Widgets.Label(new Rect(x, y, w - 140f, 18f), l2);
            Text.Font = GameFont.Small; GUI.color = Color.white;
            bool open = expandedClaims.Contains(c.id);
            Text.Font = GameFont.Tiny;
            if (Widgets.ButtonText(new Rect(x + w - 130f, y - 2f, 130f, 20f), (open ? "▾ " : "▸ ") + Ins.T("UI.Claim.Details"), false)) { if (open) expandedClaims.Remove(c.id); else expandedClaims.Add(c.id); }
            Text.Font = GameFont.Small;
            y += 20f;
            if (open)
            {
                GUI.color = PaleHi; Text.Font = GameFont.Tiny;
                string det = ClaimLetters.Explain(c) + (c.Clause != null ? "\n" + Ins.T("Letter.Clause.Cited", c.Clause.Print(false)) : "") + FormDetail(c);
                float dh = Text.CalcHeight(det, w - 20f);
                Widgets.Label(new Rect(x + 6f, y, w - 12f, dh), det); y += dh + 6f;
                Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            if (c.state == ClaimState.Decided)
            {
                // right-aligned action column: one fixed width, buttons stacked from the right (no inline x increments)
                float bx = r.xMax - 10f;
                if (c.outcome != ClaimOutcome.Denied && !c.negotiationClosed)
                { bx -= 116f; if (Widgets.ButtonText(new Rect(bx, y, 110f, 28f), Ins.T("Btn.Accept"))) comp.Accept(c.id); }
                if (c.outcome != ClaimOutcome.Approved && !c.negotiationClosed && HSettings.GetBool(InsSettings.Negotiation))
                { bx -= 156f; if (Widgets.ButtonText(new Rect(bx, y, 150f, 28f), Ins.T("Btn.Negotiate") + " (" + (3 - c.rounds) + ")")) Find.WindowStack.Add(new Dialog_Negotiate(c.id)); }
                if (c.outcome != ClaimOutcome.Denied && c.materials.Count > 0 && c.tier != CoverTier.BareMinimum)
                {
                    bool mat = c.materialsChosen;
                    float mw = Math.Max(160f, bx - x - 10f);
                    Widgets.CheckboxLabeled(new Rect(x, y + 2f, mw, 26f), Ins.T("Btn.Materials"), ref mat);
                    c.materialsChosen = mat;
                }
            }
        }

        // ---- colonists overview (F28) -------------------------------------------------------------------------------------------------------------
        static void DrawCover(Rect r, InsuranceComponent comp)
        {
            var map = InsurerBooks.HomeMap;
            var pawns = InsuranceStatus.AllColonists().Where(InsuranceStatus.Shown).OrderByDescending(p => p.ageTracker.AgeBiologicalYearsFloat).ToList();
            float lead = InsDlg.TextH(Ins.T("UI.Cover.Lead"), r.width - 12f);
            GUI.color = Grey; Widgets.Label(new Rect(r.x + 6f, r.y, r.width - 12f, lead), Ins.T("UI.Cover.Lead")); GUI.color = Color.white;
            var top = new Rect(r.x, r.y + lead + 6f, r.width, 30f);
            if (Widgets.ButtonText(new Rect(top.x + 6f, top.y, 240f, 28f), Ins.T("UI.CoverAll"))) Dialog_CoverEveryone.Open();
            var body = new Rect(r.x, top.yMax + 6f, r.width, r.yMax - top.yMax - 6f);
            // header: kinds
            float nameW = Math.Min(220f, body.width * 0.28f);
            var kindDefs = InsuranceStatus.Defs;
            float cw = (body.width - nameW - 22f) / Math.Max(1, kindDefs.Count);
            var head = new Rect(body.x, body.y, body.width, 30f);
            Widgets.DrawBoxSolid(head, BlueMid);
            Text.Anchor = TextAnchor.MiddleLeft; Widgets.Label(new Rect(head.x + 8f, head.y, nameW - 8f, head.height), "<b>" + Ins.T("UI.Cover.Colonist") + "</b>");
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int i = 0; i < kindDefs.Count; i++)
            {
                var cr = new Rect(head.x + nameW + i * cw, head.y, cw, head.height);
                Text.Font = GameFont.Tiny; Widgets.Label(cr, "<b>" + InsuranceStatus.KindLabel(kindDefs[i]) + "</b>"); Text.Font = GameFont.Small;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            var list = new Rect(body.x, head.yMax, body.width, body.yMax - head.yMax);
            float rowH = 40f;
            Widgets.BeginScrollView(list, ref scrollCover, new Rect(0, 0, list.width - 18f, pawns.Count * (rowH + 2f) + 8f));
            float y = 0f;
            foreach (var p in pawns)
            {
                var row = new Rect(0, y, list.width - 18f, rowH);
                Widgets.DrawBoxSolid(row, new Color(0f, 0f, 0f, 0.18f));
                Widgets.Label(new Rect(row.x + 8f, row.y + 4f, nameW - 8f, 20f), p.LabelShortCap);
                GUI.color = Grey; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(row.x + 8f, row.y + 22f, nameW - 8f, 16f), p.ageTracker.AgeBiologicalYears + " y" + (InsuranceStatus.Insurable(p) ? "" : " · " + Ins.T("Status.NotInsurable"))); Text.Font = GameFont.Small; GUI.color = Color.white;
                bool ins = InsuranceStatus.Insurable(p);
                var items = InsuranceStatus.Of(p);
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var cell = new Rect(row.x + nameW + i * cw + 3f, row.y + 3f, cw - 6f, rowH - 6f);
                    var col = InsuranceStatus.ColorOf(ins ? it.state : CoverState.None);
                    Widgets.DrawBoxSolid(cell, new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f, 0.9f));
                    GUI.color = col; Widgets.DrawBox(cell); GUI.color = Color.white;
                    var tex = InsuranceStatus.Icon(it);
                    if (tex != null) { GUI.color = col; GUI.DrawTexture(new Rect(cell.x + 4f, cell.y + (cell.height - 22f) / 2f, 22f, 22f), tex); GUI.color = Color.white; }
                    Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = col;
                    string t = !ins ? "–" : it.Has ? InsuranceStatus.StateLabel(it.state) + (it.colonyWide ? " *" : "") : Ins.T("Status.No");
                    if (!it.Has && ins && it.def != null && it.def.kind == PolicyKind.Person)
                    {
                        var buy = new Rect(cell.x + 30f, cell.y + 2f, cell.width - 34f, cell.height - 4f);
                        Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                        if (Widgets.ButtonText(buy, Ins.T("Status.Buy"), true, true, true)) OpenShopFor(it.def, p);
                    }
                    else Widgets.Label(new Rect(cell.x + 30f, cell.y, cell.width - 32f, cell.height), t);
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
                    TooltipHandler.TipRegion(cell, InsuranceStatus.Tooltip(p, it));
                }
                y += rowH + 2f;
            }
            Widgets.EndScrollView();
            if (pawns.Count == 0) { GUI.color = Grey; Widgets.Label(new Rect(list.x + 8f, list.y + 8f, list.width - 16f, 30f), Ins.T("UI.Cover.None")); GUI.color = Color.white; }
        }

        // ---- fine print + schedule of limbs ------------------------------------------------------------------------------------------------------
        static void DrawFinePrint(Rect r)
        {
            var clauses = ClauseEngine.All.ToList();
            var limbs = LimbSchedule.All.Where(e => !e.Lethal).OrderByDescending(e => e.Percent).ToList();
            bool abbr = InsSettings.Pressure >= 0.6;
            Text.Font = GameFont.Tiny;
            float tw = r.width - 40f;
            float h = 40f;
            foreach (var c in clauses) h += Text.CalcHeight(c.Print(abbr), tw - 90f) + 10f;
            h += 120f + limbs.Count * 20f + 80f;
            Text.Font = GameFont.Small;
            var view = new Rect(0, 0, r.width - 18f, h);
            Widgets.BeginScrollView(r, ref scrollFine, view);
            float y = 0;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, view.width, 32f), Ins.T("UI.Fine.Title")); Text.Font = GameFont.Small; y += 32f;
            GUI.color = Grey; Widgets.Label(new Rect(0, y, view.width, 40f), Ins.T("UI.Fine.Lead")); GUI.color = Color.white; y += 44f;
            Text.Font = GameFont.Tiny;
            foreach (var c in clauses)
            {
                bool on = ClauseEngine.Active(c, CoverTier.GoodEnough);
                string txt = c.Print(abbr); float ch = Text.CalcHeight(txt, tw - 90f);
                GUI.color = on ? Pale : new Color(0.5f, 0.52f, 0.56f);
                Widgets.Label(new Rect(8f, y, 76f, 18f), "<b>§" + c.number + "</b>");
                Widgets.Label(new Rect(84f, y, tw - 90f, ch), txt + (on ? "" : "  [" + Ins.T("UI.Fine.Inactive") + "]"));
                y += ch + 10f;
            }
            GUI.color = Color.white; Text.Font = GameFont.Small; y += 8f;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, view.width, 32f), Ins.T("UI.Limbs.Title")); Text.Font = GameFont.Small; y += 32f;
            GUI.color = Grey; Widgets.Label(new Rect(0, y, view.width, 44f), Ins.T("UI.Limbs.Lead")); GUI.color = Color.white; y += 48f;
            Text.Font = GameFont.Tiny;
            foreach (var e in limbs)
            {
                GUI.color = PaleHi;
                Widgets.Label(new Rect(8f, y, 150f, 18f), e.Key);
                Widgets.Label(new Rect(160f, y, 50f, 18f), e.Percent.ToString("0") + " %");
                GUI.color = Grey; Widgets.Label(new Rect(214f, y, view.width - 230f, 18f), e.Comment);
                y += 20f;
            }
            GUI.color = Color.white; Text.Font = GameFont.Small;
            Widgets.EndScrollView();
        }
    }

    /// <summary>The stand-alone Clawback Mutual window (comms console: "Call Clawback Mutual"). Draws the same content as the bank tab.</summary>
    public sealed class Window_Clawback : Window
    {
        static Window_Clawback current;
        public override Vector2 InitialSize => HUI.Size(1180f, 800f, 900f, 600f, 0.94f, 0.94f);

        public Window_Clawback()
        {
            doCloseX = true; doCloseButton = false; draggable = true; resizeable = true; absorbInputAroundWindow = false; preventCameraMotion = false;
            closeOnClickedOutside = false; forcePause = false;
        }

        public static void Open()
        {
            if (current != null && Find.WindowStack.IsOpen(current)) { current.SetInitialSizeAndPosition(); return; }
            current = new Window_Clawback();
            Find.WindowStack.Add(current);
        }

        public override void DoWindowContents(Rect inRect) => InsuranceUI.Draw(inRect);
    }
}
