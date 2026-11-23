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
        public override Vector2 RequestedTabSize => HUI.Size(1180f, 720f, 1000f, 600f, 0.92f, 0.88f);
        static Vector2 listScroll;

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

        /// <summary>Layout of the tab for a given inner rect: header, page list, page area (tests check that all of it stays inside).</summary>
        public static void Layout(Rect inRect, out Rect header, out Rect list, out Rect content)
        {
            float headerH = 44f;
            header = new Rect(inRect.x, inRect.y, inRect.width, headerH);
            var body = new Rect(inRect.x, inRect.y + headerH + 4f, inRect.width, inRect.height - headerH - 4f);
            float listW = Mathf.Clamp(inRect.width * 0.17f, 170f, 230f);
            list = new Rect(body.x, body.y, listW, body.height);
            content = new Rect(list.xMax + 16f, body.y, body.width - listW - 16f, body.height);
        }

        void Draw(Rect inRect)
        {
            UiProbe.Hit("maintab");
            var pages = Pages();
            if (pages.Count == 0) { UiStyle.LabelColored(inRect, "HH.UI.Tab.NoPages".Translate(), UiStyle.Grey); return; }
            if (pages.All(p => p.Id != SelectedPage)) SelectedPage = pages[0].Id;
            Layout(inRect, out var header, out var list, out var content);

            // header: title left, renown line in the space between the title and the buttons, buttons right
            Text.Font = GameFont.Medium;
            float titleW = Mathf.Ceil(Text.CalcSize("Hearth & Harbor").x) + 4f;
            GUI.color = UiStyle.Amber; Widgets.Label(new Rect(header.x, header.y + 2f, titleW, 36f), "Hearth & Harbor"); GUI.color = Color.white;
            Text.Font = GameFont.Small;
            string settingsLabel = "HH.UI.Tab.Settings".Translate(), guideLabel = "HH.UI.Tab.Guide".Translate();
            float bw = Mathf.Clamp(Mathf.Max(HUI.TextW(settingsLabel), HUI.TextW(guideLabel)) + 28f, 130f, 190f);
            var settingsR = new Rect(header.xMax - bw, header.y + 4f, bw, 32f);
            var guideR = new Rect(settingsR.x - 8f - bw, header.y + 4f, bw, 32f);
            if (Widgets.ButtonText(settingsR, settingsLabel)) OpenSettings();
            if (Widgets.ButtonText(guideR, guideLabel)) HSettings.Set("core.guide", HSettings.GetBool("core.guide") ? 0f : 1f);
            TooltipHandler.TipRegion(guideR, "HH.UI.Tab.GuideTip".Translate());
            string renown = null;
            if (HearthUI.RenownLine != null) renown = Guard.Run(ModuleId.Core, "UI.RenownLine", HearthUI.RenownLine, null);
            if (!renown.NullOrEmpty())
            {
                var rr = new Rect(header.x + titleW + 20f, header.y + 6f, Mathf.Max(0f, guideR.x - 12f - (header.x + titleW + 20f)), 28f);
                if (rr.width > 40f)
                {
                    bool wrap = Text.WordWrap; Text.WordWrap = false;
                    UiStyle.LabelColored(rr, renown.Truncate(rr.width), UiStyle.Grey, TextAnchor.MiddleLeft);
                    Text.WordWrap = wrap;
                    TooltipHandler.TipRegion(rr, renown);
                }
            }
            Widgets.DrawBoxSolid(new Rect(header.x, header.yMax - 1f, header.width, 1f), UiStyle.Line);

            // page list (scrolls when many modules registered a page)
            const float rowH = 36f;
            float needH = pages.Count * (rowH + 2f);
            float vw = list.width - (needH > list.height ? PageFrame.ScrollBar : 0f);
            Widgets.BeginScrollView(list, ref listScroll, new Rect(0f, 0f, vw, needH));
            float y = 0f;
            foreach (var p in pages)
            {
                var row = new Rect(0f, y, vw, rowH);
                bool sel = p.Id == SelectedPage;
                if (sel) { Widgets.DrawBoxSolid(row, UiStyle.AmberSoft); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), UiStyle.Amber); }
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                string lab = p.Label;
                var lr = new Rect(row.x + 14f, row.y, row.width - 18f, row.height);
                UiStyle.LabelColored(lr, lab, sel ? Color.white : UiStyle.Grey, TextAnchor.MiddleLeft);
                if (HUI.TextW(lab) > lr.width) TooltipHandler.TipRegion(row, lab);
                if (Widgets.ButtonInvisible(row)) SelectedPage = p.Id;
                y += rowH + 2f;
            }
            Widgets.EndScrollView();
            Widgets.DrawBoxSolid(new Rect(list.xMax + 7f, list.y, 1f, list.height), UiStyle.Line);
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
            // 1.1.0 (F12): required furniture missing → the venue does not open at all
            if (!Guard.Run(ModuleId.Core, "UI.VenueReady", () => Venues.IsReady(v), true))
                return ("HH.Setup.NotReadyState".TranslateOr("not ready").CapitalizeFirst(), false, Venues.ReadySummary(v));
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
            var body = PageFrame.Header(r, "HH.UI.Page.Overview".Translate(), "HH.UI.Page.Sub.overview".TranslateOr("Today at a glance: guests, takings, venues."));
            float w = PageFrame.BeginScroll(body, ref scroll, viewH), y = 0;
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
            float tw = Mathf.Min(280f, (w - 14f * (tiles.Count - 1)) / tiles.Count);
            float tileH = 0f;
            foreach (var t in tiles) tileH = Mathf.Max(tileH, 14f + HUI.TextH(t.label, tw - 24f, GameFont.Tiny) + 46f);
            tileH = Mathf.Max(tileH, 92f);
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = new Rect(i * (tw + 14f), y, tw, tileH);
                Widgets.DrawBoxSolid(t, new Color(1f, 1f, 1f, 0.05f)); GUI.color = UiStyle.Line; Widgets.DrawBox(t); GUI.color = Color.white;
                float lh = HUI.TextH(tiles[i].label, t.width - 24f, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(t.x + 12f, t.y + 8f, t.width - 24f, lh), tiles[i].label, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(t.x + 12f, t.y + 8f + lh, t.width - 24f, t.height - lh - 12f), tiles[i].value, i == 1 ? UiStyle.Amber : Color.white, TextAnchor.MiddleLeft, GameFont.Medium);
                TooltipHandler.TipRegion(t, tiles[i].tip);
            }
            y += tileH + 14f;

            // arrivals expected in the next 24 hours (Inn module): one compact line under the tiles
            string arrivals = map == null ? null : Guard.Run(ModuleId.Core, "UI.OverviewArrivals", () => ArrivalsLine(map), null);
            if (arrivals != null) { float ah = HUI.TextH(arrivals, w - 8f, GameFont.Tiny); UiStyle.LabelColored(new Rect(4f, y - 6f, w - 8f, ah), arrivals, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny); y += ah + 6f; }

            // income detail
            y += UiStyle.Section(0, y, w, "HH.UI.Overview.IncomeHead".Translate());
            string silverWord = "HH.UI.Silver".Translate();
            UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 26f), "HH.UI.Overview.Today".Translate(), Color.white, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 26f), UiStyle.Silver(TodayIncome()) + " " + silverWord, Color.white, TextAnchor.MiddleLeft); y += 26f;
            UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 26f), "HH.UI.Overview.Yesterday".Translate(), UiStyle.Grey, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 26f), UiStyle.Silver(YesterdayIncome()) + " " + silverWord, UiStyle.Grey, TextAnchor.MiddleLeft); y += 32f;
            // B2/C9: income by category (today and the last 7 days, from the ledger's daily category totals)
            var cats = IncomeByCategory(7);
            if (cats.Count > 0)
            {
                UiStyle.LabelColored(new Rect(4f, y, w, 20f), "HH.UI.Overview.Sources".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 22f;
                double max = Math.Max(0.01, cats.Max(c => c.week.Silver));
                float nameW = Mathf.Clamp(w * 0.22f, 150f, 240f);
                float restW = w - nameW - 12f;
                float barW = Mathf.Max(80f, restW * 0.42f), todayW = restW * 0.26f, weekW = restW * 0.32f;
                foreach (var c in cats)
                {
                    UiStyle.LabelColored(new Rect(4f, y, nameW, 26f), CategoryLabel(c.cat), Color.white, TextAnchor.MiddleLeft);
                    UiStyle.Bar(new Rect(8f + nameW, y + 6f, barW, 14f), (float)(c.week.Silver / max), UiStyle.Amber);
                    UiStyle.LabelColored(new Rect(8f + nameW + barW + 10f, y, todayW, 26f), "HH.UI.Overview.CatToday".Translate(UiStyle.SilverWhole(c.today)), Color.white, TextAnchor.MiddleLeft, GameFont.Tiny);
                    UiStyle.LabelColored(new Rect(8f + nameW + barW + 10f + todayW, y, weekW, 26f), "HH.UI.Overview.CatWeek".Translate(UiStyle.SilverWhole(c.week)), UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    y += 26f;
                }
                y += 8f;
                // last 7 days
                UiStyle.LabelColored(new Rect(4f, y, w, 20f), "HH.UI.Overview.Week".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 22f;
                var days = DailyIncome(7);
                double dmax = Math.Max(0.01, days.Max(d => d.Silver));
                float colW = Mathf.Min(110f, (w - 8f) / 7f), chartH = 90f;
                int today = Find.TickManager.TicksGame / 60000;
                for (int i = 0; i < days.Count; i++)
                {
                    float h = (float)(days[i].Silver / dmax) * chartH;
                    var bar = new Rect(4f + i * colW + 8f, y + chartH - h, colW - 16f, h);
                    Widgets.DrawBoxSolid(bar, i == days.Count - 1 ? UiStyle.Amber : new Color(UiStyle.Amber.r, UiStyle.Amber.g, UiStyle.Amber.b, 0.55f));
                    TooltipHandler.TipRegion(new Rect(4f + i * colW, y, colW, chartH + 20f), "HH.UI.Overview.DayTip".Translate(today - (days.Count - 1 - i), UiStyle.SilverWhole(days[i])));
                    UiStyle.LabelColored(new Rect(4f + i * colW, y + chartH + 2f, colW, 18f), UiStyle.SilverWhole(days[i]), UiStyle.Grey, TextAnchor.UpperCenter, GameFont.Tiny);
                }
                y += chartH + 28f;
            }
            else { UiStyle.LabelColored(new Rect(4f, y, w, 22f), "HH.UI.Overview.NoIncome".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 28f; }

            // C9: every venue with its readiness
            if (map != null && Venues.All(map).Count > 0)
            {
                y += 4f;
                y += UiStyle.Section(0, y, w, "HH.UI.Overview.VenuesHead".Translate());
                float nameW = w * 0.30f, typeW = w * 0.22f, hoursW = w * 0.12f, chipW = Mathf.Clamp(w * 0.24f, 150f, 260f);
                foreach (var v in Venues.All(map))
                {
                    var (chip, good, tip) = VenueChip(v);
                    const float rh = 30f;
                    var rowR = new Rect(0, y, w, rh);
                    if (Mouse.IsOver(rowR)) Widgets.DrawHighlight(rowR);
                    string vname = v.Comp.DisplayName;
                    UiStyle.LabelColored(new Rect(4f, y, nameW - 8f, rh), vname.Truncate(nameW - 8f), Color.white, TextAnchor.MiddleLeft);
                    string vtype = v.Type?.LabelCap.ToString() ?? "";
                    UiStyle.LabelColored(new Rect(nameW, y, typeW - 8f, rh), vtype.Truncate(typeW - 8f), UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    string hours = v.Comp.alwaysOpen || v.Comp.openHour == v.Comp.closeHour ? "HH.UI.Overview.AllDay".Translate().ToString() : $"{v.Comp.openHour:00}–{v.Comp.closeHour:00}";
                    UiStyle.LabelColored(new Rect(nameW + typeW, y, hoursW - 4f, rh), hours, UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
                    var cr = new Rect(w - chipW - 4f, y + 3f, chipW, rh - 6f);
                    Widgets.DrawBoxSolid(cr, good ? new Color(UiStyle.Good.r, UiStyle.Good.g, UiStyle.Good.b, 0.25f) : new Color(UiStyle.Bad.r, UiStyle.Bad.g, UiStyle.Bad.b, 0.25f));
                    UiStyle.LabelColored(cr, chip.Truncate(cr.width - 6f), Color.white, TextAnchor.MiddleCenter, GameFont.Tiny);
                    if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(cr, tip);
                    if (vname != null && HUI.TextW(vname) > nameW - 8f) TooltipHandler.TipRegion(new Rect(4f, y, nameW - 8f, rh), vname);
                    if (Widgets.ButtonInvisible(rowR)) CameraJumper.TryJumpAndSelect(v.Sign);
                    y += rh;
                }
            }

            // extra sections from other modules (concierge, renown graph ...)
            foreach (var s in HearthUI.OverviewSections.ToList())
            {
                y += 10f;
                var sect = s;
                var l = new Listing_Standard();
                var lr = new Rect(0, y, w, 2000f);
                l.Begin(lr);
                Guard.Run(ModuleId.Core, "UI.OverviewSection." + sect.Id, () => sect.Draw(l));
                y += l.CurHeight;
                l.End();
            }
            viewH = PageFrame.EndScroll(y);
        }
    }
}
