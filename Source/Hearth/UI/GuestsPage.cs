// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>
    /// Guest overview (10.4): one row per guest on the current map. Minimal and quiet, sortable, click to jump to the guest.
    /// Rows are cached for half a second; only the visible ones are drawn.
    /// </summary>
    public static class GuestsPage
    {
        public enum Col { Name, Faction, Stay, Mood, Satisfaction, Budget, Spent, Wing, Leaves }
        public sealed class Row
        {
            public Pawn Pawn; public GuestInfo Info; public string Name, Faction, Stay, Wing, Leaves;
            public float Mood = -1f, Sat; public double Budget, Spent; public int LeaveTick;
        }

        static readonly Col[] cols = { Col.Name, Col.Faction, Col.Stay, Col.Mood, Col.Satisfaction, Col.Budget, Col.Spent, Col.Wing, Col.Leaves };
        static readonly float[] weights = { 2.4f, 2.0f, 1.5f, 1.3f, 1.5f, 1.1f, 1.0f, 1.0f, 1.3f };
        public static Col SortCol = Col.Name; public static bool SortDesc;
        static Vector2 scroll;
        static List<Row> rows = new List<Row>(); static float nextRefresh; static int rowsMapId = -1;
        public static IReadOnlyList<Row> Rows => rows;

        public static string KindLabel(StayKind k) => ("HH.UI.Kind." + k).Translate();

        public static Row MakeRow(Pawn p, GuestInfo gi)
        {
            int now = Find.TickManager.TicksGame;
            var row = new Row
            {
                Pawn = p, Info = gi, Name = p.LabelShortCap, Faction = p.Faction?.Name ?? "—", Stay = KindLabel(gi.Kind), Wing = gi.WingId.NullOrEmpty() ? "—" : gi.WingId,
                Mood = p.needs?.mood != null ? p.needs.mood.CurLevelPercentage : -1f, Sat = gi.Satisfaction, Budget = gi.Budget.Silver, Spent = gi.Spent.Silver, LeaveTick = gi.PlannedLeaveTick,
            };
            row.Leaves = gi.PlannedLeaveTick > now ? (gi.PlannedLeaveTick - now).ToStringTicksToPeriod() : "—";
            return row;
        }

        public static void Refresh(Map map, bool force = false)
        {
            if (map == null) { rows.Clear(); return; }
            if (!force && map.uniqueID == rowsMapId && Time.realtimeSinceStartup < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartup + 0.5f; rowsMapId = map.uniqueID;
            var list = new List<Row>();
            foreach (var p in GuestApi.GuestsOn(map))
            {
                if (p == null || p.Dead || !p.Spawned) continue;
                var gi = GuestApi.Info(p); if (gi == null) continue;
                list.Add(MakeRow(p, gi));
            }
            rows = Sort(list, SortCol, SortDesc);
        }

        public static List<Row> Sort(List<Row> list, Col col, bool desc)
        {
            Func<Row, IComparable> key;
            switch (col)
            {
                case Col.Faction: key = r => r.Faction; break;
                case Col.Stay: key = r => r.Stay; break;
                case Col.Mood: key = r => r.Mood; break;
                case Col.Satisfaction: key = r => r.Sat; break;
                case Col.Budget: key = r => r.Budget; break;
                case Col.Spent: key = r => r.Spent; break;
                case Col.Wing: key = r => r.Wing; break;
                case Col.Leaves: key = r => r.LeaveTick <= 0 ? int.MaxValue : r.LeaveTick; break;
                default: key = r => r.Name; break;
            }
            var ordered = desc ? list.OrderByDescending(key).ThenBy(r => r.Name) : list.OrderBy(key).ThenBy(r => r.Name);
            return ordered.ToList();
        }

        public static string ColLabel(Col c) => ("HH.UI.Guests.Col." + c).Translate();

        public static void Draw(Rect r)
        {
            UiProbe.Hit("guestspage");
            var map = Find.CurrentMap;
            Refresh(map);
            int day = rows.Count(x => x.Info.Kind == StayKind.DayVisitor), stay = rows.Count - day;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "HH.UI.Guests.Head".Translate(rows.Count, day));
            Text.Font = GameFont.Small;
            float y = r.y + 40f;
            if (!GuestsHearthTracked())
                { UiStyle.LabelColored(new Rect(r.x, y, r.width, 22f), "HH.UI.Guests.VanillaNote".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 22f; }
            if (rows.Count == 0)
            {
                UiStyle.LabelColored(new Rect(r.x, y + 10f, r.width, 60f), "HH.UI.Guests.None".Translate(), UiStyle.Grey);
                return;
            }
            float totalW = r.width - 18f; float sum = weights.Sum();
            var colX = new float[cols.Length + 1]; float acc = 0;
            for (int i = 0; i < cols.Length; i++) { colX[i] = acc; acc += totalW * weights[i] / sum; }
            colX[cols.Length] = totalW;
            // header
            var head = new Rect(r.x, y, r.width, 26f);
            Widgets.DrawBoxSolid(head, new Color(1f, 1f, 1f, 0.06f));
            for (int i = 0; i < cols.Length; i++)
            {
                var cr = new Rect(r.x + colX[i] + 4f, y, colX[i + 1] - colX[i] - 4f, 26f);
                bool sorted = SortCol == cols[i];
                UiStyle.LabelColored(cr, ColLabel(cols[i]), sorted ? UiStyle.Amber : Color.white, TextAnchor.MiddleLeft, GameFont.Tiny);
                if (sorted)
                {
                    var icon = SortIcon(SortDesc);
                    if (icon != null) GUI.DrawTexture(new Rect(cr.xMax - 16f, y + 5f, 14f, 14f), icon);
                }
                if (Widgets.ButtonInvisible(new Rect(r.x + colX[i], y, colX[i + 1] - colX[i], 26f)))
                {
                    if (SortCol == cols[i]) SortDesc = !SortDesc; else { SortCol = cols[i]; SortDesc = false; }
                    rows = Sort(rows, SortCol, SortDesc);
                }
            }
            y += 28f;
            var outR = new Rect(r.x, y, r.width, r.yMax - y);
            const float rowH = 34f;
            var view = new Rect(0, 0, outR.width - 18f, rows.Count * rowH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            float vy0 = scroll.y, vy1 = scroll.y + outR.height;
            for (int i = 0; i < rows.Count; i++)
            {
                float ry = i * rowH;
                if (!SettingRowUI.ForceDrawAll && (ry + rowH < vy0 || ry > vy1)) continue;
                DrawRow(new Rect(0, ry, view.width, rowH), rows[i], colX, i);
            }
            Widgets.EndScrollView();
        }

        // vanilla's own sorting icons (same paths as PawnColumnWorker); kept out of literal form so the placeholder tool never shadows them
        const string SortPath = "UI/Icons/Sorting", SortDescPath = "UI/Icons/SortingDescending";
        static Texture2D sortUp, sortDown;
        static Texture2D SortIcon(bool desc)
        {
            if (sortUp == null) { sortUp = ContentFinder<Texture2D>.Get(SortPath, false); sortDown = ContentFinder<Texture2D>.Get(SortDescPath, false); }
            return desc ? sortDown : sortUp;
        }

        static bool GuestsHearthTracked() => HearthUI.GuestsViaInn;

        static void DrawRow(Rect row, Row g, float[] colX, int index)
        {
            if (index % 2 == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.025f));
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            if (Widgets.ButtonInvisible(row)) { if (g.Pawn != null && g.Pawn.Spawned) CameraJumper.TryJumpAndSelect(g.Pawn); }
            float cy = row.y + row.height / 2f;
            for (int i = 0; i < cols.Length; i++)
            {
                var cr = new Rect(row.x + colX[i] + 4f, row.y, colX[i + 1] - colX[i] - 6f, row.height);
                switch (cols[i])
                {
                    case Col.Name:
                        if (g.Pawn != null) { try { Widgets.ThingIcon(new Rect(cr.x, cy - 14f, 28f, 28f), g.Pawn); } catch { } }
                        UiStyle.LabelColored(new Rect(cr.x + 32f, cr.y, cr.width - 32f, cr.height), g.Name, Color.white, TextAnchor.MiddleLeft);
                        break;
                    case Col.Faction:
                        UiStyle.LabelColored(cr, g.Faction, g.Pawn?.Faction?.Color ?? Color.white, TextAnchor.MiddleLeft, GameFont.Tiny); break;
                    case Col.Mood:
                        if (g.Mood >= 0f)
                        {
                            UiStyle.Bar(new Rect(cr.x, cy - 5f, cr.width - 44f, 10f), g.Mood, UiStyle.Heat(g.Mood));
                            UiStyle.LabelColored(new Rect(cr.xMax - 42f, cr.y, 42f, cr.height), (g.Mood * 100f).ToString("0") + "%", Color.white, TextAnchor.MiddleRight, GameFont.Tiny);
                            TooltipHandler.TipRegion(cr, () => g.Pawn?.needs?.mood?.GetTipString() ?? "", 9000 + g.Pawn.thingIDNumber);
                        }
                        else UiStyle.LabelColored(cr, "—", UiStyle.DimGrey, TextAnchor.MiddleLeft);
                        break;
                    case Col.Satisfaction:
                    {
                        float t = (g.Sat + 1f) / 2f;
                        UiStyle.Bar(new Rect(cr.x, cy - 5f, cr.width - 40f, 10f), t, UiStyle.Heat(t));
                        UiStyle.LabelColored(new Rect(cr.xMax - 38f, cr.y, 38f, cr.height), (g.Sat >= 0 ? "+" : "") + g.Sat.ToString("0.00"), Color.white, TextAnchor.MiddleRight, GameFont.Tiny);
                        TooltipHandler.TipRegion(cr, () => SatisfactionTip(g.Info), 9500 + g.Pawn.thingIDNumber);
                        break;
                    }
                    case Col.Budget: UiStyle.LabelColored(cr, g.Budget.ToString("#,##0"), Color.white, TextAnchor.MiddleLeft); break;
                    case Col.Spent: UiStyle.LabelColored(cr, g.Spent.ToString("#,##0"), UiStyle.Grey, TextAnchor.MiddleLeft); break;
                    case Col.Stay: UiStyle.LabelColored(cr, g.Stay, Color.white, TextAnchor.MiddleLeft, GameFont.Tiny); break;
                    case Col.Wing: UiStyle.LabelColored(cr, g.Wing, UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny); break;
                    default: UiStyle.LabelColored(cr, g.Leaves, UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny); break;
                }
            }
        }

        public static string SatisfactionTip(GuestInfo gi)
        {
            if (gi == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("HH.UI.Guests.SatTip".Translate());
            foreach (var e in gi.Events.Skip(Math.Max(0, gi.Events.Count - 6)).Reverse()) sb.AppendLine("• " + e);
            return sb.ToString().TrimEnd();
        }
    }
}
