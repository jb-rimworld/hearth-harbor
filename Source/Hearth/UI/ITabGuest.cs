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
        static readonly Vector2 Size = new Vector2(430f, 520f);
        Vector2 scroll; float viewH = 600f;

        public ITab_HH_Guest()
        {
            size = Size;
            labelKey = "HH.UI.GuestTab.Label";
            tutorTag = "HHGuest";
        }

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
            var rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
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

        /// <summary>Drawn separately from FillTab so tests can render it without an inspect pane.</summary>
        public static float DrawBody(float w, Pawn p, GuestInfo gi)
        {
            float y = 0;
            int now = Find.TickManager.TicksGame;
            var st = HearthUI.GuestStatus?.Invoke(p);
            bool readOnly = st != null && st.ReadOnly;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, w, 32f), p.LabelShortCap); Text.Font = GameFont.Small; y += 34f;
            if (readOnly && !st.StateLine.NullOrEmpty())
            {
                float sh = UiStyle.CachedHeight(st.StateLine, w - 12f, GameFont.Small) + 6f;
                Widgets.DrawBoxSolid(new Rect(0, y, w, sh), UiStyle.AmberSoft);
                UiStyle.LabelColored(new Rect(6f, y + 3f, w - 12f, sh - 6f), st.StateLine, Color.white);
                y += sh + 6f;
            }
            // stay info
            y += UiStyle.Section(0, y, w, "HH.UI.GuestTab.Stay".Translate());
            y = Line(y, w, "HH.UI.GuestTab.Faction".Translate(), p.Faction?.Name ?? "—");
            y = Line(y, w, "HH.UI.GuestTab.Kind".Translate(), GuestsPage.KindLabel(gi.Kind));
            if (st != null && !st.WingLabel.NullOrEmpty()) y = Line(y, w, "HH.UI.GuestTab.Wing".Translate(), st.WingLabel);
            if (gi.ArrivedTick > 0) y = Line(y, w, "HH.UI.GuestTab.Arrived".Translate(), "HH.UI.GuestTab.Ago".Translate((now - gi.ArrivedTick).ToStringTicksToPeriod()));
            if (gi.PlannedLeaveTick > now && !readOnly) y = Line(y, w, "HH.UI.GuestTab.Leaves".Translate(), "HH.UI.GuestTab.In".Translate((gi.PlannedLeaveTick - now).ToStringTicksToPeriod()));
            y = Line(y, w, "HH.UI.GuestTab.Payment".Translate(), gi.PaysDigital ? "HH.UI.GuestTab.Card".Translate().ToString() : "HH.UI.GuestTab.Purse".Translate().ToString());
            y += 6f;

            // satisfaction
            y += UiStyle.Section(0, y, w, "HH.UI.GuestTab.Satisfaction".Translate());
            float t = (gi.Satisfaction + 1f) / 2f;
            UiStyle.Bar(new Rect(0, y + 2f, w - 70f, 16f), t, UiStyle.Heat(t));
            UiStyle.LabelColored(new Rect(w - 64f, y, 64f, 22f), (gi.Satisfaction >= 0 ? "+" : "") + gi.Satisfaction.ToString("0.00"), Color.white, TextAnchor.MiddleRight);
            TooltipHandler.TipRegion(new Rect(0, y, w, 22f), "HH.UI.GuestTab.SatisfactionTip".Translate());
            y += 26f;
            // C10: what to do about it, first
            if (st != null && st.Problems.Count > 0)
            {
                UiStyle.LabelColored(new Rect(0, y, w, 20f), "HH.UI.GuestTab.Problems".Translate(), UiStyle.Bad, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
                foreach (var pr in st.Problems)
                {
                    float h = UiStyle.CachedHeight(pr, w - 24f, GameFont.Small);
                    Widgets.DrawBoxSolid(new Rect(4f, y + 9f, 4f, 4f), UiStyle.Bad);
                    Widgets.Label(new Rect(14f, y, w - 24f, h), pr);
                    y += h + 1f;
                }
                y += 4f;
            }
            UiStyle.LabelColored(new Rect(0, y, w, 20f), "HH.UI.GuestTab.Reasons".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); y += 20f;
            if (gi.Events.Count == 0) { UiStyle.LabelColored(new Rect(8f, y, w - 8f, 22f), "HH.UI.GuestTab.NoReasons".Translate(), UiStyle.DimGrey); y += 24f; }
            foreach (var e in gi.Events.Skip(Math.Max(0, gi.Events.Count - 8)).Reverse())
            {
                float h = UiStyle.CachedHeight(e, w - 24f, GameFont.Small);
                Widgets.Label(new Rect(14f, y, w - 24f, h), e);
                Widgets.DrawBoxSolid(new Rect(4f, y + 9f, 4f, 4f), UiStyle.Amber);
                y += h + 1f;
            }
            y += 6f;

            // money
            y += UiStyle.Section(0, y, w, "HH.UI.GuestTab.Money".Translate());
            y = Line(y, w, "HH.UI.GuestTab.Budget".Translate(), UiStyle.Silver(gi.Budget) + " " + "HH.UI.Silver".Translate());
            y = Line(y, w, "HH.UI.GuestTab.Spent".Translate(), UiStyle.Silver(gi.Spent) + " " + "HH.UI.Silver".Translate());
            double total = gi.Budget.Silver + gi.Spent.Silver;
            if (total > 0) UiStyle.Bar(new Rect(0, y + 2f, w, 8f), (float)(gi.Spent.Silver / total), UiStyle.Amber);
            y += 16f;

            // handling
            y += UiStyle.Section(0, y, w, "HH.UI.GuestTab.Handling".Translate());
            float cw = (w - 8f) / 3f;
            int i = 0;
            foreach (HandlingMode m in Enum.GetValues(typeof(HandlingMode)))
            {
                var r = new Rect(i * (cw + 4f), y, cw, 30f);
                if (UiStyle.Chip(r, ("HH.UI.Handling." + m).Translate(), gi.Handling == m, ("HH.UI.Handling." + m + ".Tip").Translate(), enabled: !readOnly) && !readOnly) SetHandling(p, gi, m);
                i++;
            }
            y += 36f;
            // C3: how close a recruit is to joining
            if (st != null && st.RecruitProgress >= 0f && !readOnly)
            {
                UiStyle.Bar(new Rect(0, y + 2f, w, 10f), st.RecruitProgress, st.RecruitProgress >= 1f ? UiStyle.Good : UiStyle.Amber);
                y += 16f;
                if (!st.RecruitLine.NullOrEmpty())
                {
                    float rh = UiStyle.CachedHeight(st.RecruitLine, w, GameFont.Tiny);
                    UiStyle.LabelColored(new Rect(0, y, w, rh), st.RecruitLine, Color.white, TextAnchor.UpperLeft, GameFont.Tiny); y += rh + 4f;
                }
            }
            string desc = ("HH.UI.Handling." + gi.Handling + ".Tip").Translate();
            float dh = UiStyle.CachedHeight(desc, w, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(0, y, w, dh), desc, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny); y += dh + 6f;

            // sections from other modules
            foreach (var s in HearthUI.GuestTabSections.ToList())
            {
                var sect = s;
                var l = new Listing_Standard();
                l.Begin(new Rect(0, y, w, 2000f));
                Guard.Run(ModuleId.Core, "UI.GuestTabSection." + sect.Id, () => sect.Draw(p, l));
                y += l.CurHeight + 4f;
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
            UiStyle.LabelColored(new Rect(4f, y, w * 0.4f, 22f), label, UiStyle.Grey, TextAnchor.MiddleLeft);
            UiStyle.LabelColored(new Rect(w * 0.4f, y, w * 0.6f, 22f), value, Color.white, TextAnchor.MiddleLeft);
            return y + 22f;
        }
    }
}
