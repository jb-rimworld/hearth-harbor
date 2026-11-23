// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Silverline;

namespace Hearth.Shell
{
    /// <summary>
    /// Public hooks of the player-facing shell. Other modules call these in OnDefsLoaded; nothing here is required for them to work.
    /// </summary>
    public static class HearthUI
    {
        /// <summary>
        /// "Is the mod used on this map?" probes (N08). The main tab button stays hidden until Venues.All, the bank account,
        /// a guest on the map or any probe says yes. The Inn module registers "there is a guest bed on this map" here.
        /// </summary>
        public static readonly List<Func<Map, bool>> UsageProbes = new List<Func<Map, bool>>();

        /// <summary>Set by the Inn module: "this map has at least one bed marked for guests". Counts as "used" and completes the guide step.</summary>
        public static Func<Map, bool> GuestBedProbe;

        /// <summary>Header line of the Overview page (e.g. stars and numbers of the Renown module). Return null when there is nothing to say.</summary>
        public static Func<string> RenownLine;

        /// <summary>Blocks drawn on the Overview page below the built-in numbers (Concierge tips, mini graphs …).</summary>
        public static readonly List<OverviewSection> OverviewSections = new List<OverviewSection>();

        /// <summary>Blocks added to the "Guest" tab of a pawn after the built-in ones (casino debts, spa visits …).</summary>
        public static readonly List<GuestTabSection> GuestTabSections = new List<GuestTabSection>();

        /// <summary>
        /// Set by the Inn module: header of the Guests page (renown stars, arrival forecast, wing status, door policy button).
        /// Draws into the rect it gets (x, y, width; height is a maximum) and returns the height it used.
        /// </summary>
        public static Func<Rect, float> GuestsHeader;

        /// <summary>Set by the Inn module: what the Guest tab and the Guests page show beyond <see cref="GuestInfo"/> (null = not an Inn guest).</summary>
        public static Func<Pawn, GuestTabStatus> GuestStatus;

        /// <summary>Set by the Inn module: the most pressing guest complaint on a map, as one actionable line (null = all content).</summary>
        public static Func<Map, string> TopComplaint;

        /// <summary>Inn-side facts about one guest for the shell (B3, C3, C10).</summary>
        public sealed class GuestTabStatus
        {
            /// <summary>The stay is settled: the guest is leaving or checked out. The tab shows it read-only.</summary>
            public bool ReadOnly;
            /// <summary>"Leaving early: dangerous temperature" / "Checked out: paid 95 silver" …</summary>
            public string StateLine;
            /// <summary>Player-facing wing name ("Guest Wing 1"), or null.</summary>
            public string WingLabel;
            /// <summary>Recruitment progress 0…1 (bond / threshold) when the handling is Recruit; negative otherwise.</summary>
            public float RecruitProgress = -1f;
            public string RecruitLine;
            /// <summary>Why the guest is unhappy, each line says what to do about it.</summary>
            public List<string> Problems = new List<string>();
        }

        public sealed class OverviewSection { public string Id; public int Order = 100; public Action<Listing_Standard> Draw; }
        public sealed class GuestTabSection { public string Id; public int Order = 100; public Action<Pawn, Listing_Standard> Draw; }

        public static void AddOverviewSection(string id, int order, Action<Listing_Standard> draw)
        { OverviewSections.RemoveAll(s => s.Id == id); OverviewSections.Add(new OverviewSection { Id = id, Order = order, Draw = draw }); OverviewSections.Sort((a, b) => a.Order.CompareTo(b.Order)); }

        public static void AddGuestTabSection(string id, int order, Action<Pawn, Listing_Standard> draw)
        { GuestTabSections.RemoveAll(s => s.Id == id); GuestTabSections.Add(new GuestTabSection { Id = id, Order = order, Draw = draw }); GuestTabSections.Sort((a, b) => a.Order.CompareTo(b.Order)); }

