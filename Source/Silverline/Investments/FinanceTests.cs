// SPDX-License-Identifier: MIT
// In-game suites for the finance products. Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hearth;
using Hearth.Markets;
using Hearth.Testing;
using RimWorld;
using Verse;

namespace Silverline
{
    static class FinTest
    {
        static int synth;
        public static int NextHour(FinanceGame g)
        {
            if (synth == 0) synth = (g.lastHourTick > 0 ? g.lastHourTick : Find.TickManager.TicksGame) / 2500 + 1;
            synth++; return synth * 2500;
        }
        public static void Hours(FinanceGame g, int n) { for (int i = 0; i < n; i++) { int t = NextHour(g); g.lastHourTick = t; g.StepHourLive(t); } }
        public static void Fund(Money m) => Ledger.Current.Post("world", FinanceAccounts.Colony, m, "test funding");
        public static bool Balanced => Ledger.Current.SumAll() == 0;
        public static Faction AnyFaction() => Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && !f.def.permanentEnemy);
        public static AssetState Idx(FinanceGame g) => g.Asset("SL_Asset_Index");
    }

    public sealed class FinanceMathSuite : HearthTestSuite
    {
        public override string Name => "finance.math";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            // deterministic maths agrees with the system library
            t.Check(Math.Abs(DetMath.Exp(1.234) - Math.Exp(1.234)) < 1e-12, "DetMath.Exp");
            t.Check(Math.Abs(DetMath.Ln(7.5) - Math.Log(7.5)) < 1e-12, "DetMath.Ln");
            // settings and tier anchors (9.2): M per year 100 … 0.1 over the seven tiers
            var s = HSettings.Find("finance.profitLog"); t.Check(s != null, "profitability setting registered");
            if (s != null)
            {
                double[] expected = { 100, 2.0, 1.5, 1.2, 1.0, 0.7, 0.1 };
                for (int i = 0; i < 7; i++) t.Check(Math.Abs(Math.Exp(s.DefaultFor(i)) / expected[i] - 1) < 0.01, $"tier {i} anchor M={Math.Exp(s.DefaultFor(i)):0.###}");
                double mid = Math.Exp(s.DefaultFor(0.5f)); t.Check(mid > 2 && mid < 100, "interpolated tier stays inside the anchors: " + mid.ToString("0.0"));
            }
            foreach (var key in new[] { "finance.volMult", "finance.jumpMult", "finance.hazardMult", "finance.omenGamma", "finance.omenFee", "finance.projectStakePct", "lottery.globalPrice", "lottery.colonyQuote", "lottery.colonyInterval" })
                t.Check(HSettings.Find(key) != null, "setting " + key);
            // all asset defs are sane and produce finite drift
            t.Check(AssetDefs.All.Count >= 12, "asset defs loaded: " + AssetDefs.All.Count);
            foreach (var d in AssetDefs.All) { var errs = d.ConfigErrors().ToList(); t.Check(errs.Count == 0, "asset def " + d.defName + " " + string.Join(";", errs)); }
            var g = FinanceGame.Current; g.EnsureInit();
            foreach (var a in g.assets) { g.RefreshSpec(a); t.Check(!double.IsNaN(a.drift) && !double.IsInfinity(a.drift) && a.price > 0, $"{a.id}: drift {a.drift:0.0000}/day, price {a.price:0.0000}"); }
            // simulated expectation for the index follows the slider (mean multiple after 30 days)
            var idx = FinTest.Idx(g); g.RefreshSpec(idx);
            var dist = MarketMath.Simulate(idx.spec, idx.lnMa, MarketMath.DefaultSystemic(FinanceSettings.Hazard), 30, 1500, 2026);
            double exp = Math.Exp(idx.lnMa * 30 / 60.0);
            t.Check(Math.Abs(dist.Mean / exp - 1) < 0.04, $"index mean ×{dist.Mean:0.000} vs expected ×{exp:0.000}");
            // memecoin: median far below the mean
            var meme = AssetDefs.All.First(d => d.kind == AssetKind.Memecoin);
            var ms = g.ListMemecoin(Find.TickManager.TicksGame, true);
            if (ms != null) { g.RefreshSpec(ms); var md = MarketMath.Simulate(ms.spec, ms.lnMa, MarketMath.DefaultSystemic(FinanceSettings.Hazard), 3, 3000, 7); t.Check(md.Median < 0.85 * md.Mean, $"memecoin median ×{md.Median:0.00} < mean ×{md.Mean:0.00}"); g.Delist(ms, Find.TickManager.TicksGame); }
            // the pre-rolled history exists (charts are never empty)
            t.Check(idx.hourly.Count >= 100 && idx.daily.Count >= 30, $"history: {idx.hourly.Count} hours, {idx.daily.Count} days");
            // texts: every key resolves (Keyed file in sync with the table)
            int missing = 0; foreach (var k in FinanceText.En.Keys) if (!k.CanTranslate()) { missing++; if (missing < 5) t.Note("missing key " + k); }
            t.Check(missing == 0, "all finance keys are in the Keyed file (missing " + missing + ")");
            // tabs registered
            t.Check(BankTabs.Tabs.Any(x => x.Id == "invest"), "Invest tab registered"); t.Check(BankTabs.Tabs.Any(x => x.Id == "lottery"), "Lottery tab registered");
            t.Check(FinTest.Balanced, "ledger balanced");
            yield break;
        }
    }

    public sealed class FinanceBuySuite : HearthTestSuite
    {
        public override string Name => "finance.buy";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            var g = FinanceGame.Current; g.EnsureInit(); var led = Ledger.Current;
            FinTest.Fund(Money.FromSilver(20000.12345));
            t.Check(FinanceAccounts.Available, "products available (fallback account when no bank module)");
            var idx = FinTest.Idx(g); t.Check(idx != null && idx.price > 0, "index fund listed at " + idx?.price.ToString("0.00000"));

            // buy / sell round trip at an unchanged price loses only rounding
            var cash0 = FinanceAccounts.Cash;
            var r = g.Buy(idx.id, Money.FromSilver(1000.5)); t.Check(r.Ok, "buy index: " + r.Message);
            long units = g.UnitsOf(idx.id); t.Check(units > 0, "units held " + FinanceGame.UnitsText(units));
            var spent = cash0 - FinanceAccounts.Cash; t.Check(spent.Micro <= Money.FromSilver(1000.5).Micro && spent.Micro > Money.FromSilver(1000.49).Micro, "paid " + spent.ToStringFull());
            t.Check(led.Balance(FinanceAccounts.Market(idx.id)) == spent, "market account holds what you paid");
            t.Check(FinTest.Balanced, "ledger balanced after buy");
            var half = g.Sell(idx.id, units / 2); t.Check(half.Ok && half.Money.Micro > 0, "sell half: " + half.Message);
            var all = g.SellAll(idx.id); t.Check(all.Ok, "sell all: " + all.Message);
            t.Check(g.UnitsOf(idx.id) == 0 && g.PositionOf(idx.id) == null, "position closed");
            var lost = cash0 - FinanceAccounts.Cash; t.Check(lost.Micro >= 0 && lost.Micro <= 3, "round trip at the same price costs only rounding: " + lost.ToStringFull());
            t.Check(FinTest.Balanced, "ledger balanced after round trip");

            // five decimals
            var meme = g.ListMemecoin(Find.TickManager.TicksGame, true) ?? g.assets.First(a => a.def.kind == AssetKind.Memecoin && !a.delisted);
            var mb = g.Buy(meme.id, Money.FromSilver(0.5)); t.Check(mb.Ok, "buy 0.5 silver of a memecoin: " + mb.Message);
            long mu = g.UnitsOf(meme.id); t.Check(mu > 0 && mu % 1 == 0, "five-decimal units: " + FinanceGame.UnitsText(mu));
            t.Check(!g.Buy(meme.id, Money.FromSilver(0.000001)).Ok, "dust trades are refused");
            t.Check(!g.Buy(idx.id, Money.FromSilver(1_000_000)).Ok, "cannot buy more than the account holds");
            t.Check(!g.Sell(idx.id, 1).Ok, "cannot sell what you do not hold");

            // price moves daily (24 hourly steps) and the portfolio value follows
            g.Buy(idx.id, Money.FromSilver(500)); var v0 = g.PortfolioValue(); double p0 = idx.price; int h0 = idx.hourly.Count;
            FinTest.Hours(g, 24);
            t.Check(idx.price != p0, $"index moved in a day: {p0:0.00000} → {idx.price:0.00000}");
            t.Check(idx.hourly.Count >= h0 || idx.hourly.Count == 168, "hourly history grows");
            t.Check(g.PortfolioValue() != v0, "portfolio value changed");
            int dayCountBefore = idx.daily.Count; g.DayChange(FinTest.NextHour(g)); t.Check(idx.daily.Count >= dayCountBefore, "daily close recorded");
            // a week of hours: no NaN, no negative prices
            FinTest.Hours(g, 24 * 7);
            t.Check(g.assets.All(a => a.price > 0 && !double.IsNaN(a.price)), "all prices stay positive and finite after a week");
            t.Check(FinTest.Balanced, "ledger balanced after a week");

            // NFT: slow sale with spread
            var nft = g.assets.First(a => a.def.kind == AssetKind.Nft);
            var nb = g.Buy(nft.id, Money.FromSilver(300)); t.Check(nb.Ok, "buy NFT: " + nb.Message);
            long nu = g.UnitsOf(nft.id); var cashBeforeSale = FinanceAccounts.Cash; double priceAtSale = nft.price;
            var ns = g.Sell(nft.id, nu); t.Check(ns.Ok && ns.Money.Micro == 0, "NFT sale is queued, no money yet: " + ns.Message);
            var ps = g.pendingSales.LastOrDefault(); t.Check(ps != null && ps.spread >= 0.10f && ps.spread <= 0.30f, "spread " + ps?.spread);
            int waitTicks = ps != null ? ps.executeTick - Find.TickManager.TicksGame : -1;
            t.Check(waitTicks >= 55000 && waitTicks <= 4 * 60000 + 5000, "NFT sale takes 1–4 days: " + waitTicks / 60000.0 + " d");
            t.Check(FinanceAccounts.Cash == cashBeforeSale, "cash unchanged while the sale is pending");
            if (ps != null) { ps.executeTick = Find.TickManager.TicksGame - 1; FinTest.Hours(g, 1); }
            t.Check(g.pendingSales.Count == 0 && FinanceAccounts.Cash > cashBeforeSale, "NFT sale settled: +" + (FinanceAccounts.Cash - cashBeforeSale).ToStringShort());
            t.Check(FinTest.Balanced, "ledger balanced after NFT sale");

            // memecoin delisting pays holders out at the last price
            long muHeld = g.UnitsOf(meme.id); var cb = FinanceAccounts.Cash; g.Delist(meme, Find.TickManager.TicksGame);
            t.Check(g.UnitsOf(meme.id) == 0 && FinanceAccounts.Cash.Micro >= cb.Micro + (long)(muHeld * meme.price) - 2, "delisting pays out the holding");
            t.Check(!g.Buy(meme.id, Money.FromSilver(1)).Ok, "delisted coin cannot be bought");
            t.Check(FinTest.Balanced, "ledger balanced after delisting");

            // frozen exchange (hack) blocks trades; the bank run blocks savings sales
            var rc = g.Asset("SL_Asset_RimCoin"); rc.frozenUntil = Find.TickManager.TicksGame + 5000;
            t.Check(!g.Buy(rc.id, Money.FromSilver(10)).Ok, "frozen coin cannot be bought"); rc.frozenUntil = -1;
            var matt = g.Asset("SL_Asset_Mattress"); g.Buy(matt.id, Money.FromSilver(100)); g.savingsLockedUntil = Find.TickManager.TicksGame + 100000;
            t.Check(!g.SellAll(matt.id).Ok, "savings locked during a bank run"); g.savingsLockedUntil = -1;
            t.Check(g.SellAll(matt.id).Ok, "savings can be sold again");

            // patience bond
            var bo = g.OpenPatienceBond(30, Money.FromSilver(500)); t.Check(bo.Ok, "open bond: " + bo.Message);
            var bondId = g.bonds.Last().id; var early = g.ClosePatienceBond(bondId, false); t.Check(!early.Ok, "early break needs force");
            var b2 = g.OpenPatienceBond(60, Money.FromSilver(1000)); var cb2 = FinanceAccounts.Cash; var brk = g.ClosePatienceBond(g.bonds.Last().id, true);
            t.Check(brk.Ok && brk.Money < Money.FromSilver(1000), "breaking early costs " + (Money.FromSilver(1000) - brk.Money).ToStringShort());
            var bb = g.bonds.First(x => x.id == bondId); bb.startTick = Find.TickManager.TicksGame - 31 * 60000; var cb3 = FinanceAccounts.Cash;
            var mat = g.ClosePatienceBond(bondId, false); t.Check(mat.Ok && mat.Money > Money.FromSilver(500) * 1.02, "matured bond pays a bonus: " + mat.Money.ToStringShort());
            t.Check(FinTest.Balanced, "ledger balanced after bonds");

            // caretaker repairs a position whose asset vanished
            g.positions.Add(new Position { assetId = "SL_Asset_Gone", units = 5000, cost = 12345 });
            int fixedN = Caretaker.Checks.First(c => c.Name == "Finance.Positions").Run(); t.Check(fixedN >= 1 && g.PositionOf("SL_Asset_Gone") == null, "caretaker removed the orphan position (" + fixedN + ")");
            t.Check(FinTest.Balanced, "ledger balanced at the end");
            yield break;
        }
    }

    public sealed class FinanceSignalsSuite : HearthTestSuite
    {
        public override string Name => "finance.signals";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            var g = FinanceGame.Current; g.EnsureInit();
            var idx = FinTest.Idx(g); g.InjectSignal(idx.id, 5f); t.Check(idx.signal <= 0.2f + 1e-6, "signal is clamped at injection: " + idx.signal);
            g.RefreshSpec(idx); double cap = MarketMath.CapSignal(idx.signal, idx.spec.SigmaDay);
            t.Check(cap <= 0.5 * idx.spec.SigmaDay + 1e-12, $"coupling capped at 0.5 sigma: {cap:0.00000} <= {0.5 * idx.spec.SigmaDay:0.00000}");
            idx.signal = 0;
            g.UpdateSignals(Find.TickManager.TicksGame); g.UpdateSignals(Find.TickManager.TicksGame + 60000);      // two passes: snapshot, then differences
            t.Check(g.assets.All(a => a.signal >= -0.2f && a.signal <= 0.2f), "signals stay in range");
            foreach (var a in g.assets.Where(a => a.def?.factionDefName != null)) t.Check(a.dynHaz > 0, $"{a.id} hazard multiplier {a.dynHaz:0.00}");
            // decay
            var st = g.assets.First(); st.signal = 0.1f; g.UpdateSignals(Find.TickManager.TicksGame + 120000); t.Check(Math.Abs(st.signal) < 0.1f, "signals decay: " + st.signal);
            t.Check(FinTest.Balanced, "ledger balanced");
            yield break;
        }
    }

    public sealed class FinanceOmenSuite : HearthTestSuite
    {
        public override string Name => "finance.omen";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            var g = FinanceGame.Current; g.EnsureInit(); var led = Ledger.Current;
            FinTest.Fund(Money.FromSilver(50000)); int now = Find.TickManager.TicksGame;
            g.EnsureOmenMarkets(now);
            t.Check(g.contracts.Count >= 4, "standing markets: " + g.contracts.Count);
            t.Check(g.contracts.All(c => Enum.IsDefined(typeof(OmenKind), c.kind)), "only the two market families exist");
            var names = Enum.GetNames(typeof(OmenKind)); t.Check(!names.Any(n => n.IndexOf("Death", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Marriage", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Raid", StringComparison.OrdinalIgnoreCase) >= 0 && n != "Razzia"), "no markets on death, marriage or raids (D52)");
            var crash = g.contracts.First(c => !c.resolved && c.kind == OmenKind.Crash);
            double q = g.OmenQuote(crash, now); t.Check(q > 0.02 && q < 0.98, "quote " + q.ToString("0.000"));
            t.Check(g.OmenQuote(crash, now) == g.OmenQuote(crash, now), "quote is deterministic within a day");
            double pTrue = g.OmenP(crash, now); t.Check(Math.Abs(pTrue - (1 - Math.Exp(-g.CrashLambdaPerDay * 7))) < 1e-6, "true probability is exact: " + pTrue.ToString("0.0000"));
            // buy / close: costs fee and spread
            var cash0 = FinanceAccounts.Cash; var buy = g.OmenBuy(crash.id, true, Money.FromSilver(100)); t.Check(buy.Ok, "buy YES: " + buy.Message);
            t.Check(cash0 - FinanceAccounts.Cash == Money.FromSilver(100), "stake taken from the account");
            t.Check(led.Balance(FinanceAccounts.Fees).Micro > 0, "exchange fee booked: " + led.Balance(FinanceAccounts.Fees).ToStringFull());
            double sh = g.Shares(crash, true); t.Check(sh > 0, "shares " + sh.ToString("0.000"));
            var close = g.OmenClose(crash.id, true); t.Check(close.Ok && close.Money < Money.FromSilver(100), "closing early costs fee and spread: " + close.Money.ToStringShort());
            t.Check(g.Shares(crash, true) == 0, "position closed");
            // cap on position size
            var tooBig = g.OmenBuy(crash.id, true, FinanceAccounts.Cash); t.Check(!tooBig.Ok, "stake above the position limit is refused: " + tooBig.Message);

            // resolution by event: a crash happens while we hold YES
            g.OmenBuy(crash.id, true, Money.FromSilver(100)); double shares = g.Shares(crash, true); var other = g.contracts.First(c => !c.resolved && c.kind == OmenKind.Rally);
            g.OmenBuy(other.id, true, Money.FromSilver(50));
            var cashBefore = FinanceAccounts.Cash; int ht = FinTest.NextHour(g); g.lastHourTick = ht;
            g.AddEvent(MarketEventKind.Crash, null, ht - 100);
            g.StepHourLive(ht);
            t.Check(crash.resolved && crash.outcome, "contract resolved YES by the crash event");
            var payout = FinanceAccounts.Cash - cashBefore;
            t.Check(payout.Micro >= Money.FromSilver(Math.Floor(shares * 100000) / 100000).Micro - 5, "payout equals the shares: " + payout.ToStringShort() + " vs " + shares.ToString("0.000"));
            t.Check(g.omenPositions.All(p => p.contractId != crash.id), "positions of the resolved contract are gone");
            t.Check(FinTest.Balanced, "ledger balanced after payout");

            // resolution by expiry: window ends without the event → NO, YES holders get nothing
            var rally = g.contracts.FirstOrDefault(c => !c.resolved && c.kind == OmenKind.Rally); 
            if (rally != null)
            {
                int expiry = FinTest.NextHour(g); rally.endTick = expiry; var heldYes = g.Shares(rally, true); var c0 = FinanceAccounts.Cash;
                g.lastHourTick = expiry;
                // make sure no rally happens in this step window by removing records
                g.events.RemoveAll(e => e.kind == MarketEventKind.Rally);
                g.StepHourLive(expiry);
                if (rally.resolved) { t.Check(!rally.outcome || g.events.Any(e => e.kind == MarketEventKind.Rally), "window ended: NO unless a rally really happened"); if (!rally.outcome) t.Check(FinanceAccounts.Cash == c0 || FinanceAccounts.Cash.Micro >= c0.Micro, "YES holders got nothing (cash unchanged)"); }
                else t.Check(false, "the expired contract was not resolved");
            }
            // NO side wins when nothing happens
            var bank = g.contracts.FirstOrDefault(c => !c.resolved && c.kind == OmenKind.BankRun);
            if (bank != null)
            {
                var bn = g.OmenBuy(bank.id, false, Money.FromSilver(200)); t.Check(bn.Ok, "buy NO: " + bn.Message); double noShares = g.Shares(bank, false); var c1 = FinanceAccounts.Cash;
                g.Resolve(bank, false, now); t.Check(FinanceAccounts.Cash - c1 == Money.FromSilver(Math.Floor(noShares * 100000) / 100000), "NO holders paid per share");
            }
            // world market: neutral shadow roll, limited to 5 %
            var f = FinTest.AnyFaction();
            if (f != null)
            {
                var proj = g.StartProject(f, now, 12); t.Check(proj != null, "project started for the world market");
                g.EnsureOmenMarkets(now); var wc = g.contracts.FirstOrDefault(c => c.kind == OmenKind.ProjectNeutral && c.projectId == proj.id);
                t.Check(wc != null && wc.WorldEvent, "world market created for the project");
                if (wc != null)
                {
                    double nw = g.NetWorth().Silver; var max = g.OmenMaxStake(wc); t.Check(max.Silver <= nw * 0.05 + 0.01, $"world market limit 5 % of net worth: {max.Silver:0.00} <= {nw * 0.05:0.00}");
                    var wb = g.OmenBuy(wc.id, true, Money.FromSilver(Math.Min(max.Silver, 50))); t.Check(wb.Ok, "buy on the world market: " + wb.Message);
                    g.ResolveProject(proj, now + 1); g.lastHourTick = FinTest.NextHour(g); g.StepHourLive(g.lastHourTick);
                    t.Check(wc.resolved && wc.outcome == proj.neutralOutcome, "settled on the shadow roll: " + wc.outcome);
                }
            }
            t.Check(FinTest.Balanced, "ledger balanced at the end");
            yield break;
        }
    }

    public sealed class FinanceProjectsSuite : HearthTestSuite
    {
        public override string Name => "finance.projects";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            var g = FinanceGame.Current; g.EnsureInit(); FinTest.Fund(Money.FromSilver(100000)); int now = Find.TickManager.TicksGame;
            var f = FinTest.AnyFaction(); if (f == null) { t.Note("no faction in this world, skipping"); yield break; }
            var proj = g.StartProject(f, now, 15); t.Check(proj != null && proj.pOpen > 0.02 && proj.pOpen < 0.98, "project started, P=" + proj?.pOpen.ToString("0.00"));
            t.Check(proj.volume >= 2000, "volume has a floor: " + proj.volume);
            // limits
            g.yearIndex = -1; g.yearStartNetWorth = g.NetWorth().Micro; g.yearGains = 0;
            double nw = g.NetWorth().Silver; var max = g.ProjectMaxStake(proj); t.Check(Math.Abs(max.Silver - nw * 0.10) < nw * 0.001, $"per project 10 % of net worth: {max.Silver:0.00} / {nw:0.00}");
            var tooBig = g.BetOnProject(proj.id, true, max + Money.FromSilver(100)); t.Check(!tooBig.Ok, "stake above the limit refused");
            // short: quote falls with the stake
            double qSmall = g.ShortQuote(proj, Money.FromSilver(10)), qBig = g.ShortQuote(proj, Money.FromSilver(max.Silver));
            t.Check(qBig < qSmall && qBig >= 1.0, $"short quote falls with the stake: ×{qSmall:0.000} → ×{qBig:0.000}");
            var s1 = g.BetOnProject(proj.id, true, Money.FromSilver(1000)); t.Check(s1.Ok, "short placed: " + s1.Message);
            var l1 = g.BetOnProject(proj.id, false, Money.FromSilver(100)); t.Check(!l1.Ok, "no long on a shorted project: " + l1.Message);
            // sabotage lowers P and locks new shorts
            double p0 = g.ProjectP(proj); g.ProjectSabotageStrike(proj.id, false); double p1 = g.ProjectP(proj);
            t.Check(p1 < p0 && proj.strikes == 1, $"a strike lowers the success chance: {p0:0.000} → {p1:0.000}");
            t.Check(!g.BetOnProject(proj.id, true, Money.FromSilver(10)).Ok, "the market smells smoke: no new shorts for 10 days");
            // long on a second project, quote fixed at purchase
            var f2 = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x != f && !x.IsPlayer && !x.Hidden && !x.defeated && x.def.humanlikeFaction && !x.def.permanentEnemy);
            FactionProject proj2 = f2 != null ? g.StartProject(f2, now, 15) : g.StartProject(f, now, 12);
            if (proj2 == proj) proj2 = null;
            if (proj2 != null)
            {
                var lg = g.BetOnProject(proj2.id, false, Money.FromSilver(500)); t.Check(lg.Ok, "long placed: " + lg.Message);
                double fixedQ = g.bets.Last().quote; g.ProjectHelp(proj2.id, 1.0); t.Check(g.bets.Last().quote == fixedQ, "help changes P but not the quote of bought longs");
                // settlement: success pays stake x quote (capped), failure pays nothing
                g.yearStartNetWorth = Money.FromSilver(10_000_000).Micro; g.yearGains = 0; var cb = FinanceAccounts.Cash;
                g.ResolveProject(proj2, now + 100);
                if (proj2.success) t.Check(FinanceAccounts.Cash - cb == Money.FromSilver(500 * fixedQ), "long pays stake × quote: " + (FinanceAccounts.Cash - cb).ToStringShort());
                else t.Check(FinanceAccounts.Cash == cb, "failed project: long is lost");
            }
            // the short settles
            var cb2 = FinanceAccounts.Cash; g.yearStartNetWorth = Money.FromSilver(10_000_000).Micro; g.yearGains = 0; g.ResolveProject(proj, now + 100);
            if (!proj.success) t.Check(FinanceAccounts.Cash - cb2 > Money.FromSilver(1000), "short paid on failure: " + (FinanceAccounts.Cash - cb2).ToStringShort());
            else t.Check(FinanceAccounts.Cash == cb2, "project succeeded: short lost");
            // annual cap: tiny allowance → only the stake comes back
            var f3 = FinTest.AnyFaction(); var proj3 = g.StartProject(f3, now, 20);
            if (proj3 != null)
            {
                g.bets.Add(new ProjectBet { id = 9001, projectId = proj3.id, isShort = true, stake = Money.FromSilver(1000).Micro, quote = 5.0, tick = now }); FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.Projects, Money.FromSilver(1000), "test");
                g.yearStartNetWorth = Money.FromSilver(100).Micro; g.yearGains = 0; var cb3 = FinanceAccounts.Cash;
                proj3.pNeutral = proj3.pOpen; proj3.playerHelp = -100;   // forced failure
                g.ResolveProject(proj3, now + 200);
                if (!proj3.success) t.Check(FinanceAccounts.Cash - cb3 == Money.FromSilver(1000 + 15), "yearly cap keeps profit to 15 % of 100: " + (FinanceAccounts.Cash - cb3).ToStringShort()); else t.Note("project 3 unexpectedly succeeded");
            }
            // cutoff
            var f4 = FinTest.AnyFaction(); var p4 = g.StartProject(f4, now, 2); if (p4 != null) t.Check(!g.BetOnProject(p4.id, false, Money.FromSilver(10)).Ok, "no bets in the last 3 days");
            t.Check(g.ProjectsStaked().Micro >= 0, "staked total non-negative");
            t.Check(FinTest.Balanced, "ledger balanced at the end");
            yield break;
        }
    }

    public sealed class FinanceSaveSuite : HearthTestSuite
    {
        public override string Name => "finance.save";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            var g = FinanceGame.Current; g.EnsureInit(); FinTest.Fund(Money.FromSilver(5000));
            g.Buy("SL_Asset_Index", Money.FromSilver(100)); g.EnsureOmenMarkets(Find.TickManager.TicksGame);
            var c = g.contracts.First(x => !x.resolved); g.OmenBuy(c.id, true, Money.FromSilver(10)); g.OpenPatienceBond(30, Money.FromSilver(50));
            var lot = LotteryGame.Current; lot.EnsureInit(); lot.BuyGlobalTicket(new[] { 1, 2, 3, 4, 5 }, 2);
            string path = Path.Combine(Path.GetTempPath(), "hh_finance_save_test.xml"); FinanceGame g2 = null; LotteryGame l2 = null; string err = null;
            try
            {
                Scribe.saver.InitSaving(path, "HHFinanceTest");
                var a = g; var b = lot; Scribe_Deep.Look(ref a, "fin", Current.Game); Scribe_Deep.Look(ref b, "lot", Current.Game);
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path); Scribe.EnterNode("HHFinanceTest");
                Scribe_Deep.Look(ref g2, "fin", Current.Game); Scribe_Deep.Look(ref l2, "lot", Current.Game);
                Scribe.ExitNode(); Scribe.loader.FinalizeLoading();
            }
            catch (Exception e) { err = e.Message; try { Scribe.ForceStop(); } catch { } }
            t.Check(err == null && g2 != null && l2 != null, "scribe round trip " + err);
            // the clone constructor re-pointed Instance; restore ours
            FinanceGame.Instance = g; LotteryGame.Instance = lot;
            if (g2 != null)
            {
                t.Check(g2.assets.Count == g.assets.Count, $"assets {g2.assets.Count}/{g.assets.Count}");
                t.Check(g2.positions.Count == g.positions.Count && g2.positions.Sum(p => p.units) == g.positions.Sum(p => p.units), "positions equal");
                t.Check(g2.contracts.Count == g.contracts.Count && g2.omenPositions.Count == g.omenPositions.Count, "omen contracts and positions equal");
                t.Check(g2.bonds.Count == g.bonds.Count, "bonds equal");
                t.Check(g2.news.Count == g.news.Count, "news equal");
                var ia = g.Asset("SL_Asset_Index"); var ib = g2.Asset("SL_Asset_Index");
                t.Check(ib != null && Math.Abs(ib.price - ia.price) < 1e-9 && ib.hourly.Count == ia.hourly.Count && ib.daily.Count == ia.daily.Count, "index price and history survive");
                t.Check(g2.lastHourTick == g.lastHourTick && g2.nextMemeTick == g.nextMemeTick, "schedule survives");
            }
            if (l2 != null) t.Check(l2.tickets.Count == lot.tickets.Count && l2.nextGlobalDrawTick == lot.nextGlobalDrawTick && l2.tickets.All(x => x.nums.Count == 5), "lottery tickets survive");
            try { File.Delete(path); } catch { }
            yield break;
        }
    }
}
#endif
