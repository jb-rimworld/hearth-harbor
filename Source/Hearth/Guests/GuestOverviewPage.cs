// SPDX-License-Identifier: MIT
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// A plain "Guests" page for the Hearth &amp; Harbor main tab (10.4, first version): renown, the arrival forecast and
    /// everybody staying right now. The UI module may replace it with a richer overview (same data, public API).
    /// </summary>
    public static class GuestOverviewPage
    {
        static Vector2 scroll;

        public static void Register()
        {
            HearthTabs.Register(new HearthTabPage
            {
                Id = "inn.overview", LabelKey = "HH.Inn.Tab.Guests", LabelEn = "Guests", Module = ModuleId.Inn, Order = 10,
                Draw = r => Guard.Run(ModuleId.Inn, "Inn.OverviewPage", () => Draw(r)),
            });
        }

        public static void Draw(Rect rect)
        {
            var reg = GuestRegistry.Instance; var map = Find.CurrentMap;
            if (reg == null || map == null) return;
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
            var guests = reg.OnMap(map).Where(r => r.Pawn != null).ToList();
            var forecast = sch?.GetForecast(HSettings.GetInt(InnSettings.ForecastDays));
            float h = 120f + (forecast?.Count ?? 0) * 24f + guests.Count * 26f + 80f;
            var view = new Rect(0, 0, rect.width - 16f, Mathf.Max(h, rect.height));
            Widgets.BeginScrollView(rect, ref scroll, view);
            var ls = new Listing_Standard();
            ls.Begin(view);
            Text.Font = GameFont.Medium;
            ls.Label("HH.Inn.Tab.Title".TranslateOr("Guests") + "  " + GuestRegistry.StarsText());
            Text.Font = GameFont.Small;
            ls.Label("HH.Inn.Tab.Summary".TranslateOr("Renown {0} · Whispers {1} · {2} groups hosted · {3} guest beds ({4} free)")
                .Replace("{0}", reg.Renown.ToString("0.0")).Replace("{1}", reg.Whispers.ToString("0.0")).Replace("{2}", reg.GroupsHosted.ToString())
                .Replace("{3}", GuestBeds.CountOn(map).ToString()).Replace("{4}", GuestBeds.FreeOn(map).Count.ToString()));
            ls.GapLine();
            ls.Label("HH.Inn.Tab.Forecast".TranslateOr("Expected arrivals"));
            if (sch == null || !sch.Active) ls.Label("   " + "HH.Inn.Tab.Inactive".TranslateOr("Nobody is coming: mark a bed as guest bed or open a venue."));
            else if (forecast.Count == 0) ls.Label("   " + "HH.Inn.Tab.NoneYet".TranslateOr("No groups planned yet."));
            else foreach (var g in forecast) ls.Label("   " + g.Describe(map));
            ls.GapLine();
            ls.Label("HH.Inn.Tab.Staying".TranslateOr("Staying now"));
            if (guests.Count == 0) ls.Label("   " + "HH.Inn.Tab.NoGuests".TranslateOr("No guests at the moment."));
            foreach (var r in guests)
            {
                var row = ls.GetRect(24f);
                Widgets.DrawHighlightIfMouseover(row);
                string room = r.Bed != null ? (r.RoomLabel ?? "?") + " · " + r.NightPrice.WholeSilver + "/night" : (r.Overnight ? "HH.Inn.Tab.NoRoom".TranslateOr("no room yet") : "HH.Inn.Report.Day".TranslateOr("day visitor"));
                string line = $"{r.Pawn.LabelShort} ({r.Pawn.Faction?.Name}) · {room} · " +
                              "HH.Inn.Report.Budget".TranslateOr("budget {0}").Replace("{0}", r.Budget.WholeSilver.ToString()) + " · " +
                              "HH.Inn.Report.Mood".TranslateOr("satisfaction {0}").Replace("{0}", ((r.Satisfaction + 1f) * 50f).ToString("0") + "%") + " · " + r.Handling;
                Widgets.Label(row, line);
                if (Widgets.ButtonInvisible(row)) CameraJumper.TryJumpAndSelect(r.Pawn);
                TooltipHandler.TipRegion(row, () => string.Join("\n", r.Events.Skip(System.Math.Max(0, r.Events.Count - 12))), r.PawnId);
            }
            ls.End();
            Widgets.EndScrollView();
        }
    }
}
