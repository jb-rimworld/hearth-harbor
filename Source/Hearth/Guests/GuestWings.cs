// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    /// <summary>
    /// A guest wing (5.8). An Area, but kept in our own map component instead of the vanilla area manager:
    /// it never counts against the vanilla area limit, never shows up in colonist area restrictions,
    /// and removing the mod cannot leave an unknown area class in the save.
    /// </summary>
    public sealed class Area_HH_GuestWing : Area
    {
        string label;
        Color color = new Color(0.95f, 0.72f, 0.35f);
        CellBoolDrawer wingDrawer;
        List<IntVec3> cellCache; int cacheTick = -1;

        public Area_HH_GuestWing() { }
        public Area_HH_GuestWing(AreaManager am, string label) : base(am) { this.label = label; }
        /// <summary>For importers (Hospitality guest areas): a wing without a name yet. Put it into the map's wing list, or into the vanilla
        /// area list, from where <see cref="MapComponent_HH_GuestWings"/> moves it over within 250 ticks.</summary>
        public Area_HH_GuestWing(AreaManager am) : base(am) { }

        public override string Label => label;
        public override Color Color => color;
        public override int ListPriority => 450;
        public override bool Mutable => true;
        public override bool AssignableAsAllowed() => false;
        public override string RenamableLabel { get => label ?? BaseLabel; set => label = value; }
        public override string BaseLabel => "HH.Inn.Wing.Base".TranslateOr("Guest Wing");
        public override string GetUniqueLoadID() => "Area_HH_GuestWing_" + ID;
        public string WingId => "wing" + ID;

        CellBoolDrawer Drawer => wingDrawer ?? (wingDrawer = new CellBoolDrawer(this, Map.Size.x, Map.Size.z, 3651));

        /// <summary>Writes straight into the grid: no vanilla path-cost or region notifications (it is not a vanilla area).</summary>
        protected override void Set(IntVec3 c, bool val)
        {
            int idx = Map.cellIndices.CellToIndex(c);
            if (InnerGrid[idx] == val) return;
            InnerGrid[idx] = val;
            wingDrawer?.SetDirty();
            cacheTick = -1;
        }

        public void DrawNow() { if (Map == Find.CurrentMap) { Drawer.MarkForDraw(); Drawer.CellBoolDrawerUpdate(); } }

        public List<IntVec3> CellsCached()
        {
            int now = Find.TickManager.TicksGame;
            if (cellCache == null || cacheTick < 0 || now - cacheTick > 2500) { cellCache = ActiveCells.ToList(); cacheTick = now; }
            return cellCache;
        }

        public IntVec3 RandomCellFor(Pawn p)
        {
            var cells = CellsCached();
            if (cells.Count == 0) return IntVec3.Invalid;
            for (int i = 0; i < 12; i++)
            {
                var c = cells[Rand.Range(0, cells.Count)];
                if (c.Standable(Map) && !c.IsForbidden(p) && c.GetFirstPawn(Map) == null && p.CanReach(c, PathEndMode.OnCell, Danger.None)) return c;
            }
            return IntVec3.Invalid;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref color, "color", new Color(0.95f, 0.72f, 0.35f));
        }
    }

    /// <summary>Stores the wings of one map, draws them while a wing tool is selected.</summary>
    public sealed class MapComponent_HH_GuestWings : MapComponent
    {
        public List<Area_HH_GuestWing> wings = new List<Area_HH_GuestWing>();
        public Area_HH_GuestWing selected;
        public bool drawRequested;

        public MapComponent_HH_GuestWings(Map map) : base(map) { }

        public bool AnyWing { get { for (int i = 0; i < wings.Count; i++) if (wings[i].TrueCount > 0) return true; return false; } }

        public Area_HH_GuestWing NewWing()
        {
            int n = wings.Count + 1;
            var w = new Area_HH_GuestWing(map.areaManager, "HH.Inn.Wing.Name".TranslateOr("Guest Wing {0}").Replace("{0}", n.ToString()));
            wings.Add(w);
            return w;
        }

        /// <summary>Wings that an importer put into the vanilla area list move into ours (they never count as vanilla areas).</summary>
        public int AdoptForeignWings()
        {
            int n = 0;
            var all = map.areaManager.AllAreas;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (!(all[i] is Area_HH_GuestWing w)) continue;
                all.RemoveAt(i);
                if (w.RenamableLabel.NullOrEmpty() || w.RenamableLabel == w.BaseLabel) w.RenamableLabel = "HH.Inn.Wing.Name".TranslateOr("Guest Wing {0}").Replace("{0}", (wings.Count + 1).ToString());
                if (!wings.Contains(w)) wings.Add(w);
                HLog.Info("Inn", $"Guest wing '{w.Label}' adopted from the area list ({w.TrueCount} cells)");
                n++;
            }
            if (n > 0) WingStatus.ClearCache();
            return n;
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 77) return;
            Guard.Run(ModuleId.Inn, "Inn.AdoptWings", () => AdoptForeignWings());
        }

        public override void FinalizeInit() { base.FinalizeInit(); Guard.Run(ModuleId.Inn, "Inn.AdoptWings", () => AdoptForeignWings()); }

        public override void MapComponentUpdate()
        {
            if (!drawRequested) return;
            drawRequested = false;
            foreach (var w in wings) w.DrawNow();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref wings, "wings", LookMode.Deep);
            Scribe_References.Look(ref selected, "selected");
            if (Scribe.mode == LoadSaveMode.LoadingVars && wings != null) foreach (var w in wings) if (w != null) w.areaManager = map.areaManager;
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { wings = wings ?? new List<Area_HH_GuestWing>(); wings.RemoveAll(w => w == null); foreach (var w in wings) w.areaManager = map.areaManager; }
        }
    }

    public static class GuestWings
    {
        public static MapComponent_HH_GuestWings Comp(Map map) => map?.GetComponent<MapComponent_HH_GuestWings>();

        /// <summary>The wing the guest is bound to: the one containing their bed, else the first non-empty wing.</summary>
        public static Area_HH_GuestWing WingFor(Pawn p)
        {
            var c = Comp(p?.Map); if (c == null || c.wings.Count == 0) return null;
            var r = GuestRegistry.Instance?.Get(p);
            if (r != null && !r.WingId.NullOrEmpty())
                foreach (var w in c.wings) if (w.WingId == r.WingId && w.TrueCount > 0) return w;
            if (r?.Bed != null && r.Bed.Spawned)
                foreach (var w in c.wings) if (w.TrueCount > 0 && w[r.Bed.Position]) { r.WingId = w.WingId; return w; }
            foreach (var w in c.wings) if (w.TrueCount > 0) return w;
            return null;
        }

        /// <summary>Cell check used by the InAllowedArea patch.</summary>
        public static bool AllowedFor(Pawn p, IntVec3 c)
        {
            var w = WingFor(p);
            if (w == null) return true;
            if (!c.InBounds(p.Map)) return true;
            if (w[c]) return true;
            // a guest's own bed and the front desk are always allowed
            var r = GuestRegistry.Instance?.Get(p);
            if (r?.Bed != null && r.Bed.OccupiedRect().Contains(c)) return true;
            // guests may shop in every open venue room, inside a wing or not (4.5)
            var room = c.GetRoom(p.Map);
            if (room != null && !room.PsychologicallyOutdoors)
            {
                var venues = Venues.All(p.Map);
                for (int i = 0; i < venues.Count; i++) if (venues[i].Room == room) return true;
            }
            return false;
        }

        public static void Notify_GuestBedAdded(Building_Bed b, bool byPlayer)
        {
            if (b?.Map == null || !HSettings.GetBool(InnSettings.AutoWing)) return;
            EnsureWingFor(b, announce: byPlayer);
        }

        /// <summary>
        /// "It just works" (4.5, 5.8, N31): the first guest bed creates Guest Wing 1 from the bed's room, rooms with venue signs
        /// reachable through doors, and the corridors between them. Later guest rooms join the nearest wing. Never colonist bedrooms or prisons.
        /// </summary>
        public static void EnsureWingFor(Building_Bed b, bool announce = false)
        {
            if (b?.Map == null || !HSettings.GetBool(InnSettings.AutoWing)) return;
            var map = b.Map; var comp = Comp(map); if (comp == null) return;
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || room.IsHuge || room.IsPrisonCell) return;
            foreach (var w in comp.wings) if (w.TrueCount > 0 && w[b.Position]) return; // already in a wing
            bool created = false;
            Area_HH_GuestWing wing = comp.wings.FirstOrDefault(w => w.TrueCount > 0);
            if (wing == null) { wing = comp.wings.FirstOrDefault() ?? comp.NewWing(); created = true; }
            var rooms = CollectRooms(room, map);
            int added = 0;
            foreach (var rm in rooms)
                foreach (var c in rm.Cells) if (!wing[c]) { wing[c] = true; added++; }
            if (added > 0) HLog.Info("Inn", $"{wing.Label}: added {added} cells around {b.LabelShort} ({rooms.Count} rooms)");
            if (created && announce && added > 0)
                Messages.Message("HH.Inn.Msg.WingCreated".TranslateOr("{0} created around your guest bed. Guests stay inside it. Adjust it with the guest wing tools in the Zone menu.").Replace("{0}", wing.Label),
                    new LookTargets(b), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>BFS through doors (≤ 4 steps): bed room, venue rooms, and the corridors leading to them.</summary>
        static List<Room> CollectRooms(Room start, Map map)
        {
            var result = new List<Room> { start };
            var venueRooms = new HashSet<Room>();
            foreach (var v in Venues.All(map)) if (v.Room != null) venueRooms.Add(v.Room);
            var prev = new Dictionary<Room, Room> { [start] = null };
            var queue = new Queue<(Room r, int d)>(); queue.Enqueue((start, 0));
            while (queue.Count > 0)
            {
                var (r, d) = queue.Dequeue();
                if (d >= 4) continue;
                foreach (var n in NeighbourRooms(r))
                {
                    if (prev.ContainsKey(n)) continue;
                    if (n.IsPrisonCell || n.PsychologicallyOutdoors || n.IsHuge) continue;
                    if (IsColonistBedroom(n)) continue;
                    prev[n] = r;
                    if (venueRooms.Contains(n))
                        for (var x = n; x != null && !result.Contains(x); x = prev[x]) result.Add(x);
                    queue.Enqueue((n, d + 1));
                }
            }
            return result;
        }

        static bool IsColonistBedroom(Room r)
        {
            foreach (var b in r.ContainedBeds) if (b.def.building.bed_humanlike && !GuestBeds.IsGuestBed(b) && !b.Medical) return true;
            return false;
        }

        /// <summary>Rooms on the other side of doors of this room (doors themselves are their own tiny rooms).</summary>
        static IEnumerable<Room> NeighbourRooms(Room r)
        {
            var seen = new HashSet<Room>();
            foreach (var reg in r.Regions)
                foreach (var link in reg.links)
                {
                    var other = link.GetOtherRegion(reg);
                    if (other == null) continue;
                    var or = other.Room;
                    if (or == null || or == r) continue;
                    if (or.IsDoorway)
                    {
                        if (seen.Add(or)) yield return or;
                        foreach (var dreg in or.Regions)
                            foreach (var l2 in dreg.links)
                            {
                                var o2 = l2.GetOtherRegion(dreg)?.Room;
                                if (o2 != null && o2 != r && o2 != or && seen.Add(o2)) yield return o2;
                            }
                    }
                    else if (seen.Add(or)) yield return or;
                }
        }
    }

    // ======================================================================= designators (Zone menu)

    public abstract class Designator_HH_GuestWing : Designator_Cells
    {
        readonly bool add;
        public override bool DragDrawMeasurements => true;
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;

        protected Designator_HH_GuestWing(bool add)
        {
            this.add = add;
            soundDragSustain = SoundDefOf.Designate_DragAreaAdd;
            soundDragChanged = SoundDefOf.Designate_DragZone_Changed;
            soundSucceeded = add ? SoundDefOf.Designate_ZoneAdd : SoundDefOf.Designate_ZoneDelete;
            useMouseIcon = true;
        }

        public override bool Visible => Modules.On(ModuleId.Inn) && base.Visible;

        Area_HH_GuestWing Target(bool create)
        {
            var comp = GuestWings.Comp(Map); if (comp == null) return null;
            var w = comp.selected ?? comp.wings.FirstOrDefault();
            if (w == null && create) w = comp.NewWing();
            comp.selected = w;
            return w;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map)) return false;
            var w = Target(false);
            if (w == null) return add;
            return add ? !w[c] : w[c];
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            var w = Target(add); if (w == null) return;
            w[c] = add;
        }

        public override void ProcessInput(Event ev)
        {
            var comp = GuestWings.Comp(Map);
            if (comp != null && comp.wings.Count > 1)
            {
                var opts = comp.wings.Select(w => new FloatMenuOption(w.Label, () => { comp.selected = w; base.ProcessInput(ev); })).ToList();
                if (add) opts.Add(new FloatMenuOption("HH.Inn.Wing.New".TranslateOr("New guest wing"), () => { comp.selected = comp.NewWing(); base.ProcessInput(ev); }));
                Find.WindowStack.Add(new FloatMenu(opts));
                return;
            }
            base.ProcessInput(ev);
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
            var comp = GuestWings.Comp(Map); if (comp != null) comp.drawRequested = true;
        }
    }

    public sealed class Designator_HH_GuestWingExpand : Designator_HH_GuestWing
    {
        public Designator_HH_GuestWingExpand() : base(true)
        {
            defaultLabel = "HH.Inn.Wing.Expand".TranslateOr("Expand guest wing");
            defaultDesc = "HH.Inn.Wing.ExpandDesc".TranslateOr("Paint where guests may go. Guests stay inside their wing (the bed's room is always allowed). Without any wing, guests wander near their rooms.");
            icon = ContentFinder<Texture2D>.Get("UI/HH/Inn/GuestWingExpand", false) ?? BaseContent.BadTex;
        }
    }

    public sealed class Designator_HH_GuestWingClear : Designator_HH_GuestWing
    {
        public Designator_HH_GuestWingClear() : base(false)
        {
            defaultLabel = "HH.Inn.Wing.Clear".TranslateOr("Shrink guest wing");
            defaultDesc = "HH.Inn.Wing.ClearDesc".TranslateOr("Remove cells from the guest wing.");
            icon = ContentFinder<Texture2D>.Get("UI/HH/Inn/GuestWingClear", false) ?? BaseContent.BadTex;
        }
    }
}
