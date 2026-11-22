// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.City
{
    public class CompProperties_TaxiStand : CompProperties
    {
        public CompProperties_TaxiStand() { compClass = typeof(CompTaxiStand); }
    }

    /// <summary>Per-stand state: a stand serves one cab at a time. Saved (small).</summary>
    public class CompTaxiStand : ThingComp
    {
        public int BusyUntil, Departures, Arrivals;
        public long TakingsMicro;

        public bool CabFree => Find.TickManager.TicksGame >= BusyUntil;
        public Money Takings => new Money(TakingsMicro);

        public override string CompInspectStringExtra()
        {
            if (!CityAccess.ModuleOn) return null;
            string state = CabFree ? "HH.City.TaxiFree".TranslateOr("A cab is waiting.") : "HH.City.TaxiBusy".TranslateOr("The cab is out on a ride.");
            return state + " " + ("HH.City.TaxiStats".TranslateOr("Departures: {0}, arrivals: {1}, takings: {2} silver"))
                .Replace("{0}", Departures.ToString()).Replace("{1}", Arrivals.ToString()).Replace("{2}", Takings.ToStringShort());
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref BusyUntil, "busyUntil");
            Scribe_Values.Look(ref Departures, "departures");
            Scribe_Values.Look(ref Arrivals, "arrivals");
            Scribe_Values.Look(ref TakingsMicro, "takings");
        }
    }

    public class Building_TaxiStand : Building
    {
        public CompTaxiStand Taxi => GetComp<CompTaxiStand>();

        /// <summary>A standable, reachable cell next to the stand where a guest waits for the cab.</summary>
        public IntVec3 WaitCellFor(Pawn p)
        {
            IntVec3 best = IntVec3.Invalid; float bestD = float.MaxValue;
            foreach (var c in GenAdj.CellsAdjacent8Way(this))
            {
                if (!c.InBounds(Map) || !c.Standable(Map) || c.IsForbidden(p)) continue;
                float d = c.DistanceToSquared(p.Position);
                if (d < bestD && p.CanReach(c, PathEndMode.OnCell, Danger.None)) { best = c; bestD = d; }
            }
            return best;
        }

        public bool UnderOpenSky => !this.OccupiedRect().Cells.Any(c => c.Roofed(Map));
    }

    /// <summary>
    /// The taxi service (KONZEPT 5.19): departing guests may hail a cab at a stand and pay the fare; arriving guests may be dropped off
    /// at the stand. Cheap scan every 60 ticks, only while a stand exists. The cab is a skyfaller visual (pod-like), not a vehicle.
    /// </summary>
    public static class TaxiService
    {
        static readonly Money FareReference = Money.FromSilver(15);

        public static IEnumerable<Building_TaxiStand> Stands(Map map)
        {
            var def = CityDefs.TaxiStand; if (def == null || map == null) yield break;
            foreach (var t in map.listerThings.ThingsOfDef(def))
            {
                if (t is Building_TaxiStand s && s.Spawned && s.Faction == Faction.OfPlayer && s.UnderOpenSky) yield return s;
            }
        }

        public static bool AnyStand(Map map)
        {
            var def = CityDefs.TaxiStand; if (def == null || map == null) return false;
            return map.listerThings.ThingsOfDef(def).Count > 0;
        }

        public static Building_TaxiStand FreeStandFor(Pawn guest)
        {
            return Stands(guest.Map).Where(s => s.Taxi != null && s.Taxi.CabFree && guest.CanReserve(s) && s.WaitCellFor(guest).IsValid)
                .OrderBy(s => s.Position.DistanceToSquared(guest.Position)).FirstOrDefault();
        }

        public static bool IsDeparting(Pawn p)
        {
            var gi = GuestApi.Info(p);
            int now = Find.TickManager.TicksGame;
            if (gi != null && gi.PlannedLeaveTick > 0 && now >= gi.PlannedLeaveTick - 300) return true;
            var toil = p.GetLord()?.CurLordToil;
            return toil != null && toil.GetType().Name.StartsWith("LordToil_ExitMap");
        }

        static bool Eligible(Pawn guest)
        {
            if (guest == null || !guest.Spawned || guest.Dead || guest.Downed || guest.InMentalState || guest.IsPrisoner || guest.Map == null) return false;
            if (guest.carryTracker?.CarriedThing != null) return false;
            return GuestApi.IsGuest(guest);
        }

        static double Chance(string key, GuestInfo gi)
            => Math.Min(1.0, CitySettings.F(key) * (0.6 + 0.8 * Math.Max(0, Math.Min(100, gi?.Wealth ?? 30)) / 100.0));

        /// <summary>Offers a cab to a departing guest. <paramref name="ignoreChance"/> is for tests and the dev menu.</summary>
        public static bool TryOfferDeparture(Pawn guest, bool ignoreChance = false)
        {
            if (!CityAccess.TaxiOn) return false;
            if (!Eligible(guest)) { if (ignoreChance) HLog.Debug("City", "Taxi: " + guest + " is not eligible"); return false; }
            // the decision (and the once-per-stay roll) only happens when a cab is really free
            var stand = FreeStandFor(guest);
            if (stand == null) { if (ignoreChance) HLog.Debug("City", "Taxi: no free stand for " + guest); return false; }
            var gi = GuestApi.Info(guest);
            var fare = CitySettings.Silver(CitySettings.TaxiFare);
            if (GuestApi.Budget(guest) < fare) { if (ignoreChance) HLog.Debug("City", $"Taxi: {guest} cannot afford {fare}"); return false; }
            var data = CityGameComponent.Instance;
            if (!ignoreChance && data != null)
            {
                if (data.TicksSince(guest, "taxiRoll") < 60000) return false;
                data.MarkVisit(guest, "taxiRoll");
                if (!JRand.Stream("city.taxi").Chance(Chance(CitySettings.TaxiDepartureChance, gi))) { HLog.Debug("City", "Taxi: " + guest + " prefers to walk"); return false; }
            }
            var cell = stand.WaitCellFor(guest);
            var job = JobMaker.MakeJob(CityDefs.TaxiDeparture, stand, cell);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            HLog.Debug("City", $"{guest} heads for the taxi stand {stand}");
            return true;
        }

        /// <summary>Drops an arriving guest at a stand by cab: charges the landing fee, then the cab (with the guest inside) lands.</summary>
        public static bool TryArriveByTaxi(Pawn guest, bool ignoreChance = false)
        {
            if (!CityAccess.TaxiOn || CityDefs.TaxiIncoming == null) return false;
            if (!Eligible(guest)) { if (ignoreChance) HLog.Debug("City", "Taxi: " + guest + " is not eligible to arrive by cab"); return false; }
            var stand = FreeStandFor(guest);
            if (stand == null) { if (ignoreChance) HLog.Debug("City", "Taxi: no free stand to land " + guest); return false; }
            var data = CityGameComponent.Instance;
            var gi = GuestApi.Info(guest);
            if (!ignoreChance && data != null)
            {
                if (data.TicksSince(guest, "taxiArrive") < 120000) return false;
                data.MarkVisit(guest, "taxiArrive");
                if (!JRand.Stream("city.taxi").Chance(Chance(CitySettings.TaxiArrivalChance, gi))) return false;
            }
            var fee = CitySettings.Silver(CitySettings.TaxiArrivalFee);
            if (fee.Micro > 0 && !GuestApi.ChargeGuest(guest, fee, "taxi", "Taxi landing fee")) { HLog.Debug("City", $"Taxi: {guest} cannot pay the landing fee {fee}"); return false; }
            var map = guest.Map; var pos = stand.Position;
            stand.Taxi.BusyUntil = Find.TickManager.TicksGame + 400;
            stand.Taxi.Arrivals++; stand.Taxi.TakingsMicro += fee.Micro;
            data?.Record("taxiArrival", fee);
            GuestApi.AddSatisfaction(guest, 3 * CityPricing.PointsToSatisfaction, "Taxi: arrived in style");
            guest.DeSpawn();
            SkyfallerMaker.SpawnSkyfaller(CityDefs.TaxiIncoming, guest, pos, map);
            HLog.Debug("City", $"{guest} arrives by taxi at {stand}");
            return true;
        }

        /// <summary>Called every 60 ticks by the module.</summary>
        public static void Tick()
        {
            if (!CityAccess.TaxiOn) return;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome || !AnyStand(map)) continue;
                var guests = GuestApi.GuestsOn(map).ToList();
                int now = Find.TickManager.TicksGame;
                foreach (var g in guests)
                {
                    if (!Eligible(g)) continue;
                    if (g.CurJob != null && g.CurJob.def == CityDefs.TaxiDeparture) continue;
                    if (IsDeparting(g)) { TryOfferDeparture(g); continue; }
                    var gi = GuestApi.Info(g);
                    if (gi != null && now - gi.ArrivedTick < 400 && g.Position.CloseToEdge(map, 10)) TryArriveByTaxi(g);
                }
            }
        }
    }

    /// <summary>The departing guest walks to the stand, pays, waits for the cab and is gone.</summary>
    public class JobDriver_HHTaxiDeparture : JobDriver
    {
        Building_TaxiStand Stand => job.targetA.Thing as Building_TaxiStand;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Stand == null || Stand.Taxi == null || !Stand.UnderOpenSky);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            var fare = CitySettings.Silver(CitySettings.TaxiFare);
            var pay = Toils.Make("PayFare");
            pay.initAction = () =>
            {
                var stand = Stand;
                if (!GuestApi.ChargeGuest(pawn, fare, "taxi", "Taxi fare"))
                {
                    GuestApi.AddSatisfaction(pawn, -2 * CityPricing.PointsToSatisfaction, "Taxi: could not pay the fare");
                    EndJobWith(JobCondition.Incompletable); return;
                }
                stand.Taxi.BusyUntil = Find.TickManager.TicksGame + CitySettings.Int(CitySettings.TaxiWaitTicks) + 300;
                stand.Taxi.Departures++; stand.Taxi.TakingsMicro += fare.Micro;
                CityGameComponent.Instance?.Record("taxi", fare);
                CityPricing.ApplyPriceThought(pawn, GuestApi.Info(pawn), fare, FareReferenceValue, "Taxi");
            };
            yield return pay;

            var wait = Toils_General.Wait(CitySettings.Int(CitySettings.TaxiWaitTicks), TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            var lift = Toils.Make("Lift");
            lift.initAction = () =>
            {
                var stand = Stand; var map = pawn.Map; var pos = stand.Position;
                GuestApi.AddSatisfaction(pawn, 4 * CityPricing.PointsToSatisfaction, "Taxi: quick departure");
                if (CityDefs.TaxiLeaving != null) SkyfallerMaker.SpawnSkyfaller(CityDefs.TaxiLeaving, pos, map);
                HLog.Debug("City", pawn + " leaves by taxi");
                pawn.ExitMap(false, Rot4.Invalid);
            };
            lift.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return lift;
        }

        static Money FareReferenceValue => Money.FromSilver(15);
    }
}
