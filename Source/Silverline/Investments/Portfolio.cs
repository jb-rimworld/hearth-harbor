// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using Verse;

namespace Silverline
{
    public struct TradeResult
    {
        public bool Ok; public string Message; public Money Money; public long Units;
        public static TradeResult Fail(string key, params object[] a) => new TradeResult { Ok = false, Message = FT.T(key, a) };
    }

    /// <summary>Buying, selling, NFT sale queue and the patience bonds (Sparvertrag). All money goes through the ledger (bank:colony ⇄ market:&lt;asset&gt;).</summary>
    public sealed partial class FinanceGame
    {
        public const long MinTradeMicro = 1000;          // 0.01 silver

        public bool CanTrade(AssetState a, bool buying, out string reason)
        {
            reason = null;
            if (a == null || a.def == null) { reason = FT.T("HH.Finance.Trade.Unknown"); return false; }
            if (!FinanceAccounts.Available) { reason = FT.T("HH.Finance.Trade.NoBank"); return false; }
            if (MarketHalted) { reason = FT.T("HH.Finance.Trade.Halted"); return false; }
            int t = Tick;
            if (a.Frozen(t)) { reason = FT.T("HH.Finance.Trade.Frozen", a.Label); return false; }
            if (a.delisted) { reason = FT.T("HH.Finance.Trade.Delisted", a.Label); return false; }
            if (buying && !GroupOn(a.def)) { reason = FT.T("HH.Finance.Trade.Off"); return false; }
            if (!buying && a.def.IsSavings && savingsLockedUntil > t) { reason = FT.T("HH.Finance.Trade.BankRun"); return false; }
            return true;
        }

        public TradeResult Buy(string assetId, Money amount)
        {
            var a = Asset(assetId);
            if (!CanTrade(a, true, out var why)) return new TradeResult { Message = why };
            if (amount.Micro < MinTradeMicro) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            if (FinanceAccounts.Cash < amount) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            long units = MarketMath.UnitsFor(amount.Micro, a.price);
            if (units <= 0) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            long cost = (long)Math.Ceiling(units * a.price - 1e-9);
            if (cost > amount.Micro) cost = amount.Micro;
            if (cost <= 0) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            var c = new Money(cost);
            if (!FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.Market(a.id), c, "Silverline: bought " + a.Label)) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            var pos = PositionOf(a.id); if (pos == null) { pos = new Position { assetId = a.id }; positions.Add(pos); }
            pos.units += units; pos.cost += cost; tradeCountDay++;
            return new TradeResult { Ok = true, Money = c, Units = units, Message = FT.T("HH.Finance.Trade.Bought", UnitsText(units), a.Label, FT.Silver5(c)) };
        }

        public TradeResult Sell(string assetId, long units)
        {
            var a = Asset(assetId);
            if (!CanTrade(a, false, out var why)) return new TradeResult { Message = why };
            var pos = PositionOf(assetId);
            if (pos == null || pos.units <= 0) return TradeResult.Fail("HH.Finance.Trade.NothingToSell");
            units = Math.Min(units, pos.units);
            if (units <= 0) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            long costShare = pos.units > 0 ? (long)Math.Round((double)pos.cost * units / pos.units) : 0;
            if (a.def.kind == AssetKind.Nft)
            {
                var rng = JRand.Sealed("finance.nft");
                double days = a.def.saleDaysMin + (a.def.saleDaysMax - a.def.saleDaysMin) * rng.NextDouble();
                float spread = (float)(a.def.saleSpreadMin + (a.def.saleSpreadMax - a.def.saleSpreadMin) * rng.NextDouble());
                pendingSales.Add(new PendingSale { assetId = assetId, units = units, executeTick = Tick + (int)(days * Day), spread = spread });
                RemoveUnits(pos, units, costShare);
                tradeCountDay++;
                return new TradeResult { Ok = true, Units = units, Message = FT.T("HH.Finance.Trade.NftQueued", a.Label, days.ToString("0.0"), (spread * 100).ToString("0")) };
            }
            long value = (long)Math.Floor(units * a.price + 1e-9);
            var m = new Money(Math.Max(0, value));
            if (m.Micro > 0 && !FinanceAccounts.Post(FinanceAccounts.Market(a.id), FinanceAccounts.Colony, m, "Silverline: sold " + a.Label, true)) return TradeResult.Fail("HH.Finance.Trade.Unknown");
            RemoveUnits(pos, units, costShare); tradeCountDay++;
            return new TradeResult { Ok = true, Money = m, Units = units, Message = FT.T("HH.Finance.Trade.Sold", UnitsText(units), a.Label, FT.Silver5(m)) };
        }

