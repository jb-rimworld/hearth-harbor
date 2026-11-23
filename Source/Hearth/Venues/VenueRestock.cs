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
            if (VenueApi.SellsOf(v) == SellKind.Dishes)
            {
                // a restaurant restocks every kind of plate, not only the ones lying there right now
                foreach (var d in DefDatabase<ThingDef>.AllDefs)
                    if (d.comps != null && d.comps.Any(c => c.compClass == typeof(Hearth.Kitchen.CompDish)) && seen.Add(d)) yield return d;
            }
            foreach (var t in VenueSales.Stock(v, true)) if (seen.Add(t.def)) yield return t.def;
        }
    }

    /// <summary>Window of the "Restock" button: the switch, the goods filter and what the room holds now (F35 layout).</summary>
    public class Dialog_VenueRestock : Window
    {
        readonly CompVenueSignHH comp;
        readonly ThingFilterUI.UIState uiState = new ThingFilterUI.UIState();
        int places, stacks; float countedAt = -99f;
        public override Vector2 InitialSize => HUI.Size(580f, 720f, 460f, 560f);

        public Dialog_VenueRestock(CompVenueSignHH comp)
        {
            this.comp = comp;
            doCloseX = true; closeOnClickedOutside = false; absorbInputAroundWindow = false; forcePause = false; draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent == null || comp.parent.Destroyed) { Close(); return; }
            float y = VenueDialogUI.Header(inRect, "HH.Venue.RestockTitle".TranslateOr("Restock"), comp,
                comp.restockOn ? "HH.Venue.On".TranslateOr("on") : "HH.Venue.Off".TranslateOr("off"), comp.restockOn ? VenueDialogUI.Green : VenueDialogUI.Grey);
            y = VenueDialogUI.Hint(inRect.x, y, inRect.width, "HH.Venue.RestockHint".TranslateOr("When on, every storage place in this room (stockpile zones and shelves, yours or anybody's) takes the goods ticked below with priority Important, so your haulers keep the room stocked. The places' old settings come back when you switch this off.")) + 8f;

            // switch as a coloured strip
            var strip = new Rect(inRect.x, y, inRect.width, 36f);
            var col = comp.restockOn ? VenueDialogUI.Green : VenueDialogUI.Grey;
            Widgets.DrawBoxSolid(strip, new Color(col.r, col.g, col.b, 0.18f));
            GUI.color = col; Widgets.DrawBox(strip); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(strip.x + 10f, strip.y, strip.width - 190f, strip.height), comp.restockOn
                ? "HH.Venue.RestockStateOn".TranslateOr("Restock is on: the room's storage follows this list")
                : "HH.Venue.RestockStateOff".TranslateOr("Restock is off: the room's storage keeps its own settings"));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(strip.xMax - 176f, strip.y + 4f, 170f, 28f), comp.restockOn ? "HH.Venue.RestockBtnOff".TranslateOr("Switch off") : "HH.Venue.RestockBtnOn".TranslateOr("Switch on")))
                comp.SetRestock(!comp.restockOn);
            y += 44f;

            // footer: what the room holds now (counted once a second)
            if (Time.realtimeSinceStartup - countedAt > 1f)
            {
                countedAt = Time.realtimeSinceStartup;
                var v = comp.parent.Spawned ? VenueApi.VenueOfSign(comp.parent.Map, comp.parent) : null;
                places = v == null ? 0 : VenueRestock.StorageOf(v).Count;
                stacks = v == null ? 0 : VenueSales.Stock(v).Count;
            }
            const float footer = 26f;
            var area = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - footer - 4f);
            comp.restockFilter ??= new ThingFilter();
            ThingFilterUI.DoThingFilterConfigWindow(area, uiState, comp.restockFilter, null, 1);
            if (comp.restockOn && GUI.changed) comp.SyncRestock();
            GUI.color = VenueDialogUI.Grey;
            Widgets.Label(new Rect(inRect.x, inRect.yMax - footer, inRect.width, footer),
                "HH.Venue.RestockFooter".TranslateOr("Storage places in this room: {0} · stacks for sale now: {1}").Replace("{0}", places.ToString()).Replace("{1}", stacks.ToString()));
            GUI.color = Color.white;
        }
    }
}
