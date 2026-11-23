// SPDX-License-Identifier: MIT
// In-game tests for the Hospitality takeover and import. Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using Hearth.Compat.Hospitality;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Compat.Hospitality.Tests
{
    static class HspTestUtil
    {
        public const string FixtureDefault = "/private/tmp/claude-501/hh_tests/hospcompat_fixture.rws";
        public static string FixturePath => Environment.GetEnvironmentVariable("HH_HSP_FIXTURE") is string s && s.Length > 0 ? s : FixtureDefault;

        public static IntVec3 FreeRect(Map map, int w, int h, IntVec3 near, HashSet<IntVec3> avoid = null)
        {
            foreach (var c in GenRadial.RadialCellsAround(near, 30, true))
            {
                var r = new CellRect(c.x, c.z, w, h).ExpandedBy(1);
                bool ok = true;
                foreach (var x in r)
                    if (!x.InBounds(map) || !x.Standable(map) || x.GetFirstBuilding(map) != null || x.Fogged(map) || x.GetFirstPawn(map) != null || (avoid != null && avoid.Contains(x))) { ok = false; break; }
                if (ok) { if (avoid != null) foreach (var x in r) avoid.Add(x); return c; }
            }
            return IntVec3.Invalid;
        }

        public static Building_Bed SpawnBed(Map map, string defName, HashSet<IntVec3> used)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return null;
            var cell = FreeRect(map, def.size.x, def.size.z, map.Center, used);
            if (!cell.IsValid) return null;
            var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            t.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(t, cell, map, Rot4.North) as Building_Bed;
        }

        public static Lord SpawnVanillaVisitors(Map map)
        {
            var parms = StorytellerUtility.DefaultParmsNow(DefDatabase<IncidentCategoryDef>.GetNamed("FactionArrival"), map);
            parms.faction = Find.FactionManager.AllFactionsListForReading
                .Where(f => !f.IsPlayer && !f.HostileTo(Faction.OfPlayer) && !f.def.hidden && f.def.humanlikeFaction && !f.temporary && !f.defeated)
                .OrderBy(f => f.loadID).FirstOrDefault();
            parms.points = Math.Max(parms.points, 300f);
            var before = map.lordManager.lords.ToList();
            bool ok = IncidentDefOf.VisitorGroup.Worker.TryExecute(parms);
            if (!ok) return null;
            return map.lordManager.lords.FirstOrDefault(l => !before.Contains(l));
        }

        /// <summary>Error log entries that exist right now (compare later with NewErrors).</summary>
        public static HashSet<LogMessage> Snapshot() => new HashSet<LogMessage>(Log.Messages.Where(m => m.type == LogMessageType.Error));
        public static List<string> NewErrors(HashSet<LogMessage> before, Func<string, bool> filter = null)
            => Log.Messages.Where(m => m.type == LogMessageType.Error && !before.Contains(m)).Select(m => m.text).Where(x => filter == null || filter(x)).ToList();

        public static Area MakeArea(Map map, string label, IntVec3 center, int radius)
        {
            if (!map.areaManager.TryMakeNewAllowed(out var a)) return null;
            a.SetLabel(label);
            foreach (var c in GenRadial.RadialCellsAround(center, radius, true)) if (c.InBounds(map)) a[c] = true;
            return a;
        }
    }

    /// <summary>Without Hospitality: nothing happens (no takeover, no dialog, vanilla untouched), the importer sits in the chain.</summary>
    public sealed class HspDetectSuite : HearthTestSuite
    {
        public override string Name => "hospcompat.detect";
        public override IEnumerable<int> Run(TestContext t)
        {
            bool family = HospitalityFamily.AnyReplacedActive;
            t.Note("family mods running: " + (HospitalityFamily.Detected.Count == 0 ? "none" : string.Join(", ", HospitalityFamily.Detected)));
            if (family) { t.Note("Hospitality is active in this run: detect checks the takeover side only"); t.Check(HospitalityTakeover.Active, "takeover active with Hospitality"); yield break; }
            t.Check(HospitalityTakeover.Ran, "takeover check ran at startup");
            t.Check(!HospitalityTakeover.Active, "no takeover without Hospitality");
            t.Check(!HospitalityDisabler.DialogOffered && !HospitalityDisabler.DialogShown, "no dialog offered or shown");
            t.Check(HospitalityTakeover.PatchesRemoved == 0 && HospitalityTakeover.IncidentsSilenced == 0 && HospitalityTakeover.BuildablesHidden == 0, "nothing unpatched, silenced or hidden");
            t.Check(HospitalityImporter.IsInChain(), "save importer registered in BackCompatibility.conversionChain");
            var vg = IncidentDefOf.VisitorGroup;
            t.Check(vg.workerClass == typeof(IncidentWorker_VisitorGroup) && vg.baseChance > 0f, "vanilla VisitorGroup untouched (" + vg.workerClass?.Name + ", chance " + vg.baseChance + ")");
            t.Check(DefDatabase<MainButtonDef>.AllDefsListForReading.All(b => b.modContentPack == null || HospitalityFamily.Lookup(b.modContentPack.PackageId) == null), "no family main tab present");
            // family lookup logic
            t.Check(HospitalityFamily.Lookup("Orion.Hospitality")?.Role == FamilyRole.Replaced, "Orion.Hospitality is a replaced mod");
            t.Check(HospitalityFamily.Lookup("orion.hospitality_steam")?.Role == FamilyRole.Replaced, "Steam duplicate postfix ignored");
            t.Check(HospitalityFamily.Lookup("local.tourism")?.Role == FamilyRole.MentionOnly, "Tourism is only mentioned, never gutted");
            t.Check(HospitalityFamily.Lookup("juliusberger00.hearthharbor") == null, "we are not family");
            t.Check(HospitalityFamily.IsFamilyClassName("Hospitality.CompGuest") && !HospitalityFamily.IsFamilyClassName("HospitalityX.Foo") && !HospitalityFamily.IsFamilyClassName("RimWorld.Building_Bed"), "family class names recognised exactly");
            t.Check(HospitalityTakeover.RemainingFamilyPatches() == 0, "no family patches anywhere");
            var removed = AccessTools.Field(typeof(BackCompatibility), "RemovedDefs").GetValue(null) as List<Tuple<string, Type>>;
            t.Check(removed != null && removed.Any(x => x.Item1 == "GuestAngered" && x.Item2 == typeof(ThoughtDef)), "Hospitality thoughts registered as removed defs");
            t.Check(Find.Maps.All(m => !m.components.Any(c => c is HospitalityMapStandIn)), "no Hospitality stand-in map component in a normal game");
            t.Check(Current.Game.components.Count(c => c is ImportedGuestBeds) == 1 && ImportedGuestBeds.Current != null && !ImportedGuestBeds.Current.StandInForHospitality, "exactly one import record, not a stand-in");
            if (HospitalityImporter.LastLoad == null) t.Check(ImportedGuestBeds.Current.Beds.Count == 0 && !HospitalityImport.IsImportedGuestBed(null), "no imported beds in a fresh game");
            yield return 60;
            t.Check(Find.WindowStack.Windows.All(w => !(w is Dialog_HospitalityTakeover)), "no takeover dialog on screen");
        }
    }

    /// <summary>Converter logic on synthetic XML nodes (no save involved).</summary>
    public sealed class HspConvertSuite : HearthTestSuite
    {
        public override string Name => "hospcompat.convert";
        public override IEnumerable<int> Run(TestContext t)
        {
            if (HospitalityFamily.AnyReplacedActive) { t.Note("Hospitality active: its classes exist, the converter must leave them alone"); t.Check(HospitalityImporter.MapType(typeof(Thing), "Hospitality.Building_GuestBed", null) == null, "existing family class not converted"); yield break; }
            var saved = HospitalityImporter.Pending;
            HospitalityImporter.BeginLoad();
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml("<r><thing Class=\"Hospitality.Building_GuestBed\"><def>DoubleBedGuest</def><id>DoubleBedGuest77001</id><stuff>WoodLog</stuff><RentalFee>23</RentalFee></thing>"
                    + "<thing Class=\"Pawn\"><def>Human</def><id>Human77002</id><arrived>True</arrived><chat>True</chat><guestArea>Area_9_Named</guestArea></thing>"
                    + "<thing Class=\"Pawn\"><def>Human</def><id>Human77003</id></thing></r>");
                var bedNode = doc.DocumentElement.ChildNodes[0];
                t.Check(HospitalityImporter.MapType(typeof(Thing), "Hospitality.Building_GuestBed", bedNode) == typeof(Building_Bed), "guest bed class → Building_Bed");
                var pb = HospitalityImporter.Pending.Beds.FirstOrDefault();
                t.Check(pb != null && pb.IdNumber == 77001 && pb.Fee == 23 && pb.OldDef == "DoubleBedGuest", "bed recorded with id, fee and old def: " + (pb == null ? "none" : pb.IdNumber + "/" + pb.Fee + "/" + pb.OldDef));
                t.Check(HospitalityImporter.MapType(typeof(Thing), "Pawn", doc.DocumentElement.ChildNodes[1]) == null, "pawn class untouched");
                t.Check(HospitalityImporter.Pending.Visitors.TryGetValue(77002, out var v) && v.Arrived && v.Chat && v.GuestAreaRef == "Area_9_Named", "guest data peeked from pawn node");
                HospitalityImporter.MapType(typeof(Thing), "Pawn", doc.DocumentElement.ChildNodes[2]);
                t.Check(!HospitalityImporter.Pending.Visitors.ContainsKey(77003), "pawn without guest data not recorded");
                t.Check(HospitalityImporter.MapDefName(typeof(ThingDef), "DoubleBedGuest") == "DoubleBed", "DoubleBedGuest → DoubleBed");
                t.Check(HospitalityImporter.MapDefName(typeof(ThingDef), "BedGuest") == "Bed", "BedGuest → Bed");
                t.Check(HospitalityImporter.MapDefName(typeof(ThingDef), "SteelGuest") == null, "non-bed ...Guest not mapped");
                t.Check(HospitalityImporter.MapDefName(typeof(ThingDef), "Guest") == null, "bare 'Guest' not mapped");
                t.Check(HospitalityImporter.MapDefName(typeof(JobDef), "ClaimGuestBed") == "Wait", "Hospitality job → Wait (dropped)");
                t.Check(HospitalityImporter.MapDefName(typeof(DutyDef), "Relax") == "ExitMapBest", "Hospitality duty → ExitMapBest");
                t.Check(HospitalityImporter.MapDefName(typeof(InteractionDef), "GuestDiplomacy") == "Chitchat", "Hospitality interaction log → Chitchat");
                t.Check(HospitalityImporter.MapDefName(typeof(IncidentDef), "VisitorGroupMax") == "VisitorGroup", "queued Hospitality incident → VisitorGroup");
                t.Check(HospitalityImporter.MapDefName(typeof(WorkTypeDef), "Diplomat") == "HH_Hosting", "Diplomat work type → HH_Hosting");
                t.Check(HospitalityImporter.MapDefName(typeof(ThoughtDef), "GuestAngered") == null && HospitalityImporter.Pending.Dropped.ContainsKey("thought"), "Hospitality thought dropped and counted");
                t.Check(HospitalityImporter.MapDefName(typeof(ThoughtDef), "SomeOtherModThought") == null && HospitalityImporter.Pending.Dropped["thought"] == 1, "foreign thought not counted");
                t.Check(HospitalityImporter.MapType(typeof(MapComponent), "Hospitality.Hospitality_MapComponent", null) == typeof(HospitalityMapStub), "map component → stub");
                t.Check(HospitalityImporter.MapType(typeof(MapComponent), "Storefront.Store.StoresManager", null) == typeof(HospitalityDroppedMapComponent), "add-on map component → dropped stub");
                t.Check(HospitalityImporter.MapType(typeof(GameComponent), "Hospitality.Hospitality_GameComponent", null) == typeof(ImportedGuestBeds), "game component → import record stand-in");
                t.Check(HospitalityImporter.MapType(typeof(LordJob), "Hospitality.LordJob_VisitColony", null) == typeof(HospitalityLordJobStub), "visit lord job → stub");
                t.Check(HospitalityImporter.MapType(typeof(LordToilData), "Hospitality.LordToilData_VisitPoint", null) == typeof(HospitalityLordToilDataStub), "toil data → stub");
                t.Check(HospitalityImporter.MapType(typeof(TriggerData), "Hospitality.TriggerData_VisitorsPleasedMax", null) == typeof(HospitalityTriggerDataStub), "trigger data → stub");
                t.Check(HospitalityImporter.MapType(typeof(JobDriver), "Hospitality.JobDriver_ClaimBed", null) == typeof(HospitalityJobDriverStub), "job driver → stub");
                t.Check(HospitalityImporter.MapType(typeof(Thing), "RimWorld.Building_Bed", null) == null, "vanilla classes untouched");
                t.Check(new HospitalityLordJobStub().LostImportantReferenceDuringLoading, "lord stub asks vanilla to drop the lord");
            }
            finally { HospitalityImporter.Pending = saved; }
            yield return 1;
        }
    }

    /// <summary>
    /// Round trip: builds a save that looks like a Hospitality save (guest beds with fee, map component, game component, a visit
    /// lord with Hospitality classes, a Hospitality job, guest data on the visitors, a Hospitality thought, Hospitality in the mod list),
    /// loads it in this session (Hospitality absent) and checks the import. With HH_HSP_FIXTURE (or the default fixture path)
    /// pointing to a real Hospitality save made by hospcompat.makefixture, that one is loaded instead (HH_HSP_REAL=1).
    /// </summary>
    public sealed class HspImportSuite : HearthTestSuite
    {
        public override string Name => "hospcompat.import";
        public override int MaxTicks => 20000;
        static int phase;                 // survives the game reload (same process)
        static string mode = "synthetic";
        static int expectBeds, expectFeeSum;
        static HashSet<LogMessage> errorsBefore;
        static string expectAreaLabel;
        const string SaveName = "hh_hsp_import";

        public override IEnumerable<int> Run(TestContext t)
        {
            if (HospitalityFamily.AnyReplacedActive) { t.Note("Hospitality active: import is only tested without it"); t.Check(true, "skipped with Hospitality"); yield break; }
            if (phase == 0) { foreach (var x in PhaseA(t)) yield return x; yield break; }
            foreach (var x in PhaseB(t)) yield return x;
        }

        IEnumerable<int> PhaseA(TestContext t)
        {
            var map = t.Map;
            string dest = GenFilePaths.FilePathForSavedGame(SaveName);
            if (Environment.GetEnvironmentVariable("HH_HSP_REAL") == "1")
            {
                mode = "real";
                t.Check(File.Exists(HspTestUtil.FixturePath), "real Hospitality fixture present: " + HspTestUtil.FixturePath);
                if (!File.Exists(HspTestUtil.FixturePath)) yield break;
                File.Copy(HspTestUtil.FixturePath, dest, true);
                string expect = HspTestUtil.FixturePath + ".expect";
                expectBeds = 1; expectFeeSum = -1; expectAreaLabel = null;
                if (File.Exists(expect))
                    foreach (var line in File.ReadAllLines(expect))
                    {
                        var kv = line.Split('=');
                        if (kv.Length != 2) continue;
                        if (kv[0] == "beds") int.TryParse(kv[1], out expectBeds);
                        if (kv[0] == "feeSum") int.TryParse(kv[1], out expectFeeSum);
                        if (kv[0] == "area") expectAreaLabel = kv[1];
                    }
                t.Note($"real fixture: expecting {expectBeds} beds, fee sum {expectFeeSum}, area '{expectAreaLabel}'");
            }
            else
            {
                mode = "synthetic";
                var used = new HashSet<IntVec3>();
                var bed1 = HspTestUtil.SpawnBed(map, "Bed", used);
                var bed2 = HspTestUtil.SpawnBed(map, "DoubleBed", used);
                var plain = HspTestUtil.SpawnBed(map, "Bed", used);
                t.Check(bed1 != null && bed2 != null && plain != null, "spawned 3 vanilla beds");
                if (bed1 == null || bed2 == null || plain == null) yield break;
                var area = HspTestUtil.MakeArea(map, "Guests (test)", bed1.Position, 4);
                t.Check(area != null, "made an allowed area for guests");
                var lord = HspTestUtil.SpawnVanillaVisitors(map);
                t.Check(lord != null && lord.ownedPawns.Count > 0, "vanilla visitor group arrived: " + (lord?.ownedPawns.Count ?? 0));
                if (lord == null || area == null) yield break;
                yield return 250;
                var visitors = lord.ownedPawns.ToList();
                t.Note("lord before saving: " + lord.LordJob?.GetType().Name + ", toil " + lord.CurLordToil?.GetType().Name + ", faction " + lord.faction?.Name + ", pawns " + visitors.Count + ", lords on map " + map.lordManager.lords.Count);
                GameDataSaveLoader.SaveGame(SaveName);
                t.Check(File.Exists(dest), "saved " + dest);
                // turn it into a Hospitality save
                var doc = new XmlDocument(); doc.Load(dest);
                int mutated = MakeHospitalitySave(t, doc, bed1, 17, bed2, 25, area, lord, visitors);
                t.Check(mutated >= 6, "save rewritten as a Hospitality save (" + mutated + " edits)");
                doc.Save(dest);
                expectBeds = 2; expectFeeSum = 42; expectAreaLabel = area.Label;
            }
            phase = 1;
            errorsBefore = HspTestUtil.Snapshot();
            t.Note("loading " + dest + " (" + mode + ")");
            HLog.Info("Compat", "TEST hospcompat.import: loading " + mode + " Hospitality save");
            GameDataSaveLoader.LoadGame(SaveName);
            yield return 100000;   // this game ends here; the new game's test runner continues with phase B
        }

        static int MakeHospitalitySave(TestContext t, XmlDocument doc, Building_Bed b1, int fee1, Building_Bed b2, int fee2, Area area, Lord lord, List<Pawn> visitors)
        {
            int n = 0;
            var root = doc.DocumentElement; // savegame
            // 1. mod list: Hospitality was active
            var meta = root["meta"];
            void AddLi(string list, string val) { var l = meta?[list]; if (l == null) return; var li = doc.CreateElement("li"); li.InnerText = val; l.AppendChild(li); }
            AddLi("modIds", "orion.hospitality"); AddLi("modSteamIds", "3509486825"); AddLi("modNames", "Hospitality (Continued)"); n++;
            var game = root["game"];
            // 2. game component
            var gcs = game["components"];
            if (gcs != null) { var li = doc.CreateElement("li"); li.SetAttribute("Class", "Hospitality.Hospitality_GameComponent"); li.InnerXml = "<defaultFoodRestriction><id>600</id><label>Hospitality_Guests</label></defaultFoodRestriction>"; gcs.AppendChild(li); n++; }
            var mapNode = game["maps"]?.ChildNodes.Cast<XmlNode>().FirstOrDefault();
            if (mapNode == null) { t.Check(false, "map node in save"); return n; }
            // 3. map component with guest settings
            var mcs = mapNode["components"];
            if (mcs != null)
            {
                var li = doc.CreateElement("li"); li.SetAttribute("Class", "Hospitality.Hospitality_MapComponent");
                li.InnerXml = "<guestsAreWelcome>False</guestsAreWelcome><refuseGuestsUntilWeHaveBeds>True</refuseGuestsUntilWeHaveBeds><defaultAreaRestriction>" + area.GetUniqueLoadID()
                    + "</defaultAreaRestriction><defaultAreaShopping>null</defaultAreaShopping><incidentQueue><queued /></incidentQueue>";
                mcs.AppendChild(li); n++;
                var li2 = doc.CreateElement("li"); li2.SetAttribute("Class", "Gastronomy.Restaurant.RestaurantsManager"); li2.InnerXml = "<restaurants />"; mcs.AppendChild(li2); n++;
            }
            // 4. guest beds
            foreach (var (bed, fee) in new[] { (b1, fee1), (b2, fee2) })
            {
                var node = mapNode.SelectSingleNode("things/thing[id='" + bed.ThingID + "']") as XmlElement;
                if (node == null) { t.Note("bed node not found: " + bed.ThingID); continue; }
                node.SetAttribute("Class", "Hospitality.Building_GuestBed");
                node["def"].InnerText = bed.def.defName + "Guest";
                node["id"].InnerText = bed.def.defName + "Guest" + bed.thingIDNumber;
                var f = doc.CreateElement("RentalFee"); f.InnerText = fee.ToString(); node.AppendChild(f);
                n++;
            }
            // 5. the visit lord → Hospitality classes, with Hospitality toil and trigger data
            // vanilla does not write a field with its default value: the first lord of a game (loadID 0, which happens when no other
            // lord was made before, e.g. with the Inn's scheduler not having sent anyone yet) has no <loadID> element at all
            var lordNode = mapNode.SelectSingleNode(lord.loadID == 0 ? "lordManager/lords/li[not(loadID)]" : "lordManager/lords/li[loadID='" + lord.loadID + "']") as XmlElement;
            if (lordNode != null)
            {
                (lordNode["lordJob"] as XmlElement)?.SetAttribute("Class", "Hospitality.LordJob_VisitColony");
                var stay = doc.CreateElement("stayDuration"); stay.InnerText = "90000"; lordNode["lordJob"]?.AppendChild(stay);
                var tdata = lordNode["lordToilData"];
                if (tdata != null) tdata.InnerXml = "<keys><li>0</li></keys><values><li Class=\"Hospitality.LordToilData_VisitPoint\"><radius>45</radius></li></values>";
                var trig = lordNode["triggerData"];
                if (trig != null) trig.InnerXml = "<keys><li>3</li></keys><values><li Class=\"Hospitality.TriggerData_VisitorsPleasedMax\"><threshold>100</threshold></li></values>";
                n++;
            }
            else t.Note("lord node not found: " + lord.loadID);
            // 6. CompGuest keys on the visitors, a Hospitality job on the first, a Hospitality thought on the second
            for (int i = 0; i < visitors.Count; i++)
            {
                var pn = mapNode.SelectSingleNode("things/thing[id='" + visitors[i].ThingID + "']") as XmlElement;
                if (pn == null) continue;
                pn.AppendChild(MakeEl(doc, "arrived", "True"));
                pn.AppendChild(MakeEl(doc, "chat", i == 0 ? "True" : "False"));
                pn.AppendChild(MakeEl(doc, "guestArea", area.GetUniqueLoadID()));
                pn.AppendChild(MakeEl(doc, "lord", "Lord_" + lord.loadID));
                if (i == 0)
                {
                    var jobs = pn["jobs"];
                    var cj = jobs?["curJob"]; var cd = jobs?["curDriver"] as XmlElement;
                    if (cj?["def"] != null && cd != null) { cj["def"].InnerText = "ClaimGuestBed"; cd.SetAttribute("Class", "Hospitality.JobDriver_ClaimBed"); n++; }
                }
                if (i == visitors.Count - 1)
                {
                    var mem = pn.SelectSingleNode("needs/needs/li[@Class='Need_Mood']/thoughts/memories/memories");
                    if (mem != null)
                    {
                        var li = doc.CreateElement("li"); li.SetAttribute("Class", "Thought_Memory");
                        li.InnerXml = "<def>GuestAngered</def><sourcePrecept>null</sourcePrecept><otherPawn>null</otherPawn><moodPowerFactor>1</moodPowerFactor><age>100</age>";
                        mem.AppendChild(li); n++;
                    }
                }
                n++;
            }
            return n;
        }

        static XmlElement MakeEl(XmlDocument d, string name, string text) { var e = d.CreateElement(name); e.InnerText = text; return e; }

        IEnumerable<int> PhaseB(TestContext t)
        {
            phase = 2;
            t.Note("phase B: " + mode + " Hospitality save loaded");
            var last = HospitalityImporter.LastLoad;
            t.Check(last != null, "importer saw the load");
            var rec = ImportedGuestBeds.Current;
            t.Check(rec != null, "ImportedGuestBeds component present");
            if (rec == null || last == null) yield break;
            t.Note("summary: " + string.Join("; ", HospitalityImporter.LastSummary));
            t.Check(rec.Beds.Count == expectBeds, $"imported guest beds: {rec.Beds.Count} (expected {expectBeds})");
            if (expectFeeSum >= 0) t.Check(rec.Beds.Sum(b => b.Fee) == expectFeeSum, "rental fees kept: " + string.Join(",", rec.Beds.Select(b => b.Fee)));
            var map = Find.CurrentMap;
            var beds = map.listerThings.ThingsInGroup(ThingRequestGroup.Bed).OfType<Building_Bed>().ToList();
            // With the Inn module on, the importer hands each bed to the Inn (guest bed comp) and marks it adopted, after which
            // IsImportedGuestBed (= "still waiting to be adopted") is false by design. So look the beds up by "was imported".
            var imported = beds.Where(HospitalityImport.WasImported).ToList();
            t.Check(imported.Count == expectBeds, "imported beds found on the map: " + string.Join(", ", imported.Select(b => b.ThingID)));
            t.Check(imported.All(b => b.GetType() == typeof(Building_Bed) && !b.def.defName.EndsWith("Guest")), "imported beds are plain vanilla beds with base defs");
            t.Check(imported.All(b => b.Faction == Faction.OfPlayer), "imported beds still belong to the colony");
            t.Check(beds.Count(b => !HospitalityImport.WasImported(b)) >= 1, "normal beds stay normal");
            if (Modules.On(ModuleId.Inn) && GenTypes.GetTypeInAnyAssembly("Hearth.Guests.CompHH_GuestBed") != null)
            {
                t.Check(imported.All(b => b.GetComp<Hearth.Guests.CompHH_GuestBed>()?.Flag == true), "Inn is on: every imported bed is a guest bed of the Inn");
                t.Check(!HospitalityImport.PendingBeds.Any(), "Inn is on: nothing is left waiting for adoption");
                foreach (var b in imported)
                {
                    var c = b.GetComp<Hearth.Guests.CompHH_GuestBed>(); int fee = rec.Beds.First(r => r.ThingIdNumber == b.thingIDNumber).Fee;
                    if (c == null) continue;
                    if (fee <= 0) t.Check(c.PriceMode == Hearth.Guests.BedPriceMode.Free, "fee 0 became a free bed: " + b.ThingID);
                    else t.Check(c.PriceMode == Hearth.Guests.BedPriceMode.Manual && c.ManualPrice == fee, $"fee {fee} became a fixed price: {c.PriceMode}/{c.ManualPrice}");
                }
            }
            t.Check(imported.All(b => HospitalityImport.RentalFee(b) == rec.Beds.First(r => r.ThingIdNumber == b.thingIDNumber).Fee), "RentalFee API answers");
            t.Check(!map.components.Any(c => c is HospitalityMapStub || c is HospitalityDroppedMapComponent), "map stubs removed after load");
            t.Check(Current.Game.components.Count(c => c is ImportedGuestBeds) == 1, "game component stand-in removed after load (one import record left)");
            t.Check(map.lordManager.lords.All(l => !(l.LordJob is HospitalityLordJobStub)), "no stub lord left");
            if (mode == "synthetic")
            {
                var ms = HospitalityImport.SettingsFor(map);
                t.Check(ms != null && !ms.GuestsWelcome && ms.RefuseUntilBeds, "map settings imported (guests not welcome, wait for beds)");
                t.Check(rec.Areas.Any(a => a.Kind == "guest" && a.Label == expectAreaLabel), "guest area recorded: " + string.Join(", ", rec.Areas.Select(a => a.Label + "/" + a.Kind + "/" + a.Cells)));
                t.Check(rec.Areas.Count(a => a.Kind == "guest") == 1, "area recorded once although referenced by map and visitors");
                t.Check(rec.Visitors.Count >= 1, "known visitors recorded: " + rec.Visitors.Count);
                t.Check(rec.Visitors.Any(v => v.Entertain), "visitor 'entertain' flag kept");
                t.Check(last.LordsDropped == 1, "the Hospitality visit lord was dropped by vanilla: " + last.LordsDropped);
                // they may already have walked off the map by now, so check what the importer did, and the ones still here
                t.Check(HospitalityImporter.LastGuestsSentHome >= 1, "visitors sent home in peace with a vanilla exit lord: " + HospitalityImporter.LastGuestsSentHome);
                var stillHere = map.lordManager.lords.Where(l => l.LordJob is LordJob_ExitMapBest).Sum(l => l.ownedPawns.Count);
                t.Note(stillHere + " of them still on the map");
                var former = rec.Visitors.Select(v => map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == v.ThingIdNumber)).Where(p => p != null).ToList();
                t.Check(former.All(p => p.GetLord() != null), "every former Hospitality guest still on the map has a lord (" + former.Count + " on map)");
                t.Check(last.JobsDropped >= 1 || last.Dropped.ContainsKey("job"), "Hospitality job dropped");
                t.Check(last.Dropped.ContainsKey("thought"), "Hospitality thought dropped");
                t.Check(last.DroppedMapComponents.Count == 1, "add-on map component dropped");
                t.Check(last.DroppedGameComponents.Count == 1, "Hospitality game component dropped");
            }
            else
            {
                if (expectAreaLabel != null) t.Check(rec.Areas.Any(a => a.Label == expectAreaLabel), "guest area from the real save recorded: " + string.Join(", ", rec.Areas.Select(a => a.Label)));
                t.Note($"real save: {rec.Visitors.Count} known visitors, {last.LordsDropped} lords dropped, {last.JobsDropped} jobs dropped, unknown classes: {string.Join(", ", last.UnknownClasses)}");
            }
            t.Check(last.UnknownClasses.Count == 0, "no unhandled Hospitality classes: " + string.Join(", ", last.UnknownClasses));
            var letters = Find.LetterStack.LettersListForReading.Where(l => l.Label.ToString().Contains("Hospitality")).ToList();
            t.Check(letters.Count == 1 && rec.LetterSent, "one import letter sent");
            if (letters.Count > 0 && letters[0] is StandardLetter sl) t.Note("letter: " + sl.Text.ToString().Replace("\n", " | "));
            // red errors caused by the load (vanilla's own mod-mismatch notice is a warning, not an error)
            var loadErrors = HspTestUtil.NewErrors(errorsBefore ?? new HashSet<LogMessage>(), e => !e.StartsWith("[HHTEST]"));
            foreach (var e in loadErrors.Take(10)) t.Note("error in log: " + e.Split('\n')[0]);
            t.Check(loadErrors.Count == 0, "no red errors while loading the Hospitality save (" + loadErrors.Count + ")");
            errorsBefore = HspTestUtil.Snapshot();
            yield return 2500;
            var later = HspTestUtil.NewErrors(errorsBefore, e => !e.StartsWith("[HHTEST]"));
            foreach (var e in later.Take(10)) t.Note("error after load: " + e.Split('\n')[0]);
            t.Check(later.Count == 0, "2500 ticks after the import without new red errors");
            t.Check(map.lordManager.lords.All(l => l.LordJob != null), "all lords valid");
            // save again: the result must not depend on the stubs
            GameDataSaveLoader.SaveGame("hh_hsp_import_after");
            string xml = File.ReadAllText(GenFilePaths.FilePathForSavedGame("hh_hsp_import_after"));
            t.Check(!xml.Contains("Hearth.Compat.Hospitality.Hospitality") && !xml.Contains("Class=\"Hospitality."), "re-saved game contains no stub or Hospitality classes");
            t.Check(xml.Contains("Hearth.Compat.Hospitality.ImportedGuestBeds"), "import record saved with the game");
        }
    }

    /// <summary>Hospitality Continued active alongside us: everything of it is inert, the dialog is offered, disabling writes the mod list.</summary>
    public sealed class HspTakeoverSuite : HearthTestSuite
    {
        public override string Name => "hospcompat.takeover"; public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(HospitalityFamily.AnyReplacedActive, "Hospitality family active in this run: " + string.Join(", ", HospitalityFamily.Detected));
            if (!HospitalityFamily.AnyReplacedActive) yield break;
            t.Check(HospitalityTakeover.Active, "takeover active");
            foreach (var r in HospitalityTakeover.Report) t.Note(r);
            t.Check(HospitalityTakeover.StepFailures.Count == 0, "all takeover steps succeeded: " + string.Join("; ", HospitalityTakeover.StepFailures));
            t.Check(HospitalityTakeover.PatchesRemoved > 10, "Hospitality patches removed: " + HospitalityTakeover.PatchesRemoved);
            t.Check(HospitalityTakeover.RemainingFamilyPatches() == 0, "no Hospitality patch left anywhere");
            var familyIncidents = DefDatabase<IncidentDef>.AllDefsListForReading.Where(d => HospitalityFamily.IsTakeoverDef(d) && d.defName != "VisitorGroup").ToList();
            t.Check(familyIncidents.Count > 0 && familyIncidents.All(d => d.baseChance == 0f && d.baseChanceWithRoyalty <= 0f), "family incidents at chance 0: " + string.Join(", ", familyIncidents.Select(d => d.defName + "=" + d.baseChance)));
            var vg = IncidentDefOf.VisitorGroup;
            t.Check(HospitalityTakeover.VisitorGroupRestored && vg.workerClass == typeof(IncidentWorker_VisitorGroup) && vg.baseChance > 0f && vg.Worker.GetType() == typeof(IncidentWorker_VisitorGroup),
                "VisitorGroup back to vanilla: " + vg.workerClass?.FullName + " chance " + vg.baseChance);
            var guestsTab = DefDatabase<MainButtonDef>.GetNamedSilentFail("Guests");
            t.Check(guestsTab != null && !guestsTab.buttonVisible, "Hospitality main tab hidden");
            t.Check(HospitalityTakeover.VisibleFamilyDesignators() == 0, "no Hospitality building in the architect menu");
            var human = ThingDefOf.Human;
            t.Check(human.inspectorTabsResolved.All(tab => !HospitalityFamily.IsTakeoverType(tab.GetType())), "no Hospitality guest tab on humans");
            t.Check(human.comps.All(c => !HospitalityFamily.IsTakeoverType(c.compClass)), "no Hospitality comp on humans");
            t.Check(DefDatabase<WorkTypeDef>.AllDefsListForReading.SelectMany(w => w.workGiversByPriority).All(g => !HospitalityFamily.IsTakeoverType(g.giverClass)), "no Hospitality work giver active");
            t.Check(HospitalityTakeover.MethodsSilenced > 0, "Hospitality component/alert methods silenced: " + HospitalityTakeover.MethodsSilenced);
            var mapCompType = GenTypes.GetTypeInAnyAssembly("Hospitality.Hospitality_MapComponent");
            var tick = mapCompType?.GetMethod("MapComponentTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            t.Check(tick != null && HospitalityTakeover.IsSilenced(tick), "Hospitality_MapComponent.MapComponentTick silenced");
            t.Check(HospitalityDisabler.DialogOffered && !HospitalityDisabler.DialogShown, "disable dialog offered (not drawn in test runs)");
            // the dialog can be built and drawn once without errors
            var e0 = HspTestUtil.Snapshot();
            var dlg = new Dialog_HospitalityTakeover();
            Find.WindowStack.Add(dlg);
            yield return 2;
            Find.WindowStack.TryRemove(dlg, false);
            t.Check(HspTestUtil.NewErrors(e0).Count == 0, "dialog opens without errors");
            // vanilla visitors still come and behave; no Hospitality code runs for them
            var errs = HspTestUtil.Snapshot();
            var map = t.Map;
            var used = new HashSet<IntVec3>();
            var bed = HspTestUtil.SpawnBed(map, "BedGuest", used);
            t.Check(bed != null && HospitalityFamily.IsTakeoverType(bed.GetType()), "a Hospitality guest bed can still exist (" + bed?.GetType().FullName + ")");
            var lord = HspTestUtil.SpawnVanillaVisitors(map);
            t.Check(lord != null && lord.LordJob is LordJob_VisitColony, "vanilla visitor group arrives with a vanilla lord: " + lord?.LordJob?.GetType().FullName);
            t.Check(lord == null || lord.ownedPawns.All(p => p.AllComps.All(c => !HospitalityFamily.IsTakeoverType(c.GetType()))), "visitors carry no Hospitality comp");
            t.Check(lord == null || lord.ownedPawns.All(GuestApi.IsGuest), "visitors are our guests (GuestApi fallback)");
            yield return 3000;
            var newErr = HspTestUtil.NewErrors(errs, e => !e.StartsWith("[HHTEST]"));
            foreach (var e in newErr.Take(8)) t.Note("error: " + e.Split('\n')[0]);
            t.Check(newErr.Count == 0, "3000 ticks with Hospitality inert and visitors present: no red errors");
            t.Check(map.lordManager.lords.All(l => !HospitalityFamily.IsTakeoverType(l.LordJob?.GetType())), "no Hospitality lord was started");
            // importer side of the takeover: the live guest bed is remembered
            var rec = ImportedGuestBeds.Current;
            int n = HospitalityImporter.RegisterLiveGuestBeds(rec);
            t.Check(n == 1 && HospitalityImport.IsImportedGuestBed(bed), "live Hospitality guest bed noted for the later import");
            // the dialog's Yes path writes the mod list correctly (no restart in tests), then we put it back
            var ids = HospitalityFamily.OfferedForDisabling.Select(d => d.Entry.PackageId).ToList();
            bool ok = HospitalityDisabler.TryDisable(ids, out string failure);
            string xml = File.Exists(GenFilePaths.ModsConfigFilePath) ? File.ReadAllText(GenFilePaths.ModsConfigFilePath).ToLowerInvariant() : "";
            t.Check(ok && failure == null, "disable succeeded: " + (failure ?? "ok"));
            t.Check(!xml.Contains("<li>orion.hospitality</li>") && xml.Contains("juliusberger00.hearthharbor"), "ModsConfig.xml lists us but not Hospitality");
            foreach (var id in ids) ModsConfig.SetActive(id, true);
            ModsConfig.Save();
            t.Check(ModsConfig.IsActive("orion.hospitality"), "mod list restored after the check");
            HospitalityDisabler.SetNeverAsk(true);
            t.Check(HospitalityDisabler.NeverAsk, "never-ask setting stored");
            HospitalityDisabler.SetNeverAsk(false);
        }
    }

    /// <summary>
    /// Run with Hospitality active and HH_HSP_NOTAKEOVER=1 (Hospitality fully working): makes a real Hospitality save as fixture
    /// for hospcompat.import with HH_HSP_REAL=1: two guest beds with fees, a default guest area, a real Hospitality visit.
    /// </summary>
    public sealed class HspMakeFixtureSuite : HearthTestSuite
    {
        public override string Name => "hospcompat.makefixture"; public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(HospitalityFamily.AnyReplacedActive && !HospitalityTakeover.Active, "Hospitality active and not taken over (HH_HSP_NOTAKEOVER=1)");
            if (!HospitalityFamily.AnyReplacedActive || HospitalityTakeover.Active) yield break;
            var map = t.Map;
            var used = new HashSet<IntVec3>();
            var b1 = HspTestUtil.SpawnBed(map, "BedGuest", used);
            var b2 = HspTestUtil.SpawnBed(map, "DoubleBedGuest", used);
            var plain = HspTestUtil.SpawnBed(map, "Bed", used);
            t.Check(b1 != null && b2 != null && plain != null, "spawned 2 Hospitality guest beds and a normal bed: " + b1?.GetType().FullName);
            if (b1 == null || b2 == null) yield break;
            var setFee = b1.GetType().GetMethod("SetRentalFee");
            setFee?.Invoke(b1, new object[] { 17 }); setFee?.Invoke(b2, new object[] { 25 });
            t.Check(setFee != null, "fees set through Hospitality's own API");
            var area = HspTestUtil.MakeArea(map, "Guest wing (fixture)", b1.Position, 5);
            var mcType = GenTypes.GetTypeInAnyAssembly("Hospitality.Hospitality_MapComponent");
            var mc = map.components.FirstOrDefault(c => c.GetType() == mcType);
            t.Check(mc != null && area != null, "Hospitality map component found");
            mcType?.GetField("defaultAreaRestriction")?.SetValue(mc, area);
            mcType?.GetField("refuseGuestsUntilWeHaveBeds")?.SetValue(mc, true);
            // a real Hospitality visit: its own VisitorGroup worker
            var vg = DefDatabase<IncidentDef>.GetNamed("VisitorGroup");
            t.Note("VisitorGroup worker: " + vg.workerClass.FullName);
            var parms = StorytellerUtility.DefaultParmsNow(DefDatabase<IncidentCategoryDef>.GetNamed("FactionArrival"), map);
            parms.faction = Find.FactionManager.AllFactionsListForReading.Where(f => !f.IsPlayer && !f.HostileTo(Faction.OfPlayer) && !f.def.hidden && f.def.humanlikeFaction && !f.temporary).OrderBy(f => f.loadID).FirstOrDefault();
            parms.points = 300f;
            bool fired = vg.Worker.TryExecute(parms);
            t.Check(fired, "Hospitality visitor group fired");
            int waited = 0;
            while (waited < 8000 && !map.lordManager.lords.Any(l => HospitalityFamily.IsTakeoverType(l.LordJob?.GetType()))) { yield return 250; waited += 250; }
            var hl = map.lordManager.lords.FirstOrDefault(l => HospitalityFamily.IsTakeoverType(l.LordJob?.GetType()));
            t.Check(hl != null, "a Hospitality visit lord exists after " + waited + " ticks: " + hl?.LordJob.GetType().FullName + " with " + (hl?.ownedPawns.Count ?? 0) + " guests");
            yield return 1500;
            GameDataSaveLoader.SaveGame("hh_hsp_fixture");
            string src = GenFilePaths.FilePathForSavedGame("hh_hsp_fixture");
            t.Check(File.Exists(src), "fixture saved");
            Directory.CreateDirectory(Path.GetDirectoryName(HspTestUtil.FixturePath));
            File.Copy(src, HspTestUtil.FixturePath, true);
            File.WriteAllLines(HspTestUtil.FixturePath + ".expect", new[] { "beds=2", "feeSum=42", "area=" + area.Label });
            string xml = File.ReadAllText(src);
            t.Check(xml.Contains("Hospitality.Building_GuestBed") && xml.Contains("<RentalFee>17</RentalFee>"), "fixture contains Hospitality guest beds with fee");
            t.Check(xml.Contains("Hospitality.Hospitality_MapComponent"), "fixture contains Hospitality's map component");
            t.Note("fixture written to " + HspTestUtil.FixturePath + " (" + new FileInfo(HspTestUtil.FixturePath).Length / 1024 + " KB)");
        }
    }
}
#endif