        // ---- "is the mod used" (main tab visibility) ---------------------------------------------------------------
        static float nextCheck; static bool cached;
        /// <summary>Forces the next call of <see cref="IsUsedInColony"/> to recompute (tests, after placing a sign).</summary>
        public static void InvalidateUsage() { nextCheck = 0f; }

        /// <summary>Cheap (cached for half a second of real time). Once true for a save it stays true (sticky flag), so the tab never flickers away.</summary>
        public static bool IsUsedInColony()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (UiState.Flag("used")) return true;
            if (Time.realtimeSinceStartup < nextCheck) return cached;
            nextCheck = Time.realtimeSinceStartup + 0.5f;
            cached = Guard.Run(ModuleId.Core, "UI.IsUsedInColony", ComputeUsed, false);
            if (cached) UiState.SetFlag("used");
            return cached;
        }

        static bool ComputeUsed()
        {
            if (BankApi.Ready) return true;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                if (Venues.All(map).Count > 0) return true;
                if (GuestBedProbe != null && Guard.Run(ModuleId.Core, "UI.GuestBedProbe", () => GuestBedProbe(map), false)) return true;
                if (GuestsViaInn && GuestApi.GuestsOn(map).Any()) return true;
                for (int i = 0; i < UsageProbes.Count; i++)
                {
                    var probe = UsageProbes[i];
                    if (Guard.Run(ModuleId.Core, "UI.UsageProbe", () => probe(map), false)) return true;
                }
            }
            return false;
        }

        /// <summary>True when guests are tracked by the Inn module (vanilla visitors alone do not make the tab appear).</summary>
        public static bool GuestsViaInn => GuestApi.Impl != null && Modules.On(ModuleId.Inn);
    }

    /// <summary>
    /// Small per-save UI state (guide progress, tutorial level, "used" flag). Kept as extra keys in the profile's settings store,
    /// so it is saved with the colony without a component of its own and disappears harmlessly with the mod (3.7).
    /// </summary>
    public static class UiState
    {
        const string Prefix = "ui.";
        static HearthSettingsStore Store => HearthProfile.Current?.Store;

        public static bool Flag(string key) { var s = Store; return s != null && s.TryGet(Prefix + key, out float v) && v >= 0.5f; }
        public static void SetFlag(string key, bool on = true) { var s = Store; if (s == null) return; if (on) s.Set(Prefix + key, 1f); else s.Remove(Prefix + key); }
        public static float Value(string key, float def) { var s = Store; return s != null && s.TryGet(Prefix + key, out float v) ? v : def; }
        public static void SetValue(string key, float v) { Store?.Set(Prefix + key, v); }
        public static bool HasValue(string key) => Store != null && Store.Has(Prefix + key);
        public static void Store_Remove(string key) { Store?.Remove(Prefix + key); }

        /// <summary>All keys this class may have written (used to keep them out of templates).</summary>
        public static IEnumerable<string> OwnKeys()
        {
            foreach (var k in new[] { "used", "guestSeen", "paymentSeen", "saved", "tutorial", "guideCollapsed", "doneOpen" }) yield return Prefix + k;
            foreach (var id in Hearth.Shell.ITab_HH_Guest.SectionIds) yield return Prefix + "gt." + id;   // collapsed sections of the Guest tab (F35)
            foreach (var d in DefDatabase<GuideTopicDef>.AllDefs) { yield return Prefix + "done." + d.defName; yield return Prefix + "open." + d.defName; }
        }

        /// <summary>Copy of a profile for the template: without our progress flags.</summary>
        public static HearthProfile CleanClone(HearthProfile p)
        {
            var c = p.CloneForNewGame();
            foreach (var k in OwnKeys()) c.Store.Remove(k);
            c.FirstStartCardDone = false;
            return c;
        }

        // ---- tutorial level (4.11) ---------------------------------------------------------------------------------
        /// <summary>Effective level of this save: Off, Short, or Full (Always full = Full that never turns Short).</summary>
        public static TutorialMode EffectiveTutorial()
        {
            if (HearthProfile.Current != null && HasValue("tutorial")) return (TutorialMode)Mathf.Clamp(Mathf.RoundToInt(Value("tutorial", 1)), 0, 3);
            return HearthMod.Template.Tutorial == TutorialMode.FullFirstTime ? TutorialMode.Short : HearthMod.Template.Tutorial;
        }

        /// <summary>Resolves "Full first time, then short" into a concrete level for a new save and remembers that the full tour was used.</summary>
        public static TutorialMode ResolveForNewSave(TutorialMode chosen)
        {
            if (chosen != TutorialMode.FullFirstTime) return chosen;
            var tstore = HearthMod.Template.Profile.Store;
            if (tstore.TryGet("ui.fullTourSeen", out float v) && v >= 0.5f) return TutorialMode.Short;
            tstore.Set("ui.fullTourSeen", 1f);
            try { HearthMod.Instance?.WriteSettings(); } catch (Exception e) { HLog.Warn("UI", "Could not write template: " + e.Message); }
            return TutorialMode.FullFirstTime;
        }

        public static string TutorialLabel(TutorialMode m) => ("HH.UI.Tutorial." + m).Translate();

        // ---- difficulty chosen before the colony exists (storyteller page, 9.6) --------------------------------------
        /// <summary>Scratch profile the storyteller-page dialog edits. Applied to the new colony in OnGameStarted; the template stays untouched.</summary>
        public static HearthProfile PendingProfile { get; private set; }
        public static HearthProfile EnsurePendingProfile() => PendingProfile ?? (PendingProfile = HearthMod.Template.Profile.CloneForNewGame());
        public static void ClearPendingDifficulty() { PendingProfile = null; }
        public static void ApplyPendingDifficulty(HearthProfile into)
        {
            var p = PendingProfile; PendingProfile = null;
            if (p == null || into == null) return;
            into.ReplaceModules(p.modules);
            into.Tier = p.Tier; into.Content = p.Content; into.UnderworldDiscovered = p.UnderworldDiscovered;
            into.Store = p.Store.Clone();
            HLog.Info("UI", "Hosting difficulty from the storyteller page applied: tier " + p.Tier.ToString("0.00"));
        }
    }

    /// <summary>Counts how often each shell screen was drawn (tests use it to prove that real OnGUI frames reached the code).</summary>
    public static class UiProbe
    {
        static readonly Dictionary<string, int> hits = new Dictionary<string, int>();
        public static void Hit(string name) { hits.TryGetValue(name, out int n); hits[name] = n + 1; }
        public static int Count(string name) => hits.TryGetValue(name, out int n) ? n : 0;
    }

    /// <summary>Colours and small drawing helpers so every page looks the same. Amber only as accent (2.4).</summary>
    public static class UiStyle
    {
        public static readonly Color Amber = new Color(0.851f, 0.502f, 0.247f);          // #D9803F
        public static readonly Color AmberSoft = new Color(0.851f, 0.502f, 0.247f, 0.18f);
        public static readonly Color Grey = new Color(0.72f, 0.72f, 0.72f);
        public static readonly Color DimGrey = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color Good = new Color(0.55f, 0.78f, 0.45f);
        public static readonly Color Bad = new Color(0.88f, 0.42f, 0.38f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.15f);
        public const float Pad = 10f, RowH = 28f, Gap = 6f;

        static readonly Dictionary<int, Texture2D> solids = new Dictionary<int, Texture2D>();
        public static Texture2D Solid(Color c)
        {
            int k = ((int)(c.r * 255) << 24) ^ ((int)(c.g * 255) << 16) ^ ((int)(c.b * 255) << 8) ^ (int)(c.a * 255);
            if (!solids.TryGetValue(k, out var t)) solids[k] = t = SolidColorMaterials.NewSolidColorTexture(c);
            return t;
        }

        public static void Heading(Rect r, string text)
        {
            Text.Font = GameFont.Medium; Widgets.Label(r, text); Text.Font = GameFont.Small;
        }

        /// <summary>Section header with a thin amber rule beneath. Returns the height used.</summary>
        public static float Section(float x, float y, float w, string text)
        {
            Text.Font = GameFont.Small; GUI.color = Amber;
            Widgets.Label(new Rect(x, y, w, 24f), text.ToUpperInvariant());
            GUI.color = Line; Widgets.DrawLineHorizontal(x, y + 24f, w); GUI.color = Color.white;
            return 30f;
        }

        public static void LabelColored(Rect r, string text, Color c, TextAnchor anchor = TextAnchor.UpperLeft, GameFont font = GameFont.Small)
        {
            var f = Text.Font; var a = Text.Anchor; Text.Font = font; Text.Anchor = anchor; GUI.color = c;
            Widgets.Label(r, text);
            GUI.color = Color.white; Text.Anchor = a; Text.Font = f;
        }

        public static void Bar(Rect r, float fill01, Color c)
        {
            Widgets.DrawBoxSolid(r, new Color(0.12f, 0.12f, 0.12f, 0.85f));
            var f = new Rect(r.x + 1, r.y + 1, Mathf.Max(0f, (r.width - 2f) * Mathf.Clamp01(fill01)), r.height - 2f);
            Widgets.DrawBoxSolid(f, c);
            GUI.color = Line; Widgets.DrawBox(r); GUI.color = Color.white;
        }

        /// <summary>Red → amber → green for 0..1.</summary>
        public static Color Heat(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            return t01 < 0.5f ? Color.Lerp(new Color(0.82f, 0.30f, 0.27f), new Color(0.88f, 0.70f, 0.28f), t01 * 2f)
                              : Color.Lerp(new Color(0.88f, 0.70f, 0.28f), new Color(0.45f, 0.75f, 0.40f), (t01 - 0.5f) * 2f);
        }

        /// <summary>A toggle-style button; selected ones get the amber highlight. Returns true when clicked.</summary>
        public static bool Chip(Rect r, string label, bool selected, string tip = null, bool enabled = true)
        {
            if (selected) { Widgets.DrawBoxSolid(r, AmberSoft); GUI.color = Amber; Widgets.DrawBox(r); GUI.color = Color.white; }
            else if (Mouse.IsOver(r) && enabled) Widgets.DrawHighlight(r);
            else { GUI.color = Line; Widgets.DrawBox(r); GUI.color = Color.white; }
            var a = Text.Anchor; var fnt = Text.Font; Text.Anchor = TextAnchor.MiddleCenter;
            if (label.Length > 13 && r.width < 150f) Text.Font = GameFont.Tiny;
            if (!enabled) GUI.color = DimGrey;
            Widgets.Label(r, label);
            GUI.color = Color.white; Text.Anchor = a; Text.Font = fnt;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        public static string Silver(Money m) => m.ToStringShort();
        public static string SilverWhole(Money m) => Mathf.RoundToInt((float)m.Silver).ToString("#,##0");
        public static string Ticks(int t) => t.ToStringTicksToPeriod();

        static readonly Dictionary<string, float> heightCache = new Dictionary<string, float>();
        /// <summary>Text height with a cache keyed by text + width + font (CalcHeight is slow and rows draw every frame).</summary>
        public static float CachedHeight(string text, float width, GameFont font)
        {
            string k = ((int)font) + "|" + (int)width + "|" + text;
            if (heightCache.TryGetValue(k, out float h)) return h;
            var f = Text.Font; Text.Font = font; h = Text.CalcHeight(text, width); Text.Font = f;
            if (heightCache.Count > 4000) heightCache.Clear();
            return heightCache[k] = h;
        }
    }
}
