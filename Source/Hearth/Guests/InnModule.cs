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
            HLog.Info("Inn", $"Guest bed switch added to {beds} bed types; guest API, card payments and caretaker checks registered");
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
            yield return $"Inn: {reg.Records.Count} guests in {reg.Groups.Count} groups · renown {reg.Renown:0.0} ({GuestRegistry.StarsText()}) · whispers {reg.Whispers:0.0} · hosted {reg.GroupsHosted} groups / {reg.GuestsHosted} guests";
            foreach (var map in Find.Maps)
            {
                var s = map.GetComponent<MapComponent_HH_GuestScheduler>();
                yield return $"Inn {map}: {GuestBeds.CountOn(map)} guest beds ({GuestBeds.FreeOn(map).Count} free), forecast {s?.Forecast.Count ?? 0}, wings {GuestWings.Comp(map)?.wings.Count ?? 0}";
            }
        }
    }

    /// <summary>Thin bridge so the contract always talks to the current game's register.</summary>
    public sealed class GuestApiBridge : IGuestApiImpl
    {
        static GuestRegistry R => GuestRegistry.Instance;
        public bool IsGuest(Pawn p) => R != null && R.IsGuest(p);
        public GuestInfo Info(Pawn p) => R?.Info(p);
        public IEnumerable<Pawn> GuestsOn(Map map) => R?.GuestsOn(map) ?? Enumerable.Empty<Pawn>();
        public void AddSatisfaction(Pawn p, float delta, string reason) => R?.AddSatisfaction(p, delta, reason);
        public void Spend(Pawn p, Money amount, string category) => R?.Spend(p, amount, category);
    }
}
