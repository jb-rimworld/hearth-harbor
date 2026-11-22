// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using Verse;

namespace Hearth.Shell
{
    public enum TierScale { Log, SymLog, Linear, Step }

    /// <summary>
    /// Interpolation of difficulty anchors (KONZEPT 9.2). Values with a zero anchor never use the pure log space:
    /// they use symlog (epsilon 0.01) so ln 0 is never taken. Step rows (consequence stage) switch at the nearest tier.
    /// Always finite, and monotone whenever the anchors are monotone (tested in-game by ui.calc).
    /// </summary>
    public static class TierMath
    {
        const double Eps = 0.01;
        public static double SymLog(double x) => Math.Sign(x) * Math.Log(1.0 + Math.Abs(x) / Eps);
        public static double SymExp(double y) => Math.Sign(y) * Eps * (Math.Exp(Math.Abs(y)) - 1.0);

        public static float Interp(float[] a, TierScale scale, float tier)
        {
            if (a == null || a.Length != 7) return 0f;
            if (float.IsNaN(tier) || float.IsInfinity(tier)) tier = 3f;
            tier = Math.Max(0f, Math.Min(6f, tier));
            if (scale == TierScale.Step) return a[(int)Math.Round(tier, MidpointRounding.AwayFromZero)];
            int i = Math.Min(5, (int)Math.Floor(tier)); double t = tier - i;
            double lo = a[i], hi = a[i + 1], r;
            switch (scale)
            {
                case TierScale.Log:
                    if (lo > 0 && hi > 0) r = Math.Exp(Math.Log(lo) + (Math.Log(hi) - Math.Log(lo)) * t);
                    else r = SymExp(SymLog(lo) + (SymLog(hi) - SymLog(lo)) * t);
                    break;
                case TierScale.SymLog: r = SymExp(SymLog(lo) + (SymLog(hi) - SymLog(lo)) * t); break;
                default: r = lo + (hi - lo) * t; break;
            }
            if (double.IsNaN(r) || double.IsInfinity(r)) r = lo;
            return (float)r;
        }
    }

    public enum TierFormat { Factor, Signed, Percent, Permille, Int, Stage }

    /// <summary>One line of the live calculator: a number that changes with the hosting difficulty.</summary>
    public sealed class TierRow
    {
        public string Id;                 // label key HH.UI.Calc.<Id> (+ .Desc)
        public string SettingKey;         // if registered in HSettings, the real value is shown; otherwise the reference value from 9.2
        public float[] Anchors;           // 7 values Comped … Why?
        public TierScale Scale;
        public TierFormat Format;
        public string Unit;               // optional suffix key part, e.g. "perYear"

        public float Reference(float tier) => TierMath.Interp(Anchors, Scale, tier);
        public float Standard => Anchors[3];

        public string FormatValue(float v)
        {
            var c = CultureInfo.InvariantCulture;
            switch (Format)
            {
                case TierFormat.Factor: return "×" + (Math.Abs(v) >= 10 ? v.ToString("0", c) : v.ToString("0.##", c));
                case TierFormat.Signed: return (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.00", c);
                case TierFormat.Percent: return (v * 100f).ToString("0.#", c) + " %";
                case TierFormat.Permille: return v.ToString("0.000", c);
                case TierFormat.Stage: return StageName((int)Math.Round(v));
                default: return ((int)Math.Round(v)).ToString(c);
            }
        }

        public static string StageName(int stage)
        {
            if (stage <= 0) return "HH.UI.Stage.0".Translate();
            return stage + " · " + ("HH.UI.Stage." + Math.Min(7, stage)).Translate();
        }
    }

    public static class TierTable
    {
        static TierRow R(string id, string key, float[] a, TierScale s, TierFormat f) => new TierRow { Id = id, SettingKey = key, Anchors = a, Scale = s, Format = f };

