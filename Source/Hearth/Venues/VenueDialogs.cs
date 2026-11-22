// SPDX-License-Identifier: MIT
using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    /// <summary>Opening hours of a venue sign: presets, two hour pickers and a 24-hour strip (5.13, N25).</summary>
    public class Dialog_VenueHours : Window
    {
        readonly CompVenueSignHH comp;
        public override Vector2 InitialSize => new Vector2(480f, 360f);

        public Dialog_VenueHours(CompVenueSignHH comp)
        {
            this.comp = comp;
            doCloseX = true; closeOnClickedOutside = false; absorbInputAroundWindow = false; forcePause = false; draggable = true;
        }

        void Apply(int open, int close, bool always)
        {
            comp.openHour = open; comp.closeHour = close; comp.alwaysOpen = always;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent == null || comp.parent.Destroyed) { Close(); return; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 34f), "HH.Venue.HoursTitle".TranslateOr("Opening hours") + " – " + comp.DisplayName);
            Text.Font = GameFont.Small;
            float y = 42f;

            // presets
            string[] names =
            {
                "HH.Venue.PresetDay".TranslateOr("Day") + $" {VS.I(VS.DayOpen)}–{VS.I(VS.DayClose)}",
                "HH.Venue.PresetEvening".TranslateOr("Evening") + $" {VS.I(VS.EveningOpen)}–{VS.I(VS.EveningClose)}",
                "HH.Venue.PresetNight".TranslateOr("Night") + $" {VS.I(VS.NightOpen)}–{VS.I(VS.NightClose)}",
                "HH.Venue.PresetAlways".TranslateOr("Always"),
            };
            float bw = (inRect.width - 3 * 6f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                if (!Widgets.ButtonText(new Rect(i * (bw + 6f), y, bw, 30f), names[i])) continue;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                switch (i)
                {
                    case 0: Apply(VS.I(VS.DayOpen), VS.I(VS.DayClose), false); break;
                    case 1: Apply(VS.I(VS.EveningOpen), VS.I(VS.EveningClose), false); break;
                    case 2: Apply(VS.I(VS.NightOpen), VS.I(VS.NightClose), false); break;
                    default: Apply(0, 0, true); break;
                }
            }
            y += 40f;

            // 24 hour strip
            DrawStrip(new Rect(0, y, inRect.width, 34f));
            y += 48f;

            bool always = comp.alwaysOpen;
            Widgets.CheckboxLabeled(new Rect(0, y, inRect.width, 26f), "HH.Venue.AlwaysOpen".TranslateOr("open around the clock"), ref always);
            if (always != comp.alwaysOpen) { comp.alwaysOpen = always; if (!always && comp.openHour < 0) Apply(8, 22, false); }
            y += 32f;

            GUI.color = comp.alwaysOpen ? new Color(1, 1, 1, 0.4f) : Color.white;
            HourRow(new Rect(0, y, inRect.width, 28f), "HH.Venue.Opens".TranslateOr("Opens at"), comp.openHour, v => comp.openHour = v);
            y += 34f;
            HourRow(new Rect(0, y, inRect.width, 28f), "HH.Venue.Closes".TranslateOr("Closes at"), comp.closeHour, v => comp.closeHour = v);
            GUI.color = Color.white;
            y += 40f;

            Text.Font = GameFont.Tiny; GUI.color = Color.gray;
            Widgets.Label(new Rect(0, y, inRect.width, 36f), "HH.Venue.HoursHint".TranslateOr("A closing hour earlier than the opening hour means the business stays open past midnight."));
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 110f, inRect.height - 32f, 110f, 32f), "HH.Venue.Done".TranslateOr("Done"))) Close();
        }

        void HourRow(Rect r, string label, int value, Action<int> set)
        {
            Widgets.Label(new Rect(r.x, r.y, 90f, r.height), label);
            if (Widgets.ButtonText(new Rect(r.x + 95f, r.y, 28f, r.height), "−") && !comp.alwaysOpen) set((value + 23) % 24);
            Widgets.Label(new Rect(r.x + 128f, r.y, 60f, r.height), value.ToString("00") + ":00");
            if (Widgets.ButtonText(new Rect(r.x + 190f, r.y, 28f, r.height), "+") && !comp.alwaysOpen) set((value + 1) % 24);
            float nv = Widgets.HorizontalSlider(new Rect(r.x + 230f, r.y + 6f, r.width - 230f, 20f), value, 0f, 23f, false, null, null, null, 1f);
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
                Widgets.DrawBoxSolid(cell, open ? new Color(0.35f, 0.62f, 0.38f) : new Color(0.25f, 0.25f, 0.25f));
                if (h == now) Widgets.DrawBox(cell, 2);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                if (h % 3 == 0) Widgets.Label(cell, h.ToString());
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            }
        }
    }

    /// <summary>Price factor of a venue sign: slider and tiers (6.1: Free … Luxury).</summary>
    public class Dialog_VenuePrice : Window
    {
        readonly CompVenueSignHH comp;
        public override Vector2 InitialSize => new Vector2(480f, 330f);
        static readonly float[] tiers = { 0.25f, 0.5f, 0.75f, 1f, 1.3f, 1.8f };
        static readonly string[] tierNames = { "Charity", "Discount", "Budget", "Standard", "Premium", "Luxury" };

        public Dialog_VenuePrice(CompVenueSignHH comp)
        {
            this.comp = comp;
            doCloseX = true; closeOnClickedOutside = false; absorbInputAroundWindow = false; forcePause = false; draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent == null || comp.parent.Destroyed) { Close(); return; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 34f), "HH.Venue.PriceTitle".TranslateOr("Prices") + " – " + comp.DisplayName);
            Text.Font = GameFont.Small;
            float y = 42f;
            float bw = (inRect.width - 5 * 4f) / 6f;
            for (int i = 0; i < tiers.Length; i++)
            {
                string label = ("HH.Venue.Tier." + tierNames[i]).TranslateOr(tierNames[i]);
                var r = new Rect(i * (bw + 4f), y, bw, 40f);
                if (Mathf.Abs(comp.priceFactor - tiers[i]) < 0.01f) Widgets.DrawHighlight(r);
                if (Widgets.ButtonText(r, label + "\n" + (tiers[i] * 100f).ToString("0") + " %")) { comp.priceFactor = tiers[i]; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
            }
            y += 56f;
            Widgets.Label(new Rect(0, y, inRect.width, 28f), "HH.Venue.PriceFactor".TranslateOr("Price factor") + ": " + (comp.priceFactor * 100f).ToString("0") + " % ("
                + ("HH.Venue.Tier." + VenueApi.PriceTierLabel(comp.priceFactor)).TranslateOr(VenueApi.PriceTierLabel(comp.priceFactor)) + ")");
            y += 30f;
            float v = Widgets.HorizontalSlider(new Rect(0, y, inRect.width, 24f), comp.priceFactor, 0f, 3f, false, null, "0 %", "300 %", 0.05f);
            comp.priceFactor = Mathf.Round(Mathf.Clamp(v, 0f, 3f) * 20f) / 20f;
            y += 40f;
            Text.Font = GameFont.Tiny; GUI.color = Color.gray;
            Widgets.Label(new Rect(0, y, inRect.width, 60f), "HH.Venue.PriceHint".TranslateOr("Guests compare the price with what the item is worth to them. Wealthy guests put up with more. Venues without anybody on duty charge a little less (self-service); a night bell service costs extra. Free means free."));
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 110f, inRect.height - 32f, 110f, 32f), "HH.Venue.Done".TranslateOr("Done"))) Close();
        }
    }

    public class Dialog_RenameVenue : Window
    {
        readonly CompVenueSignHH comp;
        string text;
        public override Vector2 InitialSize => new Vector2(360f, 170f);

        public Dialog_RenameVenue(CompVenueSignHH comp)
        {
            this.comp = comp; text = comp.customName ?? "";
            doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 32f), "HH.Venue.RenameTitle".TranslateOr("Name this business"));
            Text.Font = GameFont.Small;
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (enter) Event.current.Use();
            text = Widgets.TextField(new Rect(0, 40f, inRect.width, 30f), text, 40);
            if (Widgets.ButtonText(new Rect(0, inRect.height - 34f, inRect.width / 2f - 4f, 32f), "HH.Venue.Reset".TranslateOr("Use default name"))) { comp.customName = null; Close(); }
            if (Widgets.ButtonText(new Rect(inRect.width / 2f + 4f, inRect.height - 34f, inRect.width / 2f - 4f, 32f), "OK".Translate()) || enter)
            {
                comp.customName = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                Close();
            }
        }
    }
}
