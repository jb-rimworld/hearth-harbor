// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.City
{
    /// <summary>
    /// Personal security (KONZEPT 5.19): a wealthy guest hires an armed colonist who follows them around; the colony earns an hourly fee.
    /// A guest hurt while protected gets the fees back and is unhappy. A hire that finds nobody within a few hours is dropped without charge.
    /// </summary>
    public static class SecurityService
    {
        public const int HourTicks = 2500;

        public static bool Eligible(Pawn guest, GuestInfo gi)
        {
            if (!CityAccess.SecurityOn || gi == null || guest == null || !guest.Spawned || guest.Dead || guest.Downed) return false;
            return gi.Wealth >= CitySettings.Int(CitySettings.SecurityMinWealth);
        }

        /// <summary>Bodyguards are only offered on maps where the player runs a business: at least one venue sign or taxi stand.</summary>
        public static bool OptedIn(Map map) => Venues.All(map).Count > 0 || TaxiService.AnyStand(map);

        public static bool IsArmedGuard(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned || p.Downed || p.Drafted || !p.Awake()) return false;
            if (p.Faction != Faction.OfPlayer || p.WorkTagIsDisabled(WorkTags.Violent)) return false;
            if (p.equipment?.Primary == null) return false;
            if (HHWork.Hosting != null && (p.workSettings == null || p.WorkTypeIsDisabled(HHWork.Hosting) || !p.workSettings.WorkIsActive(HHWork.Hosting))) return false;
            return p.health.capacities.CapableOf(PawnCapacityDefOf.Moving) && p.health.capacities.CapableOf(PawnCapacityDefOf.Sight);
        }

        public static bool AnyGuardAvailable(Map map)
        {
            var list = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < list.Count; i++) if (IsArmedGuard(list[i])) return true;
            return false;
        }

        public static Money HourlyFee(GuestInfo gi)
        {
            double day = gi.Budget.Silver * CitySettings.F(CitySettings.SecurityDayShare);
            return Money.Max(Money.FromSilver(day / 24.0), CitySettings.Silver(CitySettings.SecurityMinHourFee));
        }

        /// <summary>Starts a hire (contract waiting for a guard). Returns null if there is no reason or no way to.</summary>
        public static BodyguardContract Hire(Pawn guest, bool ignoreChance = false)
        {
            var data = CityGameComponent.Instance; var gi = GuestApi.Info(guest);
            if (data == null || !Eligible(guest, gi) || !GuestApi.IsGuest(guest)) return null;
            if (data.ContractFor(guest) != null) return null;
            if (!AnyGuardAvailable(guest.Map)) return null;
            var fee = HourlyFee(gi);
            if (gi.Budget < fee * 3) return null; // cannot afford even a short contract
            if (!ignoreChance)
            {
                if (data.TicksSince(guest, "securityRoll") < 60000) return null;
                data.MarkVisit(guest, "securityRoll");
                double p = CitySettings.F(CitySettings.SecurityChance) + (gi.Wealth >= 80 ? 0.4 : 0.0);
                if (!JRand.Stream("city.security").Chance(Math.Min(1.0, p))) return null;
            }
            var c = new BodyguardContract { Guest = guest, RequestTick = Find.TickManager.TicksGame, HourlyMicro = fee.Micro };
            data.Contracts.Add(c);
            HLog.Debug("City", $"{guest} wants a bodyguard at {fee} per hour");
            return c;
        }

        public static void End(BodyguardContract c, string reason)
        {
            if (c.Ended) return;
            c.Ended = true; c.EndReason = reason;
            HLog.Debug("City", $"Bodyguard contract of {c.Guest} ended: {reason} ({c.Hours} h, {c.Paid} paid)");
            var j = c.Guard?.CurJob;
            if (j != null && j.def == CityDefs.Bodyguard && c.Guard.jobs?.curDriver != null) c.Guard.jobs.EndCurrentJob(JobCondition.Succeeded);
        }

        /// <summary>Refunds what the guest paid when they were hurt despite protection; the incident counts double.</summary>
        static void Refund(BodyguardContract c)
        {
            if (c.Refunded) return;
            c.Refunded = true;
            var guest = c.Guest; var paid = c.Paid;
            if (paid.Micro > 0 && guest?.MapHeld != null)
            {
                Ledger.Current?.Post(Payments.ColonyTill(guest.MapHeld), "world", paid, "Bodyguard refund (guest hurt)", true);
                var gi = GuestApi.Info(guest); if (gi != null) { gi.Budget += paid; gi.Spent -= paid; }
            }
            if (guest != null) GuestApi.AddSatisfaction(guest, -0.3f, "Hurt despite a bodyguard");
            CityGameComponent.Instance?.Record("securityRefund", paid);
            End(c, "guest hurt");
        }

        static bool HurtSince(Pawn guest, int sinceTick)
        {
            int now = Find.TickManager.TicksGame;
            var hs = guest.health.hediffSet.hediffs;
            for (int i = 0; i < hs.Count; i++)
                if (hs[i] is Hediff_Injury && now - hs[i].ageTicks >= 0 && hs[i].ageTicks <= now - sinceTick) return true;
            return false;
        }

        /// <summary>Every 250 ticks: roll new hires, bill running contracts, close finished ones.</summary>
        public static void Tick()
        {
            var data = CityGameComponent.Instance; if (data == null) return;
            if (!CityAccess.SecurityOn) { foreach (var c in data.Contracts.Where(x => !x.Ended).ToList()) End(c, "service switched off"); return; }
            int now = Find.TickManager.TicksGame;

            foreach (var map in Find.Maps)
            {
                // N08: nothing happens on its own until the player has opted into hosting (a venue sign or a taxi stand on the map)
                if (!map.IsPlayerHome || !OptedIn(map) || !AnyGuardAvailable(map)) continue;
                foreach (var g in GuestApi.GuestsOn(map).ToList())
                {
                    var gi = GuestApi.Info(g);
                    if (Eligible(g, gi)) Hire(g);
                }
            }

            foreach (var c in data.Contracts.Where(x => !x.Ended).ToList())
            {
                var g = c.Guest;
                if (g == null || g.Dead || !g.Spawned || !GuestApi.IsGuest(g)) { End(c, "guest gone"); continue; }
                if (!c.Started)
                {
                    if (now - c.RequestTick > CitySettings.Int(CitySettings.SecurityHireHours) * HourTicks) End(c, "no guard found");
                    continue;
                }
                if (c.Guard != null && (c.Guard.Dead || !c.Guard.Spawned || c.Guard.Downed)) c.Guard = null;
                if (!c.Refunded && HurtSince(g, c.StartTick)) { Refund(c); continue; }
                bool guarding = c.Guard != null && c.Guard.CurJob != null && c.Guard.CurJob.def == CityDefs.Bodyguard && c.Guard.CurJob.targetA.Thing == g;
                if (!guarding) { c.LastBillTick = Math.Max(c.LastBillTick, now - HourTicks + 1); if (now - c.LastBillTick > 4 * HourTicks) End(c, "guard lost"); continue; }
                while (now - c.LastBillTick >= HourTicks)
                {
                    if (!GuestApi.ChargeGuest(g, c.Hourly, "security", "Bodyguard, 1 hour")) { End(c, "guest out of money"); break; }
                    c.LastBillTick += HourTicks; c.Hours++; c.PaidMicro += c.HourlyMicro;
                    data.Record("security", c.Hourly);
                    GuestApi.AddSatisfaction(g, 0.01f, "Bodyguard: feels safe");
                }
            }
            if (now % 60000 < 250) data.Prune();
        }
    }

    /// <summary>Scans for guests with an open or own contract so armed colonists with the hosting work type take the job.</summary>
    public class WorkGiver_HHBodyguard : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Nothing);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!CityAccess.SecurityOn) return true;
            var data = CityGameComponent.Instance;
            return data == null || data.Contracts.Count == 0 || !SecurityService.IsArmedGuard(pawn);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var data = CityGameComponent.Instance; if (data == null) yield break;
            foreach (var c in data.Contracts)
                if (!c.Ended && c.Guest != null && c.Guest.Spawned && c.Guest.Map == pawn.Map && (c.Guard == null || c.Guard == pawn)) yield return c.Guest;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var guest = t as Pawn; var data = CityGameComponent.Instance;
            var c = data?.ContractFor(guest);
            if (c == null || (c.Guard != null && c.Guard != pawn)) return false;
            if (data.ContractOf(pawn) != null && data.ContractOf(pawn) != c) return false;
            return pawn.CanReach(guest, PathEndMode.Touch, Danger.Deadly);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(CityDefs.Bodyguard, t);
    }

    /// <summary>Follow the protected guest and stay close; the colony is paid by the hour (SecurityService.Tick).</summary>
    public class JobDriver_HHBodyguard : JobDriver
    {
        Pawn Guest => job.targetA.Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            var begin = Toils.Make("TakeContract");
            begin.initAction = () =>
            {
                var data = CityGameComponent.Instance; var c = data?.ContractFor(Guest);
                if (c == null || (c.Guard != null && c.Guard != pawn)) { EndJobWith(JobCondition.Incompletable); return; }
                c.Guard = pawn;
                if (!c.Started) { c.StartTick = Find.TickManager.TicksGame; c.LastBillTick = c.StartTick; HLog.Debug("City", pawn + " starts guarding " + Guest); }
            };
            yield return begin;

            var follow = Toils.Make("Follow");
            follow.defaultCompleteMode = ToilCompleteMode.Never;
            Toils.OnTick(follow, delta =>
            {
                var g = Guest; var c = CityGameComponent.Instance?.ContractFor(g);
                if (g == null || !g.Spawned || c == null || c.Ended) { EndJobWith(JobCondition.Succeeded); return; }
                if (!Toils.HashTick(pawn, 20, delta)) return;
                bool near = pawn.Position.InHorDistOf(g.Position, 4f) && GenSight.LineOfSight(pawn.Position, g.Position, pawn.Map);
                if (near) { if (pawn.pather.Moving) pawn.pather.StopDead(); pawn.rotationTracker.FaceCell(g.Position); return; }
                if (!pawn.CanReach(g, PathEndMode.Touch, Danger.Deadly)) { EndJobWith(JobCondition.Incompletable); return; }
                if (!pawn.pather.Moving || pawn.pather.Destination.Thing != g) pawn.pather.StartPath(g, PathEndMode.Touch);
            });
            yield return follow;
        }

        public override bool IsContinuation(Job j) => job.GetTarget(TargetIndex.A) == j.GetTarget(TargetIndex.A);
    }
}
