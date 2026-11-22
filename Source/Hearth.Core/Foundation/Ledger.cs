// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Double-entry cash book in micro-silver (K19, 15). Every money movement of every module is a Post(from, to, amount, memo).
    /// Accounts are plain strings, by convention:
    ///   "world"              outside economy (source/sink of all money; may go negative)
    ///   "colony:cash"        physical silver of the player colony is NOT tracked here; use "colony:till" for till/register balance
    ///   "colony:till:<mapId>" till/register balance of a map (emptied at midnight; see Tills)
    ///   report categories are NOT accounts: pass a category to Post/Payments.TryCharge and read Ledger.DayIncome(day, category)
    ///   "bank:<accountId>"   Silverline accounts (owned by the bank module)
    ///   "guest:<pawnThingId>" virtual money of a guest (card, prepaid, tab)
    ///   "house:<venueId>"    house bank of a casino/venue
    /// Invariant: the sum over all accounts is always zero. Rounding: amounts are exact micro-silver, no rounding inside the ledger.
    /// </summary>
    public sealed class Ledger : IExposable
    {
        public sealed class Entry : IExposable
        {
            public int Tick; public string From, To, Memo; public Money Amount;
            public void ExposeData()
            {
                Scribe_Values.Look(ref Tick, "t"); Scribe_Values.Look(ref From, "f"); Scribe_Values.Look(ref To, "to"); Scribe_Values.Look(ref Memo, "m");
                ScribeMoney.Look(ref Amount, "a");
            }
        }

        Dictionary<string, long> balances = new Dictionary<string, long>();
        List<Entry> journal = new List<Entry>();
        public const int JournalMax = 600;
        /// <summary>Per-day totals by category account, for reports: day index → account → micro.</summary>
        Dictionary<int, Dictionary<string, long>> daily = new Dictionary<int, Dictionary<string, long>>();

        public static Ledger Current => HearthGame.Instance?.Ledger;

        public Money Balance(string account) => new Money(balances.TryGetValue(account, out long v) ? v : 0);
        public IEnumerable<KeyValuePair<string, Money>> Accounts(string prefix = null)
            => balances.Where(kv => prefix == null || kv.Key.StartsWith(prefix)).Select(kv => new KeyValuePair<string, Money>(kv.Key, new Money(kv.Value)));
        public IReadOnlyList<Entry> Journal => journal;

        /// <summary>Moves money. Returns false (and books nothing) if amount is negative or the source may not go negative and lacks funds.</summary>
        /// <param name="category">Optional report category. When set (or when Payments.TryCharge/TryPayout is running with one), the amount
        /// is also counted in the daily income totals "income:&lt;category&gt;": positive when money flows into a "colony:*" account,
        /// negative when it flows out. This is independent of later till flushes (see DayIncome).</param>
        public bool Post(string from, string to, Money amount, string memo, bool allowNegative = false, string category = null)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) { HLog.Warn("Ledger", $"Refused post with empty account '{from}'→'{to}' {amount} ({memo})"); return false; }
            if (amount.Micro < 0) { HLog.Warn("Ledger", $"Refused negative post {from}→{to} {amount} ({memo})"); return false; }
            if (amount.Micro == 0 || from == to) return true;
            long fb = balances.TryGetValue(from, out long v) ? v : 0;
            if (!allowNegative && from != "world" && fb < amount.Micro) return false;
            balances[from] = checked(fb - amount.Micro);
            balances[to] = checked((balances.TryGetValue(to, out long w) ? w : 0) + amount.Micro);
            int tick = Find.TickManager?.TicksGame ?? 0;
            journal.Add(new Entry { Tick = tick, From = from, To = to, Amount = amount, Memo = memo });
            if (journal.Count > JournalMax) journal.RemoveRange(0, journal.Count - JournalMax);
            int day = tick / 60000;
            if (!daily.TryGetValue(day, out var d)) { daily[day] = d = new Dictionary<string, long>(); PruneDaily(day); }
            d[to] = (d.TryGetValue(to, out long x) ? x : 0) + amount.Micro;
            d[from] = (d.TryGetValue(from, out long y) ? y : 0) - amount.Micro;
            category = category ?? Payments.ActiveCategory;
            if (!string.IsNullOrEmpty(category))
            {
                bool intoColony = to.StartsWith("colony:", StringComparison.Ordinal), outOfColony = from.StartsWith("colony:", StringComparison.Ordinal);
                if (intoColony != outOfColony)
                {
                    string ck = "income:" + category;
                    d[ck] = (d.TryGetValue(ck, out long z) ? z : 0) + (intoColony ? amount.Micro : -amount.Micro);
                }
            }
            RaiseChanged(from, to, amount, memo);
            return true;
        }

        /// <summary>Calls every subscriber on its own, so one throwing subscriber is logged and neither breaks the caller nor the others.</summary>
        static void RaiseChanged(string from, string to, Money amount, string memo)
        {
            var h = Changed; if (h == null) return;
            foreach (var d in h.GetInvocationList())
            {
                try { ((Action<string, string, Money, string>)d)(from, to, amount, memo); }
                catch (Exception e) { HLog.Error("Ledger", "Subscriber of Ledger.Changed threw (ignored)", e); }
            }
        }

        /// <summary>Raised after every successful post (UI refresh, achievements, bank interest triggers …).</summary>
        public static event Action<string, string, Money, string> Changed;

        public Money DayTotal(int day, string account) => daily.TryGetValue(day, out var d) && d.TryGetValue(account, out long v) ? new Money(v) : Money.Zero;
        public Money DayTotalPrefix(int day, string prefix) => daily.TryGetValue(day, out var d) ? new Money(d.Where(kv => kv.Key.StartsWith(prefix)).Sum(kv => kv.Value)) : Money.Zero;

        /// <summary>Net income of a category on a day (see the category parameter of Post). Unaffected by till flushes.</summary>
        public Money DayIncome(int day, string category) => DayTotal(day, "income:" + category);
        /// <summary>Net income of all categories on a day.</summary>
        public Money DayIncomeTotal(int day) => DayTotalPrefix(day, "income:");

        void PruneDaily(int today) { foreach (var k in daily.Keys.Where(k => k < today - 120).ToList()) daily.Remove(k); }

        public long SumAll() => balances.Values.Sum();

        /// <summary>Daily invariant check; repairs by booking the difference against "world" and logs it loudly.</summary>
        public void DailyCheck()
        {
            // forget settled guest accounts (card/prepaid/tab of guests who have left)
            foreach (var k in balances.Where(kv => kv.Value == 0 && kv.Key.StartsWith("guest:", StringComparison.Ordinal)).Select(kv => kv.Key).ToList()) balances.Remove(k);
            long s = SumAll();
            if (s != 0)
            {
                HLog.Error("Ledger", $"Ledger out of balance by {new Money(s).ToStringFull()} silver — booked correction against world");
                balances["world"] = (balances.TryGetValue("world", out long w) ? w : 0) - s;
                Caretaker.Fixed("Ledger.Balance", "sum of accounts was " + new Money(s).ToStringFull());
            }
        }

        List<string> bk; List<long> bv; List<int> dk; List<DailyBlock> dv;
        sealed class DailyBlock : IExposable
        {
            public Dictionary<string, long> map = new Dictionary<string, long>(); List<string> k; List<long> v;
            public void ExposeData() { Scribe_Collections.Look(ref map, "m", LookMode.Value, LookMode.Value, ref k, ref v); if (map == null) map = new Dictionary<string, long>(); }
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref balances, "balances", LookMode.Value, LookMode.Value, ref bk, ref bv);
            Scribe_Collections.Look(ref journal, "journal", LookMode.Deep);
            Dictionary<int, DailyBlock> wrap = null;
            if (Scribe.mode == LoadSaveMode.Saving) wrap = daily.ToDictionary(kv => kv.Key, kv => new DailyBlock { map = kv.Value });
            Scribe_Collections.Look(ref wrap, "daily", LookMode.Value, LookMode.Deep, ref dk, ref dv);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                balances = balances ?? new Dictionary<string, long>();
                journal = journal ?? new List<Entry>();
                daily = wrap?.ToDictionary(kv => kv.Key, kv => kv.Value.map) ?? new Dictionary<int, Dictionary<string, long>>();
            }
        }
    }
}
