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
    /// "Guest" tab of a pawn (10.5): stay info, satisfaction with the latest reasons, budget and the handling mode.
    /// Added to every humanlike race in OnDefsLoaded; visible for guests (GuestApi.IsGuest) and, read-only, for Inn guests who are
    /// leaving or have checked out but are still on the map (B3), so the player can see why they left. Vanilla look.
    /// </summary>
    public class ITab_HH_Guest : ITab
    {
        /// <summary>Designed at a 900 px tall screen like every H&amp;H window (F35); <see cref="HUI.Size"/> scales it up with the screen and the player's setting.</summary>
        static Vector2 DesignedSize() => HUI.Size(500f, 600f, 440f, 460f, 0.6f, 0.74f);
        Vector2 scroll; float viewH = 600f;

        public ITab_HH_Guest()
        {
            size = DesignedSize();
            labelKey = "HH.UI.GuestTab.Label";
            tutorTag = "HHGuest";
        }

        /// <summary>The tab is sized from the screen every frame, so a resolution or size-setting change is picked up without reopening it (a constructor value would go stale).</summary>
        protected override void UpdateSize() { size = DesignedSize(); }

        /// <summary>The size the tab has right now (tests read it).</summary>
        public Vector2 CurrentSize => size;

        public override bool IsVisible
        {
            get
            {
                var p = SelPawn;
                if (p == null || !Modules.On(ModuleId.Core)) return false;
                return Guard.Run(ModuleId.Core, "UI.GuestTabVisible", () => Visible(p), false);
            }
        }

        /// <summary>Guests, plus leaving/settled Inn guests (shown read-only).</summary>
        public static bool Visible(Pawn p)
        {
            if (p == null || p.Dead) return false;
            if (GuestApi.IsGuest(p)) return true;
            return HearthUI.GuestStatus?.Invoke(p)?.ReadOnly == true;
        }

        protected override void FillTab()
        {
            var p = SelPawn;
            if (p == null) return;
            var rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Guard.Run(ModuleId.Core, "UI.GuestTab", () => DrawContents(rect, p));
        }

        void DrawContents(Rect rect, Pawn p)
        {
            UiState.SetFlag("seenGuestTab"); UiProbe.Hit("guesttab");
            var gi = GuestApi.Info(p);
            if (gi == null) { UiStyle.LabelColored(rect, "HH.UI.GuestTab.NoInfo".Translate(), UiStyle.Grey); return; }
            var view = new Rect(0, 0, rect.width - 18f, viewH);
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = DrawBody(view.width, p, gi);
            viewH = Mathf.Max(y + 6f, rect.height);
            Widgets.EndScrollView();
        }

        // ---- collapsible sections (F35): the flag "gt.<id>" means collapsed, kept per colony in the profile store
        const string CollapsePrefix = "gt.";
        public static readonly string[] SectionIds = { "stay", "satisfaction", "money", "handling" };

        static bool IsOpen(string id) => !UiState.Flag(CollapsePrefix + id);

        /// <summary>A header row with a +/- toggle and a thin rule under it. Returns true while the section is open.</summary>
        static bool SectionHeader(ref float y, float w, string id, string title, string summary = null)
        {
            bool open = IsOpen(id);
            var r = new Rect(0, y, w, 30f);
            if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.05f));
            UiStyle.LabelColored(new Rect(4f, y, 22f, 30f), open ? "\u2212" : "+", UiStyle.Amber, TextAnchor.MiddleCenter, GameFont.Medium);
            UiStyle.LabelColored(new Rect(28f, y, w - 28f, 30f), title.ToUpperInvariant(), UiStyle.Amber, TextAnchor.MiddleLeft);
            if (!open && !summary.NullOrEmpty()) UiStyle.LabelColored(new Rect(w * 0.45f, y, w * 0.55f - 6f, 30f), summary.Truncate(w * 0.55f - 8f), UiStyle.Grey, TextAnchor.MiddleRight, GameFont.Tiny);
            GUI.color = UiStyle.Line; Widgets.DrawLineHorizontal(0f, y + 30f, w); GUI.color = Color.white;
            if (Widgets.ButtonInvisible(r)) UiState.SetFlag(CollapsePrefix + id, open);
            TooltipHandler.TipRegion(r, open ? "HH.UI.GuestTab.CollapseTip".TranslateOr("Click to collapse") : "HH.UI.GuestTab.ExpandTip".TranslateOr("Click to expand"));
            y += 34f;
            return open;
        }

        /// <summary>Drawn separately from FillTab so tests can render it without an inspect pane.</summary>
        public static float DrawBody(float w, Pawn p, GuestInfo gi)
        {
            float y = 0;
            int now = Find.TickManager.TicksGame;
            var st = HearthUI.GuestStatus?.Invoke(p);
            bool readOnly = st != null && st.ReadOnly;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, w, 34f), p.LabelShortCap); Text.Font = GameFont.Small; y += 34f;
            string sub = (p.Faction?.Name ?? "—") + " · " + GuestsPage.KindLabel(gi.Kind);
            UiStyle.LabelColored(new Rect(0, y, w, 22f), sub, UiStyle.Grey); y += 26f;
            if (readOnly && !st.StateLine.NullOrEmpty())
            {
                float sh = UiStyle.CachedHeight(st.StateLine, w - 16f, GameFont.Small) + 8f;
                Widgets.DrawBoxSolid(new Rect(0, y, w, sh), UiStyle.AmberSoft);
                UiStyle.LabelColored(new Rect(8f, y + 4f, w - 16f, sh - 8f), st.StateLine, Color.white);
                y += sh + 8f;
            }
            y += 4f;

            // stay info
            if (SectionHeader(ref y, w, "stay", "HH.UI.GuestTab.Stay".Translate(), gi.PlannedLeaveTick > now && !readOnly ? "HH.UI.GuestTab.In".Translate((gi.PlannedLeaveTick - now).ToStringTicksToPeriod()).ToString() : null))
            {
                y = Line(y, w, "HH.UI.GuestTab.Faction".Translate(), p.Faction?.Name ?? "—");
                y = Line(y, w, "HH.UI.GuestTab.Kind".Translate(), GuestsPage.KindLabel(gi.Kind));
                if (st != null && !st.WingLabel.NullOrEmpty()) y = Line(y, w, "HH.UI.GuestTab.Wing".Translate(), st.WingLabel);
                if (gi.ArrivedTick > 0) y = Line(y, w, "HH.UI.GuestTab.Arrived".Translate(), "HH.UI.GuestTab.Ago".Translate((now - gi.ArrivedTick).ToStringTicksToPeriod()));
                if (gi.PlannedLeaveTick > now && !readOnly) y = Line(y, w, "HH.UI.GuestTab.Leaves".Translate(), "HH.UI.GuestTab.In".Translate((gi.PlannedLeaveTick - now).ToStringTicksToPeriod()));
                y = Line(y, w, "HH.UI.GuestTab.Payment".Translate(), gi.PaysDigital ? "HH.UI.GuestTab.Card".Translate().ToString() : "HH.UI.GuestTab.Purse".Translate().ToString());
                y += 8f;
            }

            // satisfaction
            float t = (gi.Satisfaction + 1f) / 2f;
            string satSummary = (gi.Satisfaction >= 0 ? "+" : "") + gi.Satisfaction.ToString("0.00");
            if (SectionHeader(ref y, w, "satisfaction", "HH.UI.GuestTab.Satisfaction".Translate(), satSummary))
            {
                UiStyle.Bar(new Rect(0, y + 3f, w - 76f, 18f), t, UiStyle.Heat(t));
                UiStyle.LabelColored(new Rect(w - 70f, y, 70f, 24f), satSummary, Color.white, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(new Rect(0, y, w, 24f), "HH.UI.GuestTab.SatisfactionTip".Translate());
                y += 30f;
                // C10: what to do about it, first
                if (st != null && st.Problems.Count > 0)
                {
                    UiStyle.LabelColored(new Rect(0, y, w, 20f), "HH.UI.GuestTab.Problems".Translate(), UiStyle.Bad, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
                    foreach (var pr in st.Problems)
                    {
                        float h = UiStyle.CachedHeight(pr, w - 28f, GameFont.Small);
                        Widgets.DrawBoxSolid(new Rect(5f, y + 8f, 5f, 5f), UiStyle.Bad);
                        Widgets.Label(new Rect(18f, y, w - 28f, h), pr);
                        y += h + 3f;
                    }
                    y += 4f;
                }
                UiStyle.LabelColored(new Rect(0, y, w, 20f), "HH.UI.GuestTab.Reasons".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
                if (gi.Events.Count == 0) { UiStyle.LabelColored(new Rect(8f, y, w - 8f, 22f), "HH.UI.GuestTab.NoReasons".Translate(), UiStyle.DimGrey); y += 26f; }
                foreach (var e in gi.Events.Skip(Math.Max(0, gi.Events.Count - 8)).Reverse())
                {
                    float h = UiStyle.CachedHeight(e, w - 28f, GameFont.Small);
                    Widgets.DrawBoxSolid(new Rect(5f, y + 8f, 5f, 5f), UiStyle.Amber);
                    Widgets.Label(new Rect(18f, y, w - 28f, h), e);
                    y += h + 3f;
                }
                y += 8f;
            }

            // money
            if (SectionHeader(ref y, w, "money", "HH.UI.GuestTab.Money".Translate(), UiStyle.Silver(gi.Budget) + " " + "HH.UI.Silver".Translate()))
            {
                y = Line(y, w, "HH.UI.GuestTab.Budget".Translate(), UiStyle.Silver(gi.Budget) + " " + "HH.UI.Silver".Translate());
                y = Line(y, w, "HH.UI.GuestTab.Spent".Translate(), UiStyle.Silver(gi.Spent) + " " + "HH.UI.Silver".Translate());
                double total = gi.Budget.Silver + gi.Spent.Silver;
                if (total > 0) UiStyle.Bar(new Rect(0, y + 3f, w, 10f), (float)(gi.Spent.Silver / total), UiStyle.Amber);
                y += 22f;
            }

            // handling (F04: four modes with Ideology)
            if (SectionHeader(ref y, w, "handling", "HH.UI.GuestTab.Handling".Translate(), ("HH.UI.Handling." + gi.Handling).Translate().ToString()))
            {
                var modes = HandlingRules.All(p);
                if (!modes.Contains(gi.Handling)) modes.Add(gi.Handling);
                float gap = 6f, cw = (w - gap * (modes.Count - 1)) / modes.Count;
                for (int i = 0; i < modes.Count; i++)
                {
                    var m = modes[i];
                    var r = new Rect(i * (cw + gap), y, cw, 34f);
                    if (UiStyle.Chip(r, ("HH.UI.Handling." + m).Translate(), gi.Handling == m, ("HH.UI.Handling." + m + ".Tip").Translate(), enabled: !readOnly) && !readOnly) SetHandling(p, gi, m);
                }
                y += 42f;
                // C3: how close a recruit is to joining (F04: or how far a convert's certainty has fallen)
                if (st != null && st.RecruitProgress >= 0f && !readOnly)
                {
                    UiStyle.Bar(new Rect(0, y + 2f, w, 12f), st.RecruitProgress, st.RecruitProgress >= 1f ? UiStyle.Good : UiStyle.Amber);
                    y += 20f;
                }
                if (st != null && !st.RecruitLine.NullOrEmpty() && !readOnly)
                {
                    float rh = UiStyle.CachedHeight(st.RecruitLine, w, GameFont.Tiny);
                    UiStyle.LabelColored(new Rect(0, y, w, rh), st.RecruitLine, Color.white, TextAnchor.UpperLeft, GameFont.Tiny); y += rh + 6f;
                }
                string desc = ("HH.UI.Handling." + gi.Handling + ".Tip").Translate();
                float dh = UiStyle.CachedHeight(desc, w, GameFont.Tiny);
                UiStyle.LabelColored(new Rect(0, y, w, dh), desc, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny); y += dh + 10f;
            }

            // sections from other modules
            foreach (var s in HearthUI.GuestTabSections.ToList())
            {
                var sect = s;
                var l = new Listing_Standard();
                l.Begin(new Rect(0, y, w, 2000f));
                Guard.Run(ModuleId.Core, "UI.GuestTabSection." + sect.Id, () => sect.Draw(p, l));
                y += l.CurHeight + 6f;
                l.End();
            }
            return y;
        }

        /// <summary>The one place that changes a guest's handling mode (the buttons call it; tests too).</summary>
        public static void SetHandling(Pawn p, GuestInfo gi, HandlingMode m)
        {
            if (gi == null || gi.Handling == m) return;
            if (HearthUI.GuestStatus?.Invoke(p)?.ReadOnly == true) return; // the stay is over
            gi.Handling = m;
            HLog.Debug("UI", p?.LabelShort + " handling set to " + m);
        }

        static float Line(float y, float w, string label, string value)
        {
            float lw = w * 0.38f;
            float h = Mathf.Max(24f, UiStyle.CachedHeight(value, w - lw - 4f, GameFont.Small) + 2f);
            UiStyle.LabelColored(new Rect(6f, y, lw - 6f, 24f), label, UiStyle.Grey, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(lw, y, w - lw - 4f, h), value, Color.white, TextAnchor.MiddleLeft);
            return y + h;
        }
    }
}