        public TradeResult SellAll(string assetId) => Sell(assetId, UnitsOf(assetId));

        void RemoveUnits(Position pos, long units, long costShare)
        {
            pos.units -= units; pos.cost = Math.Max(0, pos.cost - costShare);
            if (pos.units <= 0) positions.Remove(pos);
        }

        public static string UnitsText(long units) => (units / (double)MarketMath.UnitsPerOne).ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

        void ExecutePendingSales(int tick)
        {
            for (int i = pendingSales.Count - 1; i >= 0; i--)
            {
                var ps = pendingSales[i]; if (ps.executeTick > tick) continue;
                var a = Asset(ps.assetId); pendingSales.RemoveAt(i);
                if (a == null) continue;
                long value = (long)Math.Floor(ps.units * a.price * (1 - ps.spread));
                var m = new Money(Math.Max(0, value));
                if (m.Micro > 0) FinanceAccounts.Post(FinanceAccounts.Market(a.id), FinanceAccounts.Colony, m, "Silverline: NFT sale settled " + a.Label, true);
                Push(MarketEventKind.Signal, FT.T("HH.Finance.News.NftSold", a.Label, FT.Silver(m)), true, a.id, 0.05f, tick, true, null);
            }
        }

        /// <summary>Gain (or loss) of one position against its cost.</summary>
        public Money UnrealizedPL(string assetId)
        {
            var p = PositionOf(assetId); var a = Asset(assetId); if (p == null || a == null) return Money.Zero;
            return new Money(MarketMath.ValueOf(p.units, a.price) - p.cost);
        }

        // ---- patience bonds ("Fixed Term Patience Bond", 8.5) -------------------------------------------------------------
        public static readonly int[] PatienceTerms = { 30, 60, 120 };

        double SavingsLnM() => DetMath.Ln(MarketMath.SavingsMultiple(FinanceSettings.M));

        public Money PatienceValue(PatienceBond b, int tick) => new Money((long)Math.Round(ProjectMath.PatienceValue(b.principal, SavingsLnM(), b.termDays, b.HeldDays(tick))));
        public Money BondsValue() { long s = 0; int t = Tick; foreach (var b in bonds) if (!b.closed) s += PatienceValue(b, t).Micro; return new Money(s); }

        public TradeResult OpenPatienceBond(int termDays, Money amount)
        {
            if (!FinanceAccounts.Available) return TradeResult.Fail("HH.Finance.Trade.NoBank");
            if (!PatienceTerms.Contains(termDays)) return TradeResult.Fail("HH.Finance.Trade.Unknown");
            if (amount.Micro < MinTradeMicro) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            if (FinanceAccounts.Cash < amount) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            if (!FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.Patience, amount, "Silverline: Patience Bond opened")) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            bonds.Add(new PatienceBond { id = ++bondCounter, termDays = termDays, startTick = Tick, principal = amount.Micro });
            return new TradeResult { Ok = true, Money = amount, Message = FT.T("HH.Finance.Bond.Opened", termDays.ToString(), FT.Silver(amount)) };
        }

        public TradeResult ClosePatienceBond(int id, bool force)
        {
            var b = bonds.FirstOrDefault(x => x.id == id && !x.closed);
            if (b == null) return TradeResult.Fail("HH.Finance.Trade.Unknown");
            int t = Tick; bool matured = b.Matured(t);
            if (!matured && !force) return TradeResult.Fail("HH.Finance.Bond.Early");
            var v = PatienceValue(b, t);
            var pay = matured ? v * (1 + ProjectMath.PatienceLoyalty) : v * (1 - ProjectMath.PatienceEarlyPenalty);
            FinanceAccounts.Post(FinanceAccounts.Patience, FinanceAccounts.Colony, pay, "Silverline: Patience Bond " + (matured ? "matured" : "broken early"), true);
            b.closed = true;
            return new TradeResult { Ok = true, Money = pay, Message = FT.T(matured ? "HH.Finance.Bond.Matured" : "HH.Finance.Bond.Broken", FT.Silver(pay)) };
        }

        void BondsDay(int tick)
        {
            foreach (var b in bonds.ToList())
                if (!b.closed && b.Matured(tick)) { var r = ClosePatienceBond(b.id, false); if (r.Ok) Push(MarketEventKind.Signal, r.Message, false, null, 0.05f, tick, true, null); }
            bonds.RemoveAll(b => b.closed);
        }

        /// <summary>Move to the bank's account only when the colony has an account: used by the UI to show a deposit hint.</summary>
        public static bool FallbackMode => BankApi.Impl == null;
    }
}
