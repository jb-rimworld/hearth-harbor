// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// Waiting, night bell and the front desk's hours (5.13). The Venues module runs the bell and the waiting job of staffed services;
    /// it reports the outcome through GuestApi.AddSatisfaction, and this class turns those outcomes into mood thoughts
    /// (also callable directly: <see cref="Waited"/>). A waiting room (seats, or a lounge venue) halves the waiting thought and
    /// gives +1 … +4 by its impressiveness.
    /// </summary>
    public static class GuestThoughts
    {
        static string bellNobody, staffNobody;
        static string BellNobodyText => bellNobody ?? (bellNobody = "HH.Venue.NobodyCameBell".TranslateOr("Rang the bell. Nobody came."));
        static string StaffNobodyText => staffNobody ?? (staffNobody = "HH.Venue.NobodyCameStaff".TranslateOr("Waited for service. Nobody came."));

        /// <summary>Is the front desk staffed by schedule right now? (closing hour before opening hour = open past midnight)</summary>
        public static bool DeskOpenNow(Map map)
        {
            if (map == null) return true;
            int h = GenLocalDate.HourOfDay(map);
            int o = HSettings.GetInt(InnSettings.DeskOpenHour), c = HSettings.GetInt(InnSettings.DeskCloseHour);
            if (o == c) return true;
            return o < c ? h >= o && h < c : h >= o || h < c;
        }

        /// <summary>A room counts as waiting room when it is indoors and has something to sit on, or holds a lounge venue.</summary>
        public static bool IsWaitingRoom(Room room)
        {
            if (room == null || room.PsychologicallyOutdoors || room.IsHuge) return false;
            foreach (var v in Venues.All(room.Map)) if (v.Room == room && v.Type != null && v.Type.defName == "HH_Venue_Lounge") return true;
            foreach (var t in room.ContainedAndAdjacentThings)
                if (t is Building b && b.def.building != null && b.def.building.isSittable && b.GetRoom() == room) return true;
            return false;
        }

        static int WaitingRoomStage(Room room)
        {
            float imp = room.GetStat(RoomStatDefOf.Impressiveness);
            return imp < 30f ? 0 : imp < 50f ? 1 : imp < 70f ? 2 : 3;
        }

        /// <summary>The guest waited for service (bell = after ringing the night bell). nobodyCame: the wait was in vain.</summary>
        public static void Waited(Pawn p, bool bell, bool nobodyCame, Room room = null)
        {
            if (p == null || !HSettings.GetBool(InnSettings.WaitThoughts)) return;
            var mem = p.needs?.mood?.thoughts?.memories; if (mem == null) return;
            room = room ?? (p.Spawned ? p.GetRoom() : null);
            bool waitingRoom = IsWaitingRoom(room);
            try
            {
                if (bell && nobodyCame) mem.TryGainMemory(ThoughtMaker.MakeThought(InnDefOf.HH_GuestBellNobody, 0));
                else
                {
                    int stage = nobodyCame ? 1 : 0;
                    if (waitingRoom) stage--; // the waiting room halves the waiting thought
                    if (stage >= 0) mem.TryGainMemory(ThoughtMaker.MakeThought(InnDefOf.HH_GuestWaited, stage));
                }
                if (waitingRoom) mem.TryGainMemory(ThoughtMaker.MakeThought(InnDefOf.HH_GuestWaitingRoom, WaitingRoomStage(room)));
            }
            catch (Exception e) { HLog.Once("inn.waitThought", LogLevel.Warn, "Inn", "Waiting thought failed: " + e.Message); }
        }

        /// <summary>Bridge: the Venues module's "nobody came" outcomes become thoughts (subscribed to GuestApi.SatisfactionChanged).</summary>
        internal static void OnSatisfaction(Pawn p, float delta, string reason)
        {
            if (p == null || reason.NullOrEmpty() || delta >= 0f || !Modules.On(ModuleId.Inn)) return;
            if (reason == BellNobodyText) Waited(p, bell: true, nobodyCame: true);
            else if (reason == StaffNobodyText) Waited(p, bell: false, nobodyCame: true);
        }
    }

    /// <summary>Return visits and the gift on leaving (5.14).</summary>
    public static class GuestReturns
    {
        /// <summary>Days until a group comes back: travel·2 + (S̄ &gt; 0 ? Lerp(10, 3, S̄/0.5) : Lerp(14, 30, −S̄/0.5)).</summary>
        public static float ReturnDays(float meanSurplus, float travelDays)
        {
            float d = meanSurplus > 0f ? Mathf.Lerp(10f, 3f, Mathf.Clamp01(meanSurplus / 0.5f)) : Mathf.Lerp(14f, 30f, Mathf.Clamp01(-meanSurplus / 0.5f));
            return Mathf.Max(0f, travelDays) * 2f + d;
        }

        /// <summary>Chance of a gift on leaving: Lerp(0.2, 0.9, V).</summary>
        public static float GiftChance(float value) => Mathf.Lerp(0.2f, 0.9f, Mathf.Clamp01(value));

        /// <summary>Travel time estimate: drop pods half a day, otherwise days to the faction's nearest settlement (0.5 … 5).</summary>
        public static float TravelDays(Faction f, Map map, bool byPod)
        {
            if (byPod) return 0.5f;
            try
            {
                int best = int.MaxValue;
                foreach (var s in Find.WorldObjects.Settlements)
                {
                    if (s.Faction != f) continue;
                    int d = Find.WorldGrid.TraversalDistanceBetween(map.Tile, s.Tile, true, 400);
                    if (d < best) best = d;
                }
                if (best == int.MaxValue) return 1f;
                return Mathf.Clamp(best / 4f, 0.5f, 5f); // about four tiles a day on foot
            }
            catch { return 1f; }
        }

        /// <summary>Plans the group's return in the map's arrival plan and keeps the pawns as world pawns until then.</summary>
        public static PlannedGroup PlanReturn(GuestGroup g, List<GuestRecord> members, Map map)
        {
            if (g == null || map == null || g.Faction == null || !HSettings.GetBool(InnSettings.ReturnVisits)) return null;
            var pawns = members.Where(m => m.Pawn != null && !m.Pawn.Dead && !m.Pawn.Destroyed && !m.SentAway && m.Pawn.Faction == g.Faction && !m.Pawn.IsPrisoner).Select(m => m.Pawn).ToList();
            if (pawns.Count == 0 || members.Any(m => m.SentAway)) return null;
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>(); if (sch == null) return null;
            float days = ReturnDays(g.MeanSurplus, TravelDays(g.Faction, map, g.ByPod));
            int nights = Math.Max(1, members.Max(m => m.NightsPlanned));
            var plan = sch.AddReturn(g.Faction, pawns, g.Kind, nights, Find.TickManager.TicksGame + (int)(days * 60000));
            foreach (var p in pawns) Find.WorldPawns.ForcefullyKeptPawns.Add(p);
            HLog.Info("Inn", $"{g.Faction.Name} plan to come back in {days:0.0} days ({pawns.Count} guests)");
            return plan;
        }

        /// <summary>Releases the world-pawn pin of a return that will not happen.</summary>
        public static void Unpin(IEnumerable<Pawn> pawns)
        {
            if (pawns == null) return;
            foreach (var p in pawns) if (p != null) Find.WorldPawns.ForcefullyKeptPawns.Remove(p);
        }

        /// <summary>A returning pawn may come if it is alive, still of the faction, not on any map and not in a caravan.</summary>
        public static bool CanReturn(Pawn p, Faction f)
            => p != null && !p.Dead && !p.Destroyed && !p.Spawned && p.Faction == f && !p.IsPrisoner && !p.IsSlave
               && Find.WorldPawns.Contains(p) && RimWorld.Planet.CaravanUtility.GetCaravan(p) == null;

        /// <summary>The gift on leaving: a small item worth about 10 % of what the guest spent (min. 8 silver), at the front desk.</summary>
        public static Thing TryGift(GuestRecord r, Map map)
        {
            if (r?.Pawn == null || map == null || !HSettings.GetBool(InnSettings.LeavingGifts)) return null;
            if (!JRand.Stream("inn.gift").Chance(GiftChance(r.Value))) return null;
            float value = Mathf.Clamp(r.Spent.WholeSilver * 0.1f, 8f, 80f) * Mathf.Lerp(0.8f, 1.5f, r.Wealth / 100f);
            var tech = r.Pawn.Faction?.def?.techLevel ?? TechLevel.Neolithic;
            var options = new List<ThingDef> { ThingDefOf.MedicineHerbal, ThingDefOf.Cloth, DefDatabase<ThingDef>.GetNamedSilentFail("Jade"), ThingDefOf.Gold };
            if (tech >= TechLevel.Industrial) { options.Add(ThingDefOf.MedicineIndustrial); options.Add(ThingDefOf.MealSurvivalPack); }
            var beer = DefDatabase<ThingDef>.GetNamedSilentFail("Beer"); if (beer != null) options.Add(beer);
            options.RemoveAll(d => d == null || d.BaseMarketValue <= 0.01f);
            if (options.Count == 0) return null;
            var def = options[JRand.Stream("inn.gift").Range(0, options.Count)];
            int count = Mathf.Clamp(Mathf.RoundToInt(value / def.BaseMarketValue), 1, def.stackLimit);
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            var desk = GuestRegistry.Instance?.FindDesk(map);
            IntVec3 at = desk != null ? desk.InteractionCell : (r.Pawn.Spawned ? r.Pawn.Position : IntVec3.Invalid);
            if (!at.IsValid || !GenPlace.TryPlaceThing(thing, at, map, ThingPlaceMode.Near)) { thing.Destroy(); return null; }
            r.Gift = thing.LabelCap;
            r.Log("HH.Inn.Event.Gift".TranslateOr("Left a gift: {0}.").Replace("{0}", thing.LabelCap), 5);
            return thing;
        }
    }
}
