// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Hearth.Guests;
using Hearth.Shell;

namespace Hearth.Ai
{
    /// <summary>
    /// The concierge question box on the Overview page (F05). The game works out what is wrong with the guests deterministically (GuestOverviewPage.Problems, the same lines
    /// as the "why unhappy" list). Without AI the answer is that list. With AI the facts (renown, guest and bed counts, the problems with their counts) go to the relay and the
    /// answer is a short, friendly tip phrased by the model. If the relay fails the list is shown, so the box always answers.
    /// </summary>
    public static class AiConcierge
    {
        public const int QuestionMax = 300;
        public static string Question = "", Answer;
        public static bool Pending, AnswerByAi, AnswerFallback;
        static AiTicket ticket;
        public static int Asked;

        public static void Reset() { Question = ""; Answer = null; Pending = AnswerByAi = AnswerFallback = false; ticket?.Cancel(); ticket = null; Asked = 0; }

        /// <summary>The problems of the staying guests with how many guests have each: (key, count, line for one guest, line for many with {0}).</summary>
        public static List<(string key, int n, string one, string many)> Problems(Map map)
        {
            var res = new List<(string key, int n, string one, string many)>();
            var reg = GuestRegistry.Instance; if (reg == null || map == null) return res;
            var counts = new Dictionary<string, (int n, string one, string many)>();
            foreach (var r in reg.OnMap(map))
            {
                if (r.Settled) continue;
                foreach (var p in GuestOverviewPage.Problems(r))
                {
                    counts.TryGetValue(p.key, out var c);
                    counts[p.key] = (c.n + 1, p.line, p.many);
                }
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value.n)) res.Add((kv.Key, kv.Value.n, kv.Value.one, kv.Value.many));
            return res;
        }

        /// <summary>Facts for the relay (at most 12 lines of at most 120 characters).</summary>
        public static List<object> Facts(Map map)
        {
            var facts = new List<object>();
            var reg = GuestRegistry.Instance;
            if (reg != null)
            {
                facts.Add("Renown " + reg.Renown.ToString("0") + " of 100 (" + GuestRegistry.Stars + " of 5 stars)");
                int staying = map == null ? 0 : reg.OnMap(map).Count(r => !r.Settled);
                facts.Add(staying + " guest(s) staying now");
                if (map != null) facts.Add(GuestBeds.CountOn(map) + " guest beds, " + GuestBeds.FreeOn(map).Count + " free");
            }
            var probs = Problems(map);
            if (probs.Count == 0) facts.Add("No guest has an open problem right now");
            foreach (var p in probs.Take(8)) facts.Add(AiSchema.Clean(p.n == 1 ? p.one : p.many.Replace("{0}", p.n.ToString()), 120));
            try { if (map != null) facts.Add(Venues.All(map).Count + " venue(s) in the colony"); } catch (Exception) { }
            return facts.Take(12).ToList();
        }

        /// <summary>The offline answer: the game's own tips, most common problem first.</summary>
        public static string Tips(Map map)
        {
            var probs = Problems(map);
            if (probs.Count == 0) return "HH.Inn.Concierge.AllClear".TranslateOr("all clear, the guests are content.").CapitalizeFirst();
            var lines = probs.Take(4).Select(p => "• " + (p.n == 1 ? p.one : p.many.Replace("{0}", p.n.ToString())));
            return AiT.T("Concierge.Tips", "Here is what I see, most common first:") + "\n" + string.Join("\n", lines);
        }

        public static void Ask(Map map, string question)
        {
            question = AiSchema.Clean(question, QuestionMax, true);
            if (question.Length == 0) question = AiT.T("Concierge.Default", "What should I improve first?");
            Asked++; AnswerByAi = false; AnswerFallback = false; Answer = null;
            ticket?.Cancel(); ticket = null;
            if (!AiService.Active(AiFn.Concierge)) { Answer = Tips(map); return; }
            Pending = true;
            var fields = new Dictionary<string, object> { { "question", question }, { "facts", Facts(map) } };
            ticket = AiService.Send(AiFn.Concierge, fields, res =>
            {
                Pending = false; ticket = null;
                string text = res.Ok ? AiText.Reply(res.Text, 600) : "";
                if (text.Length > 0) { Answer = text; AnswerByAi = true; }
                else if (res.Error != "cancelled") { Answer = Tips(map); AnswerFallback = true; AiService.NoteFallback(); }
            });
            if (ticket == null) { Pending = false; Answer = Tips(map); AnswerFallback = true; AiService.NoteFallback(); }
        }

