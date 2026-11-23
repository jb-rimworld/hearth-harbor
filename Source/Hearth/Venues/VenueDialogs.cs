// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    /// <summary>Shared look of the venue sign windows (F35, 1.1.0): a header with the type icon, a title and the business name,
    /// an optional status chip on the right, and tiny grey hints that are measured instead of clipped.</summary>
    public static class VenueDialogUI
    {
        public static readonly Color Grey = new Color(0.65f, 0.65f, 0.65f);
        public static readonly Color Green = new Color(0.45f, 0.78f, 0.45f);
        public static readonly Color Red = new Color(0.9f, 0.45f, 0.4f);
        public static readonly Color Amber = new Color(0.95f, 0.74f, 0.35f);

        /// <summary>Draws the header and returns the y where the content starts.</summary>
        public static float Header(Rect inRect, string title, CompVenueSignHH comp, string chip = null, Color? chipCol = null)
        {
            var type = comp?.type;
            var icon = new Rect(inRect.x, inRect.y, 48f, 48f);
            if (type != null)
            {
                Widgets.DrawBoxSolid(icon, new Color(type.color.r * 0.35f, type.color.g * 0.35f, type.color.b * 0.35f));
                if (type.icon != null) GUI.DrawTexture(icon.ContractedBy(5f), type.icon);
            }
            float x = icon.xMax + 10f;
            float chipW = 0f;
            if (!chip.NullOrEmpty())
            {
                Text.Font = GameFont.Small;
                chipW = Text.CalcSize(chip).x + 18f;
                var cr = new Rect(inRect.xMax - chipW - 30f, inRect.y + 12f, chipW, 24f);   // leave room for the close X
                var col = chipCol ?? Grey;
                Widgets.DrawBoxSolid(cr, new Color(col.r, col.g, col.b, 0.22f));
                GUI.color = col; Widgets.DrawBox(cr); GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(cr, chip); Text.Anchor = TextAnchor.UpperLeft;
                chipW += 36f;
            }
            float w = inRect.xMax - x - Mathf.Max(chipW, 30f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, inRect.y, w, 30f), title.Truncate(w));
            Text.Font = GameFont.Small;
            GUI.color = Grey;
            string sub = comp == null ? "" : comp.DisplayName + (type != null && comp.DisplayName != type.LabelCap.ToString() ? " · " + type.LabelCap : "");
            Widgets.Label(new Rect(x, inRect.y + 28f, w, 22f), sub.Truncate(w));
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            Widgets.DrawLineHorizontal(inRect.x, inRect.y + 56f, inRect.width);
            GUI.color = Color.white;
            return inRect.y + 64f;
        }

        /// <summary>A measured paragraph in Tiny grey; returns the y below it.</summary>
        public static float Hint(float x, float y, float width, string text, Color? col = null)
        {
            if (text.NullOrEmpty()) return y;
            Text.Font = GameFont.Tiny;
            float h = Text.CalcHeight(text, width);
            GUI.color = col ?? Grey;
            Widgets.Label(new Rect(x, y, width, h), text);
            GUI.color = Color.white; Text.Font = GameFont.Small;
            return y + h;
        }

        /// <summary>Lays out n buttons in as many rows as needed so that each is at least minW wide.</summary>
        public static List<Rect> Grid(float x, float y, float width, int n, float minW, float h, float gap, out float bottom)
        {
            int perRow = Mathf.Clamp((int)((width + gap) / (minW + gap)), 1, n);
            int rows = (n + perRow - 1) / perRow;
            float bw = (width - (perRow - 1) * gap) / perRow;
            var res = new List<Rect>(n);
            for (int i = 0; i < n; i++) res.Add(new Rect(x + (i % perRow) * (bw + gap), y + (i / perRow) * (h + gap), bw, h));
            bottom = y + rows * (h + gap) - gap;
            return res;
        }

        public static string OpenChip(CompVenueSignHH comp, out Color col)
        {
            bool open = comp.parent.Spawned && comp.IsOpenAt(GenLocalDate.HourOfDay(comp.parent.Map));
            col = open ? Green : Grey;
            return open ? "HH.Venue.OpenNow".TranslateOr("open now") : "HH.Venue.ClosedNow".TranslateOr("closed now");
        }
    }

    /// <summary>Opening hours of a venue sign: presets, a 24-hour strip and two hour pickers (5.13, N25; F35 layout).</summary>
    public class Dialog_VenueHours : Window
    {
        readonly CompVenueSignHH comp;
        public override Vector2 InitialSize => HUI.Size(560f, 470f, 480f, 420f);

        public Dialog_VenueHours(CompVenueSignHH comp)
        {
            this.comp = comp;
            doCloseX = true; closeOnClickedOutside = false; absorbInputAroundWindow = false; forcePause = false; draggable = true;
        }

        void Apply(int open, int close, bool always)
        {
            comp.openHour = open; comp.closeHour = close; comp.alwaysOpen = always;
        }

        bool Is(int open, int close) => !comp.alwaysOpen && comp.openHour == open && comp.closeHour == close;

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent == null || comp.parent.Destroyed) { Close(); return; }
            string chip = VenueDialogUI.OpenChip(comp, out var chipCol);
            float y = VenueDialogUI.Header(inRect, "HH.Venue.HoursTitle".TranslateOr("Opening hours"), comp, chip, chipCol);

            // presets: name on the first line, hours on the second; a grid when the window is narrow
            var presets = new (string name, int open, int close, bool always)[]
            {
                ("HH.Venue.PresetDay".TranslateOr("Day"), VS.I(VS.DayOpen), VS.I(VS.DayClose), false),
                ("HH.Venue.PresetEvening".TranslateOr("Evening"), VS.I(VS.EveningOpen), VS.I(VS.EveningClose), false),
                ("HH.Venue.PresetNight".TranslateOr("Night"), VS.I(VS.NightOpen), VS.I(VS.NightClose), false),
                ("HH.Venue.PresetAlways".TranslateOr("Always"), 0, 0, true),
            };
            var rects = VenueDialogUI.Grid(inRect.x, y, inRect.width, presets.Length, 118f, 44f, 6f, out float bottom);
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                bool cur = p.always ? comp.alwaysOpen : Is(p.open, p.close);
                if (cur) { Widgets.DrawBoxSolid(rects[i], new Color(0.35f, 0.55f, 0.35f, 0.35f)); }
                string label = p.name + "\n" + (p.always ? "0–24" : $"{p.open:00}–{p.close:00}");
                if (Widgets.ButtonText(rects[i], label))
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Apply(p.open, p.close, p.always);
                }
            }
            y = bottom + 14f;

            // 24 hour strip with hour labels below
            DrawStrip(new Rect(inRect.x, y, inRect.width, 34f));
            y += 34f + 18f;

            bool always = comp.alwaysOpen;
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, Mathf.Min(inRect.width, 320f), 26f), "HH.Venue.AlwaysOpen".TranslateOr("open around the clock"), ref always);
            if (always != comp.alwaysOpen) { comp.alwaysOpen = always; if (!always && comp.openHour < 0) Apply(8, 22, false); }
            y += 32f;

            GUI.color = comp.alwaysOpen ? new Color(1, 1, 1, 0.4f) : Color.white;
            HourRow(new Rect(inRect.x, y, inRect.width, 28f), "HH.Venue.Opens".TranslateOr("Opens at"), comp.openHour, v => comp.openHour = v);
            y += 34f;
            HourRow(new Rect(inRect.x, y, inRect.width, 28f), "HH.Venue.Closes".TranslateOr("Closes at"), comp.closeHour, v => comp.closeHour = v);
            GUI.color = Color.white;
            y += 40f;

            VenueDialogUI.Hint(inRect.x, y, inRect.width - 120f, "HH.Venue.HoursHint".TranslateOr("A closing hour earlier than the opening hour means the business stays open past midnight."));
            if (Widgets.ButtonText(new Rect(inRect.xMax - 110f, inRect.yMax - 32f, 110f, 32f), "HH.Venue.Done".TranslateOr("Done"))) Close();
        }

        void HourRow(Rect r, string label, int value, Action<int> set)
        {
            float lw = Mathf.Max(90f, Text.CalcSize(label).x + 8f);
            Widgets.Label(new Rect(r.x, r.y + 3f, lw, r.height), label);
            float x = r.x + lw + 4f;
            if (Widgets.ButtonText(new Rect(x, r.y, 28f, r.height), "−") && !comp.alwaysOpen) set((value + 23) % 24);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(x + 30f, r.y, 60f, r.height), value.ToString("00") + ":00");
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(x + 92f, r.y, 28f, r.height), "+") && !comp.alwaysOpen) set((value + 1) % 24);
            float sx = x + 132f;
            float nv = Widgets.HorizontalSlider(new Rect(sx, r.y + 6f, r.xMax - sx, 20f), value, 0f, 23f, false, null, null, null, 1f);
            int iv = Mathf.RoundToInt(nv);
            if (iv != value && !comp.alwaysOpen) set(iv);
        }

        void DrawStrip(Rect r)
        {
            float cw = r.width / 24f;
            int now = comp.parent.Spawned ? GenLocalDate.HourOfDay(comp.parent.Map) : -1;
            for (int h = 0; h < 24; h++)
            {
                var cell = new Rect(r.x + h * cw, r.y, cw - 1f, r.height);
                bool open = comp.IsOpenAt(h);
                Widgets.DrawBoxSolid(cell, open ? new Color(0.35f, 0.62f, 0.38f) : new Color(0.22f, 0.22f, 0.24f));
                if (h == now) { GUI.color = new Color(1f, 0.85f, 0.4f); Widgets.DrawBox(cell, 2); GUI.color = Color.white; }
                TooltipHandler.TipRegion(cell, $"{h:00}:00–{(h + 1) % 24:00}:00: " + (open ? "HH.Venue.Open".TranslateOr("open") : "HH.Venue.Closed".TranslateOr("closed"))
                                               + (h == now ? " (" + "HH.Venue.Now".TranslateOr("now") + ")" : ""));
            }
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter; GUI.color = VenueDialogUI.Grey;
            for (int h = 0; h <= 24; h += 3)
                Widgets.Label(new Rect(r.x + h * cw - 14f, r.yMax + 1f, 28f, 16f), h.ToString());
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
        }
    }

    /// <summary>Price factor of a venue sign: tiers, slider and what guests actually pay for what is on the shelves (6.1; F35 layout).</summary>
    public class Dialog_VenuePrice : Window
    {
        readonly CompVenueSignHH comp;
        Vector2 scroll;
        List<Thing> sample; float sampleTime = -99f;
        public override Vector2 InitialSize => HUI.Size(600f, 560f, 480f, 440f);
        static readonly float[] tiers = { 0.25f, 0.5f, 0.75f, 1f, 1.3f, 1.8f };
        static readonly string[] tierNames = { "Charity", "Discount", "Budget", "Standard", "Premium", "Luxury" };

        public Dialog_VenuePrice(CompVenueSignHH comp)
        {
            this.comp = comp;
            doCloseX = true; closeOnClickedOutside = false; absorbInputAroundWindow = false; forcePause = false; draggable = true;
        }

        Venue Venue => comp.parent.Spawned ? VenueApi.VenueOfSign(comp.parent.Map, comp.parent) : null;

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent == null || comp.parent.Destroyed) { Close(); return; }
            string tier = ("HH.Venue.Tier." + VenueApi.PriceTierLabel(comp.priceFactor)).TranslateOr(VenueApi.PriceTierLabel(comp.priceFactor));
            float y = VenueDialogUI.Header(inRect, "HH.Venue.PriceTitle".TranslateOr("Prices"), comp, (comp.priceFactor * 100f).ToString("0") + " % · " + tier, VenueDialogUI.Amber);

            var rects = VenueDialogUI.Grid(inRect.x, y, inRect.width, tiers.Length, 84f, 42f, 5f, out float bottom);
            for (int i = 0; i < tiers.Length; i++)
            {
                string label = ("HH.Venue.Tier." + tierNames[i]).TranslateOr(tierNames[i]);
                if (Mathf.Abs(comp.priceFactor - tiers[i]) < 0.01f) Widgets.DrawBoxSolid(rects[i], new Color(0.55f, 0.45f, 0.25f, 0.45f));
                if (Widgets.ButtonText(rects[i], label + "\n" + (tiers[i] * 100f).ToString("0") + " %")) { comp.priceFactor = tiers[i]; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
            }
            y = bottom + 12f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "HH.Venue.PriceFactor".TranslateOr("Price factor") + ": " + (comp.priceFactor * 100f).ToString("0") + " % (" + tier + ")");
            y += 26f;
            float v = Widgets.HorizontalSlider(new Rect(inRect.x, y, inRect.width, 24f), comp.priceFactor, 0f, 3f, false, null, "0 %", "300 %", 0.05f);
            comp.priceFactor = Mathf.Round(Mathf.Clamp(v, 0f, 3f) * 20f) / 20f;
            y += 34f;

            // what guests pay now: every factor spelled out
            var venue = Venue;
            if (venue != null)
            {
                float mult = VenueApi.PriceMultiplier(venue);
                string line = "HH.Venue.PriceNow".TranslateOr("Guests pay {0} % of the market value right now").Replace("{0}", (mult * 100f).ToString("0"));
                var notes = new List<string>();
                if (Math.Abs(venue.Type.priceFactor - 1f) > 0.001f) notes.Add(venue.Type.LabelCap + " ×" + venue.Type.priceFactor.ToString("0.##"));
                if (Math.Abs(VS.F(VS.ShopPrice) - 1f) > 0.001f) notes.Add("HH.Venue.PriceSetting".TranslateOr("price level setting") + " ×" + VS.F(VS.ShopPrice).ToString("0.##"));
                if (VenueHonor.IsOn(venue)) notes.Add("HH.Honor.PriceNote".TranslateOr("honor system: full price, some guests pay less"));
                else if (!VenueApi.NeedsStaff(venue) && !VenueApi.IsStaffed(venue) && VS.F(VS.SelfService) > 0f) notes.Add("HH.Venue.SelfServiceNote".TranslateOr("self-service") + " −" + (VS.F(VS.SelfService) * 100f).ToString("0") + " %");
                if (VenueApi.IsBellService(venue)) notes.Add("HH.Venue.BellNote".TranslateOr("night bell") + " +" + (VS.F(VS.NightBellSurcharge) * 100f).ToString("0") + " %");
                if (notes.Count > 0) line += " (" + string.Join(", ", notes) + ")";
                Text.Font = GameFont.Small;
                float lh = Text.CalcHeight(line, inRect.width);
                GUI.color = VenueDialogUI.Amber; Widgets.Label(new Rect(inRect.x, y, inRect.width, lh), line); GUI.color = Color.white;
                y += lh + 6f;

                // sample prices of what is for sale
                if (Time.realtimeSinceStartup - sampleTime > 1f)
                {
                    sampleTime = Time.realtimeSinceStartup;
                    sample = VenueApi.SellsOf(venue) == SellKind.None ? new List<Thing>() : VenueSales.Stock(venue).Where(t => t.Spawned).GroupBy(t => t.def).Select(g => g.First()).OrderByDescending(t => t.MarketValue).Take(30).ToList();
                }
                float footer = 40f;
                var tableR = new Rect(inRect.x, y, inRect.width, Mathf.Max(60f, inRect.yMax - y - footer));
                DrawPriceTable(tableR, venue, mult);
                y = tableR.yMax + 4f;
            }
            VenueDialogUI.Hint(inRect.x, inRect.yMax - 34f, inRect.width - 120f, "HH.Venue.PriceHintShort".TranslateOr("Guests compare the price with what the item is worth to them; wealthy guests put up with more. Free means free."));
            if (Widgets.ButtonText(new Rect(inRect.xMax - 110f, inRect.yMax - 32f, 110f, 32f), "HH.Venue.Done".TranslateOr("Done"))) Close();
        }

        void DrawPriceTable(Rect r, Venue venue, float mult)
        {
            Widgets.DrawBoxSolid(r, new Color(0f, 0f, 0f, 0.15f));
            if (sample == null || sample.Count == 0)
            {
                GUI.color = VenueDialogUI.Grey; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, VenueApi.SellsOf(venue) == SellKind.None ? "HH.Venue.PriceNoGoods".TranslateOr("This kind of business does not sell goods from its room.") : "HH.Venue.PriceEmpty".TranslateOr("Nothing for sale in the room yet."));
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                return;
            }
            var head = new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 22f);
            var cols = HUI.Cols(head, 6f, 0.58f, 0.21f, 0.21f);
            Text.Font = GameFont.Tiny; GUI.color = VenueDialogUI.Grey;
            Widgets.Label(cols[0], "HH.Venue.PriceColItem".TranslateOr("For sale"));
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(cols[1], "HH.Venue.PriceColValue".TranslateOr("Worth"));
            Widgets.Label(cols[2], "HH.Venue.PriceColPrice".TranslateOr("Guests pay"));
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            var outR = new Rect(r.x, r.y + 24f, r.width, r.height - 26f);
            var view = new Rect(0f, 0f, outR.width - 16f, sample.Count * 26f);
            Widgets.BeginScrollView(outR, ref scroll, view);
            for (int i = 0; i < sample.Count; i++)
            {
                var t = sample[i];
                var row = new Rect(4f, i * 26f, view.width - 8f, 24f);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                var c = HUI.Cols(row, 6f, 0.58f, 0.21f, 0.21f);
                Widgets.ThingIcon(new Rect(c[0].x, c[0].y + 1f, 22f, 22f), t);
                Widgets.Label(new Rect(c[0].x + 26f, c[0].y + 2f, c[0].width - 26f, 22f), t.LabelNoCount.CapitalizeFirst().Truncate(c[0].width - 26f));
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(c[1].x, c[1].y + 2f, c[1].width, 22f), t.MarketValue.ToString("0.00"));
                Widgets.Label(new Rect(c[2].x, c[2].y + 2f, c[2].width, 22f), VenueApi.PriceOf(t, 1, venue).ToStringShort());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Widgets.EndScrollView();
        }
    }

    /// <summary>Name of a business: free text, a generated suggestion (F33) or the plain type name (F35 layout).</summary>
    public class Dialog_RenameVenue : Window
    {
        readonly CompVenueSignHH comp;
        string text;
        string suggestion;
        int suggestionRoll;
        bool focused;
        const int MaxLength = 40;
        public override Vector2 InitialSize => HUI.Size(520f, 260f, 420f, 240f);

        public Dialog_RenameVenue(CompVenueSignHH comp)
        {
            this.comp = comp; text = comp.customName ?? "";
            suggestionRoll = comp.nameRoll;
            if (comp.nameAuto) suggestion = comp.customName;
            doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; forcePause = false;
        }

        bool HasGenerator => VenueNames.HasGenerator(comp.type);

        void Suggest()
        {
            suggestionRoll++;
            string n = VenueNames.For(comp.type, comp.parent, suggestionRoll);
            if (n.NullOrEmpty()) return;
            suggestion = n; text = n;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        void Accept()
        {
            string t = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            comp.customName = t;
            comp.nameAuto = t != null && t == suggestion;
            if (comp.nameAuto) comp.nameRoll = suggestionRoll;
            Close();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = VenueDialogUI.Header(inRect, "HH.Venue.RenameTitle".TranslateOr("Name this business"), comp);
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (enter) Event.current.Use();
            // text field with a character counter; empty shows the plain type name greyed
            var fr = new Rect(inRect.x, y, inRect.width - 64f, 32f);
            GUI.SetNextControlName("HHVenueName");
            text = Widgets.TextField(fr, text, MaxLength);
            if (!focused) { UI.FocusControl("HHVenueName", this); focused = true; }
            if (text.NullOrEmpty())
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                Widgets.Label(new Rect(fr.x + 6f, fr.y + 6f, fr.width - 12f, 24f), comp.type?.LabelCap.ToString() ?? "");
                GUI.color = Color.white;
            }
            Text.Anchor = TextAnchor.MiddleRight; GUI.color = VenueDialogUI.Grey;
            Widgets.Label(new Rect(fr.xMax + 4f, fr.y, 60f, 32f), (text?.Length ?? 0) + "/" + MaxLength);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            y += 40f;
            // suggestion and plain name
            float bw = (inRect.width - 8f) / 2f;
            if (HasGenerator)
            {
                var sr = new Rect(inRect.x, y, bw, 32f);
                if (Widgets.ButtonText(sr, "HH.Venue.NameSuggest".TranslateOr("Suggest a name"))) Suggest();
                GUI.DrawTexture(new Rect(sr.x + 6f, sr.y + 6f, 20f, 20f), VenueTex.Dice);
                TooltipHandler.TipRegion(sr, "HH.Venue.NameSuggestTip".TranslateOr("Roll a name that fits this kind of business. Roll again until you like one."));
            }
            var pr = new Rect(HasGenerator ? inRect.x + bw + 8f : inRect.x, y, HasGenerator ? bw : inRect.width, 32f);
            if (Widgets.ButtonText(pr, "HH.Venue.NamePlain".TranslateOr("Plain: {0}").Replace("{0}", comp.type?.LabelCap.ToString() ?? "")))
            { text = ""; suggestion = null; }
            y += 40f;
            if (!suggestion.NullOrEmpty() && text == suggestion)
                VenueDialogUI.Hint(inRect.x, y, inRect.width, "HH.Venue.NameAutoHint".TranslateOr("A suggested name follows the sign if you change the kind of business; a name you type stays."));
            // buttons
            float bb = inRect.yMax - 34f;
            if (Widgets.ButtonText(new Rect(inRect.xMax - 230f, bb, 110f, 32f), "CancelButton".Translate())) Close();
            if (Widgets.ButtonText(new Rect(inRect.xMax - 110f, bb, 110f, 32f), "OK".Translate()) || enter) Accept();
        }
    }
}
