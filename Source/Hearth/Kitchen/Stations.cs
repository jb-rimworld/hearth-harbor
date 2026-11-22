// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>
    /// Kitchen devices (6.4): one main device carries the bill, the other devices of the dish only have to stand in the same room
    /// within 8 cells ("side devices"). A cook never walks between devices, so robots and Project RimFactory work unchanged.
    /// </summary>
    public static class Stations
    {
        public const int Radius = 8;
        const int CacheTicks = 120;
        static readonly Dictionary<ThingDef, int> kindMaskByDef = new Dictionary<ThingDef, int>();
        static readonly Dictionary<int, (int tick, int mask)> cache = new Dictionary<int, (int, int)>();

        public static string LabelOf(StationKind k) => ("HH.Kitchen.Station." + k).TranslateOr(DefaultLabel(k));
        public static string DefaultLabel(StationKind k)
        {
            switch (k)
            {
                case StationKind.PrepCounter: return "Prep counter";
                case StationKind.StockPot: return "Stock pot";
                case StationKind.DoughBench: return "Dough bench";
                case StationKind.ColdStation: return "Cold station";
                case StationKind.Wok: return "Wok station";
                case StationKind.Fryer: return "Deep fryer";
                case StationKind.ButcherTable: return "Butcher table";
                case StationKind.Stove: return "Stove";
                default: return k.ToString();
            }
        }

        public static int Bit(StationKind k) => 1 << (int)k;

        /// <summary>Kinds a building def counts as (own devices by extension, vanilla devices by name, foreign devices by building tag HH_Stove / HH_Oven / HH_Grill …).</summary>
        public static int KindMask(ThingDef def)
        {
            if (def == null) return 0;
            if (kindMaskByDef.TryGetValue(def, out int m)) return m;
            m = 0;
            var ext = def.GetModExtension<StationExtension>();
            if (ext != null) m |= Bit(ext.kind);
            switch (def.defName)
            {
                case "Campfire": m |= Bit(StationKind.Campfire); break;
                case "FueledStove": case "ElectricStove": m |= Bit(StationKind.Stove); break;
                case "TableButcher": m |= Bit(StationKind.ButcherTable); break;
            }
            var tags = def.building?.buildingTags;
            if (tags != null)
                foreach (var t in tags)
                    if (t != null && t.StartsWith("HH_"))
                    {
                        string n = t.Substring(3);
                        if (n == "Stove") m |= Bit(StationKind.Stove);
                        else if (Enum.TryParse(n, true, out StationKind k)) m |= Bit(k);
                    }
            kindMaskByDef[def] = m;
            return m;
        }

        /// <summary>Is this device ready to be used as a side device (powered where it needs power)?</summary>
        static bool Usable(Building b)
        {
            if (!b.Spawned || b.Destroyed) return false;
            var power = b.GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return false;
            return true;
        }

        /// <summary>Kinds of devices standing around <paramref name="main"/> (same room, within 8 cells), including the main device itself. Cached for 2 seconds.</summary>
        public static int KindsAround(Thing main)
        {
            if (main?.Map == null) return 0;
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(main.thingIDNumber, out var c) && now - c.tick < CacheTicks) return c.mask;
            int mask = 0;
            var map = main.Map;
            var room = main.Position.GetRoom(map);
            foreach (var cell in GenRadial.RadialCellsAround(main.Position, Radius, true))
            {
                if (!cell.InBounds(map)) continue;
                if (room != null && cell.GetRoom(map) != room) continue;
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Building b)
                    {
                        int km = KindMask(b.def);
                        if (km != 0 && Usable(b)) mask |= km;
                    }
            }
            mask |= KindMask(main.def);
            cache[main.thingIDNumber] = (now, mask);
            return mask;
        }

        /// <summary>Kinds the dish needs besides its main device.</summary>
        public static int SideMask(DishDef dish)
        {
            int m = 0;
            for (int i = 0; i < dish.stations.Count - 1; i++) m |= Bit(dish.stations[i]);
            return m & ~Bit(dish.MainStation);
        }

        public static List<StationKind> Missing(Thing main, DishDef dish)
        {
            int need = SideMask(dish), have = KindsAround(main);
            var list = new List<StationKind>();
            if ((need & ~have) == 0) return list;
            foreach (StationKind k in Enum.GetValues(typeof(StationKind)))
                if ((need & Bit(k)) != 0 && (have & Bit(k)) == 0) list.Add(k);
            return list;
        }

        public static string MissingText(Thing main, DishDef dish)
        {
            var miss = Missing(main, dish);
            return miss.Count == 0 ? null : "HH.Kitchen.Missing".TranslateOr("Missing: {0}").Replace("{0}", string.Join(", ", miss.Select(LabelOf)));
        }

        public static void ClearCache() { cache.Clear(); }

        /// <summary>Main device defs a dish's recipe is offered at: the vanilla device(s) of the kind, our own devices, and foreign work tables tagged for it.</summary>
        public static List<ThingDef> MainDefs(StationKind k)
        {
            var list = new List<ThingDef>();
            switch (k)
            {
                case StationKind.Campfire: AddNamed(list, "Campfire"); break;
                case StationKind.Stove: AddNamed(list, "FueledStove"); AddNamed(list, "ElectricStove"); break;
                case StationKind.ButcherTable: AddNamed(list, "TableButcher"); break;
            }
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
                if (!list.Contains(d) && d.IsWorkTable && (KindMask(d) & Bit(k)) != 0) list.Add(d);
            return list;
        }

        static void AddNamed(List<ThingDef> l, string name)
        {
            var d = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if (d != null) l.Add(d);
        }

        /// <summary>At startup: devices are gated by the research of the hospitality chapters when that exists, otherwise by our own cooking research.</summary>
        public static void AssignResearch()
        {
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                var e = d.GetModExtension<StationExtension>();
                if (e == null) continue;
                ResearchProjectDef r = null;
                if (!e.researchHint.NullOrEmpty()) r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(e.researchHint);
                if (r == null && !e.fallbackResearch.NullOrEmpty()) r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(e.fallbackResearch);
                if (r == null) continue;
                if (d.researchPrerequisites == null) d.researchPrerequisites = new List<ResearchProjectDef>();
                if (!d.researchPrerequisites.Contains(r)) d.researchPrerequisites.Add(r);
            }
        }
    }
}

namespace Hearth.Kitchen
{
    /// <summary>On every kitchen device: shows which other devices are in reach (the same room, within 8 cells) in the inspect pane.</summary>
    public class CompStationInfo : ThingComp
    {
        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned || !Modules.On(ModuleId.ShopsFood)) return null;
            int mask = Stations.KindsAround(parent) & ~Stations.KindMask(parent.def);
            if (mask == 0) return "HH.Kitchen.NoNeighbours".TranslateOr("No other kitchen device in reach.");
            var kinds = new System.Collections.Generic.List<string>();
            foreach (StationKind k in System.Enum.GetValues(typeof(StationKind))) if ((mask & Stations.Bit(k)) != 0) kinds.Add(Stations.LabelOf(k));
            return ("HH.Kitchen.Neighbours".TranslateOr("In reach: {0}")).Replace("{0}", string.Join(", ", kinds));
        }
    }
}
