// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace Hearth.Compat.Hospitality
{
    /// <summary>
    /// What was taken over from Hospitality in this save: guest beds (thing ids + fee), guest/shopping areas, per-map settings,
    /// known visitors. Saved with the game, so the guest part of Hearth &amp; Harbor can pick it up whenever it is switched on.
    /// Only plain values are saved (no references into Hospitality), so removing either mod leaves a loadable save.
    /// </summary>
    public sealed class ImportedGuestBeds : GameComponent
    {
        public sealed class BedRecord : IExposable
        {
            public int ThingIdNumber; public int Fee; public string OldDef, Def; public int MapId = -1; public bool Adopted;
            public void ExposeData()
            {
                Scribe_Values.Look(ref ThingIdNumber, "id");
                Scribe_Values.Look(ref Fee, "fee");
                Scribe_Values.Look(ref OldDef, "oldDef");
                Scribe_Values.Look(ref Def, "def");
                Scribe_Values.Look(ref MapId, "map", -1);
                Scribe_Values.Look(ref Adopted, "adopted");
            }
        }

        public sealed class AreaRecord : IExposable
        {
            public int MapId; public string AreaLoadId, Label, Kind, ConvertedTo; public int Cells;
            public void ExposeData()
            {
                Scribe_Values.Look(ref MapId, "map");
                Scribe_Values.Look(ref AreaLoadId, "area");
                Scribe_Values.Look(ref Label, "label");
                Scribe_Values.Look(ref Kind, "kind");
                Scribe_Values.Look(ref ConvertedTo, "convertedTo");
                Scribe_Values.Look(ref Cells, "cells");
            }
        }

        public sealed class MapSettingsRecord : IExposable
        {
            public int MapId; public bool GuestsWelcome = true, RefuseUntilBeds, FreeFood, DefaultEntertain, DefaultMakeFriends;
            public void ExposeData()
            {
                Scribe_Values.Look(ref MapId, "map");
                Scribe_Values.Look(ref GuestsWelcome, "guestsWelcome", true);
                Scribe_Values.Look(ref RefuseUntilBeds, "refuseUntilBeds");
                Scribe_Values.Look(ref FreeFood, "freeFood");
                Scribe_Values.Look(ref DefaultEntertain, "defaultEntertain");
                Scribe_Values.Look(ref DefaultMakeFriends, "defaultMakeFriends");
            }
        }

        public sealed class VisitorRecord : IExposable
        {
            public int ThingIdNumber; public string Name, FactionName; public bool Entertain, MakeFriends;
            public void ExposeData()
            {
                Scribe_Values.Look(ref ThingIdNumber, "id");
                Scribe_Values.Look(ref Name, "name");
                Scribe_Values.Look(ref FactionName, "faction");
                Scribe_Values.Look(ref Entertain, "entertain");
                Scribe_Values.Look(ref MakeFriends, "makeFriends");
            }
        }

        public List<BedRecord> Beds = new List<BedRecord>();
        public List<AreaRecord> Areas = new List<AreaRecord>();
        public List<MapSettingsRecord> MapSettings = new List<MapSettingsRecord>();
        public List<VisitorRecord> Visitors = new List<VisitorRecord>();
        public List<string> History = new List<string>();
        public bool LetterSent;
        Dictionary<int, BedRecord> bedIndex;

        public ImportedGuestBeds(Game game) { }

        public static ImportedGuestBeds Current
        {
            get
            {
                var comps = Verse.Current.Game?.components;
                if (comps == null) return null;
                for (int i = 0; i < comps.Count; i++) if (comps[i] is ImportedGuestBeds r && !r.StandInForHospitality) return r;
                return null;
            }
        }

        BedRecord GetBed(int id)
        {
            if (bedIndex == null) { bedIndex = new Dictionary<int, BedRecord>(); foreach (var b in Beds) bedIndex[b.ThingIdNumber] = b; }
            return bedIndex.TryGetValue(id, out var r) ? r : null;
        }

        public bool IsGuestBed(Thing t) => t != null && GetBed(t.thingIDNumber) is BedRecord r && !r.Adopted;
        public int FeeOf(Thing t) => t == null ? 0 : GetBed(t.thingIDNumber)?.Fee ?? 0;

        public bool AddBed(Building_Bed bed, int fee, string oldDef)
        {
            if (bed == null) return false;
            var existing = GetBed(bed.thingIDNumber);
            if (existing != null) { existing.Fee = fee; existing.Def = bed.def.defName; return false; }
            var r = new BedRecord { ThingIdNumber = bed.thingIDNumber, Fee = Math.Max(0, fee), OldDef = oldDef, Def = bed.def.defName, MapId = bed.MapHeld?.uniqueID ?? -1 };
            Beds.Add(r); bedIndex[r.ThingIdNumber] = r;
            return true;
        }

        public bool HasArea(Map map, Area a, string kind) => Areas.Any(x => x.MapId == map.uniqueID && x.AreaLoadId == a.GetUniqueLoadID() && x.Kind == kind);

        public void AddArea(Map map, Area a, string kind, string convertedTo)
            => Areas.Add(new AreaRecord { MapId = map.uniqueID, AreaLoadId = a.GetUniqueLoadID(), Label = a.Label, Kind = kind, ConvertedTo = convertedTo, Cells = a.TrueCount });

        public void SetMapSettings(Map map, bool welcome, bool refuseUntilBeds, bool freeFood, bool entertain, bool makeFriends)
        {
            MapSettings.RemoveAll(x => x.MapId == map.uniqueID);
            MapSettings.Add(new MapSettingsRecord { MapId = map.uniqueID, GuestsWelcome = welcome, RefuseUntilBeds = refuseUntilBeds, FreeFood = freeFood, DefaultEntertain = entertain, DefaultMakeFriends = makeFriends });
        }

        public void AddKnownVisitor(Pawn p, bool entertain, bool makeFriends)
        {
            if (Visitors.Any(v => v.ThingIdNumber == p.thingIDNumber)) return;
            Visitors.Add(new VisitorRecord { ThingIdNumber = p.thingIDNumber, Name = p.LabelShortCap, FactionName = p.Faction?.Name, Entertain = entertain, MakeFriends = makeFriends });
        }

        public void AddHistory(string line)
        {
            History.Add("Day " + (GenDate.DaysPassed + 1) + ": " + line);
            while (History.Count > 20) History.RemoveAt(0);
        }

        /// <summary>True for an instance that was created for a Hospitality game component node (removed after loading).</summary>
        public bool StandInForHospitality;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var cls = Scribe.loader?.curXmlParent?.Attributes?["Class"]?.Value;
                if (HospitalityFamily.IsFamilyClassName(cls)) StandInForHospitality = true;
            }
            if (StandInForHospitality)
            {
                if (Scribe.mode == LoadSaveMode.PostLoadInit) HospitalityImporter.Pending.AddOnce(HospitalityImporter.Pending.DroppedGameComponents, this);
                return;
            }
            Scribe_Collections.Look(ref Beds, "beds", LookMode.Deep);
            Scribe_Collections.Look(ref Areas, "areas", LookMode.Deep);
            Scribe_Collections.Look(ref MapSettings, "mapSettings", LookMode.Deep);
            Scribe_Collections.Look(ref Visitors, "visitors", LookMode.Deep);
            Scribe_Collections.Look(ref History, "history", LookMode.Value);
            Scribe_Values.Look(ref LetterSent, "letterSent");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Beds ??= new List<BedRecord>(); Areas ??= new List<AreaRecord>(); MapSettings ??= new List<MapSettingsRecord>();
                Visitors ??= new List<VisitorRecord>(); History ??= new List<string>();
                bedIndex = null;
            }
        }

    }

    /// <summary>
    /// Public read API for the Inn module and other code: which beds/areas came from Hospitality.
    /// The Inn module should subscribe to <see cref="GuestBedImported"/> in OnDefsLoaded, or read <see cref="PendingBeds"/>
    /// after a load, and call <see cref="MarkAdopted"/> once it has turned a bed into one of its own guest beds.
    /// </summary>
    public static class HospitalityImport
    {
        /// <summary>Raised once per imported guest bed (bed, rental fee in silver per night).</summary>
        public static event Action<Building_Bed, int> GuestBedImported;

        public static bool IsImportedGuestBed(Thing t) => ImportedGuestBeds.Current?.IsGuestBed(t) ?? false;
        public static int RentalFee(Thing t) => ImportedGuestBeds.Current?.FeeOf(t) ?? 0;

        /// <summary>Thing id numbers of imported guest beds the Inn module has not adopted yet.</summary>
        public static IEnumerable<int> PendingBeds => ImportedGuestBeds.Current?.Beds.Where(b => !b.Adopted).Select(b => b.ThingIdNumber) ?? Enumerable.Empty<int>();
        public static IReadOnlyList<ImportedGuestBeds.AreaRecord> Areas => ImportedGuestBeds.Current?.Areas ?? new List<ImportedGuestBeds.AreaRecord>();
        public static IReadOnlyList<ImportedGuestBeds.VisitorRecord> KnownVisitors => ImportedGuestBeds.Current?.Visitors ?? new List<ImportedGuestBeds.VisitorRecord>();
        public static ImportedGuestBeds.MapSettingsRecord SettingsFor(Map map) => map == null ? null : ImportedGuestBeds.Current?.MapSettings.FirstOrDefault(m => m.MapId == map.uniqueID);

        public static void MarkAdopted(Thing bed)
        {
            var rec = ImportedGuestBeds.Current; if (rec == null || bed == null) return;
            foreach (var b in rec.Beds) if (b.ThingIdNumber == bed.thingIDNumber) b.Adopted = true;
        }

        /// <summary>Called by the importer for every bed: raises the event and, if the Inn module has a guest bed comp, sets it.</summary>
        internal static void NotifyBedImported(Building_Bed bed, int fee)
        {
            if (TryMarkInnComp(bed, fee)) MarkAdopted(bed);
            try { GuestBedImported?.Invoke(bed, fee); }
            catch (Exception e) { HLog.Error("Compat", "A GuestBedImported handler failed", e); }
        }

        static Type innCompType; static bool innCompLooked;

        /// <summary>Soft bridge to the Inn module (type by name "Hearth.Guests.CompHH_GuestBed"): sets a guest bed flag and fee if they exist.</summary>
        static bool TryMarkInnComp(Building_Bed bed, int fee)
        {
            if (!innCompLooked) { innCompLooked = true; innCompType = GenTypes.GetTypeInAnyAssembly("Hearth.Guests.CompHH_GuestBed"); }
            if (innCompType == null) return false;
            ThingComp comp = null;
            foreach (var c in bed.AllComps) if (innCompType.IsInstanceOfType(c)) { comp = c; break; }
            if (comp == null) return false;
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                bool set = false;
                var m = innCompType.GetMethod("SetGuestBed", F, null, new[] { typeof(bool) }, null);
                if (m != null) { m.Invoke(comp, new object[] { true }); set = true; }
                foreach (var name in new[] { "IsGuestBed", "isGuestBed", "Enabled", "enabled", "Active", "active" })
                {
                    if (set) break;
                    var pi = innCompType.GetProperty(name, F);
                    if (pi != null && pi.PropertyType == typeof(bool) && pi.CanWrite) { pi.SetValue(comp, true); set = true; break; }
                    var fi = innCompType.GetField(name, F);
                    if (fi != null && fi.FieldType == typeof(bool)) { fi.SetValue(comp, true); set = true; break; }
                }
                foreach (var name in new[] { "Fee", "fee", "RentalFee", "rentalFee", "Price", "price" })
                {
                    var pi = innCompType.GetProperty(name, F);
                    var ft = pi?.PropertyType ?? innCompType.GetField(name, F)?.FieldType;
                    if (ft == null) continue;
                    object v = ft == typeof(int) ? (object)fee : ft == typeof(float) ? (object)(float)fee : ft == typeof(Money) ? (object)Money.FromSilver(fee) : null;
                    if (v == null) continue;
                    if (pi != null && pi.CanWrite) pi.SetValue(comp, v); else innCompType.GetField(name, F)?.SetValue(comp, v);
                    break;
                }
                if (set) HLog.Info("Compat", $"Imported guest bed {bed.ThingID} handed to the Inn module (fee {fee})");
                return set;
            }
            catch (Exception e) { HLog.Warn("Compat", "Inn guest bed comp found but could not be set: " + e.Message); return false; }
        }
    }
}
