// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>Runtime state of venues that is not saved (staff on duty, bell rung). It repairs itself within a second after loading.</summary>
    public static class VenueState
    {
        sealed class StaffRecord { public Pawn Pawn; public int Tick; }
        static readonly Dictionary<int, StaffRecord> staff = new Dictionary<int, StaffRecord>();
        static readonly Dictionary<int, int> bellUntil = new Dictionary<int, int>();
        /// <summary>A staff ping is valid this many ticks (the tending job pings every 30).</summary>
        public const int StaffTimeout = 100;
        /// <summary>When somebody answers the night bell the venue stays open at least this long (ticks) for the guest who rang.</summary>
        public const int BellServiceTicks = 1500;

        public static void Clear() { staff.Clear(); bellUntil.Clear(); VenueSales.ClearCaches(); GuestMemory.Clear(); }

        public static void PingStaff(Thing sign, Pawn pawn)
        {
            if (sign == null) return;
            staff[sign.thingIDNumber] = new StaffRecord { Pawn = pawn, Tick = Find.TickManager.TicksGame };
        }

        public static Pawn StaffOn(Thing sign)
        {
            if (sign == null || !staff.TryGetValue(sign.thingIDNumber, out var r)) return null;
            if (Find.TickManager.TicksGame - r.Tick > StaffTimeout || r.Pawn == null || r.Pawn.Dead || !r.Pawn.Spawned) return null;
            return r.Pawn;
        }

        public static void RingBell(Thing sign, int durationTicks)
        {
            if (sign == null) return;
            int until = Find.TickManager.TicksGame + durationTicks;
            if (!bellUntil.TryGetValue(sign.thingIDNumber, out int cur) || cur < until) bellUntil[sign.thingIDNumber] = until;
        }

        public static bool BellActive(Thing sign) => sign != null && bellUntil.TryGetValue(sign.thingIDNumber, out int u) && u > Find.TickManager.TicksGame;
    }

    /// <summary>
    /// The one gate other modules should ask before serving a guest: hours, staff, bell and prices of a venue (6.1, 5.13).
    /// </summary>
    public static class VenueApi
    {
        public static VenueSaleExtension SaleOf(VenueTypeDef t) => t?.GetModExtension<VenueSaleExtension>();
        public static SellKind SellsOf(Venue v) => SaleOf(v?.Type)?.sells ?? SellKind.None;

        /// <summary>Does this venue need somebody on duty to serve? Follows the staffing rule setting. An honor-system venue (F15) never does.</summary>
        public static bool NeedsStaff(Venue v)
        {
            if (v?.Type == null) return false;
            if (VenueHonor.IsOn(v)) return false;
            switch (HSettings.GetEnum<StaffRule>(VS.StaffRuleKey))
            {
                case StaffRule.NeverRequired: return false;
                case StaffRule.AlwaysRequired: return v.Type != HHVenueDefOf.HH_Venue_Lounge;
                default: return v.Type.needsStaff;
            }
        }

        public static bool IsStaffed(Venue v) => v != null && VenueState.StaffOn(v.Sign) != null;
        public static Pawn Staffer(Venue v) => v == null ? null : VenueState.StaffOn(v.Sign);
        public static bool BellActive(Venue v) => v != null && VenueState.BellActive(v.Sign);

        /// <summary>Open hours, ignoring staff.</summary>
        public static bool InHours(Venue v) => v != null && v.Comp.IsOpenAt(GenLocalDate.HourOfDay(v.Map));

        public static VenueGate Gate(Venue v)
        {
            if (v == null) return VenueGate.ClosedHours;
            if (!VenueSetup.IsReady(v)) return VenueGate.NotReady;
            bool needs = NeedsStaff(v);
            if (!InHours(v))
            {
                if (!needs) return VenueGate.ClosedHours;
                if (BellActive(v)) return IsStaffed(v) ? VenueGate.Open : VenueGate.WaitingForStaff;
                return VenueGate.ClosedHours;
            }
            if (needs && !IsStaffed(v)) return VenueGate.WaitingForStaff;
            return VenueGate.Open;
        }

        public static bool IsOpenForBusiness(Venue v) => Gate(v) == VenueGate.Open;

        // ------------------------------------------------------------------ last orders (6.10, N25)

        /// <summary>Hours until the venue closes today; <c>float.MaxValue</c> for venues that never close.</summary>
        public static float HoursToClose(Venue v) => HoursToClose(v, v == null ? 0f : GenLocalDate.HourFloat(v.Map));
        public static float HoursToClose(Venue v, float hourNow)
        {
            var c = v?.Comp;
            if (c == null || c.alwaysOpen || c.openHour == c.closeHour) return float.MaxValue;
            float d = c.closeHour - hourNow;
            return d < 0f ? d + 24f : d;
        }

        /// <summary>Open, and closing within the "last orders" window (setting, default 30 minutes): last round, short browsing, nobody turned away mid-purchase.</summary>
        public static bool InLastOrders(Venue v) => v != null && InLastOrders(v, GenLocalDate.HourFloat(v.Map));
        public static bool InLastOrders(Venue v, float hourNow)
        {
            int minutes = VS.I(VS.LastOrders);
            return minutes > 0 && v?.Comp != null && v.Comp.IsOpenAt((int)hourNow) && HoursToClose(v, hourNow) <= minutes / 60f;
        }

        /// <summary>Closed for a moment already but a purchase that was under way at closing time is still served (a quarter of an hour).</summary>
        public static bool ServingLate(Venue v) => v != null && ServingLate(v, GenLocalDate.HourFloat(v.Map));
        public static bool ServingLate(Venue v, float hourNow)
        {
            var c = v?.Comp;
            if (c == null || VS.I(VS.LastOrders) <= 0 || c.alwaysOpen || c.openHour == c.closeHour || c.IsOpenAt((int)hourNow)) return false;
            float since = hourNow - c.closeHour;
            if (since < 0f) since += 24f;
            return since <= 0.25f;
        }

        /// <summary>Can a guest who already stands at the goods still pay? Open for business, or just closed (last orders grace).</summary>
        public static bool CanCompletePurchase(Venue v) => IsOpenForBusiness(v) || ServingLate(v);

        /// <summary>Open outside normal hours only because somebody answered the bell: the surcharge applies.</summary>
        public static bool IsBellService(Venue v) => v != null && !InHours(v) && BellActive(v) && IsStaffed(v);

        /// <summary>Everything that multiplies the market value into the unit price of a venue's goods (1.0 = market value).</summary>
        public static float PriceMultiplier(Venue v)
        {
            if (v == null) return 1f;
            float m = Math.Max(0.01f, v.Comp.priceFactor) * v.Type.priceFactor * VS.F(VS.ShopPrice);
            // self-service discount; an honor-system venue asks the full price (the guests decide what they pay, F15)
            if (!NeedsStaff(v) && !IsStaffed(v) && !VenueHonor.IsOn(v)) m *= 1f - Mathf.Clamp(VS.F(VS.SelfService), 0f, 0.9f);
            if (IsBellService(v)) m *= 1f + VS.F(VS.NightBellSurcharge);
            return m;
        }

        /// <summary>The price level a guest who rings the night bell can expect (the surcharge is added once somebody answers).</summary>
        public static float BellMultiplier(Venue v)
        {
            float m = PriceMultiplier(v);
            return IsBellService(v) ? m : m * (1f + VS.F(VS.NightBellSurcharge));
        }

        /// <summary>Price of <paramref name="count"/> pieces of a thing in a venue.</summary>
        public static Money PriceOf(Thing t, int count, Venue v)
        {
            var price = Money.FromSilver((double)t.MarketValue * count * PriceMultiplier(v));
            if (IsBellService(v)) price = Money.Max(price, HSettings.GetSilver(VS.NightBellMin));
            return price;
        }

        public static string PriceTierLabel(float factor)
        {
            if (factor <= 0.001f) return "Free";
            if (factor <= 0.30f) return "Charity";
            if (factor <= 0.60f) return "Discount";
            if (factor <= 0.85f) return "Budget";
            if (factor <= 1.15f) return "Standard";
            if (factor <= 1.5f) return "Premium";
            return "Luxury";
        }

        /// <summary>The cell where a tender stands: the counter's interaction cell, else a free cell next to the sign.</summary>
        public static IntVec3 PostCell(Venue v)
        {
            var map = v.Map;
            var counter = CounterOf(v);
            if (counter != null)
            {
                var ic = counter.InteractionCell;
                if (ic.IsValid && ic.InBounds(map) && ic.Standable(map)) return ic;
            }
            var sign = v.Sign;
            foreach (var d in GenAdj.CardinalDirections)
            {
                var c = sign.Position + d;
                if (c.InBounds(map) && c.Standable(map) && c.GetRoom(map) == v.Room) return c;
            }
            return sign.Position;
        }

        public static Thing CounterOf(Venue v)
        {
            if (v?.Room == null) return null;
            foreach (var b in v.Map.listerBuildings.allBuildingsColonist)
                if (b.def == HHVenueDefOf.HH_ShopCounter && b.Spawned && b.Position.GetRoom(v.Map) == v.Room) return b;
            return null;
        }

        public static Venue VenueOfSign(Map map, Thing sign)
        {
            if (map == null || sign == null) return null;
            var all = Venues.All(map);
            for (int i = 0; i < all.Count; i++) if (all[i].Sign == sign) return all[i];
            return null;
        }
    }
}
