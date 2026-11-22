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

        public override Vector2 InitialSize => new Vector2(Mathf.Min(860f, UI.screenWidth - 40f), Mathf.Min(660f, UI.screenHeight - 40f));

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
            var prof = HearthProfile.Current;
            if (prof == null) { Close(); return; }
            float y = inRect.y;
            // header art area (placeholder until the art arrives)
            var hr = new Rect(inRect.x, y, inRect.width, 96f);
            if (header == null) header = ContentFinder<Texture2D>.Get("UI/HH/UI/CardHeader", false);
            Widgets.DrawBoxSolid(hr, new Color(0.17f, 0.12f, 0.09f));
            if (header != null) GUI.DrawTexture(hr, header, ScaleMode.ScaleAndCrop);
            Widgets.DrawBoxSolid(new Rect(hr.x, hr.yMax - 3f, hr.width, 3f), UiStyle.Amber);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.95f, 0.85f);
            Widgets.Label(new Rect(hr.x + 16f, hr.y + 14f, hr.width - 32f, 36f), (JoinedExisting ? "HH.UI.Card.TitleJoined" : "HH.UI.Card.Title").Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(hr.x + 16f, hr.y + 52f, hr.width - 32f, 36f), (JoinedExisting ? "HH.UI.Card.SubJoined" : "HH.UI.Card.Sub").Translate());
            GUI.color = Color.white;
            y = hr.yMax + 14f;

            // presets
            float pw = (inRect.width - 3f * 10f) / 4f;
            var ps = (HostingPreset[])Enum.GetValues(typeof(HostingPreset));
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(inRect.x + i * (pw + 10f), y, pw, 86f);
                bool sel = preset == ps[i];
                Widgets.DrawBoxSolid(r, sel ? UiStyle.AmberSoft : new Color(1f, 1f, 1f, 0.04f));
                GUI.color = sel ? UiStyle.Amber : UiStyle.Line; Widgets.DrawBox(r, sel ? 2 : 1); GUI.color = Color.white;
                if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                UiStyle.LabelColored(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 24f), ("HH.UI.Preset." + ps[i]).Translate(), sel ? UiStyle.Amber : Color.white, TextAnchor.UpperLeft);
                UiStyle.LabelColored(new Rect(r.x + 10f, r.y + 30f, r.width - 20f, r.height - 34f), ("HH.UI.Preset." + ps[i] + ".Desc").Translate(), UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
                if (Widgets.ButtonInvisible(r)) ApplyPreset(ps[i]);
            }
            y += 98f;

            // modules
            y += UiStyle.Section(inRect.x, y, inRect.width, "HH.UI.Card.Modules".Translate());
            var mods = Modules.All.Where(m => m != ModuleId.Core && m != ModuleId.Underworld && m != ModuleId.AiFlavour && Modules.Available(m)).ToList();
            float mw = inRect.width / 3f;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var r = new Rect(inRect.x + (i % 3) * mw, y + (i / 3) * 28f, mw - 8f, 26f);
                bool on = prof.ModuleOn(m), nb = on;
                Widgets.CheckboxLabeled(r, Modules.Label(m), ref nb);
                TooltipHandler.TipRegion(r, ("HH.UI.ModuleDesc." + m).Translate().ToString() + (m == ModuleId.Bank ? "\n\n" + "HH.UI.Modules.BankHint".Translate().ToString() : ""));
                if (nb != on) { prof.SetModule(m, nb); preset = null; }
            }
            y += ((mods.Count + 2) / 3) * 28f + 4f;
            if (Modules.Available(ModuleId.Underworld))
            {
                UiStyle.LabelColored(new Rect(inRect.x, y, 140f, 28f), Modules.Label(ModuleId.Underworld), Color.white, TextAnchor.MiddleLeft);
                bool on = prof.ModuleOn(ModuleId.Underworld); int state = !on ? 2 : (prof.UnderworldDiscovered ? 1 : 0);
                string[] names = { "HH.UI.Underworld.Discover", "HH.UI.Underworld.On", "HH.UI.Underworld.Off" };
                for (int i = 0; i < 3; i++)
                    if (UiStyle.Chip(new Rect(inRect.x + 150f + i * 124f, y, 120f, 28f), names[i].Translate(), state == i, (names[i] + ".Tip").Translate()))
                    { prof.SetModule(ModuleId.Underworld, i != 2); prof.UnderworldDiscovered = i == 1; preset = null; }
                y += 36f;
            }
            UiStyle.LabelColored(new Rect(inRect.x, y, inRect.width, 20f), "HH.UI.Modules.BankHint".Translate(), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            y += 26f;

            // difficulty + tutorial
            y += UiStyle.Section(inRect.x, y, inRect.width, "HH.UI.Card.Difficulty".Translate());
            UiStyle.LabelColored(new Rect(inRect.x, y, 420f, 28f), "HH.UI.Card.DifficultyLine".Translate(HearthProfile.TierName(prof.Tier).ToUpperInvariant(), HearthProfile.PercentFromTier(prof.Tier).ToString("0")), UiStyle.Amber, TextAnchor.MiddleLeft);
            if (Widgets.ButtonText(new Rect(inRect.x + 430f, y, 160f, 28f), "HH.UI.Card.Change".Translate()))
                Find.WindowStack.Add(new Dialog_HostingDifficulty(prof, () => preset = null));
            y += 36f;
            UiStyle.LabelColored(new Rect(inRect.x, y, 120f, 28f), "HH.UI.General.Tutorial".Translate(), Color.white, TextAnchor.MiddleLeft);
            float tcx = inRect.x + 130f; float tcw = Mathf.Min(170f, (inRect.width - 140f) / 4f);
            foreach (var tm in new[] { TutorialMode.Off, TutorialMode.Short, TutorialMode.FullFirstTime, TutorialMode.AlwaysFull })
            {
                if (UiStyle.Chip(new Rect(tcx, y, tcw, 28f), UiState.TutorialLabel(tm), tutorial == tm, ("HH.UI.Tutorial." + tm + ".Tip").Translate())) tutorial = tm;
                tcx += tcw + 4f;
            }
            y += 40f;

            // bottom row
            var br = new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 34f);
            Widgets.CheckboxLabeled(new Rect(br.x, br.y + 4f, br.width - 200f, 26f), "HH.UI.Card.UseForAll".Translate(), ref useForAll);
            TooltipHandler.TipRegion(new Rect(br.x, br.y + 4f, br.width - 200f, 26f), "HH.UI.Card.UseForAllTip".Translate());
            var start = new Rect(br.xMax - 180f, br.y, 180f, 34f);
            Widgets.DrawBoxSolid(start, UiStyle.AmberSoft);
            if (Widgets.ButtonText(start, "HH.UI.Card.Start".Translate())) Close();
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
