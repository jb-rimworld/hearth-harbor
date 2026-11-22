// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Hearth.City
{
    /// <summary>One bodyguard hire (KONZEPT 5.19): a guest, an armed colonist, and an hourly fee booked as colony income.</summary>
    public sealed class BodyguardContract : IExposable
    {
        public Pawn Guest, Guard;
        public int StartTick = -1, RequestTick, LastBillTick;
        public long HourlyMicro, PaidMicro;
        public bool Refunded, Ended;
        public int Hours;
        public string EndReason;

        public Money Hourly => new Money(HourlyMicro);
        public Money Paid => new Money(PaidMicro);
        public bool Started => StartTick >= 0;

        public void ExposeData()
        {
            Scribe_References.Look(ref Guest, "guest");
            Scribe_References.Look(ref Guard, "guard");
            Scribe_Values.Look(ref StartTick, "startTick", -1);
            Scribe_Values.Look(ref RequestTick, "requestTick");
            Scribe_Values.Look(ref LastBillTick, "lastBillTick");
            Scribe_Values.Look(ref HourlyMicro, "hourlyMicro");
            Scribe_Values.Look(ref PaidMicro, "paidMicro");
            Scribe_Values.Look(ref Refunded, "refunded");
            Scribe_Values.Look(ref Ended, "ended");
            Scribe_Values.Look(ref Hours, "hours");
            Scribe_Values.Look(ref EndReason, "endReason");
        }
    }

    /// <summary>
    /// Saved state of City Life: when each guest last used which service, takings per service, bodyguard contracts.
    /// Removing the module leaves nothing behind that vanilla would choke on (everything is plain data).
    /// </summary>
    public sealed class CityGameComponent : GameComponent
    {
        public static CityGameComponent Instance => Current.Game?.GetComponent<CityGameComponent>();

        Dictionary<string, int> lastVisit = new Dictionary<string, int>();
        Dictionary<string, long> revenue = new Dictionary<string, long>();
        Dictionary<string, int> counts = new Dictionary<string, int>();
        public List<BodyguardContract> Contracts = new List<BodyguardContract>();
        public int LastMovieNightTick = -99999;
        List<string> tk1, tk2, tk3; List<int> tv1, tv3; List<long> tv2;

        public CityGameComponent(Game game) { }

        static string Key(Pawn p, string kind) => p.thingIDNumber + ":" + kind;

        /// <summary>Ticks since the pawn last used the service, or a large number if never.</summary>
        public int TicksSince(Pawn p, string kind)
        {
            if (p == null || Find.TickManager == null) return int.MaxValue;
            return lastVisit.TryGetValue(Key(p, kind), out int t) ? Find.TickManager.TicksGame - t : int.MaxValue;
        }
        public void MarkVisit(Pawn p, string kind) { if (p != null) lastVisit[Key(p, kind)] = Find.TickManager.TicksGame; }
        public bool OnCooldown(Pawn p, string kind, int cooldownTicks) => cooldownTicks > 0 && TicksSince(p, kind) < cooldownTicks;

        public void Record(string kind, Money amount)
        {
            revenue.TryGetValue(kind, out long r); revenue[kind] = r + amount.Micro;
            counts.TryGetValue(kind, out int c); counts[kind] = c + 1;
        }
        public Money Revenue(string kind) => new Money(revenue.TryGetValue(kind, out long r) ? r : 0);
        public int Count(string kind) => counts.TryGetValue(kind, out int c) ? c : 0;
        public Money TotalRevenue => new Money(revenue.Values.Sum());
        public IEnumerable<string> Kinds => counts.Keys.Concat(revenue.Keys).Distinct();

        /// <summary>Drop visit memories older than a few days so the save stays small.</summary>
        public int Prune(int maxAgeTicks = 180000)
        {
            int now = Find.TickManager.TicksGame, n = 0;
            foreach (var k in lastVisit.Where(kv => now - kv.Value > maxAgeTicks).Select(kv => kv.Key).ToList()) { lastVisit.Remove(k); n++; }
            n += Contracts.RemoveAll(c => c.Ended && now - c.LastBillTick > 180000);
            return n;
        }

        public BodyguardContract ContractFor(Pawn guest) => Contracts.FirstOrDefault(c => !c.Ended && c.Guest == guest);
        public BodyguardContract ContractOf(Pawn guard) => Contracts.FirstOrDefault(c => !c.Ended && c.Guard == guard);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref lastVisit, "lastVisit", LookMode.Value, LookMode.Value, ref tk1, ref tv1);
            Scribe_Collections.Look(ref revenue, "revenue", LookMode.Value, LookMode.Value, ref tk2, ref tv2);
            Scribe_Collections.Look(ref counts, "counts", LookMode.Value, LookMode.Value, ref tk3, ref tv3);
            Scribe_Collections.Look(ref Contracts, "contracts", LookMode.Deep);
            Scribe_Values.Look(ref LastMovieNightTick, "lastMovieNightTick", -99999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (lastVisit == null) lastVisit = new Dictionary<string, int>();
                if (revenue == null) revenue = new Dictionary<string, long>();
                if (counts == null) counts = new Dictionary<string, int>();
                if (Contracts == null) Contracts = new List<BodyguardContract>();
                Contracts.RemoveAll(c => c == null || c.Guest == null);
            }
        }
    }
}
