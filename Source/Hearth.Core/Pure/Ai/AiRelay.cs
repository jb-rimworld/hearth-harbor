// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hearth.Ai
{
    /// <summary>Answer of GET /v1/status (see hearth-relay/README.md). No dollar amounts, only a percentage of the shared budget.</summary>
    public sealed class RelayStatus
    {
        public bool Enabled;
        /// <summary>"no_key", "disabled" or null.</summary>
        public string Reason;
        public readonly Dictionary<string, bool> Functions = new Dictionary<string, bool>();
        public double UsedPct;
        public string Notice;
        public string MinClient;

        public bool FunctionOn(string fn) => Enabled && Functions.TryGetValue(fn, out bool on) && on;
        /// <summary>The shared budget of the last 24 hours is used up (or as good as): the relay will refuse for a while.</summary>
        public bool BudgetUsedUp => UsedPct >= 99.5;

        /// <summary>Null when the text is not a status object.</summary>
        public static RelayStatus Parse(string json)
        {
            if (!AiJson.TryParse(json, out var o) || !(o is Dictionary<string, object>)) return null;
            if (!AiJson.Bool(o, "ok")) return null;
            var s = new RelayStatus { Enabled = AiJson.Bool(o, "enabled"), Reason = AiJson.Str(o, "reason"), MinClient = AiJson.Str(o, "min_client") };
            if (AiJson.Get(o, "functions") is Dictionary<string, object> fns) foreach (var kv in fns) s.Functions[kv.Key] = kv.Value is bool b && b;
            s.UsedPct = Math.Max(0, Math.Min(100, AiJson.Num(AiJson.Get(o, "budget"), "used_pct")));
            s.Notice = AiText.Notice(AiJson.Str(o, "notice"));
            return s;
        }
    }

    /// <summary>Answer of POST /v1/ai/&lt;function&gt;: text, or an error code the game maps to a fallback.</summary>
    public sealed class RelayReply
    {
        public bool Ok;
        public string Text;
        public List<string> Headlines;
        /// <summary>Relay error code (invalid, disabled, budget, busy, upstream, timeout, not_found, method) or a client-side one: network, bad_reply, outdated.</summary>
        public string Error;
        public int Http;
        public double UsedPct = -1;

        public bool IsBudget => Error == "budget";
        /// <summary>Errors that say "the relay is fine, it just does not want us right now": they never count as a failure.</summary>
        public bool IsRefusal => Error == "budget" || Error == "disabled" || Error == "busy";

        /// <param name="http">HTTP status, 0 when no answer arrived at all (network error, timeout of the request itself).</param>
        public static RelayReply Parse(int http, string body, bool transportFailed)
        {
            var r = new RelayReply { Http = http };
            if (transportFailed || http == 0) { r.Error = "network"; return r; }
            if (!AiJson.TryParse(body, out var o) || !(o is Dictionary<string, object>))
            {
                r.Error = http >= 500 || http == 404 ? "upstream" : "bad_reply";
                return r;
            }
            if (AiJson.Get(o, "budget") is Dictionary<string, object> b) r.UsedPct = AiJson.Num(b, "used_pct", -1);
            if (!AiJson.Bool(o, "ok"))
            {
                r.Error = AiText.Code(AiJson.Str(o, "error")) ?? "upstream";
                return r;
            }
            string text = AiJson.Str(o, "text");
            if (AiJson.Get(o, "headlines") is List<object> hs)
            {
                r.Headlines = new List<string>();
                foreach (var h in hs) r.Headlines.Add(h as string ?? "");
            }
            if (string.IsNullOrWhiteSpace(text) && (r.Headlines == null || r.Headlines.Count == 0)) { r.Error = "bad_reply"; return r; }
            r.Text = text; r.Ok = true;
            return r;
        }
    }

    /// <summary>Counts failures in a row; five in a row pause all AI requests for ten minutes (the game keeps using its own texts). Time is passed in so tests can travel.</summary>
    public sealed class AiBackoff
    {
        public const int Limit = 5;
        public const double PauseSeconds = 600;
        public int Consecutive;
        public double PausedUntil;
        public int TimesPaused;

        public bool Paused(double now) => now < PausedUntil;
        public double SecondsLeft(double now) => Math.Max(0, PausedUntil - now);
        public void Success() { Consecutive = 0; }
        public void Failure(double now)
        {
            Consecutive++;
            if (Consecutive >= Limit) { PausedUntil = now + PauseSeconds; Consecutive = 0; TimesPaused++; }
        }
        public void Reset() { Consecutive = 0; PausedUntil = 0; }
    }

    /// <summary>Cleaning of what comes back from the relay before it reaches the player, and of what goes into saves.</summary>
    public static class AiText
    {
        public static string Code(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var c = AiSchema.Code(s, 24, "");
            return c.Length == 0 ? null : c;
        }

        /// <summary>The relay's notice line for the player (max 500 chars, no control characters).</summary>
        public static string Notice(string s) => string.IsNullOrWhiteSpace(s) ? null : AiSchema.Clean(s, 500, true);

        /// <summary>A reply text for a chat bubble or a letter: control characters out, links out, markdown markers out, cut at a word at <paramref name="max"/>.</summary>
        public static string Reply(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = AiSchema.Clean(s, max * 2, true);
            t = StripLinks(t);
            t = t.Replace("**", "").Replace("__", "").Replace("`", "");
            var lines = t.Split('\n');
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("#")) line = line.TrimStart('#', ' ');   // no markdown headings
                sb.Append(line).Append('\n');
            }
            t = sb.ToString().Trim();
            while (t.Contains("\n\n\n")) t = t.Replace("\n\n\n", "\n\n");
            return CutAtWord(t, max);
        }

        /// <summary>A one-line headline: one line, quotes trimmed, links out, at most <paramref name="max"/> chars. Empty when nothing usable is left.</summary>
        public static string Headline(string s, int max = 120)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = StripLinks(AiSchema.Clean(s.Replace('\n', ' ').Replace('\r', ' '), max * 2));
            t = t.Replace("**", "").Replace("`", "").Trim().Trim('"', '“', '”', '\'').Trim();
            return CutAtWord(t, max);
        }

        public static string StripLinks(string s)
        {
            if (string.IsNullOrEmpty(s) || (s.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0)) return s;
            var words = s.Split(' ');
            var sb = new StringBuilder();
            foreach (var w in words)
            {
                string lw = w.ToLowerInvariant();
                if (lw.StartsWith("http://") || lw.StartsWith("https://") || lw.StartsWith("www.")) continue;
                if (lw.Length > 0 && (lw.Contains("\nhttp") || lw.Contains("\nwww."))) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
            }
            return sb.ToString();
        }

        public static string CutAtWord(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            int cut = max - 1;
            int sp = s.LastIndexOf(' ', cut);
            if (sp > max / 2) cut = sp;
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut).TrimEnd(' ', ',', ';', ':', '-') + "…";
        }

        /// <summary>A text shortened for a tooltip or log line (log lines never carry AI text: this is for the UI only).</summary>
        public static string Short(string s, int max) => CutAtWord(AiSchema.Clean(s ?? "", max * 2), max);

        /// <summary>A stable small number from a string (not string.GetHashCode: that changes between runs on some runtimes).</summary>
        public static int StableHash(string s)
        {
            unchecked { int h = 17; if (s != null) foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; }
        }

        /// <summary>Sentence-like check of a string against things that must never reach a chat bubble from our own side (keys, user paths): true when it looks like a secret.</summary>
        public static bool LooksSecret(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string l = s.ToLowerInvariant();
            if (l.Contains("sk-or-") || l.Contains("sk-ant-") || l.Contains("api_key") || l.Contains("apikey=") || l.Contains("bearer ")) return true;
            if (l.Contains("/users/") || l.Contains("\\users\\") || l.Contains("/home/")) return true;
            // long unbroken base64/hex-like token
            int run = 0;
            foreach (char c in s)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') { if (++run >= 32) return true; }
                else run = 0;
            }
            return false;
        }
    }

    public static class AiRedact
    {
        public const string Marker = "[removed]";

        /// <summary>Replaces every word that looks like a key, a token or a user path by [removed] (mirrors the secret filter of KONZEPT 15.10). Ordinary text is returned unchanged.</summary>
        public static string Words(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 8) return s;
            if (!AiText.LooksSecret(s)) return s;
            var sb = new StringBuilder(s.Length); var word = new StringBuilder();
            Action flush = () => { if (word.Length > 0) { string w = word.ToString(); sb.Append(AiText.LooksSecret(w) ? Marker : w); word.Clear(); } };
            foreach (char c in s)
            {
                if (c == ' ' || c == '\n' || c == '\t' || c == '\r') { flush(); sb.Append(c); } else word.Append(c);
            }
            flush();
            return sb.ToString();
        }

        /// <summary>Redacts every string inside a payload (dictionaries, lists, strings) in place and returns it.</summary>
        public static object Deep(object v)
        {
            switch (v)
            {
                case string str: return Words(str);
                case Dictionary<string, object> d:
                    foreach (var k in new List<string>(d.Keys)) d[k] = Deep(d[k]);
                    return d;
                case List<object> l:
                    for (int i = 0; i < l.Count; i++) l[i] = Deep(l[i]);
                    return l;
                default: return v;
            }
        }
    }

    /// <summary>Plain-text fallbacks that never need the relay (guest reviews).</summary>
    public static class AiOffline
    {
        /// <summary>Four short review lines per star rating (index 1..5). Keyed translations HH.Ai.Review.Fallback.&lt;stars&gt;.&lt;n&gt; replace them in other languages.</summary>
        public static readonly string[][] ReviewLines =
        {
            null,
            new[] { "One star, and only because the form has no zero.", "I asked for a quiet night and got a lesson in patience.", "Never again, though the door was lovely on the way out.", "I have stayed in worse. I cannot remember where." },
            new[] { "It was fine if you like waiting. I do not.", "Some good ideas, badly timed.", "The staff tried. The building did not help.", "Two stars: one for the effort, one for the door." },
            new[] { "A room, a roof and a reasonably warm welcome.", "Nothing to complain about and nothing to write home about, so here I am.", "Decent. I would come back if the road happened to bring me.", "Three stars. The middle of the road is a fine place to stand." },
            new[] { "Good food, kind people, a bed that behaved itself.", "I slept well and paid fairly. That is rarer than it sounds.", "Pleasant and well run. I will tell my caravan.", "Four stars. Something was missing and I forgot what, which says plenty." },
            new[] { "Best stay of the season. The staff remembered my name and my order.", "I came for a night and almost forgot to leave.", "Warm, clean, funny. Five stars and a handshake.", "I would live here if they let me, and they might not." },
        };

        /// <summary>A deterministic fallback review line for <paramref name="stars"/> (1..5); <paramref name="seed"/> picks the variant.</summary>
        public static string ReviewLine(int stars, int seed, out int index)
        {
            stars = Math.Max(1, Math.Min(5, stars));
            var lines = ReviewLines[stars];
            index = Math.Abs(seed) % lines.Length;
            return lines[index];
        }

        public static string Stars(int stars)
        {
            stars = Math.Max(0, Math.Min(5, stars));
            return new string('★', stars) + new string('☆', 5 - stars);
        }
    }
}
