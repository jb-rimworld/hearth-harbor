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
    public enum MarketEventKind { Crash, Rally, Razzia, Hack, BankRun, RugPull, Default, WarEnd, Pump, Quarterly, Listing, Delisting, Signal, ProjectEnd, SolarHalt, Lottery }

    /// <summary>Runtime state of one tradable asset (the price path). History is kept as floats to keep saves small.</summary>
    public sealed class AssetState : IExposable
    {
        public string id, defName;
        public double price, dayOpen;
        public List<float> hourly = new List<float>();     // last 168 hours
        public List<float> daily = new List<float>();      // last 90 closes
        public int listedTick, delistTick = -1, frozenUntil = -1, serial;
        public bool rugged, delisted;
        public double alpha;
        public float signal;                                // decaying system-B signal (log drift units, before cap)

        // not saved
        public AssetDef def; public AssetSpec spec; public double drift; public string specKey; public double lnMa; public double dynHaz = 1, dynPUp;

        public string Label => def?.LabelCap.ToString() ?? defName;
        public string Ticker => def?.Ticker ?? defName;
        public double Change24h => hourly.Count > 24 ? price / hourly[hourly.Count - 25] - 1.0 : (hourly.Count > 0 ? price / hourly[0] - 1.0 : 0);
        public double ChangeDay => dayOpen > 0 ? price / dayOpen - 1.0 : 0;
        public bool Frozen(int tick) => frozenUntil > tick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref defName, "def");
            Scribe_Values.Look(ref price, "price"); Scribe_Values.Look(ref dayOpen, "dayOpen");
            Scribe_Collections.Look(ref hourly, "hourly", LookMode.Value);
            Scribe_Collections.Look(ref daily, "daily", LookMode.Value);
            Scribe_Values.Look(ref listedTick, "listed"); Scribe_Values.Look(ref delistTick, "delist", -1); Scribe_Values.Look(ref frozenUntil, "frozen", -1);
            Scribe_Values.Look(ref serial, "serial"); Scribe_Values.Look(ref rugged, "rugged"); Scribe_Values.Look(ref delisted, "delisted");
            Scribe_Values.Look(ref alpha, "alpha"); Scribe_Values.Look(ref signal, "signal");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { hourly = hourly ?? new List<float>(); daily = daily ?? new List<float>(); }
        }
    }

    /// <summary>The player's holding in one asset: units with 5 decimals (micro-units) and what they cost.</summary>
    public sealed class Position : IExposable
    {
        public string assetId; public long units; public long cost;
        public void ExposeData() { Scribe_Values.Look(ref assetId, "a"); Scribe_Values.Look(ref units, "u"); Scribe_Values.Look(ref cost, "c"); }
    }

    /// <summary>NFT sale in progress: the certificate is gone, the money arrives later (8.5).</summary>
    public sealed class PendingSale : IExposable
    {
        public string assetId; public long units; public int executeTick; public float spread;
        public void ExposeData() { Scribe_Values.Look(ref assetId, "a"); Scribe_Values.Look(ref units, "u"); Scribe_Values.Look(ref executeTick, "t"); Scribe_Values.Look(ref spread, "s"); }
    }

    public sealed class PatienceBond : IExposable
    {
        public int id, termDays, startTick; public long principal; public bool closed;
        public void ExposeData() { Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref termDays, "term"); Scribe_Values.Look(ref startTick, "start"); Scribe_Values.Look(ref principal, "p"); Scribe_Values.Look(ref closed, "closed"); }
        public int HeldDays(int tick) => Math.Max(0, (tick - startTick) / 60000);
        public bool Matured(int tick) => HeldDays(tick) >= termDays;
    }

    public sealed class NewsItem : IExposable
    {
        public int tick; public string text; public bool byYou; public MarketEventKind kind; public string assetId; public float magnitude;
        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "t"); Scribe_Values.Look(ref text, "x"); Scribe_Values.Look(ref byYou, "you");
            Scribe_Values.Look(ref kind, "k"); Scribe_Values.Look(ref assetId, "a"); Scribe_Values.Look(ref magnitude, "m");
        }
    }

    public sealed class EventRecord : IExposable
    {
        public int tick; public MarketEventKind kind; public string assetId;
        public void ExposeData() { Scribe_Values.Look(ref tick, "t"); Scribe_Values.Look(ref kind, "k"); Scribe_Values.Look(ref assetId, "a"); }
    }

    /// <summary>Accounts of the finance products. If the bank module is not there (yet), a local colony account is used.</summary>
    public static class FinanceAccounts
    {
        public static string Colony => BankApi.Impl?.ColonyAccount ?? "bank:colony";
        public static string Market(string assetId) => "market:" + assetId;
        public const string Omen = "market:omen", Projects = "market:projects", Patience = "market:patience", Fees = "bank:fees:finance";
        public const string LotteryGlobalPool = "lottery:global:pool", LotteryGlobalJackpot = "lottery:global:jackpot", LotteryColonyPot = "lottery:colony:pot", LotteryColonyJackpot = "lottery:colony:jackpot";

        /// <summary>Products are available when the bank module is on and (if the bank has accounts) the colony has one.</summary>
        public static bool Available => Modules.On(ModuleId.Bank) && (BankApi.Impl == null || BankApi.Impl.HasAccount);
        public static Money Cash => Ledger.Current?.Balance(Colony) ?? Money.Zero;
        public static bool Post(string from, string to, Money amount, string memo, bool allowNegative = false) => Ledger.Current != null && Ledger.Current.Post(from, to, amount, memo, allowNegative);
    }

    /// <summary>Text helper: Keyed translation with the English fallback from FinanceText (single source for the Keyed file).</summary>
    public static class FT
    {
        public static string T(string key, params object[] args) => Fmt(Raw(key), args);
        // RimWorld's Translate uses named arguments; our Keyed texts use {0},{1} placeholders, so format ourselves.
        static string Fmt(string s, object[] args)
        {
            if (args == null || args.Length == 0) return s;
            for (int i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", args[i]?.ToString() ?? "");
            return s;
        }
        public static string Raw(string key)
        {
            if (key.CanTranslate()) { try { return key.Translate().ToString(); } catch { } }
            return FinanceText.En.TryGetValue(key, out var en) ? en : key;
        }
        public static string Pick(string prefix, RngStream rng, params object[] args)
        {
            int n = 0; while (FinanceText.En.ContainsKey(prefix + "." + (n + 1)) || (prefix + "." + (n + 1)).CanTranslate()) n++;
            if (n == 0) return T(prefix, args);
            int i = rng != null ? rng.Range(1, n + 1) : Rand.RangeInclusive(1, n);
            return Fmt(Raw(prefix + "." + i), args);
        }
        public static string Silver(Money m) => m.ToStringShort() + " silver";
        public static string Silver5(Money m) => m.ToStringFull();
        public static string Pct(double v, int dec = 1) => (v * 100).ToString("+0." + new string('0', dec) + ";-0." + new string('0', dec) + ";0." + new string('0', dec), System.Globalization.CultureInfo.InvariantCulture) + " %";
    }
}
