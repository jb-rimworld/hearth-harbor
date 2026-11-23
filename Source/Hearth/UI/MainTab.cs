// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Silverline;

namespace Hearth.Shell
{
    /// <summary>Main bar button "Hearth &amp; Harbor". Invisible until the colony actually uses the mod (N08).</summary>
    public class MainButtonWorker_HH : MainButtonWorker_ToggleTab
    {
        public override bool Visible
        {
            get
            {
                if (!base.Visible) return false;
                return HearthUI.IsUsedInColony();
            }
        }
    }

    /// <summary>The main tab (10.1): a slim list of pages on the left, the selected page on the right.</summary>
    public class MainTabWindow_HH : MainTabWindow
    {
        public static string SelectedPage = "overview";
        public override Vector2 RequestedTabSize => new Vector2(1000f, 640f);
        const float ListW = 170f;

        public override void PreOpen()
        {
            base.PreOpen();
            HearthUI.InvalidateUsage();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Guard.Run(ModuleId.Core, "UI.MainTab", () => Draw(inRect));
        }

        public static List<HearthTabPage> Pages() => HearthTabs.VisiblePages.ToList();

        void Draw(Rect inRect)
        {
            UiProbe.Hit("maintab");
            var pages = Pages();
            if (pages.Count == 0) { UiStyle.LabelColored(inRect, "HH.UI.Tab.NoPages".Translate(), UiStyle.Grey); return; }
            if (pages.All(p => p.Id != SelectedPage)) SelectedPage = pages[0].Id;

            // header
            Text.Font = GameFont.Medium;
            GUI.color = UiStyle.Amber; Widgets.Label(new Rect(inRect.x, inRect.y, 360f, 34f), "Hearth & Harbor"); GUI.color = Color.white;
            Text.Font = GameFont.Small;
            string renown = null;
            if (HearthUI.RenownLine != null) renown = Guard.Run(ModuleId.Core, "UI.RenownLine", HearthUI.RenownLine, null);
            if (!renown.NullOrEmpty()) UiStyle.LabelColored(new Rect(inRect.x + 260f, inRect.y + 4f, inRect.width - 560f, 28f), renown, UiStyle.Grey, TextAnchor.MiddleLeft);
            if (Widgets.ButtonText(new Rect(inRect.xMax - 130f, inRect.y + 2f, 130f, 28f), "HH.UI.Tab.Settings".Translate())) OpenSettings();
            if (Widgets.ButtonText(new Rect(inRect.xMax - 270f, inRect.y + 2f, 130f, 28f), "HH.UI.Tab.Guide".Translate()))
                HSettings.Set("core.guide", HSettings.GetBool("core.guide") ? 0f : 1f);
            TooltipHandler.TipRegion(new Rect(inRect.xMax - 270f, inRect.y + 2f, 130f, 28f), "HH.UI.Tab.GuideTip".Translate());

            var body = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
            var list = new Rect(body.x, body.y, ListW, body.height);
            var content = new Rect(list.xMax + 12f, body.y, body.width - ListW - 12f, body.height);
            float y = list.y;
            foreach (var p in pages)
            {
                var row = new Rect(list.x, y, list.width, 30f);
                bool sel = p.Id == SelectedPage;
                if (sel) { Widgets.DrawBoxSolid(row, UiStyle.AmberSoft); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), UiStyle.Amber); }
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                UiStyle.LabelColored(new Rect(row.x + 12f, row.y, row.width - 14f, row.height), p.Label, Color.white, TextAnchor.MiddleLeft);
                if (Widgets.ButtonInvisible(row)) SelectedPage = p.Id;
                y += 32f;
            }
            Widgets.DrawBoxSolid(new Rect(list.xMax + 5f, body.y, 1f, body.height), UiStyle.Line);
            var current = pages.First(p => p.Id == SelectedPage);
            Widgets.BeginGroup(content);
            Guard.Run(current.Module, "Tab.Draw." + current.Id, () => current.Draw?.Invoke(new Rect(0, 0, content.width, content.height)));
            Widgets.EndGroup();
        }

        public static void OpenSettings()
        {
            try { Find.WindowStack.Add(new Dialog_ModSettings(HearthMod.Instance)); }
            catch (Exception e) { HLog.Warn("UI", "Could not open mod settings: " + e.Message); }
        }
    }

    /// <summary>The "Overview" page: guests today, income today, renown, plus whatever other modules add.</summary>
    public static class OverviewPage
    {
        static Vector2 scroll; static float viewH = 400f;

        /// <summary>Silver taken into the colony tills today. Uses the journal (exact inflows); falls back to the daily net if the journal does not reach back to dawn.</summary>
        public static Money TodayIncome()
        {
            var ledger = Ledger.Current; if (ledger == null) return Money.Zero;
            int now = Find.TickManager.TicksGame, dayStart = now / 60000 * 60000;
            var inflow = Money.Zero;
            var j = ledger.Journal;
            for (int i = j.Count - 1; i >= 0; i--)
            {
                var e = j[i];
                if (e.Tick < dayStart) break;
                if (e.To != null && e.To.StartsWith("colony:till") && !(e.From != null && e.From.StartsWith("colony:till"))) inflow += e.Amount;
            }
            bool covered = j.Count < Ledger.JournalMax || j[0].Tick <= dayStart;
            var net = ledger.DayTotalPrefix(now / 60000, "colony:till");
            return covered ? inflow : Money.Max(inflow, net);
        }

        /// <summary>Income categories the ledger knows by name (Ledger.Post category). Other money into the tills shows as "other".</summary>
        public static readonly string[] KnownCategories = { "inn.room", "food", "shop", "vending", "drink", "casino", "spa", "cinema", "gallery", "salon", "security", "taxi", "Faith" };

        /// <summary>Income per category: today and the last <paramref name="days"/> days (today included), biggest first, zeros dropped.</summary>
        public static List<(string cat, Money today, Money week)> IncomeByCategory(int days)
        {
            var res = new List<(string, Money, Money)>();
            var ledger = Ledger.Current; if (ledger == null) return res;
            int today = Find.TickManager.TicksGame / 60000;
            Money catToday = Money.Zero, catWeek = Money.Zero;
            foreach (var c in KnownCategories)
            {
                var t = ledger.DayIncome(today, c); var wk = Money.Zero;
                for (int d = today - days + 1; d <= today; d++) if (d >= 0) wk += ledger.DayIncome(d, c);
                catToday += t; catWeek += wk;
                if (wk.Micro > 0 || t.Micro > 0) res.Add((c, t, wk));
            }
            // everything else that reached the tills (uncategorised payments, other modules' categories)
            var otherToday = Money.Max(Money.Zero, TodayIncome() - catToday);
            var allWeek = Money.Zero;
            foreach (var d in DailyIncome(days)) allWeek += d;
            var otherWeek = Money.Max(otherToday, allWeek - catWeek);
            if (otherWeek.Micro > 0) res.Add(("other", otherToday, otherWeek));
            res.Sort((a, b) => b.Item3.Micro.CompareTo(a.Item3.Micro));
            return res;
        }

        /// <summary>Income per day for the last <paramref name="days"/> days, oldest first (today = journal inflows, earlier days = category totals or till net).</summary>
        public static List<Money> DailyIncome(int days)
        {
            var res = new List<Money>();
            var ledger = Ledger.Current;
            int today = Find.TickManager.TicksGame / 60000;
            for (int d = today - days + 1; d <= today; d++)
            {
                if (ledger == null || d < 0) { res.Add(Money.Zero); continue; }
                var v = d == today ? TodayIncome() : Money.Max(ledger.DayIncomeTotal(d), ledger.DayTotalPrefix(d, "colony:till"));
                res.Add(Money.Max(Money.Zero, Money.Max(v, ledger.DayIncomeTotal(d))));
            }
            return res;
        }

        public static string CategoryLabel(string cat)
        {
            string key = "HH.UI.Income." + cat.Replace(".", "_");
            return key.CanTranslate() ? key.Translate().ToString() : cat.CapitalizeFirst();
        }

        /// <summary>Status chip of one venue: open &amp; staffed / open (self-service) / no staff / out of stock / closed.</summary>
        public static (string chip, bool good, string tip) VenueChip(Venue v)
        {
            bool hours = v.IsOpenNow;
            bool needs = Guard.Run(ModuleId.Core, "UI.VenueNeedsStaff", () => Venues.NeedsStaff(v), false);
            bool staffed = Guard.Run(ModuleId.Core, "UI.VenueStaffed", () => Venues.IsStaffed(v), false);
            if (!hours) return ("HH.UI.Venue.Closed".Translate(), false, "HH.UI.Venue.ClosedTip".Translate());
            if (needs && !staffed) return ("HH.UI.Venue.NoStaff".Translate(), false, "HH.UI.Venue.NoStaffTip".Translate());
            int stock = Guard.Run(ModuleId.Core, "UI.VenueStock", () =>
            {
                if (v.Type?.defName == Hearth.Kitchen.Restaurant.VenueDefName && v.Room != null)
                    return Hearth.Kitchen.Restaurant.Stock(new Hearth.Kitchen.RestaurantSpot { Room = v.Room, Venue = v, Map = v.Map }).Any() ? 1 : 0;
                var kind = Hearth.ShopsFood.VenueApi.SellsOf(v);
                return kind == Hearth.ShopsFood.SellKind.None ? -1 : Hearth.ShopsFood.VenueSales.Stock(v).Count;
            }, -1);
            if (stock == 0) return ("HH.UI.Venue.NoStock".Translate(), false, "HH.UI.Venue.NoStockTip".Translate());
            return (needs ? "HH.UI.Venue.OpenStaffed".Translate() : "HH.UI.Venue.Open".Translate(), true, null);
        }

        public static Money YesterdayIncome()
        {
            var ledger = Ledger.Current; if (ledger == null) return Money.Zero;
            int day = Find.TickManager.TicksGame / 60000 - 1; if (day < 0) return Money.Zero;
            return Money.Max(Money.Zero, ledger.DayTotalPrefix(day, "colony:till"));
        }

        /// <summary>"Arrivals, next 24 h: 2 groups planned (about 4 guests), 1.3 groups a day expected." Null while nobody can come (no guest bed, no venue, Inn off).</summary>
        public static string ArrivalsLine(Map map)
        {
            if (!Modules.On(ModuleId.Inn)) return null;
            var sch = map.GetComponent<Hearth.Guests.MapComponent_HH_GuestScheduler>();
            if (sch == null || !sch.Active) return null;
            sch.Next24Hours(out int groups, out int guests);
            float rate = sch.RatePerDay(sch.OvernightPossible, sch.DayVisitsPossible);
            string planned = groups == 0
                ? "HH.UI.Overview.Arrivals.None".Translate().ToString()
                : "HH.UI.Overview.Arrivals.Planned".Translate(groups, guests).ToString();
            return "HH.UI.Overview.Arrivals".Translate(planned, rate.ToString("0.0")).ToString();
        }

        public static void Draw(Rect r)
        {
            var map = Find.CurrentMap;
            var view = new Rect(0, 0, r.width - 18f, viewH);
            Widgets.BeginScrollView(r, ref scroll, view);
            float w = view.width, y = 0;
            // big numbers
            int guests = map == null ? 0 : GuestApi.GuestsOn(map).Count();
            int venues = map == null ? 0 : Venues.All(map).Count;
            var tiles = new List<(string label, string value, string tip)>
            {
                ("HH.UI.Overview.Guests".Translate(), guests.ToString(), "HH.UI.Overview.GuestsTip".Translate()),
                ("HH.UI.Overview.Income".Translate(), UiStyle.SilverWhole(TodayIncome()), "HH.UI.Overview.IncomeTip".Translate()),
                ("HH.UI.Overview.Venues".Translate(), venues.ToString(), "HH.UI.Overview.VenuesTip".Translate()),
            };
            if (BankApi.Ready) tiles.Add(("HH.UI.Overview.Bank".Translate(), UiStyle.SilverWhole(BankApi.ColonyBalance), "HH.UI.Overview.BankTip".Translate()));
            float tw = Mathf.Min(220f, (w - 12f * (tiles.Count - 1)) / tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = new Rect(i * (tw + 12f), y, tw, 78f);
                Widgets.DrawBoxSolid(t, new Color(1f, 1f, 1f, 0.05f)); GUI.color = UiStyle.Line; Widgets.DrawBox(t); GUI.color = Color.white;
                UiStyle.LabelColored(new Rect(t.x + 10f, t.y + 6f, t.width - 20f, 20f), tiles[i].label, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(t.x + 10f, t.y + 24f, t.width - 20f, 44f), tiles[i].value, i == 1 ? UiStyle.Amber : Color.white, TextAnchor.MiddleLeft, GameFont.Medium);
                TooltipHandler.TipRegion(t, tiles[i].tip);
            }
            y += 92f;

            // arrivals expected in the next 24 hours (Inn module): one compact line under the tiles
            string arrivals = map == null ? null : Guard.Run(ModuleId.Core, "UI.OverviewArrivals", () => ArrivalsLine(map), null);
            if (arrivals != null) { UiStyle.LabelColored(new Rect(4f, y - 6f, w - 8f, 20f), arrivals, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny); y += 22f; }

            // income detail
            y += UiStyle.Section(0, y, w, "HH.UI.Overview.IncomeHead".Translate());
            UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 22f), "HH.UI.Overview.Today".Translate(), Color.white, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 22f), UiStyle.Silver(TodayIncome()) + " " + "HH.UI.Silver".Translate(), Color.white, TextAnchor.MiddleLeft); y += 22f;
            UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 22f), "HH.UI.Overview.Yesterday".Translate(), UiStyle.Grey, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 22f), UiStyle.Silver(YesterdayIncome()) + " " + "HH.UI.Silver".Translate(), UiStyle.Grey, TextAnchor.MiddleLeft); y += 26f;
            // B2/C9: income by category (today and the last 7 days, from the ledger's daily category totals)
            var cats = IncomeByCategory(7);
            if (cats.Count > 0)
            {
                UiStyle.LabelColored(new Rect(4f, y, w, 20f), "HH.UI.Overview.Sources".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
                double max = Math.Max(0.01, cats.Max(c => c.week.Silver));
                float barW = Mathf.Min(300f, w - 420f);
                foreach (var c in cats)
                {
                    UiStyle.LabelColored(new Rect(4f, y, 160f, 22f), CategoryLabel(c.cat), Color.white, TextAnchor.MiddleLeft);
                    UiStyle.Bar(new Rect(170f, y + 4f, barW, 14f), (float)(c.week.Silver / max), UiStyle.Amber);
                    UiStyle.LabelColored(new Rect(180f + barW, y, 120f, 22f), "HH.UI.Overview.CatToday".Translate(UiStyle.SilverWhole(c.today)), Color.white, TextAnchor.MiddleLeft, GameFont.Tiny);
                    UiStyle.LabelColored(new Rect(300f + barW, y, 140f, 22f), "HH.UI.Overview.CatWeek".Translate(UiStyle.SilverWhole(c.week)), UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    y += 22f;
                }
                y += 6f;
                // last 7 days
                UiStyle.LabelColored(new Rect(4f, y, w, 20f), "HH.UI.Overview.Week".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
                var days = DailyIncome(7);
                double dmax = Math.Max(0.01, days.Max(d => d.Silver));
                float colW = Mathf.Min(70f, (w - 8f) / 7f), chartH = 60f;
                int today = Find.TickManager.TicksGame / 60000;
                for (int i = 0; i < days.Count; i++)
                {
                    float h = (float)(days[i].Silver / dmax) * chartH;
                    var bar = new Rect(4f + i * colW + 6f, y + chartH - h, colW - 12f, h);
                    Widgets.DrawBoxSolid(bar, i == days.Count - 1 ? UiStyle.Amber : new Color(UiStyle.Amber.r, UiStyle.Amber.g, UiStyle.Amber.b, 0.55f));
                    TooltipHandler.TipRegion(new Rect(4f + i * colW, y, colW, chartH + 18f), "HH.UI.Overview.DayTip".Translate(today - (days.Count - 1 - i), UiStyle.SilverWhole(days[i])));
                    UiStyle.LabelColored(new Rect(4f + i * colW, y + chartH, colW, 18f), UiStyle.SilverWhole(days[i]), UiStyle.Grey, TextAnchor.UpperCenter, GameFont.Tiny);
                }
                y += chartH + 22f;
            }
            else { UiStyle.LabelColored(new Rect(4f, y, w, 22f), "HH.UI.Overview.NoIncome".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 24f; }

            // C9: every venue with its readiness
            if (map != null && Venues.All(map).Count > 0)
            {
                y += 4f;
                y += UiStyle.Section(0, y, w, "HH.UI.Overview.VenuesHead".Translate());
                foreach (var v in Venues.All(map))
                {
                    var (chip, good, tip) = VenueChip(v);
                    UiStyle.LabelColored(new Rect(4f, y, 220f, 24f), v.Comp.DisplayName.Truncate(216f), Color.white, TextAnchor.MiddleLeft);
                    UiStyle.LabelColored(new Rect(228f, y, 140f, 24f), v.Type?.LabelCap.ToString() ?? "", UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    string hours = v.Comp.alwaysOpen || v.Comp.openHour == v.Comp.closeHour ? "HH.UI.Overview.AllDay".Translate().ToString() : $"{v.Comp.openHour:00}–{v.Comp.closeHour:00}";
                    UiStyle.LabelColored(new Rect(372f, y, 80f, 24f), hours, UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    var cr = new Rect(456f, y + 2f, 150f, 20f);
                    Widgets.DrawBoxSolid(cr, good ? new Color(UiStyle.Good.r, UiStyle.Good.g, UiStyle.Good.b, 0.25f) : new Color(UiStyle.Bad.r, UiStyle.Bad.g, UiStyle.Bad.b, 0.25f));
                    UiStyle.LabelColored(cr, chip, Color.white, TextAnchor.MiddleCenter, GameFont.Tiny);
                    if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(cr, tip);
                    var rowR = new Rect(0, y, w, 24f);
                    if (Mouse.IsOver(rowR)) Widgets.DrawHighlight(rowR);
                    if (Widgets.ButtonInvisible(rowR)) CameraJumper.TryJumpAndSelect(v.Sign);
                    y += 24f;
                }
            }

            // extra sections from other modules (concierge, renown graph ...)
            foreach (var s in HearthUI.OverviewSections.ToList())
            {
                y += 8f;
                var sect = s;
                var l = new Listing_Standard();
                var lr = new Rect(0, y, w, 2000f);
                l.Begin(lr);
                Guard.Run(ModuleId.Core, "UI.OverviewSection." + sect.Id, () => sect.Draw(l));
                y += l.CurHeight;
                l.End();
            }
            viewH = y + 20f;
            Widgets.EndScrollView();
        }
    }
}
