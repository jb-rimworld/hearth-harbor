// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>LogFloat: a plain positive number (e.g. a yearly multiple M) whose slider runs on a log scale and whose tier anchors are interpolated in log space. The stored value is the number itself (0 is allowed by typing it).</summary>
    public enum SettingKind { Bool, Int, Float, Percent, Money, Enum, LogFloat }

    /// <summary>
    /// One tunable number (B3, N17). Registered once from code at startup (HearthModuleBase.RegisterSettings).
    /// The UI draws every registered setting automatically: slider in [SliderMin, SliderMax], number field,
    /// and a "free text" toggle that allows any value in [HardMin, HardMax] (technical limits, shown with a reason).
    /// If TierAnchors is set (7 values: Comped … Why?), the default follows the difficulty slider (9.2).
    /// Values live in the save game (HearthProfile); the global template is only the starting point for new games.
    /// </summary>
    public sealed class HSetting
    {
        public string Key;                 // "inn.arrivalRate" – lowercase module prefix + dot + camelCase
        public ModuleId Module;
        public string Category;            // UI group inside the module, e.g. "Arrivals"
        public SettingKind Kind;
        public float Default;
        public float SliderMin, SliderMax;
        public float HardMin = float.MinValue, HardMax = float.MaxValue;
        public Func<float> DynamicHardMax; // e.g. () => ThingDefOf.Silver.stackLimit * 10
        public string HardLimitReason;     // shown when the player hits a technical limit
        public float[] TierAnchors;        // optional, length 7
        public bool Nerd;                  // only shown with "Show nerd settings"
        public bool Global;                // not per save (UI preferences, log level …)
        public Type EnumType;
        public string LabelEn, DescEn;     // English fallback; translation keys HH.Setting.<Key> and HH.Setting.<Key>.Desc
        public float Step = 0f;            // slider rounding; 0 = automatic
        /// <summary>Older key this setting replaces (save compatibility): if only the old key is stored, its value is converted with <see cref="LegacyToValue"/>.</summary>
        public string LegacyKey;
        public Func<float, float> LegacyToValue;

        public bool IsLog => Kind == SettingKind.LogFloat;
        float LogLo => Math.Max(1e-6f, SliderMin);
        /// <summary>Slider position for a value: the value itself, or its natural log for a LogFloat (clamped to the slider range).</summary>
        public float ToSlider(float v)
        {
            v = Math.Max(SliderMin, Math.Min(SliderMax, v));
            return IsLog ? (float)Math.Log(Math.Max(LogLo, v)) : v;
        }
        /// <summary>Value for a slider position (inverse of <see cref="ToSlider"/>); a LogFloat is rounded to three significant digits so the numbers stay readable.</summary>
        public float FromSlider(float x)
        {
            if (!IsLog) return x;
            double v = Math.Exp(x); if (v <= 0) return SliderMin;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)) - 2);
            return (float)Math.Max(SliderMin, Math.Min(SliderMax, Math.Round(v / mag) * mag));
        }
        public float SliderLo => IsLog ? (float)Math.Log(LogLo) : SliderMin;
        public float SliderHi => IsLog ? (float)Math.Log(Math.Max(SliderMax, LogLo * 1.001f)) : SliderMax;

        /// <summary>Value stored in <paramref name="st"/> (also through the legacy key), or null if none.</summary>
        public float? StoredIn(HearthSettingsStore st)
        {
            if (st.TryGet(Key, out float v)) return Clamp(v);
            if (LegacyKey != null && LegacyToValue != null && st.TryGet(LegacyKey, out float lv)) return Clamp(LegacyToValue(lv));
            return null;
        }
        public bool IsStoredIn(HearthSettingsStore st) => st.Has(Key) || (LegacyKey != null && st.Has(LegacyKey));

        public string Label => ("HH.Setting." + Key).TranslateOr(LabelEn ?? Key);
        public string Desc => ("HH.Setting." + Key + ".Desc").TranslateOr(DescEn ?? "");
        /// <summary>Translated category name (key HH.SettingCat.&lt;Category without spaces and punctuation&gt;), English fallback.</summary>
        public string CategoryLabel => CatLabel(Category);
        public static string CatLabel(string cat) => string.IsNullOrEmpty(cat) ? "" : ("HH.SettingCat." + CatKey(cat)).TranslateOr(cat);
        public static string CatKey(string cat)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in cat) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public float EffectiveHardMax
        {
            get
            {
                if (DynamicHardMax != null) { try { return Math.Min(HardMax, DynamicHardMax()); } catch { } }
                return HardMax;
            }
        }

        public float DefaultFor(float tier)
        {
            if (TierAnchors == null || TierAnchors.Length != 7) return Default;
            tier = Math.Max(0, Math.Min(6, tier));
            int i = (int)Math.Floor(tier); if (i >= 6) return TierAnchors[6];
            float t = tier - i;
            if (Kind == SettingKind.LogFloat && TierAnchors[i] > 0f && TierAnchors[i + 1] > 0f)
                return (float)Math.Exp(Math.Log(TierAnchors[i]) + (Math.Log(TierAnchors[i + 1]) - Math.Log(TierAnchors[i])) * t);
            return TierAnchors[i] + (TierAnchors[i + 1] - TierAnchors[i]) * t;
        }

        public float Clamp(float v)
        {
            if (Kind == SettingKind.Bool) return v >= 0.5f ? 1f : 0f;
            float max = EffectiveHardMax;
            if (float.IsNaN(v)) v = Default;
            v = Math.Max(HardMin, Math.Min(max, v));
            if (Kind == SettingKind.Int || Kind == SettingKind.Enum || Kind == SettingKind.Money) v = (float)Math.Round(v);
            return v;
        }

        public string Format(float v)
        {
            switch (Kind)
            {
                case SettingKind.Bool: return v >= 0.5f ? "On" : "Off";
                case SettingKind.Percent: return (v * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " %";
                case SettingKind.Int: return ((long)v).ToString("#,##0", CultureInfo.InvariantCulture);
                case SettingKind.Money: return v.ToString("#,##0", CultureInfo.InvariantCulture) + " silver";
                case SettingKind.Enum:
                    if (EnumType != null) { string name = Enum.GetName(EnumType, (int)Math.Round(v)); if (name != null) return ("HH.Enum." + EnumType.Name + "." + name).TranslateOr(name); }
                    return ((int)Math.Round(v)).ToString();
                case SettingKind.LogFloat: return v.ToString(v >= 100 ? "#,##0" : v >= 10 ? "0.#" : v >= 1 ? "0.##" : "0.###", CultureInfo.InvariantCulture);
                default: return v.ToString(Math.Abs(v) >= 100 ? "#,##0.#" : "0.###", CultureInfo.InvariantCulture);
            }
        }
    }

    public static class HSettings
    {
        static readonly Dictionary<string, HSetting> byKey = new Dictionary<string, HSetting>();
        public static readonly List<HSetting> All = new List<HSetting>();

        public static HSetting Add(HSetting s)
        {
            if (byKey.ContainsKey(s.Key)) return byKey[s.Key];
            if (s.SliderMax <= s.SliderMin && s.Kind != SettingKind.Bool) { s.SliderMin = Math.Min(0, s.Default); s.SliderMax = Math.Max(1, s.Default * 4); }
            byKey[s.Key] = s; All.Add(s);
            return s;
        }

        // Fluent helpers for module code -------------------------------------------------------------
        public static HSetting Bool(ModuleId m, string cat, string key, bool def, string label, string desc = null, bool nerd = false)
            => Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.Bool, Default = def ? 1 : 0, SliderMin = 0, SliderMax = 1, HardMin = 0, HardMax = 1, LabelEn = label, DescEn = desc, Nerd = nerd });

        public static HSetting Float(ModuleId m, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1e6f, float[] tiers = null)
            => Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.Float, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, TierAnchors = tiers });

        /// <summary>A positive number on a log-scale slider with tier anchors interpolated in log space (e.g. profitability M). Stored as the plain number.</summary>
        public static HSetting LogFloat(ModuleId m, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1e6f, float[] tiers = null, string legacyKey = null, Func<float, float> legacyToValue = null)
            => Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.LogFloat, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, TierAnchors = tiers, LegacyKey = legacyKey, LegacyToValue = legacyToValue });

        public static HSetting Percent(ModuleId m, string cat, string key, float def, float min, float max, string label, string desc = null, bool nerd = false, float hardMin = 0f, float hardMax = 1f, float[] tiers = null)
            => Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.Percent, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, TierAnchors = tiers });

        public static HSetting Int(ModuleId m, string cat, string key, int def, int min, int max, string label, string desc = null, bool nerd = false, int hardMin = 0, int hardMax = 1_000_000, float[] tiers = null)
            => Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.Int, Default = def, SliderMin = min, SliderMax = max, HardMin = hardMin, HardMax = hardMax, LabelEn = label, DescEn = desc, Nerd = nerd, TierAnchors = tiers });

        /// <summary>Silver amounts. Hard max follows the silver stack limit × 10 (N17) unless overridden.</summary>
        public static HSetting Silver(ModuleId m, string cat, string key, int def, int min, int max, string label, string desc = null, bool nerd = false)
            => Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.Money, Default = def, SliderMin = min, SliderMax = max, HardMin = 0, HardMax = 2_000_000_000f, LabelEn = label, DescEn = desc, Nerd = nerd,
                DynamicHardMax = () => Math.Max(max, (ThingDefOf.Silver?.stackLimit ?? 500) * 10f), HardLimitReason = "10 × the silver stack limit of your game (keeps the game stable)" });

        public static HSetting Enum<T>(ModuleId m, string cat, string key, T def, string label, string desc = null, bool nerd = false) where T : struct, IConvertible
        {
            int max = 0; foreach (var v in System.Enum.GetValues(typeof(T))) max = Math.Max(max, Convert.ToInt32(v));
            return Add(new HSetting { Module = m, Category = cat, Key = key, Kind = SettingKind.Enum, EnumType = typeof(T), Default = Convert.ToInt32(def), SliderMin = 0, SliderMax = max, HardMin = 0, HardMax = max, LabelEn = label, DescEn = desc, Nerd = nerd });
        }

        public static HSetting Find(string key) => byKey.TryGetValue(key, out var s) ? s : null;

        // Reading -------------------------------------------------------------------------------------
        /// <summary>The store a setting lives in: the template for global settings (and with no game), else the save's profile.</summary>
        internal static HearthSettingsStore Store(HSetting s) => s.Global || HearthProfile.Current == null ? HearthMod.Template.Store : HearthProfile.Current.Store;
        static float Tier => HearthProfile.Current?.Tier ?? HearthMod.Template.Tier;

        public static float Get(string key)
        {
            var s = Find(key);
            if (s == null) { HLog.Once("missingSetting:" + key, LogLevel.Error, "Settings", "Unknown setting key " + key + " (not registered)"); return 0f; }
            return Get(s);
        }
        public static float Get(HSetting s)
        {
            var sv = s.StoredIn(Store(s));
            if (sv.HasValue) return sv.Value;
            return s.Clamp(s.DefaultFor(Tier));
        }
        public static bool GetBool(string key) => Get(key) >= 0.5f;
        public static int GetInt(string key) => (int)Math.Round(Get(key));
        public static T GetEnum<T>(string key) where T : struct => (T)System.Enum.ToObject(typeof(T), GetInt(key));
        public static Money GetSilver(string key) => Money.FromSilver(Get(key));

        public static bool IsOverridden(HSetting s) => s.IsStoredIn(Store(s));
        public static void Set(HSetting s, float v) { var st = Store(s); st.Set(s.Key, s.Clamp(v)); if (s.LegacyKey != null) st.Remove(s.LegacyKey); }
        public static void Set(string key, float v) { var s = Find(key); if (s != null) Set(s, v); }
        public static void Reset(HSetting s) { var st = Store(s); st.Remove(s.Key); if (s.LegacyKey != null) st.Remove(s.LegacyKey); }
    }

    /// <summary>Key → value store, saved as two parallel lists (stable across versions; unknown keys are kept).</summary>
    public sealed class HearthSettingsStore : IExposable
    {
        Dictionary<string, float> values = new Dictionary<string, float>();
        public Dictionary<string, bool> freeText = new Dictionary<string, bool>();

        public bool TryGet(string k, out float v) => values.TryGetValue(k, out v);
        public bool Has(string k) => values.ContainsKey(k);
        public void Set(string k, float v) => values[k] = v;
        public void Remove(string k) => values.Remove(k);
        public int Count => values.Count;
        public HearthSettingsStore Clone()
        {
            var c = new HearthSettingsStore();
            foreach (var kv in values) c.values[kv.Key] = kv.Value;
            foreach (var kv in freeText) c.freeText[kv.Key] = kv.Value;
            return c;
        }

        List<string> tmpK; List<float> tmpV; List<string> tmpFK; List<bool> tmpFV;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref values, "values", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Collections.Look(ref freeText, "freeText", LookMode.Value, LookMode.Value, ref tmpFK, ref tmpFV);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (values == null) values = new Dictionary<string, float>();
                if (freeText == null) freeText = new Dictionary<string, bool>();
            }
        }
    }
}
