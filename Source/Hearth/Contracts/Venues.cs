// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// A kind of business a Venue Sign can turn a room into (6.1): Tavern, Shop, Restaurant, Casino, Spa, Cinema …
    /// Defined in XML (Common/Defs/*/VenueTypes.xml) by the module that owns it.
    /// </summary>
    public class VenueTypeDef : Def
    {
        public ModuleId module = ModuleId.ShopsFood;
        public int defaultOpenHour = 8;
        public int defaultCloseHour = 22;
        public string iconPath;
        [Unsaved] public Texture2D icon;
        /// <summary>Does the room need staff (Serving work) to be open?</summary>
        public bool needsStaff;
        public WorkTypeDef staffWorkType;
        public float priceFactor = 1f;
        public ResearchProjectDef researchPrerequisite;
        public Color color = new Color(0.85f, 0.5f, 0.25f);

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (!iconPath.NullOrEmpty()) LongEventHandler.ExecuteWhenFinished(() => icon = ContentFinder<Texture2D>.Get(iconPath, false));
        }
    }

    /// <summary>One business: the room of a Venue Sign. Built and cached by Venues.Refresh; never saved itself.</summary>
    public sealed class Venue
    {
        public Thing Sign;
        public CompVenueSign Comp;
        Room room;
        /// <summary>The room of the sign. Re-evaluated when RimWorld has replaced the old Room object (door/wall changes).</summary>
        public Room Room
        {
            get
            {
                if ((room == null || room.Dereferenced) && Sign != null && Sign.Spawned) room = Venues.RoomOfSign(Sign);
                return room;
            }
            set { room = value; }
        }
        public VenueTypeDef Type => Comp.type;
        public Map Map => Sign.Map;
        public string Account => "house:venue:" + Sign.thingIDNumber;
        public bool IsOpenNow => Comp.IsOpenAt(GenLocalDate.HourOfDay(Sign.Map));
        public bool Contains(IntVec3 c) { var r = Room; return r != null && Sign.Spawned && c.GetRoom(Sign.Map) == r; }
    }

    public class CompProperties_VenueSign : CompProperties
    {
        /// <summary>Venue types this sign may show; empty = all.</summary>
        public List<VenueTypeDef> allowedTypes = new List<VenueTypeDef>();
        public bool wallMounted;
        public CompProperties_VenueSign() { compClass = typeof(CompVenueSign); }
    }

    /// <summary>
    /// Data of a Venue Sign: type, opening hours, price factor, who may take items (N45).
    /// The gizmos/UI and the staffing logic are added by the ShopsFood module; this class only stores and answers.
    /// </summary>
    public class CompVenueSign : ThingComp
    {
        public VenueTypeDef type;
        public int openHour = -1, closeHour = -1;
        public bool alwaysOpen;
        public float priceFactor = 1f;
        public bool colonyMayTakeGoods;
        public string customName;

        public CompProperties_VenueSign Props => (CompProperties_VenueSign)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureType();
            Venues.Register(this, parent.Map);
        }

        /// <summary>Gives a sign without a type the first type of an enabled module (allowed types first). Does nothing if none is usable yet.</summary>
        public void EnsureType()
        {
            if (type == null) type = Props.allowedTypes.FirstOrDefault(d => Modules.On(d.module)) ?? DefDatabase<VenueTypeDef>.AllDefs.FirstOrDefault(d => Modules.On(d.module));
            if (openHour < 0 && type != null) { openHour = type.defaultOpenHour; closeHour = type.defaultCloseHour; }
        }
        public override void PostDeSpawn(Map map
#if RW16PLUS
            , DestroyMode mode = DestroyMode.Vanish
#endif
            )
        {
            base.PostDeSpawn(map
#if RW16PLUS
                , mode
#endif
                );
            Venues.Unregister(this, map);
        }

        public bool IsOpenAt(int hour)
        {
            if (alwaysOpen || openHour == closeHour) return true;
            return openHour < closeHour ? hour >= openHour && hour < closeHour : hour >= openHour || hour < closeHour;
        }

        public string DisplayName => !customName.NullOrEmpty() ? customName : type?.LabelCap.ToString() ?? parent.LabelCap;

        public override void PostExposeData()
        {
            Scribe_Defs.Look(ref type, "venueType");
            Scribe_Values.Look(ref openHour, "openHour", -1);
            Scribe_Values.Look(ref closeHour, "closeHour", -1);
            Scribe_Values.Look(ref alwaysOpen, "alwaysOpen");
            Scribe_Values.Look(ref priceFactor, "priceFactor", 1f);
            Scribe_Values.Look(ref colonyMayTakeGoods, "colonyMayTakeGoods");
            Scribe_Values.Look(ref customName, "customName");
        }

        public override string CompInspectStringExtra()
        {
            if (type == null) return null;
            string hours = alwaysOpen ? "HH.Venue.AlwaysOpen".TranslateOr("open around the clock") : $"{openHour:00}:00–{closeHour:00}:00";
            bool open = parent.Spawned && IsOpenAt(GenLocalDate.HourOfDay(parent.Map));
            return $"{type.LabelCap} · {hours} · " + (open ? "HH.Venue.Open".TranslateOr("open") : "HH.Venue.Closed".TranslateOr("closed"));
        }
    }

    /// <summary>Finds venues on a map. Cached per map and rebuilt lazily when signs or rooms change. Signs register themselves, so no map scan is needed.</summary>
    public static class Venues
    {
        static readonly Dictionary<int, List<Venue>> cache = new Dictionary<int, List<Venue>>();
        static readonly Dictionary<int, int> builtTick = new Dictionary<int, int>();
        // spawned signs per map (added in PostSpawnSetup, removed in PostDeSpawn)
        static readonly Dictionary<Map, HashSet<CompVenueSign>> signs = new Dictionary<Map, HashSet<CompVenueSign>>();

        static Venues() { HearthGame.OnGameInit += OnGameInit; }

        /// <summary>New game or load: forget every cached venue and the signs of maps that no longer exist.</summary>
        static void OnGameInit()
        {
            Clear();
            var maps = Current.Game?.Maps;
            if (maps != null && maps.Count > 0)
                foreach (var m in signs.Keys.Where(m => !maps.Contains(m)).ToList()) signs.Remove(m);
        }

        /// <summary>Drops all cached venue lists (the sign registry stays).</summary>
        public static void Clear() { cache.Clear(); builtTick.Clear(); }

        internal static void Register(CompVenueSign c, Map map)
        {
            if (map == null) return;
            if (!signs.TryGetValue(map, out var set)) signs[map] = set = new HashSet<CompVenueSign>();
            set.Add(c);
            MarkDirty(map);
        }

        internal static void Unregister(CompVenueSign c, Map map)
        {
            if (map == null) return;
            if (signs.TryGetValue(map, out var set)) { set.Remove(c); if (set.Count == 0) signs.Remove(map); }
            MarkDirty(map);
        }

        public static void MarkDirty(Map map) { if (map != null) cache.Remove(map.uniqueID); }

        /// <summary>
        /// The venues of a map (signs of the player with a type of an enabled module, standing in an indoor room).
        /// The list is a shared cache: treat it as read-only, never add to or remove from it.
        /// </summary>
        public static List<Venue> All(Map map)
        {
            if (map == null) return new List<Venue>();
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(map.uniqueID, out var list) && builtTick.TryGetValue(map.uniqueID, out int b) && now >= b && now - b < 250 && StillValid(list, map)) return list;
            if (!signs.TryGetValue(map, out var set) || set.Count == 0) { cache[map.uniqueID] = list = new List<Venue>(); builtTick[map.uniqueID] = now; return list; }
            list = new List<Venue>(set.Count);
            foreach (var c in set)
            {
                var t = c.parent;
                if (t == null || !t.Spawned || t.Map != map || t.Faction != Faction.OfPlayer) continue;
                if (c.type == null) c.EnsureType();
                if (c.type == null || !Modules.On(c.type.module)) continue;
                var room = RoomOfSign(t);
                if (room == null || room.PsychologicallyOutdoors) continue;
                list.Add(new Venue { Sign = t, Comp = c, Room = room });
            }
            list.Sort((x, y) => x.Sign.thingIDNumber.CompareTo(y.Sign.thingIDNumber));
            cache[map.uniqueID] = list; builtTick[map.uniqueID] = now;
            return list;
        }

        static bool StillValid(List<Venue> list, Map map)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (v.Sign == null || !v.Sign.Spawned || v.Sign.Map != map || v.Comp.type == null || !Modules.On(v.Comp.type.module)) return false;
            }
            return true;
        }

        /// <summary>Wall-mounted signs belong to the room in front of them (like a wall lamp); free-standing ones to their own cell's room.</summary>
        public static Room RoomOfSign(Thing sign)
        {
            var map = sign.Map;
            if (map == null) return null;
            var cell = sign.Position;
            var room = cell.GetRoom(map);
            bool wallMounted = sign.def.building?.isAttachment == true || sign.TryGetComp<CompVenueSign>()?.Props.wallMounted == true;
            if (room == null || wallMounted || room.IsDoorway)
            {
                var front = sign.Position + sign.Rotation.FacingCell;
                if (front.InBounds(map)) room = front.GetRoom(map);
            }
            return room;
        }

        public static IEnumerable<Venue> OfType(Map map, VenueTypeDef type) => All(map).Where(v => v.Type == type);
        public static Venue At(IntVec3 cell, Map map)
        {
            var room = cell.GetRoom(map);
            if (room == null) return null;
            var all = All(map);
            for (int i = 0; i < all.Count; i++) if (all[i].Room == room) return all[i];
            return null;
        }
    }
}