        /// <summary>The numbers of KONZEPT 9.2 that matter to a player. Anchors are the Standard Room centred table of the concept.</summary>
        public static readonly TierRow[] Rows =
        {
            R("guestMoney",      "inn.guestMoney",      new[] { 5f, 1.6f, 1.25f, 1f, 0.85f, 0.7f, 0.5f },        TierScale.Log,    TierFormat.Factor),
            R("priceTolerance",  "inn.priceTolerance",  new[] { 3f, 1.3f, 1.15f, 1f, 0.9f, 0.8f, 0.65f },        TierScale.Log,    TierFormat.Factor),
            R("arrivalRate",     "inn.arrivalRate",     new[] { 3f, 1.4f, 1.2f, 1f, 0.9f, 0.8f, 0.6f },          TierScale.Log,    TierFormat.Factor),
            R("expectation",     "inn.expectation",     new[] { -0.3f, -0.1f, -0.05f, 0f, 0.05f, 0.1f, 0.2f },   TierScale.Linear, TierFormat.Signed),
            R("trouble",         "inn.trouble",         new[] { 0f, 0.2f, 0.5f, 1f, 1.5f, 2.2f, 3.5f },          TierScale.SymLog, TierFormat.Factor),
            R("stage",           "core.consequenceStage", new[] { 0f, 1f, 2f, 4f, 5f, 6f, 7f },                 TierScale.Step,   TierFormat.Stage),
            R("slotEdge",        "casino.slotEdge",     new[] { 0.02f, 0.035f, 0.05f, 0.08f, 0.12f, 0.15f, 0.18f }, TierScale.Linear, TierFormat.Percent),
            R("bankM",           "bank.interestM",      new[] { 100f, 2f, 1.5f, 1.2f, 1f, 0.7f, 0.1f },          TierScale.Log,    TierFormat.Factor),
            R("defaultFactor",   "bank.defaultFactor",  new[] { 0f, 0.2f, 0.35f, 0.5f, 1f, 1.5f, 2.5f },         TierScale.SymLog, TierFormat.Factor),
            R("collectionStage", "bank.collectionStage",new[] { 0f, 1f, 2f, 4f, 5f, 6f, 7f },                    TierScale.Step,   TierFormat.Stage),
            R("researchCost",    "core.researchCost",   new[] { 0.25f, 0.6f, 0.8f, 1f, 1.2f, 1.4f, 2f },         TierScale.Log,    TierFormat.Factor),
            R("upkeep",          "core.upkeep",         new[] { 0f, 0.5f, 0.8f, 1f, 1.2f, 1.5f, 2f },            TierScale.SymLog, TierFormat.Factor),
            R("insuranceLoading","insurance.loading",   new[] { 0f, 0.10f, 0.15f, 0.25f, 0.35f, 0.50f, 0.80f },  TierScale.Linear, TierFormat.Percent),
            R("salesPressure",   "insurance.salesPressure", new[] { 0f, 0.05f, 0.10f, 0.30f, 0.60f, 0.80f, 1f }, TierScale.SymLog, TierFormat.Percent),
            R("addiction",       "casino.addictionChance",  new[] { 0f, 0f, 0.04f, 0.08f, 0.10f, 0.13f, 0.18f }, TierScale.SymLog, TierFormat.Percent),
            R("detection",       "casino.detection",    new[] { 0f, 0.002f, 0.003f, 0.004f, 0.005f, 0.006f, 0.008f }, TierScale.SymLog, TierFormat.Permille),
        };

        // Consequence ladder (9.4): what each tier lets the game do to you. Index = tier 0..6.
        public static readonly string[] GoodwillCap = { "0", "0", "−2", "−5", "−15", "−40", "∞" };
        public static readonly string[] RenownCap = { "0", "0", "−2", "−3", "−8", "−15", "∞" };
        public static readonly string[] FineCap = { "0", "0", "0", "300", "1000", "3000", "∞" };
        public static readonly string[] Raids = { "no", "no", "no", "no", "no", "yes ×0.6", "yes ×1.0" };

        /// <summary>Real value if the setting is registered, otherwise the reference value; second result tells which.</summary>
        public static float Effective(TierRow row, HearthProfile target, out bool fromSetting, out bool overridden)
        {
            var s = row.SettingKey == null ? null : HSettings.Find(row.SettingKey);
            if (s != null && Modules.Available(s.Module))
            {
                fromSetting = true;
                var st = s.Global ? HearthMod.Template.Profile.Store : target.Store;
                overridden = st.Has(s.Key);
                return st.TryGet(s.Key, out float v) ? s.Clamp(v) : (s.TierAnchors != null ? s.DefaultFor(target.Tier) : s.Default);
            }
            fromSetting = false; overridden = false;
            return row.Reference(target.Tier);
        }
    }
}
