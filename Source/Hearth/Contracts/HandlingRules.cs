// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Which handling modes a guest can be in (F04, 1.1.0). Basics, Accompany and Recruit always exist; Convert only with the Ideology DLC and only while
    /// the guest follows another ideoligion than the colony's primary one. Everything that cycles or lists the modes (guest gizmo, guests table, guest tab)
    /// goes through here, so a fourth mode can never be missed at one of the places.
    /// </summary>
    public static class HandlingRules
    {
        /// <summary>The ideoligion the colony tries to convert guests to (null without Ideology or without a colony ideoligion).</summary>
        public static object ColonyIdeo
        {
            get
            {
                if (!ModsConfig.IdeologyActive) return null;
                try { return Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo; } catch (Exception) { return null; }
            }
        }

        /// <summary>Can this guest be talked towards the colony's ideoligion right now?</summary>
        public static bool ConvertAvailable(Pawn guest)
        {
            if (!ModsConfig.IdeologyActive || guest == null || guest.ideo == null || guest.RaceProps == null || !guest.RaceProps.Humanlike) return false;
            try
            {
                var primary = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
                var theirs = guest.ideo.Ideo;
                if (primary == null || theirs == null || theirs == primary) return false;
                if (guest.IsColonist || guest.IsPrisoner || guest.IsSlave || guest.Dead) return false;
                if (guest.DevelopmentalStage.Baby()) return false;
                if (guest.Faction != null && guest.Faction.def != null && guest.Faction.def.fixedIdeo) return false; // their culture is fixed: nothing to talk about
                return true;
            }
            catch (Exception e) { HLog.Once("handling.convert", LogLevel.Warn, "Inn", "Convert check failed: " + e.Message); return false; }
        }

        public static bool Available(Pawn guest, HandlingMode m) => m != HandlingMode.Convert || ConvertAvailable(guest);

        /// <summary>The modes to offer for this guest, in cycling order.</summary>
        public static List<HandlingMode> All(Pawn guest)
        {
            var res = new List<HandlingMode> { HandlingMode.Basics, HandlingMode.Accompany, HandlingMode.Recruit };
            if (ConvertAvailable(guest)) res.Add(HandlingMode.Convert);
            return res;
        }

        /// <summary>The mode after <paramref name="cur"/> (Basics → Accompany → Recruit → Convert → Basics), skipping modes this guest cannot be in.</summary>
        public static HandlingMode Next(Pawn guest, HandlingMode cur)
        {
            int count = Enum.GetValues(typeof(HandlingMode)).Length;
            for (int i = 1; i <= count; i++)
            {
                var m = (HandlingMode)(((int)cur + i) % count);
                if (Available(guest, m)) return m;
            }
            return HandlingMode.Basics;
        }

        /// <summary>The cycling order as text for tooltips ("Basics → Accompany → Recruit → Convert").</summary>
        public static string CycleText(Pawn guest)
        {
            var l = All(guest); var s = new List<string>();
            foreach (var m in l) s.Add(("HH.UI.Handling." + m).TranslateOr(m.ToString()));
            return string.Join(" → ", s);
        }
    }
}
