// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Guests;

namespace Hearth.Ai
{
    /// <summary>
    /// Guest reviews (F05). When a group has left and its departure report was sent, a share of the groups (setting ai.reviewShare) also leaves a short review in the voice of one
    /// of its guests, as a letter. With AI the text comes from the relay (the stars, the reasons and the kinds of venue the guest spent money in are sent, no stay details);
    /// without AI, or when the answer takes more than 25 seconds, a stock line of the same mood is used. The stars always come from the game's own result.
    /// </summary>
    public static class AiReviews
    {
        public sealed class Review { public string Name, Text, Faction; public int Stars, Tick; public bool Ai; }
        public static readonly List<Review> Recent = new List<Review>();
        public const int MaxRecent = 12;
        public static int Delivered, DeliveredByAi;

        static int session;
        public static void Reset() { Recent.Clear(); Delivered = DeliveredByAi = 0; session++; }

        /// <summary>Called after the departure report of a group was sent.</summary>
        public static void OnDeparture(GuestGroup g, Map map)
        {
            if (g == null || !Modules.On(ModuleId.AiFlavour) || !AiFnOn()) return;
            float share = HSettings.Find("ai.reviewShare") != null ? HSettings.Get("ai.reviewShare") : 0.25f;
            if (share <= 0f || !JRand.Stream("ai.review").Chance(share)) return;
            string outcome = DepartureReport.OutcomeFromSurplus(g.MeanSurplus);
            if (g.LeaveReason == "sentAway") outcome = "fine";
            int stars = outcome == "great" ? 5 : outcome == "good" ? 4 : outcome == "fine" ? 3 : outcome == "poor" ? 2 : 1;
            var reg = GuestRegistry.Instance;
            var members = reg != null ? reg.Members(g).ToList() : new List<GuestRecord>();
            string name = g.Names.Count > 0 ? g.Names[Rand.Range(0, g.Names.Count)] : members.Select(m => m.Pawn?.LabelShort).FirstOrDefault(n => !n.NullOrEmpty()) ?? "Guest";
            var reasons = new List<string> { outcome };
            if (!g.LeaveReason.NullOrEmpty()) reasons.Add(g.LeaveReason);
            if (members.Any(m => m.Returner)) reasons.Add("returning_guest");
            if (members.Any(m => m.Outcome == "joined")) reasons.Add("joined_colony");
            var venues = new List<string>();
            foreach (var m in members) foreach (var k in m.SpentBy.Keys) if (!venues.Contains(k) && venues.Count < 8) venues.Add(k);
            Submit(name, stars, reasons, venues, g.Faction?.Name, g.Id);
        }

        static bool AiFnOn() => HSettings.Find("ai.func.review") == null || HSettings.GetBool("ai.func.review");

        /// <summary>Writes a review: asks the relay when it is available, otherwise (or on any failure) uses a stock line. The letter appears when the text is there.</summary>
        public static void Submit(string name, int stars, List<string> reasons, List<string> venueKinds, string faction, int seed)
        {
            stars = Math.Max(1, Math.Min(5, stars));
            if (AiService.Active(AiFn.Review))
            {
                var fields = new Dictionary<string, object> { { "guest", name }, { "stars", stars }, { "reasons", reasons ?? new List<string>() }, { "venue_kinds", venueKinds ?? new List<string>() } };
                int mySession = session;
                var ticket = AiService.Send(AiFn.Review, fields, res =>
                {
                    // cancelled (game changed, module or consent switched off) or a callback from an older game: nothing is delivered, no letter in the wrong game
                    if (res.Error == "cancelled" || mySession != session || !AiService.Enabled) return;
                    string text = res.Ok ? AiText.Reply(res.Text, 320) : "";
                    if (text.Length > 0) Deliver(name, faction, stars, text, true);
                    else { AiService.NoteFallback(); Deliver(name, faction, stars, Offline(stars, seed), false); }
                }, 25);
                if (ticket != null) return;
                AiService.NoteFallback();
            }
            Deliver(name, faction, stars, Offline(stars, seed), false);
        }

        public static string Offline(int stars, int seed)
        {
            string en = AiOffline.ReviewLine(stars, AiText.StableHash(seed.ToString()), out int idx);
            return ("HH.Ai.Review.Fallback." + stars + "." + idx).TranslateOr(en);
        }

        static void Deliver(string name, string faction, int stars, string text, bool ai)
        {
            try
            {
                var r = new Review { Name = name, Faction = faction, Stars = stars, Text = text, Ai = ai, Tick = Find.TickManager?.TicksGame ?? 0 };
                Recent.Insert(0, r); if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
                Delivered++; if (ai) DeliveredByAi++;
                string body = AiOffline.Stars(stars) + "\n\n“" + text + "”\n\n— " + name + (faction.NullOrEmpty() ? "" : ", " + faction);
                if (ai && ShowBadge) body += "\n\n" + AiT.T("Badge.Written", "Written by the AI. The stars come from the game.");
                var def = stars >= 4 ? LetterDefOf.PositiveEvent : stars == 3 ? LetterDefOf.NeutralEvent : LetterDefOf.NegativeEvent;
                Find.LetterStack.ReceiveLetter(AiT.T("Review.Label", "Guest review: {0}", name), body, def);
            }
            catch (Exception e) { HLog.Warn("AI", "Could not deliver a review: " + e.GetType().Name + ": " + e.Message); }
        }

        public static bool ShowBadge => HSettings.Find("ai.showBadge") == null || HSettings.GetBool("ai.showBadge");
    }

    [HarmonyPatch(typeof(DepartureReport), nameof(DepartureReport.Send))]
    static class Patch_DepartureReport_Review
    {
        static void Postfix(GuestGroup g, Map map)
        {
            if (!Modules.On(ModuleId.AiFlavour)) return;
            Guard.Run(ModuleId.AiFlavour, "Ai.ReviewOnDeparture", () => AiReviews.OnDeparture(g, map));
        }
    }
}
