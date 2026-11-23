// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>Added to every humanlike bed def at startup (no cloned bed defs, 5.11).</summary>
    public sealed class CompProperties_HH_GuestBed : CompProperties
    {
        public CompProperties_HH_GuestBed() { compClass = typeof(CompHH_GuestBed); }
    }

    /// <summary>The "Guest bed" switch on a normal bed. Colonists never use guest beds; guests only use guest beds.</summary>
    public sealed class CompHH_GuestBed : ThingComp
    {
        bool guestBed;
        /// <summary>C8: per-bed price rule on top of the automatic price.</summary>
        public BedPriceMode PriceMode = BedPriceMode.Auto;
        /// <summary>Fixed price in silver per night, used only while PriceMode is Manual (set when a Hospitality fee is taken over).</summary>
        public int ManualPrice;
        static Texture2D Icon => InnTex.GuestBed;

        public Building_Bed Bed => parent as Building_Bed;

        /// <summary>Raw flag (true even while the bed is temporarily medical/prisoner and therefore inactive).</summary>
        public bool Flag => guestBed;

        public bool IsGuestBed
        {
            get
            {
                if (!guestBed) return false;
                var b = Bed;
                return b != null && b.ForColonists && !b.Medical && b.Faction == Faction.OfPlayer;
            }
        }

        public void SetGuestBed(bool on, bool byPlayer = true)
        {
            if (guestBed == on) return;
            guestBed = on;
            var b = Bed;
            if (b == null) return;
            if (on)
            {
                // colonists lose this bed; guests take it from now on
                foreach (var p in b.OwnersForReading.ToList())
                    if (!GuestRegistry.IsRegisteredGuest(p)) p.ownership?.UnclaimBed();
                if (b.Spawned) { GuestBeds.Register(b); GuestWings.Notify_GuestBedAdded(b, byPlayer); }
            }
            else
            {
                foreach (var p in b.OwnersForReading.ToList())
                    if (GuestRegistry.IsRegisteredGuest(p)) GuestRegistry.Instance?.Notify_BedLost(p, b);
                GuestBeds.Unregister(b);
            }
            b.NotifyRoomAssignedPawnsChanged();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (guestBed) GuestBeds.Register(Bed);
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
            GuestBeds.Unregister(Bed, map);
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref guestBed, "hhGuestBed");
            Scribe_Values.Look(ref PriceMode, "hhPriceMode", BedPriceMode.Auto);
            Scribe_Values.Look(ref ManualPrice, "hhManualPrice", 0);
        }

        // ---- combined bed selector protocol v1 (RimWard draws "Use: Colonist / Guest / Patient"; see RimWard README) ----
        /// <summary>Protocol version this mod speaks. RimWard only takes over the bed button when this is >= 1.</summary>
        public static int BedSelectorVersion => 1;
        /// <summary>True when Hearth &amp; Harbor itself draws the selector (not yet): RimWard then only hides its own toggles.</summary>
        public static bool DrawsBedSelector => false;
        /// <summary>The "Guest" entry exists for this bed (Inn module on, colony-owned humanlike bed).</summary>
        public bool SelectorAvailable { get { var b = Bed; return Modules.On(ModuleId.Inn) && b != null && b.Faction == Faction.OfPlayer && b.def.building.bed_humanlike; } }
        /// <summary>Why this bed cannot become a guest bed right now; null when it can (or already is one).</summary>
        public string SelectorBlockedReason
        {
            get
            {
                var b = Bed; if (b == null || guestBed) return null;
                return b.Medical || !b.ForColonists ? "HH.Inn.GuestBed.NotColonistBed".TranslateOr("Only a normal colonist bed (not medical, prisoner or slave) can be a guest bed.") : null;
            }
        }
        static System.Reflection.MethodInfo otherDraws; static bool otherDrawsResolved;
        /// <summary>Another mod (RimWard) draws the combined selector for this bed, so our own toggle stays hidden.</summary>
        public static bool OtherDrawsSelector(Building_Bed bed)
        {
            if (!otherDrawsResolved)
            {
                otherDrawsResolved = true;
                try { otherDraws = GenTypes.GetTypeInAnyAssembly("RimWard.BedSelector")?.GetMethod("Draws", new[] { typeof(Building_Bed) }); } catch (Exception) { }
                if (otherDraws != null) HLog.Info("Inn", "RimWard.BedSelector found: RimWard draws the combined bed button, the Guest bed toggle moves into it");
            }
            if (otherDraws == null) return false;
            try { return (bool)otherDraws.Invoke(null, new object[] { bed }); } catch (Exception) { return false; }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Modules.On(ModuleId.Inn)) yield break;
            var b = Bed;
            if (b == null || b.Faction != Faction.OfPlayer || !b.def.building.bed_humanlike) yield break;
            // RimWard draws the combined bed selector (which includes "Guest"): only our toggle is hidden, the price button stays
            if (!OtherDrawsSelector(b))
            {
            var cmd = new Command_Toggle
            {
                defaultLabel = "HH.Inn.GuestBed.Label".TranslateOr("Guest bed"),
                defaultDesc = "HH.Inn.GuestBed.Desc".TranslateOr("Rent this bed to guests. Colonists will not sleep here; guests only sleep in guest beds. The price per night follows the room, the bed and how many beds share the room."),
                icon = Icon,
                isActive = () => guestBed,
                toggleAction = () => SetGuestBed(!guestBed),
                hotKey = KeyBindingDefOf.Misc5,
            };
            if (!guestBed && (b.Medical || !b.ForColonists))
                cmd.Disable("HH.Inn.GuestBed.NotColonistBed".TranslateOr("Only a normal colonist bed (not medical, prisoner or slave) can be a guest bed."));
            yield return cmd;
            }
            if (!guestBed) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HH.Inn.PriceMode.Gizmo".TranslateOr("Price: {0}").Replace("{0}", PriceModeLabel(PriceMode)),
                defaultDesc = "HH.Inn.PriceMode.GizmoDesc".TranslateOr("Change the price of this bed: free (for friends and recruits), 25 % off, the automatic price, or 25 % more. Applies to the next booking."),
                icon = InnTex.Price,
                action = () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(PriceModeLabel(BedPriceMode.Free), () => SetPriceModeAll(BedPriceMode.Free)),
                    new FloatMenuOption(PriceModeLabel(BedPriceMode.Discount), () => SetPriceModeAll(BedPriceMode.Discount)),
                    new FloatMenuOption(PriceModeLabel(BedPriceMode.Auto), () => SetPriceModeAll(BedPriceMode.Auto)),
                    new FloatMenuOption(PriceModeLabel(BedPriceMode.Premium), () => SetPriceModeAll(BedPriceMode.Premium)),
                })),
            };
        }

        /// <summary>Applies the mode to every selected guest bed (multi-select works like vanilla gizmos).</summary>
        void SetPriceModeAll(BedPriceMode m)
        {
            bool any = false;
            foreach (var o in Find.Selector.SelectedObjectsListForReading)
                if (o is Thing t && GuestBeds.Comp(t) is CompHH_GuestBed c && c.guestBed) { c.PriceMode = m; any = true; }
            if (!any) PriceMode = m;
        }

        public static string PriceModeLabel(BedPriceMode m)
        {
            switch (m)
            {
                case BedPriceMode.Free: return "HH.Inn.PriceMode.Free".TranslateOr("free");
                case BedPriceMode.Discount: return "HH.Inn.PriceMode.Discount".TranslateOr("−25 %");
                case BedPriceMode.Premium: return "HH.Inn.PriceMode.Premium".TranslateOr("+25 %");
                case BedPriceMode.Manual: return "HH.Inn.PriceMode.Manual".TranslateOr("fixed price");
                default: return "HH.Inn.PriceMode.Auto".TranslateOr("automatic");
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!guestBed || !Modules.On(ModuleId.Inn) || !parent.Spawned) return null;
            if (!IsGuestBed) return "HH.Inn.GuestBed.Inactive".TranslateOr("Guest bed (inactive while medical, prisoner or slave bed)");
            var q = GuestBedPricing.Quote(Bed);
            if (q == null) return "HH.Inn.GuestBed.Label".TranslateOr("Guest bed");
            string s = "HH.Inn.GuestBed.Inspect".TranslateOr("Guest bed: {0} silver/night ({1})")
                .Replace("{0}", q.Price.WholeSilver.ToString()).Replace("{1}", q.Breakdown);
            string wing = WingStatus.LineAt(parent.Map, parent.Position);
            if (wing != null) s += "\n" + wing;
            // B7: who comes next (only while the bed is free: an occupied bed shows its guest instead)
            var owner = Bed.OwnersForReading.FirstOrDefault(o => GuestRegistry.IsRegisteredGuest(o));
            if (owner != null) s += "\n" + "HH.Inn.GuestBed.Booked".TranslateOr("Booked by {0}").Replace("{0}", owner.LabelShortCap);
            else
            {
                var next = parent.Map.GetComponent<MapComponent_HH_GuestScheduler>()?.NextArrival();
                if (next != null) s += "\n" + "HH.Inn.Desk.Next".TranslateOr("Next guests: {0}").Replace("{0}", next.Describe(parent.Map));
            }
            return s;
        }
    }

    /// <summary>Fast lookup of active guest beds per map (5.1: hot patches only do a HashSet check).</summary>
    public static class GuestBeds
    {
        static readonly HashSet<int> ids = new HashSet<int>();
        static readonly Dictionary<int, List<Building_Bed>> byMap = new Dictionary<int, List<Building_Bed>>();

        public static int TotalCount => ids.Count;

        public static void Register(Building_Bed b)
        {
            if (b?.Map == null) return;
            if (!ids.Add(b.thingIDNumber)) return;
            if (!byMap.TryGetValue(b.Map.uniqueID, out var l)) byMap[b.Map.uniqueID] = l = new List<Building_Bed>();
            if (!l.Contains(b)) l.Add(b);
        }

        public static void Unregister(Building_Bed b, Map map = null)
        {
            if (b == null) return;
            ids.Remove(b.thingIDNumber);
            map = map ?? b.Map;
            if (map != null && byMap.TryGetValue(map.uniqueID, out var l)) l.Remove(b);
            else foreach (var kv in byMap) kv.Value.Remove(b);
        }

        public static void ClearAll() { ids.Clear(); byMap.Clear(); }

        /// <summary>Cheap: is this thing a registered guest bed? (does not re-check medical/prisoner flags)</summary>
        public static bool IsRegistered(Thing t) => t != null && ids.Count > 0 && ids.Contains(t.thingIDNumber);

        public static bool IsGuestBed(Thing t)
        {
            if (!IsRegistered(t)) return false;
            var c = t.TryGetComp<CompHH_GuestBed>();
            return c != null && c.IsGuestBed;
        }

        public static List<Building_Bed> On(Map map)
        {
            var res = new List<Building_Bed>();
            if (map == null || !byMap.TryGetValue(map.uniqueID, out var l)) return res;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                var b = l[i];
                if (b == null || b.Destroyed || !b.Spawned || b.Map != map) { if (b != null) ids.Remove(b.thingIDNumber); l.RemoveAt(i); continue; }
                if (IsGuestBed(b)) res.Add(b);
            }
            return res;
        }

        public static int CountOn(Map map) => On(map).Count;

        public static bool IsFree(Building_Bed b) => b.OwnersForReading.Count == 0 && !b.AnyOccupants;

        /// <summary>C4: a multi-slot guest bed whose only owner is this guest's partner (lover, fiancé, spouse) staying in the same group.</summary>
        public static bool SharableWith(Building_Bed b, Pawn guest, int groupId)
        {
            if (b == null || guest == null || b.SleepingSlotsCount < 2 || b.OwnersForReading.Count != 1) return false;
            var other = b.OwnersForReading[0];
            if (other == guest || !GuestRegistry.IsRegisteredGuest(other)) return false;
            var r = GuestRegistry.Instance?.Get(other);
            if (r == null || r.GroupId != groupId || r.Bed != b || r.Settled) return false;
            if (!LovePartnerRelationUtility.LovePartnerRelationExists(guest, other)) return false;
            // vanilla's own rule: some ideologies only share a bed with a spouse
            return b.CompAssignableToPawn == null || !b.CompAssignableToPawn.IdeoligionForbids(guest);
        }

        public static List<Building_Bed> FreeOn(Map map) => On(map).Where(IsFree).ToList();

        public static CompHH_GuestBed Comp(Thing t) => t?.TryGetComp<CompHH_GuestBed>();
    }

    /// <summary>C8: price rule of one guest bed.</summary>
    public enum BedPriceMode { Auto = 0, Free = 1, Discount = 2, Premium = 3, Manual = 4 }

    /// <summary>A computed price for one guest bed (5.11).</summary>
    public sealed class BedQuote
    {
        public Money Price;
        public float RoomScore;
        public string ClassKey, ClassLabel, OccupancyLabel, QualityLabel;
        public float ClassMult, Occupancy, QualityMult, StarMult;
        public BedPriceMode Mode = BedPriceMode.Auto;
        public int Beds, Cells, Furniture;
        public string RoomName;
        public string Breakdown => $"{ClassLabel} · {OccupancyLabel} ×{Occupancy:0.00} · {QualityLabel} ×{QualityMult:0.00}" + (Math.Abs(StarMult - 1f) > 0.001f ? $" · {GuestRegistry.StarsText()} ×{StarMult:0.00}" : "")
                                   + (Mode != BedPriceMode.Auto ? " · " + CompHH_GuestBed.PriceModeLabel(Mode) : "");
    }

    /// <summary>
    /// Room score RS (0–100) and price per night (5.11):
    /// price = base × room class (economy 1 / standard 1.5 / deluxe 2.5 / suite 4 / royal 7) × occupancy (single 1.35 / two-bed 1.10 / dorm 0.85 /
    /// hallway 0.72) × bed quality × (1 + 0.1·(stars − 3)), rounded to 5; then the bed's own price rule (free / −25 % / +25 %).
    /// Results are cached per room for 250 ticks.
    /// </summary>
    public static class GuestBedPricing
    {
        sealed class RoomCache { public int Tick; public float Score; public int Beds, Cells, Furniture; public bool Outdoors; }
        static readonly Dictionary<int, RoomCache> cache = new Dictionary<int, RoomCache>();

        public static readonly (string key, string label, float minScore, int minFurniture, int minCells, float mult)[] Classes =
        {
            ("Royal", "royal suite", 90f, 4, 30, 7f),
            ("Suite", "suite", 80f, 3, 25, 4f),
            ("Deluxe", "deluxe room", 60f, 2, 15, 2.5f),
            ("Standard", "standard room", 40f, 1, 0, 1.5f),
            ("Economy", "economy room", 0f, 0, 0, 1f),
        };

        public static float QualityFactor(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 0.75f;
                case QualityCategory.Poor: return 0.85f;
                case QualityCategory.Normal: return 1f;
                case QualityCategory.Good: return 1.1f;
                case QualityCategory.Excellent: return 1.2f;
                case QualityCategory.Masterwork: return 1.35f;
                case QualityCategory.Legendary: return 1.5f;
                default: return 1f;
            }
        }

        static float StageNorm(Room room, RoomStatDef stat, int divisor)
        {
            if (stat?.scoreStages == null || stat.scoreStages.Count == 0) return 0.5f;
            int idx = stat.GetScoreStageIndex(room.GetStat(stat));
            int d = divisor > 0 ? divisor : Math.Max(1, stat.scoreStages.Count - 1);
            return Mathf.Clamp01(idx / (float)d);
        }

        static RoomCache Evaluate(Room room)
        {
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(room.ID, out var c) && now - c.Tick < 250) return c;
            c = new RoomCache { Tick = now };
            c.Outdoors = room.PsychologicallyOutdoors || room.TouchesMapEdge;
            c.Cells = room.CellCount;
            foreach (var t in room.ContainedAndAdjacentThings)
            {
                if (t is Building_Bed bb) { if (bb.def.building.bed_humanlike && bb.GetRoom() == room) c.Beds++; continue; }
                if (!(t is Building) || t.GetRoom() != room) continue;
                if (t.def.IsArt || t.TryGetComp<CompFacility>() != null || t.def.building?.joyKind != null
                    || t.def.GetStatValueAbstract(StatDefOf.Comfort) > 0.01f || t.def.IsTable)
                    c.Furniture++;
            }
            if (c.Outdoors) { c.Score = 5f; cache[room.ID] = c; return c; }
            float imp = StageNorm(room, RoomStatDefOf.Impressiveness, 7);
            float space = StageNorm(room, RoomStatDefOf.Space, 6);
            float clean = StageNorm(room, RoomStatDefOf.Cleanliness, 0);
            float beauty = StageNorm(room, RoomStatDefOf.Beauty, 0);
            float temp = room.Temperature >= 10f && room.Temperature <= 30f ? 1f : room.Temperature >= 0f && room.Temperature <= 38f ? 0.5f : 0f;
            // bath (10 points) only counts with a bath mod, and only for a room that has one (B9: no free points without it)
            float bath = BathModActive && HasBath(room) ? 1f : 0f;
            float score = 35f * imp + 15f * space + 15f * clean + 10f * Mathf.Min(1f, c.Furniture / 3f)
                          + 10f * bath + 5f * beauty + 5f * 0.5f /* ideology style: neutral */ + 5f * temp;
            c.Score = Mathf.Clamp(score, 0f, 100f);
            cache[room.ID] = c;
            return c;
        }

        static bool? bathMod;
        public static bool BathModActive => bathMod ?? (bathMod = ModsConfig.IsActive("Dubwise.DubsBadHygiene") || ModsConfig.IsActive("Dubwise.DubsBadHygiene.Lite")).Value;

        static bool HasBath(Room room)
        {
            foreach (var t in room.ContainedAndAdjacentThings)
            {
                if (!(t is Building) || t.GetRoom() != room) continue;
                string n = t.def.defName;
                if (n.IndexOf("Toilet", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Shower", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Bath", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Tub", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static float RoomScore(Room room) => room == null ? 0f : Evaluate(room).Score;

        public static BedQuote Quote(Building_Bed bed)
        {
            if (bed == null || !bed.Spawned) return null;
            var room = bed.GetRoom();
            if (room == null) return null;
            var rc = Evaluate(room);
            var q = new BedQuote { RoomScore = rc.Score, Beds = Math.Max(1, rc.Beds), Cells = rc.Cells, Furniture = rc.Furniture };
            var cls = Classes[Classes.Length - 1];
            if (!rc.Outdoors)
                foreach (var k in Classes)
                    if (rc.Score >= k.minScore && rc.Furniture >= k.minFurniture && rc.Cells >= k.minCells) { cls = k; break; }
            q.ClassKey = cls.key; q.ClassMult = cls.mult;
            q.ClassLabel = ("HH.Inn.RoomClass." + cls.key).TranslateOr(cls.label);
            if (rc.Outdoors) { q.Occupancy = 0.72f; q.OccupancyLabel = "HH.Inn.Occupancy.Hallway".TranslateOr("hallway"); }
            else if (q.Beds >= 3) { q.Occupancy = 0.85f; q.OccupancyLabel = "HH.Inn.Occupancy.Dorm".TranslateOr("dorm"); }
            else if (q.Beds == 2 || rc.Cells < 9) { q.Occupancy = 1.10f; q.OccupancyLabel = "HH.Inn.Occupancy.TwoBed".TranslateOr("two-bed"); }
            else { q.Occupancy = 1.35f; q.OccupancyLabel = "HH.Inn.Occupancy.Single".TranslateOr("single"); }
            if (bed.TryGetQuality(out var qc)) { q.QualityMult = QualityFactor(qc); q.QualityLabel = qc.GetLabel(); }
            else { q.QualityMult = 1f; q.QualityLabel = "HH.Inn.Quality.Plain".TranslateOr("plain"); }
            q.StarMult = 1f + 0.1f * (GuestRegistry.Stars - 3);
            float baseP = HSettings.Get(InnSettings.PriceBase);
            float raw = baseP * q.ClassMult * q.Occupancy * q.QualityMult * q.StarMult;
            float rounded = Math.Max(5f, (float)Math.Round(raw / 5f) * 5f);
            q.Mode = GuestBeds.Comp(bed)?.PriceMode ?? BedPriceMode.Auto;
            if (q.Mode == BedPriceMode.Free) rounded = 0f;
            else if (q.Mode == BedPriceMode.Discount) rounded = Math.Max(5f, (float)Math.Round(rounded * 0.75f / 5f) * 5f);
            else if (q.Mode == BedPriceMode.Premium) rounded = Math.Max(5f, (float)Math.Round(rounded * 1.25f / 5f) * 5f);
            else if (q.Mode == BedPriceMode.Manual) rounded = Math.Max(0, GuestBeds.Comp(bed)?.ManualPrice ?? 0);
            q.Price = Money.FromSilver((int)rounded);
            q.RoomName = room.Role != null && room.Role != RoomRoleDefOf.None ? room.GetRoomRoleLabel() : q.ClassLabel;
            return q;
        }

        public static void ClearCache() => cache.Clear();
    }
}
