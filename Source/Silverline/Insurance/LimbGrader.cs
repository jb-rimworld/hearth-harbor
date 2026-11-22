// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>Applies the Schedule of Limbs (Anhang B.2/B.3) to a real RimWorld body.</summary>
    public static class LimbGrader
    {
        public static string LabelOf(BodyPartRecord part) => (part.customLabel.NullOrEmpty() ? part.def.label : part.customLabel) ?? part.def.defName;

        public static string KeyOf(BodyPartRecord part) => LimbSchedule.KeyFor(part.def.defName, part.customLabel.NullOrEmpty() ? part.def.label : part.customLabel);

        static bool HasTag(BodyPartRecord p, string tag)
        {
            var tags = p.def.tags; if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++) if (tags[i].defName == tag) return true;
            return false;
        }

        static IEnumerable<string> TagsOf(BodyPartRecord p) => (p.def.tags ?? new List<BodyPartTagDef>()).Select(t => t.defName);

        /// <summary>Value (percent of the basic sum) of this part for this body. lethal = handled under Death. entry = schedule line used (null = fallback).</summary>
        public static double ValueOf(Pawn pawn, BodyPartRecord part, out bool lethal, out LimbEntry entry, out string key)
        {
            lethal = false; entry = null; key = KeyOf(part);
            var body = pawn.RaceProps.body;
            if (LimbSchedule.TryGet(key, out var e))
            {
                entry = e;
                if (e.Lethal) { lethal = true; return 0; }
                if (key == "Brain" || key == "Spine") return 0; // handled by capacity
                if (e.SharedByTag && !e.Tag.NullOrEmpty())
                {
                    double total = LimbSchedule.TagTotal(e.Tag); if (total <= 0) total = e.Percent * 2;
                    int n = body.AllParts.Count(x => HasTag(x, e.Tag)); if (n <= 0) n = body.AllParts.Count(x => x.def == part.def);
                    return LimbSchedule.SharedValue(total, Math.Max(1, n));
                }
                if (e.LimbFamily)
                {
                    int n = body.AllParts.Count(x => x.def == part.def);
                    return e.Percent * LimbSchedule.LimbScale(n);
                }
                return e.Percent;
            }
            // foreign body part: tag table, then coverage
            bool first = false, largest = false;
            foreach (var tag in TagsOf(part))
            {
                double total = LimbSchedule.TagTotal(tag);
                if (total < 0) { lethal = true; return 0; }
                if (total > 0)
                {
                    int n = Math.Max(1, body.AllParts.Count(x => HasTag(x, tag)));
                    return LimbSchedule.SharedValue(total, n);
                }
                if (tag.EndsWith("Digit"))
                {
                    var siblings = body.AllParts.Where(x => x.parent == part.parent && HasTag(x, tag)).ToList();
                    first = siblings.Count > 0 && siblings[0] == part && tag.StartsWith("Manipulation");
                    largest = siblings.Count > 0 && siblings.OrderByDescending(x => x.coverageAbs).First() == part;
                }
                double v = LimbSchedule.LimbTagValue(tag, first, largest);
                if (v > 0)
                {
                    if (tag.EndsWith("Core"))
                    {
                        int n = body.AllParts.Count(x => HasTag(x, tag));
                        v *= LimbSchedule.LimbScale(n);
                    }
                    return v;
                }
            }
            return LimbSchedule.CoverageFallback(part.coverageAbs);
        }

        static bool IsAddedPartHost(Pawn pawn, BodyPartRecord part, out Hediff_AddedPart added)
        {
            added = null;
            var hs = pawn.health.hediffSet;
            for (int i = 0; i < hs.hediffs.Count; i++)
                if (hs.hediffs[i] is Hediff_AddedPart ap && ap.Part == part) { added = ap; return true; }
            return false;
        }

        /// <summary>
        /// Invalidity grade of the body as it is now (0..100): top-most missing parts, prosthetics (they only exist because a part was lost)
        /// and scars. bionicsMatter (Overbooked+, clause 21): a built-in part reduces the value by its efficiency.
        /// </summary>
        public static double StateGrade(Pawn pawn, bool countProsthetics, bool bionicsMatter, List<LostPart> detail, out bool partialOnly, out bool lethalFound)
        {
            partialOnly = false; lethalFound = false;
            if (pawn?.health?.hediffSet == null) return 0;
            var hs = pawn.health.hediffSet;
            var values = new List<double>();
            var counted = new HashSet<BodyPartRecord>();
            bool anyFull = false;

            foreach (var mp in hs.GetMissingPartsCommonAncestors())
            {
                var part = mp.Part; if (part == null) continue;
                double v = ValueOf(pawn, part, out bool lethal, out var entry, out var key);
                if (lethal) { lethalFound = true; continue; }
                counted.Add(part);
                if (v <= 0) continue;
                values.Add(v); anyFull = true;
                detail?.Add(new LostPart { label = LabelOf(part), key = key, comment = entry?.Comment ?? "", value = v, partIndex = pawn.RaceProps.body.GetIndexOfPart(part) });
            }
            if (countProsthetics)
            {
                foreach (var h in hs.hediffs)
                {
                    if (!(h is Hediff_AddedPart ap) || ap.Part == null) continue;
                    var part = ap.Part;
                    if (counted.Contains(part) || hs.PartIsMissing(part)) continue;
                    if (part.parent != null && AncestorCounted(part, counted)) continue;
                    double v = ValueOf(pawn, part, out bool lethal, out var entry, out var key);
                    if (lethal || v <= 0) continue;
                    if (bionicsMatter)
                    {
                        float eff = ap.def.addedPartProps?.partEfficiency ?? 1f;
                        v *= Math.Max(0.0, 1.0 - eff);
                    }
                    counted.Add(part);
                    if (v <= 0) continue;
                    values.Add(v); anyFull = true;
                    detail?.Add(new LostPart { label = LabelOf(part), key = key, comment = entry?.Comment ?? "", value = v, partIndex = pawn.RaceProps.body.GetIndexOfPart(part) });
                }
            }
            // scars: permanent injuries on parts that are still there
            var byPart = new Dictionary<BodyPartRecord, bool>();
            foreach (var h in hs.hediffs)
            {
                if (!(h is Hediff_Injury) || h.Part == null || !h.IsPermanent()) continue;
                if (counted.Contains(h.Part) || AncestorCounted(h.Part, counted) || hs.PartIsMissing(h.Part)) continue;
                byPart[h.Part] = true;
            }
            bool partialAny = false;
            foreach (var part in byPart.Keys)
            {
                string key = KeyOf(part);
                if (key == "Brain")
                {
                    double v = LimbSchedule.BrainValue(pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness));
                    if (v > 0) { values.Add(v); partialAny = true; detail?.Add(new LostPart { label = LabelOf(part), key = key, comment = Comment(key), value = v }); }
                    continue;
                }
                if (key == "Spine")
                {
                    double v = LimbSchedule.SpineValue(pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving));
                    if (v > 0) { values.Add(v); partialAny = true; detail?.Add(new LostPart { label = LabelOf(part), key = key, comment = Comment(key), value = v }); }
                    continue;
                }
                double pv = ValueOf(pawn, part, out bool lethal, out var entry, out _);
                if (lethal || pv <= 0) continue;
                float efficiency = PawnCapacityUtility.CalculatePartEfficiency(hs, part);
                double loss = InsuranceMath.PartialLoss(pv, efficiency);
                if (loss < 0.5) continue;
                values.Add(loss); partialAny = true;
                detail?.Add(new LostPart { label = LabelOf(part) + " (scarred)", key = key, comment = entry?.Comment ?? "", value = loss, partIndex = pawn.RaceProps.body.GetIndexOfPart(part) });
            }
            partialOnly = partialAny && !anyFull;
            return InsuranceMath.CombineGrades(values);
        }

        public static double StateGrade(Pawn pawn, bool countProsthetics, bool bionicsMatter)
            => StateGrade(pawn, countProsthetics, bionicsMatter, null, out _, out _);

        static bool AncestorCounted(BodyPartRecord part, HashSet<BodyPartRecord> counted)
        {
            for (var p = part.parent; p != null; p = p.parent) if (counted.Contains(p)) return true;
            return false;
        }

        static string Comment(string key) => LimbSchedule.TryGet(key, out var e) ? e.Comment : "";

        /// <summary>Does an old ailment sit on a part that was lost now (B.3 "Mitwirkung")? Bad back / spine, cataract / eye, hearing loss / ear.</summary>
        public static bool Contributed(Pawn pawn, IEnumerable<LostPart> lost)
        {
            if (pawn?.health?.hediffSet == null) return false;
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                string d = h.def.defName;
                if (d == "BadBack" || d == "Cataract" || d == "HearingLoss")
                {
                    string want = d == "BadBack" ? "Spine" : d == "Cataract" ? "Eye" : "Ear";
                    if (lost.Any(l => l.key == want)) return true;
                }
            }
            return false;
        }
    }
}
