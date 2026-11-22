// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>The storage settings a storage place had before a sign took it over (N45 "Auffüllen"): put back when the sign lets go.</summary>
    public sealed class StorageBackup : IExposable
    {
        public const int KindZone = 0, KindBuilding = 1;
        public int kind, id, priority;
        public ThingFilter filter;
        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref priority, "priority", (int)StoragePriority.Normal);
            Scribe_Deep.Look(ref filter, "filter");
        }
    }

    /// <summary>
    /// "Restock" (N45): the sign hands its goods filter and the priority Important to every storage place in its room, own zones, shelves and
    /// other people's alike, so the vanilla haulers keep the shelves full. What the places had before is remembered and comes back
    /// when the sign switches it off, is removed, or stops selling. Opt-in: nothing happens until the player switches it on.
    /// </summary>
    public static class VenueRestock
    {
        /// <summary>Storage places that belong to the room of a venue (stockpile zones with at least one cell in it, storage buildings standing in it).</summary>
        public static List<IStoreSettingsParent> StorageOf(Venue v)
        {
            var res = new List<IStoreSettingsParent>();
            if (v?.Room == null) return res;
            var map = v.Map; var room = v.Room;
            foreach (var z in map.zoneManager.AllZones)
            {
                if (!(z is Zone_Stockpile sp)) continue;
                var cells = sp.Cells;
                for (int i = 0; i < cells.Count; i++) if (cells[i].GetRoom(map) == room) { res.Add(sp); break; }
            }
            var buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (!(b is IStoreSettingsParent p) || b.def == HHVenueDefOf.HH_VendingMachine || !b.Spawned) continue;
                if (b.Position.GetRoom(map) != room) continue;
                if (p.GetStoreSettings() == null || !p.StorageTabVisible) continue;
                res.Add(p);
            }
            return res;
        }

        public static void KeyOf(IStoreSettingsParent p, out int kind, out int id)
        {
            if (p is Zone z) { kind = StorageBackup.KindZone; id = z.ID; }
            else { kind = StorageBackup.KindBuilding; id = ((Thing)p).thingIDNumber; }
        }

        public static IStoreSettingsParent Find(Map map, StorageBackup b)
        {
            if (map == null) return null;
            if (b.kind == StorageBackup.KindZone) return map.zoneManager.AllZones.FirstOrDefault(z => z.ID == b.id) as IStoreSettingsParent;
            var list = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < list.Count; i++) if (list[i].thingIDNumber == b.id) return list[i] as IStoreSettingsParent;
            return null;
        }

        /// <summary>Defs of the goods this venue's room sells right now (what a first filter is made from).</summary>
        public static IEnumerable<ThingDef> GoodsNow(Venue v)
        {
            var seen = new HashSet<ThingDef>();
            foreach (var t in VenueSales.Stock(v, true)) if (seen.Add(t.def)) yield return t.def;
        }
    }

    /// <summary>Window of the "Restock" button: the goods filter and the switch.</summary>
    public class Dialog_VenueRestock : Window
    {
        readonly CompVenueSignHH comp;
        readonly ThingFilterUI.UIState uiState = new ThingFilterUI.UIState();
        public override Vector2 InitialSize => new Vector2(460f, 640f);

        public Dialog_VenueRestock(CompVenueSignHH comp)
        {
            this.comp = comp;
            doCloseX = true; closeOnClickedOutside = false; absorbInputAroundWindow = false; forcePause = false; draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent == null || comp.parent.Destroyed) { Close(); return; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 34f), "HH.Venue.RestockTitle".TranslateOr("Restock") + " – " + comp.DisplayName);
            Text.Font = GameFont.Small;
            var hint = new Rect(0, 38f, inRect.width, 64f);
            Widgets.Label(hint, "HH.Venue.RestockHint".TranslateOr("When on, every storage place in this room (stockpile zones and shelves, yours or anybody's) takes the goods ticked below with priority Important, so your haulers keep the room stocked. The places' old settings come back when you switch this off."));
            var btn = new Rect(0, 106f, inRect.width, 32f);
            string label = comp.restockOn ? "HH.Venue.RestockSwitchOff".TranslateOr("Restock is ON – switch off and give the storage back") : "HH.Venue.RestockSwitchOn".TranslateOr("Restock is off – switch on");
            if (Widgets.ButtonText(btn, label)) comp.SetRestock(!comp.restockOn);
            var area = new Rect(0, 146f, inRect.width, inRect.height - 146f);
            comp.restockFilter ??= new ThingFilter();
            ThingFilterUI.DoThingFilterConfigWindow(area, uiState, comp.restockFilter, null, 1);
            if (comp.restockOn && GUI.changed) comp.SyncRestock();
        }
    }
}
