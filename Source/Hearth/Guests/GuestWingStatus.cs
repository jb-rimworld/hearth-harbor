// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    /// <summary>
    /// Status line per guest wing (5.8, N31): "Guest Wing 1: Bedroom ✓ · Kitchen ✓ · Restaurant ✗ missing · est. ★★★".
    /// Shown in the guest overview (HearthUI overview section), on the front desk and on guest beds. Cached for 250 ticks per map.
    /// </summary>
    public static class WingStatus
    {
        public enum State { Ok, Missing, Problem }
        public sealed class Item { public string Key; public string Label; public State State; public string Why; }
        public sealed class Report { public Area_HH_GuestWing Wing; public List<Item> Items = new List<Item>(); public int Stars; public string Line; }

        const float NearDistance = 30f;
        static WorkTypeDef CookWork => DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
        static readonly Dictionary<int, (int tick, List<Report> reports)> cache = new Dictionary<int, (int, List<Report>)>();

        public static void ClearCache() => cache.Clear();

        public static List<Report> For(Map map)
        {
            if (map == null) return new List<Report>();
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(map.uniqueID, out var c) && now - c.tick < 250 && now >= c.tick) return c.reports;
            var list = new List<Report>();
            var comp = GuestWings.Comp(map);
            if (comp != null) foreach (var w in comp.wings) if (w.TrueCount > 0) list.Add(Build(map, w));
            cache[map.uniqueID] = (now, list);
            return list;
        }

        public static IEnumerable<string> Lines(Map map) => For(map).Select(r => r.Line);

        /// <summary>The line of the wing that contains this cell (null if none).</summary>
        public static string LineAt(Map map, IntVec3 c)
        {
            foreach (var r in For(map)) if (c.InBounds(map) && r.Wing[c]) return r.Line;
            return null;
        }

        static string Mark(State s) => s == State.Ok ? "✓" : "✗";

        static Report Build(Map map, Area_HH_GuestWing wing)
        {
            var rep = new Report { Wing = wing };
            var cells = wing.CellsCached();
            // Bedroom: a guest bed in the wing, room closed and roofed, bed reachable
            var beds = GuestBeds.On(map).Where(b => wing[b.Position]).ToList();
            var bedroom = new Item { Key = "bedroom", Label = "HH.Inn.WingStatus.Bedroom".TranslateOr("Bedroom") };
            float bestScore = 0f;
            if (beds.Count == 0) { bedroom.State = State.Missing; bedroom.Why = "HH.Inn.WingStatus.NoGuestBed".TranslateOr("no guest bed"); }
            else
            {
                var good = beds.FirstOrDefault(b => { var rm = b.GetRoom(); return rm != null && !rm.PsychologicallyOutdoors && rm.OpenRoofCount == 0; });
                if (good == null) { bedroom.State = State.Problem; bedroom.Why = "HH.Inn.WingStatus.RoomOpen".TranslateOr("room open or unroofed"); }
                else bedroom.State = State.Ok;
                foreach (var b in beds) bestScore = Mathf.Max(bestScore, GuestBedPricing.RoomScore(b.GetRoom()));
            }
            rep.Items.Add(bedroom);

            // Kitchen: a meal source in or near the wing, powered/fuelled, and someone allowed to cook
            var kitchen = new Item { Key = "kitchen", Label = "HH.Inn.WingStatus.Kitchen".TranslateOr("Kitchen") };
            var stations = map.listerBuildings.allBuildingsColonist.Where(b => b.def.building != null && b.def.building.isMealSource && Near(b.Position, wing, cells)).ToList();
            if (stations.Count == 0) { kitchen.State = State.Missing; kitchen.Why = "HH.Inn.WingStatus.NoStove".TranslateOr("no cooking station"); }
            else
            {
                bool working = stations.Any(s => (s.TryGetComp<CompPowerTrader>()?.PowerOn ?? true) && (s.TryGetComp<CompRefuelable>()?.HasFuel ?? true));
                bool cook = map.mapPawns.FreeColonistsSpawned.Any(p => p.workSettings != null && CookWork != null && p.workSettings.WorkIsActive(CookWork));
                if (!working) { kitchen.State = State.Problem; kitchen.Why = "HH.Inn.WingStatus.NoPower".TranslateOr("no power or fuel"); }
                else if (!cook) { kitchen.State = State.Problem; kitchen.Why = "HH.Inn.WingStatus.NoCook".TranslateOr("nobody may cook"); }
                else kitchen.State = State.Ok;
            }
            rep.Items.Add(kitchen);

            // Restaurant: a restaurant or tavern venue in or near the wing, open in its hours
            var resto = new Item { Key = "restaurant", Label = "HH.Inn.WingStatus.Restaurant".TranslateOr("Restaurant") };
            var eat = Venues.All(map).Where(v => v.Type != null && (v.Type.defName == "HH_Venue_Restaurant" || v.Type.defName == "HH_Venue_Tavern") && v.Sign.Spawned && Near(v.Sign.Position, wing, cells)).ToList();
            if (eat.Count == 0) { resto.State = State.Missing; resto.Why = "HH.Inn.WingStatus.Missing".TranslateOr("missing"); }
            else if (!eat.Any(v => v.Comp.alwaysOpen || v.Comp.openHour != v.Comp.closeHour || v.Comp.openHour >= 0)) { resto.State = State.Problem; resto.Why = "HH.Inn.WingStatus.NoHours".TranslateOr("no opening hours"); }
            else resto.State = State.Ok;
            rep.Items.Add(resto);

            // Bathroom only with a bath mod (12.11)
            if (ModsConfig.IsActive("Dubwise.DubsBadHygiene"))
            {
                var bath = new Item { Key = "bathroom", Label = "HH.Inn.WingStatus.Bathroom".TranslateOr("Bathroom") };
                bool any = map.listerBuildings.allBuildingsColonist.Any(b => wing[b.Position] && (b.def.defName.IndexOf("Toilet", System.StringComparison.OrdinalIgnoreCase) >= 0 || b.def.defName.IndexOf("Shower", System.StringComparison.OrdinalIgnoreCase) >= 0));
                bath.State = any ? State.Ok : State.Missing; bath.Why = any ? null : "HH.Inn.WingStatus.Missing".TranslateOr("missing");
                rep.Items.Add(bath);
            }

            // estimate: the best guest room's score, one star less while the bedroom is not usable
            int stars = Mathf.Clamp(1 + (int)(bestScore / 20f), 1, 5);
            if (bedroom.State != State.Ok) stars = 1;
            rep.Stars = stars;
            var sb = new StringBuilder();
            sb.Append(wing.Label).Append(": ");
            for (int i = 0; i < rep.Items.Count; i++)
            {
                var it = rep.Items[i];
                if (i > 0) sb.Append(" · ");
                sb.Append(it.Label).Append(' ').Append(Mark(it.State));
                if (it.State != State.Ok && !it.Why.NullOrEmpty()) sb.Append(' ').Append(it.Why);
            }
            sb.Append(" · ").Append("HH.Inn.WingStatus.Est".TranslateOr("est.")).Append(' ').Append(new string('★', stars)).Append(new string('☆', 5 - stars));
            rep.Line = sb.ToString();
            return rep;
        }

        /// <summary>In the wing, or within 30 cells of one of its cells (stand-in for "door path ≤ 30").</summary>
        static bool Near(IntVec3 c, Area_HH_GuestWing wing, List<IntVec3> cells)
        {
            if (wing[c]) return true;
            float d2 = NearDistance * NearDistance;
            for (int i = 0; i < cells.Count; i += 3) if ((cells[i] - c).LengthHorizontalSquared <= d2) return true;
            return false;
        }
    }
}
