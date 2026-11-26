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
        /// <summary>Return visit (5.14): these world pawns come back instead of new ones.</summary>
        public List<Pawn> Returning = new List<Pawn>();
        public bool IsReturn => Returning != null && Returning.Count > 0;

        public string Describe(Map map)
        {
            string when = Eta();
            string kind = Kind == StayKind.Overnight
                ? "HH.Inn.Forecast.Overnight".TranslateOr("{0} night(s)").Replace("{0}", Nights.ToString())
                : "HH.Inn.Forecast.Day".TranslateOr("day visit");
            string back = IsReturn ? " · " + "HH.Inn.Forecast.Return".TranslateOr("coming back") : "";
            return $"{Size}× {Faction?.Name ?? "?"} · {kind}{back} · {when}";
        }

        /// <summary>"in 5 hours" / "in 1.3 days" / "any moment" (B7: a countdown, not just days).</summary>
        public string Eta()
        {
            int ticks = ArrivalTick - Find.TickManager.TicksGame;
            if (ticks < 1250) return "HH.Inn.Forecast.Now".TranslateOr("any moment");
            return "HH.Inn.Forecast.InTime".TranslateOr("in {0}").Replace("{0}", ticks.ToStringTicksToPeriod(allowSeconds: false, shortForm: false, canUseDecimals: true));
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
            Scribe_Collections.Look(ref Returning, "returning", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { Returning = Returning ?? new List<Pawn>(); Returning.RemoveAll(p => p == null); }
        }
    }

    /// <summary>Counters of one scheduler: why groups were (not) planned and (not) arrived. Saved with the scheduler (whole game, not per session).</summary>
    public sealed class SchedulerStats : IExposable
    {
        public int Planned, ThinnedOut, NoFaction, Arrived, WaitingForBed, Postponed, Dropped, DroughtBreaks;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Planned, "planned"); Scribe_Values.Look(ref ThinnedOut, "thinned"); Scribe_Values.Look(ref NoFaction, "noFaction");
            Scribe_Values.Look(ref Arrived, "arrived"); Scribe_Values.Look(ref WaitingForBed, "waitedForBed"); Scribe_Values.Look(ref Postponed, "postponed");
            Scribe_Values.Look(ref Dropped, "dropped"); Scribe_Values.Look(ref DroughtBreaks, "droughtBreaks");
        }
        public string Line() => $"planned {Planned}, thinned {ThinnedOut}, no faction {NoFaction}, arrived {Arrived}, waited for a bed {WaitingForBed}, postponed {Postponed}, dropped {Dropped}, drought breaks {DroughtBreaks}";
    }

    /// <summary>
    /// Arrival plan of one map (5.4, 5.13). Runs only while the colony has a guest bed (overnight guests) or a venue (day visitors);
    /// otherwise it is empty and does nothing at all (N08). Keeps a forecast of the next days for the guest overview.
    /// Rate = 1.0 groups/day × renown curve × capacity × arrival setting. The capacity is the one expected at the arrival time (guests that will have left
    /// by then free their beds, groups already planned take theirs), and a house that stays quiet with room to spare gets a group nudged in (drought breaker).
    /// </summary>
    public sealed class MapComponent_HH_GuestScheduler : MapComponent
    {
        List<PlannedGroup> forecast = new List<PlannedGroup>();
        int nextId = 1;
        bool everActive;
        int planHorizon = -1;
        /// <summary>Time of the next candidate group when it is drawn but still beyond the forecast window (-1 = none). Saved: redrawing it after a load
        /// would be statistically harmless (memoryless) but would take another number from the saved random stream, so a reloaded game would plan differently.</summary>
        int nextCandidate = -1;
        const int Never = int.MinValue;
        /// <summary>Last planned group that really arrived, when the plan woke up, and the last scheduler tick (drought breaker, 5.4). Ticks, or <see cref="Never"/>.</summary>
        int lastArrivalTick = Never, activeSince = Never, lastTickSeen = Never;
        /// <summary>What happened to the plan in this game: shown by the long run report and the dev overview.</summary>
        public SchedulerStats Counters = new SchedulerStats();
        /// <summary>Tests switch this on to collect one line per scheduler decision (long run diagnostics); null = off, no cost.</summary>
        public static List<string> DiagLog;
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

        /// <summary>Planned groups due within the next 24 hours and how many guests they bring (overview line).</summary>
        public void Next24Hours(out int groups, out int guests)
        {
            groups = guests = 0;
            int limit = Find.TickManager.TicksGame + 60000;
            foreach (var g in forecast) if (g.ArrivalTick <= limit) { groups++; guests += g.IsReturn ? g.Returning.Count : g.Size; }
        }

        public PlannedGroup NextArrival() => forecast.OrderBy(g => g.ArrivalTick).FirstOrDefault();

        /// <summary>Drops every planned return visit and releases the pinned world pawns (removal, tests).</summary>
        public void ClearAllReturns()
        {
            for (int i = forecast.Count - 1; i >= 0; i--) if (forecast[i].IsReturn) { GuestReturns.Unpin(forecast[i].Returning); forecast.RemoveAt(i); }
        }

        internal void ClearReturnsForTests() => ClearAllReturns();

#if HH_TESTS
        internal void ClearForecastForTests() { foreach (var g in forecast) GuestReturns.Unpin(g.Returning); forecast.Clear(); }
        internal PlannedGroup AddPlannedForTests(Faction f, int size, StayKind kind, int nights, int tick)
        { var g = new PlannedGroup { Id = nextId++, Faction = f, Size = size, Kind = kind, Nights = nights, ArrivalTick = tick }; forecast.Add(g); return g; }
        /// <summary>Pretends nobody has come for this many ticks (the plan woke up and the last arrival lies that far back).</summary>
        internal void MakeQuietForTests(int ticks) { int now = Find.TickManager.TicksGame; lastArrivalTick = now - ticks; activeSince = now - ticks; }
#endif

        /// <summary>Drops the planned return that brings this pawn back (pin cap). True if there was one.</summary>
        public bool DropReturnOf(Pawn p)
        {
            for (int i = forecast.Count - 1; i >= 0; i--)
                if (forecast[i].IsReturn && forecast[i].Returning.Contains(p)) { GuestReturns.Unpin(forecast[i].Returning); forecast.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>An abandoned or destroyed map takes its return plans along: release the pins (A5).</summary>
        public override void MapRemoved()
        {
            base.MapRemoved();
            Guard.Run(ModuleId.Inn, "Inn.MapRemovedPins", () => { foreach (var g in forecast) GuestReturns.Unpin(g.Returning); forecast.Clear(); });
        }

        /// <summary>Adds a return visit to the plan (5.14). It arrives even if the regular plan is full.</summary>
        public PlannedGroup AddReturn(Faction f, List<Pawn> pawns, StayKind kind, int nights, int tick)
        {
            var g = new PlannedGroup { Id = nextId++, Faction = f, Size = pawns.Count, Kind = kind, Nights = Math.Max(1, nights), Returning = new List<Pawn>(pawns) };
            g.ArrivalTick = AlignToHours(tick, kind, false);
            forecast.Add(g);
            return g;
        }

        public bool OvernightPossible => GuestBeds.CountOn(map) > 0;
        public bool DayVisitsPossible => HSettings.GetBool(InnSettings.DayVisitors) && Venues.All(map).Count > 0;
        public bool Active => map.IsPlayerHome && (OvernightPossible || DayVisitsPossible);

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % PerfMode.Interval(250) != 123) return;
            if (!Modules.On(ModuleId.Inn) || SuspendedForTests) return;
            Guard.Run(ModuleId.Inn, "Inn.Scheduler", Tick);
        }

        void Tick()
        {
            int now = Find.TickManager.TicksGame;
            // the plan was asleep (module off, suspended, no bed for a while): it starts a fresh quiet period instead of counting the whole sleep as a drought
            if (lastTickSeen != Never && now - lastTickSeen > 2500) { activeSince = now; nextCandidate = -1; }
            lastTickSeen = now;
            var door = GuestRegistry.Instance?.Door;
            bool night = OvernightPossible && (door == null || door.KindOk(StayKind.Overnight)), day = DayVisitsPossible && (door == null || door.KindOk(StayKind.DayVisitor));
            if (!map.IsPlayerHome || (!night && !day))
            {
                if (forecast.Count > 0) { foreach (var x in forecast) GuestReturns.Unpin(x.Returning); forecast.Clear(); HLog.Debug("Inn", "Forecast cleared: no guest bed and no venue on " + map); }
                planHorizon = -1; nextCandidate = -1;
                return;
            }
            for (int i = forecast.Count - 1; i >= 0; i--)
            {
                var g = forecast[i];
                bool drop = g.Faction == null || g.Faction.defeated || (g.Kind == StayKind.Overnight && !night) || (g.Kind == StayKind.DayVisitor && !day);
                if (drop) { GuestReturns.Unpin(g.Returning); forecast.RemoveAt(i); }
            }
            if (!everActive)
            {
                everActive = true;
                var rng = JRand.Stream("inn.schedule");
                float d = (float)rng.Range(HSettings.Get(InnSettings.FirstGuestMinDays), Math.Max(HSettings.Get(InnSettings.FirstGuestMinDays), HSettings.Get(InnSettings.FirstGuestMaxDays)));
                var kind = night ? StayKind.Overnight : StayKind.DayVisitor;
                var first = Plan(now + (int)(d * 60000), kind, rng);
                if (first != null) { first.ArrivalTick = Math.Max(now + 2500, AlignToHours(first.ArrivalTick, kind, true)); forecast.Add(first); Counters.Planned++; }
                planHorizon = now + (int)(d * 60000);
                HLog.Info("Inn", $"First guests planned for {map}: {first?.Describe(map)}");
                // B7: tell the player once when the first guests are due
                if (first != null && HSettings.GetBool(InnSettings.ArrivalNotice))
                    Messages.Message("HH.Inn.Msg.FirstGuestsEta".TranslateOr("Your first guests are on their way: {0}, expected {1}.")
                        .Replace("{0}", first.Faction?.Name ?? "?").Replace("{1}", first.Eta()), MessageTypeDefOf.PositiveEvent, false);
            }
            if (activeSince == Never) activeSince = now;
            TopUp(now, night, day);
            BreakDrought(now, night, day);
            for (int i = forecast.Count - 1; i >= 0; i--)
            {
                var g = forecast[i];
                if (g.ArrivalTick > now) continue;
                forecast.RemoveAt(i);
                var res = TryArrive(g, out string why);
                if (res == ArriveResult.Arrived) { Counters.Arrived++; lastArrivalTick = now; Diag($"arrived: {g.Describe(map)}"); continue; }
                if (res == ArriveResult.Moot) { Diag($"moot: {g.Describe(map)} ({why})"); continue; }
                if (res == ArriveResult.NoBed) Counters.WaitingForBed++;
                if (++g.Postponed <= 3)
                {
                    Counters.Postponed++;
                    g.ArrivalTick = AlignToHours(now + 2500 * 8, g.Kind, false);
                    forecast.Add(g);
                    Diag($"postponed ({why}): {g.Describe(map)}");
                }
                else { Counters.Dropped++; Diag($"dropped after 3 postponements ({why}): {g.Describe(map)}"); HLog.Debug("Inn", "Planned group dropped after 3 postponements: " + g.Describe(map)); GuestReturns.Unpin(g.Returning); }
            }
            if (DiagLog != null && now % 2500 < 250) Diag($"state: beds {GuestBeds.CountOn(map)} free {GuestBeds.FreeOn(map).Count}, rate {RatePerDay(night, day):0.00}/day, forecast {forecast.Count}, last candidate {(planHorizon - now) / 60000f:0.0} d, candidates {CandidateFactions().Count}, {Counters.Line()}");
        }

        /// <summary>Renown curve: 0 → 0.9, 25 → 0.95, 50 → 1.0, 75 → 1.4, 100 → 1.8. The low end is nearly flat on purpose: a new house with a free bed
        /// must see guests at all (long run: 0.5-0.6 at renown 12-19 left days without arrivals); renown then shapes who comes, and the upper half the volume (9.3).</summary>
        public static float RenownCurve(float r)
        {
            float[] x = { 0, 25, 50, 75, 100 }, y = { 0.9f, 0.95f, 1f, 1.4f, 1.8f };
            r = Mathf.Clamp(r, 0, 100);
            for (int i = 0; i < 4; i++) if (r <= x[i + 1]) return Mathf.Lerp(y[i], y[i + 1], (r - x[i]) / (x[i + 1] - x[i]));
            return y[4];
        }

        public const float BaseRatePerDay = 1.0f;

        /// <summary>Night capacity share: any free guest bed gives 0.8, two or more 1.0 (the old 1/3 per bed starved single-bed houses).</summary>
        public static float NightCapacity(int freeBeds) => freeBeds <= 0 ? 0f : Mathf.Min(1f, 0.6f + 0.2f * freeBeds);

        /// <summary>Day-visitor capacity share: each venue opens 0.3, two or more 0.6.</summary>
        float DayCapacity() => Mathf.Min(0.6f, Venues.All(map).Count * 0.3f);

        /// <summary>Groups per day for this map right now.</summary>
        public float RatePerDay(bool night, bool day) => RateFor(night, day, GuestBeds.FreeOn(map).Count);

        /// <summary>Groups per day with this many free guest beds.</summary>
        float RateFor(bool night, bool day, int freeBeds)
        {
            float renown = GuestRegistry.Instance?.Renown ?? GuestRegistry.StartRenown;
            float cap = 0f;
            if (night) cap += NightCapacity(freeBeds);
            if (day) cap += DayCapacity();
            return BaseRatePerDay * RenownCurve(renown) * cap * HSettings.Get(InnSettings.ArrivalRate) * StorytellerHints.GuestArrivalFactor;   // F09: e.g. Hetty ×1.25
        }

        /// <summary>
        /// Guest beds that should be free at <paramref name="tick"/>: every bed, minus overnight guests still booked then, minus planned
        /// overnight groups (return visits included) that cover that moment. A group planned for tomorrow must not be judged by tonight's full house.
        /// </summary>
        public int ProjectedFreeBeds(int tick)
        {
            int taken = 0;
            var reg = GuestRegistry.Instance;
            if (reg != null) foreach (var r in reg.OnMap(map)) if (r.Overnight && !r.Settled && r.PlannedLeaveTick > tick) taken++;
            foreach (var g in forecast)
                if (g.Kind == StayKind.Overnight && g.ArrivalTick <= tick && tick < g.ArrivalTick + g.Nights * 60000 + 20000) taken += g.IsReturn ? g.Returning.Count : g.Size;
            return Math.Max(0, GuestBeds.CountOn(map) - taken);
        }

        /// <summary>
        /// Plans groups up to the forecast horizon. Candidates fall at the full-house-empty rate and are thinned by the capacity expected at their
        /// arrival time (an inhomogeneous Poisson process): quiet beds give a steady stream, a full house a trickle of day visitors.
        /// </summary>
        void TopUp(int now, bool night, bool day)
        {
            int horizon = now + HSettings.GetInt(InnSettings.ForecastDays) * 60000;
            var rng = JRand.Stream("inn.schedule");
            int beds = GuestBeds.CountOn(map);
            float lmax = RateFor(night, day, beds);
            int guard = 0;
            while (guard++ < 80)
            {
                // nothing to plan right now (no beds, arrivals off): do NOT jump the horizon ahead, or the house stays dark for the whole forecast window
                // (long run: days 7-14 without a single arrival after a full house at planning time). It is looked at again every 250 ticks.
                if (lmax <= 0.0001f) { nextCandidate = -1; break; }
                // The next candidate is drawn once and kept until it falls inside the forecast window. (Drawing it anew on every call and dropping it
                // when it overshot the window threw the candidate away: after the first fill the plan stalled, long run: up to 6 days without a group.)
                if (nextCandidate >= 0 && nextCandidate < now) nextCandidate = -1;   // stale: the plan was asleep (module off, tests); never plan into the past
                if (nextCandidate < 0)
                {
                    double gap = Math.Min(30.0, -Math.Log(Math.Max(1e-6, rng.NextDouble())) / lmax) * 60000.0;
                    nextCandidate = (int)Math.Min(int.MaxValue / 2, Math.Max(planHorizon, now) + Math.Max(2500.0, gap));
                }
                if (nextCandidate > horizon) break;
                planHorizon = nextCandidate;
                nextCandidate = -1;
                int freeAt = ProjectedFreeBeds(planHorizon);
                float lt = RateFor(night, day, freeAt);
                if (rng.NextDouble() * lmax > lt) { Counters.ThinnedOut++; continue; }
                // night and day: the kind follows each side's share of the capacity at that moment
                float capN = night ? NightCapacity(freeAt) : 0f, capD = day ? DayCapacity() : 0f;
                StayKind kind = night && day ? (rng.Chance(capN / Math.Max(0.0001f, capN + capD)) ? StayKind.Overnight : StayKind.DayVisitor) : night ? StayKind.Overnight : StayKind.DayVisitor;
                var g = Plan(planHorizon, kind, rng);
                if (g == null) { Counters.NoFaction++; Diag($"no faction can come at +{(planHorizon - now) / 2500f:0.0} h: {WhyNobody()}"); continue; }
                g.ArrivalTick = AlignToHours(planHorizon, kind, false);
                forecast.Add(g);
                Counters.Planned++;
                Diag($"planned: {g.Describe(map)} (free beds then {freeAt}, rate {lt:0.00}/{lmax:0.00})");
            }
        }

        /// <summary>
        /// Drought breaker: a house with room to spare that has seen nobody for far longer than its rate says is normal, and has nobody due within
        /// half a day, gets one group in the next hours. Pure chance (or a plan that lost its group) must not leave free beds empty for days.
        /// </summary>
        void BreakDrought(int now, bool night, bool day)
        {
            float lam = RatePerDay(night, day);
            if (lam <= 0.0001f) return;
            float tolerateDays = Mathf.Clamp(1.5f / lam, 1f, 15f);
            int quiet = now - Math.Max(lastArrivalTick, activeSince);   // activeSince is set before the first call
            if (quiet < tolerateDays * 60000f) return;
            foreach (var g in forecast) if (g.ArrivalTick <= now + 30000) return;
            var rng = JRand.Stream("inn.schedule");
            int freeNow = GuestBeds.FreeOn(map).Count;
            float capN = night ? NightCapacity(freeNow) : 0f, capD = day ? DayCapacity() : 0f;
            var kind = night && (!day || rng.Chance(capN / Math.Max(0.0001f, capN + capD))) ? StayKind.Overnight : StayKind.DayVisitor;
            int at = now + 2500 * 2;
            var rescue = Plan(at, kind, rng);
            if (rescue == null) { Counters.NoFaction++; Diag("drought, but nobody can come: " + WhyNobody()); return; }
            rescue.ArrivalTick = AlignToHours(at, kind, false);
            forecast.Add(rescue);
            Counters.Planned++; Counters.DroughtBreaks++;
            lastArrivalTick = now;   // look again after another full wait, not on the next scheduler tick
            Diag($"drought breaker after {quiet / 60000f:0.0} quiet days: {rescue.Describe(map)}");
            HLog.Info("Inn", $"Drought breaker on {map}: no arrivals for {quiet / 60000f:0.0} days with room to spare, planned {rescue.Describe(map)}");
        }

        public static void Diag(string line) { DiagLog?.Add($"tick {Find.TickManager.TicksGame} (day {Find.TickManager.TicksGame / 60000f:0.00}): {line}"); }

        /// <summary>Why no faction could be picked: one line per faction that is not allowed (diagnostics only).</summary>
        string WhyNobody()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f.IsPlayer || f.Hidden || f.temporary || !f.def.humanlikeFaction) continue;
                string why = WhyCannotVisit(f, map);
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(f.Name).Append(": ").Append(why ?? "ok");
            }
            return sb.ToString();
        }

        /// <summary>The reason a faction cannot send guests now, or null when it can (mirrors <see cref="FactionCanVisit"/>).</summary>
        public static string WhyCannotVisit(Faction f, Map map)
        {
            if (f == null) return "none";
            if (f.IsPlayer) return "player";
            if (f.Hidden) return "hidden";
            if (f.defeated) return "defeated";
            if (f.temporary) return "temporary";
            if (!f.def.humanlikeFaction) return "not humanlike";
            if (f.HostileTo(Faction.OfPlayer)) return "hostile to us (goodwill " + f.PlayerGoodwill + ")";
            if (f.def.pawnGroupMakers == null || !f.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Peaceful || m.kindDef == PawnGroupKindDefOf.Trader)) return "no peaceful pawn group";
            if (map != null && NeutralGroupIncidentUtility.AnyBlockingHostileLord(map, f)) return "a hostile lord is on the map";
            return null;
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

        /// <summary>Factions that may send guests right now (friendly enough, door policy, no hostile lord on the map).</summary>
        List<Faction> CandidateFactions()
        {
            var door = GuestRegistry.Instance?.Door;
            return Find.FactionManager.AllFactionsListForReading.Where(f => FactionCanVisit(f, map) && (door == null || door.FactionOk(f))).ToList();
        }

        /// <summary>Weighted by goodwill; never a faction hostile to one that will be (or is) here at the same time (5.8).</summary>
        Faction PickFaction(int from, int to, RngStream rng)
        {
            var cands = CandidateFactions();
            cands.RemoveAll(f => forecast.Any(g => g.Faction != null && g.Faction.HostileTo(f) && Overlaps(g, from, to)));
            if (cands.Count == 0) return null;
            float total = cands.Sum(f => FactionWeight(f, map));
            double roll = rng.NextDouble() * total;
            foreach (var f in cands) { roll -= FactionWeight(f, map); if (roll <= 0) return f; }
            return cands[cands.Count - 1];
        }

        /// <summary>Goodwill weight; when the underworld is in use on <paramref name="map"/> (N08: a facility exists), respectable factions × exp(−N/40),
        /// rough ones × (0.2 + N/50) (5.14 Whispers). Without a map the whispers do not count.</summary>
        public static float FactionWeight(Faction f, Map map = null)
        {
            float w = 1f + Mathf.Max(0, f.PlayerGoodwill) / 25f;
            if (Modules.On(ModuleId.Underworld) && Hearth.Underworld.Underworld.InUse(map))
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

        enum ArriveResult { Arrived, NoBed, Closed, Refused, Moot }

        /// <summary>Fires the planned group. <paramref name="why"/> says what stopped it; Moot = nothing more to do (return visit with nobody left).</summary>
        ArriveResult TryArrive(PlannedGroup g, out string why)
        {
            why = null;
            var f = g.Faction;
            var door = GuestRegistry.Instance?.Door;
            if (door != null && !door.KindOk(g.Kind)) { why = "door policy refuses " + g.Kind; return ArriveResult.Refused; }
            if (!FactionCanVisit(f, map) || EnemyPresent(map, f) || (door != null && !door.FactionOk(f)))
            {
                var alt = Find.FactionManager.AllFactionsListForReading.Where(x => FactionCanVisit(x, map) && !EnemyPresent(map, x) && (door == null || door.FactionOk(x))).ToList();
                if (alt.Count == 0) { why = "no faction can come: " + (WhyCannotVisit(f, map) ?? (EnemyPresent(map, f) ? "an enemy is on the map" : "door policy")); return ArriveResult.Refused; }
                f = alt.RandomElement();
            }
            int size = g.Size;
            if (g.Kind == StayKind.Overnight)
            {
                int free = GuestBeds.FreeOn(map).Count;
                if (free <= 0) { why = "no free guest bed"; return ArriveResult.NoBed; }
                size = Math.Min(size, free);
            }
            else
            {
                OpeningWindow(out int open, out int close);
                float h = GenLocalDate.HourFloat(map);
                if (h < open || h > close - 3) { why = $"venues closed ({h:0.0} h, open {open}-{close})"; return ArriveResult.Closed; }
            }
            var req = new GuestArrivalRequest { Faction = f, Size = size, Kind = g.Kind, Nights = g.Nights };
            if (g.IsReturn)
            {
                if (f != g.Faction) { why = "a return visit is the same people or nobody"; return ArriveResult.Refused; }
                req.Returning = g.Returning.Where(p => GuestReturns.CanReturn(p, f)).Take(size).ToList();
                if (req.Returning.Count == 0) { GuestReturns.Unpin(g.Returning); why = "nobody left to come back"; return ArriveResult.Moot; } // drop quietly
                req.Size = req.Returning.Count;
            }
            bool ok = GuestArrival.TryFire(map, req) != null;
            if (ok) GuestReturns.Unpin(g.Returning);
            else why = "the arrival itself failed (see the Inn log)";
            return ok ? ArriveResult.Arrived : ArriveResult.Refused;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref forecast, "forecast", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Values.Look(ref everActive, "everActive");
            Scribe_Values.Look(ref planHorizon, "planHorizon", -1);
            Scribe_Values.Look(ref lastArrivalTick, "lastArrival", Never);
            Scribe_Values.Look(ref activeSince, "activeSince", Never);
            Scribe_Values.Look(ref lastTickSeen, "lastTickSeen", Never);
            Scribe_Values.Look(ref nextCandidate, "nextCandidate", -1);
            Scribe_Deep.Look(ref Counters, "stats");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Counters == null) Counters = new SchedulerStats();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { forecast = forecast ?? new List<PlannedGroup>(); forecast.RemoveAll(x => x == null || x.Faction == null); }
        }
    }
}
