// SPDX-License-Identifier: MIT
// F02 + F03 (1.1.0): couples are booked together in one double bed and pay half each; double beds earn more; the wish for a double bed.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    public sealed partial class GuestRegistry
    {
        /// <summary>Do these two guests want (and may) share a bed? Lovers, fiancés and spouses; ideologies that forbid it are respected.</summary>
        public static bool CanShareBed(Pawn a, Pawn b)
        {
            if (a == null || b == null || a == b) return false;
            if (!LovePartnerRelationUtility.LovePartnerRelationExists(a, b)) return false;
            if (ModsConfig.IdeologyActive)
            {
                try { if (!BedUtility.WillingToShareBed(a, b) || !BedUtility.WillingToShareBed(b, a)) return false; }
                catch (Exception e) { HLog.Once("inn.shareBed", LogLevel.Warn, "Inn", "Bed-sharing check failed, assuming allowed: " + e.Message); }
            }
            return true;
        }

        /// <summary>The partner of this guest who is staying in the same group (any state of the stay), or null.</summary>
        public GuestRecord FindPartner(GuestRecord r)
        {
            if (r?.Pawn == null) return null;
            for (int i = 0; i < records.Count; i++)
            {
                var m = records[i];
                if (m == r || m.GroupId != r.GroupId || m.Settled || m.Pawn == null) continue;
                if (LovePartnerRelationUtility.LovePartnerRelationExists(r.Pawn, m.Pawn)) return m;
            }
            return null;
        }

        /// <summary>The partner who still needs a bed (same group, not settled, no bed yet).</summary>
        GuestRecord FindWaitingPartner(GuestRecord r)
        {
            var m = FindPartner(r);
            return m != null && m.Bed == null ? m : null;
        }

        /// <summary>F03: another guest of a couple (any group, not this guest's partner) still waits for a bed for two.</summary>
        bool CoupleNeedsDouble(GuestRecord r)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var m = records[i];
                if (m == r || m.Settled || !m.WantsDouble || m.Bed != null || m.Pawn == null || m.Pawn.MapHeld != r.Pawn?.MapHeld) continue;
                if (r.Pawn != null && m.Pawn != null && LovePartnerRelationUtility.LovePartnerRelationExists(r.Pawn, m.Pawn)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Marks both partners of a couple when a guest is registered (they arrive in one group): they would like one double bed.</summary>
        void UpdateCoupleFlags(GuestRecord r)
        {
            var m = FindPartner(r);
            if (m == null || !m.Overnight || !r.Overnight || !CanShareBed(r.Pawn, m.Pawn)) return;
            r.WantsDouble = true; m.WantsDouble = true;
        }

        public static bool SharesBed(GuestRecord a, GuestRecord b)
            => a?.Bed != null && b?.Bed != null && a.Bed == b.Bed && a.Bed.SleepingSlotsCount >= 2;

        /// <summary>Splits a bed price between two sleepers in whole silver: the first pays the larger half. A free bed stays free.</summary>
        public static void SplitPrice(Money total, out Money first, out Money second)
        {
            if (total.Micro <= 0) { first = Money.Zero; second = Money.Zero; return; }
            first = Money.FromSilver(Math.Ceiling(total.Silver / 2.0 - 1e-9));
            second = total - first;
            if (second.Micro < 0) second = Money.Zero;
        }

        /// <summary>
        /// F02: books ONE double bed for a couple, both at once: each pays half of the bed's price (no surcharge), both own the bed.
        /// Returns false (and books nothing) when there is no free bed for two, nobody can reach it, or one of them cannot afford their half.
        /// </summary>
        bool AssignCouple(GuestRecord a, GuestRecord b, int nights, bool freeNights, HashSet<Room> groupRooms)
        {
            var pa = a.Pawn; var pb = b.Pawn; var map = pa?.MapHeld;
            if (pa == null || pb == null || map == null || pb.MapHeld != map || !b.Overnight || b.Settled || b.Bed != null) return false;
            if (!CanShareBed(pa, pb)) return false;
            Building_Bed best = null; BedQuote bestQ = null; float bestU = float.MinValue;
            Money bestFirst = Money.Zero, bestSecond = Money.Zero;
            foreach (var bed in GuestBeds.FreeOn(map))
            {
                if (bed.SleepingSlotsCount < 2) continue;
                if (!pa.CanReach(bed, PathEndMode.OnCell, Danger.Some) || !pb.CanReach(bed, PathEndMode.OnCell, Danger.Some)) continue;
                if (!RestUtility.CanUseBedEver(pa, bed.def) || !RestUtility.CanUseBedEver(pb, bed.def)) continue;
                var q = GuestBedPricing.Quote(bed); if (q == null) continue;
                if (freeNights) q.Price = Money.Zero;
                SplitPrice(q.Price, out var first, out var second);
                if (first * nights > a.Budget || second * nights > b.Budget) continue;
                float u = GuestEconomy.BedUtility(q.RoomScore, first, Math.Max(a.Wealth, b.Wealth), groupRooms.Contains(bed.GetRoom())) + 40f;
                if (u > bestU) { bestU = u; best = bed; bestQ = q; bestFirst = first; bestSecond = second; }
            }
            if (best == null) return false;
            bestQ.Price = bestFirst;
            if (!ClaimBed(a, best, bestQ)) return false;
            bestQ.Price = bestSecond;
            if (!ClaimBed(b, best, bestQ)) { ReleaseBed(a); return false; }
            string line = "HH.Inn.Event.CoupleBooked".TranslateOr("Booked one double bed with {0}: {1} silver a night each.");
            a.Log(line.Replace("{0}", pb.LabelShort).Replace("{1}", bestFirst.WholeSilver.ToString()), 2);
            b.Log(line.Replace("{0}", pa.LabelShort).Replace("{1}", bestSecond.WholeSilver.ToString()), 2);
            HLog.Debug("Inn", $"Couple {pa.LabelShort} + {pb.LabelShort} booked {best.ThingID}: {bestFirst} + {bestSecond} silver");
            return true;
        }

        /// <summary>
        /// F02: once the beds of a couple are known, the wish for a double bed is settled once per stay: a small satisfaction bonus when they share a bed,
        /// a small annoyance when both ended up in single beds. Waits while the partner has not checked in and may still move in.
        /// </summary>
        void ResolveDoubleWish(GuestRecord r)
        {
            if (r == null || !r.WantsDouble || r.Bed == null) return;
            var partner = FindPartner(r);
            if (partner == null) return;
            if (r.DoubleWishDone && partner.DoubleWishDone) return;
            bool shared = SharesBed(r, partner);
            // a partner who is only a day visitor, or who never got a bed, is no "missed double bed": nothing to resolve then
            if (!shared && !(partner.Overnight && partner.Bed != null && r.CheckedIn && partner.CheckedIn)) return;
            bool wish = HSettings.GetBool(InnSettings.CoupleWish);
            foreach (var x in new[] { r, partner })
            {
                if (x.DoubleWishDone) continue;
                x.DoubleWishDone = true;
                if (!wish || x.Pawn == null) continue;
                if (shared)
                    AddSatisfaction(x.Pawn, 0.06f, "HH.Inn.Event.SleepsTogether".TranslateOr("Sleeps next to {0}: just what they hoped for.").Replace("{0}", (x == r ? partner : r).Pawn.LabelShort));
                else
                {
                    AddSatisfaction(x.Pawn, -0.03f, "HH.Inn.Event.WantedDouble".TranslateOr("Wanted a double bed with {0}, and got two single beds.").Replace("{0}", (x == r ? partner : r).Pawn.LabelShort));
                    try { x.Pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(InnDefOf.HH_WantedDoubleBed); }
                    catch (Exception e) { HLog.Once("inn.wantedDouble", LogLevel.Warn, "Inn", "Double-bed thought failed: " + e.Message); }
                }
            }
        }

        /// <summary>Morning after a night in a shared bed: the couple is happy about it (a small memory, once a day).</summary>
        void GiveCoupleThought(GuestRecord r)
        {
            if (r == null || !r.WantsDouble || !HSettings.GetBool(InnSettings.CoupleWish)) return;
            var partner = FindPartner(r);
            if (partner == null || !SharesBed(r, partner)) return;
            try { r.Pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(InnDefOf.HH_SleptTogether); }
            catch (Exception e) { HLog.Once("inn.sleptTogether", LogLevel.Warn, "Inn", "Slept-together thought failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ lying down in the booked bed (F03 roomy bed, F31 couple's heart)
        /// <summary>Last seen "lies in the booked bed" per guest pawn (not saved: after a load the first look only records the state).</summary>
        static readonly Dictionary<int, bool> bedSeen = new Dictionary<int, bool>();
        /// <summary>Tests only: replaces "lies in this bed" for a pawn (null = the real check).</summary>
        internal static Func<Pawn, Building_Bed, bool> InBedOverride;

        public static void ClearBedSeen() => bedSeen.Clear();

        static bool LiesIn(Pawn p, Building_Bed bed)
        {
            if (p == null || bed == null || !p.Spawned) return false;
            if (InBedOverride != null) return InBedOverride(p, bed);
            // any job that lies the pawn down in this bed counts (LayDown, resting while sick, a nap from another mod): the posture decides, not the job def
            return p.CurrentBed() == bed;
        }

        /// <summary>Every 30 ticks: notices when a checked-in guest lies down in the booked bed (a cheap pass over the register).</summary>
        void BedFxPass()
        {
            if (records.Count == 0) { if (bedSeen.Count > 0) bedSeen.Clear(); return; }
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                var p = r.Pawn;
                if (p == null || !p.Spawned || r.Settled || !r.CheckedIn || !r.Overnight || r.Bed == null || !r.Bed.Spawned) continue;
                bool now = LiesIn(p, r.Bed);
                int id = p.thingIDNumber;
                if (!bedSeen.TryGetValue(id, out bool before)) { bedSeen[id] = now; continue; }
                if (now == before) continue;
                bedSeen[id] = now;
                if (now) OnLayDown(r);
            }
            if (bedSeen.Count > records.Count * 2 + 16) bedSeen.Clear();   // guests long gone: start over (the next pass only records)
        }

        /// <summary>A guest just lay down in the booked bed: a couple sharing it gets a small heart, a guest alone in a double bed is pleased once per stay.</summary>
        void OnLayDown(GuestRecord r)
        {
            var bed = r.Bed;
            var partner = FindPartner(r);
            if (partner != null && SharesBed(r, partner))
            {
                if (LiesIn(partner.Pawn, bed)) Hearth.Life.GuestLifeFx.NoteHeart(r.Pawn, partner.Pawn);
                return;
            }
            if (bed.SleepingSlotsCount < 2 || r.RoomyBedDone) return;
            var owners = bed.OwnersForReading;
            for (int i = 0; i < owners.Count; i++) if (owners[i] != r.Pawn) return;   // somebody else sleeps there too: not "all to themselves"
            r.RoomyBedDone = true;
            AddSatisfaction(r.Pawn, GuestEconomy.RoomyBedSatisfaction, "HH.Inn.Event.RoomyBed".TranslateOr("Had a roomy double bed all to themselves."));
        }

        // ---- tests only
        internal void BedFxPassForTests() => BedFxPass();
        internal void ResolveDoubleWishForTests(GuestRecord r) => ResolveDoubleWish(r);
        internal void ReleaseBedForTests(GuestRecord r) => ReleaseBed(r);
    }
}
