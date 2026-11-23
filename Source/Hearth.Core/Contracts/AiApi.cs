using System;
using System.Collections.Generic;

namespace Hearth
{
    /// <summary>
    /// Contract between game features and the optional "AI flavour" module (1.1.0, F05/F27).
    /// Lives in Hearth.Core so the standalone Silverline can use it too. Features never depend on the AI:
    /// they call a Try* method with a ready offline text; when the AI module is off, unavailable or fails,
    /// <paramref name="onText"/> is simply never called (or called with the fallback, see each method) and the
    /// offline text stays. All callbacks arrive on the main thread.
    /// The AI module sets <see cref="Impl"/> in its OnDefsLoaded; nobody else may set it.
    /// </summary>
    public static class AiApi
    {
        public interface IAiProvider
        {
            /// <summary>True when the player opted in, the module is on and the relay (or the player's own key) is usable.</summary>
            bool Available(string function);
            /// <summary>Sends a request; the provider builds the payload JSON itself from <paramref name="fields"/>.</summary>
            void Request(string function, Dictionary<string, object> fields, Action<string> onText, Action<string> onError);
        }

        public static IAiProvider Impl;

        public static bool Available(string function) => Impl != null && SafeAvailable(function);

        static bool SafeAvailable(string f)
        {
            try { return Impl.Available(f); } catch (Exception) { return false; }
        }

        /// <summary>
        /// Insurance claim letter (F27). <paramref name="choices"/> are the claim form's selection fields (key → chosen option id),
        /// <paramref name="freeText"/> the player's free text (max 600 chars). Outcome and amount are already decided and must not change.
        /// </summary>
        public static bool TryClaimLetter(string policy, string pawn, string incident, string outcome, float amount, string reasonCode,
            Dictionary<string, string> choices, string freeText, Action<string> onText)
        {
            if (!Available("claim-letter")) return false;
            var form = new Dictionary<string, object> { { "choices", choices ?? new Dictionary<string, string>() }, { "free_text", Trim(freeText, 600) } };
            var f = new Dictionary<string, object>
            {
                { "policy", Trim(policy, 64) }, { "pawn", Trim(pawn, 64) }, { "incident", Trim(incident, 64) },
                { "outcome", outcome }, { "amount", amount }, { "reason_code", Trim(reasonCode, 64) }, { "form", form },
            };
            Impl.Request("claim-letter", f, onText, null);
            return true;
        }

        public static string Trim(string s, int max) => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max));
    }
}
