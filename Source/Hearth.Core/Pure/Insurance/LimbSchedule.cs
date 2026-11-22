// SPDX-License-Identifier: MIT
// Pure code (no game references). Gliedertaxe of Clawback Mutual (Anhang B.2, B.3).
using System;
using System.Collections.Generic;

namespace Hearth.Insurance
{
    /// <summary>One line of the Schedule of Limbs: value in percent of the basic sum, per side, with the dry comment.</summary>
    public sealed class LimbEntry
    {
        public string Key;          // "Hand", "Thumb", "BigToe" …
        public double Percent;
        public bool Lethal;         // handled under Death, no invalidity
        public string Comment;
        public bool SharedByTag;    // value is a total divided among all parts with the same source tag (B.3)
        public string Tag;          // e.g. "SightSource"
        public bool LimbFamily;     // value is scaled 2/N for bodies with more than two limbs of that kind
    }

    public static class LimbSchedule
    {
        static readonly Dictionary<string, LimbEntry> table = new Dictionary<string, LimbEntry>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, double> tagTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> lethalTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConsciousnessSource", "BloodPumpingSource", "BloodFiltrationLiver" };

        static LimbSchedule() { LoadDefaults(); }

        static void A(string key, double pct, string comment, string tag = null, bool shared = false, bool family = false)
            => table[key] = new LimbEntry { Key = key, Percent = pct, Comment = comment, Tag = tag, SharedByTag = shared, LimbFamily = family };
        static void L(string key, string comment) => table[key] = new LimbEntry { Key = key, Percent = 0, Lethal = true, Comment = comment };

        /// <summary>Restore the built-in schedule (Anhang B.2). Mods may then override single lines (SL_LimbScheduleDef).</summary>
        public static void LoadDefaults()
        {
            table.Clear(); tagTotals.Clear();
            A("Shoulder", 70, "The whole arm, shoulder included. We measured twice.", family: true);
            A("Arm", 65, "Five percent less, because you kept the shoulder. Lucky you.", family: true);
            A("Humerus", 35, "Still technically an arm. We checked.", family: true);
            A("Radius", 30, "Half a forearm's worth of disappointment.", family: true);
            A("Clavicle", 10, "Nobody knew they had one until now.", family: true);
            A("Hand", 55, "Applause is no longer covered.", family: true);
            A("Thumb", 20, "Opposable. Now opposed.");
            A("IndexFinger", 10, "You may still point. Elsewhere.");
            A("MiddleFinger", 5, "Expressive value not insured.");
            A("RingFinger", 5, "Jewellery cover sold separately.");
            A("Pinky", 5, "Refined tea posture reclassified as casual.");
            A("Finger", 5, "A finger. Counted, labelled and filed.");
            A("Leg", 70, "Walking is now a group activity.", family: true);
            A("Femur", 35, "Load-bearing. Formerly.", family: true);
            A("Tibia", 25, "The shin has filed a complaint. Denied.", family: true);
            A("Foot", 40, "One less sock to lose.", family: true);
            A("BigToe", 5, "Balance is a lifestyle choice.");
            A("Toe", 2, "Collectively vocal. Individually, two percent.");
            A("Eye", 50, "Depth perception was always overrated.", "SightSource", shared: true);
            A("Ear", 30, "We heard about it. Barely.", "HearingSource", shared: true);
            A("Nose", 10, "Your cooking finally smells fine.");
            A("Tongue", 20, "Taste 5 %. Complaining 15 %. Further claims in writing, please.", "TalkingSource");
            A("Jaw", 30, "Covered. Please don't chew on it.", "EatingSource");
            A("Skull", 10, "The brain's packaging. Contents assessed separately.");
            A("Pelvis", 20, "Hips don't lie. Claims forms do.");
            A("Ribcage", 5, "We counted your ribs. You have one ribcage.");
            A("Sternum", 5, "Chest-related, not heart-related. Different department.");
            A("Lung", 25, "The second lung remains included. While it lasts.", "BreathingSource", shared: true);
            A("Kidney", 15, "One is a spare. The other is also, technically, a spare.", "BloodFiltrationKidney", shared: true);
            A("Stomach", 30, "Gourmet rider void.");
            A("Waist", 0, "Coverage zero in your body, coverage zero in your policy.");
            A("Brain", 0, "Cognitive decline is assessed by our most cognitive employee.");
            A("Spine", 0, "Back pain is a pre-existing condition. Always.");
            const string death = "Handled under Death. Our condolences are pre-printed.";
            L("Heart", death); L("Liver", death); L("Neck", death); L("Head", death); L("Torso", death);

            tagTotals["SightSource"] = 100; tagTotals["HearingSource"] = 60; tagTotals["BreathingSource"] = 50;
            tagTotals["BloodFiltrationKidney"] = 30; tagTotals["EatingSource"] = 30; tagTotals["TalkingSource"] = 20;
        }

