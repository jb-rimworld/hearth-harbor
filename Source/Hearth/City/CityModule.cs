// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Hearth.City
{
    /// <summary>
    /// City Life (KONZEPT 6.9, 5.19): cinema and movie nights, beauty salon, gallery, park benches, taxi stand, personal security.
    /// Everything stays dormant until the player builds something (N08): no screen, chair, sign or stand, no effect.
    /// </summary>
    public sealed class CityModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.CityLife;

        public override void RegisterSettings() => CitySettings.Register();

        public override void OnDefsLoaded()
        {
            GuestActivities.Register(new CinemaActivity());
            GuestActivities.Register(new SalonActivity());
            GuestActivities.Register(new GalleryActivity());
            GuestActivities.Register(new ParkActivity());
            HearthTabs.Register(new HearthTabPage
            {
                Id = "city", LabelKey = "HH.Tab.City", LabelEn = "City life", Module = ModuleId.CityLife, Order = 70,
                Draw = CityTab.Draw, Visible = CityTab.AnythingBuilt,
            });
            Caretaker.Register(ModuleId.CityLife, "city.contracts", CityCaretaker.CheckContracts);
            Caretaker.Register(ModuleId.CityLife, "city.screens", CityCaretaker.CheckScreens);
            HLog.Info("City", "City Life ready: 4 guest activities (cinema, salon, gallery, park), taxi, security");
        }

        public override void OnGameStarted() { CityScan.Clear(); CityAccess.AutoFinishResearch(); }
        public override void OnGameLoaded() { CityScan.Clear(); CityAccess.AutoFinishResearch(); }

        public override void GameTick(int t)
        {
            if (t % 600 == 11) CityAccess.AutoFinishResearch();
            if (t % 60 == 7) Guard.Run(ModuleId.CityLife, "Taxi.Tick", TaxiService.Tick);
            if (t % 250 == 123) Guard.Run(ModuleId.CityLife, "Security.Tick", SecurityService.Tick);
            if (t % 250 == 77) Guard.Run(ModuleId.CityLife, "MovieNights.Tick", MovieNights.Tick);
        }

        public override void PrepareRemoval(List<string> report)
        {
            var data = CityGameComponent.Instance;
            if (data != null)
            {
                int n = 0;
                foreach (var c in data.Contracts.Where(x => !x.Ended).ToList()) { SecurityService.End(c, "module removed"); n++; }
                if (n > 0) report.Add($"{n} bodyguard contract(s) ended.");
            }
            int stopped = 0;
            foreach (var map in Find.Maps)
            {
                var def = CityDefs.ProjectorScreen; if (def == null) continue;
                foreach (var t in map.listerThings.ThingsOfDef(def)) { var c = t.TryGetComp<CompProjectorScreen>(); if (c != null && c.ShowActive) { c.StopShow(); stopped++; } }
            }
            if (stopped > 0) report.Add($"{stopped} running film(s) stopped.");
            report.Add("Projector screens, salon chairs, park benches and taxi stands stay as ordinary buildings.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var data = CityGameComponent.Instance;
            yield return $"City Life: research level 1 {(CityAccess.Level1 ? "open" : "closed")}, level 2 {(CityAccess.Level2 ? "open" : "closed")}, research mode {(CityAccess.ResearchOff ? "off" : "full")}";
            if (data == null) yield break;
            foreach (var k in data.Kinds.OrderBy(x => x)) yield return $"  {k}: {data.Count(k)} uses, {data.Revenue(k)} silver";
            int open = data.Contracts.Count(c => !c.Ended);
            yield return $"  bodyguard contracts: {open} running, {data.Contracts.Count} stored";
        }
    }

    /// <summary>Colonists start a movie night now and then (every evening a dice roll, only with a powered screen and enough seats).</summary>
    public static class MovieNights
    {
        public const int EveningHour = 18;

        public static void Tick()
        {
            if (!CityAccess.Level1 || !CitySettings.B(CitySettings.MovieNight)) return;
            var data = CityGameComponent.Instance; var def = DefDatabase<GatheringDef>.GetNamedSilentFail("HH_Screening");
            if (data == null || def == null) return;
            int now = Find.TickManager.TicksGame;
            if (now - data.LastMovieNightTick < 40000) return;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome || CityDefs.ProjectorScreen == null || map.listerThings.ThingsOfDef(CityDefs.ProjectorScreen).Count == 0) continue;
                if (GenLocalDate.HourOfDay(map) != EveningHour) continue;
                data.LastMovieNightTick = now;
                if (!JRand.Stream("city.movieNight").Chance(CitySettings.F(CitySettings.MovieNightChance))) return;
                if (map.lordManager.lords.Any(l => l.LordJob is LordJob_Joinable_Gathering)) return;
                if (!def.CanExecute(map)) return;
                if (!GatheringsUtility.EnoughPotentialGuestsToStartGathering(map, def)) return;
                if (def.Worker.TryExecute(map)) HLog.Info("City", "Movie night started on " + map);
                return;
            }
        }
    }

    public static class CityCaretaker
    {
        /// <summary>Contracts whose guest or guard vanished (despawned, dead, saved away) are closed.</summary>
        public static int CheckContracts()
        {
            var data = CityGameComponent.Instance; if (data == null) return 0;
            int n = 0;
            foreach (var c in data.Contracts.Where(x => !x.Ended).ToList())
            {
                if (c.Guest == null || c.Guest.Destroyed || c.Guest.Dead || !c.Guest.Spawned)
                {
                    SecurityService.End(c, "guest gone (caretaker)"); n++; Caretaker.Fixed("city.contracts", "contract of a guest who is no longer on the map");
                }
            }
            return n;
        }

        /// <summary>A screening whose end tick lies absurdly far ahead (edited save) is cut short.</summary>
        public static int CheckScreens()
        {
            int n = 0; int now = Find.TickManager.TicksGame;
            foreach (var map in Find.Maps)
            {
                var def = CityDefs.ProjectorScreen; if (def == null) continue;
                foreach (var t in map.listerThings.ThingsOfDef(def))
                {
                    var c = t.TryGetComp<CompProjectorScreen>();
                    if (c != null && c.ShowEnd - now > 30000) { Caretaker.Fixed("city.screens", $"screening end {c.ShowEnd - now} ticks ahead at {t}"); c.StopShow(); n++; }
                }
            }
            return n;
        }
    }

    /// <summary>The "City life" page of the main tab: what is built, what it earned, what is unlocked.</summary>
    public static class CityTab
    {
        public static bool AnythingBuilt()
        {
            var map = Find.CurrentMap; if (map == null) return false;
            foreach (var d in new[] { CityDefs.ProjectorScreen, CityDefs.SalonChair, CityDefs.ParkBench, CityDefs.TaxiStand })
                if (d != null && map.listerThings.ThingsOfDef(d).Count > 0) return true;
            return CityScan.Galleries(map).Count > 0;
        }

        static Vector2 scroll;

        public static void Draw(Rect rect)
        {
            var map = Find.CurrentMap; var data = CityGameComponent.Instance;
            if (map == null || data == null) return;
            var view = new Rect(0, 0, rect.width - 20f, 640f);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var l = new Listing_Standard(); l.Begin(view);
            Text.Font = GameFont.Medium; l.Label("HH.City.TabTitle".TranslateOr("City life")); Text.Font = GameFont.Small;
            l.Gap(4f);
            l.Label("HH.City.TabResearch".TranslateOr("Research: level 1 (cinema, salon, gallery): {0}; level 2 (taxi, security): {1}")
                .Replace("{0}", CityAccess.Level1 ? "open" : "closed").Replace("{1}", CityAccess.Level2 ? "open" : "closed"));
            l.GapLine();

            var cinemas = CityScan.Cinemas(map);
            int screens = CityDefs.ProjectorScreen == null ? 0 : map.listerThings.ThingsOfDef(CityDefs.ProjectorScreen).Count;
            l.Label($"Cinema: {screens} screen(s), {cinemas.Count} with a venue sign; {data.Count("cinema")} tickets, {data.Revenue("cinema").ToStringShort()} silver");
            foreach (var s in cinemas) l.Label($"   {s.Venue.Comp.DisplayName}: {s.Seats} seats, " + (s.Comp.Powered ? (s.Comp.ShowActive ? "showing " + s.Comp.FilmTitle : "idle") : "no power"));
            var salons = CityScan.Salons(map);
            l.Label($"Salon: {salons.Sum(s => s.Chairs.Count)} chair(s) in {salons.Count} salon(s); {data.Count("salon")} new looks, {data.Revenue("salon").ToStringShort()} silver");
            var gal = CityScan.Galleries(map);
            l.Label($"Gallery: {gal.Count} gallery(ies), {gal.Sum(g => g.Art.Count)} artwork(s); {data.Count("gallery")} visits, {data.Revenue("gallery").ToStringShort()} silver");
            int benches = CityDefs.ParkBench == null ? 0 : map.listerThings.ThingsOfDef(CityDefs.ParkBench).Count;
            l.Label($"Park: {benches} bench(es); {data.Count("park")} guest sits");
            l.Label($"Taxi: {(CityDefs.TaxiStand == null ? 0 : map.listerThings.ThingsOfDef(CityDefs.TaxiStand).Count)} stand(s); {data.Count("taxi")} rides, {data.Count("taxiArrival")} landings, {(data.Revenue("taxi") + data.Revenue("taxiArrival")).ToStringShort()} silver");
            int running = data.Contracts.Count(c => !c.Ended && c.Started);
            l.Label($"Security: {running} bodyguard(s) on duty, {data.Revenue("security").ToStringShort()} silver earned");
            l.GapLine();
            l.Label($"Total takings of City Life: {data.TotalRevenue.ToStringShort()} silver");
            l.End();
            Widgets.EndScrollView();
        }
    }
}
