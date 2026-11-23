// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using Verse;

namespace Silverline
{
    public enum OmenKind { Crash, Rally, Razzia, Hack, BankRun, MemeRug, ProjectNeutral }

    /// <summary>
    /// A binary Omen Exchange contract (KONZEPT 8.6, D52). Two kinds of markets only: (a) events of system A with exactly known odds,
    /// (b) world events whose resolution ignores what the player does (faction projects are settled by a neutral shadow roll,
    /// not by the real outcome at the building site). No markets on marriage, death, colonists, enmity or raids.
    /// </summary>
    public sealed class OmenContract : IExposable
    {
        public int id; public OmenKind kind; public string assetId; public int projectId;
        public int startTick, endTick, resolvedTick = -1; public bool resolved, outcome;
        public bool WorldEvent => kind == OmenKind.ProjectNeutral;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref assetId, "asset"); Scribe_Values.Look(ref projectId, "project");
            Scribe_Values.Look(ref startTick, "start"); Scribe_Values.Look(ref endTick, "end"); Scribe_Values.Look(ref resolvedTick, "resolvedTick", -1);
            Scribe_Values.Look(ref resolved, "resolved"); Scribe_Values.Look(ref outcome, "outcome");
        }
    }

    public sealed class OmenPosition : IExposable
    {
        public int contractId; public bool yes; public double shares; public long paid; public int tick;
        public void ExposeData() { Scribe_Values.Look(ref contractId, "c"); Scribe_Values.Look(ref yes, "yes"); ScribeMoney.LookExact(ref shares, "sh"); Scribe_Values.Look(ref paid, "paid"); Scribe_Values.Look(ref tick, "t"); }
    }

    public sealed partial class FinanceGame
    {
        public List<OmenContract> contracts = new List<OmenContract>();
        public List<OmenPosition> omenPositions = new List<OmenPosition>();
        int omenCounter;

        void ExposeOmen()
        {
            Scribe_Collections.Look(ref contracts, "omenContracts", LookMode.Deep);
            Scribe_Collections.Look(ref omenPositions, "omenPositions", LookMode.Deep);
            Scribe_Values.Look(ref omenCounter, "omenCounter");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { contracts = contracts ?? new List<OmenContract>(); omenPositions = omenPositions ?? new List<OmenPosition>(); }
        }

        public Money OmenStaked() { long s = 0; foreach (var p in omenPositions) s += p.paid; return new Money(s); }
        bool OmenReferences(string assetId) => contracts.Any(c => !c.resolved && c.assetId == assetId) || omenPositions.Any(p => contracts.Any(c => c.id == p.contractId && c.assetId == assetId));

        public OmenContract Contract(int id) => contracts.FirstOrDefault(c => c.id == id);

        // ---- odds and quotes ---------------------------------------------------------------------------------------
        public double RemainingDays(OmenContract c, int tick) => Math.Max(0, (c.endTick - tick) / (double)Day);

        /// <summary>True probability of YES for the time left (exactly known for system A).</summary>
        public double OmenP(OmenContract c, int tick)
        {
            if (c.resolved) return c.outcome ? 1 : 0;
            double d = RemainingDays(c, tick);
            switch (c.kind)
            {
                case OmenKind.Crash: return OmenMath.EventProbability(CrashLambdaPerDay, d);
                case OmenKind.Rally: return OmenMath.EventProbability(RallyLambdaPerDay, d);
                case OmenKind.Razzia: return OmenMath.EventProbability(RazziaLambdaPerDay, d);
                case OmenKind.BankRun: return OmenMath.EventProbability(BankRunLambdaPerDay, d);
                case OmenKind.Hack: { var a = Asset(c.assetId); return a?.def == null ? 0 : OmenMath.EventProbability(HackLambdaPerDay(a.def), d); }
                case OmenKind.MemeRug:
                    {
                        var a = Asset(c.assetId); if (a?.def == null || a.rugged) return a != null && a.rugged ? 1 : 0;
                        double life = Math.Max(0, (a.listedTick + a.def.lifeDays * Day - tick) / Day);
                        return OmenMath.EventProbability(a.def.hazard * FinanceSettings.Hazard, Math.Min(d, life));
                    }
                case OmenKind.ProjectNeutral: { var p = projects.FirstOrDefault(x => x.id == c.projectId); return p == null ? 0.5 : p.pNeutral; }
            }
            return 0.5;
        }

        public double OmenQuote(OmenContract c, int tick)
        {
            if (c.resolved) return c.outcome ? 0.99 : 0.01;
            double noise = OmenMath.Noise(Seed(), (ulong)c.id, DayIndex(tick));
            return OmenMath.Quote(OmenP(c, tick), FinanceSettings.OmenGamma, noise);
        }

        public double AskYes(OmenContract c, int tick) => OmenMath.AskYes(OmenQuote(c, tick), FinanceSettings.OmenSpread);
        public double AskNo(OmenContract c, int tick) => OmenMath.AskNo(OmenQuote(c, tick), FinanceSettings.OmenSpread);

        // ---- trading ------------------------------------------------------------------------------------------------
        public double Staked(OmenContract c) { long s = 0; foreach (var p in omenPositions) if (p.contractId == c.id) s += p.paid; return s / (double)MarketMath.UnitsPerOne; }
        public double Shares(OmenContract c, bool yes) { double s = 0; foreach (var p in omenPositions) if (p.contractId == c.id && p.yes == yes) s += p.shares; return s; }

        public Money OmenMaxStake(OmenContract c)
        {
            double nw = NetWorth().Silver;
            double cap = OmenMath.PositionCap(nw, HSettings.Get(c.WorldEvent ? "finance.omenCapB" : "finance.omenCapA"));
            return Money.FromSilver(Math.Max(0, Math.Min(cap - Staked(c), FinanceAccounts.Cash.Silver)));
        }

        public TradeResult OmenBuy(int contractId, bool yes, Money stake)
        {
            var c = Contract(contractId); int t = Tick;
            if (!FinanceAccounts.Available) return TradeResult.Fail("HH.Finance.Trade.NoBank");
            if (c == null || c.resolved || c.endTick - t < Hour) return TradeResult.Fail("HH.Finance.Omen.Closed");
            if (MarketHalted) return TradeResult.Fail("HH.Finance.Trade.Halted");
            if (stake.Micro < MinTradeMicro) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            if (FinanceAccounts.Cash < stake) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            if (stake > OmenMaxStake(c)) return TradeResult.Fail("HH.Finance.Omen.Limit", FT.Silver(OmenMaxStake(c)));
            double ask = yes ? AskYes(c, t) : AskNo(c, t), fee = FinanceSettings.OmenFee;
            double shares = OmenMath.SharesFor(stake.Silver, ask, fee);
            var feeM = Money.FromSilver(OmenMath.FeeOf(stake.Silver, fee));
            if (!FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.Omen, stake - feeM, "Omen Exchange: " + (yes ? "YES " : "NO ") + OmenTitle(c))) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.Fees, feeM, "Omen fee");
            omenPositions.Add(new OmenPosition { contractId = c.id, yes = yes, shares = shares, paid = stake.Micro, tick = t });
            tradeCountDay++;
            return new TradeResult { Ok = true, Money = stake, Message = FT.T("HH.Finance.Omen.Bought", shares.ToString("0.00"), yes ? "YES" : "NO", FT.Silver(stake)) };
        }

        public TradeResult OmenClose(int contractId, bool yes)
        {
            var c = Contract(contractId); int t = Tick;
            if (c == null || c.resolved) return TradeResult.Fail("HH.Finance.Omen.Closed");
            double shares = Shares(c, yes); if (shares <= 0) return TradeResult.Fail("HH.Finance.Trade.NothingToSell");
            double q = OmenQuote(c, t), spread = FinanceSettings.OmenSpread;
            double bid = yes ? OmenMath.BidYes(q, spread) : OmenMath.BidNo(q, spread);
            var proceeds = Money.FromSilver(OmenMath.CloseValue(shares, bid, FinanceSettings.OmenFee));
            FinanceAccounts.Post(FinanceAccounts.Omen, FinanceAccounts.Colony, proceeds, "Omen Exchange: closed " + OmenTitle(c), true);
            omenPositions.RemoveAll(p => p.contractId == c.id && p.yes == yes); tradeCountDay++;
            return new TradeResult { Ok = true, Money = proceeds, Message = FT.T("HH.Finance.Omen.Closed.Ok", FT.Silver(proceeds)) };
        }

        // ---- contracts life cycle -------------------------------------------------------------------------------------
        public string OmenTitle(OmenContract c)
        {
            switch (c.kind)
            {
                case OmenKind.Crash: return FT.T("HH.Finance.Omen.Title.Crash", Math.Round((c.endTick - c.startTick) / (double)Day).ToString());
                case OmenKind.Rally: return FT.T("HH.Finance.Omen.Title.Rally", Math.Round((c.endTick - c.startTick) / (double)Day).ToString());
                case OmenKind.Razzia: return FT.T("HH.Finance.Omen.Title.Razzia", Math.Round((c.endTick - c.startTick) / (double)Day).ToString());
                case OmenKind.BankRun: return FT.T("HH.Finance.Omen.Title.BankRun", Math.Round((c.endTick - c.startTick) / (double)Day).ToString());
                case OmenKind.Hack: return FT.T("HH.Finance.Omen.Title.Hack", Asset(c.assetId)?.Label ?? "?", Math.Round((c.endTick - c.startTick) / (double)Day).ToString());
                case OmenKind.MemeRug: return FT.T("HH.Finance.Omen.Title.Rug", Asset(c.assetId)?.Label ?? "?");
                case OmenKind.ProjectNeutral: { var p = projects.FirstOrDefault(x => x.id == c.projectId); return FT.T("HH.Finance.Omen.Title.Project", p != null ? ProjectTitle(p) : "?"); }
            }
            return "?";
        }

        OmenContract NewContract(OmenKind kind, int tick, double days, string assetId = null, int projectId = 0)
        {
            var c = new OmenContract { id = ++omenCounter, kind = kind, assetId = assetId, projectId = projectId, startTick = tick, endTick = tick + (int)(days * Day) };
            contracts.Add(c); return c;
        }

        bool HasOpen(OmenKind k, string assetId = null, int projectId = 0) => contracts.Any(c => !c.resolved && c.kind == k && c.assetId == assetId && c.projectId == projectId);

        /// <summary>Make sure the standing markets exist (called daily and after load).</summary>
        public void EnsureOmenMarkets(int tick)
        {
            if (!HasOpen(OmenKind.Crash)) NewContract(OmenKind.Crash, tick, 7);
            if (!HasOpen(OmenKind.Rally)) NewContract(OmenKind.Rally, tick, 5);
            if (!HasOpen(OmenKind.BankRun)) NewContract(OmenKind.BankRun, tick, 15);
            if (FinanceSettings.CryptoOn)
            {
                if (!HasOpen(OmenKind.Razzia)) NewContract(OmenKind.Razzia, tick, 10);
                foreach (var a in assets.Where(x => !x.delisted && x.def != null && x.def.kind == AssetKind.Crypto && x.def.hazard > 0))
                    if (!HasOpen(OmenKind.Hack, a.id)) NewContract(OmenKind.Hack, tick, 15, a.id);
                if (FinanceSettings.MemeOn)
                    foreach (var a in assets.Where(x => !x.delisted && x.def != null && x.def.kind == AssetKind.Memecoin && !x.rugged))
                        if (!HasOpen(OmenKind.MemeRug, a.id)) { var c = NewContract(OmenKind.MemeRug, tick, 1); c.endTick = a.listedTick + (int)(a.def.lifeDays * Day); }
            }
            if (HSettings.GetBool("finance.projectsOn"))
                foreach (var p in projects.Where(x => !x.resolved))
                    if (!HasOpen(OmenKind.ProjectNeutral, null, p.id) && p.endTick - tick > 3 * Day) { var c = NewContract(OmenKind.ProjectNeutral, tick, 1, null, p.id); c.endTick = p.endTick; }
        }

        void OmenDay(int tick)
        {
            EnsureOmenMarkets(tick);
            contracts.RemoveAll(c => c.resolved && tick - c.resolvedTick > 3 * Day && !omenPositions.Any(p => p.contractId == c.id));
        }

        /// <summary>Hourly: settle contracts whose event happened (YES at once) or whose window ended (NO).</summary>
        void OmenHour(int tick)
        {
            for (int i = 0; i < contracts.Count; i++)
            {
                var c = contracts[i]; if (c.resolved) continue;
                if (c.kind == OmenKind.ProjectNeutral)
                {
                    var p = projects.FirstOrDefault(x => x.id == c.projectId);
                    if (p == null) { Resolve(c, false, tick); continue; }
                    if (p.resolved) Resolve(c, p.neutralOutcome, tick);
                    continue;
                }
                bool hit = false;
                switch (c.kind)
                {
                    case OmenKind.Crash: hit = EventBetween(MarketEventKind.Crash, c.startTick, tick); break;
                    case OmenKind.Rally: hit = EventBetween(MarketEventKind.Rally, c.startTick, tick); break;
                    case OmenKind.Razzia: hit = EventBetween(MarketEventKind.Razzia, c.startTick, tick); break;
                    case OmenKind.BankRun: hit = EventBetween(MarketEventKind.BankRun, c.startTick, tick); break;
                    case OmenKind.Hack: hit = EventBetween(MarketEventKind.Hack, c.startTick, tick, c.assetId); break;
                    case OmenKind.MemeRug: hit = EventBetween(MarketEventKind.RugPull, c.startTick, tick, c.assetId); break;
                }
                if (hit) Resolve(c, true, tick);
                else if (tick >= c.endTick) Resolve(c, false, tick);
            }
        }

        /// <summary>Settle a contract. Public for tests.</summary>
        public void Resolve(OmenContract c, bool outcome, int tick)
        {
            if (c.resolved) return;
            c.resolved = true; c.outcome = outcome; c.resolvedTick = tick;
            long totalIn = 0, totalOut = 0;
            foreach (var p in omenPositions.Where(x => x.contractId == c.id).ToList())
            {
                totalIn += p.paid;
                if (p.yes == outcome)
                {
                    var pay = Money.FromSilver(Math.Floor(p.shares * MarketMath.UnitsPerOne) / MarketMath.UnitsPerOne);
                    FinanceAccounts.Post(FinanceAccounts.Omen, FinanceAccounts.Colony, pay, "Omen Exchange payout: " + OmenTitle(c), true);
                    totalOut += pay.Micro;
                }
                omenPositions.Remove(p);
            }
            Push(MarketEventKind.Signal, FT.T(outcome ? "HH.Finance.Omen.Settled.Yes" : "HH.Finance.Omen.Settled.No", OmenTitle(c)), false, c.assetId, 0, tick, totalIn > 0, null);
        }
    }
}
