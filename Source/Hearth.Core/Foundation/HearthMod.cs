// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Base class for each module's entry point. Subclasses are found by reflection at startup (no registration needed).
    /// Order: RegisterSettings (Mod constructor) → Harmony patches (Mod constructor) → OnDefsLoaded (after all defs, StaticConstructorOnStartup)
    /// → OnGameStarted / OnGameLoaded (per game) → GameTick every tick while the module is on.
    /// </summary>
    public abstract class HearthModuleBase
    {
        public abstract ModuleId Id { get; }
        public virtual void RegisterSettings() { }
        public virtual void OnDefsLoaded() { }
        public virtual void OnGameStarted() { }
        public virtual void OnGameLoaded() { }
        public virtual void GameTick(int ticksGame) { }
        /// <summary>Called when the player prepares to remove the module (S6): send guests home, refund, strip hediffs …</summary>
        public virtual void PrepareRemoval(List<string> report) { }
        /// <summary>
        /// Called when the module is switched off in a running game (both "keep things in place" and after PrepareRemoval). Everything stays
        /// where it is, except state that cannot wait for the module to come back: money a guest has handed over for something that is
        /// running right now (a casino purse) is settled here, so nothing sits in limbo while the module sleeps.
        /// </summary>
        public virtual void OnSwitchedOff(List<string> report) { }
        /// <summary>Lines for the diagnosis window.</summary>
        public virtual IEnumerable<string> Diagnosis() { yield break; }
    }

    public sealed class HearthTemplate : ModSettings
    {
        public HearthProfile Profile = new HearthProfile();
        public bool ShowFirstStartCard = true;
        public bool TemplateFromLastCard;
        public TutorialMode Tutorial = TutorialMode.FullFirstTime;
        public bool EverPlayed;
        public bool ShowNerd;
        public bool VerboseLog;
        /// <summary>AI flavour opt-in (F05): version of the consent text the player agreed to (0 = never), when, and the last relay notice already shown. Per installation, not per save: privacy is personal.</summary>
        public int AiConsentVersion;
        public string AiConsentDate;
        public string AiNoticeSeen;
        public HearthSettingsStore Store => Profile.Store;
        public float Tier => Profile.Tier;
        public bool ModuleOn(ModuleId m) => Profile.ModuleOn(m);

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref Profile, "profile");
            Scribe_Values.Look(ref ShowFirstStartCard, "showFirstStartCard", true);
            Scribe_Values.Look(ref Tutorial, "tutorial", TutorialMode.FullFirstTime);
            Scribe_Values.Look(ref EverPlayed, "everPlayed");
            Scribe_Values.Look(ref ShowNerd, "showNerd");
            Scribe_Values.Look(ref VerboseLog, "verboseLog");
            Scribe_Values.Look(ref AiConsentVersion, "aiConsentVersion", 0);
            Scribe_Values.Look(ref AiConsentDate, "aiConsentDate");
            Scribe_Values.Look(ref AiNoticeSeen, "aiNoticeSeen");
            if (Profile == null) Profile = new HearthProfile();
        }
    }

    public class HearthMod : Mod
    {
        public const string Version = "1.1.0";
        public const string HarmonyId = "juliusberger00.hearthharbor";
        public static HearthMod Instance;
        public static HearthTemplate Template => Instance?.GetSettings<HearthTemplate>() ?? fallbackTemplate;
        static readonly HearthTemplate fallbackTemplate = new HearthTemplate();
        public static Harmony Harmony;
        public static readonly List<HearthModuleBase> ModuleObjects = new List<HearthModuleBase>();

        /// <summary>Runs OnSwitchedOff of every module object of <paramref name="m"/> (see HearthProfile.SetModule).</summary>
        internal static void NotifySwitchedOff(ModuleId m)
        {
            var report = new List<string>();
            foreach (var obj in ModuleObjects)
            {
                if (obj.Id != m) continue;
                // not Guard.Run: a module paused by the error barrier must still settle what it holds
                try { obj.OnSwitchedOff(report); }
                catch (Exception e) { HLog.Error(m.ToString(), obj.GetType().Name + ".OnSwitchedOff failed", e); }
            }
            foreach (var line in report) HLog.Info(m.ToString(), "Switched off: " + line);
        }
        public static bool BankAssemblyLoaded, GuestAssemblyLoaded;
        /// <summary>Set by HearthUI when Hearth.dll provides the full settings window.</summary>
        public static Action<Rect> SettingsWindowOverride;
        public static Func<string> SettingsCategoryOverride;
        /// <summary>Set by the rich settings window: jumps to a page ("mod:Bank", "difficulty" …) and clears the search. Null without Hearth.dll.</summary>
        public static Action<string> SettingsPageSelector;

        /// <summary>Opens the mod settings window of Hearth &amp; Harbor, on the given page when the rich window is installed (e.g. "mod:Bank").
        /// Used by in-game screens that point the player to "the real settings" (F19). Never throws.</summary>
        public static void OpenSettings(string pageId = null)
        {
            try
            {
                if (pageId != null) SettingsPageSelector?.Invoke(pageId);
                if (Instance != null) Find.WindowStack.Add(new RimWorld.Dialog_ModSettings(Instance));
            }
            catch (Exception e) { HLog.Warn("Core", "Could not open the mod settings: " + e.Message); }
        }
        public static readonly List<string> PatchFailures = new List<string>();

        public HearthMod(ModContentPack content) : base(content)
        {
            Instance = this;
            HLog.SessionStart(Version);
            HLog.Verbose = Template.VerboseLog;
            var asms = content.assemblies.loadedAssemblies;
            BankAssemblyLoaded = asms.Any(a => a.GetName().Name == "Silverline");
            GuestAssemblyLoaded = asms.Any(a => a.GetName().Name == "Hearth");
            HLog.Info("Core", $"Loading from {content.PackageId} ({content.FolderName}); bank={BankAssemblyLoaded} guests={GuestAssemblyLoaded}");

            foreach (var t in asms.SelectMany(SafeTypes).Where(t => typeof(HearthModuleBase).IsAssignableFrom(t) && !t.IsAbstract))
            {
                try { ModuleObjects.Add((HearthModuleBase)Activator.CreateInstance(t)); }
                catch (Exception e) { HLog.Error("Core", "Could not create module " + t.FullName, e); }
            }
            ModuleObjects.Sort((a, b) => a.Id.CompareTo(b.Id));
            CoreSettings.Register();
            foreach (var m in ModuleObjects) Guard.Run(m.Id, m.GetType().Name + ".RegisterSettings", m.RegisterSettings);

            Harmony = new Harmony(HarmonyId);
            ApplyPatches(asms);
        }

        static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        /// <summary>
        /// Applies every [HarmonyPatch] class of our assemblies one by one. A class that fails (target missing in this
        /// RimWorld version, conflicting mod …) is logged and skipped; everything else keeps working (A6, 15.1).
        /// </summary>
        void ApplyPatches(IEnumerable<Assembly> asms)
        {
            int ok = 0;
            foreach (var t in asms.SelectMany(SafeTypes))
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e)
                {
                    PatchFailures.Add(t.FullName + ": " + (e.InnerException ?? e).Message);
                    HLog.Error("Core", "Patch class " + t.FullName + " skipped", e.InnerException ?? e);
                }
            }
            HLog.Info("Core", $"Harmony: {ok} patch classes applied, {PatchFailures.Count} skipped");
        }

        public override string SettingsCategory() => SettingsCategoryOverride?.Invoke() ?? (GuestAssemblyLoaded ? "Hearth & Harbor" : "Silverline");

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (SettingsWindowOverride != null) { SettingsWindowOverride(inRect); return; }
            SimpleSettingsWindow.Draw(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            HLog.Verbose = Template.VerboseLog;
        }
    }

    [StaticConstructorOnStartup]
    public static class HearthStartup
    {
        static HearthStartup()
        {
            foreach (var m in HearthMod.ModuleObjects) Guard.Run(m.Id, m.GetType().Name + ".OnDefsLoaded", m.OnDefsLoaded);
            Guard.Run(ModuleId.Core, "ModuleGate.Init", ModuleGate.Init);   // after OnDefsLoaded: generated defs (dish recipes) are in the table too
            HLog.Info("Core", $"Defs loaded. Modules: {string.Join(", ", HearthMod.ModuleObjects.Select(m => m.Id + (Modules.Available(m.Id) ? "" : "(missing)")))}; settings registered: {HSettings.All.Count}");
        }
    }

    /// <summary>One research mode for the whole mod (KONZEPT 7.1): Full = the trees of Hearth &amp; Harbor and Silverline, Off = everything is available.</summary>
    public enum CoreResearchMode { Full = 0, Off = 1 }

    /// <summary>Settings of the core itself.</summary>
    public static class CoreSettings
    {
        public const string ResearchModeKey = "core.researchMode";

        /// <summary>A special switch (registered by the UI module, key core.*). Off when the key does not exist, never logs.</summary>
        public static bool Switch(string key)
        {
            try { return HSettings.Find(key) != null && HSettings.GetBool(key); }
            catch (Exception e) { HLog.Once("core.switch." + key, LogLevel.Warn, "Core", "Could not read switch " + key + ": " + e.Message); return false; }
        }

        /// <summary>
        /// True when research gating is off for every module: the shared setting core.researchMode = Off, or its older shortcut
        /// core.noResearchGating (registered by the UI module). Modules combine this with their own old key (bank.researchMode, city.researchMode).
        /// </summary>
        public static bool ResearchOff
        {
            get
            {
                try
                {
                    if (HSettings.Find(ResearchModeKey) != null && HSettings.GetInt(ResearchModeKey) == (int)CoreResearchMode.Off) return true;
                    if (HSettings.Find("core.noResearchGating") != null && HSettings.GetBool("core.noResearchGating")) return true;
                }
                catch (Exception e) { HLog.Once("core.researchOff", LogLevel.Warn, "Core", "Could not read the research mode: " + e.Message); }
                return false;
            }
        }

        public static void Register()
        {
            HSettings.Enum(ModuleId.Core, "Research", ResearchModeKey, CoreResearchMode.Full, "Research mode",
                "Full: the Hearth & Harbor and Silverline research trees gate their buildings and features. Off: everything is available from the start (the dark projects of the underworld still need their discovery).");
            HSettings.Enum(ModuleId.Core, "Randomness", "core.rngAlgorithm", RngAlgorithm.SplitMix64, "Random number algorithm",
                "Algorithm for all Hearth & Harbor random streams. Same odds for all, only the sequence differs. ChaCha8 is cryptographic (paranoid), SplitMix64 is fastest.", nerd: true);
            HSettings.Bool(ModuleId.Core, "Randomness", "core.sealedFate", false, "Sealed fate",
                "On: reloading a save does not re-roll guest events (everything uses sealed random streams). Player bets, bank trades and lottery are always sealed.");
            HSettings.Bool(ModuleId.Core, "Help", "core.helpHint", true, "Show the small real-life help hint where you gamble",
                "A small, quiet line on every screen where you yourself gamble: if gambling is a problem in real life, help exists (findahelpline.com). Never shown elsewhere.");
            HSettings.Bool(ModuleId.Core, "Help", "core.guide", true, "Show the Hearth & Harbor guide", "The learning helper at the top right. Right-click it to hide it.");
            var ws = HSettings.Percent(ModuleId.Core, "Help", HUI.WindowScaleKey, 1.15f, 0.8f, 1.5f, "Size of Hearth & Harbor windows",
                "All Hearth & Harbor and Silverline windows grow or shrink by this factor (on top of the automatic size for large screens).", hardMin: 0.6f, hardMax: 2f);
            ws.Global = true;
        }
    }
}
