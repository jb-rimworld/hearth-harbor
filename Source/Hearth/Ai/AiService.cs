// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Ai
{
    public sealed class AiResult
    {
        public bool Ok;
        public string Text;
        public List<string> Headlines;
        /// <summary>Relay error code, or a client-side one: network, bad_reply, timeout, cancelled, too_big, unavailable.</summary>
        public string Error;
    }

    /// <summary>One request that was queued. Cancel() stops it (the callback then gets "cancelled").</summary>
    public sealed class AiTicket
    {
        public string Function;
        public bool Cancelled, Done, Started;
        internal IAiHandle Handle;
        internal string Json;
        internal Action<AiResult> Callback;
        internal double Created, Deadline;
        public void Cancel() { AiService.Cancel(this); }
    }

    public enum AiState { Off, NoConsent, Checking, Online, Resting, Paused, Unavailable, Offline }

    /// <summary>
    /// The one place that talks to the relay (https://hh-relay.jberger.de): consent gate, status polling, request queue, result dispatch, failure back-off.
    /// Everything runs on the main thread: Send/Get start an asynchronous web request and <see cref="Pump"/> (called every frame by a postfix on Game.UpdatePlay) picks the
    /// finished ones up and calls the callbacks. Nothing here ever waits. A callback is called exactly once, even when the request is cancelled.
    /// The game never depends on an answer: every caller has its own offline text and uses it when the callback says "not ok".
    /// </summary>
    public static class AiService
    {
        public const string DefaultRelay = "https://hh-relay.jberger.de";
        public const int MaxInFlight = 2, MaxQueued = 8, DefaultTimeout = 35;
        public const int MaxBodyBytes = 7900;     // the relay refuses more than 8 KB

        // seams for tests ---------------------------------------------------------------------------------
        public static IAiTransport Transport = new UnityAiTransport();
        public static Func<double> Clock = () => Time.realtimeSinceStartup;
        /// <summary>Tests: pretend the opt-in was given / the module is on without touching the template.</summary>
        public static bool? ConsentOverride, ModuleOverride;

        // state -------------------------------------------------------------------------------------------
        public static RelayStatus Status;
        public static double StatusAt = -1e9, StatusRetryAt, LastInterest = -1e9, RestUntil;
        public static string RestReason, LastError, StatusError;
        public static int Sent, Succeeded, Failed, Refused, Fallbacks, StatusChecks;
        public static readonly AiBackoff Backoff = new AiBackoff();
        static readonly List<AiTicket> inFlight = new List<AiTicket>(), queued = new List<AiTicket>();
        static IAiHandle statusHandle;
        static int dayIndex = -1, dayCount;

        public static bool Busy => inFlight.Count > 0 || queued.Count > 0 || statusHandle != null;
        public static int InFlightCount => inFlight.Count;
        public static int QueuedCount => queued.Count;
        public static double Now => Clock();

        public static string RelayUrl
        {
            get
            {
                try
                {
                    // a developer override (own relay, local test relay): one line in Config/HearthHarbor_AI.relay; ignored unless it is http(s)
                    string p = Path.Combine(GenFilePaths.ConfigFolderPath, "HearthHarbor_AI.relay");
                    if (File.Exists(p)) { string u = File.ReadAllText(p).Trim().TrimEnd('/'); if (u.StartsWith("https://") || u.StartsWith("http://localhost") || u.StartsWith("http://127.0.0.1")) return u; }
                }
                catch (Exception) { }
                return DefaultRelay;
            }
        }

        public static string Lang()
        {
            try { return AiLang.ForFolder(LanguageDatabase.activeLanguage?.folderName); } catch (Exception) { return "en"; }
        }

        // gates -------------------------------------------------------------------------------------------
        public static bool ModuleOn => ModuleOverride ?? Modules.On(ModuleId.AiFlavour);
        public static bool ConsentGiven => ConsentOverride ?? AiConsent.Given;
        /// <summary>The module is on AND the player opted in: the precondition of every request.</summary>
        public static bool Enabled => ModuleOn && ConsentGiven;

        static bool FunctionSwitchOn(string fn)
        {
            string key = AiFn.SettingKey(fn);
            try { return key != null && (HSettings.Find(key) == null || HSettings.GetBool(key)); } catch (Exception) { return true; }
        }

        /// <summary>True when a request for <paramref name="fn"/> would be sent right now.</summary>
        public static bool Active(string fn)
        {
            if (!Enabled || !FunctionSwitchOn(fn)) return false;
            if (Status == null || !Status.FunctionOn(fn) || Status.BudgetUsedUp) return false;
            double now = Now;
            if (Backoff.Paused(now) || now < RestUntil) return false;
            if (DailyCapReached()) return false;
            return true;
        }

        public static AiState State
        {
            get
            {
                if (!ModuleOn) return AiState.Off;
                if (!ConsentGiven) return AiState.NoConsent;
                double now = Now;
                if (Backoff.Paused(now)) return AiState.Paused;
                if (Status == null) return StatusError != null ? AiState.Offline : AiState.Checking;
                if (!Status.Enabled) return AiState.Unavailable;
                if (now < RestUntil || Status.BudgetUsedUp) return AiState.Resting;
                return AiState.Online;
            }
        }

        public static string StateLabel()
        {
            switch (State)
            {
                case AiState.Off: return AiT.T("State.Off", "AI texts are off");
                case AiState.NoConsent: return AiT.T("State.NoConsent", "AI texts are not set up");
                case AiState.Checking: return AiT.T("State.Checking", "Checking the AI relay...");
                case AiState.Online: return AiT.T("State.Online", "AI online");
                case AiState.Resting: return AiT.T("State.Resting", "AI is resting");
                case AiState.Paused: return AiT.T("State.Paused", "AI paused for a few minutes");
                case AiState.Unavailable: return AiT.T("State.Unavailable", "AI not available right now");
                default: return AiT.T("State.Offline", "AI offline");
            }
        }

        public static string StateDetail()
        {
            switch (State)
            {
                case AiState.Off: return AiT.T("State.Off.Tip", "Switch the module on in the Hearth & Harbor settings.");
                case AiState.NoConsent: return AiT.T("State.NoConsent.Tip", "Nothing is sent anywhere until you agree. The game uses its own texts.");
                case AiState.Online: return AiT.T("State.Online.Tip", "The relay answers. Texts written by the AI are marked.");
                case AiState.Resting: return AiT.T("State.Resting.Tip", "The shared daily budget of the relay is used up. The game uses its own texts until it recovers.");
                case AiState.Paused: return AiT.T("State.Paused.Tip", "Several requests failed in a row. The game uses its own texts for ten minutes.");
                case AiState.Unavailable: return AiT.T("State.Unavailable.Tip", "The relay is switched off or not ready. The game uses its own texts.");
                case AiState.Offline: return AiT.T("State.Offline.Tip", "The relay could not be reached. The game uses its own texts and tries again later.");
                default: return "";
            }
        }

        // daily cap ---------------------------------------------------------------------------------------
        static bool DailyCapReached()
        {
            int cap;
            try { cap = HSettings.Find("ai.maxPerDay") == null ? 0 : HSettings.GetInt("ai.maxPerDay"); } catch (Exception) { return false; }
            if (cap <= 0) return false;
            RollDay();
            return dayCount >= cap;
        }

        static void RollDay()
        {
            int d = Find.TickManager != null ? Find.TickManager.TicksGame / 60000 : 0;
            if (d != dayIndex) { dayIndex = d; dayCount = 0; }
        }

        // requests ----------------------------------------------------------------------------------------
        /// <summary>
        /// Queues a request. <paramref name="fields"/> are the relay's input fields without <c>lang</c> (added here); they are repaired to the relay's limits, scrubbed of
        /// anything that looks like a key or a path, and sent as JSON. Returns null (callback never called) when AI is not active for this function or the payload is unusable;
        /// otherwise the callback is called exactly once on the main thread. <paramref name="deadlineSeconds"/> ends the request early with "timeout".
        /// </summary>
        public static AiTicket Send(string fn, IDictionary<string, object> fields, Action<AiResult> done, int deadlineSeconds = DefaultTimeout, bool priority = false)
        {
            if (!Active(fn) || done == null) return null;
            if (queued.Count >= MaxQueued) { HLog.Once("ai.queueFull", LogLevel.Warn, "AI", "Request queue is full: new AI requests are skipped (offline texts are used)"); return null; }
            var f = new Dictionary<string, object>(fields);
            if (!f.ContainsKey("lang")) f["lang"] = Lang();
            var clean = AiSchema.Normalize(fn, f);
            if (clean == null) { HLog.Once("ai.badPayload." + fn, LogLevel.Warn, "AI", "Payload for " + fn + " was unusable (a required field is missing); offline text used"); return null; }
            AiRedact.Deep(clean);
            Shrink(fn, clean);
            string json = AiJson.Write(clean);
            if (Encoding.UTF8.GetByteCount(json) > MaxBodyBytes) { HLog.Once("ai.tooBig." + fn, LogLevel.Warn, "AI", "Payload for " + fn + " is too big; offline text used"); return null; }
            double now = Now;
            var t = new AiTicket { Function = fn, Callback = done, Json = json, Created = now, Deadline = now + Math.Max(5, deadlineSeconds) };
            if (priority) queued.Insert(0, t); else queued.Add(t);
            LastInterest = now;
            RollDay(); dayCount++;
            StartQueued();
            return t;
        }

        /// <summary>Keeps the Ottoline snapshot under the relay's body limit by dropping the tails of its lists.</summary>
        public static void Shrink(string fn, Dictionary<string, object> clean)
        {
            if (fn != AiFn.Ottoline || !(clean.TryGetValue("snapshot", out var s) && s is Dictionary<string, object> snap)) return;
            string[] lists = { "recent", "policies", "investments", "loans" };
            for (int guard = 0; guard < 8 && Encoding.UTF8.GetByteCount(AiJson.Write(clean)) > MaxBodyBytes; guard++)
                foreach (var name in lists)
                    if (snap.TryGetValue(name, out var l) && l is List<object> list && list.Count > 0) list.RemoveRange(list.Count / 2, list.Count - list.Count / 2);
        }

        static void StartQueued()
        {
            while (inFlight.Count < MaxInFlight && queued.Count > 0)
            {
                var t = queued[0]; queued.RemoveAt(0);
                if (t.Cancelled || t.Done) continue;
                try
                {
                    t.Handle = Transport.Post(RelayUrl + "/v1/ai/" + t.Function, t.Json, HearthMod.Version, DefaultTimeout);
                    t.Started = true; Sent++;
                    inFlight.Add(t);
                }
                catch (Exception e)
                {
                    HLog.Warn("AI", "Could not start the request for " + t.Function + ": " + e.GetType().Name);
                    Finish(t, new AiResult { Error = "network" }, 0, -1);
                    RecordFailure("network");
                }
            }
        }

        public static void Cancel(AiTicket t)
        {
            if (t == null || t.Done) return;
            t.Cancelled = true;
            if (inFlight.Remove(t)) { try { t.Handle?.Abort(); } catch (Exception) { } }
            else queued.Remove(t);
            Finish(t, new AiResult { Error = "cancelled" }, 0, -1);
        }

        public static void CancelAll(string why)
        {
            foreach (var t in new List<AiTicket>(inFlight)) Cancel(t);
            foreach (var t in new List<AiTicket>(queued)) Cancel(t);
            if (statusHandle != null) { try { statusHandle.Abort(); } catch (Exception) { } statusHandle = null; }
            if (why != null) HLog.Debug("AI", "All AI requests cancelled: " + why);
        }

        /// <summary>Tests only: the next callbacks throw on purpose, so their failure is logged as a warning instead of a red error.</summary>
        internal static bool ExpectCallbackFailure;

        static void Finish(AiTicket t, AiResult r, int http, double ms)
        {
            if (t.Done) return;
            t.Done = true; t.Handle = null;
            try { t.Callback(r); }
            catch (Exception e)
            {
                // a failed request is never an error (its callback just gets the fallback result); a callback that throws is a bug of the caller and is reported
                if (ExpectCallbackFailure) HLog.Warn("AiFlavour", "Ai.Callback." + t.Function + " threw (expected by the test): " + e.Message);
                else Guard.Fail(ModuleId.AiFlavour, "Ai.Callback." + t.Function, e);
            }
        }

        // pump --------------------------------------------------------------------------------------------
        /// <summary>Called every frame. Cheap when nothing is going on.</summary>
        public static void Pump()
        {
            double now = Now;
            if (!Enabled)
            {
                if (inFlight.Count + queued.Count > 0 || statusHandle != null) CancelAll("module off or no consent");
                return;
            }
            // finished requests
            for (int i = inFlight.Count - 1; i >= 0; i--)
            {
                var t = inFlight[i];
                if (t.Handle == null) { inFlight.RemoveAt(i); continue; }
                bool timedOut = now > t.Deadline && !t.Handle.IsDone;
                if (!t.Handle.IsDone && !timedOut) continue;
                inFlight.RemoveAt(i);
                if (timedOut)
                {
                    try { t.Handle.Abort(); } catch (Exception) { }
                    RecordFailure("timeout");
                    Finish(t, new AiResult { Error = "timeout" }, 0, now - t.Created);
                    continue;
                }
                Complete(t, now);
            }
            // queued requests waiting too long (the queue only backs up when the relay is slow)
            for (int i = queued.Count - 1; i >= 0; i--)
                if (now > queued[i].Deadline) { var t = queued[i]; queued.RemoveAt(i); RecordFailure("timeout"); Finish(t, new AiResult { Error = "timeout" }, 0, -1); }
            StartQueued();
            PumpStatus(now);
        }

        static void Complete(AiTicket t, double now)
        {
            AiHttpResult http = null;
            try { http = t.Handle.Result; } catch (Exception e) { HLog.Warn("AI", "Reading a reply failed: " + e.GetType().Name); }
            double ms = (now - t.Created) * 1000;
            var reply = RelayReply.Parse(http?.Status ?? 0, http?.Body, http == null || http.TransportFailed);
            if (reply.UsedPct >= 0 && Status != null) Status.UsedPct = reply.UsedPct;
            var res = new AiResult { Ok = reply.Ok, Text = reply.Ok ? reply.Text : null, Headlines = reply.Headlines, Error = reply.Error };
            // log line: function, outcome and numbers only, never text
            HLog.Info("AI", "request fn=" + t.Function + " status=" + (http?.Status ?? 0) + " result=" + (reply.Ok ? "ok" : reply.Error) + " ms=" + (int)ms);
            if (reply.Ok) { Backoff.Success(); Succeeded++; LastError = null; }
            else if (reply.IsBudget) { RestUntil = now + 600; RestReason = "budget"; if (Status != null) Status.UsedPct = 100; Refused++; LastError = "budget"; }
            else if (reply.Error == "busy") { RestUntil = now + 30; RestReason = "busy"; Refused++; LastError = "busy"; }
            else if (reply.Error == "disabled") { Status = null; StatusError = null; StatusRetryAt = now + 60; Refused++; LastError = "disabled"; }
            else RecordFailure(reply.Error);
            Finish(t, res, http?.Status ?? 0, ms);
        }

        static void RecordFailure(string code)
        {
            Failed++; LastError = code;
            bool wasPaused = Backoff.Paused(Now);
            Backoff.Failure(Now);
            if (!wasPaused && Backoff.Paused(Now)) HLog.Warn("AI", "Five failed requests in a row (last: " + code + "): AI pauses for ten minutes, the game uses its own texts");
        }

        /// <summary>A feature that wanted AI text and used its own instead (diagnosis counter).</summary>
        public static void NoteFallback() { Fallbacks++; }

        // status ------------------------------------------------------------------------------------------
        /// <summary>The UI is open or a feature wants AI: keep the relay status fresh (every ten minutes) while someone cares.</summary>
        public static void Touch() { LastInterest = Now; }

        /// <summary>Asks the relay for its status now (settings button, consent given, game start). Ignored while one is running.</summary>
        public static void RefreshStatus() { StatusRetryAt = 0; StatusAt = -1e9; LastInterest = Now; }

        static void PumpStatus(double now)
        {
            if (statusHandle != null)
            {
                if (!statusHandle.IsDone) { if (now - StatusAt > 40) { try { statusHandle.Abort(); } catch (Exception) { } } return; }
                AiHttpResult http = null;
                try { http = statusHandle.Result; } catch (Exception) { }
                statusHandle = null;
                var st = http != null && !http.TransportFailed && http.Status == 200 ? RelayStatus.Parse(http.Body) : null;
                StatusChecks++;
                if (st == null) { Status = null; StatusError = http == null || http.TransportFailed ? "network" : "bad_reply"; StatusRetryAt = now + 120; HLog.Info("AI", "status check failed (" + StatusError + ")"); }
                else { Status = st; StatusError = null; StatusAt = now; HLog.Info("AI", "status: enabled=" + st.Enabled + (st.Reason != null ? " reason=" + st.Reason : "") + " used=" + st.UsedPct.ToString("0.#") + "%"); AiNotice.Check(st); }
                return;
            }
            bool never = Status == null && now >= StatusRetryAt;
            // a relay that is switched off, out of key or out of budget is asked again every ten minutes even when nobody has the AI screens open,
            // so a running game picks the AI up as soon as the owner switches it on; a healthy status is only refreshed while someone cares
            bool stale = Status != null && now - StatusAt > 600 && (now - LastInterest < 900 || !Status.Enabled || Status.BudgetUsedUp);
            if (!never && !stale) return;
            try
            {
                statusHandle = Transport.Get(RelayUrl + "/v1/status", HearthMod.Version, 15);
                StatusAt = now;   // while a check runs, StatusAt doubles as its start time
                if (Status == null) StatusRetryAt = now + 120;
            }
            catch (Exception e) { HLog.Warn("AI", "Could not start the status check: " + e.GetType().Name); StatusError = "network"; StatusRetryAt = now + 120; }
        }

        // lifecycle ---------------------------------------------------------------------------------------
        public static void OnGameInit()
        {
            CancelAll("game init");
            Status = null; StatusError = null; StatusRetryAt = 0; StatusAt = -1e9; RestUntil = 0; RestReason = null;
            Backoff.Reset(); dayIndex = -1; dayCount = 0;
            Sent = Succeeded = Failed = Refused = Fallbacks = StatusChecks = 0; LastError = null;
            AiHeadlines.Reset(); AiChat.Reset(); AiReviews.Reset(); AiConcierge.Reset();
        }

        public static void OnConsentChanged()
        {
            CancelAll("consent changed");
            Status = null; StatusError = null; StatusRetryAt = 0; StatusAt = -1e9; Backoff.Reset(); RestUntil = 0;
            LastInterest = Now;
        }
    }

    /// <summary>The relay can show the player one notice (e.g. "the budget ends soon"): a letter, once per text.</summary>
    public static class AiNotice
    {
        public static void Check(RelayStatus st)
        {
            try
            {
                if (st?.Notice == null || Current.ProgramState != ProgramState.Playing) return;
                string seen = HearthMod.Template.AiNoticeSeen, id = AiText.StableHash(st.Notice).ToString();
                if (seen == id) return;
                HearthMod.Template.AiNoticeSeen = id;
                try { HearthMod.Instance?.WriteSettings(); } catch (Exception) { }
                Find.LetterStack.ReceiveLetter(AiT.T("Notice.Title", "Hearth & Harbor: a note about the AI texts"), st.Notice, LetterDefOf.NeutralEvent);
            }
            catch (Exception e) { HLog.Warn("AI", "Could not show the relay notice: " + e.GetType().Name); }
        }
    }
}
