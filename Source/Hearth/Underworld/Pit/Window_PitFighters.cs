// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Shell;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>
    /// "Fighters…" of a fight pit marker (B.3): everyone who could fight here, grouped, with the reason when someone cannot fight now;
    /// a tick box per colonist/slave (saved in the pit); the two sides of a bout the player starts himself and a line that rates the match.
    /// The list is the pit's 250-tick candidate cache (refreshed when the window opens and when a box is ticked), never rebuilt per frame.
    /// </summary>
    public class Window_PitFighters : Window
    {
        readonly CompFightPit pit;
        Vector2 scroll; float viewH = 400f;
        static readonly PitKind[] Groups = { PitKind.Colonist, PitKind.Prisoner, PitKind.Animal, PitKind.Guest, PitKind.Beast };
        const float RowH = 30f;

        public CompFightPit Pit => pit;
        public override Vector2 InitialSize => HUI.Size(680f, 640f, 560f, 460f);

        public Window_PitFighters(CompFightPit pit)
        {
            this.pit = pit;
            doCloseX = true; draggable = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; preventCameraMotion = false;
            pit.Candidates(true);
        }

        public static Window_PitFighters Open(CompFightPit pit)
        {
            if (pit == null) return null;
            var open = Find.WindowStack.WindowOfType<Window_PitFighters>();
            if (open != null && open.pit == pit) { open.pit.Candidates(true); Find.WindowStack.Notify_ManuallySetFocus(open); return open; }
            open?.Close(false);
            var w = new Window_PitFighters(pit);
            Find.WindowStack.Add(w);
            return w;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (pit?.parent == null || !pit.parent.Spawned || !Underworld.Visible) { Close(false); return; }
            Guard.Run(ModuleId.Underworld, "Pit.Window", () => Draw(inRect));
        }

        void Draw(Rect inRect)
        {
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, inRect.width - 30f, 34f), UwPay.T("Pit.Win.Title"));
                Text.Font = GameFont.Small;
                float y = 38f;
                UiStyle.LabelColored(new Rect(0f, y, inRect.width, 24f), pit.StatusLine(), UiStyle.Grey);
                y += 26f;
                float bottomH = 118f;
                var listRect = new Rect(0f, y, inRect.width, inRect.height - y - bottomH);
                DrawList(listRect);
                DrawBottom(new Rect(0f, inRect.height - bottomH + 6f, inRect.width, bottomH - 6f));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }

        void DrawList(Rect outer)
        {
            var cands = pit.Candidates();
            Widgets.DrawMenuSection(outer);
            var inner = outer.ContractedBy(6f);
            var view = new Rect(0f, 0f, inner.width - 16f, viewH);
            Widgets.BeginScrollView(inner, ref scroll, view);
            float y = 0f;
            foreach (var g in Groups)
            {
                var rows = cands.Where(c => c.Kind == g && c.Pawn != null && !c.Pawn.Destroyed).ToList();
                string off = GroupOff(g);
                if (rows.Count == 0 && off == null && g != PitKind.Colonist) continue;
                y += UiStyle.Section(0f, y, view.width, UwPay.T("Pit.Win.Group." + g));
                if (off != null) { UiStyle.LabelColored(new Rect(8f, y, view.width - 8f, 22f), off, UiStyle.DimGrey); y += 24f; continue; }
                if (rows.Count == 0) { UiStyle.LabelColored(new Rect(8f, y, view.width - 8f, 22f), UwPay.T("Pit.Win.Nobody"), UiStyle.DimGrey); y += 24f; continue; }
                foreach (var c in rows) { DrawRow(new Rect(0f, y, view.width, RowH), c); y += RowH; }
                y += 6f;
            }
            viewH = Math.Max(y, 40f);
            Widgets.EndScrollView();
        }

        string GroupOff(PitKind g)
        {
            switch (g)
            {
                case PitKind.Animal: return pit.allowAnimals ? null : UwPay.T("Pit.Win.GroupOff", UwPay.T("Pit.Animals"));
                case PitKind.Guest: return pit.allowGuests ? null : UwPay.T("Pit.Win.GroupOff", UwPay.T("Pit.Guests"));
                case PitKind.Prisoner: return pit.allowPrisoners ? null : UwPay.T("Pit.Win.GroupOff", UwPay.T("Pit.Prisoners"));
                default: return null;
            }
        }

        void DrawRow(Rect r, PitCandidate c)
        {
            var p = c.Pawn;
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            var cols = HUI.Cols(r, 6f, 0.30f, 0.17f, 0.16f, 0.12f, 0.25f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(cols[0], p.LabelShortCap);
            UiStyle.LabelColored(cols[1], PitFighters.KindLabel(c.Kind), UiStyle.Grey, TextAnchor.MiddleLeft);
            string skill = p.RaceProps.Humanlike && p.skills != null ? UwPay.T("Pit.Win.Melee", p.skills.GetSkill(SkillDefOf.Melee).Level) : UwPay.T("Pit.Win.Power", c.Power.ToString("0"));
            Widgets.Label(cols[2], skill);
            Widgets.Label(cols[3], p.health.summaryHealth.SummaryHealthPercent.ToStringPercent());
            var last = cols[4];
            if (c.Kind == PitKind.Colonist)
            {
                bool on = pit.IsTicked(p), was = on;
                var box = new Rect(last.x, last.y + (last.height - 24f) / 2f, 24f, 24f);
                Widgets.Checkbox(box.position, ref on, 24f);
                TooltipHandler.TipRegion(box, UwPay.T("Pit.Win.Tick"));
                if (on != was) { pit.SetTicked(p, on); pit.Candidates(true); }
                last = new Rect(last.x + 30f, last.y, last.width - 30f, last.height);
            }
            if (c.Reason == null) UiStyle.LabelColored(last, UwPay.T("Pit.Win.Ready"), UiStyle.Good, TextAnchor.MiddleLeft);
            else UiStyle.LabelColored(last, c.Reason, c.SoftReason ? UiStyle.Amber : UiStyle.Bad, TextAnchor.MiddleLeft);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(r, p.LabelCap + (c.Reason != null ? "\n" + c.Reason : ""));
        }

        void DrawBottom(Rect r)
        {
            var manual = pit.ManualReady();
            if (pit.pickA != null && !manual.Any(c => c.Pawn == pit.pickA)) pit.pickA = null;
            if (pit.pickB != null && !manual.Any(c => c.Pawn == pit.pickB)) pit.pickB = null;
            UiStyle.LabelColored(new Rect(r.x, r.y, r.width, 22f), UwPay.T("Pit.Win.PickSides"), UiStyle.DimGrey);
            float y = r.y + 24f, w = (r.width - 12f) / 2f;
            SideButton(new Rect(r.x, y, w, 30f), UwPay.T("Pit.Win.SideA"), true, manual);
            SideButton(new Rect(r.x + w + 12f, y, w, 30f), UwPay.T("Pit.Win.SideB"), false, manual);
            y += 34f;
            var a = pit.pickA; var b = pit.pickB;
            if (a != null && b == null) b = FightBout.BestOpponent(pit, a, manual);
            if (a != null && b != null) UiStyle.LabelColored(new Rect(r.x, y, r.width * 0.6f, 26f), a.LabelShortCap + " – " + b.LabelShortCap + ": " + PitFighters.Rating(a, b), UiStyle.Grey, TextAnchor.MiddleLeft);
            var btn = new Rect(r.xMax - 220f, y, 220f, 32f);
            string closed = pit.ClosedReason() ?? (FightBout.OfPit(pit) != null ? UwPay.T("Pit.Why.BoutRunning") : null);
            if (closed != null)
            {
                GUI.color = UiStyle.DimGrey; Widgets.DrawBoxSolid(btn, new Color(0.2f, 0.2f, 0.2f, 0.5f)); GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(btn, UwPay.T("Pit.Win.Start")); Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(btn, closed);
            }
            else if (Widgets.ButtonText(btn, UwPay.T("Pit.Win.Start")))
            {
                if (!FightBout.TryStartFor(pit, true, out string why)) Messages.Message(UwPay.T("Pit.CannotStart", why), pit.parent, MessageTypeDefOf.RejectInput, false);
            }
        }

        void SideButton(Rect r, string label, bool sideA, List<PitCandidate> manual)
        {
            var cur = sideA ? pit.pickA : pit.pickB;
            string text = label + ": " + (cur != null ? cur.LabelShortCap : UwPay.T("Pit.Win.Anyone"));
            if (!Widgets.ButtonText(r, text)) return;
            var opts = new List<FloatMenuOption> { new FloatMenuOption(UwPay.T("Pit.Win.Anyone"), () => { if (sideA) pit.pickA = null; else pit.pickB = null; }) };
            foreach (var c in manual)
            {
                var p = c.Pawn;
                if (p == (sideA ? pit.pickB : pit.pickA)) continue;
                opts.Add(new FloatMenuOption(p.LabelShortCap + " (" + PitFighters.KindLabel(c.Kind) + ")", () => { if (sideA) pit.pickA = p; else pit.pickB = p; }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }
}
