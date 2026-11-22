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
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "HH.Kitchen.Cookbook.Title".TranslateOr("Colony cookbook"));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y + 32f, rect.width * 0.5f, 24f), ("HH.Kitchen.Cookbook.Known".TranslateOr("{0} of {1} dishes known")).Replace("{0}", known.ToString()).Replace("{1}", all.Count.ToString()));
            var bar = new Rect(rect.x, rect.y + 60f, rect.width, 28f);
            Widgets.Label(new Rect(bar.x, bar.y + 3f, 60f, 24f), "HH.Kitchen.Cookbook.Search".TranslateOr("Search"));
            search = Widgets.TextField(new Rect(bar.x + 64f, bar.y, 220f, 26f), search);
            Widgets.CheckboxLabeled(new Rect(bar.x + 300f, bar.y, 220f, 26f), "HH.Kitchen.Cookbook.Filter".TranslateOr("Show only known"), ref onlyKnown);
            var rows = Rows(search, onlyKnown);

            var outer = new Rect(rect.x, rect.y + 96f, rect.width, rect.height - 96f);
            const float rowH = 34f;
            var view = new Rect(0f, 0f, outer.width - 18f, rows.Count * rowH);
            Widgets.BeginScrollView(outer, ref scroll, view);
            int first = Mathf.Max(0, (int)(scroll.y / rowH) - 1), last = Mathf.Min(rows.Count, first + (int)(outer.height / rowH) + 3);
            for (int i = first; i < last; i++) DrawRow(new Rect(0f, i * rowH, view.width, rowH), rows[i], i);
            Widgets.EndScrollView();
        }

        static void DrawRow(Rect r, CookbookRow row, int i)
        {
            if (i % 2 == 0) Widgets.DrawLightHighlight(r);
            Widgets.DrawHighlightIfMouseover(r);
            var d = row.Dish;
            var gray = row.Known ? Color.white : new Color(0.65f, 0.65f, 0.65f);
            GUI.color = gray;
            Widgets.Label(new Rect(r.x + 6f, r.y + 6f, 60f, 24f), ("HH.Kitchen.Cookbook.Level".TranslateOr("Level {0}")).Replace("{0}", d.level.ToString()));
            Widgets.Label(new Rect(r.x + 70f, r.y + 6f, 240f, 24f), row.Name);
            if (row.Known)
            {
                Widgets.Label(new Rect(r.x + 320f, r.y + 6f, 120f, 24f), d.CuisineLabel);
                Widgets.Label(new Rect(r.x + 445f, r.y + 6f, 250f, 24f), d.StationSummary());
                Widgets.Label(new Rect(r.x + 700f, r.y + 6f, 80f, 24f), "★ " + d.minCooking);
                Widgets.Label(new Rect(r.x + 780f, r.y + 6f, 80f, 24f), DishEffects.MenuPrice(d, QualityCategory.Normal, 0).ToStringShort());
            }
            else if (row.Hint != null) Widgets.Label(new Rect(r.x + 320f, r.y + 6f, r.width - 330f, 24f), row.Hint);
            GUI.color = Color.white;
            string tip = row.Known
                ? d.label.CapitalizeFirst() + "\n\n" + d.description + "\n\n" + ("HH.Kitchen.Inspiration".TranslateOr("Inspiration: {0}")).Replace("{0}", d.inspiration) + "\n" + ("HH.Kitchen.Cooked".TranslateOr("Cooked at: {0} ({1})")).Replace("{0}", d.StationSummary()).Replace("{1}", d.StepSummary()) + "\n" + ("HH.Kitchen.Made".TranslateOr("Made from: {0}")).Replace("{0}", d.IngredientSummary()) + "\n" + ("HH.Kitchen.FreshHours".TranslateOr("Fresh for {0} h")).Replace("{0}", d.shelfLifeHours.ToString("0.#"))
                : row.Hint;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
        }
    }
}
