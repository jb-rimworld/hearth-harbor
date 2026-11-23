// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Silverline;
using UnityEngine;
using Verse;

namespace Hearth.Ai
{
    /// <summary>The "Ask Ottoline" tab of the bank window: a chat in the style of the lobby (walnut, brass, green). Only ADDS a tab through BankTabs; the other tabs are not touched.</summary>
    public static class AiChatTab
    {
        static Vector2 scroll;
        static int lastCount = -1;

        public static void Draw(Rect r)
        {
            AiService.Touch();
            var f = Text.Font; var a = Text.Anchor; var wrap = Text.WordWrap;
            try { DrawInner(r); }
            finally { Text.Font = f; Text.Anchor = a; Text.WordWrap = wrap; GUI.color = Color.white; }
        }

        static void DrawInner(Rect r)
        {
            // status strip -------------------------------------------------------------------------------
            var strip = new Rect(r.x, r.y, r.width, 30f);
            DrawStatus(strip);

            // input block (bottom) -----------------------------------------------------------------------
            float inputH = 74f, faqH = FaqRows(r.width) == 2 ? 58f : 34f;
            var input = new Rect(r.x, r.yMax - inputH, r.width, inputH);
            var faq = new Rect(r.x, input.y - faqH - 4f, r.width, faqH);
            var chat = new Rect(r.x, strip.yMax + 4f, r.width, faq.y - strip.yMax - 8f);
            DrawHistory(chat);
            DrawFaq(faq);
            DrawInput(input);
        }

