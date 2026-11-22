// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// Entry point of the Inn &amp; Hotel module: the guest core (KONZEPT 5). Guest register, guest beds, arrivals,
    /// guest lord and behaviour, guest wings, satisfaction and departure reports, handling modes.
    /// Nothing happens until the player marks a guest bed or opens a venue (N08).
    /// </summary>
    public sealed class InnModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Inn;

        public override void RegisterSettings() => InnSettings.Register();

        public override void OnDefsLoaded()
        {
            int beds = 0;
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!d.IsBed || d.building == null || !d.building.bed_humanlike) continue;
                if (d.thingClass == null || !typeof(Building_Bed).IsAssignableFrom(d.thingClass)) continue;
                if (d.building.bed_crib || (ModsConfig.BiotechActive && d == ThingDefOf.DeathrestCasket)) continue;
                if (d.comps == null) d.comps = new List<CompProperties>();
                if (d.comps.Any(c => c is CompProperties_HH_GuestBed)) continue;
                d.comps.Add(new CompProperties_HH_GuestBed());
                beds++;
            }
            GuestApi.Impl = new GuestApiBridge();
            Payments.Register(new InnCardProvider());
            Caretaker.Register(ModuleId.Inn, "Inn.GuestRecords", () => GuestRegistry.Instance?.CaretakerRecords() ?? 0);
            Caretaker.Register(ModuleId.Inn, "Inn.GuestBedOwners", () => GuestRegistry.Instance?.CaretakerBeds() ?? 0);
            LongEventHandler.ExecuteWhenFinished(AddDesignators);
            GuestOverviewPage.Register();
            WireOtherModules();
            // 12.6: Hospitality alongside us is a mistake; the takeover dialog lives in the HospCompat module. We only note it.
            foreach (var id in new[] { "Orion.Hospitality", "Mlie.HospitalityContinued", "Orion.Hospitality.Continued" })
                if (ModLister.GetActiveModWithIdentifier(id, ignorePostfix: true) != null)
                    HLog.Warn("Inn", "Hospitality (" + id + ") is active alongside Hearth & Harbor. Inn guests are ours; Hospitality's visitors are left alone. The takeover dialog explains how to switch it off.");
            HLog.Info("Inn", $"Guest bed switch added to {beds} bed types; guest API, card payments and caretaker checks registered");
        }

        /// <summary>Hooks offered by other modules (all in Hearth.dll, so direct calls). Each handler checks that the Inn is on.</summary>
        static void WireOtherModules()
        {
            // Underworld scandals change the house's renown (already capped by the tier there).
            Hearth.Underworld.Underworld.RenownDelta += (map, delta, reason) =>
                Guard.Run(ModuleId.Inn, "Inn.UnderworldRenown", () => { if (Modules.On(ModuleId.Inn)) GuestRegistry.Instance?.ChangeRenown(delta, "underworld: " + reason); });
            // Guest beds imported from a Hospitality save become our guest beds.
            Hearth.Compat.Hospitality.HospitalityImport.GuestBedImported += (bed, fee) =>
                Guard.Run(ModuleId.Inn, "Inn.AdoptImportedBed", () => AdoptImportedBed(bed, fee));
            // UI shell: guest beds count as "the mod is used" and complete the guide step; renown header; wing status lines.
            Hearth.Shell.HearthUI.GuestBedProbe = map => Modules.On(ModuleId.Inn) && GuestBeds.CountOn(map) > 0;
            if (Hearth.Shell.HearthUI.RenownLine == null)
                Hearth.Shell.HearthUI.RenownLine = () => Modules.On(ModuleId.Inn) && GuestRegistry.Instance != null
                    ? "HH.Inn.RenownLine".TranslateOr("Renown {0} {1}").Replace("{0}", GuestRegistry.StarsText()).Replace("{1}", GuestRegistry.Instance.Renown.ToString("0"))
                    : null;
            Hearth.Shell.HearthUI.AddOverviewSection("inn.wings", 20, ls =>
            {
                var map = Find.CurrentMap; if (map == null || !Modules.On(ModuleId.Inn)) return;
                foreach (var line in WingStatus.Lines(map)) ls.Label(line);
            });
        }

        /// <summary>Turns a bed imported from Hospitality into one of our guest beds (fee is replaced by our own pricing, 5.11).</summary>
        public static bool AdoptImportedBed(Building_Bed bed, int fee)
        {
            if (bed == null || bed.Destroyed || !Modules.On(ModuleId.Inn)) return false;
            var comp = GuestBeds.Comp(bed); if (comp == null) return false;
            comp.SetGuestBed(true, byPlayer: false);
            Hearth.Compat.Hospitality.HospitalityImport.MarkAdopted(bed);
            HLog.Info("Inn", $"Adopted guest bed {bed.ThingID} from Hospitality (old fee {fee} silver; our own pricing applies)");
            return true;
        }

        public override void OnGameLoaded()
        {
            // beds imported before the Inn module was switched on (or before the handler existed) are adopted on load
            var pending = new HashSet<int>(Hearth.Compat.Hospitality.HospitalityImport.PendingBeds);
            if (pending.Count == 0 || !Modules.On(ModuleId.Inn)) return;
            foreach (var map in Find.Maps)
                foreach (var b in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().ToList())
                    if (pending.Contains(b.thingIDNumber)) Guard.Run(ModuleId.Inn, "Inn.AdoptImportedBed", () => AdoptImportedBed(b, Hearth.Compat.Hospitality.HospitalityImport.RentalFee(b)));
        }

        static void AddDesignators()
        {
            var cat = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("HH_Hospitality") ?? DesignationCategoryDefOf.Zone;
            foreach (var t in new[] { typeof(Designator_HH_GuestWingExpand), typeof(Designator_HH_GuestWingClear) })
            {
                if (!cat.specialDesignatorClasses.Contains(t)) cat.specialDesignatorClasses.Add(t);
                if (cat.AllResolvedDesignators.Any(x => x.GetType() == t)) continue;
                try { var d = (Designator)Activator.CreateInstance(t); d.isOrder = true; cat.AllResolvedDesignators.Add(d); }
                catch (Exception e) { HLog.Error("Inn", "Could not add designator " + t.Name, e); }
            }
        }

        public override void GameTick(int ticksGame) => GuestRegistry.Instance?.Tick(ticksGame);

        public override void PrepareRemoval(List<string> report)
        {
            var reg = GuestRegistry.Instance; if (reg == null) return;
            int n = 0;
            foreach (var g in reg.Groups.ToList()) { if (!g.LeaveSent) { reg.DepartGroup(g, "removal"); n++; } }
            report.Add($"Inn: {n} guest group(s) sent home, card funds returned.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var reg = GuestRegistry.Instance;
            if (reg == null) { yield return "Inn: no game"; yield break; }
            yield return $"Inn: {reg.Records.Count} guests in {reg.Groups.Count} groups · renown {reg.Renown:0.0} ({GuestRegistry.StarsText()}) · whispers {reg.CurrentWhispers:0.0} · hosted {reg.GroupsHosted} groups / {reg.GuestsHosted} guests";
            foreach (var map in Find.Maps)
            {
                var s = map.GetComponent<MapComponent_HH_GuestScheduler>();
                yield return $"Inn {map}: {GuestBeds.CountOn(map)} guest beds ({GuestBeds.FreeOn(map).Count} free), forecast {s?.Forecast.Count ?? 0}, wings {GuestWings.Comp(map)?.wings.Count ?? 0}";
            }
        }
    }

    /// <summary>Thin bridge so the contract always talks to the current game's register.</summary>
    public sealed class GuestApiBridge : IGuestApiImpl, IGuestRenownImpl
    {
        static GuestRegistry R => GuestRegistry.Instance;
        public bool IsGuest(Pawn p) => R != null && R.IsGuest(p);
        public GuestInfo Info(Pawn p) => R?.Info(p);
        public IEnumerable<Pawn> GuestsOn(Map map) => R?.GuestsOn(map) ?? Enumerable.Empty<Pawn>();
        public void AddSatisfaction(Pawn p, float delta, string reason) => R?.AddSatisfaction(p, delta, reason);
        public void Spend(Pawn p, Money amount, string category) => R?.Spend(p, amount, category);
        public float Renown(Map map) => R?.Renown ?? GuestRegistry.StartRenown;
        public float Whispers(Map map) => R?.CurrentWhispers ?? 0f;
        public float AddRenown(Map map, float delta, string reason) => R?.ChangeRenown(delta, reason) ?? 0f;
    }
}
