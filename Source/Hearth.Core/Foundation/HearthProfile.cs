// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Verse;

namespace Hearth
{
    public enum ContentIntensity { Cozy = 0, Standard = 1, Dark = 2, Grim = 3 }
    public enum TutorialMode { Off = 0, Short = 1, FullFirstTime = 2, AlwaysFull = 3 }

    /// <summary>
    /// Everything game-relevant for one save (S12, N13): module switches, difficulty tier, setting values,
    /// content intensity, first-start card state. The global template (HearthMod.Template) has the same shape
    /// and is copied into a new game when it starts.
    /// </summary>
    public sealed class HearthProfile : IExposable
    {
        public static HearthProfile Current => HearthGame.Instance?.Profile;

        public Dictionary<ModuleId, bool> modules = new Dictionary<ModuleId, bool>();
        /// <summary>Difficulty as continuous tier: 0 Comped … 3 Standard Room … 6 Why are you hurting yourself?</summary>
        public float Tier = 3f;
        public ContentIntensity Content = ContentIntensity.Standard;
        public bool UnderworldDiscovered;
        public bool FirstStartCardDone;
        public HearthSettingsStore Store = new HearthSettingsStore();
        public int ProfileVersion = 1;

        public bool ModuleOn(ModuleId m)
        {
            if (m == ModuleId.Core) return true;
            return modules.TryGetValue(m, out bool on) ? on : Modules.DefaultOn(m);
        }
        public void SetModule(ModuleId m, bool on) { if (m != ModuleId.Core) modules[m] = on; }

        public HearthProfile CloneForNewGame()
        {
            var p = new HearthProfile { Tier = Tier, Content = Content, Store = Store.Clone(), FirstStartCardDone = false };
            foreach (var kv in modules) p.modules[kv.Key] = kv.Value;
            return p;
        }

        List<ModuleId> tmpK; List<bool> tmpV;
        public void ExposeData()
        {
            Scribe_Values.Look(ref ProfileVersion, "profileVersion", 1);
            Scribe_Collections.Look(ref modules, "modules", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Values.Look(ref Tier, "tier", 3f);
            Scribe_Values.Look(ref Content, "content", ContentIntensity.Standard);
            Scribe_Values.Look(ref UnderworldDiscovered, "underworldDiscovered");
            Scribe_Values.Look(ref FirstStartCardDone, "firstStartCardDone");
            Scribe_Deep.Look(ref Store, "settings");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (modules == null) modules = new Dictionary<ModuleId, bool>();
                if (Store == null) Store = new HearthSettingsStore();
            }
        }

        public static readonly string[] TierNamesEn = { "Comped", "All-Inclusive", "Bed & Breakfast", "Standard Room", "Overbooked", "One-Star Review", "Why are you hurting yourself?" };
        public static string TierName(float tier)
        {
            int i = Math.Max(0, Math.Min(6, (int)Math.Round(tier)));
            return ("HH.Tier." + i).TranslateOr(TierNamesEn[i]);
        }
        /// <summary>Start slider 0–500 % (B9, D16): literally ×0.5 → 50 % = Standard Room. Piecewise linear over the anchors.</summary>
        public static readonly float[] TierPercent = { 0f, 15f, 30f, 50f, 100f, 200f, 500f };
        public static float TierFromPercent(float pct)
        {
            if (pct <= 0) return 0;
            for (int i = 0; i < 6; i++)
                if (pct <= TierPercent[i + 1]) return i + (pct - TierPercent[i]) / (TierPercent[i + 1] - TierPercent[i]);
            return 6;
        }
        public static float PercentFromTier(float tier)
        {
            tier = Math.Max(0, Math.Min(6, tier)); int i = Math.Min(5, (int)tier);
            return TierPercent[i] + (TierPercent[i + 1] - TierPercent[i]) * (tier - i);
        }
        public bool HardTier => Tier >= 3.999f;
    }
}
