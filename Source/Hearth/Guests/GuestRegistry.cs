// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Guests
{
    /// <summary>
    /// The guest register (5.1): every Inn guest's file, the groups, colony-wide Renown and Whispers (5.7) and the
    /// goodwill bookkeeping. Saved with the game; "is this a guest?" is a HashSet lookup for hot patches.
    /// Implements the shared <see cref="IGuestApiImpl"/> contract.
    /// </summary>
    public sealed class GuestRegistry : GameComponent, IGuestApiImpl
    {
        public static GuestRegistry Instance;
        static readonly HashSet<int> guestIds = new HashSet<int>();
        static readonly HashSet<int> restrictedIds = new HashSet<int>();

        public const float StartRenown = 10f;
        List<GuestRecord> records = new List<GuestRecord>();
        List<GuestGroup> groups = new List<GuestGroup>();
        int nextGroupId = 1;
        public float Renown = StartRenown;
        public float Whispers;
        /// <summary>Door policy / guest filters (5.9).</summary>
        public DoorPolicy Door = new DoorPolicy();
        public int GroupsHosted, GuestsHosted, ArrivalsAnnounced;
        public Money LifetimeIncome;
        /// <summary>World pawns we keep (ForcefullyKeptPawns) because they plan to come back (5.14, A5).</summary>
        public List<Pawn> PinnedReturners = new List<Pawn>();
        Dictionary<string, int> goodwillByQuadrum = new Dictionary<string, int>();
        int lastDriftDay = -1;
        readonly Dictionary<int, GuestRecord> byPawn = new Dictionary<int, GuestRecord>();

        public GuestRegistry(Game game) { Instance = this; guestIds.Clear(); restrictedIds.Clear(); GuestBeds.ClearAll(); GuestBedPricing.ClearCache(); }

        public IReadOnlyList<GuestRecord> Records => records;
        public IReadOnlyList<GuestGroup> Groups => groups;

        // ------------------------------------------------------------------ fast checks
        public static bool IsRegisteredGuest(Pawn p) => p != null && guestIds.Count > 0 && guestIds.Contains(p.thingIDNumber);
        /// <summary>Guests that currently must stay inside their wing (checked in, staying, not leaving).</summary>
        public static bool IsWingRestricted(Pawn p) => p != null && restrictedIds.Count > 0 && restrictedIds.Contains(p.thingIDNumber);
        public static int Stars => Mathf.Clamp(1 + (int)((Instance?.Renown ?? StartRenown) / 20f), 1, 5);
        public static string StarsText() { int s = Stars; return new string('★', s) + new string('☆', 5 - s); }

        public GuestRecord Get(Pawn p) => p != null && byPawn.TryGetValue(p.thingIDNumber, out var r) ? r : null;
        public GuestGroup Group(int id) { for (int i = 0; i < groups.Count; i++) if (groups[i].Id == id) return groups[i]; return null; }
        public IEnumerable<GuestRecord> Members(GuestGroup g) { for (int i = 0; i < records.Count; i++) if (records[i].GroupId == g.Id) yield return records[i]; }
        public IEnumerable<GuestRecord> OnMap(Map map) { for (int i = 0; i < records.Count; i++) if (records[i].MapId == map.uniqueID) yield return records[i]; }

        void Index(GuestRecord r)
        {
            if (r?.Pawn == null) return;
            byPawn[r.Pawn.thingIDNumber] = r; guestIds.Add(r.Pawn.thingIDNumber);
            UpdateRestriction(r);
        }

        void UpdateRestriction(GuestRecord r)
        {
            if (r?.Pawn == null) return;
            if (r.CheckedIn && r.Phase == GuestPhase.Staying && !r.Settled) restrictedIds.Add(r.Pawn.thingIDNumber);
            else restrictedIds.Remove(r.Pawn.thingIDNumber);
        }

        void RebuildIndex()
        {
            byPawn.Clear(); guestIds.Clear(); restrictedIds.Clear();
            foreach (var r in records) Index(r);
        }

        // ------------------------------------------------------------------ registration
        public GuestGroup NewGroup(Faction f, Map map, StayKind kind, bool byPod)
        {
            var g = new GuestGroup { Id = nextGroupId++, Faction = f, MapId = map.uniqueID, Kind = kind, ArrivedTick = Find.TickManager.TicksGame, ByPod = byPod };
            groups.Add(g);
            return g;
        }

        /// <summary>Creates the guest file, rolls wealth, budget and payment method and hands the guest its money (5.3, N42).</summary>
        public GuestRecord Register(Pawn p, GuestGroup g, Map map, int nights, int plannedLeaveTick, bool? forceDigital = null, Money? forceBudget = null)
        {
            if (p == null || g == null) return null;
            var existing = Get(p); if (existing != null) return existing;
            var rng = JRand.Stream("inn.guest");
            var r = new GuestRecord
            {
                Pawn = p, PawnId = p.thingIDNumber, GroupId = g.Id, MapId = map.uniqueID, Kind = g.Kind,
                ArrivedTick = Find.TickManager.TicksGame, PlannedLeaveTick = plannedLeaveTick, NightsPlanned = g.Kind == StayKind.Overnight ? nights : 0,
            };
            r.Wealth = GuestEconomy.RollWealth(p, g.Faction, Renown, rng);
            // wealth band of the door policy (N28): re-roll a few times, then clamp into the band
            for (int i = 0; i < DoorPolicy.Retries && !Door.WealthOk(r.Wealth); i++) r.Wealth = GuestEconomy.RollWealth(p, g.Faction, Renown, rng);
            r.Wealth = Door.ClampWealth(r.Wealth);
            float days = g.Kind == StayKind.Overnight ? nights + 0.5f : 0.35f;
            r.Budget = forceBudget ?? GuestEconomy.RollBudget(p, r.Wealth, days, g.ByPod, rng);
            r.StartBudget = r.Budget;
            r.PaysDigital = forceDigital ?? GuestEconomy.PaysDigital(g.Faction, r.Budget);
            r.Expectation = GuestEconomy.Expectation(r.Wealth, Renown, g.Faction);
            records.Add(r);
            Index(r);
            if (r.PaysDigital) Ledger.Current?.Post("world", r.Account, r.Budget, "Inn: card funds of " + p.LabelShort);
            else GiveSilver(p, r.Budget);
            EnsureTravelFood(p, days);
            r.Log("HH.Inn.Event.Arrived".TranslateOr("Arrived from {0}.").Replace("{0}", g.Faction?.Name ?? "?"));
            try { p.needs?.AddOrRemoveNeedsAsAppropriate(); } catch (Exception e) { HLog.Warn("Inn", "needs refresh failed for " + p + ": " + e.Message); }
            GuestsHosted++;
            HLog.Debug("Inn", $"Guest registered: {p.LabelShort} ({g.Faction?.Name}) W={r.Wealth} budget={r.Budget} digital={r.PaysDigital} kind={r.Kind} nights={nights}");
            return r;
        }

        static void GiveSilver(Pawn p, Money budget)
        {
            int coins = (int)Math.Ceiling(budget.Silver - 1e-9);
            if (coins <= 0 || p.inventory == null) return;
            while (coins > 0)
            {
                var s = ThingMaker.MakeThing(ThingDefOf.Silver);
                s.stackCount = Math.Min(coins, ThingDefOf.Silver.stackLimit);
                coins -= s.stackCount;
                if (!p.inventory.innerContainer.TryAdd(s)) { s.Destroy(); break; }
            }
        }

        /// <summary>Guests bring their own packed lunch so they never raid the colony's freezer (food is sold by venues).</summary>
        static void EnsureTravelFood(Pawn p, float days)
        {
            if (p.inventory == null || p.needs?.food == null) return;
            int have = 0;
            foreach (var t in p.inventory.innerContainer) if (t.def.IsNutritionGivingIngestible && t.def.ingestible.HumanEdible) have += t.stackCount;
            int want = Mathf.CeilToInt(Mathf.Max(1f, days) * 2f);
            if (have >= want) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Pemmican") ?? ThingDefOf.MealSurvivalPack;
            var food = ThingMaker.MakeThing(def);
            food.stackCount = Math.Min(def.stackLimit, def == ThingDefOf.MealSurvivalPack ? want - have : (want - have) * 4);
            if (!p.inventory.innerContainer.TryAdd(food)) food.Destroy();
        }

        /// <summary>Removes a guest file (card remainder back to the world, bed released). Never touches the pawn otherwise.</summary>
        public void Unregister(GuestRecord r, string why)
        {
            if (r == null) return;
            ReturnCardFunds(r);
            ReleaseBed(r);
            records.Remove(r);
            if (r.Pawn != null) { byPawn.Remove(r.Pawn.thingIDNumber); guestIds.Remove(r.Pawn.thingIDNumber); restrictedIds.Remove(r.Pawn.thingIDNumber); }
            else { byPawn.Remove(r.PawnId); guestIds.Remove(r.PawnId); restrictedIds.Remove(r.PawnId); }
            try { if (r.Pawn != null && !r.Pawn.Destroyed && !r.Pawn.Dead) r.Pawn.needs?.AddOrRemoveNeedsAsAppropriate(); } catch { }
            HLog.Debug("Inn", "Guest file closed: " + (r.Pawn?.LabelShort ?? ("#" + r.PawnId)) + " (" + why + ")");
            var g = Group(r.GroupId);
            if (g != null && !records.Any(x => x.GroupId == g.Id)) { if (!g.Reported && g.LeaveSent) SendReport(g); groups.Remove(g); }
        }

        void ReturnCardFunds(GuestRecord r)
        {
            if (!r.PaysDigital) return;
            var l = Ledger.Current; if (l == null) return;
            var bal = l.Balance(r.Account);
            if (bal.Micro > 0) l.Post(r.Account, "world", bal, "Inn: unused card funds return home");
        }

        void ReleaseBed(GuestRecord r)
        {
            try
            {
                if (r.Pawn?.ownership?.OwnedBed != null && (r.Bed == null || r.Pawn.ownership.OwnedBed == r.Bed)) r.Pawn.ownership.UnclaimBed();
                else if (r.Bed != null && r.Pawn != null && r.Bed.OwnersForReading.Contains(r.Pawn)) r.Bed.CompAssignableToPawn?.ForceRemovePawn(r.Pawn);
            }
            catch (Exception e) { HLog.Warn("Inn", "Could not release bed of " + r.Pawn + ": " + e.Message); }
            r.Bed = null;
        }

        // ------------------------------------------------------------------ check-in & beds
        public Building_FrontDesk FindDesk(Map map) => map?.listerBuildings.allBuildingsColonist.OfType<Building_FrontDesk>().FirstOrDefault(d => d.Spawned);

        /// <summary>Service bonus of a personal welcome by a host standing at the front desk (C7).</summary>
        public const float WelcomeBonus = 0.05f;

        /// <summary>Check-in at the front desk (or self check-in): picks a bed, charges the first night (5.4, 5.11).</summary>
        public bool CheckIn(Pawn p, Building_FrontDesk desk)
        {
            var r = Get(p);
            if (r == null || r.CheckedIn || r.Settled) return false;
            r.CheckedIn = true; r.CheckInTick = Find.TickManager.TicksGame; r.SelfCheckIn = desk == null;
            r.Phase = GuestPhase.Staying;
            r.ArrivalMood = p.needs?.mood?.CurLevel ?? 0.5f;
            // outside the desk's hours and without a host standing by, the desk is an honesty box (5.13, 6.1): a little cheaper
            bool honesty = desk != null && desk.Spawned && !GuestThoughts.DeskOpenNow(desk.Map) && !desk.HostNearby();
            if (honesty) { r.Log("HH.Inn.Event.HonestyBox".TranslateOr("Checked in at the honesty box; nobody was on duty.")); desk.Notify_CheckIn(p); }
            else if (desk != null)
            {
                var host = desk.Host();
                if (host != null)
                {
                    // C7: a host on duty welcomes the guest in person
                    r.ServiceSum += WelcomeBonus;
                    host.skills?.Learn(SkillDefOf.Social, 60f);
                    r.Log("HH.Inn.Event.Welcomed".TranslateOr("Was welcomed at the front desk by {0}.").Replace("{0}", host.LabelShort), 2);
                }
                else r.Log("HH.Inn.Event.CheckedIn".TranslateOr("Checked in at the front desk."));
                desk.Notify_CheckIn(p);
            }
            else r.Log("HH.Inn.Event.SelfCheckIn".TranslateOr("Found the room alone; nobody was at a desk."));
            if (r.Overnight)
            {
                if (!AssignBed(r))
                {
                    bool anyFree = GuestBeds.FreeOn(p.MapHeld).Count > 0;
                    r.Log(anyFree ? "HH.Inn.Event.NoBed".TranslateOr("Could not get an affordable bed and stayed for the day only.")
                                  : "HH.Inn.Event.NoFreeBed".TranslateOr("Found every guest bed taken and stayed for the day only."), 6);
                    r.ServiceSum -= 0.15f;
                    r.Kind = StayKind.DayVisitor; r.NightsPlanned = 0;
                    int leave = Find.TickManager.TicksGame + 2500 * 4;
                    if (r.PlannedLeaveTick > leave) r.PlannedLeaveTick = leave;
                }
                else if (!ApplyHonestyDiscount(r, honesty) || !ChargeNight(r, first: true))
                {
                    r.Log("HH.Inn.Event.CouldNotPay".TranslateOr("Could not pay for the night."));
                    ReleaseBed(r); r.Kind = StayKind.DayVisitor; r.NightsPlanned = 0;
                    r.PlannedLeaveTick = Math.Min(r.PlannedLeaveTick, Find.TickManager.TicksGame + 2500 * 2);
                }
            }
            UpdateRestriction(r);
            return true;
        }

        /// <summary>A couple pays this many times the price of one bed (C4).</summary>
        public const float CoupleFactor = 1.3f;

        public bool AssignBed(GuestRecord r)
        {
            var p = r.Pawn; var map = p?.MapHeld; if (map == null) return false;
            Building_Bed best = null; BedQuote bestQ = null; float bestU = float.MinValue;
            Building_Bed cheapest = null; BedQuote cheapQ = null;
            int nights = Math.Max(1, r.NightsPlanned);
            var groupRooms = new HashSet<Room>();
            foreach (var m in records) if (m.GroupId == r.GroupId && m.Bed != null && m.Bed.Spawned) groupRooms.Add(m.Bed.GetRoom());
            bool partnerWaiting = false;
            foreach (var m in records)
                if (m != r && m.GroupId == r.GroupId && m.Bed == null && !m.Settled && m.Pawn != null && LovePartnerRelationUtility.LovePartnerRelationExists(p, m.Pawn)) { partnerWaiting = true; break; }
            // C4: couples share their partner's double bed and pay the room price × 1.3 together (the second pays the 0.3)
            foreach (var m in records)
            {
                if (m == r || m.GroupId != r.GroupId || m.Bed == null || !m.Bed.Spawned) continue;
                if (!GuestBeds.SharableWith(m.Bed, p, r.GroupId) || !p.CanReach(m.Bed, PathEndMode.OnCell, Danger.Some)) continue;
                var share = Money.FromSilver(Math.Round(m.NightPrice.Silver * (CoupleFactor - 1f)));
                if (share * nights > r.Budget) continue;
                var cq = GuestBedPricing.Quote(m.Bed); if (cq == null) continue;
                cq.Price = share;
                if (!ClaimBed(r, m.Bed, cq)) continue;
                r.Log("HH.Inn.Event.SharedBed".TranslateOr("Shares the bed with {0} ({1} silver a night extra).").Replace("{0}", m.Pawn.LabelShort).Replace("{1}", share.WholeSilver.ToString()));
                return true;
            }
            foreach (var b in GuestBeds.FreeOn(map))
            {
                if (!p.CanReach(b, PathEndMode.OnCell, Danger.Some)) continue;
                if (!RestUtility.CanUseBedEver(p, b.def)) continue;
                var q = GuestBedPricing.Quote(b); if (q == null) continue;
                if (cheapQ == null || q.Price < cheapQ.Price) { cheapest = b; cheapQ = q; }
                if (q.Price * nights > r.Budget) continue;
                float u = GuestEconomy.BedUtility(q.RoomScore, q.Price, r.Wealth, groupRooms.Contains(b.GetRoom()));
                if (partnerWaiting && b.SleepingSlotsCount >= 2) u += 40f; // C4: a couple's first guest takes a bed for two
                if (u > bestU) { bestU = u; best = b; bestQ = q; }
            }
            if (best == null && cheapest != null && cheapQ.Price <= r.Budget)
            {
                best = cheapest; bestQ = cheapQ;
                r.NightsPlanned = Math.Max(1, (int)(r.Budget.Micro / Math.Max(1, cheapQ.Price.Micro)));
                r.Log("HH.Inn.Event.ShortenedStay".TranslateOr("Shortened the stay to {0} night(s) to afford the room.").Replace("{0}", r.NightsPlanned.ToString()), 4);
                r.PlannedLeaveTick = PlannedCheckout(map, r.NightsPlanned);
            }
            if (best == null) return false;
            if (!ClaimBed(r, best, bestQ)) return false;
            if (bestQ.Price > GuestEconomy.ComfortPrice(r.Wealth))
            {
                r.ServiceSum -= 0.08f;
                r.Log("HH.Inn.Event.Pricey".TranslateOr("Found {0} silver a night steep.").Replace("{0}", bestQ.Price.WholeSilver.ToString()), 4);
            }
            return true;
        }

        /// <summary>Honesty box: the booked night price drops by the discount, rounded to whole silver (always true, for chaining).</summary>
        static bool ApplyHonestyDiscount(GuestRecord r, bool honesty)
        {
            float d = HSettings.Get(InnSettings.HonestyDiscount);
            if (!honesty || d <= 0f || r.NightPrice.Micro <= 0) return true;
            r.NightPrice = Money.FromSilver(Math.Max(1, (int)Math.Round(r.NightPrice.Silver * (1f - d))));
            r.Log("HH.Inn.Event.HonestyPrice".TranslateOr("Paid the honesty-box price of {0} silver a night.").Replace("{0}", r.NightPrice.WholeSilver.ToString()));
            return true;
        }

        bool ClaimBed(GuestRecord r, Building_Bed b, BedQuote q)
        {
            try
            {
                if (r.Pawn.ownership != null) r.Pawn.ownership.ClaimBedIfNonMedical(b);
                b.NotifyRoomAssignedPawnsChanged();
            }
            catch (Exception e) { HLog.Warn("Inn", "Bed claim failed for " + r.Pawn + ": " + e.Message); return false; }
            r.Bed = b; r.NightPrice = q.Price; r.RoomScore = q.RoomScore; r.RoomClass = q.ClassKey; r.RoomLabel = q.ClassLabel;
            r.Log("HH.Inn.Event.GotRoom".TranslateOr("Took a bed in a {0} for {1} silver a night.").Replace("{0}", q.ClassLabel).Replace("{1}", q.Price.WholeSilver.ToString()));
            GuestWings.EnsureWingFor(b);
            return true;
        }

        public void Notify_BedLost(Pawn p, Building_Bed b)
        {
            var r = Get(p); if (r == null) return;
            ReleaseBed(r);
            r.Log("HH.Inn.Event.BedTaken".TranslateOr("Lost the bed in the middle of the stay."), 5);
            r.ServiceSum -= 0.1f;
        }

        /// <summary>Re-find a bed for a checked-in guest who lost theirs (price stays as booked).</summary>
        public bool TryReassignBed(GuestRecord r)
        {
            if (r == null || !r.CheckedIn || !r.Overnight || r.Bed != null || r.Pawn?.MapHeld == null) return false;
            var p = r.Pawn;
            var b = GuestBeds.FreeOn(p.MapHeld).Where(x => p.CanReach(x, PathEndMode.OnCell, Danger.Some)).OrderByDescending(x => GuestBedPricing.RoomScore(x.GetRoom())).FirstOrDefault();
            if (b == null) return false;
            var price = r.NightPrice; var q = GuestBedPricing.Quote(b); if (q == null) return false;
            bool ok = ClaimBed(r, b, q); r.NightPrice = price;
            return ok;
        }

        public bool ChargeNight(GuestRecord r, bool first = false)
        {
            if (r.Bed == null) return false;
            if (r.NightPrice.Micro <= 0)
            {
                // a free bed (price rule "free", or the partner already pays for it): the night is booked without money
                r.NightsPaid++; r.LastNightTick = Find.TickManager.TicksGame;
                if (first) r.Log("HH.Inn.Event.FreeNight".TranslateOr("Stays the night for free."));
                return true;
            }
            string memo = "Inn: night in " + (r.RoomLabel ?? "a room") + " (" + r.Pawn.LabelShort + ")";
            bool ok = GuestApi.ChargeGuest(r.Pawn, r.NightPrice, "inn.room", memo);
            if (!ok) return false;
            r.NightsPaid++; r.LastNightTick = Find.TickManager.TicksGame;
            LifetimeIncome += r.NightPrice;
            r.Log((first ? "HH.Inn.Event.PaidFirstNight" : "HH.Inn.Event.PaidNight").TranslateOr(first ? "Paid {0} silver for the first night ({1})." : "Paid {0} silver for another night ({1}).")
                .Replace("{0}", r.NightPrice.WholeSilver.ToString()).Replace("{1}", r.PaysDigital ? "HH.Inn.Card".TranslateOr("card") : "HH.Inn.Cash".TranslateOr("cash")));
            return true;
        }

        public static int PlannedCheckout(Map map, int nights)
        {
            int now = Find.TickManager.TicksGame;
            float hour = GenLocalDate.HourFloat(map);
            float co = HSettings.GetInt(InnSettings.CheckOutHour);
            float toMorning = hour < co - 2f ? co - hour : 24f - hour + co;
            return now + (int)(toMorning * 2500f) + Math.Max(0, nights - 1) * 60000;
        }

        // ------------------------------------------------------------------ ticking
        public void Tick(int t)
        {
            if (records.Count == 0 && groups.Count == 0) { DailyDrift(t); return; }
            if (t % 250 == 61) for (int i = records.Count - 1; i >= 0; i--) { var r = records[i]; Guard.Run(ModuleId.Inn, "Inn.UpdateRecord", () => UpdateRecord(r)); }
            if (t % 1250 == 611) for (int i = groups.Count - 1; i >= 0; i--) { var g = groups[i]; Guard.Run(ModuleId.Inn, "Inn.GroupTick", () => GroupTick(g)); }
            DailyDrift(t);
        }

        void DailyDrift(int t)
        {
            int day = t / 60000;
            if (day == lastDriftDay || t % 2500 != 1777) return;
            if (lastDriftDay < 0) { lastDriftDay = day; return; }
            lastDriftDay = day;
            Renown += (20f - Renown) * (0.01f / 15f);
            Whispers = Math.Max(0f, Whispers * (1f - 0.02f / 15f));
        }

        void UpdateRecord(GuestRecord r)
        {
            var p = r.Pawn;
            if (p == null || p.Dead || p.Destroyed || !p.Spawned) return;
            var mood = p.needs?.mood;
            if (mood != null && r.Phase != GuestPhase.Leaving) { r.MoodSum += mood.CurLevel; r.MoodSamples++; if (r.ArrivalMood < 0) r.ArrivalMood = mood.CurLevel; }
            Recompute(r);
        }

        /// <summary>V = clamp01(0.45·M̄ + 0.35·X̄ + 0.20·(Δ + 0.5)) − incidents; Satisfaction = clamp(2·(V − E), −1, 1)  (5.6)</summary>
        public void Recompute(GuestRecord r)
        {
            float m = r.MoodMean;
            float x = Mathf.Clamp01(0.5f + r.ServiceSum);
            float cur = r.Pawn?.needs?.mood?.CurLevel ?? m;
            float delta = r.ArrivalMood >= 0 ? cur - r.ArrivalMood : 0f;
            r.Value = Mathf.Clamp01(Mathf.Clamp01(0.45f * m + 0.35f * x + 0.20f * (delta + 0.5f)) - r.Incidents);
            r.Satisfaction = Mathf.Clamp(2f * (r.Value - r.Expectation), -1f, 1f);
        }

        void GroupTick(GuestGroup g)
        {
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == g.MapId);
            var members = Members(g).ToList();
            if (members.Count == 0) { if (g.LeaveSent && !g.Reported) SendReport(g); groups.Remove(g); return; }
            if (map == null || g.LeaveSent) return;
            int now = Find.TickManager.TicksGame;
            float hour = GenLocalDate.HourFloat(map);
            bool wantLeave = false; string reason = "planned";
            foreach (var r in members)
            {
                if (r.Settled || r.Pawn == null || !r.Pawn.Spawned) continue;
                // late check-in: nobody waits forever at an empty desk
                if (!r.CheckedIn && now - r.ArrivedTick > 2500 * 5 && r.CheckInFailures >= 0) { CheckIn(r.Pawn, null); }
                if (r.CheckedIn && r.Overnight)
                {
                    if (r.Bed == null) TryReassignBed(r);
                    // morning verdict on last night's bed
                    if (r.Bed != null && hour >= 6f && hour < 10f && now - r.CheckInTick > 2500 * 4 && now - r.LastVerdictTick > 2500 * 16) GiveNightVerdict(r);
                    // evening charge for another night
                    // a paid night is only charged again once it has been slept (check-in pays for tonight)
                    if (hour >= 18f && r.NightsPaid < r.NightsPlanned && r.NightsSlept >= r.NightsPaid && now - r.LastNightTick > 2500 * 10 && r.PlannedLeaveTick - now > 2500 * 8)
                    {
                        if (!ChargeNight(r)) { r.Log("HH.Inn.Event.CouldNotPay".TranslateOr("Could not pay for the night.")); wantLeave = true; reason = "money"; }
                    }
                }
                if (now >= r.PlannedLeaveTick) wantLeave = true;
                // day visitors go home at "last orders": 30 minutes before every venue has closed (5.13)
                if (r.Kind == StayKind.DayVisitor && now - r.ArrivedTick > 1250 && !AnyVenueOpenSoon(map)) { wantLeave = true; reason = "closing"; }
                if (HSettings.GetBool(InnSettings.LeaveEarly) && r.CheckedIn)
                {
                    if (r.Satisfaction < -0.6f) { if (r.LowSince < 0) r.LowSince = now; else if (now - r.LowSince > 2500 * 8) { wantLeave = true; reason = "unhappy"; } }
                    else r.LowSince = -1;
                }
            }
            if (wantLeave) DepartGroup(g, reason);
        }

        /// <summary>True when the map has no venues at all (nothing to close), or one of them is open now and still in 30 minutes.</summary>
        public static bool AnyVenueOpenSoon(Map map)
        {
            var all = Venues.All(map);
            if (all.Count == 0) return true;
            int h = GenLocalDate.HourOfDay(map);
            int h2 = (int)(GenLocalDate.HourFloat(map) + 0.5f) % 24;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i].Comp; if (c == null) continue;
                if (c.alwaysOpen || (c.IsOpenAt(h) && c.IsOpenAt(h2))) return true;
            }
            return false;
        }

        void GiveNightVerdict(GuestRecord r)
        {
            r.NightsSlept++;
            r.LastVerdictTick = Find.TickManager.TicksGame;
            float comfort = r.Bed.GetStatValue(StatDefOf.Comfort);
            float rs = GuestBedPricing.RoomScore(r.Bed.GetRoom());
            float d = (rs + 10f * comfort - GuestEconomy.ExpectedRoomScore(r.Wealth)) / 15f;
            int stage = d < -2f ? 0 : d < -1f ? 1 : d < 0f ? 2 : d < 1f ? 3 : d < 2f ? 4 : 5;
            r.ServiceSum += Mathf.Clamp(d * 0.05f, -0.15f, 0.15f);
            try
            {
                var th = (Thought_Memory)ThoughtMaker.MakeThought(InnDefOf.HH_GuestRoomVerdict, stage);
                r.Pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(th);
            }
            catch (Exception e) { HLog.Once("inn.verdictThought", LogLevel.Warn, "Inn", "Room verdict thought failed: " + e.Message); }
            string[] verdict = { "HH.Inn.Verdict.0", "HH.Inn.Verdict.1", "HH.Inn.Verdict.2", "HH.Inn.Verdict.3", "HH.Inn.Verdict.4", "HH.Inn.Verdict.5" };
            string[] fallback = { "Slept badly and blamed the room.", "Found the room below their standard.", "Slept so-so.", "Slept fine.", "Slept well.", "Slept like royalty." };
            r.Log(verdict[stage].TranslateOr(fallback[stage]), stage == 0 ? 6 : stage == 5 ? 6 : stage == 1 ? 5 : stage == 4 ? 3 : 1);
        }

        // ------------------------------------------------------------------ departure, settlement (5.7, 5.14)
        /// <summary>Settles every member (points, refunds, recruitment), books goodwill and renown, sends the report, then tells the lord to leave.</summary>
        public void DepartGroup(GuestGroup g, string reason)
        {
            if (g == null || g.LeaveSent) return;
            g.LeaveSent = true; g.LeaveReason = reason;
            var members = Members(g).ToList();
            foreach (var r in members) Settle(r, reason);
            FinishGroupNumbers(g, members);
            var lords = new HashSet<Lord>();
            foreach (var r in members) { var l = r.Pawn?.GetLord(); if (l != null && l.LordJob is LordJob_HH_GuestStay) lords.Add(l); }
            foreach (var l in lords) l.ReceiveMemo(LordJob_HH_GuestStay.MemoLeave);
            SendReport(g);
        }

        /// <summary>Called by the lord when the group leaves for its own reasons (danger, wounded, hostile).</summary>
        public void Notify_LordLeaving(Lord lord, string reason)
        {
            var job = lord?.LordJob as LordJob_HH_GuestStay; if (job == null) return;
            var g = Group(job.groupId); if (g == null || g.LeaveSent) return;
            g.LeaveSent = true; g.LeaveReason = reason;
            var members = Members(g).ToList();
            foreach (var r in members) Settle(r, reason);
            FinishGroupNumbers(g, members);
            SendReport(g);
        }

        void Settle(GuestRecord r, string reason)
        {
            if (r.Settled) return;
            Recompute(r);
            r.Settled = true; r.Phase = GuestPhase.Leaving;
            UpdateRestriction(r);
            var p = r.Pawn;
            bool alive = p != null && !p.Dead && !p.Destroyed;
            bool hostile = alive && p.HostileTo(Faction.OfPlayer);
            var rng = JRand.Stream("inn.departure");
            float m = r.MoodMean;
            float s = r.Value - r.Expectation;
            r.GoodwillPoints = 0;
            if (alive && !hostile && !r.SentAway && !p.IsPrisoner)
            {
                if (m < 0.25f || r.Incidents > 0.001f || s < -0.3f)
                {
                    int loss = Mathf.RoundToInt(5f * Mathf.Clamp01((0.25f - m) / 0.25f)) + Mathf.RoundToInt(r.Incidents * 8f);
                    if (s < -0.3f) loss = Math.Max(loss, 1);
                    r.GoodwillPoints = -Math.Max(0, loss);
                }
                else
                {
                    float roll = (float)rng.Range(-0.75, 0.75);
                    r.GoodwillPoints = Mathf.Clamp(Mathf.RoundToInt(10f * (m - 0.35f) + 4f * s + roll), 0, 5);
                }
                if (r.Insulted) r.GoodwillPoints -= 1;
            }
            r.Outcome = s > 0.25f ? "great" : s > 0.05f ? "good" : s > -0.1f ? "fine" : s > -0.3f ? "poor" : "bad";
            if (alive && !hostile) TryRecruitAtDeparture(r, s);
            if (alive && !hostile && !r.SentAway && p.Spawned && p.Faction != Faction.OfPlayer && reason != "unhappy" && reason != "money" && reason != "removal")
                Guard.Run(ModuleId.Inn, "Inn.Gift", () => GuestReturns.TryGift(r, p.Map));
            ReturnCardFunds(r);
            ReleaseBed(r);
            if (r.SentAway) r.Log("HH.Inn.Event.SentAway".TranslateOr("Was sent away."));
            else if (reason == "unhappy") r.Log("HH.Inn.Event.LeftUnhappy".TranslateOr("Left early, unhappy."));
            else if (reason == "money") r.Log("HH.Inn.Event.LeftBroke".TranslateOr("Left early: the money ran out."));
            HLog.Debug("Inn", $"Settled {p?.LabelShort}: M={m:0.00} V={r.Value:0.00} E={r.Expectation:0.00} points={r.GoodwillPoints} spent={r.Spent}");
        }

        void FinishGroupNumbers(GuestGroup g, List<GuestRecord> members)
        {
            if (members.Count == 0) return;
            float sBar = members.Average(x => x.Value - x.Expectation);
            g.MeanSurplus = sBar;
            g.SpentTotal = Money.Zero;
            foreach (var r in members) { g.SpentTotal += r.Spent; if (r.Pawn != null) g.Names.Add(r.Pawn.LabelShort); }
            int sum = members.Sum(x => x.GoodwillPoints);
            g.GoodwillTotal = ApplyGoodwill(g.Faction, sum);
            // Renown: ΔR = 6·√size/2·S̄·(S̄>0 ? (1 − R/120) : (1 + R/200)), loss capped per group (5.7)
            if (!members.All(x => x.SentAway))
            {
                float dR = 6f * Mathf.Sqrt(members.Count) / 2f * sBar * (sBar > 0 ? (1f - Renown / 120f) : (1f + Renown / 200f));
                float cap = HSettings.Get(InnSettings.RenownLossCap);
                if (dR < -cap) dR = -cap;
                g.RenownDelta = ChangeRenown(dR);
            }
            else g.RenownDelta = ChangeRenown(-1f);
            GroupsHosted++;
            if (g.LeaveReason != "hostile" && g.LeaveReason != "sentAway" && g.LeaveReason != "removal")
            {
                var map = Find.Maps.FirstOrDefault(m => m.uniqueID == g.MapId);
                Guard.Run(ModuleId.Inn, "Inn.PlanReturn", () => GuestReturns.PlanReturn(g, members.Where(m => m.Pawn != null && m.Pawn.Faction == g.Faction).ToList(), map));
            }
            g.ReportLines.Clear();
            foreach (var r in members) g.ReportLines.Add(DepartureReport.GuestLine(r));
        }

        /// <summary>Changes renown, respecting the floor on easy tiers (never below the start value up to Standard Room).</summary>
        public float ChangeRenown(float delta) => ChangeRenown(delta, null);

        public float ChangeRenown(float delta, string reason)
        {
            float before = Renown;
            Renown = Mathf.Clamp(Renown + delta, 0f, 100f);
            float tier = HearthProfile.Current?.Tier ?? 3f;
            if (tier <= 3.001f && Renown < StartRenown && before >= StartRenown) Renown = StartRenown;
            if (tier <= 3.001f && Renown < before && before < StartRenown) Renown = before;
            if (!reason.NullOrEmpty() && Math.Abs(Renown - before) > 0.0001f) HLog.Info("Inn", $"Renown {Renown - before:+0.0;-0.0} ({reason}) → {Renown:0.0}");
            return Renown - before;
        }

        /// <summary>Whispers as other modules see them: the Underworld module owns them while it is on; otherwise the Inn keeps its own count.</summary>
        public float CurrentWhispers => UnderworldOn ? Hearth.Underworld.Underworld.Whispers : Whispers;

        static bool UnderworldOn => Modules.On(ModuleId.Underworld) && Hearth.Underworld.Underworld.Comp != null;

        /// <summary>Raises (or lowers) whispers: forwarded to the Underworld module when it runs, else kept here.</summary>
        public void AddWhispers(float delta, string reason)
        {
            if (UnderworldOn) { Hearth.Underworld.Underworld.AddWhispers(delta, reason); return; }
            Whispers = Mathf.Clamp(Whispers + delta, 0f, 100f);
        }

        static int QuadrumIndex => Find.TickManager.TicksAbs / 900000;

        /// <summary>Books goodwill with the vanilla system; +cap per faction and quadrum, never above the ceiling by hosting, loss capped per visit.</summary>
        public int ApplyGoodwill(Faction f, int sum)
        {
            if (f == null || f.IsPlayer || f.defeated || sum == 0) return 0;
            int delta;
            if (sum > 0)
            {
                string key = f.GetUniqueLoadID() + "|" + QuadrumIndex;
                int used = goodwillByQuadrum.TryGetValue(key, out int u) ? u : 0;
                int capLeft = Math.Max(0, HSettings.GetInt(InnSettings.GoodwillCapQuadrum) - used);
                int room = Math.Max(0, HSettings.GetInt(InnSettings.GoodwillCeiling) - f.PlayerGoodwill);
                delta = Math.Min(sum, Math.Min(capLeft, room));
                if (delta <= 0) return 0;
                goodwillByQuadrum[key] = used + delta;
                PruneGoodwillKeys();
            }
            else delta = Math.Max(sum, -HSettings.GetInt(InnSettings.GoodwillLossCap));
            if (delta == 0) return 0;
            try { f.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: false, canSendHostilityLetter: true, reason: InnDefOf.HH_HostedGuests); }
            catch (Exception e) { HLog.Error("Inn", "Goodwill change failed", e); return 0; }
            return delta;
        }

        void PruneGoodwillKeys()
        {
            int q = QuadrumIndex;
            foreach (var k in goodwillByQuadrum.Keys.ToList())
            {
                int bar = k.LastIndexOf('|');
                if (bar > 0 && int.TryParse(k.Substring(bar + 1), out int kq) && kq < q - 4) goodwillByQuadrum.Remove(k);
            }
        }

        void SendReport(GuestGroup g)
        {
            if (g.Reported) return;
            g.Reported = true;
            Guard.Run(ModuleId.Inn, "Inn.DepartureReport", () => DepartureReport.Send(g, Find.Maps.FirstOrDefault(m => m.uniqueID == g.MapId)));
        }

        void TryRecruitAtDeparture(GuestRecord r, float s)
        {
            if (r.Handling != HandlingMode.Recruit || r.Insulted || r.SentAway) return;
            var p = r.Pawn; var map = p.MapHeld; if (map == null) return;
            if (TooImportantToJoin(p)) { r.Log("HH.Inn.Event.TooImportant".TranslateOr("Too important at home to stay."), 4); return; }
            var colonists = map.mapPawns.FreeColonistsSpawned;
            int bond = Bond(p, map, out int threshold);
            if (bond < threshold) { r.Log("HH.Inn.Event.BondTooLow".TranslateOr("Liked the place, but not enough to stay (bond {0}/{1}).").Replace("{0}", bond.ToString()).Replace("{1}", threshold.ToString()), 5); return; }
            float chance = RecruitChanceOverride ?? RecruitChance(s, bond, threshold);
            if (!JRand.Stream("inn.recruit").Chance(chance)) { r.Log("HH.Inn.Event.Declined".TranslateOr("Thought about staying for good, then went home."), 5); return; }
            var recruiter = colonists.Where(c => c.relations != null).OrderByDescending(c => c.relations.OpinionOf(p)).FirstOrDefault();
            if (recruiter == null) return;
            var f = p.Faction;
            int cost = RecruitGoodwillCost(p);
            try
            {
                ReturnCardFunds(r);
                InteractionWorker_RecruitAttempt.DoRecruit(recruiter, p, true);
                if (f != null && !f.IsPlayer) f.TryAffectGoodwillWith(Faction.OfPlayer, -cost, canSendMessage: true, canSendHostilityLetter: true, reason: InnDefOf.HH_HostedGuests);
                r.Log("HH.Inn.Event.Joined".TranslateOr("Decided to stay and joined the colony."));
                r.Outcome = "joined";
                HLog.Info("Inn", $"{p.LabelShort} joined at departure (bond {bond}/{threshold}, chance {chance:0.00}, goodwill −{cost})");
            }
            catch (Exception e) { HLog.Error("Inn", "Recruitment at departure failed", e); }
        }

        /// <summary>Faction leaders and titled guests never join at departure.</summary>
        public static bool TooImportantToJoin(Pawn p) => p != null && (p.Faction?.leader == p || (p.royalty != null && p.royalty.AllTitlesForReading.Count > 0));

        /// <summary>Join chance at departure once the bond is reached: clamp(0.35 + 0.5·S + 0.01·(bond − threshold), 0.05, 0.9), S = V − E.</summary>
        public static float RecruitChance(float surplus, int bond, int threshold) => Mathf.Clamp(0.35f + 0.5f * surplus + 0.01f * (bond - threshold), 0.05f, 0.9f);

        /// <summary>Goodwill the guest's faction loses when the guest joins: 5 + value/200 + 30/(1 + settlements), 5 … 40.</summary>
        public static int RecruitGoodwillCost(Pawn p)
        {
            var f = p?.Faction; if (f == null || f.IsPlayer) return 0;
            int settlements = Find.WorldObjects.Settlements.Count(x => x.Faction == f);
            return Mathf.Clamp(Mathf.RoundToInt(5f + p.MarketValue / 200f + 30f / (1f + settlements)), 5, 40);
        }

        /// <summary>Bond = sum of all colonists' opinions of the guest (rivals count negative); threshold 30 + 4·n (5.10).</summary>
        public static int Bond(Pawn guest, Map map, out int threshold)
        {
            var cols = map.mapPawns.FreeColonistsSpawned;
            threshold = BondThresholdOverride ?? 30 + 4 * cols.Count;
            int sum = 0;
            foreach (var c in cols) if (c.relations != null && c != guest) sum += c.relations.OpinionOf(guest);
            return sum;
        }

        /// <summary>A Hosting colonist chatted with the guest (Accompany/Recruit). Recruit talks can offend (5.10).</summary>
        public void Notify_Talk(Pawn host, Pawn guest)
        {
            var r = Get(guest); if (r == null || r.Settled) return;
            int now = Find.TickManager.TicksGame;
            r.Talks++;
            bool firstToday = now - r.LastAccompanyTick > 30000;
            r.LastAccompanyTick = now;
            float social = host.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5;
            if (firstToday) r.ServiceSum += 0.1f;
            int stage = social < 5 ? 0 : social < 10 ? 1 : social < 15 ? 2 : 3;
            try
            {
                var th = (Thought_Memory)ThoughtMaker.MakeThought(InnDefOf.HH_GuestAccompanied, stage);
                guest.needs?.mood?.thoughts?.memories?.TryGainMemory(th, host);
                if (r.Handling == HandlingMode.Recruit)
                    host.needs?.mood?.thoughts?.memories?.TryGainMemory(InnDefOf.HH_HostedWell, guest);
            }
            catch (Exception e) { HLog.Once("inn.talkThought", LogLevel.Warn, "Inn", "Accompany thought failed: " + e.Message); }
            host.skills?.Learn(SkillDefOf.Social, 40f);
            if (r.Handling == HandlingMode.Recruit && !r.Insulted)
            {
                bool loyal = guest.Faction?.leader == guest || (guest.royalty != null && guest.royalty.AllTitlesForReading.Count > 0);
                float p = InsultChanceOverride ?? (HSettings.Get(InnSettings.InsultBase) * (1f - social / 20f) + (loyal ? 0.10f : 0f));
                if (JRand.Stream("inn.insult").Chance(p))
                {
                    r.Insulted = true;
                    r.Budget = r.Budget * 0.7;
                    r.PlannedLeaveTick -= 30000;
                    try { guest.needs?.mood?.thoughts?.memories?.TryGainMemory(InnDefOf.HH_GuestPestered, host); } catch { }
                    r.Log("HH.Inn.Event.Insulted".TranslateOr("Felt pestered by {0}'s recruiting.").Replace("{0}", host.LabelShort), 7);
                    Messages.Message("HH.Inn.Msg.Insulted".TranslateOr("{0} found the recruiting pushy and will spend less and leave sooner.").Replace("{0}", guest.LabelShort), guest, MessageTypeDefOf.NegativeEvent, false);
                }
                else r.Log("HH.Inn.Event.RecruitTalk".TranslateOr("Had a long talk with {0} about staying.").Replace("{0}", host.LabelShort), 3);
            }
            else if (firstToday) r.Log("HH.Inn.Event.Accompanied".TranslateOr("Was shown around by {0}.").Replace("{0}", host.LabelShort), 2);
        }

        /// <summary>"Send away" (5.10): leaves at once, no points, renown −1.</summary>
        public void SendAway(Pawn p)
        {
            var r = Get(p); if (r == null) return;
            var g = Group(r.GroupId); if (g == null) return;
            foreach (var m in Members(g)) m.SentAway = true;
            DepartGroup(g, "sentAway");
        }

        /// <summary>Called by LordJob_HH_GuestStay when a member leaves the lord.</summary>
        public void Notify_PawnLost(Pawn p, PawnLostCondition cond)
        {
            var r = Get(p); if (r == null) return;
            var g = Group(r.GroupId);
            switch (cond)
            {
                case PawnLostCondition.ExitedMap:
                    if (!r.Settled) { if (g != null && !g.LeaveSent) Notify_LordLeavingGroup(g, "exited"); else Settle(r, "exited"); }
                    break;
                case PawnLostCondition.Killed:
                    r.Log("HH.Inn.Event.Died".TranslateOr("Died during the stay."));
                    ChangeRenown(-Math.Min(3f, HSettings.Get(InnSettings.RenownLossCap) + 1f));
                    AddWhispers(4f, "guest died during the stay");
                    foreach (var m in Members(g ?? new GuestGroup { Id = -9 })) if (m != r) m.Incidents += 0.03f;
                    r.Settled = true; r.GoodwillPoints = 0;
                    break;
                case PawnLostCondition.MadePrisoner:
                    r.Log("HH.Inn.Event.Arrested".TranslateOr("Was arrested."));
                    ChangeRenown(-Math.Min(8f, HSettings.Get(InnSettings.RenownLossCap) * 1.5f + 1f));
                    AddWhispers(5f, "guest arrested");
                    r.Settled = true;
                    break;
                default:
                    if (!r.Settled) Settle(r, cond.ToString());
                    break;
            }
            Unregister(r, cond.ToString());
        }

        void Notify_LordLeavingGroup(GuestGroup g, string reason)
        {
            g.LeaveSent = true; g.LeaveReason = reason;
            var members = Members(g).ToList();
            foreach (var r in members) Settle(r, reason);
            FinishGroupNumbers(g, members);
            SendReport(g);
        }

        public void Notify_Incident(Pawn p, float amount, string what)
        {
            var r = Get(p); if (r == null) return;
            r.Incidents = Mathf.Min(1f, r.Incidents + amount);
            r.Log(what);
        }

        // ------------------------------------------------------------------ caretaker (N69)
        public int CaretakerRecords()
        {
            int fixes = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var r = records[i];
                string why = null;
                var p = r.Pawn;
                if (p == null) why = "guest file without pawn";
                else if (p.Destroyed) why = "pawn destroyed";
                else if (p.Dead) why = "pawn dead";
                else if (p.Faction == Faction.OfPlayer) why = "pawn joined the colony";
                else if (!p.Spawned && p.ParentHolder is not ActiveTransporterInfo && !(p.ParentHolder is Thing) && Find.TickManager.TicksGame - r.ArrivedTick > 2500)
                    why = "pawn despawned";
                else if (p.Spawned && p.GetLord() == null && Find.TickManager.TicksGame - r.ArrivedTick > 600) why = "guest without lord";
                else if (p.Spawned && p.GetLord()?.LordJob is not LordJob_HH_GuestStay && Find.TickManager.TicksGame - r.ArrivedTick > 600) why = "guest in a foreign lord";
                if (why == null) continue;
                Caretaker.Fixed("Inn.GuestRecords", why + ": " + (p?.LabelShort ?? "#" + r.PawnId) + " (record removed)");
                if (!r.Settled) { r.Settled = true; }
                Unregister(r, "caretaker: " + why);
                fixes++;
            }
            // groups without members and long gone
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                var g = groups[i];
                if (records.Any(x => x.GroupId == g.Id)) continue;
                groups.RemoveAt(i);
            }
            // index drift
            if (guestIds.Count != records.Count(x => x.Pawn != null)) { RebuildIndex(); Caretaker.Fixed("Inn.GuestRecords", "guest index rebuilt"); fixes++; }
            return fixes;
        }

        public int CaretakerBeds()
        {
            int fixes = 0;
            foreach (var map in Find.Maps)
            {
                foreach (var b in GuestBeds.On(map))
                {
                    foreach (var o in b.OwnersForReading.ToList())
                    {
                        if (IsRegisteredGuest(o) && Get(o)?.Bed == b) continue;
                        o.ownership?.UnclaimBed();
                        if (b.OwnersForReading.Contains(o)) b.CompAssignableToPawn?.ForceRemovePawn(o);
                        Caretaker.Fixed("Inn.GuestBedOwners", $"{o.LabelShort} owned guest bed {b.ThingID} without a booking (released)");
                        fixes++;
                    }
                }
            }
            return fixes;
        }

        // ------------------------------------------------------------------ IGuestApiImpl
        public bool IsGuest(Pawn p) { var r = Get(p); return r != null && !r.Settled; }
        public GuestInfo Info(Pawn p) => Get(p);
        public IEnumerable<Pawn> GuestsOn(Map map)
        {
            var res = new List<Pawn>();
            if (map == null) return res;
            foreach (var r in records) if (r.MapId == map.uniqueID && !r.Settled && r.Pawn != null && r.Pawn.Spawned && r.Pawn.Map == map) res.Add(r.Pawn);
            return res;
        }
        public void AddSatisfaction(Pawn p, float delta, string reason)
        {
            var r = Get(p); if (r == null) return;
            r.ServiceSum = Mathf.Clamp(r.ServiceSum + delta, -1f, 1f);
            r.Log(reason);
            Recompute(r);
        }
        public void Spend(Pawn p, Money amount, string category)
        {
            var r = Get(p); if (r == null) return;
            r.Budget -= amount; if (r.Budget.Micro < 0) r.Budget = Money.Zero;
            r.Spent += amount;
            r.AddSpent(category, amount);
        }

        // ------------------------------------------------------------------ gizmos (shown through the lord)

        public IEnumerable<Gizmo> GizmosFor(Pawn p)
        {
            var r = Get(p);
            if (r == null || r.Settled || !Modules.On(ModuleId.Inn)) yield break;
            Texture2D icon;
            string label, desc;
            switch (r.Handling)
            {
                case HandlingMode.Accompany:
                    icon = InnTex.Handling_Accompany;
                    label = "HH.Inn.Handling.Accompany".TranslateOr("Accompany");
                    desc = "HH.Inn.Handling.AccompanyDesc".TranslateOr("Colonists with Hosting work keep this guest company: better mood, better ratings. Costs colonist time. Falls back to basics when nobody can host.");
                    break;
                case HandlingMode.Recruit:
                    icon = InnTex.Handling_Recruit;
                    label = "HH.Inn.Handling.Recruit".TranslateOr("Recruit");
                    desc = "HH.Inn.Handling.RecruitDesc".TranslateOr("Like Accompany, plus talks about staying. If the colony's bond with the guest is strong enough, they may join at departure. Pushy talks can offend.");
                    break;
                default:
                    icon = InnTex.Handling_Basics;
                    label = "HH.Inn.Handling.Basics".TranslateOr("Basics");
                    desc = "HH.Inn.Handling.BasicsDesc".TranslateOr("Paid services only, no entertainment. Costs no colonist time.");
                    break;
            }
            if (p.MapHeld != null && r.Handling == HandlingMode.Recruit)
            {
                int bond = Bond(p, p.MapHeld, out int thr);
                desc += "\n\n" + "HH.Inn.Handling.Bond".TranslateOr("Bond: {0} / {1}").Replace("{0}", bond.ToString()).Replace("{1}", thr.ToString());
            }
            yield return new Command_Action
            {
                defaultLabel = "HH.Inn.Handling.Label".TranslateOr("Handling: {0}").Replace("{0}", label),
                defaultDesc = desc + "\n\n" + "HH.Inn.Handling.Click".TranslateOr("Click to switch: Basics → Accompany → Recruit."),
                icon = icon,
                action = () =>
                {
                    var next = (HandlingMode)(((int)r.Handling + 1) % 3);
                    foreach (var m in Members(Group(r.GroupId) ?? new GuestGroup { Id = r.GroupId })) if (!m.Settled && (Find.Selector.IsSelected(m.Pawn) || m == r)) m.Handling = next;
                },
            };
            yield return new Command_Action
            {
                defaultLabel = "HH.Inn.SendAway.Label".TranslateOr("Send away"),
                defaultDesc = "HH.Inn.SendAway.Desc".TranslateOr("Ask this guest's whole group to leave now. Nights already paid are kept, no goodwill is gained, renown −1."),
                icon = InnTex.SendAway,
                action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "HH.Inn.SendAway.Confirm".TranslateOr("Send {0}'s group away?").Replace("{0}", p.LabelShort), () => SendAway(p), destructive: true)),
            };
        }

        public string ReportFor(Pawn p)
        {
            var r = Get(p); if (r == null) return null;
            if (r.Settled) return "HH.Inn.Report.Leaving".TranslateOr("Guest, heading home");
            string kind = r.Overnight ? "HH.Inn.Report.Overnight".TranslateOr("overnight guest") : "HH.Inn.Report.Day".TranslateOr("day visitor");
            string s = kind + " · " + "HH.Inn.Report.Budget".TranslateOr("budget {0}").Replace("{0}", r.Budget.WholeSilver.ToString())
                       + " · " + "HH.Inn.Report.Mood".TranslateOr("satisfaction {0}").Replace("{0}", ((r.Satisfaction + 1f) * 50f).ToString("0") + "%");
            if (!r.CheckedIn) s += " · " + "HH.Inn.Report.NotCheckedIn".TranslateOr("checking in");
            return s;
        }

        // ------------------------------------------------------------------ save
        List<string> gwK; List<int> gwV;
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            Scribe_Collections.Look(ref groups, "groups", LookMode.Deep);
            Scribe_Values.Look(ref nextGroupId, "nextGroupId", 1);
            Scribe_Values.Look(ref Renown, "renown", StartRenown);
            Scribe_Values.Look(ref Whispers, "whispers");
            Scribe_Deep.Look(ref Door, "doorPolicy");
            Scribe_Values.Look(ref GroupsHosted, "groupsHosted");
            Scribe_Values.Look(ref GuestsHosted, "guestsHosted");
            Scribe_Values.Look(ref ArrivalsAnnounced, "arrivalsAnnounced");
            Scribe_Values.Look(ref lastDriftDay, "lastDriftDay", -1);
            GuestRecord.MoneyLook(ref LifetimeIncome, "lifetimeIncome");
            Scribe_Collections.Look(ref goodwillByQuadrum, "goodwillByQuadrum", LookMode.Value, LookMode.Value, ref gwK, ref gwV);
            Scribe_Collections.Look(ref PinnedReturners, "pinnedReturners", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records = records ?? new List<GuestRecord>();
                groups = groups ?? new List<GuestGroup>();
                goodwillByQuadrum = goodwillByQuadrum ?? new Dictionary<string, int>();
                Door = Door ?? new DoorPolicy();
                PinnedReturners = PinnedReturners ?? new List<Pawn>(); PinnedReturners.RemoveAll(x => x == null);
                records.RemoveAll(x => x == null);
                groups.RemoveAll(x => x == null);
                Instance = this;
                RebuildIndex();
            }
        }

        public override void FinalizeInit() { Instance = this; RebuildIndex(); }

        // test helpers ---------------------------------------------------------------
        /// <summary>Tests only: makes the recruitment roll deterministic.</summary>
        internal static float? RecruitChanceOverride;
        /// <summary>Tests only: fixed bond threshold (colonists' base opinions vary with traits and ideologies).</summary>
        internal static int? BondThresholdOverride;
        /// <summary>Tests only: fixed insult chance (the setting is capped and the host's social skill grows with every talk).</summary>
        internal static float? InsultChanceOverride;
        internal void ResetGoodwillBookForTests() => goodwillByQuadrum.Clear();
        internal void ForceRemoveAllForTests() { foreach (var r in records.ToList()) Unregister(r, "test cleanup"); groups.Clear(); }
        internal void AddRawRecordForTests(GuestRecord r) { records.Add(r); if (r.Pawn != null) Index(r); }
    }

}
