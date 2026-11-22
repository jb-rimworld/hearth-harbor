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
        public static IGuestApiImpl Impl;
        static readonly Dictionary<int, GuestInfo> fallbackInfo = new Dictionary<int, GuestInfo>();

        public static bool IsGuest(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead) return false;
            if (Impl != null && Modules.On(ModuleId.Inn)) return Impl.IsGuest(p);
            return IsVanillaVisitor(p);
        }

        public static bool IsVanillaVisitor(Pawn p)
            => p.RaceProps.Humanlike && p.Faction != null && !p.Faction.IsPlayer && !p.HostileTo(Faction.OfPlayer)
               && !p.IsPrisoner && !p.IsSlave && p.GetLord() != null && !p.Downed && p.Map != null && p.Map.IsPlayerHome;

        public static GuestInfo Info(Pawn p)
        {
            if (p == null) return null;
            if (Impl != null && Modules.On(ModuleId.Inn)) return Impl.Info(p);
            if (!IsVanillaVisitor(p)) return null;
            if (!fallbackInfo.TryGetValue(p.thingIDNumber, out var gi))
            {
                gi = new GuestInfo { Pawn = p, Kind = StayKind.DayVisitor, Budget = Money.FromSilver(60 + p.thingIDNumber % 90), ArrivedTick = Find.TickManager.TicksGame };
                fallbackInfo[p.thingIDNumber] = gi;
            }
            return gi;
        }

        public static IEnumerable<Pawn> GuestsOn(Map map)
        {
            if (map == null) return Enumerable.Empty<Pawn>();
            if (Impl != null && Modules.On(ModuleId.Inn)) return Impl.GuestsOn(map);
            return map.mapPawns.AllPawnsSpawned.Where(IsVanillaVisitor).ToList();
        }

        public static void AddSatisfaction(Pawn p, float delta, string reason)
        {
            if (Impl != null && Modules.On(ModuleId.Inn)) { Impl.AddSatisfaction(p, delta, reason); return; }
            var gi = Info(p); if (gi != null) { gi.Satisfaction = Math.Max(-1, Math.Min(1, gi.Satisfaction + delta)); gi.Events.Add(reason); }
        }

        /// <summary>Money left that the guest is willing to spend this stay.</summary>
        public static Money Budget(Pawn p) => Info(p)?.Budget ?? Money.Zero;

        /// <summary>Call after a successful Payments.TryCharge to reduce the remaining budget.</summary>
        public static void Spend(Pawn p, Money amount, string category)
        {
            if (Impl != null && Modules.On(ModuleId.Inn)) { Impl.Spend(p, amount, category); return; }
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
            if (!Payments.TryCharge(guest, price, Payments.ColonyTill(guest.MapHeld), memo, out _))
            {
                // Vanilla visitors without silver still pay "virtually" in fallback mode (they carry trade goods instead).
                if (Impl != null && Modules.On(ModuleId.Inn)) return false;
                Ledger.Current?.Post("world", Payments.ColonyTill(guest.MapHeld), price, memo + " (visitor)");
            }
            Spend(guest, price, category);
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

        /// <summary>Weighted pick among activities of enabled modules; returns null if nothing is possible.</summary>
        public static Job TryPick(Pawn guest, GuestInfo info)
        {
            var options = new List<(IGuestActivity a, float w)>();
            foreach (var a in All)
            {
                if (!Modules.On(a.Module)) continue;
                float w = Guard.Run(a.Module, "Activity.Desire." + a.Id, () => a.Desire(guest, info), 0f);
                if (w > 0) options.Add((a, w));
            }
            for (int tries = 0; tries < 3 && options.Count > 0; tries++)
            {
                float sum = options.Sum(o => o.w), roll = Rand.Value * sum;
                var pick = options[options.Count - 1];
                foreach (var o in options) { roll -= o.w; if (roll <= 0) { pick = o; break; } }
                var job = Guard.Run(pick.a.Module, "Activity.Job." + pick.a.Id, () => pick.a.TryGiveJob(guest, info), null);
                if (job != null) return job;
                options.Remove(pick);
            }
            return null;
        }
    }
}
