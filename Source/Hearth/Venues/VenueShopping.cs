// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.ShopsFood
{
    /// <summary>Guest buying decisions (6.1 "Preise und Kaufentscheidung") shared by shops, taverns and vending machines.</summary>
    public static class VenueShopping
    {
        /// <summary>Price tolerance T(W) = 0.85 + 1.65 (W/100)^1.3, times the difficulty-dependent setting.</summary>
        public static float Tolerance(GuestInfo info)
        {
            float w = Mathf.Clamp(info?.Wealth ?? 30, 1, 100) / 100f;
            return (0.85f + 1.65f * Mathf.Pow(w, 1.3f)) * Math.Max(0.05f, VS.F(VS.PriceTolerance));
        }

        /// <summary>Fixed noise per (guest, business) of +-10 %.</summary>
        public static float Noise(Pawn guest, int businessId)
        {
            unchecked
            {
                int h = guest.thingIDNumber * 73856093 ^ businessId * 19349663;
                return ((h & 0x7fffffff) % 2001 - 1000) / 10000f;
            }
        }

        /// <summary>Rho = price multiplier / (tolerance * perceived quality). Above 1.6 the guest does not buy.</summary>
        public static float Rho(Pawn guest, GuestInfo info, float priceMultiplier, int businessId)
            => priceMultiplier / (Tolerance(info) * 1.0f * (1f + Noise(guest, businessId)));

        public static bool Accepts(float rho) => rho <= 1.6f;

        /// <summary>Satisfaction change for a purchase (points / 100, KONZEPT 6.1 price thoughts).</summary>
        public static float SatisfactionFor(float rho, out string reason)
        {
            if (rho < 0.6f) { reason = "HH.Venue.Bargain".TranslateOr("Bargain!"); return 0.03f; }
            if (rho < 0.9f) { reason = "HH.Venue.GoodValue".TranslateOr("Good value"); return 0.01f; }
            if (rho <= 1.1f) { reason = null; return 0f; }
            if (rho <= 1.3f) { reason = "HH.Venue.Pricey".TranslateOr("Pricey"); return -0.02f; }
            reason = "HH.Venue.RippedOff".TranslateOr("Ripped off"); return -0.05f;
        }

        static readonly List<Thing> scratch = new List<Thing>();

        /// <summary>Pick an item in a venue the guest can afford, accept and reach, and make a buy job for it.</summary>
        public static Job TryBuyJob(Pawn guest, GuestInfo info, Venue v, JobDef def)
        {
            var stock = VenueSales.Stock(v);
            if (stock.Count == 0) return null;
            float mult = VenueApi.PriceMultiplier(v);
            float rho = Rho(guest, info, mult, v.Sign.thingIDNumber);
            if (!Accepts(rho)) return null;
            bool drink = def == HHVenueDefOf.HH_BuyDrink;
            scratch.Clear(); scratch.AddRange(stock);
            scratch.Shuffle();
            int tries = 0;
            foreach (var item in scratch)
            {
                if (++tries > 14) break;
                if (!item.Spawned || item.Destroyed) continue;
                var unit = VenueApi.PriceOf(item, 1, v);
                if (unit.Micro <= 0 || unit > info.Budget) continue;
                int count = ChooseCount(item, unit, info.Budget, drink);
                if (!guest.CanReserveAndReach(item, PathEndMode.Touch, Danger.None, 1, count)) continue;
                var job = JobMaker.MakeJob(def, item, v.Sign);
                job.count = count;
                return job;
            }
            return null;
        }

        static int ChooseCount(Thing item, Money unit, Money budget, bool single)
        {
            if (single || item.stackCount <= 1) return 1;
            int afford = (int)Math.Floor(budget.Silver * 0.5 / Math.Max(0.01, unit.Silver));
            int max = Mathf.Clamp(afford, 1, Math.Min(item.stackCount, 10));
            return Rand.RangeInclusive(1, max);
        }

        public static Job TryVendingJob(Pawn guest, GuestInfo info)
        {
            var map = guest.Map; if (map == null) return null;
            float mult = Math.Max(0.01f, VS.F(VS.VendingPrice));
            var machines = VenueSales.VendingMachines(map).Where(VenueSales.MachineWorks).ToList();
            machines.Shuffle();
            foreach (var m in machines)
            {
                if (!Accepts(Rho(guest, info, mult, m.thingIDNumber))) continue;
                scratch.Clear(); scratch.AddRange(VenueSales.MachineStock(m)); scratch.Shuffle();
                foreach (var item in scratch)
                {
                    var unit = VenueSales.VendingPrice(item, 1);
                    if (unit.Micro <= 0 || unit > info.Budget) continue;
                    if (!guest.CanReserveAndReach(item, PathEndMode.Touch, Danger.None, 1, 1)) continue;
                    var job = JobMaker.MakeJob(HHVenueDefOf.HH_BuyGoods, item, m);
                    job.count = 1;
                    return job;
                }
            }
            return null;
        }

        public static IntVec3 WaitCell(Venue v)
        {
            var map = v.Map;
            var lounge = Venues.OfType(map, HHVenueDefOf.HH_Venue_Lounge).FirstOrDefault();
            if (lounge != null)
            {
                var cells = lounge.Room.Cells.Where(c => c.Standable(map)).ToList();
                if (cells.Count > 0) return cells.RandomElement();
            }
            var post = VenueApi.PostCell(v);
            if (CellFinder.TryFindRandomCellNear(v.Sign.Position, map, 3, c => c.Standable(map) && c != post && c.GetRoom(map) == v.Room, out var found)) return found;
            return v.Sign.Position;
        }

        public static Job WaitJob(Venue v, bool ringBell)
        {
            var job = JobMaker.MakeJob(HHVenueDefOf.HH_WaitAtVenue, v.Sign, WaitCell(v));
            job.count = ringBell ? 1 : 0;
            return job;
        }
    }

    /// <summary>A guest walks to an item, looks at it, pays and takes it home (6.8, N45).</summary>
    public class JobDriver_BuyGoods : JobDriver
    {
        Thing Item => job.targetA.Thing;
        bool Vending => job.targetB.Thing != null && job.targetB.Thing.def == HHVenueDefOf.HH_VendingMachine;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, Math.Max(1, job.count), null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Item == null || !Item.Spawned);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var browse = ToilMaker.MakeToil("Browse");
            browse.defaultCompleteMode = ToilCompleteMode.Delay;
            browse.defaultDuration = Math.Max(1, VS.I(VS.BrowseTicks));
            browse.handlingFacing = true;
            browse.tickAction = () => { if (Item != null) pawn.rotationTracker.FaceTarget(Item); };
            browse.socialMode = RandomSocialMode.Off;
            yield return browse;

            var pay = ToilMaker.MakeToil("Pay");
            pay.defaultCompleteMode = ToilCompleteMode.Instant;
            pay.initAction = DoPay;
            yield return pay;
        }

        void Fail(string why)
        {
            HLog.Debug("Venues", $"{pawn.LabelShort}: purchase failed ({why})");
            GuestMemory.SetNoService(pawn, 2500);
            EndJobWith(JobCondition.Incompletable);
        }

        void DoPay()
        {
            var item = Item;
            var map = pawn.Map;
            if (item == null || !item.Spawned || map == null) { Fail("item gone"); return; }
            var info = GuestApi.Info(pawn);
            if (info == null) { Fail("not a guest"); return; }
            int count = Math.Max(1, Math.Min(job.count, item.stackCount));
            bool vending = Vending;
            Venue venue = null;
            Money price;
            float mult;
            if (vending)
            {
                var m = job.targetB.Thing as Building;
                if (m == null || !m.Spawned || !VenueSales.MachineWorks(m)) { Fail("machine off"); return; }
                price = VenueSales.VendingPrice(item, count);
                mult = Math.Max(0.01f, VS.F(VS.VendingPrice));
            }
            else
            {
                venue = VenueApi.VenueOfSign(map, job.targetB.Thing);
                if (venue == null || !VenueApi.IsOpenForBusiness(venue)) { Fail("venue closed"); return; }
                price = VenueApi.PriceOf(item, count, venue);
                mult = VenueApi.PriceMultiplier(venue);
            }
            int business = job.targetB.Thing?.thingIDNumber ?? 0;
            float rho = VenueShopping.Rho(pawn, info, mult, business);
            if (!VenueShopping.Accepts(rho)) { Fail("too expensive"); return; }
            string label = item.LabelNoCount;
            string memo = (vending ? "Vending machine: " : (venue.DisplayNameOf() + ": ")) + label + (count > 1 ? " x" + count : "");
            if (!GuestApi.ChargeGuest(pawn, price, vending ? "vending" : "shop", memo)) { Fail("cannot pay"); return; }

            var taken = item.SplitOff(count);
            var tdef = taken.def;
            bool consumed = false;
            int added = pawn.inventory.innerContainer.TryAdd(taken, taken.stackCount, true);
            if (!taken.Destroyed && taken.holdingOwner == null && taken.stackCount > 0)
                GenPlace.TryPlaceThing(taken, pawn.Position, map, ThingPlaceMode.Near);
            Thing carried = null;
            foreach (var t in pawn.inventory.innerContainer) if (t.def == tdef) { carried = t; break; }

            // bookkeeping
            GuestMemory.AddPurchase(pawn);
            if (venue != null)
            {
                (venue.Comp as CompVenueSignHH)?.RecordSale(price, count);
                var staffer = venue.Staffer();
                if (staffer != null)
                {
                    staffer.skills?.Learn(SkillDefOf.Social, 25f, false);
                    // a friendly, skilled tender makes the purchase a little nicer (6.5 service quality, simplified)
                    int social = staffer.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5;
                    GuestApi.AddSatisfaction(pawn, 0.004f + 0.016f * Mathf.Clamp01(social / 20f), "HH.Venue.GoodService".TranslateOr("Friendly service"));
                }
            }
            float delta = VenueShopping.SatisfactionFor(rho, out string reason);
            if (delta != 0f) GuestApi.AddSatisfaction(pawn, delta, reason);
            MoteMaker.ThrowText(pawn.DrawPos, map, "-" + price.ToStringShort(), Color.yellow, 2.5f);

            // drinks are drunk on the spot; vending food when hungry
            bool isDrink = VenueSales.IsDrink(tdef);
            bool wantsNow = job.def == HHVenueDefOf.HH_BuyDrink || (vending && isDrink)
                            || (vending && tdef.ingestible != null && tdef.IsNutritionGivingIngestible && pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.7f);
            if (wantsNow && tdef.ingestible != null && carried != null && added > 0)
            {
                var ingest = JobMaker.MakeJob(JobDefOf.Ingest, carried);
                ingest.count = 1;
                pawn.jobs.jobQueue.EnqueueFirst(ingest);
                consumed = true;
            }
            if (job.def == HHVenueDefOf.HH_BuyDrink || (vending && isDrink)) GuestMemory.SetDrink(pawn);
            if (vending) GuestMemory.SetVending(pawn);
            HLog.Debug("Venues", $"{pawn.LabelShort} bought {label} x{count} for {price.ToStringShort()} (rho {rho:0.00}){(consumed ? ", consuming" : "")}");
        }
    }

    public static class VenueExt
    {
        public static string DisplayNameOf(this Venue v) => (v.Comp as CompVenueSign)?.DisplayName ?? "Venue";
        public static Pawn Staffer(this Venue v) => VenueApi.Staffer(v);
    }

    // ---------------------------------------------------------------- activities

    abstract class VenueActivityBase : IGuestActivity
    {
        public abstract string Id { get; }
        public ModuleId Module => ModuleId.ShopsFood;
        public abstract float Desire(Pawn guest, GuestInfo info);
        public abstract Job TryGiveJob(Pawn guest, GuestInfo info);

        protected static bool Basic(Pawn guest, GuestInfo info)
            => guest?.Map != null && info != null && guest.Awake() && !guest.Downed && !GuestMemory.InNoServiceCooldown(guest);
    }

    /// <summary>venues.shop: browse the shelves of an open shop and buy something.</summary>
    sealed class ShopActivity : VenueActivityBase
    {
        public override string Id => "venues.shop";
        public override float Desire(Pawn guest, GuestInfo info)
        {
            if (!Basic(guest, info) || GuestMemory.Purchases(guest) >= VS.I(VS.MaxPurchases)) return 0f;
            if (info.Budget.Silver < 3) return 0f;
            var all = Venues.All(guest.Map);
            bool any = false;
            for (int i = 0; i < all.Count && !any; i++)
            {
                var v = all[i];
                if (VenueApi.SellsOf(v) != SellKind.Goods || !VenueApi.IsOpenForBusiness(v)) continue;
                if (VenueSales.Stock(v).Count > 0) any = true;
            }
            if (!any) return 0f;
            float budget = Mathf.Clamp01((float)info.Budget.Silver / 80f);
            return VS.F(VS.ShopDesire) * (0.35f + 0.9f * budget);
        }
        public override Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            var open = Venues.All(guest.Map).Where(v => VenueApi.SellsOf(v) == SellKind.Goods && VenueApi.IsOpenForBusiness(v)).ToList();
            open.Shuffle();
            foreach (var v in open)
            {
                var job = VenueShopping.TryBuyJob(guest, info, v, HHVenueDefOf.HH_BuyGoods);
                if (job != null) return job;
            }
            return null;
        }
    }

    /// <summary>venues.drink: order a drink in a tavern and drink it there; waits or rings the night bell when nobody serves.</summary>
    sealed class DrinkActivity : VenueActivityBase
    {
        public override string Id => "venues.drink";
        static bool Candidate(Venue v) => VenueApi.SellsOf(v) == SellKind.Drinks && VenueSales.Stock(v).Count > 0;
        public override float Desire(Pawn guest, GuestInfo info)
        {
            if (!Basic(guest, info) || info.Budget.Silver < 3) return 0f;
            int gap = VS.I(VS.DrinkGapHours) * GenDate.TicksPerHour;
            if (Find.TickManager.TicksGame - GuestMemory.LastDrink(guest) < gap) return 0f;
            float best = 0f;
            var all = Venues.All(guest.Map);
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (!Candidate(v)) continue;
                switch (VenueApi.Gate(v))
                {
                    case VenueGate.Open: best = Math.Max(best, 0.9f); break;
                    case VenueGate.WaitingForStaff: best = Math.Max(best, 0.5f); break;
                    default: if (VS.B(VS.NightBell) && VenueApi.NeedsStaff(v)) best = Math.Max(best, 0.2f); break;
                }
            }
            return best * VS.F(VS.ShopDesire);
        }
        public override Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            var taverns = Venues.All(guest.Map).Where(Candidate).ToList();
            taverns.Shuffle();
            foreach (var v in taverns)
            {
                switch (VenueApi.Gate(v))
                {
                    case VenueGate.Open:
                        var job = VenueShopping.TryBuyJob(guest, info, v, HHVenueDefOf.HH_BuyDrink);
                        if (job != null) return job;
                        break;
                    case VenueGate.WaitingForStaff:
                        return VenueShopping.WaitJob(v, ringBell: !VenueApi.InHours(v));
                    default:
                        if (VS.B(VS.NightBell) && VenueApi.NeedsStaff(v)) return VenueShopping.WaitJob(v, ringBell: true);
                        break;
                }
            }
            return null;
        }
    }

    /// <summary>venues.vending: buy a snack or a drink from a powered vending machine, any hour, no staff.</summary>
    sealed class VendingActivity : VenueActivityBase
    {
        public override string Id => "venues.vending";
        public override float Desire(Pawn guest, GuestInfo info)
        {
            if (!Basic(guest, info) || info.Budget.Silver < 2) return 0f;
            if (Find.TickManager.TicksGame - GuestMemory.LastVending(guest) < 1500) return 0f;
            bool any = false;
            foreach (var m in VenueSales.VendingMachines(guest.Map))
                if (VenueSales.MachineWorks(m) && VenueSales.MachineStock(m).Any()) { any = true; break; }
            if (!any) return 0f;
            var food = guest.needs?.food;
            float hunger = food == null ? 0.5f : Mathf.Lerp(1f, 0.3f, food.CurLevelPercentage);
            return 0.6f * hunger * VS.F(VS.ShopDesire);
        }
        public override Job TryGiveJob(Pawn guest, GuestInfo info) => VenueShopping.TryVendingJob(guest, info);
    }

    /// <summary>
    /// Stand-in for the guest brain when the Inn module is not providing one (KONZEPT 5: without Inn, vanilla visitors act as guests).
    /// Every 90 ticks it offers idle visitors an activity. Silent as soon as GuestApi.Impl exists and the Inn module is on.
    /// </summary>
    public static class GuestFallback
    {
        /// <summary>Set to false if another module already drives vanilla visitors through GuestActivities.</summary>
        public static bool Enabled = true;
        static HashSet<JobDef> idleJobs;

        static bool Idle(Pawn p)
        {
            if (p.CurJob == null) return true;
            if (idleJobs == null) idleJobs = new HashSet<JobDef> { JobDefOf.Wait_Wander, JobDefOf.GotoWander, JobDefOf.Wait, JobDefOf.Wait_MaintainPosture };
            return idleJobs.Contains(p.CurJobDef) && !p.CurJob.playerForced;
        }

        public static void Run(Map map)
        {
            if (!Enabled || (GuestApi.Impl != null && Modules.On(ModuleId.Inn))) return;
            int now = Find.TickManager.TicksGame;
            foreach (var p in GuestApi.GuestsOn(map))
            {
                if (now < GuestMemory.NextOffer(p) || !Idle(p) || !p.Awake() || p.Downed) continue;
                if (!(p.GetLord()?.CurLordToil is LordToil_DefendPoint)) continue;
                var info = GuestApi.Info(p); if (info == null) continue;
                GuestMemory.SetNextOffer(p, now + Rand.RangeInclusive(240, 600));
                var job = GuestActivities.TryPick(p, info);
                if (job != null) p.jobs.StartJob(job, JobCondition.InterruptOptional);
            }
        }
    }
}
