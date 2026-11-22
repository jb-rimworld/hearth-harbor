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

        public static Money YesterdayIncome()
        {
            var ledger = Ledger.Current; if (ledger == null) return Money.Zero;
            int day = Find.TickManager.TicksGame / 60000 - 1; if (day < 0) return Money.Zero;
            return Money.Max(Money.Zero, ledger.DayTotalPrefix(day, "colony:till"));
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

            // income detail
            y += UiStyle.Section(0, y, w, "HH.UI.Overview.IncomeHead".Translate());
            UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 22f), "HH.UI.Overview.Today".Translate(), Color.white, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 22f), UiStyle.Silver(TodayIncome()) + " " + "HH.UI.Silver".Translate(), Color.white, TextAnchor.MiddleLeft); y += 22f;
            UiStyle.LabelColored(new Rect(4f, y, w * 0.5f, 22f), "HH.UI.Overview.Yesterday".Translate(), UiStyle.Grey, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.5f, y, w * 0.5f, 22f), UiStyle.Silver(YesterdayIncome()) + " " + "HH.UI.Silver".Translate(), UiStyle.Grey, TextAnchor.MiddleLeft); y += 26f;
            var sources = Ledger.Current == null ? new List<KeyValuePair<string, Money>>() : Ledger.Current.Accounts("colony:income:").Where(kv => kv.Value.IsPositive).OrderByDescending(kv => kv.Value.Micro).Take(6).ToList();
            if (sources.Count > 0)
            {
                UiStyle.LabelColored(new Rect(4f, y, w, 20f), "HH.UI.Overview.Sources".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
                Money max = sources[0].Value;
                foreach (var kv in sources)
                {
                    string name = kv.Key.Substring("colony:income:".Length);
                    UiStyle.LabelColored(new Rect(4f, y, 160f, 22f), name, Color.white, TextAnchor.MiddleLeft);
                    UiStyle.Bar(new Rect(170f, y + 4f, Mathf.Min(300f, w - 330f), 14f), (float)(kv.Value.Micro / (double)max.Micro), UiStyle.Amber);
                    UiStyle.LabelColored(new Rect(180f + Mathf.Min(300f, w - 330f), y, 120f, 22f), UiStyle.SilverWhole(kv.Value), UiStyle.Grey, TextAnchor.MiddleLeft);
                    y += 22f;
                }
            }
            else { UiStyle.LabelColored(new Rect(4f, y, w, 22f), "HH.UI.Overview.NoIncome".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 24f; }

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
