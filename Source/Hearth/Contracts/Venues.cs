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
        /// <summary>Grammar for business names of this type (F33): a RulePackDef with an <c>r_name</c> rule. Null = signs show the type label.</summary>
        public RulePackDef nameRules;

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
        /// <summary>Open for business right now: inside the hours (or answering the night bell) and, if the type needs somebody on duty, somebody is. Same answer as the gate the venue's own shops use.</summary>
        public bool OpenAndStaffed => Hearth.ShopsFood.VenueApi.IsOpenForBusiness(this);
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

        /// <summary>
        /// The room a sign belongs to. A wall-mounted sign (building.isAttachment, like the vanilla wall lamp) stands on the floor cell
        /// in front of its wall (Placeworker_AttachedToWall: the wall is at Position + Rotation.FacingCell), so its own cell is the right room.
        /// If that cell has no room (or is a doorway) the neighbouring cells are tried, the cell in front of the sign first.
        /// </summary>
        public static Room RoomOfSign(Thing sign)
        {
            var map = sign?.Map;
            if (map == null) return null;
            var cell = sign.Position;
            var room = cell.GetRoom(map);
            if (room != null && !room.IsDoorway) return room;
            Room fallback = room;
            var rot = sign.Rotation;
            // the wall side is the one an attachment leans on: look away from it first
            var order = new[] { rot.Opposite.FacingCell, rot.Rotated(RotationDirection.Clockwise).FacingCell, rot.Rotated(RotationDirection.Counterclockwise).FacingCell, rot.FacingCell };
            for (int i = 0; i < order.Length; i++)
            {
                var c = cell + order[i];
                if (!c.InBounds(map)) continue;
                var r = c.GetRoom(map);
                if (r == null || r.IsDoorway) continue;
                return r;
            }
            return fallback;
        }

        /// <summary>Somebody is on duty at this venue right now (a colonist standing at the sign or counter). A venue that needs nobody is not "staffed" by this rule; ask <see cref="Venue.OpenAndStaffed"/> for "can serve".</summary>
        public static bool IsStaffed(Venue v) => Hearth.ShopsFood.VenueApi.IsStaffed(v);
        /// <summary>The pawn on duty, if any.</summary>
        public static Pawn Staffer(Venue v) => Hearth.ShopsFood.VenueApi.Staffer(v);
        /// <summary>Does the venue need somebody on duty to serve (type and staffing rule)?</summary>
        public static bool NeedsStaff(Venue v) => Hearth.ShopsFood.VenueApi.NeedsStaff(v);
        /// <summary>Shorthand for <see cref="Venue.OpenAndStaffed"/>.</summary>
        public static bool IsOpenAndStaffed(Venue v) => v != null && v.OpenAndStaffed;

        /// <summary>
        /// Renown of the colony, 0-100. Set by the module that owns renown (Inn/Renown) with a function; until then the colony counts as neutral (50)
        /// so nothing that asks for a minimum renown is blocked.
        /// </summary>
        public static Func<float> RenownGetter;
        public static float Renown() { try { return RenownGetter != null ? Mathf.Clamp(RenownGetter(), 0f, 100f) : 50f; } catch (Exception e) { HLog.Once("venues.renown", LogLevel.Warn, "Venues", "Renown getter failed: " + e.Message); return 50f; } }

        /// <summary>
        /// 1.1.0 (F14): a colonist served a guest (a sale, a round at a table, a treatment). The Venues module counts it for the
        /// colonist's shift mood (busy vs. bored). Other modules call this; it does nothing while nobody listens.
        /// </summary>
        public static void NoteServed(Pawn staffer)
        {
            if (staffer == null || ServedHook == null) return;
            try { ServedHook(staffer); } catch (Exception e) { HLog.Once("venues.servedHook", LogLevel.Warn, "Venues", "NoteServed failed: " + e.Message); }
        }
        public static Action<Pawn> ServedHook;

        /// <summary>
        /// 1.1.0 (F12): does the venue have the furniture its type requires (or is that rule switched off)? A venue that is not ready does not open.
        /// Answered by the Venues module (cached per sign); true while nobody answers.
        /// </summary>
        public static bool IsReady(Venue v)
        {
            if (v == null) return false;
            if (ReadyHook == null) return true;
            try { return ReadyHook(v); } catch (Exception e) { HLog.Once("venues.readyHook", LogLevel.Warn, "Venues", "IsReady failed: " + e.Message); return true; }
        }
        public static Func<Venue, bool> ReadyHook;

        /// <summary>1.1.0 (F12): one line about the venue's setup ("Not ready – missing: a counter"), or null. Answered by the Venues module.</summary>
        public static string ReadySummary(Venue v)
        {
            if (v == null || ReadySummaryHook == null) return null;
            try { return ReadySummaryHook(v); } catch (Exception e) { HLog.Once("venues.readySummaryHook", LogLevel.Warn, "Venues", "ReadySummary failed: " + e.Message); return null; }
        }
        public static Func<Venue, string> ReadySummaryHook;

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
