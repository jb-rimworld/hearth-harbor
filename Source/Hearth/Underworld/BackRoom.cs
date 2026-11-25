// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>
    /// The back room door (6.11): a door that only staff, colonists and shady guests can open. A room whose every door is a back room door
    /// is a "back room": it does not count for the house's renown and is not a venue (the Inn/Venues modules ask BackRoom.IsBackRoom).
    /// </summary>
    public class Building_BackRoomDoor : Building_Door
    {
        public override bool PawnCanOpen(Pawn p)
        {
            if (!base.PawnCanOpen(p)) return false;
            return BackRoom.MayEnter(p);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string extra = "HH.Underworld.BackRoomDoorInfo".Translate() + "\n" + BackRoom.StatusLine(this);
            return s.NullOrEmpty() ? extra : s + "\n" + extra;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            BackRoom.MarkDirty(map);
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            var m = Map;
            base.DeSpawn(mode);
            BackRoom.MarkDirty(m);
        }
    }

    public static class BackRoom
    {
        sealed class MapCache
        {
            public int builtTick = -9999; public bool dirty = true; public HashSet<Room> rooms = new HashSet<Room>(); public HashSet<IntVec3> cells = new HashSet<IntVec3>();
            public List<Building_BackRoomDoor> doors = new List<Building_BackRoomDoor>();
            /// <summary>Per door: status (see <see cref="DoorState"/>) and the size of its back room.</summary>
            public Dictionary<Building_BackRoomDoor, (DoorState state, int cells)> status = new Dictionary<Building_BackRoomDoor, (DoorState, int)>();
        }
        /// <summary>Why a back room door does (not) make a back room. Ordered: the first one that applies to a side wins.</summary>
        public enum DoorState { BackRoom = 0, OrdinaryDoor = 1, OpenToOutside = 2, NoRoom = 3 }
        static readonly Dictionary<int, MapCache> caches = new Dictionary<int, MapCache>();

        public static void MarkDirty(Map map) { if (map != null && caches.TryGetValue(map.uniqueID, out var c)) c.dirty = true; }

        /// <summary>Who may pass: everyone who is not a guest, plus shady guests. Colony pawns, prisoners' wardens, raiders (they break it) are not our business.</summary>
        public static bool MayEnter(Pawn p)
        {
            if (p == null) return true;
            if (p.Faction != null && p.Faction.IsPlayer) return true;
            if (!Underworld.Unlocked) return true;                 // switched off: a plain door
            if (p.HostileTo(Faction.OfPlayer)) return true;       // enemies are not "guests"
            if (!GuestApi.IsGuest(p)) return true;
            return Underworld.IsShady(p);
        }

        static MapCache Cache(Map map)
        {
            if (!caches.TryGetValue(map.uniqueID, out var c)) caches[map.uniqueID] = c = new MapCache();
            int now = Find.TickManager.TicksGame;
            if (c.dirty || now - c.builtTick > 500) Rebuild(map, c, now);
            return c;
        }

        static void Rebuild(Map map, MapCache c, int now)
        {
            c.dirty = false; c.builtTick = now; c.rooms.Clear(); c.doors.Clear(); c.cells.Clear(); c.status.Clear();
            var def = UwDefs.BackRoomDoor; if (def == null) return;
            foreach (var t in map.listerThings.ThingsOfDef(def))
                if (t is Building_BackRoomDoor d && d.Spawned) c.doors.Add(d);
            foreach (var d in c.doors)
            {
                var best = DoorState.NoRoom; int size = 0;
                foreach (var cell in GenAdj.CellsAdjacentCardinal(d))
                {
                    if (!cell.InBounds(map)) continue;
                    var room = cell.GetRoom(map);
                    if (room == null || room.IsDoorway) continue;
                    DoorState k;
                    if (room.PsychologicallyOutdoors) k = DoorState.OpenToOutside;
                    else if (c.rooms.Contains(room) || OnlyBackRoomDoors(room, map))
                    {
                        k = DoorState.BackRoom;
                        if (c.rooms.Add(room)) foreach (var rc in room.Cells) c.cells.Add(rc);
                        size = Math.Max(size, room.CellCount);
                    }
                    else k = DoorState.OrdinaryDoor;
                    if (k < best) best = k;
                }
                c.status[d] = (best, size);
            }
        }

        /// <summary>Does this door make a back room right now, and if not, why not (cached with the back rooms, at most 500 ticks old).</summary>
        public static DoorState StateOf(Building_BackRoomDoor door, out int cells)
        {
            cells = 0;
            if (door == null || !door.Spawned) return DoorState.NoRoom;
            var c = Cache(door.Map);
            if (!c.status.TryGetValue(door, out var st)) { c.dirty = true; c = Cache(door.Map); if (!c.status.TryGetValue(door, out st)) return DoorState.NoRoom; }
            cells = st.cells; return st.state;
        }

        /// <summary>One line for the inspect string and the Underworld tab: "Back room: yes (38 tiles)" or why not.</summary>
        public static string StatusLine(Building_BackRoomDoor door)
        {
            if (!Underworld.Unlocked) return UwPay.T("BackRoom.Off");
            switch (StateOf(door, out int cells))
            {
                case DoorState.BackRoom: return UwPay.T("BackRoom.Yes", cells);
                case DoorState.OrdinaryDoor: return UwPay.T("BackRoom.NoOrdinaryDoor");
                case DoorState.OpenToOutside: return UwPay.T("BackRoom.NoOutside");
                default: return UwPay.T("BackRoom.NoRoom");
            }
        }

        /// <summary>True if the room has at least one door and all of its doors are back room doors.</summary>
        static bool OnlyBackRoomDoors(Room room, Map map)
        {
            int doors = 0;
            foreach (var c in room.BorderCells)
            {
                var d = c.GetDoor(map);
                if (d == null) continue;
                doors++;
                if (!(d is Building_BackRoomDoor)) return false;
            }
            return doors > 0;
        }

        public static bool HasBackRoom(Map map) => map != null && Cache(map).rooms.Count > 0;
        public static bool HasDoor(Map map) => map != null && Cache(map).doors.Count > 0;
        public static IReadOnlyCollection<Room> Rooms(Map map) => map == null ? (IReadOnlyCollection<Room>)new Room[0] : Cache(map).rooms;
        public static List<Building_BackRoomDoor> Doors(Map map) => map == null ? new List<Building_BackRoomDoor>() : Cache(map).doors;

        /// <summary>Is this room hidden behind back room doors? The Inn module skips such rooms when it rates the house (they do not count against renown).</summary>
        public static bool IsBackRoom(Room room) { if (room == null || room.Map == null || room.CellCount == 0) return false; var c = Cache(room.Map); return c.cells.Contains(room.Cells.First()); }
        public static bool IsBackRoomCell(IntVec3 cell, Map map) => map != null && cell.InBounds(map) && Cache(map).cells.Contains(cell);

        /// <summary>A free standable cell inside a back room (for gang members that came to use it).</summary>
        public static bool TryFindCell(Map map, Pawn forPawn, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            foreach (var room in Rooms(map).InRandomOrder())
            {
                var cells = room.Cells.Where(c => c.Standable(map) && c.GetFirstItem(map) == null && (forPawn == null || forPawn.CanReach(c, Verse.AI.PathEndMode.OnCell, Danger.Some))).ToList();
                if (cells.Count > 0) { cell = cells.RandomElement(); return true; }
            }
            return false;
        }

        /// <summary>Respectable (non-shady) guests currently inside or standing at a back room door: they might discover it.</summary>
        public static IEnumerable<Pawn> RespectableWitnesses(Map map)
        {
            foreach (var g in GuestApi.GuestsOn(map))
            {
                if (Underworld.IsShady(g)) continue;
                if (IsBackRoomCell(g.Position, map)) { yield return g; continue; }
                foreach (var d in Doors(map)) if (d.Position.DistanceTo(g.Position) < 4.5f && d.Open) { yield return g; break; }
            }
        }
    }
}
