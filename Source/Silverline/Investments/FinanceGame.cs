// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>
    /// Save-game state of all Silverline investment products (prices, holdings, news, Omen contracts, projects, bets).
    /// Own GameComponent (AGENTS rule 7): removing the mod leaves a loadable save. Ticked from FinanceModule.GameTick.
    /// </summary>
    public sealed partial class FinanceGame : GameComponent
    {
        public static FinanceGame Instance;
        /// <summary>The state if a game is running (creates the component on demand for odd test setups).</summary>
        public static FinanceGame Current
        {
            get
            {
                if (Instance != null) return Instance;
                if (Current_Game() == null) return null;
                var g = Verse.Current.Game.GetComponent<FinanceGame>();
                if (g == null) { g = new FinanceGame(Verse.Current.Game); Verse.Current.Game.components.Add(g); }
                return Instance = g;
            }
        }
        static Game Current_Game() => Verse.Current.Game;

        public List<AssetState> assets = new List<AssetState>();
        public List<Position> positions = new List<Position>();
        public List<PendingSale> pendingSales = new List<PendingSale>();
        public List<PatienceBond> bonds = new List<PatienceBond>();
        public List<NewsItem> news = new List<NewsItem>();
        public List<EventRecord> events = new List<EventRecord>();
        public bool initialized;
        public int lastHourTick = -1, lastDayIndex = -1, nextMemeTick = -1, serialCounter, bondCounter, savingsLockedUntil = -1;
        public int yearIndex = -1; public long yearStartNetWorth, yearGains;
        public long tradeCountDay; public int tradeSalt;

        public FinanceGame(Game game) { Instance = this; }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref assets, "assets", LookMode.Deep);
            Scribe_Collections.Look(ref positions, "positions", LookMode.Deep);
            Scribe_Collections.Look(ref pendingSales, "pendingSales", LookMode.Deep);
            Scribe_Collections.Look(ref bonds, "bonds", LookMode.Deep);
            Scribe_Collections.Look(ref news, "news", LookMode.Deep);
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref lastHourTick, "lastHour", -1); Scribe_Values.Look(ref lastDayIndex, "lastDay", -1);
            Scribe_Values.Look(ref nextMemeTick, "nextMeme", -1); Scribe_Values.Look(ref serialCounter, "serial");
            Scribe_Values.Look(ref bondCounter, "bondCounter"); Scribe_Values.Look(ref savingsLockedUntil, "savingsLocked", -1);
            Scribe_Values.Look(ref yearIndex, "yearIndex", -1); Scribe_Values.Look(ref yearStartNetWorth, "yearStartNW"); Scribe_Values.Look(ref yearGains, "yearGains");
            Scribe_Values.Look(ref tradeSalt, "tradeSalt");
            Scribe_Values.Look(ref tradeCountDay, "tradeCountDay");   // feeds tomorrow's trade salt: a reload must not reset it
            ExposeSignals(); ExposeOmen(); ExposeProjects();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                assets = assets ?? new List<AssetState>(); positions = positions ?? new List<Position>(); pendingSales = pendingSales ?? new List<PendingSale>();
                bonds = bonds ?? new List<PatienceBond>(); news = news ?? new List<NewsItem>(); events = events ?? new List<EventRecord>();
                assets.RemoveAll(a => a == null || a.id == null);
                positions.RemoveAll(p => p == null || p.assetId == null);
                Instance = this;
            }
        }

        // ---- lookups --------------------------------------------------------------------------------
        public AssetState Asset(string id) { for (int i = 0; i < assets.Count; i++) if (assets[i].id == id) return assets[i]; return null; }
        public Position PositionOf(string id) { for (int i = 0; i < positions.Count; i++) if (positions[i].assetId == id) return positions[i]; return null; }
        public long UnitsOf(string id) => PositionOf(id)?.units ?? 0;

        /// <summary>Assets the player may see and trade now (def present, setting groups on, not delisted).</summary>
        public IEnumerable<AssetState> Offered()
        {
            foreach (var a in assets)
            {
                if (a.def == null || a.delisted) continue;
                if (!GroupOn(a.def)) { if (UnitsOf(a.id) == 0) continue; }
                yield return a;
            }
        }
        public static bool GroupOn(AssetDef d)
        {
            if (d.kind == AssetKind.Crypto) return FinanceSettings.CryptoOn;
            if (d.kind == AssetKind.Memecoin) return FinanceSettings.CryptoOn && FinanceSettings.MemeOn;
            if (d.kind == AssetKind.Nft) return FinanceSettings.NftOn;
            return true;
        }

        public Money PortfolioValue()
        {
            long sum = 0;
            foreach (var p in positions) { var a = Asset(p.assetId); if (a != null) sum += MarketMath.ValueOf(p.units, a.price); }
            foreach (var ps in pendingSales) { var a = Asset(ps.assetId); if (a != null) sum += (long)(MarketMath.ValueOf(ps.units, a.price) * (1 - ps.spread)); }
            sum += BondsValue().Micro;
            return new Money(sum);
        }

        /// <summary>Everything invested, split for the bank's net worth: securities (stocks, funds, crypto, NFTs incl. pending sales), patience bonds, money staked in Omen contracts and projects (at cost).</summary>
        public BankGameComp.InvestmentBreakdown InvestmentBreakdown()
        {
            if (!initialized) return default;
            long bonds = BondsValue().Micro;
            return new BankGameComp.InvestmentBreakdown { Securities = PortfolioValue().Micro - bonds, Bonds = bonds, Stakes = (OmenStaked() + ProjectsStaked()).Micro };
        }

        /// <summary>Net worth as the products see it: bank cash + portfolio + money staked in Omen contracts and projects (at cost).</summary>
        public Money NetWorth() => FinanceAccounts.Cash + PortfolioValue() + OmenStaked() + ProjectsStaked();

        public void AddEvent(MarketEventKind kind, string assetId, int tick)
        {
            events.Add(new EventRecord { tick = tick, kind = kind, assetId = assetId });
            if (events.Count > 300) events.RemoveRange(0, events.Count - 300);
        }

        public bool EventBetween(MarketEventKind kind, int from, int to, string assetId = null)
        {
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var e = events[i]; if (e.tick < from) break;
                if (e.kind == kind && e.tick <= to && (assetId == null || e.assetId == assetId)) return true;
            }
            return false;
        }

        public static int Tick => Find.TickManager?.TicksGame ?? 0;
        public static int DayIndex(int tick) => tick / 60000;
    }
}