        /// <summary>Add or replace a line (used by SL_LimbScheduleDef from XML).</summary>
        public static void Set(LimbEntry e) { if (e != null && !string.IsNullOrEmpty(e.Key)) table[e.Key] = e; }
        public static void SetTagTotal(string tag, double total) { if (!string.IsNullOrEmpty(tag)) tagTotals[tag] = total; }
        public static IEnumerable<LimbEntry> All => table.Values;

        public static bool TryGet(string key, out LimbEntry e) => table.TryGetValue(key ?? "", out e);

        /// <summary>
        /// Table key for a RimWorld body part: the def name, refined by the custom label for fingers and toes
        /// ("left thumb" → Thumb, "right big toe" → BigToe).
        /// </summary>
        public static string KeyFor(string partDefName, string label)
        {
            string l = (label ?? "").ToLowerInvariant();
            if (partDefName == "Finger")
            {
                if (l.Contains("thumb")) return "Thumb";
                if (l.Contains("index")) return "IndexFinger";
                if (l.Contains("middle")) return "MiddleFinger";
                if (l.Contains("ring")) return "RingFinger";
                if (l.Contains("pinky") || l.Contains("little")) return "Pinky";
                return "Finger";
            }
            if (partDefName == "Toe") return l.Contains("big") ? "BigToe" : "Toe";
            return partDefName;
        }

        /// <summary>Value divided among identical sources: Eye 50 for two eyes, 25 for four (B.3).</summary>
        public static double SharedValue(double total, int sources) => sources <= 0 ? total : total / sources;

        /// <summary>Limb scaling: bodies with more than two limbs of a kind do not earn more than two would (4 arms pay as 2).</summary>
        public static double LimbScale(int limbsOfKind) => limbsOfKind > 2 ? 2.0 / limbsOfKind : 1.0;

        /// <summary>Total value of a part tag for foreign bodies (B.3 tag table); 0 = unknown, negative = death tag.</summary>
        public static double TagTotal(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return 0;
            if (lethalTags.Contains(tag)) return -1;
            return tagTotals.TryGetValue(tag, out var v) ? v : 0;
        }

        /// <summary>Fallback value for a limb part found only by tags: 70/55/20 manipulation, 70/40/5 moving, by position (B.3).</summary>
        public static double LimbTagValue(string tag, bool isFirstDigit, bool isLargestDigit)
        {
            switch (tag)
            {
                case "ManipulationLimbCore": return 70;
                case "ManipulationLimbSegment": return 55;
                case "ManipulationLimbDigit": return isFirstDigit ? 20 : 5;
                case "MovingLimbCore": return 70;
                case "MovingLimbSegment": return 40;
                case "MovingLimbDigit": return isLargestDigit ? 5 : 2;
                default: return 0;
            }
        }

        /// <summary>Without a matching tag: coverage x 100, at most 10 (B.3).</summary>
        public static double CoverageFallback(double coverage) => Math.Min(10.0, Math.Max(0.0, coverage) * 100.0);

        /// <summary>Spine: 100 when movement is gone (B.2); brain: 100 at consciousness <= 30 % (linear in between).</summary>
        public static double SpineValue(double movingCapacity) => 100.0 * Math.Max(0.0, Math.Min(1.0, 1.0 - movingCapacity));
        public static double BrainValue(double consciousness) => consciousness <= 0.3 ? 100.0 : 100.0 * Math.Max(0.0, (1.0 - consciousness) / 0.7);
    }
}
