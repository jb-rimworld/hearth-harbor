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

        /// <summary>Rho = price multiplier / (tolerance * perceived quality). Above 1.6 the guest does not buy.
        /// The guest's xenotype shifts the tolerance (baseliners -5 %), the seller's price feel (penny-pincher +0.1) makes prices feel steeper.</summary>
        public static float Rho(Pawn guest, GuestInfo info, float priceMultiplier, int businessId, Pawn seller = null)
        {
            float tol = Tolerance(info) * Math.Max(0.2f, 1f + Hearth.Dlc.XenotypeHospitality.PriceToleranceOffset(guest));
            float feel = seller == null ? 1f : Math.Max(0.2f, 1f + Hearth.Dlc.DlcApi.PriceFeelOffset(seller));
            return priceMultiplier * feel / (tol * (1f + Noise(guest, businessId)));
        }

        /// <summary>Share of the budget a guest spends on one purchase: xenotype spend factor (hussars 1.15) x the guest's generosity stat.</summary>
        public static float SpendFactor(Pawn guest)
            => Mathf.Clamp(Hearth.Dlc.XenotypeHospitality.SpendFactor(guest) * Hearth.Dlc.DlcApi.TipFactor(guest), 0.5f, 2f);

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
            float rho = Rho(guest, info, mult, v.Sign.thingIDNumber, v.Staffer());
            if (!Accepts(rho)) return null;
            bool drink = def == HHVenueDefOf.HH_BuyDrink;
            // a skilled tender sells a little more (F14)
            var tender = v.Staffer();
            float serviceSpend = tender != null ? VenueService.SpendFactor(VenueService.Quality(tender)) : 1f;
            scratch.Clear(); scratch.AddRange(stock);
            scratch.Shuffle();
            // xenotype interests (11.5): favourites first (hemogen packs for sanguophages), rejected goods never
            if (ModsConfig.BiotechActive) SortByInterest(guest, scratch);
            int tries = 0;
            foreach (var item in scratch)
            {
                if (++tries > 14) break;
                if (!item.Spawned || item.Destroyed) continue;
                var unit = VenueApi.PriceOf(item, 1, v);
                if (unit.Micro <= 0 || unit > info.Budget) continue;
                int count = ChooseCount(item, unit, info.Budget, drink, SpendFactor(guest) * serviceSpend);
                if (!guest.CanReserveAndReach(item, PathEndMode.Touch, Danger.None, 1, count)) continue;
                var job = JobMaker.MakeJob(def, item, v.Sign);
                job.count = count;
                return job;
            }
            return null;
        }

        /// <summary>Stable sort by xenotype interest weight, highest first; weight 0 (rejected) is removed.</summary>
        public static void SortByInterest(Pawn guest, List<Thing> items)
        {
            if (Hearth.Dlc.XenotypeHospitality.For(guest) == null) return;
            var weighted = new List<(Thing t, float w, int i)>(items.Count);
            for (int i = 0; i < items.Count; i++) { float w = Hearth.Dlc.XenotypeHospitality.InterestWeight(guest, items[i].def); if (w > 0f) weighted.Add((items[i], w, i)); }
            weighted.Sort((a, b) => a.w != b.w ? b.w.CompareTo(a.w) : a.i.CompareTo(b.i));
            items.Clear(); foreach (var x in weighted) items.Add(x.t);
        }

        static int ChooseCount(Thing item, Money unit, Money budget, bool single, float spend = 1f)
        {
            if (single || item.stackCount <= 1) return 1;
            int afford = (int)Math.Floor(budget.Silver * Math.Min(0.9, 0.5 * spend) / Math.Max(0.01, unit.Silver));
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
            browse.defaultDuration = BrowseDuration();
            browse.handlingFacing = true;
            browse.tickAction = () => { if (Item != null) pawn.rotationTracker.FaceTarget(Item); };
            browse.socialMode = RandomSocialMode.Off;
            yield return browse;

            var pay = ToilMaker.MakeToil("Pay");
            pay.defaultCompleteMode = ToilCompleteMode.Instant;
            pay.initAction = DoPay;
            yield return pay;
        }

        /// <summary>Browsing time; short during last orders (the guest knows the shop is about to close).</summary>
        int BrowseDuration()
        {
            int ticks = Math.Max(1, VS.I(VS.BrowseTicks));
            var map = pawn.Map;
            var sign = job.targetB.Thing;
            if (map != null && sign != null && !Vending)
            {
                var v = VenueApi.VenueOfSign(map, sign);
                if (v != null && VenueApi.InLastOrders(v)) ticks = Math.Max(1, ticks / 3);
                var tender = v?.Staffer();
                if (tender != null) ticks = Math.Max(1, Mathf.RoundToInt(ticks * VenueService.BrowseFactor(VenueService.Quality(tender))));
            }
            return ticks;
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
                if (venue == null || !VenueApi.CanCompletePurchase(venue)) { Fail("venue closed"); return; }
                price = VenueApi.PriceOf(item, count, venue);
                mult = VenueApi.PriceMultiplier(venue);
            }
            int business = job.targetB.Thing?.thingIDNumber ?? 0;
            float rho = VenueShopping.Rho(pawn, info, mult, business, venue?.Staffer());
            if (!VenueShopping.Accepts(rho)) { Fail("too expensive"); return; }
            string label = item.LabelNoCount;
            string memo = (vending ? "Vending machine: " : (venue.DisplayNameOf() + ": ")) + label + (count > 1 ? " x" + count : "");
            // income shows drinks apart from goods: "drink" (taverns, bars), "vending" (machines), "shop" (everything else)
            string incomeCat = vending ? "vending" : VenueSales.IsDrink(item.def) ? "drink" : "shop";
            // honor system (F15): unattended, the guest decides what goes into the box; only that is charged
            bool honor = !vending && VenueHonor.Applies(venue);
            HonorOutcome honorOutcome = HonorOutcome.PaidFull; bool watched = false;
            Money listPrice = price;
            if (honor)
            {
                if (price > info.Budget) { Fail("cannot pay"); return; }
                honorOutcome = VenueHonor.Resolve(pawn, venue, price, out price, out watched);
                if (honorOutcome != HonorOutcome.PaidFull) memo += " (" + "HH.Honor.MemoPaid".TranslateOr("honor box: paid {0} of {1}").Replace("{0}", price.ToStringShort()).Replace("{1}", listPrice.ToStringShort()) + ")";
            }
            // an honor-box guest who paid nothing is not charged at all (no zero booking); every other sale is charged as before
            if ((price.IsPositive || !honor) && !GuestApi.ChargeGuest(pawn, price, incomeCat, memo)) { Fail("cannot pay"); return; }

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
                var signComp = venue.Comp as CompVenueSignHH;
                signComp?.RecordSale(price, count);
                if (honor)
                {
                    signComp?.RecordHonor(honorOutcome, listPrice, price);
                    VenueHonor.Feedback(pawn, venue, honorOutcome, listPrice, price, watched);
                }
                var staffer = venue.Staffer();
                if (staffer != null)
                {
                    // a friendly, skilled tender makes the purchase nicer and may earn a tip (F14: Social drives the service)
                    float q = VenueService.OnServedSale(staffer, pawn);
                    VenueService.TryTip(staffer, pawn, venue, price, incomeCat, q);
                }
            }
            // a guest who paid nothing or too little does not judge the price (they did not pay it)
            string reason = null; float delta = 0f;
            if (honorOutcome == HonorOutcome.PaidFull) delta = VenueShopping.SatisfactionFor(rho, out reason);
            if (delta != 0f) GuestApi.AddSatisfaction(pawn, delta, reason);
            if (price.IsPositive) MoteMaker.ThrowText(pawn.DrawPos, map, "-" + price.ToStringShort(), Color.yellow, 2.5f);

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
        /// <summary>Shoppers hurry to buy before closing time.</summary>
        public const float LastOrdersRush = 1.5f;
        public override string Id => "venues.shop";
        public override float Desire(Pawn guest, GuestInfo info)
        {
            if (!Basic(guest, info) || GuestMemory.Purchases(guest) >= VS.I(VS.MaxPurchases)) return 0f;
            if (info.Budget.Silver < 3) return 0f;
            var all = Venues.All(guest.Map);
            bool any = false, last = false;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (VenueApi.SellsOf(v) != SellKind.Goods || !VenueApi.IsOpenForBusiness(v)) continue;
                if (VenueSales.Stock(v).Count > 0) { any = true; if (VenueApi.InLastOrders(v)) last = true; }
            }
            if (!any) return 0f;
            float budget = Mathf.Clamp01((float)info.Budget.Silver / 80f);
            return VS.F(VS.ShopDesire) * (0.35f + 0.9f * budget) * (last ? LastOrdersRush : 1f);
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
        /// <summary>A guest does not wait or ring the bell for something that is too expensive for them anyway (the bell surcharge included).</summary>
        static bool WouldPay(Pawn guest, GuestInfo info, Venue v, bool bell)
            => VenueShopping.Accepts(VenueShopping.Rho(guest, info, bell ? VenueApi.BellMultiplier(v) : VenueApi.PriceMultiplier(v), v.Sign.thingIDNumber));
        public override float Desire(Pawn guest, GuestInfo info)
        {
            if (!Basic(guest, info) || info.Budget.Silver < 3) return 0f;
            int gap = VS.I(VS.DrinkGapHours) * GenDate.TicksPerHour;
            int since = Find.TickManager.TicksGame - GuestMemory.LastDrink(guest);
            if (since < gap / 2) return 0f;
            float best = 0f;
            var all = Venues.All(guest.Map);
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (!Candidate(v)) continue;
                // last orders: one more round is allowed after half the usual gap
                bool last = VenueApi.InLastOrders(v);
                if (since < gap && !last) continue;
                switch (VenueApi.Gate(v))
                {
                    case VenueGate.Open: best = Math.Max(best, last ? 1.2f : 0.9f); break;
                    case VenueGate.WaitingForStaff: if (WouldPay(guest, info, v, false)) best = Math.Max(best, 0.5f); break;
                    case VenueGate.ClosedHours: if (VS.B(VS.NightBell) && VenueApi.NeedsStaff(v) && WouldPay(guest, info, v, true)) best = Math.Max(best, 0.2f); break;
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
                        if (WouldPay(guest, info, v, false)) return VenueShopping.WaitJob(v, ringBell: !VenueApi.InHours(v));
                        break;
                    case VenueGate.ClosedHours:
                        if (VS.B(VS.NightBell) && VenueApi.NeedsStaff(v) && WouldPay(guest, info, v, true)) return VenueShopping.WaitJob(v, ringBell: true);
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

    // GuestFallback (the vanilla-visitor activity driver) lives in GuestFallback.cs (owned by the Inn module).
}
