// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth
{
    public enum StayKind { DayVisitor, Overnight, Traveler, Caravan, Lodger, Patient }
    public enum HandlingMode { Basics = 0, Accompany = 1, Recruit = 2 }

    /// <summary>Read-mostly view of one guest, shared by all modules (5.1). Owned and saved by the Inn module.</summary>
    public class GuestInfo
    {
        public Pawn Pawn;
        public StayKind Kind = StayKind.DayVisitor;
        public HandlingMode Handling = HandlingMode.Basics;
        /// <summary>Spending budget left for this stay.</summary>
        public Money Budget;
        public Money Spent;
        /// <summary>-1 … +1, starts at 0. Drives renown, goodwill, return visits.</summary>
        public float Satisfaction;
        public int ArrivedTick, PlannedLeaveTick;
        public string WingId;
        /// <summary>Wealth level 1–100 (5.3).</summary>
        public int Wealth = 30;
        public bool PaysDigital;
        public readonly List<string> Events = new List<string>();
    }

    /// <summary>Implemented by the Inn module. Until it is loaded/enabled, GuestApi falls back to vanilla visitors.</summary>
    public interface IGuestApiImpl
    {
        bool IsGuest(Pawn p);
        GuestInfo Info(Pawn p);
        IEnumerable<Pawn> GuestsOn(Map map);
        void AddSatisfaction(Pawn p, float delta, string reason);
        void Spend(Pawn p, Money amount, string category);
    }

    /// <summary>
    /// The one way other modules ask about guests (casino, shops, kitchen, UI, RimWard bridge).
    /// Without the Inn module, vanilla visitors count as guests with a small default budget, so venues still earn money.
    /// </summary>
    public static class GuestApi
    {
        static bool InnOn => Impl != null && Modules.On(ModuleId.Inn);
        public static IGuestApiImpl Impl;
        // fallback guests (no Inn module): keyed by thingIDNumber, which is only unique within one game
        static readonly Dictionary<int, GuestInfo> fallbackInfo = new Dictionary<int, GuestInfo>();
        static int lastPrune;

        static GuestApi() { HearthGame.OnGameInit += () => { fallbackInfo.Clear(); lastPrune = 0; }; }

        public static bool IsGuest(Pawn p)
        {
            if (p == null || p.Dead) return false;
            if (InnOn && p.Spawned && Impl.IsGuest(p)) return true;
            // Vanilla visitor groups (not booked by the Inn) still count as day visitors: they may use venues, shops, the casino …
            if (!p.Spawned) return false;
            return IsVanillaVisitor(p) || Registered(p) != null; // a downed visitor that was already a guest stays one
        }

        /// <summary>Only real visiting/trading groups count: not quest pawns (bestowers, military aid), beggars, travellers passing through.</summary>
        public static bool IsVanillaVisitor(Pawn p)
        {
            if (!(p.RaceProps.Humanlike && p.Faction != null && !p.Faction.IsPlayer && !p.HostileTo(Faction.OfPlayer)
                  && !p.IsPrisoner && !p.IsSlave && !p.Downed && p.Map != null && p.Map.IsPlayerHome)) return false;
            var job = p.GetLord()?.LordJob;
            return job is LordJob_VisitColony || job is LordJob_TradeWithColony;
        }

        static GuestInfo Registered(Pawn p) => fallbackInfo.TryGetValue(p.thingIDNumber, out var gi) && gi.Pawn == p ? gi : null;

        public static GuestInfo Info(Pawn p)
        {
            if (p == null) return null;
            if (InnOn) { var inn = Impl.Info(p); if (inn != null) return inn; }
            var known = Registered(p);
            if (known != null) return p.Dead || p.Destroyed ? null : known; // keeps its info while downed
            if (!IsVanillaVisitor(p)) return null;
            PruneFallback();
            var gi = new GuestInfo { Pawn = p, Kind = StayKind.DayVisitor, Budget = Money.FromSilver(60 + p.thingIDNumber % 90), ArrivedTick = Find.TickManager.TicksGame };
            fallbackInfo[p.thingIDNumber] = gi; // also replaces a stale entry of a different pawn with the same id
            return gi;
        }

        /// <summary>Drops entries of pawns that are gone (at most once per 2500 ticks).</summary>
        static void PruneFallback()
        {
            int now = Find.TickManager.TicksGame;
            if (now < lastPrune || now - lastPrune < 2500) { if (now < lastPrune) lastPrune = now; return; }
            lastPrune = now;
            List<int> dead = null;
            foreach (var kv in fallbackInfo)
            {
                var pw = kv.Value.Pawn;
                if (pw == null || pw.Destroyed || pw.Dead || !pw.Spawned) (dead ?? (dead = new List<int>())).Add(kv.Key);
            }
            if (dead != null) foreach (var k in dead) fallbackInfo.Remove(k);
        }

        public static IEnumerable<Pawn> GuestsOn(Map map)
        {
            if (map == null) return Enumerable.Empty<Pawn>();
            var vanilla = map.mapPawns.AllPawnsSpawned.Where(IsVanillaVisitor);
            if (InnOn) return Impl.GuestsOn(map).Concat(vanilla.Where(v => !Impl.IsGuest(v))).Distinct().ToList();
            return vanilla.ToList();
        }

        public static void AddSatisfaction(Pawn p, float delta, string reason)
        {
            if (InnOn && Impl.Info(p) != null) { Impl.AddSatisfaction(p, delta, reason); return; }
            var gi = Info(p); if (gi != null) { gi.Satisfaction = Math.Max(-1, Math.Min(1, gi.Satisfaction + delta)); gi.Events.Add(reason); }
        }

        /// <summary>Money left that the guest is willing to spend this stay.</summary>
        public static Money Budget(Pawn p) => Info(p)?.Budget ?? Money.Zero;

        /// <summary>Call after a successful Payments.TryCharge to reduce the remaining budget.</summary>
        public static void Spend(Pawn p, Money amount, string category)
        {
            if (InnOn && Impl.Info(p) != null) { Impl.Spend(p, amount, category); return; }
            var gi = Info(p); if (gi != null) { gi.Budget -= amount; gi.Spent += amount; }
        }

        /// <summary>
        /// Charge a guest for a service and book it as colony income in one step:
        /// checks budget, takes the money (cash, card …), reduces the budget, raises Paid.
        /// </summary>
        public static bool ChargeGuest(Pawn guest, Money price, string category, string memo)
        {
            if (!IsGuest(guest)) return false;
            if (Budget(guest) < price) return false;
            string till = Payments.ColonyTill(guest.MapHeld);
            if (!Payments.TryCharge(guest, price, till, memo, out _, category))
            {
                // Vanilla visitors without silver still pay "virtually" in fallback mode (they carry trade goods instead).
                if (InnOn && Impl.Info(guest) != null) return false; // booked guests must really pay
                Ledger.Current?.Post("world", till, price, memo + " (visitor)", false, category);
                Payments.RaisePaid(guest, price, till, memo, PaymentMethod.Barter);
            }
            Spend(guest, price, category);
            return true;
        }

        /// <summary>
        /// Pays money to a guest (winnings, refund, insurance claim …) out of the colony's till and books it in the ledger.
        /// A refund reduces the guest's Spent and gives the budget back. Returns false if nothing was paid.
        /// </summary>
        public static bool CreditGuest(Pawn guest, Money amount, string category, string memo)
        {
            if (guest == null || amount.Micro <= 0) return false;
            var map = guest.MapHeld; if (map == null) return false;
            // allowOverdraft: the original charge may already have left the till (banked or put down as silver)
            if (!Payments.TryPayout(guest, amount, Payments.ColonyTill(map), memo, category, true)) return false;
            var gi = Info(guest);
            if (gi != null) { gi.Spent = Money.Max(Money.Zero, gi.Spent - amount); gi.Budget += amount; }
            return true;
        }
    }

    /// <summary>
    /// Something a guest can do on the map (shop, eat, gamble, spa, cinema …). Registered by modules in OnDefsLoaded.
    /// The guest brain (Inn module) asks every registered activity for its desire and picks one by weighted chance.
    /// Without the Inn module, vanilla visitors are offered the same activities by a small think-node patch.
    /// </summary>
    public interface IGuestActivity
    {
        string Id { get; }
        ModuleId Module { get; }
        /// <summary>0 = not possible now; ~1 = normal; higher = strongly wanted. Must be cheap (called often).</summary>
        float Desire(Pawn guest, GuestInfo info);
        /// <summary>Return a job, or null if it turned out impossible (no free slot, closed …).</summary>
        Job TryGiveJob(Pawn guest, GuestInfo info);
    }

    public static class GuestActivities
    {
        public static readonly List<IGuestActivity> All = new List<IGuestActivity>();
        public static void Register(IGuestActivity a) { All.RemoveAll(x => x.Id == a.Id); All.Add(a); HLog.Debug("Core", "Guest activity registered: " + a.Id); }

        // reused between calls (the think tree runs on the main thread only)
        static readonly List<IGuestActivity> optA = new List<IGuestActivity>();
        static readonly List<float> optW = new List<float>();

        /// <summary>Weighted pick among activities of enabled modules; returns null if nothing is possible.</summary>
        public static Job TryPick(Pawn guest, GuestInfo info)
        {
            if (guest == null || info == null) return null;
            optA.Clear(); optW.Clear();
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var a = All[i];
                    if (a == null || !Modules.On(a.Module)) continue;
                    float w = Guard.Run(a.Module, "Activity.Desire." + a.Id, () => a.Desire(guest, info), 0f);
                    if (float.IsNaN(w) || float.IsInfinity(w) || w <= 0f) continue;
                    optA.Add(a); optW.Add(w);
                }
                if (optA.Count == 0) return null;
                var rng = JRand.Stream("guest.activity");
                for (int tries = 0; tries < 3 && optA.Count > 0; tries++)
                {
                    float sum = 0f;
                    for (int i = 0; i < optW.Count; i++) sum += optW[i];
                    float roll = (float)rng.NextDouble() * sum;
                    int pick = optA.Count - 1;
                    for (int i = 0; i < optA.Count; i++) { roll -= optW[i]; if (roll <= 0f) { pick = i; break; } }
                    var chosen = optA[pick];
                    var job = Guard.Run(chosen.Module, "Activity.Job." + chosen.Id, () => chosen.TryGiveJob(guest, info), null);
                    if (job != null) return job;
                    optA.RemoveAt(pick); optW.RemoveAt(pick);
                }
                return null;
            }
            finally { optA.Clear(); optW.Clear(); }
        }
    }
}