        // ------------------------------------------------------------------------------------------ UI
        public static void DrawSection(Listing_Standard ls)
        {
            if (!Modules.On(ModuleId.AiFlavour) || !Modules.On(ModuleId.Inn)) return;
            var map = Find.CurrentMap; if (map == null) return;
            AiService.Touch();
            float w = ls.ColumnWidth;
            var head = ls.GetRect(30f);
            UiStyle.Section(head.x, head.y, head.width, AiT.T("Concierge.Title", "Ask the concierge"));
            string state = AiService.Active(AiFn.Concierge) ? AiT.T("Concierge.StateAi", "AI online: answers are phrased by the AI") : AiT.T("Concierge.StateOffline", "Answers come from the game's own tips");
            UiStyle.LabelColored(new Rect(head.xMax - 360f, head.y + 2f, 360f, 22f), state, AiService.Active(AiFn.Concierge) ? UiStyle.Good : UiStyle.DimGrey, TextAnchor.MiddleRight, GameFont.Tiny);
            var box = ls.GetRect(56f);
            string q = Widgets.TextArea(new Rect(box.x, box.y, box.width - 150f, box.height), Question ?? "");
            if (q.Length > QuestionMax) q = q.Substring(0, QuestionMax);
            Question = q;
            if (Question.Length == 0) UiStyle.LabelColored(new Rect(box.x + 6f, box.y + 4f, box.width - 170f, 22f), AiT.T("Concierge.Placeholder", "Why are no nobles coming? What should I improve?"), UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny);
            var bt = new Rect(box.xMax - 140f, box.y, 140f, 26f);
            if (Pending)
            {
                if (Widgets.ButtonText(bt, AiT.T("Concierge.Stop", "Stop"))) { ticket?.Cancel(); Pending = false; Answer = Tips(map); AnswerFallback = true; }
            }
            else if (Widgets.ButtonText(bt, AiT.T("Concierge.Ask", "Ask"))) Ask(map, Question);
            UiStyle.LabelColored(new Rect(box.xMax - 140f, box.y + 30f, 140f, 20f), Question.Length + " / " + QuestionMax, UiStyle.DimGrey, TextAnchor.MiddleRight, GameFont.Tiny);
            if (Pending)
            {
                var d = ls.GetRect(24f);
                UiStyle.LabelColored(d, AiT.T("Concierge.Thinking", "The concierge is thinking") + new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3), UiStyle.Amber, TextAnchor.MiddleLeft);
            }
            else if (!Answer.NullOrEmpty())
            {
                float h = Text.CalcHeight(Answer, w - 8f) + 6f;
                var ar = ls.GetRect(h);
                Widgets.DrawBoxSolid(ar, new Color(1f, 1f, 1f, 0.04f));
                Widgets.Label(new Rect(ar.x + 4f, ar.y + 3f, ar.width - 8f, h), Answer);
                if (AnswerByAi && AiReviews.ShowBadge) { var br = ls.GetRect(18f); UiStyle.LabelColored(br, AiT.T("Badge.Ai", "AI") + " · " + AiT.T("Badge.TipConcierge", "written by the AI from the game's own facts"), UiStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny); }
                else if (AnswerFallback) { var br = ls.GetRect(18f); UiStyle.LabelColored(br, AiT.T("Concierge.Offline", "The AI did not answer; these are the game's own tips."), UiStyle.DimGrey, TextAnchor.MiddleLeft, GameFont.Tiny); }
            }
            ls.Gap(4f);
        }
    }
}
