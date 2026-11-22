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
        enum Page { Shop, Mine, Claims, FinePrint }

        sealed class CardState
        {
            public HashSet<string> riders = new HashSet<string>();
            public HashSet<string> preTicked = new HashSet<string>();
            public Pawn holder;
            public double sum = -1;
            public bool fine, customize, init;
        }

        static Page page = Page.Shop;
        static Vector2 scrollShop, scrollMine, scrollClaims, scrollFine;
        static readonly Dictionary<string, CardState> cards = new Dictionary<string, CardState>();
        static readonly HashSet<int> expandedClaims = new HashSet<int>();
        static Texture2D logo; static bool logoTried;

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
        public static Color Pale => bankSkin ? BankUI.Cream : Pale;
        public static Color PaleHi => bankSkin ? BankUI.Cream : PaleHi;
        public static Color PaleDim => bankSkin ? BankUI.CreamDim : PaleDim;

        /// <summary>Draw inside the bank window: same pages, the bank's colours.</summary>
        public static void DrawInBank(Rect rect)
        {
            bool was = bankSkin; bankSkin = true;
            try { Draw(rect); } finally { bankSkin = was; }
        }

        public static Texture2D Logo
        {
            get
            {
                if (!logoTried) { logoTried = true; logo = ContentFinder<Texture2D>.Get("UI/HH/Insurance/ClawbackLogo", false); }
                return logo;
            }
        }

        /// <summary>Selects the page of the desk (0 policies, 1 my policies, 2 claims, 3 fine print) and optionally expands cards. Used by tests and screenshots.</summary>
        public static void ShowPage(int p, bool expandAll = false)
        {
            page = (Page)Math.Max(0, Math.Min(3, p));
            if (expandAll) foreach (var d in InsurancePricing.AllPolicies) Card(d).fine = true;
            scrollShop = scrollMine = scrollClaims = scrollFine = Vector2.zero;
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
            int openClaims = comp.OpenClaims.Count();
            string[] labels = { Ins.T("Tab.Shop"), Ins.T("Tab.Mine", comp.Active.Count()), Ins.T("Tab.Claims", openClaims), Ins.T("Tab.FinePrint") };
            float w = Math.Min(190f, r.width / 4f);
            for (int i = 0; i < 4; i++)
            {
                var br = new Rect(r.x + i * (w + 4f), r.y, w, r.height);
                bool sel = (int)page == i;
                if (sel) { Widgets.DrawBoxSolid(br, Blue); }
                if (Widgets.ButtonText(br, labels[i], !sel)) { page = (Page)i; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
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

        static float CardHeight(SL_PolicyDef def, CardState cs)
        {
            float h = 30f + 40f + 176f + 8f;
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
            float banner = 46f;
            DrawBundleBanner(new Rect(r.x, r.y, r.width, banner), comp);
            var outR = new Rect(r.x, r.y + banner + 4f, r.width, r.height - banner - 4f);
            float total = 0f; foreach (var p in policies) total += CardHeight(p, Card(p)) + 8f;
            var view = new Rect(0, 0, outR.width - 18f, total);
            Widgets.BeginScrollView(outR, ref scrollShop, view);
            float y = 0;
            foreach (var p in policies)
            {
                var cs = Card(p);
                float h = CardHeight(p, cs);
                DrawCard(new Rect(0, y, view.width, h), p, cs, comp);
                y += h + 8f;
            }
            Widgets.EndScrollView();
        }

        static void DrawBundleBanner(Rect r, InsuranceComponent comp)
        {
            int n = comp.Active.Count();
            Widgets.DrawBoxSolid(r, new Color(0.15f, 0.28f, 0.20f, 0.9f));
            GUI.color = Good; Widgets.DrawBox(r); GUI.color = Color.white;
            string text;
            double cur = 1 - InsuranceMath.BundleFactor(n), next = 1 - InsuranceMath.BundleFactor(n + 1);
            if (n == 0) text = Ins.T("UI.Bundle.None");
            else if (n < 3) text = Ins.T("UI.Bundle.Some", n, Ins.Pct(cur), Ins.Pct(1 - InsuranceMath.BundleFactor(3)));
            else text = Ins.T("UI.Bundle.Full", n, Ins.Pct(cur));
            if (InsSettings.Hard && n > 0) text += "  " + Ins.T("UI.Bundle.Retro");
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 10f, r.y, r.width - 20f, r.height), "<b>" + Ins.T("UI.Bundle.Title") + "</b>  " + text);
            Text.Anchor = TextAnchor.UpperLeft;
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
            Widgets.Label(new Rect(x, y, w, 40f), Ins.T("UI.Covers") + " " + def.coversText);
            GUI.color = Color.white; y += 40f;

            // sum / holder first: the tier quotes depend on them
            Pawn holder = null; double sum = cs.sum;
            InsurancePricing.SumRange(def, map, null, out _, out _);
            if (def.kind == PolicyKind.Person)
            {
                var cands = map?.mapPawns?.FreeColonistsSpawned?.Where(p => p.RaceProps.Humanlike).ToList() ?? new List<Pawn>();
                if (cs.holder == null || cs.holder.Dead || !cands.Contains(cs.holder)) cs.holder = cands.FirstOrDefault(p => !comp.Has(def, p)) ?? cands.FirstOrDefault();
                holder = cs.holder;
            }
            InsurancePricing.SumRange(def, map, holder, out double smin, out double smax);
            if (cs.sum < 0 || cs.sum < smin || cs.sum > smax) cs.sum = InsurancePricing.SuggestSum(def, map, holder);
            sum = cs.sum;

            // tier columns
            float colW = (w - 16f) / 3f; float colH = 176f;
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
                    foreach (var p in map?.mapPawns?.FreeColonistsSpawned ?? new List<Pawn>())
                    {
                        var pp = p;
                        opts.Add(new FloatMenuOption(p.LabelShortCap + (comp.Has(def, p) ? " (" + Ins.T("UI.Insured") + ")" : ""), () => cs.holder = pp));
                    }
                    if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
                }
                x += 220f;
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
            GUI.color = premium && badge ? Gold : Pale;
            Widgets.Label(new Rect(x, y, w, 22f), price);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(x, y, w, 22f), Ins.T("UI.Price.Tip", string.Join("\n", q.lines), Ins.Silver(q.premiumQuadrum)));
            y += 22f;

            Text.Font = GameFont.Tiny;
            GUI.color = PaleHi;
            string benefit = def.BenefitFor(tier) ?? "";
            Widgets.Label(new Rect(x, y, w, 50f), benefit);
            y += 52f;
            GUI.color = Grey;
            InsuranceMath.PayoutDelayDays(tier, out int dmin, out int dmax);
            string mech = def.kind == PolicyKind.Colony || def.kind == PolicyKind.Travel && def.riskModel != RiskModel.PerTravelDay
                ? Ins.T("UI.Mech.Property", Ins.Pct(InsuranceMath.Deductible(tier)), Ins.Pct(InsuranceMath.Replacement(tier)))
                : Ins.T("UI.Mech.Person", InsuranceMath.InvalidityThreshold(tier).ToString("0"), Ins.Pct(InsuranceMath.Progression(InsuranceMath.ModelFor(tier), 100) / 100.0));
            Widgets.Label(new Rect(x, y, w, 28f), mech + "  " + (dmax == 0 ? Ins.T("UI.Pay.Instant") : Ins.T("UI.Pay.Days", dmin, dmax)));
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
            float rowH = 112f;
            float total = list.Count * (rowH + 6f) + 80f + (comp.debt > 0.01 ? 40f : 0f);
            if (list.Count == 0) { GUI.color = Grey; Widgets.Label(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 60f), Ins.T("UI.Mine.None")); GUI.color = Color.white; return; }
            var view = new Rect(0, 0, r.width - 18f, total);
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
            foreach (var c in list) { DrawContract(new Rect(0, y, view.width, rowH), c, comp); y += rowH + 6f; }
            y += 6f;
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, view.width, 60f), Ins.T("UI.Mine.Foot"));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            Widgets.EndScrollView();
        }

        static void DrawContract(Rect r, PolicyContract c, InsuranceComponent comp)
        {
            Widgets.DrawBoxSolid(r, c.Active ? BlueDark : new Color(0.12f, 0.12f, 0.12f, 0.8f));
            GUI.color = c.Active ? Blue : Grey; Widgets.DrawBox(r); GUI.color = Color.white;
            float x = r.x + 10f, w = r.width - 20f, y = r.y + 4f;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(x, y, w * 0.6f, 28f), c.Label); Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight; Widgets.Label(new Rect(x + w * 0.5f, y + 2f, w * 0.5f, 24f), "<b>" + Ins.TierName(c.tier) + "</b>"); Text.Anchor = TextAnchor.UpperLeft;
            y += 28f;
            var q = c.Active ? InsurancePricing.QuoteFor(c) : null;
            var sb = new List<string>();
            sb.Add(Ins.T("UI.Mine.Sum", Ins.Silver(c.sum)));
            if (q != null) sb.Add(q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), q.billingDays));
            sb.Add(Ins.T("UI.Mine.NoClaims", c.noClaimsClass, Ins.Pct(1 - InsuranceMath.NoClaimsFactor(c.noClaimsClass))));
            if (c.Active && c.InWaitingPeriod(Ins.Now)) sb.Add(Ins.T("UI.Mine.Waiting", (c.waitUntilTick - Ins.Now).ToStringTicksToPeriod()));
            if (c.discountUntilTick > Ins.Now) sb.Add(Ins.T("UI.Mine.Discount", Ins.Pct(c.discountShare)));
            if (c.owedClawback > 0.01) sb.Add(Ins.T("UI.Mine.Owed", Ins.Silver(c.owedClawback)));
            Widgets.Label(new Rect(x, y, w, 24f), string.Join("  ·  ", sb)); y += 24f;
            GUI.color = Grey; Text.Font = GameFont.Tiny;
            string line2 = Ins.T("UI.Mine.Paid", Ins.Silver(c.premiumPaid), Ins.Silver(c.benefitPaid));
            if (c.riders.Count > 0) line2 += "  ·  " + string.Join(", ", c.riders.Select(k => ClaimLetters.RiderLabel(c.Def, k)));
            if (!c.Active) line2 = Ins.T("UI.Mine.Ended", c.endReason ?? "") + "  ·  " + line2;
            Widgets.Label(new Rect(x, y, w, 20f), line2); y += 18f;
            Text.Font = GameFont.Small; GUI.color = Color.white;
            if (c.Active)
            {
                if (c.CancelPending)
                {
                    GUI.color = Bad;
                    Widgets.Label(new Rect(x, y, w - 200f, 22f), Ins.T("UI.Mine.CancelPending", (c.cancelEffectiveTick - Ins.Now).ToStringTicksToPeriod()) + (c.formErrorHit ? "  " + Ins.T("UI.Mine.FormError") : ""));
                    GUI.color = Color.white;
                    if (Widgets.ButtonText(new Rect(x + w - 190f, y - 2f, 190f, 24f), Ins.T("UI.Mine.Withdraw"))) comp.WithdrawCancel(c);
                }
                else
                {
                    // upgrade nudge (Standard: one calm line; Overbooked: a button)
                    if (c.tier != CoverTier.Premium && InsSettings.TrickActive(InsSettings.TrickUpsell) && InsurancePricing.TierUnlocked(c.Def, CoverTier.Premium, out _))
                    { GUI.color = Gold; Widgets.Label(new Rect(x, y, w - 150f, 22f), Ins.T("UI.Mine.Upsell")); GUI.color = Color.white; }
                    // the small, quiet link
                    string txt = Ins.T("UI.Mine.CancelLink");
                    Text.Font = GameFont.Tiny;
                    var cr = new Rect(r.xMax - 10f - 90f, r.yMax - 20f, 90f, 16f);
                    GUI.color = Mouse.IsOver(cr) ? PaleHi : PaleDim;
                    Text.Anchor = TextAnchor.MiddleRight; Widgets.Label(cr, txt);
                    if (Mouse.IsOver(cr)) { Widgets.DrawLineHorizontal(cr.xMax - Text.CalcSize(txt).x, cr.yMax - 1f, Text.CalcSize(txt).x); }
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
                    if (Widgets.ButtonInvisible(cr)) Find.WindowStack.Add(new Dialog_CancelPolicy(c.id));
                }
            }
        }

        // ---- claims -------------------------------------------------------------------------------------------------------------------------------
        static void DrawClaims(Rect r, InsuranceComponent comp)
        {
            var list = comp.claims.OrderByDescending(c => c.Open).ThenByDescending(c => c.filedTick).ToList();
            float total = 16f; var heights = new Dictionary<int, float>();
            foreach (var c in list) { float h = ClaimHeight(c, r.width - 18f); heights[c.id] = h; total += h + 6f; }
            total += 30f + comp.log.Count * 16f;
            if (list.Count == 0) { GUI.color = Grey; Widgets.Label(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 40f), Ins.T("UI.Claims.None")); GUI.color = Color.white; }
            var view = new Rect(0, 0, r.width - 18f, Math.Max(total, 40f));
            Widgets.BeginScrollView(r, ref scrollClaims, view);
            float y = 0;
            foreach (var c in list) { DrawClaim(new Rect(0, y, view.width, heights[c.id]), c, comp); y += heights[c.id] + 6f; }
            if (comp.log.Count > 0)
            {
                y += 6f; GUI.color = Grey; Widgets.Label(new Rect(0, y, view.width, 24f), "<b>" + Ins.T("UI.Claims.Log") + "</b>"); y += 24f; Text.Font = GameFont.Tiny;
                foreach (var l in Enumerable.Reverse(comp.log).Take(24)) { Widgets.Label(new Rect(8f, y, view.width - 16f, 16f), l); y += 16f; }
                Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            Widgets.EndScrollView();
        }

        static float ClaimHeight(Claim c, float w)
        {
            float h = 58f;
            if (expandedClaims.Contains(c.id)) { Text.Font = GameFont.Tiny; h += Text.CalcHeight(ClaimLetters.Explain(c) + (c.Clause != null ? "\n" + c.Clause.Print(false) : ""), w - 30f) + 10f; Text.Font = GameFont.Small; }
            if (c.state == ClaimState.Decided) h += 30f;
            return h;
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
                string det = ClaimLetters.Explain(c) + (c.Clause != null ? "\n" + Ins.T("Letter.Clause.Cited", c.Clause.Print(false)) : "");
                float dh = Text.CalcHeight(det, w - 20f);
                Widgets.Label(new Rect(x + 6f, y, w - 12f, dh), det); y += dh + 6f;
                Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            if (c.state == ClaimState.Decided)
            {
                float bx = x;
                if (c.outcome != ClaimOutcome.Denied && !c.negotiationClosed)
                { if (Widgets.ButtonText(new Rect(bx, y, 110f, 26f), Ins.T("Btn.Accept"))) comp.Accept(c.id); bx += 116f; }
                if (c.outcome != ClaimOutcome.Approved && !c.negotiationClosed && HSettings.GetBool(InsSettings.Negotiation))
                { if (Widgets.ButtonText(new Rect(bx, y, 130f, 26f), Ins.T("Btn.Negotiate") + " (" + (3 - c.rounds) + ")")) Find.WindowStack.Add(new Dialog_Negotiate(c.id)); bx += 136f; }
                if (c.outcome != ClaimOutcome.Denied && c.materials.Count > 0 && c.tier != CoverTier.BareMinimum)
                {
                    bool mat = c.materialsChosen;
                    Widgets.CheckboxLabeled(new Rect(bx, y + 2f, 240f, 24f), Ins.T("Btn.Materials"), ref mat);
                    c.materialsChosen = mat;
                }
            }
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
                GUI.color = on ? Pale : PaleDim;
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
        public override Vector2 InitialSize => new Vector2(Math.Min(UI.screenWidth - 40f, 1000f), Math.Min(UI.screenHeight - 60f, 760f));

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
