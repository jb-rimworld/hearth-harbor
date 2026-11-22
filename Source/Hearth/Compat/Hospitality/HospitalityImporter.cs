// SPDX-License-Identifier: MIT
// Import from Hospitality save games, "everything that can be" (KONZEPT 15.7, D12). Names only, no Hospitality code.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Compat.Hospitality
{
    /// <summary>What the converter saw while one save game was loading. Cleared before every load.</summary>
    public sealed class PendingImport
    {
        public sealed class Bed { public int IdNumber; public string OldDef, NewDef; public int Fee; public string SourceClass; }
        public sealed class Visitor
        {
            public int IdNumber; public string ThingId; public bool Arrived, Chat, Recruit, Rescued; public string GuestAreaRef, ShoppingAreaRef, LordRef, BedRef;
            public bool LooksLikeGuest => Arrived || Chat || Recruit || IsRef(LordRef) || IsRef(BedRef);
            static bool IsRef(string s) => !string.IsNullOrEmpty(s) && s != "null";
        }
        public readonly List<Bed> Beds = new List<Bed>();
        public readonly Dictionary<int, Visitor> Visitors = new Dictionary<int, Visitor>();
        public readonly List<HospitalityMapStub> MapStubs = new List<HospitalityMapStub>();
        public readonly List<MapComponent> DroppedMapComponents = new List<MapComponent>();
        public readonly List<GameComponent> DroppedGameComponents = new List<GameComponent>();
        public readonly HashSet<LordJob> DroppedLords = new HashSet<LordJob>();
        public readonly HashSet<JobDriver> DroppedJobs = new HashSet<JobDriver>();
        public readonly HashSet<Pawn> JobPawns = new HashSet<Pawn>();
        public int LordsDropped => DroppedLords.Count;
        public int JobsDropped => DroppedJobs.Count;
        /// <summary>Vanilla may run PostLoadInit more than once for components; record each object once.</summary>
        public void AddOnce<T>(List<T> list, T item) { if (!list.Contains(item)) list.Add(item); }
        /// <summary>Def names mapped to a counterpart, by category ("thought", "job" …) → count.</summary>
        public readonly Dictionary<string, int> Mapped = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Dropped = new Dictionary<string, int>();
        /// <summary>Family classes we could not stand in for (vanilla logs them; listed in the letter).</summary>
        public readonly HashSet<string> UnknownClasses = new HashSet<string>();
        public readonly HashSet<string> UnknownDefs = new HashSet<string>();
        public bool SawAnything => Beds.Count > 0 || MapStubs.Count > 0 || DroppedMapComponents.Count > 0 || DroppedGameComponents.Count > 0 || LordsDropped > 0
                                   || JobsDropped > 0 || Mapped.Count > 0 || Dropped.Count > 0 || UnknownClasses.Count > 0 || Visitors.Values.Any(v => v.LooksLikeGuest);
        public static void Inc(Dictionary<string, int> d, string k) { d.TryGetValue(k, out int n); d[k] = n + 1; }
    }

    /// <summary>
    /// Vanilla back-compatibility hook (BackCompatibility.conversionChain, 1.7 checked). Only acts on names of the Hospitality
    /// family and only when the class/def is missing in this session, so it is a no-op for every normal save.
    /// </summary>
    public sealed class HospitalityBackCompatibilityConverter : BackCompatibilityConverter
    {
        public override bool AppliesToVersion(int majorVer, int minorVer) => true;

        public override string BackCompatibleDefName(Type defType, string defName, bool forDefInjections = false, XmlNode node = null)
        {
            if (forDefInjections || string.IsNullOrEmpty(defName)) return null;
            return HospitalityImporter.MapDefName(defType, defName);
        }

        public override Type GetBackCompatibleType(Type baseType, string providedClassName, XmlNode node)
            => HospitalityImporter.MapType(baseType, providedClassName, node);

        public override void PostExposeData(object obj) { }

        public override void PreLoadSavegame(string loadingVersion) => HospitalityImporter.BeginLoad();

        public override void PostLoadSavegame(string loadingVersion)
        {
            try { HospitalityImporter.FinishLoad(); }
            catch (Exception e) { HLog.Error("Compat", "Hospitality import failed after loading; the game itself is fine", e); }
        }
    }

    public static class HospitalityImporter
    {
        public static PendingImport Pending = new PendingImport();
        /// <summary>Result of the last finished load (tests and diagnosis).</summary>
        public static PendingImport LastLoad;
        public static List<string> LastSummary = new List<string>();
        public static bool Registered { get; private set; }
        static bool removedDefsRegistered;

        // ------------------------------------------------------------ registration
        public static void Register()
        {
            if (Registered) return;
            var field = AccessTools.Field(typeof(BackCompatibility), "conversionChain");
            if (!(field?.GetValue(null) is List<BackCompatibilityConverter> chain))
            {
                HLog.Error("Compat", "BackCompatibility.conversionChain not found: Hospitality save import unavailable in this RimWorld version");
                return;
            }
            if (!chain.Any(c => c is HospitalityBackCompatibilityConverter)) chain.Add(new HospitalityBackCompatibilityConverter());
            Registered = true;
            RegisterRemovedDefs();
            HLog.Info("Compat", "Hospitality save importer registered (" + chain.Count + " converters in the chain)");
        }

        public static bool IsInChain()
        {
            var field = AccessTools.Field(typeof(BackCompatibility), "conversionChain");
            return field?.GetValue(null) is List<BackCompatibilityConverter> chain && chain.Any(c => c is HospitalityBackCompatibilityConverter);
        }

        /// <summary>
        /// Defs without any counterpart (thoughts, history events …) are registered as "removed" with vanilla, so loading drops them
        /// with no red error (vanilla then deletes memories with a missing def). Only names that do not exist in this session.
        /// </summary>
        static void RegisterRemovedDefs()
        {
            if (removedDefsRegistered) return;
            removedDefsRegistered = true;
            var field = AccessTools.Field(typeof(BackCompatibility), "RemovedDefs");
            if (!(field?.GetValue(null) is List<Tuple<string, Type>> list)) { HLog.Warn("Compat", "BackCompatibility.RemovedDefs not found; dropped Hospitality defs will log vanilla errors"); return; }
            int n = 0;
            void Add<T>(string[] names) where T : Def
            {
                foreach (var s in names)
                    if (DefDatabase<T>.GetNamedSilentFail(s) == null && !list.Any(x => x.Item1 == s && x.Item2 == typeof(T))) { list.Add(new Tuple<string, Type>(s, typeof(T))); n++; }
            }
            Add<ThoughtDef>(HospitalityDefTables.Thoughts);
            Add<HistoryEventDef>(HospitalityDefTables.HistoryEvents);
            Add<HediffDef>(HospitalityDefTables.Hediffs);
            Add<TraderKindDef>(HospitalityDefTables.TraderKinds);
            Add<JoyKindDef>(HospitalityDefTables.JoyKinds);
            Add<WorkGiverDef>(HospitalityDefTables.WorkGivers);
            HLog.Debug("Compat", n + " Hospitality def names registered as removed with vanilla");
        }

        public static void BeginLoad() { Pending = new PendingImport(); }

        // ------------------------------------------------------------ def names
        static readonly HashSet<string> droppedThoughtNames = new HashSet<string>(HospitalityDefTables.Thoughts);

        public static string MapDefName(Type defType, string defName)
        {
            if (defType == typeof(ThingDef))
            {
                string bare = defName, prefix = "";
                foreach (var p in new[] { "Blueprint_Install_", "Blueprint_", "Frame_" })
                    if (bare.StartsWith(p, StringComparison.Ordinal)) { prefix = p; bare = bare.Substring(p.Length); break; }
                if (bare.Length > 5 && bare.EndsWith("Guest", StringComparison.Ordinal))
                {
                    string baseName = bare.Substring(0, bare.Length - 5);
                    var baseDef = DefDatabase<ThingDef>.GetNamedSilentFail(baseName);
                    if (baseDef != null && baseDef.IsBed)
                    {
                        string result = prefix + baseName;
                        if (prefix.Length == 0 || DefDatabase<ThingDef>.GetNamedSilentFail(result) != null) { PendingImport.Inc(Pending.Mapped, "guest bed def"); return result; }
                    }
                }
                return null;
            }
            if (defType == typeof(JobDef) && HospitalityDefTables.IsJob(defName)) { PendingImport.Inc(Pending.Dropped, "job"); return "Wait"; }
            if (defType == typeof(DutyDef) && HospitalityDefTables.IsDuty(defName)) { PendingImport.Inc(Pending.Mapped, "duty"); return "ExitMapBest"; }
            if (defType == typeof(InteractionDef) && HospitalityDefTables.IsInteraction(defName)) { PendingImport.Inc(Pending.Mapped, "log entry"); return "Chitchat"; }
            if (defType == typeof(IncidentDef) && HospitalityDefTables.IsIncident(defName)) { PendingImport.Inc(Pending.Mapped, "queued incident"); return "VisitorGroup"; }
            if (defType == typeof(LetterDef) && HospitalityDefTables.IsLetter(defName)) { PendingImport.Inc(Pending.Mapped, "letter"); return "PositiveEvent"; }
            if (defType == typeof(WorkTypeDef) && HospitalityDefTables.WorkTypes.TryGetValue(defName, out var wt) && DefDatabase<WorkTypeDef>.GetNamedSilentFail(wt) != null)
            { PendingImport.Inc(Pending.Mapped, "work type"); return wt; }
            if (defType == typeof(ThoughtDef) && droppedThoughtNames.Contains(defName)) { PendingImport.Inc(Pending.Dropped, "thought"); return null; }
            if (defType == typeof(HistoryEventDef) || defType == typeof(HediffDef) || defType == typeof(TraderKindDef) || defType == typeof(JoyKindDef) || defType == typeof(WorkGiverDef))
            {
                if (HospitalityDefTables.HistoryEvents.Contains(defName) || HospitalityDefTables.Hediffs.Contains(defName) || HospitalityDefTables.TraderKinds.Contains(defName)
                    || HospitalityDefTables.JoyKinds.Contains(defName) || HospitalityDefTables.WorkGivers.Contains(defName))
                    PendingImport.Inc(Pending.Dropped, defType == typeof(HediffDef) ? "condition" : "detail");
            }
            return null;
        }

        // ------------------------------------------------------------ classes
        public static Type MapType(Type baseType, string cls, XmlNode node)
        {
            if (string.IsNullOrEmpty(cls)) return null;
            if (!HospitalityFamily.IsFamilyClassName(cls))
            {
                if (node != null && (cls == "Pawn" || cls == "Verse.Pawn")) PeekPawn(node);
                return null;
            }
            // the family is installed in this session (takeover case): leave their classes alone
            if (GenTypes.GetTypeInAnyAssembly(cls) != null) return null;
            if (baseType == null) return null;

            if (typeof(Thing).IsAssignableFrom(baseType) || baseType.IsAssignableFrom(typeof(Building_Bed)))
            {
                if (cls.EndsWith("GuestBed", StringComparison.Ordinal) && node != null)
                {
                    RecordBed(cls, node);
                    return typeof(Building_Bed);
                }
                Pending.UnknownClasses.Add(cls);
                return null;
            }
            if (typeof(MapComponent).IsAssignableFrom(baseType) || baseType == typeof(MapComponent))
                return cls == "Hospitality.Hospitality_MapComponent" ? typeof(HospitalityMapStub) : typeof(HospitalityDroppedMapComponent);
            // Our own import record stands in for Hospitality's game component: it notices the foreign node, reads nothing and is
            // removed after loading. (A separate stub GameComponent would be auto-created in every game by vanilla.)
            if (typeof(GameComponent).IsAssignableFrom(baseType)) return typeof(ImportedGuestBeds);
            if (typeof(LordJob).IsAssignableFrom(baseType)) return typeof(HospitalityLordJobStub);
            if (typeof(LordToilData).IsAssignableFrom(baseType)) return typeof(HospitalityLordToilDataStub);
            if (typeof(TriggerData).IsAssignableFrom(baseType)) return typeof(HospitalityTriggerDataStub);
            if (typeof(JobDriver).IsAssignableFrom(baseType)) return typeof(HospitalityJobDriverStub);
            Pending.UnknownClasses.Add(cls);
            return null;
        }

        static int ParseIdNumber(string thingId)
        {
            if (string.IsNullOrEmpty(thingId)) return -1;
            int i = thingId.Length;
            while (i > 0 && char.IsDigit(thingId[i - 1])) i--;
            return i < thingId.Length && int.TryParse(thingId.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1;
        }

        static void RecordBed(string cls, XmlNode node)
        {
            var b = new PendingImport.Bed { SourceClass = cls, OldDef = node["def"]?.InnerText, IdNumber = ParseIdNumber(node["id"]?.InnerText) };
            if (node["RentalFee"] != null) int.TryParse(node["RentalFee"].InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out b.Fee);
            Pending.Beds.Add(b);
        }

        static void PeekPawn(XmlNode node)
        {
            // CompGuest writes its keys straight into the pawn's node. Only look when one of them is there.
            XmlNode guestArea = node["guestArea"], chat = node["chat"], recruit = node["recruit"], arrived = node["arrived"], lord = node["lord"];
            if (guestArea == null && chat == null && recruit == null && arrived == null && lord == null) return;
            string id = node["id"]?.InnerText;
            int n = ParseIdNumber(id);
            if (n < 0) return;
            Pending.Visitors[n] = new PendingImport.Visitor
            {
                IdNumber = n, ThingId = id,
                Arrived = IsTrue(arrived), Chat = IsTrue(chat), Recruit = IsTrue(recruit), Rescued = IsTrue(node["rescued"]),
                GuestAreaRef = guestArea?.InnerText, ShoppingAreaRef = node["shoppingArea"]?.InnerText, LordRef = lord?.InnerText, BedRef = node["bed"]?.InnerText,
            };
        }

        static bool IsTrue(XmlNode n) => n != null && n.InnerText.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------ after loading
        public static void FinishLoad()
        {
            var p = Pending;
            Pending = new PendingImport();
            LastLoad = p;
            var rec = ImportedGuestBeds.Current;
            var lines = new List<string>();
            int beds = 0, bedsLive = 0, areas = 0, visitors = 0, visitsRebuilt = 0, settings = 0;

            // 1. guest beds from the save (Hospitality absent) and live guest beds (Hospitality present, takeover)
            if (rec != null)
            {
                beds = ImportBeds(p, rec);
                bedsLive = RegisterLiveGuestBeds(rec);
            }
            // 2. per-map settings and guest areas (map stub), then remove the stubs
            foreach (var stub in p.MapStubs)
            {
                var map = stub.map;
                if (rec != null && map != null)
                {
                    settings++;
                    rec.SetMapSettings(map, stub.GuestsAreWelcome, stub.RefuseUntilBeds, stub.FreeFood, stub.DefaultEntertain, stub.DefaultMakeFriends);
                    areas += ImportArea(rec, map, stub.DefaultGuestArea, "guest");
                    areas += ImportArea(rec, map, stub.DefaultShoppingArea, "shopping");
                }
                map?.components.Remove(stub);
            }
            foreach (var mc in p.DroppedMapComponents) mc.map?.components.Remove(mc);
            foreach (var gc in p.DroppedGameComponents) Current.Game?.components.Remove(gc);

            // 3. known visitors (CompGuest data on pawns) and their guest areas
            var areaByLoadId = new Dictionary<string, (Map, Area)>();
            foreach (var map in Find.Maps) foreach (var a in map.areaManager.AllAreas) areaByLoadId[a.GetUniqueLoadID()] = (map, a);
            var stranded = new List<Pawn>();
            foreach (var v in p.Visitors.Values)
            {
                if (!v.LooksLikeGuest) continue;
                var pawn = FindPawn(v.IdNumber);
                if (pawn == null || pawn.Faction == null || pawn.Faction.IsPlayer || pawn.IsPrisoner || pawn.IsSlave || !pawn.RaceProps.Humanlike) continue;
                visitors++;
                rec?.AddKnownVisitor(pawn, v.Chat, v.Recruit);
                if (v.GuestAreaRef != null && areaByLoadId.TryGetValue(v.GuestAreaRef, out var ga) && rec != null) areas += ImportArea(rec, ga.Item1, ga.Item2, "guest");
                if (v.ShoppingAreaRef != null && areaByLoadId.TryGetValue(v.ShoppingAreaRef, out var sa) && rec != null) areas += ImportArea(rec, sa.Item1, sa.Item2, "shopping");
                if (pawn.Spawned && pawn.Map != null && pawn.GetLord() == null && !pawn.Downed && !pawn.Dead) stranded.Add(pawn);
            }
            // 4. visits whose Hospitality lord was dropped: neutral journey home (15.7)
            foreach (var grp in stranded.GroupBy(x => (x.Map, x.Faction)))
            {
                var pawns = grp.ToList();
                foreach (var pw in pawns) { try { pw.jobs?.StopAll(); pw.mindState.duty = null; } catch (Exception e) { HLog.Warn("Compat", "Could not reset jobs of " + pw + ": " + e.Message); } }
                try
                {
                    LordMaker.MakeNewLord(grp.Key.Faction, new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false), grp.Key.Map, pawns);
                    visitsRebuilt++;
                    HLog.Info("Compat", $"Visit of {pawns.Count} {grp.Key.Faction?.Name} guests had a Hospitality lord; they now leave in peace");
                }
                catch (Exception e) { HLog.Error("Compat", "Could not send former Hospitality guests home", e); }
            }
            // 5. guest beds no longer owned by visitors who left
            CleanBedAssignments(rec);

            // summary + letter
            if (beds > 0) lines.Add(Count(beds, "guest bed", "guest beds"));
            if (bedsLive > 0) lines.Add(Count(bedsLive, "active Hospitality guest bed noted", "active Hospitality guest beds noted"));
            if (areas > 0) lines.Add(Count(areas, "guest area", "guest areas"));
            if (visitors > 0) lines.Add(Count(visitors, "known visitor", "known visitors"));
            if (settings > 0) lines.Add(Count(settings, "map's guest settings", "maps' guest settings"));
            if (visitsRebuilt > 0) lines.Add(Count(visitsRebuilt, "running visit sent home in peace", "running visits sent home in peace"));
            var dropped = new List<string>();
            foreach (var kv in p.Dropped) dropped.Add(Count(kv.Value, kv.Key, kv.Key + "s"));
            if (p.LordsDropped > 0 && visitsRebuilt == 0) dropped.Add(Count(p.LordsDropped, "visit", "visits"));
            int comps = p.DroppedMapComponents.Count + p.DroppedGameComponents.Count;
            if (comps > 0) dropped.Add(Count(comps, "add-on record", "add-on records"));
            foreach (var u in p.UnknownClasses) dropped.Add(u.Substring(u.LastIndexOf('.') + 1));
            LastSummary = new List<string>(lines);
            if (dropped.Count > 0) LastSummary.Add("dropped: " + string.Join(", ", dropped));
            if (lines.Count == 0 && dropped.Count == 0) return;   // nothing from Hospitality in this save
            string importedText = lines.Count > 0 ? string.Join(", ", lines) : "HH.Compat.ImportNothing".TranslateOr("nothing");
            string droppedText = dropped.Count > 0 ? string.Join(", ", dropped) : "HH.Compat.ImportNothing".TranslateOr("nothing");
            HLog.Info("Compat", "Imported from Hospitality: " + importedText + "; dropped: " + droppedText);
            rec?.AddHistory("Imported from Hospitality: " + importedText + "; dropped: " + droppedText);
            bool fromSave = beds > 0 || p.MapStubs.Count > 0 || p.LordsDropped > 0 || dropped.Count > 0 || visitors > 0;
            if (fromSave && HSettings.GetBool("compat.importLetter"))
            {
                try
                {
                    Find.LetterStack.ReceiveLetter("HH.Compat.ImportLetterTitle".TranslateOr("Imported from Hospitality"),
                        "HH.Compat.ImportLetterText".TranslateOr("This colony was played with Hospitality. Hearth & Harbor took over what it could.\n\nImported: {0}.\nDropped: {1}.\n\nImported guest beds are normal beds again and are remembered as guest beds; the guest part of Hearth & Harbor picks them up. Nothing else in your colony changed.")
                            .Replace("{0}", importedText).Replace("{1}", droppedText),
                        LetterDefOf.NeutralEvent);
                    if (rec != null) rec.LetterSent = true;
                }
                catch (Exception e) { HLog.Warn("Compat", "Could not send the import letter: " + e.Message); }
            }
        }

        static string Count(int n, string one, string many) => n + " " + (n == 1 ? one : many);

        static Pawn FindPawn(int idNumber)
        {
            foreach (var map in Find.Maps)
            {
                var list = map.mapPawns.AllPawns;
                for (int i = 0; i < list.Count; i++) if (list[i].thingIDNumber == idNumber) return list[i];
            }
            foreach (var wp in Find.WorldPawns.AllPawnsAliveOrDead) if (wp.thingIDNumber == idNumber) return wp;
            return null;
        }

        static IEnumerable<Building_Bed> AllBeds()
        {
            foreach (var map in Find.Maps)
            {
                foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Bed)) if (t is Building_Bed b) yield return b;
                foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing))
                    if (t is MinifiedThing m && m.InnerThing is Building_Bed mb) yield return mb;
            }
        }

        static int ImportBeds(PendingImport p, ImportedGuestBeds rec)
        {
            if (p.Beds.Count == 0) return 0;
            var byId = new Dictionary<int, PendingImport.Bed>();
            foreach (var b in p.Beds) if (b.IdNumber >= 0) byId[b.IdNumber] = b;
            int n = 0;
            foreach (var bed in AllBeds())
            {
                if (!byId.TryGetValue(bed.thingIDNumber, out var pb)) continue;
                pb.NewDef = bed.def.defName;
                if (rec.AddBed(bed, pb.Fee, pb.OldDef)) n++;
                HospitalityImport.NotifyBedImported(bed, pb.Fee);
                byId.Remove(bed.thingIDNumber);
            }
            foreach (var missing in byId.Values) HLog.Warn("Compat", $"Hospitality guest bed {missing.OldDef}{missing.IdNumber} was in the save but did not load (def missing?)");
            return n;
        }

        /// <summary>Takeover case: Hospitality is still installed, its guest beds are live objects. Remember them for when it is gone.</summary>
        public static int RegisterLiveGuestBeds(ImportedGuestBeds rec)
        {
            if (!HospitalityFamily.AnyReplacedActive) return 0;
            int n = 0;
            foreach (var bed in AllBeds())
            {
                if (!HospitalityFamily.IsTakeoverType(bed.GetType()) || !bed.GetType().Name.EndsWith("GuestBed", StringComparison.Ordinal)) continue;
                int fee = 0;
                try { var f = AccessTools.Field(bed.GetType(), "rentalFee"); if (f != null) fee = (int)f.GetValue(bed); } catch { }
                if (rec.AddBed(bed, fee, bed.def.defName)) { n++; HospitalityImport.NotifyBedImported(bed, fee); }
            }
            return n;
        }

        static int ImportArea(ImportedGuestBeds rec, Map map, Area area, string kind)
        {
            if (area == null || map == null) return 0;
            if (rec.HasArea(map, area, kind)) return 0;
            string converted = TryConvertToGuestWing(map, area, kind);
            rec.AddArea(map, area, kind, converted);
            return 1;
        }

        /// <summary>If the Inn module provides Area_HH_GuestWing, make one with the same cells; the player's old area stays.</summary>
        static string TryConvertToGuestWing(Map map, Area area, string kind)
        {
            if (kind != "guest") return null;
            var t = GenTypes.GetTypeInAnyAssembly("Hearth.Guests.Area_HH_GuestWing");
            if (t == null || !typeof(Area).IsAssignableFrom(t)) return null;
            try
            {
                Area wing = null;
                var c2 = t.GetConstructor(new[] { typeof(AreaManager), typeof(string) });
                var c1 = t.GetConstructor(new[] { typeof(AreaManager) });
                if (c2 != null) wing = (Area)c2.Invoke(new object[] { map.areaManager, area.Label });
                else if (c1 != null) wing = (Area)c1.Invoke(new object[] { map.areaManager });
                if (wing == null) return null;
                foreach (var c in area.ActiveCells) wing[c] = true;
                try { wing.RenamableLabel = area.Label; } catch { }
                map.areaManager.AllAreas.Add(wing);
                HLog.Info("Compat", $"Guest area '{area.Label}' copied into a guest wing ({wing.TrueCount} cells)");
                return wing.GetUniqueLoadID();
            }
            catch (Exception e) { HLog.Warn("Compat", "Could not create a guest wing from Hospitality area " + area.Label + ": " + e.Message); return null; }
        }

        static void CleanBedAssignments(ImportedGuestBeds rec)
        {
            if (rec == null) return;
            foreach (var bed in AllBeds())
            {
                if (!rec.IsGuestBed(bed)) continue;
                var comp = bed.CompAssignableToPawn;
                if (comp == null) continue;
                foreach (var owner in comp.AssignedPawnsForReading.ToList())
                    if (owner == null || owner.Faction == null || !owner.Faction.IsPlayer)
                        try { comp.ForceRemovePawn(owner); } catch { }
            }
        }
    }
}
