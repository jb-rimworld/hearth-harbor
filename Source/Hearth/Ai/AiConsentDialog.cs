// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Hearth.Shell;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Ai
{
    /// <summary>The opt-in window (F05): says in plain words what is sent, where it goes, what is stored, and asks. Nothing is ever sent before "I agree".</summary>
    public sealed class Dialog_AiConsent : Window
    {
        Vector2 scroll; bool details; float viewH = 800f;

        public Dialog_AiConsent()
        {
            doCloseX = true; draggable = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; forcePause = false; closeOnAccept = false; closeOnCancel = true;
            layer = WindowLayer.Dialog;
        }

        public override Vector2 InitialSize => HUI.Size(660f, 520f, 560f, 440f);

        /// <summary>Opens the dialog unless it is already open.</summary>
        public static void Open()
        {
            if (Find.WindowStack == null || Find.WindowStack.WindowOfType<Dialog_AiConsent>() != null) return;
            Find.WindowStack.Add(new Dialog_AiConsent());
        }

        public override void PreClose() { if (!AiConsent.Given) AiConsent.DeclinedThisSession = true; base.PreClose(); }

        public static string BodyText() =>
            AiT.T("Consent.Intro", "Hearth & Harbor can let an AI write some flavour texts: Clerk Ottoline's chat answers, market headlines, guest reviews, insurance claim letters and the concierge's tips. The game plays exactly the same without it, and it always has its own texts ready for when the AI is away.")
            + "\n\n" + AiT.T("Consent.Sent", "What is sent: game text for the request, for example pawn names (up to 64 letters), amounts, the text you type into the chat, the claim form or the concierge box, and counts such as guests or beds.")
            + "\n\n" + AiT.T("Consent.NotSent", "What is NOT sent: your Steam name or id, file paths, the name of your save or colony, your other mods, or anything that looks like a key.")
            + "\n\n" + AiT.T("Consent.Where", "Where it goes: to a small relay server run by the author of this mod (hh-relay.jberger.de), and from there through OpenRouter to Anthropic's Claude models, using only providers that keep no data (zero data retention).")
            + "\n\n" + AiT.T("Consent.Stored", "What is stored: the relay keeps only counters (how many tokens, what it cost, how long it took, error codes). It does not keep what you wrote or what the AI answered. Like any web server, the host of the relay may briefly hold your IP address in its technical connection logs.")
            + "\n\n" + AiT.T("Consent.Budget", "The AI budget is shared by all players over 24 hours. When it is used up, the AI rests and the game uses its own texts.")
            + "\n\n" + AiT.T("Consent.Withdraw", "You can withdraw at any time: Mod settings, Hearth & Harbor, AI flavour, Forget my consent.");

        public static string DetailsText() =>
            AiT.T("Consent.D.Ottoline", "Ask Ottoline: your question (500 letters at most) and a snapshot of the books: balance, savings, up to 10 loans, investments, policies and recent bookings, as numbers and short labels.")
            + "\n" + AiT.T("Consent.D.Headline", "Market headlines: the kind of market event, the name of the asset and the size of the move. At most 8 events per request.")
            + "\n" + AiT.T("Consent.D.Claim", "Claim letters: the policy, the pawn's name, the incident, the decision and the amount that the game already decided, and what you wrote in the claim form (600 letters at most).")
            + "\n" + AiT.T("Consent.D.Review", "Guest reviews: the guest's first name, the stars the game gave, a few reason words (for example the way they left) and the kinds of venue they spent money in.")
            + "\n" + AiT.T("Consent.D.Concierge", "Concierge: your question (300 letters at most) and up to 12 short facts, such as renown, number of guests and beds, and the problems the game found.");

        public override void DoWindowContents(Rect inRect)
        {
            var f = Text.Font; var a = Text.Anchor; var wrap = Text.WordWrap;
            Guard.Run(ModuleId.AiFlavour, "Ai.ConsentDialog", () => Draw(inRect));
            Text.Font = f; Text.Anchor = a; Text.WordWrap = wrap; GUI.color = Color.white;
        }

        void Draw(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width - 34f, 36f), AiT.T("Consent.Title", "AI texts (optional)"));
            Text.Font = GameFont.Small; Text.WordWrap = true;
            float btnH = 38f, footH = btnH + 14f;
            var body = new Rect(0, 44f, inRect.width, inRect.height - 44f - footH);
            float w = body.width - 18f;
            string text = BodyText();
            float h = Text.CalcHeight(text, w) + 8f;
            string det = details ? DetailsText() : null;
            float dh = det == null ? 0f : Text.CalcHeight(det, w - 16f) + 16f;
            var view = new Rect(0, 0, w, h + 36f + dh + 10f);
            Widgets.BeginScrollView(body, ref scroll, view);
            Widgets.Label(new Rect(0, 0, w, h), text);
            var tg = new Rect(0, h + 4f, 260f, 28f);
            if (Widgets.ButtonText(tg, details ? AiT.T("Consent.HideDetails", "Hide details") : AiT.T("Consent.ShowDetails", "What exactly is sent?"))) details = !details;
            if (det != null)
            {
                var dr = new Rect(0, tg.yMax + 6f, w, dh);
                Widgets.DrawBoxSolid(dr, new Color(1f, 1f, 1f, 0.05f));
                Widgets.Label(new Rect(dr.x + 8f, dr.y + 8f, dr.width - 16f, dr.height - 8f), det);
            }
            Widgets.EndScrollView();

            float y = inRect.height - btnH;
            float bw = (inRect.width - 12f) / 2f;
            if (Widgets.ButtonText(new Rect(0, y, bw, btnH), AiT.T("Consent.Agree", "I agree and switch on")))
            {
                AiConsent.Grant();
                SwitchModuleOn();
                Messages.Message(AiT.T("Consent.Done", "AI texts are on. The game checks the relay now."), MessageTypeDefOf.TaskCompletion, false);
                Close();
            }
            if (Widgets.ButtonText(new Rect(bw + 12f, y, bw, btnH), AiT.T("Consent.NotNow", "Not now")))
            {
                AiConsent.DeclinedThisSession = true;
                Close();
            }
        }

        /// <summary>Forgetting the consent also switches the module off (for this save and in the template), as the dialog promises.</summary>
        public static void SwitchModuleOff()
        {
            try
            {
                HearthProfile.Current?.SetModule(ModuleId.AiFlavour, false);
                HearthMod.Template.Profile.SetModule(ModuleId.AiFlavour, false);
                try { HearthMod.Instance?.WriteSettings(); } catch (Exception) { }
            }
            catch (Exception e) { HLog.Warn("AI", "Could not switch the AI module off: " + e.GetType().Name); }
        }

        /// <summary>The module is part of the opt-in: agreeing switches it on for this save (when one is running) and in the template.</summary>
        public static void SwitchModuleOn()
        {
            try
            {
                var cur = HearthProfile.Current;
                if (cur != null) cur.SetModule(ModuleId.AiFlavour, true);
                HearthMod.Template.Profile.SetModule(ModuleId.AiFlavour, true);
                try { HearthMod.Instance?.WriteSettings(); } catch (Exception) { }
                AiService.RefreshStatus();
            }
            catch (Exception e) { HLog.Warn("AI", "Could not switch the AI module on: " + e.GetType().Name); }
        }
    }

    /// <summary>The AI block at the top of the module's page in the settings window: state, consent, relay check, counters. Called by the shell's module page.</summary>
    public static class AiSettingsPanel
    {
        /// <summary>Draws at (0, y) with width w and returns the height used.</summary>
        public static float Draw(float y0, float w, HearthProfile target)
        {
            float y = y0;
            Text.Font = GameFont.Tiny;
            string relayLine = AiService.Status != null ? AiT.T("Panel.Relay", "Relay: shared budget used {0} % of the last 24 hours. This session: {1} sent, {2} ok, {3} failed, {4} own texts used.",
                AiService.Status.UsedPct.ToString("0"), AiService.Sent, AiService.Succeeded, AiService.Failed, AiService.Fallbacks) : null;
            string detailLine = AiService.StateDetail();
            float detailH = Mathf.Max(20f, Text.CalcHeight(detailLine, w - 24f));
            float relayH = relayLine == null ? 0f : Mathf.Max(20f, Text.CalcHeight(relayLine, w - 24f));
            Text.Font = GameFont.Small;
            float yDetail = 36f, yConsent = yDetail + detailH + 2f, yRelay = yConsent + 22f, yButtons = yRelay + relayH + 4f;
            var box = new Rect(0, y, w, yButtons + 26f + 8f);
            Widgets.DrawBoxSolid(box, new Color(1f, 1f, 1f, 0.05f));
            GUI.color = UiStyle.Line; Widgets.DrawBox(box); GUI.color = Color.white;
            var st = AiService.State;
            Color c = st == AiState.Online ? UiStyle.Good : st == AiState.Off || st == AiState.NoConsent ? UiStyle.Grey : UiStyle.Amber;
            UiStyle.LabelColored(new Rect(12f, y + 8f, w - 24f, 26f), AiService.StateLabel(), c, TextAnchor.MiddleLeft, GameFont.Medium);
            UiStyle.LabelColored(new Rect(12f, y + yDetail, w - 24f, detailH), detailLine, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            string consent = AiConsent.Given ? AiT.T("Panel.ConsentGiven", "You agreed on {0} (text version {1}).", AiConsent.Date ?? "?", HearthMod.Template.AiConsentVersion) : AiT.T("Panel.ConsentNone", "You have not agreed yet. Nothing is sent.");
            UiStyle.LabelColored(new Rect(12f, y + yConsent, w - 24f, 22f), consent, Color.white, TextAnchor.MiddleLeft);
            if (relayLine != null)
                UiStyle.LabelColored(new Rect(12f, y + yRelay, w - 24f, relayH), relayLine, UiStyle.Grey, TextAnchor.UpperLeft, GameFont.Tiny);
            float bx = 12f, by = y + yButtons;
            if (!AiConsent.Given)
            {
                if (Widgets.ButtonText(new Rect(bx, by, 230f, 26f), AiT.T("Panel.SetUp", "Set up AI texts..."))) Dialog_AiConsent.Open();
                bx += 238f;
            }
            else
            {
                if (Widgets.ButtonText(new Rect(bx, by, 190f, 26f), AiT.T("Panel.Forget", "Forget my consent")))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(AiT.T("Panel.ForgetConfirm", "Forget your consent and switch the AI module off? Nothing is sent from then on. You can agree again at any time."), () => { AiConsent.Withdraw(); Dialog_AiConsent.SwitchModuleOff(); }, true));
                bx += 198f;
                if (Widgets.ButtonText(new Rect(bx, by, 190f, 26f), AiT.T("Panel.Check", "Check the relay now"))) AiService.RefreshStatus();
                bx += 198f;
            }
            return box.height + 10f;
        }
    }
}
