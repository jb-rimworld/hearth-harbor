// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Silverline;
using Verse;

namespace Hearth.Ai
{
    /// <summary>
    /// Market headlines (F05). The market engine writes its news with its own stock lines; this class notices NEW news items, asks the relay for a fresher headline for
    /// up to eight of them at once, and swaps the text of the item when the answer arrives. The swap writes the new text into the vanilla string of the item, so it is saved
    /// like any other text and a removed mod leaves plain text. The offline line is on screen from the first moment; nothing waits for the AI. Numbers are never touched.
    /// </summary>
    public static class AiHeadlines
    {
        sealed class Pending { public NewsItem Item; public string Key; public double Queued; }
        static readonly List<Pending> pending = new List<Pending>();
        static readonly HashSet<NewsItem> seen = new HashSet<NewsItem>();
        static int watermark = int.MaxValue;
        static bool synced;
        public const int BatchMax = 8, FlushAt = 5;
        public const double FlushAfterSeconds = 60;
        public static int Rewritten;
        static AiTicket inFlight;

        public static void Reset() { pending.Clear(); seen.Clear(); watermark = int.MaxValue; synced = false; Rewritten = 0; inFlight?.Cancel(); inFlight = null; }

        /// <summary>Everything that is already in the news list when a game starts or loads stays as it is.</summary>
        public static void Sync()
        {
            seen.Clear(); pending.Clear();
            var g = FinanceGame.Instance;
            watermark = -1;
            if (g?.news != null) foreach (var n in g.news) { seen.Add(n); if (n.tick > watermark) watermark = n.tick; }
            synced = true;
        }

        /// <summary>Looks for news items that appeared since the last scan (every 250 ticks).</summary>
        public static void Scan()
        {
            var g = FinanceGame.Instance; if (g?.news == null) return;
            if (!synced) Sync();
            bool active = AiService.Active(AiFn.Headline);
            double now = AiService.Now;
            // forget items the engine trimmed away
            if (seen.Count > 200) { var keep = new HashSet<NewsItem>(g.news); seen.IntersectWith(keep); }
            for (int i = 0; i < g.news.Count; i++)
            {
                var n = g.news[i];
                if (!seen.Add(n)) continue;
                if (!active || n.tick < watermark - 1 || !Eligible(n)) continue;
                pending.Add(new Pending { Item = n, Key = n.kind + "|" + n.tick, Queued = now });
                if (n.tick > watermark) watermark = n.tick;
            }
            // items that can no longer be rewritten (trimmed) leave the queue
            for (int i = pending.Count - 1; i >= 0; i--) if (!g.news.Contains(pending[i].Item)) pending.RemoveAt(i);
            Flush(false);
        }

        /// <summary>Sends the waiting items when five are ready or the oldest has waited a minute. <paramref name="force"/> sends whatever is there.</summary>
        public static void Flush(bool force)
        {
            if (pending.Count == 0 || (inFlight != null && !inFlight.Done)) return;
            if (!AiService.Active(AiFn.Headline)) { if (pending.Count > 0) { pending.Clear(); } return; }
            double now = AiService.Now;
            if (!force && pending.Count < FlushAt && now - pending[0].Queued < FlushAfterSeconds) return;
            var batch = pending.GetRange(0, Math.Min(BatchMax, pending.Count));
            pending.RemoveRange(0, batch.Count);
            var events = new List<object>();
            foreach (var p in batch) events.Add(EventFor(p.Item));
            var fields = new Dictionary<string, object> { { "events", events } };
            inFlight = AiService.Send(AiFn.Headline, fields, res => Apply(batch, res));
            if (inFlight == null) AiService.NoteFallback();
        }

        /// <summary>Only real price moves (market-wide or of one asset; not the bank run, which has no size) get a model headline; player-caused items and non-price kinds (lottery, listing, delisting, project end, solar halt, signals) keep their own text, so no real fact is replaced by an invented one.</summary>
        public static bool Eligible(NewsItem n)
        {
            if (n == null || n.byYou) return false;
            switch (n.kind)
            {
                case MarketEventKind.Crash: case MarketEventKind.Rally: case MarketEventKind.Razzia: case MarketEventKind.Hack:
                case MarketEventKind.RugPull: case MarketEventKind.Default: case MarketEventKind.WarEnd: case MarketEventKind.Pump: case MarketEventKind.Quarterly:
                    return Math.Abs(n.magnitude) > 1e-4f;
                default: return false;
            }
        }

        static Dictionary<string, object> EventFor(NewsItem n)
        {
            string asset = "the market";
            try { var a = FinanceGame.Instance?.Asset(n.assetId); if (a != null && !a.Label.NullOrEmpty()) asset = a.Label; } catch (Exception) { }
            double pct = (Math.Exp(Math.Max(-6, Math.Min(6, n.magnitude))) - 1.0) * 100.0;
            return new Dictionary<string, object> { { "kind", n.kind.ToString().ToLowerInvariant() }, { "asset", asset }, { "change_pct", Math.Max(-999, Math.Min(999, Math.Round(pct, 1))) } };
        }

        static void Apply(List<Pending> batch, AiResult res)
        {
            if (!res.Ok || res.Headlines == null) { AiService.NoteFallback(); return; }
            var g = FinanceGame.Instance; if (g?.news == null) return;
            for (int i = 0; i < batch.Count && i < res.Headlines.Count; i++)
            {
                string h = AiText.Headline(res.Headlines[i], 110);
                if (h.Length < 8 || !g.news.Contains(batch[i].Item)) continue;
                batch[i].Item.text = h; Rewritten++;
            }
        }

#if HH_TESTS
        public static int PendingCount => pending.Count;
        public static void TestPrime(bool sync = true) { Reset(); if (sync) Sync(); }
#endif
    }
}
