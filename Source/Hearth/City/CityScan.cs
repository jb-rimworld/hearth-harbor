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
    public sealed class CinemaSpot { public Venue Venue; public Thing Screen; public CompProjectorScreen Comp; public int Seats; }
    public sealed class SalonSpot { public Venue Venue; public List<Building_SalonChair> Chairs = new List<Building_SalonChair>(); }
    public sealed class GallerySpot { public Venue Venue; public List<Thing> Art = new List<Thing>(); public float AverageBeauty; }

    /// <summary>
    /// Finds the city facilities of a map: cinemas (venue + powered screen + seats), salons, galleries, benches.
    /// Cached per map for 250 ticks (venues themselves are cached by Venues.All); MarkDirty after building changes.
    /// </summary>
    public static class CityScan
    {
        sealed class Entry
        {
            public int Tick = -99999;
            public List<CinemaSpot> Cinemas = new List<CinemaSpot>();
            public List<SalonSpot> Salons = new List<SalonSpot>();
            public List<GallerySpot> Galleries = new List<GallerySpot>();
        }
        static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();

        public static void MarkDirty(Map map) { if (map != null) cache.Remove(map.uniqueID); }
        public static void Clear() => cache.Clear();

        static Entry Get(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(map.uniqueID, out var e) && now - e.Tick < 250 && now >= e.Tick) return e;
            e = new Entry { Tick = now };
            var venues = Venues.All(map);
            for (int i = 0; i < venues.Count; i++)
            {
                var v = venues[i];
                if (v.Type == null || v.Room == null) continue;
                if (CityDefs.IsCinemaType(v.Type)) BuildCinema(map, v, e);
                else if (v.Type == CityDefs.VenueSalon) BuildSalon(map, v, e);
                else if (v.Type == CityDefs.VenueGallery) BuildGallery(map, v, e);
            }
            cache[map.uniqueID] = e;
            return e;
        }

        public static List<CinemaSpot> Cinemas(Map map) => map == null ? new List<CinemaSpot>() : Get(map).Cinemas;
        public static List<SalonSpot> Salons(Map map) => map == null ? new List<SalonSpot>() : Get(map).Salons;
        public static List<GallerySpot> Galleries(Map map) => map == null ? new List<GallerySpot>() : Get(map).Galleries;

        static void BuildCinema(Map map, Venue v, Entry e)
        {
            var def = CityDefs.ProjectorScreen; if (def == null) return;
            foreach (var t in map.listerThings.ThingsOfDef(def))
            {
                if (t.Faction != Faction.OfPlayer || !v.Contains(t.Position)) continue;
                var comp = t.TryGetComp<CompProjectorScreen>(); if (comp == null) continue;
                e.Cinemas.Add(new CinemaSpot { Venue = v, Screen = t, Comp = comp, Seats = CountSeats(t) });
            }
        }

        static void BuildSalon(Map map, Venue v, Entry e)
        {
            var def = CityDefs.SalonChair; if (def == null) return;
            var s = new SalonSpot { Venue = v };
            foreach (var t in map.listerThings.ThingsOfDef(def))
                if (t is Building_SalonChair c && t.Faction == Faction.OfPlayer && v.Contains(t.Position)) s.Chairs.Add(c);
            if (s.Chairs.Count > 0) e.Salons.Add(s);
        }

        static void BuildGallery(Map map, Venue v, Entry e)
        {
            var g = new GallerySpot { Venue = v };
            g.Art = ArtIn(map, v.Room);
            if (g.Art.Count > 0) g.AverageBeauty = g.Art.Average(a => Mathf.Max(0f, a.GetStatValue(StatDefOf.Beauty)));
            e.Galleries.Add(g);
        }

        /// <summary>Artworks in a room: buildings with an art component (sculptures and the like). Their vanilla Beauty stat is what guests admire.</summary>
        public static List<Thing> ArtIn(Map map, Room room)
        {
            var result = new List<Thing>();
            if (room == null) return result;
            var seen = new HashSet<int>();
            foreach (var c in room.Cells)
            {
                var list = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t.def.category != ThingCategory.Building || !seen.Add(t.thingIDNumber)) continue;
                    if (t.TryGetComp<CompArt>() == null) continue;
                    result.Add(t);
                }
            }
            return result;
        }

        /// <summary>Cells in front of the screen from which a seated or standing pawn can watch, with a sittable building on them.</summary>
        public static int CountSeats(Thing screen)
        {
            if (screen?.Map == null) return 0;
            int n = 0;
            foreach (var c in WatchBuildingUtility.CalculateWatchCells(screen.def, screen.Position, screen.Rotation, screen.Map))
            {
                var e = c.GetEdifice(screen.Map);
                if (e != null && e.def.building != null && e.def.building.isSittable) n++;
            }
            return n;
        }

        public static IEnumerable<Thing> ScreensNear(Map map, IntVec3 spot, float radius)
        {
            var def = CityDefs.ProjectorScreen; if (def == null) yield break;
            foreach (var t in map.listerThings.ThingsOfDef(def))
                if (t.Faction == Faction.OfPlayer && t.Position.InHorDistOf(spot, radius)) yield return t;
        }

        public static bool IsServerAvailable(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned || p.Downed || p.Drafted || !p.Awake()) return false;
            if (p.workSettings == null || p.WorkTypeIsDisabled(HHWork.Serving)) return false;
            if (!p.workSettings.WorkIsActive(HHWork.Serving)) return false;
            return p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        public static bool AnyServer(Map map)
        {
            if (map == null || HHWork.Serving == null) return false;
            var list = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < list.Count; i++) if (IsServerAvailable(list[i])) return true;
            return false;
        }

        /// <summary>Estimated open venue? True when the venue exists, is open by the clock and (if it needs staff) somebody can serve.</summary>
        public static bool IsOpenForBusiness(Venue v)
        {
            if (v == null || !v.IsOpenNow || !Venues.IsReady(v)) return false;   // 1.1.0: required furniture (setup guide, F12)
            if (v.Type.needsStaff && !AnyServer(v.Map)) return false;
            return true;
        }
    }

    /// <summary>The shared work types (defined in Core).</summary>
    public static class HHWork
    {
        static WorkTypeDef serving, hosting;
        public static WorkTypeDef Serving => serving ?? (serving = DefDatabase<WorkTypeDef>.GetNamedSilentFail("HH_Serving"));
        public static WorkTypeDef Hosting => hosting ?? (hosting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("HH_Hosting"));
    }
}
