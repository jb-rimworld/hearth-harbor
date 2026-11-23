// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>A scenario part that writes values into the Hearth &amp; Harbor profile of a new colony (modules, difficulty, settings).</summary>
    public interface IHearthScenarioSeed
    {
        void Seed(HearthProfile p);
    }

    /// <summary>A scenario part that does something in the world once the new colony exists (open a bank account, finish a research project).
    /// Runs after every module has set up its new-game state, so module components are ready.</summary>
    public interface IHearthScenarioStart
    {
        void OnStart();
    }

    /// <summary>
    /// The seeding contract of F08 (1.1.0). New-game order in vanilla: the player picks a scenario, then the storyteller page (where the
    /// hosting-difficulty dialog edits a scratch profile), then the game starts: Scenario.PostGameStart runs BEFORE our game component replaces
    /// its profile by a copy of the template. So scenario values are applied in exactly two places:
    ///   1. when the scratch profile of the storyteller page is created (the player sees the scenario values and may still change them);
    ///   2. in HearthGame.StartedNewGame on the template copy (when the player never opened the dialog).
    /// World effects (IHearthScenarioStart) run once after the whole new-game setup.
    /// </summary>
    public static class ScenarioSeed
    {
        static AccessTools.FieldRef<Scenario, List<ScenPart>> partsRef;
        /// <summary>The editable part list of a scenario (vanilla keeps it internal).</summary>
        public static List<ScenPart> PartsOf(Scenario s)
        {
            if (s == null) return null;
            try { if (partsRef == null) partsRef = AccessTools.FieldRefAccess<Scenario, List<ScenPart>>("parts"); return partsRef(s); }
            catch (Exception e) { HLog.Once("scen.parts", LogLevel.Error, "Core", "Scenario.parts not found on this game version: scenario parts of Hearth & Harbor are ignored (" + e.Message + ")"); return null; }
        }

        /// <summary>Set by Hearth.dll: opens the full settings window against a scratch profile (the "More settings…" button of the rules part).
        /// The action gets the scratch profile and a callback that runs when the window closes. Null in Silverline standalone.</summary>
        public static Action<HearthProfile, Action> OpenProfileEditor;

        /// <summary>Applies every seeding part of <paramref name="s"/> to <paramref name="p"/>. Returns how many parts applied.</summary>
        public static int Apply(HearthProfile p, Scenario s, bool quiet = false)
        {
            var parts = PartsOf(s);
            if (p == null || parts == null) return 0;
            int n = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is IHearthScenarioSeed seed)) continue;
                Guard.Run(ModuleId.Core, "Scenario.Seed", () => seed.Seed(p));
                n++;
            }
            if (n > 0 && !quiet) HLog.Info("Core", "Scenario '" + (s.name ?? "?") + "': " + n + " Hearth & Harbor part(s) applied to the profile (tier " + p.Tier.ToString("0.00") + ")");
            return n;
        }

        /// <summary>True if the scenario carries any part that changes the profile (used by the 'set by scenario' note).</summary>
        public static bool HasSeeds(Scenario s)
        {
            var parts = PartsOf(s); if (parts == null) return false;
            for (int i = 0; i < parts.Count; i++) if (parts[i] is IHearthScenarioSeed) return true;
            return false;
        }

        /// <summary>Runs the world effects of the scenario once (new game only).</summary>
        public static int RunStartHooks(Scenario s)
        {
            var parts = PartsOf(s);
            if (parts == null) return 0;
            int n = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is IHearthScenarioStart st)) continue;
                Guard.Run(ModuleId.Core, "Scenario.Start", st.OnStart);
                n++;
            }
            if (n > 0) HLog.Info("Core", "Scenario start: " + n + " Hearth & Harbor part(s) ran");
            return n;
        }

        /// <summary>Draws one scenario-editor row block inside Guard (the rows run in vanilla's scenario editor: an exception of ours must never
        /// break that window). Font, colour and anchor are put back even when the drawing failed half-way. Always Core: Core never pauses.</summary>
        public static void GuardedEdit(string site, Action draw)
        {
            try { Guard.Run(ModuleId.Core, site, draw); }
            finally { Text.Font = GameFont.Small; Text.Anchor = UnityEngine.TextAnchor.UpperLeft; UnityEngine.GUI.color = UnityEngine.Color.white; }
        }

        /// <summary>Writes one explicit value into the profile (unknown keys, global preferences and keys of missing modules are skipped).</summary>
        internal static void SetSetting(HearthProfile p, string key, float value)
        {
            var s = HSettings.Find(key);
            if (s == null) { HLog.Once("scen.unknownKey:" + key, LogLevel.Warn, "Core", "Scenario sets unknown setting '" + key + "' (ignored; a module may be missing)"); return; }
            if (s.Global) { HLog.Once("scen.globalKey:" + key, LogLevel.Warn, "Core", "Scenario sets '" + key + "', a global preference that is not per colony (ignored)"); return; }
            if (!Modules.Available(s.Module)) return;
            p.Store.Set(s.Key, s.Clamp(value));
            if (s.LegacyKey != null) p.Store.Remove(s.LegacyKey);
        }
    }

    /// <summary>One key/value pair of a scenario part. XML: &lt;li&gt;&lt;key&gt;inn.arrivalRate&lt;/key&gt;&lt;value&gt;1.4&lt;/value&gt;&lt;/li&gt;</summary>
    public sealed class HHScenSetting : IExposable
    {
        public string key;
        public float value;
        public HHScenSetting() { }
        public HHScenSetting(string k, float v) { key = k; value = v; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref value, "value");
        }
        public HHScenSetting Copy() => new HHScenSetting(key, value);
    }
}
