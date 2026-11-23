// SPDX-License-Identifier: MIT
// F04 (1.1.0): the "Convert" handling mode. Hosting colonists keep the guest company like in Accompany and every talk shakes the guest's certainty
// in their own ideoligion; vanilla decides when the guest takes on the colony's (Pawn_IdeoTracker.IdeoConversionAttempt). Needs the Ideology DLC.
using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    public sealed partial class GuestRegistry
    {
        /// <summary>How hard one talk pushes: vanilla's own formula for a conversation attempt (social skill, opinion, traits) times the setting; a fallback when vanilla's is not there.</summary>
        static float ConvertReduction(Pawn host, Pawn guest, float social)
        {
            float v;
            try { v = InteractionWorker_ConvertIdeoAttempt.CertaintyReduction(host, guest); }
            catch (Exception e) { HLog.Once("inn.convertReduction", LogLevel.Warn, "Inn", "Vanilla certainty formula not available, using ours: " + e.Message); v = Mathf.Lerp(0.04f, 0.16f, Mathf.Clamp01(social / 20f)); }
            return Math.Max(0f, v) * HSettings.Get(InnSettings.ConvertPower);
        }

        /// <summary>One conversation of a Hosting colonist with a guest in Convert handling (called from Notify_Talk after the insult roll).</summary>
        void ConvertTalk(Pawn host, Pawn guest, GuestRecord r, float social)
        {
            try
            {
                var primary = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
                if (primary == null || guest.ideo == null) return;
                var theirs = guest.ideo.Ideo;
                float before = guest.ideo.Certainty;
                if (r.ConvertTalks == 0) r.ConvertStartCertainty = before;
                r.ConvertTalks++;
                float reduction = ConvertReduction(host, guest, social);
                bool flipped = ConvertAttemptOverride ?? guest.ideo.IdeoConversionAttempt(reduction, primary, true);
                if (flipped || guest.ideo.Ideo == primary)
                {
                    r.Handling = HandlingMode.Accompany;
                    r.Log("HH.Inn.Event.Embraced".TranslateOr("Embraced {0} after talks with {1}.").Replace("{0}", primary.name).Replace("{1}", host.LabelShort), 6);
                    AddSatisfaction(guest, 0.05f, "HH.Inn.Event.EmbracedSat".TranslateOr("Found a new faith in the colony."));
                    host.needs?.mood?.thoughts?.memories?.TryGainMemory(InnDefOf.HH_HostedWell, guest);
                    HLog.Info("Inn", $"{guest.LabelShort} converted from {theirs?.name} to {primary.name} (talk {r.ConvertTalks}, host {host.LabelShort})");
                    return;
                }
                float after = guest.ideo.Certainty;
                if (Math.Abs(before - after) < 0.0005f)
                    r.Log("HH.Inn.Event.Unmoved".TranslateOr("Listened to {0} about {1}, but stayed unmoved.").Replace("{0}", host.LabelShort).Replace("{1}", primary.name), 3);
                else
                    r.Log("HH.Inn.Event.ThinkingOver".TranslateOr("Talked with {0} about {1}: now {2} % sure of their own beliefs.").Replace("{0}", host.LabelShort).Replace("{1}", primary.name).Replace("{2}", (after * 100f).ToString("0")), 3);
            }
            catch (Exception e) { HLog.Error("Inn", "Conversion talk failed", e); }
        }

        /// <summary>"Certainty in X: 72 % · talks so far: 3" for the tooltip and the guest tab; null when the guest cannot be converted.</summary>
        public static string ConvertLine(Pawn guest)
        {
            if (!HandlingRules.ConvertAvailable(guest)) return null;
            var primary = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
            var r = Instance?.Get(guest);
            string line = "HH.Inn.Convert.Certainty".TranslateOr("Certainty in {0}: {1} %").Replace("{0}", guest.ideo.Ideo.name).Replace("{1}", (guest.ideo.Certainty * 100f).ToString("0"));
            if (primary != null) line += " · " + "HH.Inn.Convert.Goal".TranslateOr("at 0 % they take on {0}").Replace("{0}", primary.name);
            if (r != null && r.ConvertTalks > 0) line += " · " + "HH.Inn.Convert.Talks".TranslateOr("talks so far: {0}").Replace("{0}", r.ConvertTalks.ToString());
            return line;
        }

        /// <summary>Tests only: decides the outcome of the vanilla conversion attempt (null = ask vanilla).</summary>
        internal static bool? ConvertAttemptOverride;
    }
}
