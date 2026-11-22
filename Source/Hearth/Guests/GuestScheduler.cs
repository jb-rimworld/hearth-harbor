// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Hearth.Guests
{
    /// <summary>One group in the arrival forecast (5.4).</summary>
    public sealed class PlannedGroup : IExposable
    {
        public int Id;
        public Faction Faction;
        public int Size;
        public StayKind Kind;
        public int ArrivalTick;
        public int Nights;
        public int Postponed;

        public string Describe(Map map)
        {
            float days = (ArrivalTick - Find.TickManager.TicksGame) / 60000f;
            string when = days < 0.05f ? "HH.Inn.Forecast.Now".TranslateOr("any moment") : "HH.Inn.Forecast.In".TranslateOr("in {0} days").Replace("{0}", days.ToString("0.0"));
            string kind = Kind == StayKind.Overnight
                ? "HH.Inn.Forecast.Overnight".TranslateOr("{0} night(s)").Replace("{0}", Nights.ToString())
                : "HH.Inn.Forecast.Day".TranslateOr("day visit");
            return $"{Size}× {Faction?.Name ?? "?"} · {kind} · {when}";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref Size, "size");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref ArrivalTick, "arrival");
            Scribe_Values.Look(ref Nights, "nights");
            Scribe_Values.Look(ref Postponed, "postponed");
        }
    }

    /// <summary>
    /// Arrival plan of one map (5.4, 5.13). Runs only while the colony has a guest bed (overnight guests) or a venue (day visitors);
    /// otherwise it is empty and does nothing at all (N08). Keeps a forecast of the next days for the guest overview.
    /// Rate = 0.6 groups/day × renown curve × capacity × arrival setting.
    /// </summary>
    public sealed class MapComponent_HH_GuestScheduler : MapComponent
    {
        List<PlannedGroup> forecast = new List<PlannedGroup>();
        int nextId = 1;
        bool everActive;
        int planHorizon = -1;
        /// <summary>Tests can switch the automatic scheduler off while they drive arrivals by hand.</summary>
        public static bool SuspendedForTests;

        public MapComponent_HH_GuestScheduler(Map map) : base(map) { }

        public IReadOnlyList<PlannedGroup> Forecast => forecast;
        public bool EverActive => everActive;

        /// <summary>Planned groups within the next <paramref name="days"/> days, soonest first.</summary>
        public List<PlannedGroup> GetForecast(int days = 5)
        {
            int limit = Find.TickManager.TicksGame + days * 60000;
            return forecast.Where(g => g.ArrivalTick <= limit).OrderBy(g => g.ArrivalTick).ToList();
        }

        public PlannedGroup NextArrival() => forecast.OrderBy(g => g.ArrivalTick).FirstOrDefault();

        public bool OvernightPossible => GuestBeds.CountOn(map) > 0;
        public bool DayVisitsPossible => HSettings.GetBool(InnSettings.DayVisitors) && Venues.All(map).Count > 0;
        public bool Active => map.IsPlayerHome && (OvernightPossible || DayVisitsPossible);

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 123) return;
            if (!Modules.On(ModuleId.Inn) || SuspendedForTests) return;
            Guard.Run(ModuleId.Inn, "Inn.Scheduler", Tick);
        }

        void Tick()
        {
            int now = Find.TickManager.TicksGame;
            var door = GuestRegistry.Instance?.Door;
            bool night = OvernightPossible && (door == null || door.KindOk(StayKind.Overnight)), day = DayVisitsPossible && (door == null || door.KindOk(StayKind.DayVisitor));
            if (!map.IsPlayerHome || (!night && !day))
            {
                if (forecast.Count > 0) { forecast.Clear(); HLog.Debug("Inn", "Forecast cleared: no guest bed and no venue on " + map); }
                planHorizon = -1;
                return;
            }
            forecast.RemoveAll(g => (g.Kind == StayKind.Overnight && !night) || (g.Kind == StayKind.DayVisitor && !day) || g.Faction == null || g.Faction.defeated);
            if (!everActive)
            {
                everActive = true;
                var rng = JRand.Stream("inn.schedule");
                float d = (float)rng.Range(HSettings.Get(InnSettings.FirstGuestMinDays), Math.Max(HSettings.Get(InnSettings.FirstGuestMinDays), HSettings.Get(InnSettings.FirstGuestMaxDays)));
                var kind = night ? StayKind.Overnight : StayKind.DayVisitor;
                var first = Plan(now + (int)(d * 60000), kind, rng);
                if (first != null) { first.ArrivalTick = Math.Max(now + 2500, AlignToHours(first.ArrivalTick, kind, true)); forecast.Add(first); }
                planHorizon = now + (int)(d * 60000);
                HLog.Info("Inn", $"First guests planned for {map}: {first?.Describe(map)}");
            }
            if (planHorizon < now) planHorizon = now;
            TopUp(now, night, day);
            for (int i = forecast.Count - 1; i >= 0; i--)
            {
                var g = forecast[i];
                if (g.ArrivalTick > now) continue;
                forecast.RemoveAt(i);
                if (!TryArrive(g))
                {
                    if (++g.Postponed <= 3)
                    {
                        g.ArrivalTick = AlignToHours(now + 2500 * 8, g.Kind, false);
                        forecast.Add(g);
                    }
                    else HLog.Debug("Inn", "Planned group dropped after 3 postponements: " + g.Describe(map));
                }
            }
        }

        /// <summary>Renown curve: 0 → 0.4, 25 → 0.8, 50 → 1.0, 75 → 1.4, 100 → 1.8 (9.3).</summary>
        public static float RenownCurve(float r)
        {
            float[] x = { 0, 25, 50, 75, 100 }, y = { 0.4f, 0.8f, 1f, 1.4f, 1.8f };
            r = Mathf.Clamp(r, 0, 100);
            for (int i = 0; i < 4; i++) if (r <= x[i + 1]) return Mathf.Lerp(y[i], y[i + 1], (r - x[i]) / (x[i + 1] - x[i]));
            return y[4];
        }

        /// <summary>Groups per day for this map right now.</summary>
        public float RatePerDay(bool night, bool day)
        {
            float renown = GuestRegistry.Instance?.Renown ?? GuestRegistry.StartRenown;
            float cap = 0f;
            if (night) cap += Mathf.Min(1f, GuestBeds.FreeOn(map).Count / 3f + 0.15f);
            if (day) cap += Mathf.Min(0.6f, Venues.All(map).Count * 0.3f);
            return 0.6f * RenownCurve(renown) * cap * HSettings.Get(InnSettings.ArrivalRate);
        }

        void TopUp(int now, bool night, bool day)
        {
            int horizon = now + HSettings.GetInt(InnSettings.ForecastDays) * 60000;
            var rng = JRand.Stream("inn.schedule");
            int guard = 0;
            while (planHorizon < horizon && guard++ < 50)
            {
                float lambda = RatePerDay(night, day);
                if (lambda <= 0.0001f) { planHorizon = horizon; break; }
                double u = Math.Max(1e-6, rng.NextDouble());
                int step = (int)(-Math.Log(u) / lambda * 60000);
                planHorizon += Math.Max(2500, step);
                if (planHorizon >= horizon) break;
                StayKind kind = night && day ? (rng.Chance(0.7) ? StayKind.Overnight : StayKind.DayVisitor) : night ? StayKind.Overnight : StayKind.DayVisitor;
                var g = Plan(planHorizon, kind, rng);
                if (g == null) continue;
                g.ArrivalTick = AlignToHours(planHorizon, kind, false);
                forecast.Add(g);
            }
        }

        PlannedGroup Plan(int tick, StayKind kind, RngStream rng)
        {
            int nights = 0;
            if (kind == StayKind.Overnight)
            {
                int lo = Math.Max(1, HSettings.GetInt(InnSettings.StayNightsMin)), hi = Math.Max(lo, HSettings.GetInt(InnSettings.StayNightsMax));
                nights = rng.Range(lo, hi + 1);
            }
            int stayTicks = kind == StayKind.Overnight ? nights * 60000 + 20000 : 15000;
            var f = PickFaction(tick, tick + stayTicks, rng);
            if (f == null) return null;
            float renown = GuestRegistry.Instance?.Renown ?? GuestRegistry.StartRenown;
            float years = Find.TickManager.TicksGame / 3600000f;
            int size = Mathf.Clamp(1 + rng.Poisson(1.0 + 0.015 * renown + 0.25 * Math.Min(years, 5)), 1, Math.Min(6, HSettings.GetInt(InnSettings.MaxGroupSize)));
            return new PlannedGroup { Id = nextId++, Faction = f, Size = size, Kind = kind, ArrivalTick = tick, Nights = nights };
        }

        /// <summary>Overnight guests come in the afternoon (check-in hour … 19), day visitors during opening hours with time to spare.</summary>
        int AlignToHours(int tick, StayKind kind, bool firstGroup)
        {
            int lo, hi;
            if (kind == StayKind.Overnight) { lo = HSettings.GetInt(InnSettings.CheckInHour); hi = Math.Max(lo + 1, 19); }
            else { OpeningWindow(out lo, out hi); hi = Math.Max(lo + 1, hi - 4); }
            float hourAt = GenDate.HourFloat((long)(Find.TickManager.TicksAbs + (tick - Find.TickManager.TicksGame)), Find.WorldGrid.LongLatOf(map.Tile).x);
            float target = hourAt;
            if (hourAt < lo) target = lo + (hi - lo) * 0.3f;
            else if (hourAt >= hi) target = 24f + lo + (hi - lo) * 0.3f;
            return tick + (int)((target - hourAt) * 2500f);
        }

        /// <summary>The widest opening window of the map's venues (fallback 10–20).</summary>
        public void OpeningWindow(out int open, out int close)
        {
            open = 24; close = 0;
            foreach (var v in Venues.All(map))
            {
                if (v.Comp.alwaysOpen || v.Comp.openHour == v.Comp.closeHour) { open = 8; close = 22; return; }
                int o = v.Comp.openHour, c = v.Comp.closeHour < v.Comp.openHour ? 24 : v.Comp.closeHour;
                open = Math.Min(open, o); close = Math.Max(close, c);
            }
            if (open >= close) { open = 10; close = 20; }
        }

        public static bool FactionCanVisit(Faction f, Map map)
        {
            if (f == null || f.IsPlayer || f.Hidden || f.defeated || f.temporary) return false;
            if (!f.def.humanlikeFaction || f.HostileTo(Faction.OfPlayer)) return false;
            if (f.def.pawnGroupMakers == null || !f.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Peaceful || m.kindDef == PawnGroupKindDefOf.Trader)) return false;
            if (map != null && NeutralGroupIncidentUtility.AnyBlockingHostileLord(map, f)) return false;
            return true;
        }

        /// <summary>Weighted by goodwill; never a faction hostile to one that will be (or is) here at the same time (5.8).</summary>
        Faction PickFaction(int from, int to, RngStream rng)
        {
            var door = GuestRegistry.Instance?.Door;
            var cands = Find.FactionManager.AllFactionsListForReading.Where(f => FactionCanVisit(f, map) && (door == null || door.FactionOk(f))).ToList();
            cands.RemoveAll(f => forecast.Any(g => g.Faction != null && g.Faction.HostileTo(f) && Overlaps(g, from, to)));
            if (cands.Count == 0) return null;
            float total = cands.Sum(f => FactionWeight(f));
            double roll = rng.NextDouble() * total;
            foreach (var f in cands) { roll -= FactionWeight(f); if (roll <= 0) return f; }
            return cands[cands.Count - 1];
        }

        /// <summary>Goodwill weight; with a visible underworld, respectable factions × exp(−N/40), rough ones × (0.2 + N/50) (5.14 Whispers).</summary>
        public static float FactionWeight(Faction f)
        {
            float w = 1f + Mathf.Max(0, f.PlayerGoodwill) / 25f;
            if (Modules.On(ModuleId.Underworld) && Hearth.Underworld.Underworld.Visible)
                w *= GuestArrival.IsRoughFaction(f) ? Hearth.Underworld.Underworld.OutlawArrivalFactor : Hearth.Underworld.Underworld.RespectableArrivalFactor;
            return Mathf.Max(0.01f, w);
        }

        static bool Overlaps(PlannedGroup g, int from, int to)
        {
            int gEnd = g.ArrivalTick + (g.Kind == StayKind.Overnight ? g.Nights * 60000 + 20000 : 15000);
            return g.ArrivalTick < to && from < gEnd;
        }

        /// <summary>Enemies never together: any guest (or other lord) of a faction hostile to f on the map right now?</summary>
        public static bool EnemyPresent(Map map, Faction f)
        {
            var reg = GuestRegistry.Instance;
            if (reg != null) foreach (var r in reg.OnMap(map)) { var of = r.Pawn?.Faction; if (of != null && of != f && of.HostileTo(f) && r.Pawn.Spawned) return true; }
            foreach (var l in map.lordManager.lords) if (l.faction != null && l.faction != f && !l.faction.IsPlayer && l.faction.HostileTo(f) && !l.faction.HostileTo(Faction.OfPlayer) && l.ownedPawns.Count > 0) return true;
            return false;
        }

        bool TryArrive(PlannedGroup g)
        {
            var f = g.Faction;
            var door = GuestRegistry.Instance?.Door;
            if (door != null && !door.KindOk(g.Kind)) return false;
            if (!FactionCanVisit(f, map) || EnemyPresent(map, f) || (door != null && !door.FactionOk(f)))
            {
                var alt = Find.FactionManager.AllFactionsListForReading.Where(x => FactionCanVisit(x, map) && !EnemyPresent(map, x) && (door == null || door.FactionOk(x))).ToList();
                if (alt.Count == 0) return false;
                f = alt.RandomElement();
            }
            int size = g.Size;
            if (g.Kind == StayKind.Overnight)
            {
                int free = GuestBeds.FreeOn(map).Count;
                if (free <= 0) return false;
                size = Math.Min(size, free);
            }
            else
            {
                OpeningWindow(out int open, out int close);
                float h = GenLocalDate.HourFloat(map);
                if (h < open || h > close - 3) return false;
            }
            var req = new GuestArrivalRequest { Faction = f, Size = size, Kind = g.Kind, Nights = g.Nights };
            return GuestArrival.TryFire(map, req) != null;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref forecast, "forecast", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Values.Look(ref everActive, "everActive");
            Scribe_Values.Look(ref planHorizon, "planHorizon", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { forecast = forecast ?? new List<PlannedGroup>(); forecast.RemoveAll(x => x == null || x.Faction == null); }
        }
    }
}
