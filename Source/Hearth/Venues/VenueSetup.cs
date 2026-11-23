// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    /// <summary>One evaluated line of a venue's setup checklist (F12).</summary>
    public sealed class SetupRow
    {
        public VenueRequirement Req;
        public int Have, Need, Planned;
        public string Note;
        public bool Ok => Have >= Need;
        /// <summary>Missing and keeps the venue closed.</summary>
        public bool Blocking => !Ok && Req.Blocks;
        public string Label => Req.label.CapitalizeFirst();
        /// <summary>"2 / 1+" style count; empty for yes/no checks.</summary>
        public string CountText => Need <= 0 ? "" : Have + " / " + Need;
    }

    /// <summary>Evaluated checklist of one venue, cached for a short while.</summary>
    public sealed class SetupReport
    {
        public VenueTypeDef Type;
        public readonly List<SetupRow> Rows = new List<SetupRow>();
        public int Tick;
        /// <summary>No required structural item is missing.</summary>
        public bool Complete => !Rows.Any(r => r.Blocking);
        public int RequiredMissing => Rows.Count(r => r.Req.required && !r.Ok);
        public int RecommendedMissing => Rows.Count(r => !r.Req.required && !r.Ok);
        public IEnumerable<SetupRow> Required => Rows.Where(r => r.Req.required);
        public IEnumerable<SetupRow> Recommended => Rows.Where(r => !r.Req.required);
        public SetupRow Row(string id) => Rows.FirstOrDefault(r => r.Req.id == id);
        public IEnumerable<SetupRow> Blockers => Rows.Where(r => r.Blocking);
    }

    /// <summary>
    /// Venue setup (F12, 1.1.0): what a venue type needs (XML <see cref="VenueSetupExtension"/>), what the room has, and whether the venue
    /// is ready to open. A venue with missing required furniture is "Not ready" and stays closed while the setting venues.requireSetup is on.
    /// Counting walks the room's cells (things there) and is cached per sign for 250 ticks.
    /// </summary>
    public static class VenueSetup
    {
        public const string RequireKey = "venues.requireSetup";
        public const string HintsKey = "venues.setupHints";
        public const int CacheTicks = 250;

        static readonly Dictionary<int, SetupReport> cache = new Dictionary<int, SetupReport>();
        // last known "complete" per sign (runtime): a venue that becomes complete says so once (message + shop bell)
        static readonly Dictionary<int, bool> lastComplete = new Dictionary<int, bool>();
        static readonly Dictionary<VenueTypeDef, List<VenueRequirement>> fallback = new Dictionary<VenueTypeDef, List<VenueRequirement>>();
        static VenueSetup() { HearthGame.OnGameInit += Clear; }

        public static void Clear() { cache.Clear(); lastComplete.Clear(); desCache.Clear(); buildDefsCache.Clear(); }
        public static void MarkDirty(Thing sign) { if (sign != null) cache.Remove(sign.thingIDNumber); }

        /// <summary>Is the venue allowed to open? True when everything required stands in the room, or when the player switched the rule off.</summary>
        public static bool IsReady(Venue v)
        {
            if (v?.Type == null) return false;
            // Shops & Food off for this save: the setup rule is part of it, so no venue is held back by it
            if (!Modules.On(ModuleId.ShopsFood)) return true;
            bool require;
            try { require = HSettings.GetBool(RequireKey); } catch (Exception) { require = true; }
            if (!require) return true;
            return Report(v).Complete;
        }

        /// <summary>The checklist of a venue. <paramref name="maxAge"/> ticks old at most (the setup tab asks for fresher data).</summary>
        public static SetupReport Report(Venue v, int maxAge = CacheTicks)
        {
            if (v?.Sign == null) return new SetupReport();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (cache.TryGetValue(v.Sign.thingIDNumber, out var r) && r.Type == v.Type && now >= r.Tick && now - r.Tick < maxAge) return r;
            r = Evaluate(v);
            r.Tick = now;
            int id = v.Sign.thingIDNumber;
            cache[id] = r;
            bool complete = r.Complete && r.Rows.Any(x => x.Req.Blocks);
            if (lastComplete.TryGetValue(id, out bool was) && !was && complete) AnnounceReady(v);
            lastComplete[id] = r.Complete;
            return r;
        }

        /// <summary>The requirements of a type: its XML extension, or a small built-in list (stock, staff) for types of other mods without one.</summary>
        public static List<VenueRequirement> RequirementsOf(VenueTypeDef t)
        {
            if (t == null) return new List<VenueRequirement>();
            var ext = t.GetModExtension<VenueSetupExtension>();
            if (ext?.requirements != null && ext.requirements.Count > 0) return ext.requirements;
            if (fallback.TryGetValue(t, out var list)) return list;
            list = new List<VenueRequirement>();
            var sells = VenueApi.SaleOf(t)?.sells ?? SellKind.None;
            if (sells != SellKind.None) list.Add(new VenueRequirement { id = "stock", check = VenueCheck.Stock, label = "HH.Setup.Stock".TranslateOr("goods for sale"), hint = "HH.Setup.StockHint".TranslateOr("Store what this business sells inside the room.") });
            if (t.needsStaff) list.Add(new VenueRequirement { id = "staff", check = VenueCheck.ServingWorker, label = "HH.Setup.Staff".TranslateOr("someone doing Serving work"), hint = "HH.Setup.StaffHint".TranslateOr("Switch on the Serving work type for at least one colonist.") });
            fallback[t] = list;
            return list;
        }

        // ------------------------------------------------------------------ evaluation

        static readonly List<Thing> roomThings = new List<Thing>();
        static readonly List<Blueprint> roomPlans = new List<Blueprint>();
        static readonly HashSet<int> seen = new HashSet<int>();

        static SetupReport Evaluate(Venue v)
        {
            var rep = new SetupReport { Type = v.Type };
            CollectRoom(v);
            try
            {
                foreach (var req in RequirementsOf(v.Type))
                {
                    if (req == null) continue;
                    var row = Guard.Run(ModuleId.ShopsFood, "VenueSetup.Row." + req.id, () => EvaluateRow(v, req), null);
                    if (row != null) rep.Rows.Add(row);
                }
            }
            finally { roomThings.Clear(); roomPlans.Clear(); }
            return rep;
        }

        static void CollectRoom(Venue v)
        {
            roomThings.Clear(); roomPlans.Clear(); seen.Clear();
            var room = v.Room; var map = v.Map;
            if (room == null || map == null) return;
            foreach (var c in room.Cells)
            {
                var list = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (!seen.Add(t.thingIDNumber)) continue;
                    if (t is Blueprint bp) { if (bp.Faction == Faction.OfPlayer) roomPlans.Add(bp); continue; }
                    if (t.def.category != ThingCategory.Building || t == v.Sign) continue;
                    if (t.Faction != null && t.Faction != Faction.OfPlayer && t.Faction.HostileTo(Faction.OfPlayer)) continue;
                    roomThings.Add(t);
                }
            }
            seen.Clear();
        }

        static int MinOf(VenueRequirement r)
        {
            if (!r.minSetting.NullOrEmpty())
            {
                try { return Math.Max(0, HSettings.GetInt(r.minSetting)); }
                catch (Exception) { HLog.Once("venues.setup.min." + r.minSetting, LogLevel.Warn, "Venues", "Setup: unknown min setting " + r.minSetting + ", using " + r.min); }
            }
            return Math.Max(0, r.min);
        }

        /// <summary>Does a def count for a Things/Power/SeatsFacing requirement?</summary>
        public static bool Matches(VenueRequirement r, ThingDef d)
        {
            if (d == null) return false;
            if (r.things != null && r.things.Contains(d)) return true;
            if (r.compProps != null && d.comps != null)
                for (int i = 0; i < d.comps.Count; i++) if (r.compProps.IsAssignableFrom(d.comps[i].GetType())) return true;
            if (r.thingClass != null && d.thingClass != null && r.thingClass.IsAssignableFrom(d.thingClass)) return true;
            return false;
        }

        /// <summary>Does a def count for this requirement (the kinds that count buildings)?</summary>
        public static bool Counts(VenueRequirement r, ThingDef d)
        {
            if (d == null) return false;
            switch (r.check)
            {
                case VenueCheck.Things: case VenueCheck.Power: case VenueCheck.SeatsFacing: return Matches(r, d);
                case VenueCheck.Seats: return d.building != null && d.building.isSittable;
                case VenueCheck.Tables: return d.surfaceType == SurfaceType.Eat;
                case VenueCheck.Art: return d.HasComp(typeof(CompArt));
                case VenueCheck.Storage: return d.building != null && d.building.defaultStorageSettings != null && typeof(IStoreSettingsParent).IsAssignableFrom(d.thingClass);
                default: return false;
            }
        }

        static SetupRow EvaluateRow(Venue v, VenueRequirement r)
        {
            var row = new SetupRow { Req = r, Need = MinOf(r) };
            switch (r.check)
            {
                case VenueCheck.Things:
                case VenueCheck.Seats:
                case VenueCheck.Tables:
                case VenueCheck.Art:
                    for (int i = 0; i < roomThings.Count; i++) if (Counts(r, roomThings[i].def)) row.Have++;
                    break;
                case VenueCheck.Storage:
                    row.Have = VenueRestock.StorageOf(v).Count;
                    break;
                case VenueCheck.SeatsFacing:
                {
                    int best = 0, screens = 0;
                    for (int i = 0; i < roomThings.Count; i++)
                    {
                        var t = roomThings[i];
                        if (!Matches(r, t.def)) continue;
                        screens++;
                        int n = 0;
                        foreach (var c in WatchBuildingUtility.CalculateWatchCells(t.def, t.Position, t.Rotation, t.Map))
                        {
                            var e = c.GetEdifice(t.Map);
                            if (e != null && e.def.building != null && e.def.building.isSittable) n++;
                        }
                        if (n > best) best = n;
                    }
                    row.Have = best;
                    if (screens == 0) row.Note = "HH.Setup.NoScreenYet".TranslateOr("build the screen first");
                    break;
                }
                case VenueCheck.Power:
                {
                    int total = 0, on = 0;
                    for (int i = 0; i < roomThings.Count; i++)
                    {
                        var t = roomThings[i];
                        if (!Matches(r, t.def)) continue;
                        var p = t.TryGetComp<CompPowerTrader>();
                        if (p == null) continue;
                        total++;
                        if (p.PowerOn) on++;
                    }
                    if (total == 0) return null;   // nothing in the room uses power: the row is not shown
                    row.Have = on; row.Need = total;
                    break;
                }
                case VenueCheck.Stock:
                {
                    if (VenueApi.SellsOf(v) == SellKind.None) return null;
                    int n = VenueSales.Stock(v).Count;
                    row.Have = n > 0 ? 1 : 0; row.Need = 1;
                    row.Note = n > 0 ? "HH.Setup.StockNote".TranslateOr("{0} stacks for sale").Formatted(n.ToString()) : null;
                    break;
                }
                case VenueCheck.ServingWorker:
                {
                    int n = ServingWorkers(v.Map);
                    row.Have = n > 0 ? 1 : 0; row.Need = 1;
                    if (n > 0) row.Note = "HH.Setup.WorkersNote".TranslateOr("{0} colonist(s) do Serving").Formatted(n.ToString());
                    break;
                }
                case VenueCheck.Custom:
                {
                    var w = r.Worker;
                    if (w == null || !w.Evaluate(v, r, out int have, out int need, out string note)) return null;
                    row.Have = have; row.Need = need; row.Note = note;
                    break;
                }
            }
            if (r.check == VenueCheck.Things || r.check == VenueCheck.Seats || r.check == VenueCheck.Tables || r.check == VenueCheck.Art || r.check == VenueCheck.Storage)
                for (int i = 0; i < roomPlans.Count; i++)
                    if (roomPlans[i].def.entityDefToBuild is ThingDef td && Counts(r, td)) row.Planned++;
            // the tender's post of an unattended (honor system) venue is not needed: shown as done, with a note (F15)
            if (r.staffOnly && !row.Ok && VenueHonor.IsOn(v))
            {
                row.Need = 0;
                row.Note = "HH.Setup.HonorNotNeeded".TranslateOr("not needed with the honor system");
            }
            return row;
        }

        /// <summary>Free colonists on the map with the Serving work type switched on.</summary>
        public static int ServingWorkers(Map map)
        {
            if (map == null || HHVenueDefOf.HH_Serving == null) return 0;
            int n = 0;
            var list = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                // WorkIsActive, not GetPriority: work tab replacements hook the vanilla API
                if (!p.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving) && p.workSettings != null && p.workSettings.EverWork && p.workSettings.WorkIsActive(HHVenueDefOf.HH_Serving)) n++;
            }
            return n;
        }

        /// <summary>Things in the venue's room that count for a requirement (for the "Show" button).</summary>
        public static List<Thing> ThingsFor(Venue v, VenueRequirement r)
        {
            var res = new List<Thing>();
            if (v?.Room == null) return res;
            var map = v.Map; var done = new HashSet<int>();
            foreach (var c in v.Room.Cells)
            {
                var list = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t == v.Sign || !done.Add(t.thingIDNumber)) continue;
                    if (t.def.category == ThingCategory.Building && Counts(r, t.def)) res.Add(t);
                }
            }
            return res;
        }

        // ------------------------------------------------------------------ build options

        // The architect lookup scans every designator; gizmos and the setup tab ask for it every frame, so answers live for a second (real time).
        struct DesignatorAnswer { public float Time; public Designator_Build Des; public string Why; }
        static readonly Dictionary<ThingDef, DesignatorAnswer> desCache = new Dictionary<ThingDef, DesignatorAnswer>();
        struct BuildDefsAnswer { public float Time; public List<ThingDef> Defs; }
        static readonly Dictionary<VenueRequirement, BuildDefsAnswer> buildDefsCache = new Dictionary<VenueRequirement, BuildDefsAnswer>();
        const float LookupSeconds = 1f;

        /// <summary>A def the player could build now: its architect designator (research, module gate, DLC) or the reason why not. Cached for a second.</summary>
        public static Designator_Build DesignatorFor(ThingDef d, out string whyNot)
        {
            float now = Time.realtimeSinceStartup;
            if (d != null && desCache.TryGetValue(d, out var a) && now - a.Time < LookupSeconds && now >= a.Time) { whyNot = a.Why; return a.Des; }
            var des = FindDesignator(d, out whyNot);
            if (d != null) desCache[d] = new DesignatorAnswer { Time = now, Des = des, Why = whyNot };
            return des;
        }

        static Designator_Build FindDesignator(ThingDef d, out string whyNot)
        {
            whyNot = null;
            if (d == null) { whyNot = "?"; return null; }
            Designator_Build des = null;
            try { des = BuildCopyCommandUtility.FindAllowedDesignator(d, mustBeVisible: false); }
            catch (Exception e) { HLog.Once("venues.setup.designator." + d.defName, LogLevel.Warn, "Venues", "No designator for " + d.defName + ": " + e.Message); }
            if (des == null) { whyNot = "HH.Setup.NotBuildable".TranslateOr("not in the build menu"); return null; }
            if (!d.IsResearchFinished)
            {
                var missing = d.researchPrerequisites?.FirstOrDefault(p => !p.IsFinished);
                whyNot = "HH.Setup.NeedsResearch".TranslateOr("needs research: {0}").Formatted(missing?.LabelCap.ToString() ?? "?");
                return null;
            }
            if (!des.Visible) { whyNot = "HH.Setup.NotAvailable".TranslateOr("not available right now"); return null; }
            return des;
        }

        /// <summary>The build options of a requirement that exist in this game (DLC and modules respected), buildable ones first.
        /// Cached for a second; the list is shared, do not change it.</summary>
        public static List<ThingDef> BuildDefs(VenueRequirement r)
        {
            if (r == null) return new List<ThingDef>();
            float now = Time.realtimeSinceStartup;
            if (buildDefsCache.TryGetValue(r, out var a) && now - a.Time < LookupSeconds && now >= a.Time) return a.Defs;
            var res = new List<ThingDef>();
            foreach (var d in r.BuildOptions) if (d != null && d.designationCategory != null && !res.Contains(d)) res.Add(d);
            // stable: buildable ones first, the XML order otherwise (one designator lookup per def, not one per comparison)
            var buildable = new List<ThingDef>(); var other = new List<ThingDef>();
            foreach (var d in res) (DesignatorFor(d, out _) != null ? buildable : other).Add(d);
            buildable.AddRange(other);
            buildDefsCache[r] = new BuildDefsAnswer { Time = now, Defs = buildable };
            return buildable;
        }

        /// <summary>Selects the architect tool for a def (the player then places it like from the build menu).</summary>
        public static bool StartBuilding(ThingDef d)
        {
            var des = DesignatorFor(d, out string why);
            if (des == null) { Messages.Message(d.LabelCap + ": " + why, MessageTypeDefOf.RejectInput, false); return false; }
            Find.DesignatorManager.Select(des);
            return true;
        }

        static void AnnounceReady(Venue v)
        {
            if (v?.Map == null || !v.Map.IsPlayerHome || Current.ProgramState != ProgramState.Playing) return;
            Messages.Message("HH.Setup.NowReady".TranslateOr("{0} has everything it needs and is ready to open.").Replace("{0}", v.DisplayNameOf()),
                new LookTargets(v.Sign), MessageTypeDefOf.SilentInput, false);
            HHVenueDefOf.HH_VenueReady?.PlayOneShotOnCamera(v.Map);
        }

        // ------------------------------------------------------------------ text

        /// <summary>One line for the inspect pane: "Ready" or "Not ready: missing a counter, seats".</summary>
        public static string Summary(Venue v)
        {
            var rep = Report(v);
            if (rep.Rows.Count == 0) return null;
            var sb = new StringBuilder();
            var blockers = rep.Blockers.ToList();
            bool enforce = HSettings.GetBool(RequireKey);
            if (blockers.Count > 0)
            {
                sb.Append(enforce ? "HH.Setup.NotReadyLine".TranslateOr("Not ready – missing: {0}") : "HH.Setup.MissingLine".TranslateOr("Setup – missing: {0}"))
                  .Replace("{0}", string.Join(", ", blockers.Select(b => b.Req.label)));
            }
            else
            {
                var other = rep.Rows.Where(r => r.Req.required && !r.Ok).Select(r => r.Req.label).ToList();
                sb.Append("HH.Setup.ReadyLine".TranslateOr("Setup: ready"));
                if (other.Count > 0) sb.Append(" · ").Append("HH.Setup.Attention".TranslateOr("check: {0}").Replace("{0}", string.Join(", ", other)));
                else if (rep.RecommendedMissing > 0) sb.Append(" · ").Append("HH.Setup.Recommended".TranslateOr("{0} recommended item(s) open").Replace("{0}", rep.RecommendedMissing.ToString()));
            }
            return sb.ToString();
        }
    }
}