        static void DrawStatus(Rect r)
        {
            var st = AiService.State;
            Color c = st == AiState.Online ? BankUI.Good : st == AiState.Resting || st == AiState.Paused || st == AiState.Checking ? BankUI.Brass : BankUI.CreamDim;
            string label = AiService.StateLabel();
            Widgets.DrawBoxSolid(new Rect(r.x + 2f, r.y + 10f, 10f, 10f), c);
            BankUI.Label(new Rect(r.x + 18f, r.y + 2f, r.width * 0.55f, 26f), label, c, GameFont.Small, TextAnchor.MiddleLeft, false);
            TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width * 0.55f, r.height), AiService.StateDetail() + (AiService.LastError != null ? "\n" + AiT.T("Status.LastError", "Last error code: {0}", AiService.LastError) : ""));
            float x = r.xMax;
            if (!AiService.ConsentGiven || !AiService.ModuleOn)
            {
                x -= 200f;
                if (BankUI.Button(new Rect(x, r.y + 1f, 200f, 28f), AiT.T("Chat.SetUp", "Set up AI answers"))) Dialog_AiConsent.Open();
            }
            else if (st == AiState.Offline || st == AiState.Unavailable || st == AiState.Checking)
            {
                x -= 150f;
                if (BankUI.Button(new Rect(x, r.y + 1f, 150f, 28f), AiT.T("Chat.Recheck", "Check again"))) AiService.RefreshStatus();
            }
            Widgets.DrawLineHorizontal(r.x, r.yMax, r.width, BankUI.BrassDim);
        }

        static void DrawHistory(Rect r)
        {
            Widgets.DrawBoxSolid(r, BankUI.Walnut);
            GUI.color = BankUI.BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            var view = r.ContractedBy(4f);
            float w = view.width - 18f, bubbleW = Mathf.Min(w * 0.82f, 640f);
            var items = new List<(AiChat.Msg m, float h)>();
            var welcome = new AiChat.Msg { Text = AiT.T("Chat.Welcome", "Ask me about your account, loans, investments or policies. I explain; I never move your money. If the AI is away, the quick questions below still work.") };
            float total = 6f;
            Text.Font = GameFont.Small; Text.WordWrap = true;
            float Height(AiChat.Msg m) => Text.CalcHeight(m.Text, bubbleW - 20f) + 34f;
            { float h = Height(welcome); items.Add((welcome, h)); total += h + 8f; }
            foreach (var m in AiChat.History) { float h = Height(m); items.Add((m, h)); total += h + 8f; }
            if (AiChat.Pending) total += 40f;
            var inner = new Rect(0, 0, w, Mathf.Max(total, view.height - 2f));
            if (AiChat.ScrollToEnd || AiChat.History.Count != lastCount) { scroll.y = Mathf.Max(0f, inner.height - view.height); AiChat.ScrollToEnd = false; lastCount = AiChat.History.Count; }
            Widgets.BeginScrollView(view, ref scroll, inner);
            float y = 6f;
            foreach (var (m, h) in items)
            {
                float x = m.User ? w - bubbleW - 4f : 4f;
                var b = new Rect(x, y, bubbleW, h);
                Widgets.DrawBoxSolid(b, m.User ? BankUI.GreenLight : BankUI.WalnutLight);
                GUI.color = m.User ? BankUI.Brass : BankUI.BrassDim; Widgets.DrawBox(b, 1); GUI.color = Color.white;
                string who = m.User ? AiT.T("Chat.You", "You") : AiT.T("Chat.Clerk", "Clerk Ottoline");
                BankUI.Label(new Rect(b.x + 10f, b.y + 4f, b.width - 20f, 20f), who, m.User ? BankUI.Cream : BankUI.Brass, GameFont.Tiny, TextAnchor.UpperLeft, false);
                if (!m.User && m.Ai && AiReviews.ShowBadge)
                {
                    var badge = new Rect(b.xMax - 44f, b.y + 5f, 34f, 16f);
                    Widgets.DrawBoxSolid(badge, BankUI.Green); GUI.color = BankUI.Brass; Widgets.DrawBox(badge, 1); GUI.color = Color.white;
                    BankUI.Label(badge, AiT.T("Badge.Ai", "AI"), BankUI.Cream, GameFont.Tiny, TextAnchor.MiddleCenter, false);
                    TooltipHandler.TipRegion(badge, AiT.T("Badge.TipChat", "Written by the AI from a read-only snapshot of your books. It can explain, it cannot change anything."));
                }
                else if (!m.User && m.Offline)
                {
                    BankUI.Label(new Rect(b.xMax - 150f, b.y + 4f, 140f, 20f), AiT.T("Badge.Books", "from the books"), BankUI.CreamDim, GameFont.Tiny, TextAnchor.UpperRight, false);
                }
                BankUI.Label(new Rect(b.x + 10f, b.y + 24f, b.width - 20f, h - 28f), m.Text, BankUI.Cream);
                y += h + 8f;
            }
            if (AiChat.Pending)
            {
                string dots = new string('.', 1 + (int)(Time.realtimeSinceStartup * 2.5f) % 3);
                BankUI.Label(new Rect(8f, y + 4f, 300f, 28f), AiT.T("Chat.Thinking", "Ottoline is thinking") + dots, BankUI.Brass);
            }
            Widgets.EndScrollView();
        }

        /// <summary>One row of five quick-question buttons when the window is wide enough for the labels, otherwise two rows (3 + 2) so no label wraps or clips.</summary>
        static int FaqRows(float width) => (width - 124f - 16f) / 5f >= 150f ? 1 : 2;

        static void DrawFaq(Rect r)
        {
            BankUI.Label(new Rect(r.x, r.y, 120f, r.height), AiT.T("Chat.Quick", "Quick questions"), BankUI.CreamDim, GameFont.Tiny, TextAnchor.MiddleLeft, false);
            int rows = FaqRows(r.width), perRow = rows == 1 ? 5 : 3;
            float w = (r.width - 124f - 4f * (perRow - 1)) / perRow, h = (r.height - 4f - 4f * (rows - 1)) / rows;
            int n = 0;
            foreach (var key in AiChat.FaqKeys)
            {
                int row = n / perRow, col = n % perRow; n++;
                var b = new Rect(r.x + 124f + col * (w + 4f), r.y + 2f + row * (h + 4f), w, h);
                var fs = Text.Font; Text.Font = GameFont.Tiny;
                bool hit = Widgets.ButtonText(b, AiChat.FaqLabel(key));
                Text.Font = fs;
                if (hit) AiChat.Quick(key);
                TooltipHandler.TipRegion(b, AiChat.FaqLabel(key) + "\n" + AiT.T("Chat.QuickTip", "Answered at once from your books. Nothing is sent anywhere."));
            }
        }

        static void DrawInput(Rect r)
        {
            float btnW = 150f;
            var box = new Rect(r.x, r.y, r.width - btnW - 8f, r.height);
            string t = Widgets.TextArea(box, AiChat.Input ?? "");
            if (t.Length > AiChat.QuestionMax) t = t.Substring(0, AiChat.QuestionMax);
            AiChat.Input = t;
            if (t.Length == 0) BankUI.Label(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, 24f), AiT.T("Chat.Placeholder", "Ask Ottoline anything about your money..."), BankUI.CreamDim, GameFont.Small, TextAnchor.UpperLeft, false);
            BankUI.Label(new Rect(box.xMax - 80f, box.yMax - 18f, 76f, 16f), t.Length + " / " + AiChat.QuestionMax, BankUI.CreamDim, GameFont.Tiny, TextAnchor.LowerRight, false);
            var bx = new Rect(box.xMax + 8f, r.y, btnW, 34f);
            if (AiChat.Pending)
            {
                if (BankUI.Button(bx, AiT.T("Chat.Stop", "Stop"))) AiChat.Stop();
            }
            else
            {
                bool can = t.Trim().Length > 0;
                string why = AiService.Active(AiFn.Ottoline) ? null : AiT.T("Chat.OfflineHint", "The AI is not available, so Ottoline answers from the books if she can.");
                if (BankUI.Button(bx, AiT.T("Chat.Send", "Send"), can, AiT.T("Chat.Empty", "Type a question first."))) AiChat.Ask(t);
                if (why != null)
                {
                    var hr = new Rect(bx.x, bx.yMax + 2f, btnW, r.height - 36f);
                    var fs = Text.Font; Text.Font = GameFont.Tiny;
                    bool fits = Text.CalcHeight(why, hr.width) <= hr.height;
                    Text.Font = fs;
                    BankUI.Label(hr, fits ? why : why.Truncate(hr.width * 2f - 8f), BankUI.CreamDim, GameFont.Tiny);
                    if (!fits) TooltipHandler.TipRegion(hr, why);
                }
            }
        }
    }
}
