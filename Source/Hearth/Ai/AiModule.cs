// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Silverline;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Ai
{
    /// <summary>
    /// Entry point of the optional AI flavour module (F05). The module only writes TEXT: market headlines, guest reviews, claim letters, Ottoline's chat answers and
    /// the concierge's tips. Mechanics, numbers and outcomes never depend on it, and every text has an offline version that is used whenever the relay is unreachable,
    /// resting, switched off or the player has not opted in. Requests go to the relay (hh-relay.jberger.de), never to a model provider directly, and never without consent.
    /// </summary>
    public sealed class AiModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.AiFlavour;

        public override void RegisterSettings()
        {
            const ModuleId M = ModuleId.AiFlavour;
            HSettings.Bool(M, "Texts", "ai.func.ottoline", true, "Ask Ottoline", "Chat with Clerk Ottoline in the bank. She explains your accounts, loans, investments and policies. Without AI she answers a few quick questions from the books.");
            HSettings.Bool(M, "Texts", "ai.func.headline", true, "Market headlines", "The Rim Exchange news ticker gets headlines written by the AI. The numbers behind them do not change.");
            HSettings.Bool(M, "Texts", "ai.func.claim", true, "Insurance claim letters", "The answer letters of Clawback Mutual react to what you wrote in the claim form. The decision and the amount never change.");
            HSettings.Bool(M, "Texts", "ai.func.review", true, "Guest reviews", "Some departing guests leave a review written in their own voice. Without AI the review is a short stock line.");
            HSettings.Bool(M, "Texts", "ai.func.concierge", true, "Concierge", "Ask the concierge a question about running your guests. It phrases the tips the game already knows.");
            HSettings.Percent(M, "Texts", "ai.reviewShare", 0.25f, 0f, 1f, "Share of departing groups that leave a review", "Keeps the number of AI requests small. 25 % means about one group in four.");
            HSettings.Bool(M, "Texts", "ai.showBadge", true, "Mark AI texts", "Adds a note to AI-written guest reviews and shows the AI badge in the chat and the concierge box. Market headlines get no mark.");
            HSettings.Int(M, "Limits", "ai.maxPerDay", 200, 0, 1000, "Requests per game day (local limit)", "A courtesy limit of this game on its own requests, per in-game day. 0 = no local limit. The relay has its own shared daily budget.", nerd: true, hardMin: 0, hardMax: 100000);
        }

        public override void OnDefsLoaded()
        {
            AiApi.Impl = new AiProvider();
            HearthGame.OnGameInit += AiService.OnGameInit;
            BankTabs.Register(new BankTab
            {
                Id = "ottoline", LabelKey = "HH.Ai.Tab.Ottoline", LabelEn = "Ask Ottoline", Order = 95, Module = ModuleId.AiFlavour,
                Draw = r => Guard.Run(ModuleId.AiFlavour, "Ai.OttolineTab", () => AiChatTab.Draw(r)),
            });
            Hearth.Shell.HearthUI.AddOverviewSection("ai.concierge", 11, ls => Guard.Run(ModuleId.AiFlavour, "Ai.ConciergeSection", () => AiConcierge.DrawSection(ls)));
            ModuleGate.Changed += OnModuleGateChanged;
            HLog.Info("AI", "AI flavour ready: relay " + AiService.RelayUrl + ", consent " + (AiConsent.Given ? "given" : "not given") + ", Ask Ottoline tab and concierge section registered");
        }

        static void OnModuleGateChanged(int mask)
        {
            // switched on in a game or in the template and not yet agreed: show the consent dialog once per session (never during automated tests)
            try
            {
                if (!AiService.ModuleOn || AiConsent.Given || AiConsent.DeclinedThisSession) return;
#if HH_TESTS
                if (Hearth.Testing.HearthTestRunner.Active) return;
#endif
                if (Current.ProgramState == ProgramState.Entry && Find.WindowStack == null) return;
                AiConsent.DeclinedThisSession = true;   // asked once; the settings page and the Ask Ottoline tab offer it again
                LongEventHandler.ExecuteWhenFinished(() => Dialog_AiConsent.Open());
            }
            catch (Exception e) { HLog.Warn("AI", "Could not open the consent dialog: " + e.GetType().Name); }
        }

        public override void OnGameStarted() { AiService.OnGameInit(); AiHeadlines.Sync(); }
        public override void OnGameLoaded() { AiService.OnGameInit(); AiHeadlines.Sync(); }

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % 30 == 3 && AiService.Busy) AiService.Pump();   // safety net: normally the frame patch pumps
            if (ticksGame % 250 == 7) AiHeadlines.Scan();
            if (ticksGame % 250 == 133) AiHeadlines.Flush(false);
        }

        public override void OnSwitchedOff(List<string> report)
        {
            AiService.CancelAll("module switched off");
            report?.Add("AI flavour: pending AI requests were dropped; the game uses its own texts again.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            yield return "State: " + AiService.StateLabel() + " (consent " + (AiConsent.Given ? "given " + AiConsent.Date : "not given") + ")";
            yield return "Relay: " + AiService.RelayUrl + (AiService.Status != null ? ", enabled=" + AiService.Status.Enabled + ", budget used " + AiService.Status.UsedPct.ToString("0.#") + " %" : ", no status yet");
            yield return "Requests: " + AiService.Sent + " sent, " + AiService.Succeeded + " ok, " + AiService.Failed + " failed, " + AiService.Refused + " refused, " + AiService.Fallbacks + " offline texts used";
            if (AiService.LastError != null) yield return "Last error code: " + AiService.LastError;
            if (AiService.Backoff.TimesPaused > 0) yield return "Paused after repeated failures " + AiService.Backoff.TimesPaused + " time(s) this session";
        }
    }

    /// <summary>
    /// Per-frame pump of the AI requests: a postfix on Game.UpdatePlay, which runs every frame of a running game, also while it is paused (web answers arrive in real time).
    /// A patch instead of a GameComponent keeps the save free of any trace of the module. If the patch cannot be applied (logged), the module's own GameTick pumps as well.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    static class Patch_Game_UpdatePlay_AiPump
    {
        static void Postfix()
        {
            if (!AiService.Enabled && !AiService.Busy) return;
            Guard.Run(ModuleId.AiFlavour, "Ai.Pump", AiService.Pump);
        }
    }

    /// <summary>The bridge to the rest of the mod: other features ask AiApi, this answers.</summary>
    public sealed class AiProvider : AiApi.IAiProvider
    {
        public bool Available(string function) => AiService.Active(function);

        public void Request(string function, Dictionary<string, object> fields, Action<string> onText, Action<string> onError)
        {
            var ticket = AiService.Send(function, fields, res =>
            {
                if (res.Ok && !string.IsNullOrWhiteSpace(res.Text))
                {
                    string text = AiText.Reply(res.Text, function == AiFn.ClaimLetter ? 900 : 600);
                    if (text.Length > 0) { onText?.Invoke(text); return; }
                }
                AiService.NoteFallback();
                onError?.Invoke(res.Error ?? "bad_reply");
            });
            if (ticket == null) { AiService.NoteFallback(); onError?.Invoke("unavailable"); }
        }
    }
}
