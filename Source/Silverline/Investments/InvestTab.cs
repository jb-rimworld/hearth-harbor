// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hearth;
using Hearth.Markets;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>The "Invest" tab of the bank window: portfolio, patience bonds, Omen Exchange, faction projects, news ticker (KONZEPT 8.5–8.7).</summary>
    public static class InvestTab
    {
        enum Page { Portfolio, Bonds, Omen, Projects }
        static Page page = Page.Portfolio;
        static string selectedAsset, amountBuf = "100", message; static int messageTick;
        static int chartRange = 1;                                // 0 = 24 h, 1 = 7 d, 2 = 60 d
        static Vector2 listScroll, detailScroll, omenScroll, projScroll, bondScroll;
        static int selectedContract = -1, selectedProject = -1, selectedBondTerm = 60;
        static Dictionary<string, MarketMath.Distribution> distCache = new Dictionary<string, MarketMath.Distribution>();

        public static void Register()
        {
            BankTabs.Register(new BankTab { Id = "invest", LabelKey = "HH.Finance.Tab.Invest", LabelEn = "Invest", Order = 50, Draw = r => Guard.Run(ModuleId.Bank, "InvestTab.Draw", () => Draw(r)), Visible = () => Modules.On(ModuleId.Bank) && FinanceAccounts.Available });
        }

#if HH_TESTS
        public static void TestSetPage(int p) { page = (Page)p; }
        public static void TestSelect(string assetId) { selectedAsset = assetId; }
        public static void TestSelectContract(int id) { selectedContract = id; }
        public static void TestSelectProject(int id) { selectedProject = id; }
#endif

        static void SetMessage(string m) { message = m; messageTick = Find.TickManager?.TicksGame ?? 0; }

        public static int DrawCalls;

        public static void Draw(Rect rect)
        {
            DrawCalls++;
            var g = FinanceGame.Current; if (g == null) return;
            g.EnsureInit(); if (!g.initialized) { FinUi.Label(rect, FT.T("HH.Finance.NotReady")); return; }
            Text.Font = GameFont.Small;
            float y = rect.y;
            DrawHeader(new Rect(rect.x, y, rect.width, 68f), g); y += 70f;
            float footer = 88f;
            var body = new Rect(rect.x, y, rect.width, rect.yMax - y - footer - 4f);
            switch (page)
            {
                case Page.Portfolio: DrawPortfolio(body, g); break;
                case Page.Bonds: DrawBonds(body, g); break;
                case Page.Omen: DrawOmen(body, g); break;
                case Page.Projects: DrawProjects(body, g); break;
            }
            DrawNews(new Rect(rect.x, rect.yMax - footer, rect.width, footer - 18f), g);
            if (page == Page.Omen || page == Page.Projects) FinUi.HelpHint(new Rect(rect.x, rect.yMax - 16f, rect.width, 16f));
            else if (FinUi.Nerd) { }
        }

        // ---- header --------------------------------------------------------------------------------------------------------------
        static void DrawHeader(Rect r, FinanceGame g)
        {
            float x = r.x; var names = new[] { FT.T("HH.Finance.Page.Portfolio"), FT.T("HH.Finance.Page.Bonds"), FT.T("HH.Finance.Page.Omen"), FT.T("HH.Finance.Page.Projects") };
            for (int i = 0; i < 4; i++)
            {
                var b = new Rect(x, r.y, 140f, 26f); x += 144f;
                if (i == 3 && !HSettings.GetBool("finance.projectsOn")) continue;
                if (page == (Page)i) Widgets.DrawBoxSolid(b, new Color(1, 1, 1, 0.12f));
                if (Widgets.ButtonText(b, names[i], page != (Page)i)) page = (Page)i;
            }
            double m = FinanceSettings.M;
            string head = FT.T("HH.Finance.Header", FT.Silver(FinanceAccounts.Cash), FT.Silver(g.PortfolioValue()), m.ToString("0.00", CultureInfo.InvariantCulture), MarketMath.DailyPercent(m).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture));
            if (FinanceGame.FallbackMode) head += "  " + FT.T("HH.Finance.Fallback");
            FinUi.Label(new Rect(r.x, r.y + 28f, r.width, 40f), head, GameFont.Small, FinUi.Dim);
            if (g.MarketHalted) FinUi.Label(new Rect(r.xMax - 330f, r.y, 330f, 26f), FT.T("HH.Finance.Halted"), GameFont.Small, FinUi.Down, TextAnchor.MiddleRight);
            TooltipHandler.TipRegion(new Rect(r.x, r.y + 28f, r.width, 40f), FT.T("HH.Finance.Header.Tip"));
        }

        // ---- news ticker ---------------------------------------------------------------------------------------------------------------
        static Texture2D iconChance, iconYou;
        static void DrawNews(Rect r, FinanceGame g)
        {
            Widgets.DrawBoxSolid(r, FinUi.Panel);
            FinUi.Label(new Rect(r.x + 4, r.y, 160, 16), FT.T("HH.Finance.News.Title"), GameFont.Tiny, FinUi.Dim);
            iconChance = iconChance ?? ContentFinder<Texture2D>.Get("UI/HH/Finance/NewsChance", false);
            iconYou = iconYou ?? ContentFinder<Texture2D>.Get("UI/HH/Finance/NewsYou", false);
            float y = r.y + 16f; int shown = 0;
            for (int i = g.news.Count - 1; i >= 0 && shown < 3; i--, shown++)
            {
                var n = g.news[i]; var row = new Rect(r.x + 4, y, r.width - 8, 18f); y += 18f;
                var icon = n.byYou ? iconYou : iconChance;
                if (icon != null) GUI.DrawTexture(new Rect(row.x, row.y + 1, 16, 16), icon);
                TooltipHandler.TipRegion(new Rect(row.x, row.y, 16, 16), n.byYou ? FT.T("HH.Finance.News.You") : FT.T("HH.Finance.News.Chance"));
                string when = FinUi.DaysLeft(Find.TickManager.TicksGame + (Find.TickManager.TicksGame - n.tick), Find.TickManager.TicksGame).Replace(" d", "d").Replace(" h", "h");
                FinUi.Label(new Rect(row.x + 20, row.y, row.width - 20, row.height), when + " ago · " + n.text, GameFont.Tiny, n.magnitude < 0 ? new Color(1f, 0.82f, 0.8f) : Color.white, TextAnchor.MiddleLeft, false);
            }
            if (g.news.Count == 0) FinUi.Label(new Rect(r.x + 4, r.y + 18, r.width, 18), FT.T("HH.Finance.News.None"), GameFont.Tiny, FinUi.Dim);
        }

        // ---- portfolio -----------------------------------------------------------------------------------------------------------------
        static void DrawPortfolio(Rect r, FinanceGame g)
        {
            var list = g.Offered().OrderBy(a => a.def.order).ThenBy(a => a.Label).ToList();
            if (selectedAsset == null || g.Asset(selectedAsset) == null) selectedAsset = list.FirstOrDefault()?.id;
            var left = new Rect(r.x, r.y, r.width * 0.5f, r.height); var right = new Rect(left.xMax + 8f, r.y, r.width - left.width - 8f, r.height);
            const float rowH = 40f;
            var view = new Rect(0, 0, left.width - 16f, list.Count * rowH + 4);
            Widgets.BeginScrollView(left, ref listScroll, view);
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i]; var row = new Rect(0, i * rowH, view.width, rowH - 2f);
                if (a.id == selectedAsset) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, 0.14f)); else if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, 0.04f));
                Widgets.DrawHighlightIfMouseover(row);
                if (Widgets.ButtonInvisible(row)) { selectedAsset = a.id; detailScroll = Vector2.zero; }
                float nameW = Math.Max(120f, view.width - 240f);
                FinUi.Label(new Rect(row.x + 4, row.y + 1, nameW, 20), a.Label.Truncate(nameW - 4f), GameFont.Small, Color.white, TextAnchor.UpperLeft, false);
                TooltipHandler.TipRegion(new Rect(row.x, row.y, nameW, rowH), a.Label);
                string state = a.Frozen(Find.TickManager.TicksGame) ? FT.T("HH.Finance.Frozen") : a.rugged ? FT.T("HH.Finance.Rugged") : null;
                FinUi.Label(new Rect(row.x + 4, row.y + 21, 96, 16), a.Ticker.Truncate(92f) + (state != null ? " · " + state : ""), GameFont.Tiny, state != null ? FinUi.Down : FinUi.Dim, TextAnchor.UpperLeft, false);
                double ch = a.Change24h;
                FinUi.Label(new Rect(row.x + 104, row.y + 21, 56, 16), FT.Pct(ch), GameFont.Tiny, FinUi.ChangeColor(ch), TextAnchor.UpperLeft, false);
                long u = g.UnitsOf(a.id);
                if (u > 0) FinUi.Label(new Rect(row.x + 160, row.y + 21, Math.Max(60f, view.width - 160f - 236f + 100f), 16), FT.T("HH.Finance.Row.Held", new Money(MarketMath.ValueOf(u, a.price)).ToStringShort()), GameFont.Tiny, FinUi.Up, TextAnchor.UpperRight, false);
                FinUi.Label(new Rect(row.xMax - 236f, row.y + 1, 110, 20), FinUi.Price(a.price), GameFont.Small, Color.white, TextAnchor.UpperRight, false);
                FinUi.Sparkline(new Rect(row.xMax - 118f, row.y + 8, 62, 24), a.hourly, 72);
            }
            Widgets.EndScrollView();
            var sel = g.Asset(selectedAsset);
            var view2 = new Rect(0, 0, right.width - 18f, Math.Max(detailH, right.height));
            Widgets.BeginScrollView(right, ref detailScroll, view2);
            float end = DrawDetail(new Rect(0, 0, view2.width, view2.height), g, sel);
            Widgets.EndScrollView();
            detailH = end + 10f;
        }

        static float detailH = 520f;

        static float DrawDetail(Rect r, FinanceGame g, AssetState a)
        {
            if (a == null) return 0f;
            var d = a.def; float y = r.y;
            string title = a.Label + "  (" + a.Ticker + ")"; float tH = FinUi.TextHeight(r.width, title, GameFont.Medium); FinUi.Label(new Rect(r.x, y, r.width, tH), title, GameFont.Medium); y += tH + 2;
            if (!d.tagline.NullOrEmpty()) { string tg = "“" + d.tagline + "”"; float tgh = FinUi.TextHeight(r.width, tg); FinUi.Label(new Rect(r.x, y, r.width, tgh), tg, GameFont.Small, FinUi.Dim); y += tgh + 2; }
            string desc = d.description ?? "";
            float dh = FinUi.TextHeight(r.width, desc, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y, r.width, dh), desc, GameFont.Tiny, new Color(1, 1, 1, 0.75f)); y += dh + 4;
            FinUi.Label(new Rect(r.x, y, r.width * 0.5f, 24), FinUi.Price(a.price) + " silver", GameFont.Medium, FinUi.ChangeColor(a.Change24h));
            FinUi.Label(new Rect(r.x + r.width * 0.5f, y + 4, r.width * 0.5f, 20), FT.T("HH.Finance.Change24", FT.Pct(a.Change24h, 2), FT.Pct(a.ChangeDay, 2)), GameFont.Small, FinUi.Dim, TextAnchor.UpperRight); y += 28;
            // chart + range buttons
            string[] ranges = { "24 h", "7 d", "60 d" };
            for (int i = 0; i < 3; i++) { var b = new Rect(r.x + i * 52, y, 48, 20); if (chartRange == i) Widgets.DrawBoxSolid(b, new Color(1, 1, 1, 0.15f)); if (Widgets.ButtonText(b, ranges[i], false)) chartRange = i; }
            y += 22;
            var chartRect = new Rect(r.x, y, r.width, 130f); y += 134;
            if (chartRange == 0) FinUi.Chart(chartRect, a.hourly, 24); else if (chartRange == 1) FinUi.Chart(chartRect, a.hourly, 168); else FinUi.Chart(chartRect, a.daily, 90);
            // position
            long units = g.UnitsOf(a.id);
            if (units > 0)
            {
                var pl = g.UnrealizedPL(a.id); var val = new Money(MarketMath.ValueOf(units, a.price));
                FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Position", FinanceGame.UnitsText(units), FT.Silver5(val), FT.Silver(pl)), GameFont.Small, pl.Micro >= 0 ? FinUi.Up : FinUi.Down);
            }
            else FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Position.None"), GameFont.Small, FinUi.Dim);
            y += 24;
            // trade controls
            bool canBuy = g.CanTrade(a, true, out string whyBuy), canSell = g.CanTrade(a, false, out string whySell);
            FinUi.Label(new Rect(r.x, y + 2, 60, 24), FT.T("HH.Finance.Amount"), GameFont.Small, FinUi.Dim);
            amountBuf = Widgets.TextField(new Rect(r.x + 62, y, 110, 26), amountBuf);
            bool parsed = FinUi.TryParseSilver(amountBuf, out Money amount);
            float bx = r.x + 178;
            if (FinUi.Button(new Rect(bx, y, 64, 26), FT.T("HH.Finance.Buy"), canBuy && parsed, canBuy ? FT.T("HH.Finance.Buy.Tip") : whyBuy)) { var res = g.Buy(a.id, amount); SetMessage(res.Message); }
            bx += 68;
            if (FinUi.Button(new Rect(bx, y, 64, 26), FT.T("HH.Finance.Sell"), canSell && units > 0 && parsed, canSell ? FT.T("HH.Finance.Sell.Tip") : whySell))
            {
                long u = MarketMath.UnitsFor(amount.Micro, a.price); var res = g.Sell(a.id, Math.Min(u, units)); SetMessage(res.Message);
            }
            bx += 68;
            if (FinUi.Button(new Rect(bx, y, 74, 26), FT.T("HH.Finance.SellAll"), canSell && units > 0, canSell ? null : whySell)) { var res = g.SellAll(a.id); SetMessage(res.Message); }
            y += 30;
            // presets
            string[] pre = { "10%", "25%", "50%", "100%" }; double[] frac = { 0.1, 0.25, 0.5, 1.0 };
            for (int i = 0; i < 4; i++)
                if (Widgets.ButtonText(new Rect(r.x + 62 + i * 54, y, 50, 20), pre[i], true, true, true))
                    amountBuf = (Math.Floor(FinanceAccounts.Cash.Silver * frac[i] * 100000) / 100000).ToString("0.#####", CultureInfo.InvariantCulture);
            FinUi.Label(new Rect(r.x + 62 + 4 * 54, y, r.width - 62 - 4 * 54, 20), FT.T("HH.Finance.Decimals"), GameFont.Tiny, FinUi.Dim);
            y += 24;
            if (message != null && Find.TickManager.TicksGame - messageTick < 600 * 5) { FinUi.Label(new Rect(r.x, y, r.width, 40), message, GameFont.Small, new Color(1f, 0.92f, 0.6f)); }
            y += 42;
            // risk note + nerd distribution
            string risk = RiskNote(a);
            if (!risk.NullOrEmpty()) { float h = FinUi.TextHeight(r.width, risk, GameFont.Tiny); FinUi.Label(new Rect(r.x, y, r.width, h), risk, GameFont.Tiny, new Color(1f, 0.85f, 0.7f, 0.9f)); y += h + 2; }
            var pending = g.pendingSales.Where(p => p.assetId == a.id).ToList();
            foreach (var p in pending) { FinUi.Label(new Rect(r.x, y, r.width, 16), FT.T("HH.Finance.PendingSale", FinanceGame.UnitsText(p.units), FinUi.DaysLeft(p.executeTick, Find.TickManager.TicksGame), (p.spread * 100).ToString("0")), GameFont.Tiny, FinUi.Dim); y += 16; }
            if (FinUi.Nerd && d.Volatile) DrawDistribution(r, ref y, a);
            return y;
        }

        static string RiskNote(AssetState a)
        {
            switch (a.def.kind)
            {
                case AssetKind.Memecoin: return FT.T("HH.Finance.Risk.Meme");
                case AssetKind.Nft: return FT.T("HH.Finance.Risk.Nft", a.def.saleDaysMin.ToString("0"), a.def.saleDaysMax.ToString("0"), (a.def.saleSpreadMin * 100).ToString("0"), (a.def.saleSpreadMax * 100).ToString("0"));
                case AssetKind.Crypto: return FT.T("HH.Finance.Risk.Crypto");
                case AssetKind.Savings: return FT.T("HH.Finance.Risk.Savings");
                case AssetKind.Bond: case AssetKind.WarBond: return FT.T("HH.Finance.Risk.Bond");
                case AssetKind.FactionStock: return FT.T("HH.Finance.Risk.Stock");
                default: return null;
            }
        }

        static void DrawDistribution(Rect r, ref float y, AssetState a)
        {
            string key = a.id + "|" + FinanceSettings.LnMValue.ToString("0.000") + "|" + FinanceSettings.Vol + "|" + FinanceSettings.Hazard;
            if (!distCache.TryGetValue(key, out var dist))
            {
                var g = FinanceGame.Current; g.RefreshSpec(a);
                dist = MarketMath.Simulate(a.spec, a.lnMa, MarketMath.DefaultSystemic(FinanceSettings.Hazard), a.def.rotating ? a.def.lifeDays : 3, 1500, 4711);
                if (distCache.Count > 20) distCache.Clear(); distCache[key] = dist;
            }
            string t = FT.T("HH.Finance.Nerd.Distribution", dist.Median.ToString("0.00"), dist.Mean.ToString("0.00"), dist.P90.ToString("0.00"), (dist.ShareTotalLoss * 100).ToString("0"), (dist.ShareBelowStart * 100).ToString("0"));
            float h = FinUi.TextHeight(r.width, t, GameFont.Tiny); FinUi.Label(new Rect(r.x, y, r.width, h), t, GameFont.Tiny, FinUi.Dim); y += h;
        }

        // ---- patience bonds -------------------------------------------------------------------------------------------------------------
        static void DrawBonds(Rect r, FinanceGame g)
        {
            float y = r.y;
            string intro = FT.T("HH.Finance.Bond.Intro", (ProjectMath.PatienceLoyalty * 100).ToString("0"), (ProjectMath.PatienceEarlyPenalty * 100).ToString("0"));
            float h = FinUi.TextHeight(r.width, intro); FinUi.Label(new Rect(r.x, y, r.width, h), intro, GameFont.Small, FinUi.Dim); y += h + 8;
            double mSpar = MarketMath.SavingsMultiple(FinanceSettings.M);
            FinUi.Label(new Rect(r.x, y, 60, 24), FT.T("HH.Finance.Amount"), GameFont.Small, FinUi.Dim);
            amountBuf = Widgets.TextField(new Rect(r.x + 62, y, 110, 26), amountBuf); bool parsed = FinUi.TryParseSilver(amountBuf, out Money amount);
            y += 32;
            foreach (int term in FinanceGame.PatienceTerms)
            {
                double mult = ProjectMath.PatienceMultiple(mSpar, term);
                var row = new Rect(r.x, y, r.width, 30);
                FinUi.Label(new Rect(row.x, row.y + 4, 360, 24), FT.T("HH.Finance.Bond.Offer", term.ToString(), mult.ToString("0.000", CultureInfo.InvariantCulture)), GameFont.Small);
                if (FinUi.Button(new Rect(row.x + 380, row.y, 150, 26), FT.T("HH.Finance.Bond.Open"), parsed && FinanceAccounts.Available)) SetMessage(g.OpenPatienceBond(term, amount).Message);
                y += 32;
            }
            if (message != null && Find.TickManager.TicksGame - messageTick < 3000) { FinUi.Label(new Rect(r.x, y, r.width, 40), message, GameFont.Small, new Color(1f, 0.92f, 0.6f)); }
            y += 44;
            FinUi.Label(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Bond.Yours"), GameFont.Medium); y += 28;
            if (g.bonds.Count == 0) { FinUi.Label(new Rect(r.x, y, r.width, 24), FT.T("HH.Finance.Bond.None"), GameFont.Small, FinUi.Dim); return; }
            int t = Find.TickManager.TicksGame;
            foreach (var b in g.bonds.Where(x => !x.closed).ToList())
            {
                var v = g.PatienceValue(b, t); bool done = b.Matured(t);
                FinUi.Label(new Rect(r.x, y + 3, r.width - 200, 24), FT.T("HH.Finance.Bond.Row", b.termDays.ToString(), FT.Silver(new Money(b.principal)), FT.Silver(v), Math.Max(0, b.termDays - b.HeldDays(t)).ToString()), GameFont.Small, done ? FinUi.Up : Color.white);
                if (FinUi.Button(new Rect(r.xMax - 190, y, 190, 26), done ? FT.T("HH.Finance.Bond.Collect") : FT.T("HH.Finance.Bond.Break"), true, done ? null : FT.T("HH.Finance.Bond.Break.Tip")))
                    SetMessage(g.ClosePatienceBond(b.id, true).Message);
                y += 30;
            }
        }

        // ---- Omen Exchange --------------------------------------------------------------------------------------------------------------------
        static void DrawOmen(Rect r, FinanceGame g)
        {
            int t = Find.TickManager.TicksGame;
            var open = g.contracts.OrderBy(c => c.resolved ? 1 : 0).ThenBy(c => c.endTick).ToList();
            if (selectedContract < 0 || g.Contract(selectedContract) == null) selectedContract = open.FirstOrDefault()?.id ?? -1;
            var left = new Rect(r.x, r.y, r.width * 0.55f, r.height); var right = new Rect(left.xMax + 8, r.y, r.width - left.width - 8, r.height);
            FinUi.Label(new Rect(left.x, left.y, left.width, 34), FT.T("HH.Finance.Omen.Intro"), GameFont.Tiny, FinUi.Dim);
            var outR = new Rect(left.x, left.y + 36, left.width, left.height - 36); const float rowH = 40f;
            var view = new Rect(0, 0, outR.width - 16, open.Count * rowH);
            Widgets.BeginScrollView(outR, ref omenScroll, view);
            for (int i = 0; i < open.Count; i++)
            {
                var c = open[i]; var row = new Rect(0, i * rowH, view.width, rowH - 2);
                if (c.id == selectedContract) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, 0.14f)); else if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, 0.04f));
                Widgets.DrawHighlightIfMouseover(row); if (Widgets.ButtonInvisible(row)) selectedContract = c.id;
                FinUi.Label(new Rect(row.x + 4, row.y + 1, row.width - 190, 38), g.OmenTitle(c), GameFont.Small, Color.white);
                double q = g.OmenQuote(c, t);
                string right1 = c.resolved ? FT.T(c.outcome ? "HH.Finance.Omen.Result.Yes" : "HH.Finance.Omen.Result.No") : "YES " + (q * 100).ToString("0") + "%  ·  " + FinUi.DaysLeft(c.endTick, t);
                FinUi.Label(new Rect(row.xMax - 186, row.y + 2, 182, 20), right1, GameFont.Small, c.resolved ? (c.outcome ? FinUi.Up : FinUi.Down) : Color.white, TextAnchor.UpperRight, false);
                bool mine = g.omenPositions.Any(p => p.contractId == c.id);
                FinUi.Label(new Rect(row.xMax - 186, row.y + 22, 182, 16), (c.WorldEvent ? FT.T("HH.Finance.Omen.World") : FT.T("HH.Finance.Omen.Chance")) + (mine ? " · " + FT.T("HH.Finance.Omen.YouHold") : ""), GameFont.Tiny, FinUi.Dim, TextAnchor.UpperRight, false);
            }
            Widgets.EndScrollView();
            DrawContractDetail(right, g, g.Contract(selectedContract));
        }

        static void DrawContractDetail(Rect r, FinanceGame g, OmenContract c)
        {
            if (c == null) return; int t = Find.TickManager.TicksGame; float y = r.y;
            string title = g.OmenTitle(c); float th = FinUi.TextHeight(r.width, title, GameFont.Medium);
            FinUi.Label(new Rect(r.x, y, r.width, th), title, GameFont.Medium); y += th + 4;
            string note = c.WorldEvent ? FT.T("HH.Finance.Omen.WorldNote") : FT.T("HH.Finance.Omen.ChanceNote");
            float nh = FinUi.TextHeight(r.width, note, GameFont.Tiny); FinUi.Label(new Rect(r.x, y, r.width, nh), note, GameFont.Tiny, FinUi.Dim); y += nh + 6;
            double q = g.OmenQuote(c, t), askY = g.AskYes(c, t), askN = g.AskNo(c, t);
            FinUi.Label(new Rect(r.x, y, r.width, 22), FT.T("HH.Finance.Omen.Quote", (q * 100).ToString("0.0"), askY.ToString("0.000"), askN.ToString("0.000")), GameFont.Small); y += 24;
            if (FinUi.Nerd) { FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Omen.Nerd", (g.OmenP(c, t) * 100).ToString("0.00")), GameFont.Tiny, FinUi.Dim); y += 18; }
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Omen.Fee", (FinanceSettings.OmenFee * 100).ToString("0.#")), GameFont.Tiny, FinUi.Dim); y += 22;
            if (!c.resolved)
            {
                FinUi.Label(new Rect(r.x, y + 2, 60, 24), FT.T("HH.Finance.Amount"), GameFont.Small, FinUi.Dim);
                amountBuf = Widgets.TextField(new Rect(r.x + 62, y, 100, 26), amountBuf); bool parsed = FinUi.TryParseSilver(amountBuf, out Money stake);
                bool ok = parsed && FinanceAccounts.Available;
                if (FinUi.Button(new Rect(r.x + 168, y, 80, 26), FT.T("HH.Finance.Omen.BuyYes"), ok)) SetMessage(g.OmenBuy(c.id, true, stake).Message);
                if (FinUi.Button(new Rect(r.x + 252, y, 80, 26), FT.T("HH.Finance.Omen.BuyNo"), ok)) SetMessage(g.OmenBuy(c.id, false, stake).Message);
                y += 30;
                FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Omen.MaxStake", FT.Silver(g.OmenMaxStake(c))), GameFont.Tiny, FinUi.Dim); y += 22;
                foreach (bool yes in new[] { true, false })
                {
                    double sh = g.Shares(c, yes); if (sh <= 0) continue;
                    double bid = yes ? OmenMath.BidYes(q, FinanceSettings.OmenSpread) : OmenMath.BidNo(q, FinanceSettings.OmenSpread);
                    var val = OmenMath.CloseValue(sh, bid, FinanceSettings.OmenFee);
                    FinUi.Label(new Rect(r.x, y + 3, r.width - 150, 22), FT.T("HH.Finance.Omen.Holding", sh.ToString("0.00"), yes ? "YES" : "NO", FT.Silver(Money.FromSilver(val))), GameFont.Small);
                    if (FinUi.Button(new Rect(r.xMax - 140, y, 140, 26), FT.T("HH.Finance.Omen.Close"))) SetMessage(g.OmenClose(c.id, yes).Message);
                    y += 28;
                }
            }
            if (message != null && t - messageTick < 3000) FinUi.Label(new Rect(r.x, y + 4, r.width, 40), message, GameFont.Small, new Color(1f, 0.92f, 0.6f));
        }

        // ---- projects -----------------------------------------------------------------------------------------------------------------------------
        static void DrawProjects(Rect r, FinanceGame g)
        {
            int t = Find.TickManager.TicksGame;
            var list = g.projects.OrderBy(p => p.resolved ? 1 : 0).ThenBy(p => p.endTick).ToList();
            if (selectedProject < 0 || g.Project(selectedProject) == null) selectedProject = list.FirstOrDefault()?.id ?? -1;
            var left = new Rect(r.x, r.y, r.width * 0.5f, r.height); var right = new Rect(left.xMax + 8, r.y, r.width - left.width - 8, r.height);
            FinUi.Label(new Rect(left.x, left.y, left.width, 34), FT.T("HH.Finance.Project.Intro"), GameFont.Tiny, FinUi.Dim);
            var outR = new Rect(left.x, left.y + 36, left.width, left.height - 36); const float rowH = 40f;
            var view = new Rect(0, 0, outR.width - 16, Math.Max(1, list.Count) * rowH);
            Widgets.BeginScrollView(outR, ref projScroll, view);
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i]; var row = new Rect(0, i * rowH, view.width, rowH - 2);
                if (p.id == selectedProject) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, 0.14f)); else if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, 0.04f));
                Widgets.DrawHighlightIfMouseover(row); if (Widgets.ButtonInvisible(row)) selectedProject = p.id;
                FinUi.Label(new Rect(row.x + 4, row.y + 1, row.width - 130, 38), g.ProjectTitle(p), GameFont.Small, Color.white);
                string s = p.resolved ? FT.T(p.success ? "HH.Finance.Project.Done" : "HH.Finance.Project.Failed") : (g.ProjectP(p) * 100).ToString("0") + "% · " + FinUi.DaysLeft(p.endTick, t);
                FinUi.Label(new Rect(row.xMax - 126, row.y + 2, 122, 20), s, GameFont.Small, p.resolved ? (p.success ? FinUi.Up : FinUi.Down) : Color.white, TextAnchor.UpperRight, false);
                var stk = g.ProjectStakeOn(p.id); if (stk.Micro > 0) FinUi.Label(new Rect(row.xMax - 126, row.y + 22, 122, 16), FT.Silver(stk), GameFont.Tiny, FinUi.Dim, TextAnchor.UpperRight, false);
            }
            Widgets.EndScrollView();
            if (list.Count == 0) FinUi.Label(new Rect(left.x, left.y + 40, left.width, 40), FT.T("HH.Finance.Project.None"), GameFont.Small, FinUi.Dim);
            DrawProjectDetail(right, g, g.Project(selectedProject));
        }

        static void DrawProjectDetail(Rect r, FinanceGame g, FactionProject p)
        {
            if (p == null) return; int t = Find.TickManager.TicksGame; float y = r.y;
            string title = g.ProjectTitle(p); float th = FinUi.TextHeight(r.width, title, GameFont.Medium);
            FinUi.Label(new Rect(r.x, y, r.width, th), title, GameFont.Medium); y += th + 4;
            double P = g.ProjectP(p);
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Project.Estimate", (P * 100).ToString("0"), FinUi.DaysLeft(p.endTick, t), p.strikes.ToString()), GameFont.Small); y += 22;
            if (p.resolved) { FinUi.Label(new Rect(r.x, y, r.width, 22), FT.T(p.success ? "HH.Finance.Project.Done" : "HH.Finance.Project.Failed"), GameFont.Small, p.success ? FinUi.Up : FinUi.Down); y += 24; }
            var max = g.ProjectMaxStake(p);
            FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T("HH.Finance.Project.Quotes", g.LongQuote(p, t).ToString("0.00"), ProjectMath.ShortBaseQuote(P).ToString("0.00")), GameFont.Small, FinUi.Dim); y += 22;
            FinUi.Label(new Rect(r.x, y + 2, 60, 24), FT.T("HH.Finance.Amount"), GameFont.Small, FinUi.Dim);
            amountBuf = Widgets.TextField(new Rect(r.x + 62, y, 100, 26), amountBuf); bool parsed = FinUi.TryParseSilver(amountBuf, out Money stake);
            string blockL = g.ProjectBlock(p, false, t), blockS = g.ProjectBlock(p, true, t);
            if (FinUi.Button(new Rect(r.x + 168, y, 80, 26), FT.T("HH.Finance.Project.Long"), parsed && blockL == null, blockL ?? FT.T("HH.Finance.Project.Long.Tip"))) SetMessage(g.BetOnProject(p.id, false, stake).Message);
            if (FinUi.Button(new Rect(r.x + 252, y, 80, 26), FT.T("HH.Finance.Project.Short"), parsed && blockS == null, blockS ?? FT.T("HH.Finance.Project.Short.Tip"))) SetMessage(g.BetOnProject(p.id, true, stake).Message);
            y += 30;
            if (parsed && !p.resolved) FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Project.ShortAt", g.ShortQuote(p, stake).ToString("0.00")), GameFont.Tiny, FinUi.Dim);
            y += 20;
            FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Project.MaxStake", FT.Silver(max), FT.Silver(g.ProjectsStaked())), GameFont.Tiny, FinUi.Dim); y += 20;
            double room = Math.Max(0, g.yearStartNetWorth * FinanceSettings.ProjectLimits().YearGainPct - g.yearGains) / Money.PerSilver;
            FinUi.Label(new Rect(r.x, y, r.width, 18), FT.T("HH.Finance.Project.YearCap", FT.Silver(Money.FromSilver(room))), GameFont.Tiny, FinUi.Dim); y += 24;
            foreach (var b in g.bets.Where(x => x.projectId == p.id && !x.settled))
            { FinUi.Label(new Rect(r.x, y, r.width, 20), FT.T(b.isShort ? "HH.Finance.Project.YourShort" : "HH.Finance.Project.YourLong", FT.Silver(new Money(b.stake)), b.quote.ToString("0.00")), GameFont.Small); y += 22; }
            string sab = FT.T("HH.Finance.Project.SabotageNote"); float sh = FinUi.TextHeight(r.width, sab, GameFont.Tiny);
            FinUi.Label(new Rect(r.x, y + 4, r.width, sh), sab, GameFont.Tiny, FinUi.Dim); y += sh + 8;
            if (Prefs.DevMode && !p.resolved)
            {
                if (Widgets.ButtonText(new Rect(r.x, y, 150, 22), "Dev: sabotage strike")) SetMessage(g.ProjectSabotageStrike(p.id, false) ? "detected" : "unseen");
                if (Widgets.ButtonText(new Rect(r.x + 156, y, 120, 22), "Dev: help")) g.ProjectHelp(p.id, 0.5);
            }
            if (message != null && t - messageTick < 3000) FinUi.Label(new Rect(r.x, y + 26, r.width, 40), message, GameFont.Small, new Color(1f, 0.92f, 0.6f));
        }
    }

}
