// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>One line of the colony cookbook.</summary>
    public sealed class CookbookRow
    {
        public DishDef Dish;
        public bool Known;
        public string Name => Known ? Dish.label : "HH.Kitchen.Cookbook.Unknown".TranslateOr("???");
        public string Hint;
    }

    /// <summary>The "Colony cookbook" page of the main tab (KONZEPT 6.4): known dishes in full, unknown ones as ??? with a hint where to learn them.</summary>
    public static class CookbookPage
    {
        static Vector2 scroll;
        static string search = "";
        static bool onlyKnown;

        public static void Register()
        {
            HearthTabs.Register(new HearthTabPage { Id = "kitchen", LabelKey = "HH.Tab.Kitchen", LabelEn = "Cookbook", Module = ModuleId.ShopsFood, Order = 60, Draw = r => Guard.Run(ModuleId.ShopsFood, "Cookbook.Draw", () => Draw(r)) });
        }

        public static List<CookbookRow> Rows(string filter = null, bool knownOnly = false)
        {
            var rows = new List<CookbookRow>();
            bool dark = DishKnowledge.DarkAllowed();
            foreach (var d in DefDatabase<DishDef>.AllDefs.OrderBy(x => x.level).ThenBy(x => x.label))
            {
                if (d.dark && !dark) continue;
                var row = new CookbookRow { Dish = d, Known = DishKnowledge.IsKnown(d) };
                if (knownOnly && !row.Known) continue;
                if (!filter.NullOrEmpty() && !(row.Known && (d.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || d.inspiration.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || d.CuisineLabel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))) continue;
                row.Hint = HintFor(d);
                rows.Add(row);
            }
            return rows;
        }

        static string HintFor(DishDef d)
        {
            if (!d.IsAvailable) return ("HH.Kitchen.Cookbook.NoRecipe".TranslateOr("No recipe: {0}")).Replace("{0}", d.unavailableReason ?? "?");
            if (DishKnowledge.IsKnown(d)) return null;
            var r = DishKnowledge.LevelResearch(d.level);
            string how = r != null ? r.LabelCap.ToString() : "?";
            return ("HH.Kitchen.Cookbook.UnknownHint".TranslateOr("Not learned yet. Research {0}, or find a recipe book.")).Replace("{0}", how);
        }

        public static void Draw(Rect rect)
        {
            var all = Rows();
            int known = all.Count(r => r.Known);
            string sub = ("HH.Kitchen.Cookbook.Known".TranslateOr("{0} of {1} dishes known")).Replace("{0}", known.ToString()).Replace("{1}", all.Count.ToString());
            var body = Hearth.Shell.PageFrame.Header(rect, "HH.Kitchen.Cookbook.Title".TranslateOr("Colony cookbook"), sub);
            string searchLabel = "HH.Kitchen.Cookbook.Search".TranslateOr("Search");
            string filterLabel = "HH.Kitchen.Cookbook.Filter".TranslateOr("Show only known");
            var bar = new Rect(body.x, body.y, body.width, 30f);
            float sw = Mathf.Ceil(HUI.TextW(searchLabel)) + 8f;
            Widgets.Label(new Rect(bar.x, bar.y + 4f, sw, 26f), searchLabel);
            search = Widgets.TextField(new Rect(bar.x + sw + 4f, bar.y, Mathf.Clamp(bar.width * 0.3f, 200f, 320f), 28f), search);
            float fx = bar.x + sw + 4f + Mathf.Clamp(bar.width * 0.3f, 200f, 320f) + 24f;
            Widgets.CheckboxLabeled(new Rect(fx, bar.y, Mathf.Min(bar.xMax - fx, HUI.TextW(filterLabel) + 50f), 28f), filterLabel, ref onlyKnown);
            var rows = Rows(search, onlyKnown);

            float headH = 24f;
            var outer = new Rect(body.x, body.y + 38f + headH, body.width, Mathf.Max(0f, body.height - 38f - headH));
            // column header (fractions shared with the rows)
            var hx = ColX(body.width - 18f);
            string[] heads = { "HH.Kitchen.Cookbook.ColLevel", "HH.Kitchen.Cookbook.ColDish", "HH.Kitchen.Cookbook.ColCuisine", "HH.Kitchen.Cookbook.ColStations", "HH.Kitchen.Cookbook.ColSkill", "HH.Kitchen.Cookbook.ColPrice" };
            string[] headEn = { "Level", "Dish", "Cuisine", "Cooked at", "Skill", "Price" };
            Widgets.DrawBoxSolid(new Rect(body.x, body.y + 38f, body.width, headH), new Color(1f, 1f, 1f, 0.06f));
            for (int i = 0; i < heads.Length; i++)
                Hearth.Shell.UiStyle.LabelColored(new Rect(body.x + hx[i] + 6f, body.y + 38f, hx[i + 1] - hx[i] - 6f, headH), heads[i].TranslateOr(headEn[i]), Color.white, TextAnchor.MiddleLeft, GameFont.Tiny);
            const float rowH = 36f;
            var view = new Rect(0f, 0f, outer.width - 18f, rows.Count * rowH);
            Widgets.BeginScrollView(outer, ref scroll, view);
            int first = Mathf.Max(0, (int)(scroll.y / rowH) - 1), last = Mathf.Min(rows.Count, first + (int)(outer.height / rowH) + 3);
            for (int i = first; i < last; i++) DrawRow(new Rect(0f, i * rowH, view.width, rowH), rows[i], i);
            Widgets.EndScrollView();
        }

        // column fractions: level, dish, cuisine, stations, skill, price
        static readonly float[] colFrac = { 0.08f, 0.26f, 0.15f, 0.31f, 0.08f, 0.12f };
        static float[] ColX(float w) { var x = new float[colFrac.Length + 1]; float a = 0; for (int i = 0; i < colFrac.Length; i++) { x[i] = a; a += w * colFrac[i]; } x[colFrac.Length] = w; return x; }

        static void DrawRow(Rect r, CookbookRow row, int i)
        {
            if (i % 2 == 0) Widgets.DrawLightHighlight(r);
            Widgets.DrawHighlightIfMouseover(r);
            var d = row.Dish;
            var gray = row.Known ? Color.white : new Color(0.65f, 0.65f, 0.65f);
            GUI.color = gray;
            var cx = ColX(r.width); Text.Anchor = TextAnchor.MiddleLeft;
            Rect Cell(int c) => new Rect(r.x + cx[c] + 6f, r.y, cx[c + 1] - cx[c] - 8f, r.height);
            Widgets.Label(Cell(0), ("HH.Kitchen.Cookbook.Level".TranslateOr("Level {0}")).Replace("{0}", d.level.ToString()));
            Widgets.Label(Cell(1), row.Name.Truncate(Cell(1).width));
            if (row.Known)
            {
                Widgets.Label(Cell(2), d.CuisineLabel.Truncate(Cell(2).width));
                string st = d.StationSummary();
                Widgets.Label(Cell(3), st.Truncate(Cell(3).width));
                Widgets.Label(Cell(4), "★ " + d.minCooking);
                Widgets.Label(Cell(5), DishEffects.MenuPrice(d, QualityCategory.Normal, 0).ToStringShort());
            }
            else if (row.Hint != null)
            {
                var hr = new Rect(r.x + cx[2] + 6f, r.y, r.width - cx[2] - 10f, r.height);
                Text.Font = GameFont.Tiny; Widgets.Label(hr, row.Hint); Text.Font = GameFont.Small;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            string tip = row.Known
                ? d.label.CapitalizeFirst() + "\n\n" + d.description + "\n\n" + ("HH.Kitchen.Inspiration".TranslateOr("Inspiration: {0}")).Replace("{0}", d.inspiration) + "\n" + ("HH.Kitchen.Cooked".TranslateOr("Cooked at: {0} ({1})")).Replace("{0}", d.StationSummary()).Replace("{1}", d.StepSummary()) + "\n" + ("HH.Kitchen.Made".TranslateOr("Made from: {0}")).Replace("{0}", d.IngredientSummary()) + "\n" + ("HH.Kitchen.FreshHours".TranslateOr("Fresh for {0} h")).Replace("{0}", d.shelfLifeHours.ToString("0.#"))
                : row.Hint;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
        }
    }
}
