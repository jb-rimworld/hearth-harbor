// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    public enum HostingPreset { JustWork, CozyInn, Tycoon, Nerd }

    /// <summary>
    /// The first-start card (4.4, N13): once per new colony (and once when the mod joins a running one).
    /// Choices apply live to the colony's profile, so closing the card in any way keeps them. "Start" closes it.
    /// </summary>
    public sealed class Dialog_FirstStartCard : Window
    {
        public static int OpenedCount;
        public static Dialog_FirstStartCard Current_;
        public readonly bool JoinedExisting;
        HostingPreset? preset;
        TutorialMode tutorial;
        bool useForAll;
        bool applied;
        Texture2D header;
        public bool UseForAll { get => useForAll; set => useForAll = value; }
        public TutorialMode Tutorial { get => tutorial; set => tutorial = value; }
        public HostingPreset? Preset => preset;

        public override Vector2 InitialSize => HUI.Size(1000f, 740f, 760f, 560f, 0.96f, 0.94f);
        Vector2 scroll; float bodyH = 600f;

        public Dialog_FirstStartCard(bool joinedExisting)
        {
            JoinedExisting = joinedExisting;
            forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; doCloseX = false; doCloseButton = false;
            closeOnAccept = false; closeOnCancel = false; draggable = false; preventSave = false;
            tutorial = HearthMod.Template.Tutorial;
            preset = DetectPreset(HearthProfile.Current);
        }

        /// <summary>JUST WORK is highlighted when the colony profile is exactly the default (all default modules, Standard Room).</summary>
        public static HostingPreset? DetectPreset(HearthProfile prof)
        {
            if (prof == null) return null;
            if (Mathf.Abs(prof.Tier - 3f) < 0.001f && Modules.All.Where(m => m != ModuleId.Core).All(m => prof.ModuleOn(m) == Modules.DefaultOn(m))) return HostingPreset.JustWork;
            return null;
        }

        public override void PostOpen() { base.PostOpen(); OpenedCount++; Current_ = this; }

        // ---- presets ----------------------------------------------------------------------------------------------
        /// <summary>Applies a preset to the colony's profile (modules, tier, tutorial, nerd mode).</summary>
        public void ApplyPreset(HostingPreset p)
        {
            var prof = HearthProfile.Current; if (prof == null) return;
            preset = p;
            foreach (var m in Modules.All.Where(m => m != ModuleId.Core)) prof.SetModule(m, Modules.DefaultOn(m));
            prof.UnderworldDiscovered = false;
            switch (p)
            {
                case HostingPreset.JustWork: prof.Tier = 3f; tutorial = TutorialMode.FullFirstTime; break;
                case HostingPreset.CozyInn:
                    prof.Tier = 2f; tutorial = TutorialMode.FullFirstTime;
                    prof.SetModule(ModuleId.Casino, false); prof.SetModule(ModuleId.Underworld, false); break;
                case HostingPreset.Tycoon: prof.Tier = 3f; tutorial = TutorialMode.Short; break;
                case HostingPreset.Nerd: prof.Tier = 3f; tutorial = TutorialMode.Off; break;
            }
            if (p == HostingPreset.Nerd) HearthMod.Template.ShowNerd = true;
        }

        /// <summary>Everything the card decides that is not already live in the profile: tutorial level, template copy, done flag.</summary>
        public void Finish()
        {
            if (applied) return; applied = true;
            var prof = HearthProfile.Current;
            if (prof != null)
            {
                prof.FirstStartCardDone = true;
                UiState.SetValue("tutorial", (int)UiState.ResolveForNewSave(tutorial));
                if (useForAll)
                {
                    HearthMod.Template.Profile = UiState.CleanClone(prof);
                    HearthMod.Template.Tutorial = tutorial;
                    try { HearthMod.Instance.WriteSettings(); } catch (Exception e) { HLog.Warn("UI", "Could not write template: " + e.Message); }
                }
                HLog.Info("UI", $"First-start card done: preset {preset?.ToString() ?? "custom"}, tier {prof.Tier:0.00}, tutorial {tutorial}, forAll {useForAll}, joined {JoinedExisting}");
            }
        }

        public override void PreClose() { base.PreClose(); Finish(); }
        public override void PostClose() { base.PostClose(); if (Current_ == this) Current_ = null; }

        // ---- drawing ----------------------------------------------------------------------------------------------
        public override void DoWindowContents(Rect inRect)
        {
            Guard.Run(ModuleId.Core, "UI.FirstStartCard", () => Draw(inRect));
        }

        void Draw(Rect inRect)
        {
            UiProbe.Hit("card");
            var prof = HearthProfile.Current;
            if (prof == null) { Close(); return; }
            float y = inRect.y;
            // header art area (placeholder until the art arrives); height follows the text
            string title = (JoinedExisting ? "HH.UI.Card.TitleJoined" : "HH.UI.Card.Title").Translate();
            string sub = (JoinedExisting ? "HH.UI.Card.SubJoined" : "HH.UI.Card.Sub").Translate();
            float th = HUI.TextH(title, inRect.width - 40f, GameFont.Medium), sh = HUI.TextH(sub, inRect.width - 40f);
            float headH = Mathf.Max(104f, 18f + th + 6f + sh + 18f);
            var hr = new Rect(inRect.x, y, inRect.width, headH);
            if (header == null) header = ContentFinder<Texture2D>.Get("UI/HH/UI/CardHeader", false);
            Widgets.DrawBoxSolid(hr, new Color(0.17f, 0.12f, 0.09f));
            if (header != null) GUI.DrawTexture(hr, header, ScaleMode.ScaleAndCrop);
            Widgets.DrawBoxSolid(new Rect(hr.x, hr.yMax - 3f, hr.width, 3f), UiStyle.Amber);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.95f, 0.85f);
            Widgets.Label(new Rect(hr.x + 20f, hr.y + 16f, hr.width - 40f, th), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(hr.x + 20f, hr.y + 16f + th + 6f, hr.width - 40f, sh), sub);
            GUI.color = Color.white;
            y = hr.yMax + 12f;

            // sticky footer: use-for-all + Start
            float footH = 46f;
            var br = new Rect(inRect.x, inRect.yMax - footH, inRect.width, footH);
            Widgets.DrawBoxSolid(new Rect(br.x, br.y, br.width, 1f), UiStyle.Line);
            string useAll = "HH.UI.Card.UseForAll".Translate();
            float startW = Mathf.Clamp(HUI.TextW("HH.UI.Card.Start".Translate()) + 60f, 190f, 280f);
            var useR = new Rect(br.x, br.y + 8f, br.width - startW - 16f, Mathf.Max(28f, HUI.TextH(useAll, br.width - startW - 60f)));
            Widgets.CheckboxLabeled(useR, useAll, ref useForAll, false, null, null, true);
            TooltipHandler.TipRegion(useR, "HH.UI.Card.UseForAllTip".Translate());
            var start = new Rect(br.xMax - startW, br.y + 6f, startW, 36f);
            Widgets.DrawBoxSolid(start, UiStyle.AmberSoft);
            if (Widgets.ButtonText(start, "HH.UI.Card.Start".Translate())) Close();

            // scrolling body between header and footer
            var outer = new Rect(inRect.x, y, inRect.width, br.y - y - 6f);
            float w = PageFrame.BeginScroll(outer, ref scroll, bodyH);
            float used = DrawBody(w, prof);
            bodyH = PageFrame.EndScroll(used);
        }

        float DrawBody(float w, HearthProfile prof)
        {
            float y = 0f;
            // presets: four cards in a row when wide, two by two otherwise; every card is as tall as the tallest text
            var ps = (HostingPreset[])Enum.GetValues(typeof(HostingPreset));
            int perRow = w >= 760f ? 4 : 2;
            float gap = 12f, pw = (w - gap * (perRow - 1)) / perRow;
            float cardH = 96f;
            foreach (var p in ps)
                cardH = Mathf.Max(cardH, 12f + HUI.TextH(("HH.UI.Preset." + p).Translate(), pw - 24f) + 4f + HUI.TextH(("HH.UI.Preset." + p + ".Desc").Translate(), pw - 24f, GameFont.Tiny) + 12f);
            for (int i = 0; i < ps.Length; i++)
            {
                var r = new Rect((i % perRow) * (pw + gap), y + (i / perRow) * (cardH + gap), pw, cardH);
                bool sel = preset == ps[i];
                Widgets.DrawBoxSolid(r, sel ? UiStyle.AmberSoft : new Color(1f, 1f, 1f, 0.04f));
                GUI.color = sel ? UiStyle.Amber : UiStyle.Line; Widgets.DrawBox(r, sel ? 2 : 1); GUI.color = Color.white;
                if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                string pl = ("HH.UI.Preset." + ps[i]).Translate();
                float plh = HUI.TextH(pl, r.width - 24f);
                UiStyle.LabelColored(new Rect(r.x + 12f, r.y + 10f, r.width - 24f, plh), pl, sel ? UiStyle.Amber : Color.white, TextAnchor.UpperLeft);
                UiStyle.LabelColored(new Rect(r.x + 12f, r.y + 14f + plh, r.width - 24f, r.height - plh - 20f), ("HH.UI.Preset." + ps[i] + ".Desc").Translate(), UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                if (Widgets.ButtonInvisible(r)) ApplyPreset(ps[i]);
            }
            y += ((ps.Length + perRow - 1) / perRow) * (cardH + gap) + 4f;

            // modules
            y += UiStyle.Section(0f, y, w, "HH.UI.Card.Modules".Translate());
            var mods = Modules.All.Where(m => m != ModuleId.Core && m != ModuleId.Underworld && m != ModuleId.AiFlavour && Modules.Available(m)).ToList();
            int mcols = w >= 700f ? 3 : 2;
            float mw = w / mcols;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var r = new Rect((i % mcols) * mw, y + (i / mcols) * 32f, mw - 10f, 30f);
                bool on = prof.ModuleOn(m), nb = on;
                Widgets.CheckboxLabeled(r, Modules.Label(m), ref nb, false, null, null, true);
                TooltipHandler.TipRegion(r, ("HH.UI.ModuleDesc." + m).Translate().ToString() + (m == ModuleId.Bank ? "\n\n" + "HH.UI.Modules.BankHint".Translate().ToString() : ""));
                if (nb != on) { prof.SetModule(m, nb); preset = null; }
            }
            y += ((mods.Count + mcols - 1) / mcols) * 32f + 6f;
            if (Modules.Available(ModuleId.Underworld))
            {
                string uwl = Modules.Label(ModuleId.Underworld);
                float lw = Mathf.Clamp(HUI.TextW(uwl) + 16f, 140f, 260f);
                UiStyle.LabelColored(new Rect(0f, y, lw, 32f), uwl, Color.white, TextAnchor.MiddleLeft);
                bool on = prof.ModuleOn(ModuleId.Underworld); int state = !on ? 2 : (prof.UnderworldDiscovered ? 1 : 0);
                string[] names = { "HH.UI.Underworld.Discover", "HH.UI.Underworld.On", "HH.UI.Underworld.Off" };
                float cw = Mathf.Min(150f, (w - lw - 8f) / 3f - 4f);
                for (int i = 0; i < 3; i++)
                    if (UiStyle.Chip(new Rect(lw + i * (cw + 4f), y, cw, 32f), names[i].Translate(), state == i, (names[i] + ".Tip").Translate()))
                    { prof.SetModule(ModuleId.Underworld, i != 2); prof.UnderworldDiscovered = i == 1; preset = null; }
                y += 40f;
            }
            string bankHint = "HH.UI.Modules.BankHint".Translate();
            float bh = HUI.TextH(bankHint, w, GameFont.Tiny);
            UiStyle.LabelColored(new Rect(0f, y, w, bh), bankHint, UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += bh + 10f;

            // difficulty + tutorial
            y += UiStyle.Section(0f, y, w, "HH.UI.Card.Difficulty".Translate());
            string dl = "HH.UI.Card.DifficultyLine".Translate(HearthProfile.TierName(prof.Tier).ToUpperInvariant(), HearthProfile.PercentFromTier(prof.Tier).ToString("0"));
            string chg = "HH.UI.Card.Change".Translate();
            float chgW = Mathf.Clamp(HUI.TextW(chg) + 36f, 160f, 280f);
            float dlW = w - chgW - 16f;
            float dlH = Mathf.Max(32f, HUI.TextH(dl, dlW));
            UiStyle.LabelColored(new Rect(0f, y, dlW, dlH), dl, UiStyle.Amber, TextAnchor.MiddleLeft);
            if (Widgets.ButtonText(new Rect(w - chgW, y + (dlH - 32f) / 2f, chgW, 32f), chg))
                Find.WindowStack.Add(new Dialog_HostingDifficulty(prof, () => preset = null));
            y += dlH + 8f;
            string tut = "HH.UI.General.Tutorial".Translate();
            float tlw = Mathf.Clamp(HUI.TextW(tut) + 16f, 110f, 220f);
            var modes = new[] { TutorialMode.Off, TutorialMode.Short, TutorialMode.FullFirstTime, TutorialMode.AlwaysFull };
            // chips beside the label when they fit (190 px each), under it otherwise
            bool beside = w - tlw >= 4 * 150f;
            float tcw, tcx0, ty = y;
            if (beside) { tcw = Mathf.Min(200f, (w - tlw - 12f) / 4f - 4f); tcx0 = tlw; UiStyle.LabelColored(new Rect(0f, y, tlw, 32f), tut, Color.white, TextAnchor.MiddleLeft); }
            else { UiStyle.LabelColored(new Rect(0f, y, w, 28f), tut, Color.white, TextAnchor.MiddleLeft); ty += 30f; tcw = (w - 12f) / 2f - 2f; tcx0 = 0f; }
            for (int i = 0; i < modes.Length; i++)
            {
                var tm = modes[i];
                int perLine = beside ? 4 : 2;
                var cr = new Rect(tcx0 + (i % perLine) * (tcw + 4f), ty + (i / perLine) * 36f, tcw, 32f);
                if (UiStyle.Chip(cr, UiState.TutorialLabel(tm), tutorial == tm, ("HH.UI.Tutorial." + tm + ".Tip").Translate())) tutorial = tm;
            }
            y = ty + (beside ? 1 : 2) * 36f + 8f;
            return y;
        }
    }

    /// <summary>Decides when the card opens. Called every 30 ticks from the UI module's game tick.</summary>
    public static class FirstStartCardTrigger
    {
        static bool shownThisSession;
        public const int MinTick = 120;

        public static void Reset() { shownThisSession = false; }

        public static void Tick(int ticksGame)
        {
            var g = HearthGame.Instance;
            if (g == null || g.Profile == null || g.Profile.FirstStartCardDone || shownThisSession) return;
            if (ticksGame < MinTick || Find.CurrentMap == null || Find.WindowStack == null) return;
            if (!HearthMod.Template.ShowFirstStartCard)
            {
                // card off: every new game quietly gets the template (S12)
                g.Profile.FirstStartCardDone = true;
                UiState.SetValue("tutorial", (int)UiState.ResolveForNewSave(HearthMod.Template.Tutorial));
                HLog.Info("UI", "First-start card disabled: template applied silently");
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_FirstStartCard>() != null) return;
            if (Find.WindowStack.WindowOfType<Page>() != null) return;
            shownThisSession = true;
            Find.WindowStack.Add(new Dialog_FirstStartCard(!g.IsNewGame));
        }
    }
}
