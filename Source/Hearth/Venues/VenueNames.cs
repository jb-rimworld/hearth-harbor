// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// Business names (F33, 1.1.0): every venue type may name a RulePackDef (<see cref="VenueTypeDef.nameRules"/>) with an <c>r_name</c> rule.
    /// The name of a sign is deterministic (sign id + roll number), avoids names already used on the map and can be rolled again.
    /// Word lists are XML data (translatable with DefInjected). The token [owner] is the surname of a colonist of the colony.
    /// </summary>
    public static class VenueNames
    {
        /// <summary>Same limit as the rename window's text field.</summary>
        public const int MaxLength = 40;

        public static bool HasGenerator(VenueTypeDef t) => t?.nameRules != null;

        /// <summary>A name for a sign of this type, stable for the same sign and roll. Null when the type has no name rules.</summary>
        public static string For(VenueTypeDef t, Thing sign, int roll)
        {
            if (t == null || sign == null) return null;
            return Generate(t, sign.MapHeld, sign.thingIDNumber * 31 + roll * 7 + 1, sign);
        }

        /// <summary>Resolves the type's grammar with a fixed seed; tries a few times to avoid names already used on the map.</summary>
        public static string Generate(VenueTypeDef t, Map map, int seed, Thing except = null)
        {
            if (t?.nameRules == null) return null;
            string first = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string s = Resolve(t, map, unchecked(seed * 7919 + attempt * 104729));
                if (s.NullOrEmpty()) continue;
                if (s.Length > MaxLength) continue; // the rename field holds 40 characters; long translated variants are skipped
                first = first ?? s;
                if (!Taken(map, s, except)) return s;
            }
            if (first == null) return null;
            // every variant is taken: number it
            for (int n = 2; n < 20; n++) { string s = first + " " + Roman(n); if (!Taken(map, s, except)) return s; }
            return first;
        }

        static string Resolve(VenueTypeDef t, Map map, int seed)
        {
            Rand.PushState(seed);
            try
            {
                var req = new GrammarRequest();
                req.Includes.Add(t.nameRules);
                req.Rules.Add(new Rule_String("owner", OwnerName(map, seed)));
                string s = GrammarResolver.Resolve("r_name", req, "venue name " + t.defName, false, null, null, null, true);
                return s?.Trim();
            }
            catch (Exception e)
            {
                HLog.Once("venues.names." + t.defName, LogLevel.Warn, "Venues", "Name rules of " + t.defName + " failed: " + e.Message);
                return null;
            }
            finally { Rand.PopState(); }
        }

        /// <summary>A surname from the colony (a free colonist, chosen by the seed), or a neutral word when there is nobody.</summary>
        static string OwnerName(Map map, int seed)
        {
            var names = new List<string>();
            if (map != null)
                foreach (var p in map.mapPawns.FreeColonists)
                    if (p.Name is NameTriple n && !n.Last.NullOrEmpty() && !names.Contains(n.Last)) names.Add(n.Last);
            if (names.Count == 0) return "HH.Names.DefaultOwner".TranslateOr("Harbor");
            names.Sort(StringComparer.Ordinal);
            return names[(int)((uint)seed % (uint)names.Count)];
        }

        static bool Taken(Map map, string name, Thing except)
        {
            if (map == null) return false;
            foreach (var v in Venues.All(map))
                if (v.Sign != except && string.Equals(v.Comp?.customName, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Roman(int n)
        {
            string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n < r.Length ? r[n] : n.ToString();
        }
    }
}
