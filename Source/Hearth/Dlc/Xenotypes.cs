// SPDX-License-Identifier: MIT
// KONZEPT 11.5: xenotype hospitality as data. XenotypeHospitalityDef entries (XML, modders add their own) describe what a xenotype
// or gene group likes in a room and on a menu; XenotypeHospitality answers for a pawn and nudges guest satisfaction.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth
{
    public class XenoInterest { public ThingDef thing; public float weight = 1f; }

    /// <summary>
    /// Preferences of guests with a given xenotype or gene set. Room preferences run from -1 to +1: lightPref (+ bright), tempPref (+ warm),
    /// filthPref (+ likes dirt), indoorPref (+ likes enclosed rooms). The most specific entry for a pawn wins.
    /// </summary>
    public class XenotypeHospitalityDef : Def
    {
        public List<XenotypeDef> xenotypes = new List<XenotypeDef>();
        public List<GeneDef> anyGenes = new List<GeneDef>();
        public List<GeneDef> allGenes = new List<GeneDef>();
        public int priority;
        public float lightPref, tempPref, filthPref, indoorPref;
        public float priceTolerance;      // added to price tolerance (negative: stingier)
        public float expectationOffset;   // added to the guest's expectation
        public float spendFactor = 1f;
        public List<XenoInterest> interests = new List<XenoInterest>();
        public List<ThingDef> rejects = new List<ThingDef>();
        public List<ThoughtDef> thoughts = new List<ThoughtDef>();
        [NoTranslate] public List<string> eventIds = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (xenotypes.NullOrEmpty() && anyGenes.NullOrEmpty() && allGenes.NullOrEmpty()) yield return "matches nothing: set xenotypes, anyGenes or allGenes";
        }

        /// <summary>Higher = more specific. 0 = does not match.</summary>
        public int Specificity(Pawn p)
        {
            var g = p?.genes; if (g == null) return 0;
            int score = 0; bool matched = false;
            if (!xenotypes.NullOrEmpty())
            {
                var xt = g.Xenotype;
                if (xt != null && xenotypes.Contains(xt)) { score += 100; matched = true; } else if (anyGenes.NullOrEmpty() && allGenes.NullOrEmpty()) return 0;
            }
            if (!allGenes.NullOrEmpty())
            {
                if (allGenes.All(x => g.HasActiveGene(x))) { score += 20 * allGenes.Count; matched = true; } else return 0;
            }
            if (!anyGenes.NullOrEmpty())
            {
                if (anyGenes.Any(x => g.HasActiveGene(x))) { score += 10; matched = true; } else if (xenotypes.NullOrEmpty()) return 0;
            }
            return matched ? score + priority : 0;
        }
    }
}

namespace Hearth.Dlc
{
    public static class XenotypeHospitality
    {
        static List<XenotypeHospitalityDef> all;
        static List<XenotypeHospitalityDef> All => all ?? (all = DefDatabase<XenotypeHospitalityDef>.AllDefsListForReading.ToList());

        /// <summary>Best matching entry for a pawn, or null (no Biotech, no genes, nothing matches).</summary>
        public static XenotypeHospitalityDef For(Pawn p)
        {
            if (!ModsConfig.BiotechActive || p?.genes == null) return null;
            XenotypeHospitalityDef best = null; int bs = 0;
            foreach (var d in All) { int s = d.Specificity(p); if (s > bs) { bs = s; best = d; } }
            return best;
        }

        public static float Clamp01s(float v) => Mathf.Clamp(v, -1f, 1f);

        /// <summary>Components of a room's fit for an entry, each -1..+1 already multiplied by the preference.</summary>
        public static float RoomScore(XenotypeHospitalityDef d, Room room, Map map)
        {
            if (d == null || room == null || map == null) return 0f;
            float score = 0f;
            if (Mathf.Abs(d.lightPref) > 0.001f)
            {
                float glow = 0f; int n = 0;
                foreach (var c in room.Cells) { glow += map.glowGrid.GroundGlowAt(c, true, false); if (++n >= 16) break; }
                glow = n == 0 ? 0.5f : glow / n;
                score += d.lightPref * Clamp01s((glow - 0.5f) * 2f);
            }
            if (Mathf.Abs(d.tempPref) > 0.001f) score += d.tempPref * Clamp01s((room.Temperature - 21f) / 15f);
            if (Mathf.Abs(d.filthPref) > 0.001f) score += d.filthPref * Clamp01s(-room.GetStat(RoomStatDefOf.Cleanliness) / 2f);
            if (Mathf.Abs(d.indoorPref) > 0.001f) score += d.indoorPref * (room.PsychologicallyOutdoors ? -1f : 1f);
            return Clamp01s(score * 0.5f);
        }

        public static float InterestWeight(Pawn p, ThingDef thing)
        {
            var d = For(p); if (d == null || thing == null) return 1f;
            if (d.rejects.Contains(thing)) return 0f;
            var i = d.interests.FirstOrDefault(x => x.thing == thing);
            return i == null ? 1f : i.weight;
        }

        public static float SpendFactor(Pawn p) => HSettings.GetBool("dlc.xenoHospitality") ? For(p)?.spendFactor ?? 1f : 1f;
        public static float PriceToleranceOffset(Pawn p) => HSettings.GetBool("dlc.xenoHospitality") ? For(p)?.priceTolerance ?? 0f : 0f;

        /// <summary>Every 6 hours a guest feels how well their room fits their kind: a small satisfaction nudge (never spam).</summary>
        public static int NudgeGuests(Map map)
        {
            if (!ModsConfig.BiotechActive || !HSettings.GetBool("dlc.xenoHospitality") || !Modules.On(ModuleId.Inn)) return 0;
            int n = 0;
            foreach (var g in GuestApi.GuestsOn(map).ToList())
            {
                var d = For(g); if (d == null || !g.Spawned) continue;
                var room = g.GetRoom(); if (room == null) continue;
                float v = RoomScore(d, room, map) * 0.1f;
                if (Mathf.Abs(v) < 0.02f) continue;
                GuestApi.AddSatisfaction(g, v, v > 0 ? "Room suits my kind" : "Room does not suit my kind"); n++;
            }
            return n;
        }
    }
}
