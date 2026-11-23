// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hearth.Ai
{
    /// <summary>The five relay functions (names are the URL parts of POST /v1/ai/&lt;function&gt;).</summary>
    public static class AiFn
    {
        public const string Ottoline = "ottoline", Headline = "headline", ClaimLetter = "claim-letter", Review = "review", Concierge = "concierge";
        public static readonly string[] All = { Ottoline, Headline, ClaimLetter, Review, Concierge };
        public static bool Known(string fn) { foreach (var f in All) if (f == fn) return true; return false; }
        /// <summary>Settings key of the per-function switch (ai.func.*).</summary>
        public static string SettingKey(string fn)
        {
            switch (fn)
            {
                case Ottoline: return "ai.func.ottoline";
                case Headline: return "ai.func.headline";
                case ClaimLetter: return "ai.func.claim";
                case Review: return "ai.func.review";
                case Concierge: return "ai.func.concierge";
                default: return null;
            }
        }
    }

    /// <summary>
    /// Mirrors the relay's request validation (hearth-relay/src/validate.mjs and templates/*.mjs) on the client: every payload is trimmed to the relay's
    /// limits BEFORE it is sent, so a long pawn name or a stray control character can never turn into a "400 invalid". Unknown fields are dropped,
    /// strings are cut, numbers are clamped, lists are shortened. A required value that cannot be repaired makes the whole payload fail (Normalize returns null).
    /// </summary>
    public static class AiSchema
    {
        public abstract class Node { public abstract bool Norm(object v, out object clean); }

        // ---------------------------------------------------------------------------------------- text helpers
        /// <summary>Control characters become spaces (newlines/tabs only when allowed), then trim and cut to <paramref name="max"/> without splitting a surrogate pair.</summary>
        public static string Clean(string s, int max, bool multiline = false)
        {
            if (s == null) return "";
            var sb = new StringBuilder(Math.Min(s.Length, max + 2));
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ctl = c < 0x20 || c == 0x7f;
                if (ctl)
                {
                    if (multiline && c == '\r') { if (!(i + 1 < s.Length && s[i + 1] == '\n')) sb.Append('\n'); }   // \r\n counts once
                    else if (multiline && (c == '\n' || c == '\t')) sb.Append(c);
                    else sb.Append(' ');
                }
                else sb.Append(c);
            }
            string r = sb.ToString();
            if (multiline) r = r.Replace("\n\n\n", "\n\n");
            r = r.Trim();
            if (r.Length > max)
            {
                int cut = max;
                if (cut > 0 && char.IsHighSurrogate(r[cut - 1])) cut--;
                r = r.Substring(0, cut).TrimEnd();
            }
            return r;
        }

        /// <summary>Allowed characters of a code (reason codes, choice keys): letters, digits and _ . : -; everything else becomes _.</summary>
        public static string Code(string s, int max, string extraAllowed = ":")
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (sb.Length >= max) break;
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-' || extraAllowed.IndexOf(c) >= 0;
                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------- nodes
        sealed class StrNode : Node
        {
            readonly int max, min; readonly bool multiline; readonly Func<string, string> fix;
            public StrNode(int max, int min, bool multiline, Func<string, string> fix) { this.max = max; this.min = min; this.multiline = multiline; this.fix = fix; }
            public override bool Norm(object v, out object clean)
            {
                clean = null;
                if (!(v is string s)) return false;
                string r = fix != null ? fix(s) : Clean(s, max, multiline);
                if (r.Length < min) return false;
                clean = r; return true;
            }
        }

        sealed class NumNode : Node
        {
            readonly double min, max; readonly bool integer;
            public NumNode(double min, double max, bool integer) { this.min = min; this.max = max; this.integer = integer; }
            public override bool Norm(object v, out object clean)
            {
                clean = null;
                if (!TryNumber(v, out double d)) return false;
                if (double.IsNaN(d)) return false;
                if (d < min) d = min; if (d > max) d = max;
                if (integer) { clean = (long)Math.Round(d); return true; }
                clean = Math.Round(d, 6); return true;
            }
        }

        sealed class EnumNode : Node
        {
            readonly string[] vals;
            public EnumNode(string[] vals) { this.vals = vals; }
            public override bool Norm(object v, out object clean)
            {
                clean = null;
                var s = (v as string)?.Trim().ToLowerInvariant();
                if (s == null) return false;
                foreach (var x in vals) if (x == s) { clean = x; return true; }
                return false;
            }
        }

        sealed class NumOrStrNode : Node
        {
            readonly NumNode n = new NumNode(-1e12, 1e12, false);
            public override bool Norm(object v, out object clean)
            {
                if (v is string s) { clean = Clean(s, 64); return true; }
                return n.Norm(v, out clean);
            }
        }

        sealed class ArrNode : Node
        {
            readonly Node item; readonly int max;
            public ArrNode(Node item, int max) { this.item = item; this.max = max; }
            public override bool Norm(object v, out object clean)
            {
                clean = null;
                if (!(v is IEnumerable e) || v is string || v is IDictionary) return false;
                var list = new List<object>();
                foreach (var x in e)
                {
                    if (list.Count >= max) break;
                    if (item.Norm(x, out var c)) list.Add(c);
                }
                clean = list; return true;
            }
        }

        public sealed class Field { public Node Node; public bool Required = true; public object Default; public bool HasDefault; }

        sealed class ObjNode : Node
        {
            readonly Dictionary<string, Field> shape;
            public ObjNode(Dictionary<string, Field> shape) { this.shape = shape; }
            public override bool Norm(object v, out object clean)
            {
                clean = null;
                var src = ToDict(v); if (src == null) return false;
                var o = new Dictionary<string, object>();
                foreach (var kv in shape)
                {
                    if (src.TryGetValue(kv.Key, out var raw) && raw != null && kv.Value.Node.Norm(raw, out var c)) { o[kv.Key] = c; continue; }
                    if (kv.Value.Node is DefaultNode dn) { o[kv.Key] = dn.Fresh(); continue; }
                    if (kv.Value.HasDefault) { o[kv.Key] = kv.Value.Default; continue; }
                    if (kv.Value.Required) return false;
                }
                clean = o; return true;
            }
        }

        sealed class RecNode : Node
        {
            readonly int max, keyMax, valMax;
            public RecNode(int max, int keyMax, int valMax) { this.max = max; this.keyMax = keyMax; this.valMax = valMax; }
            public override bool Norm(object v, out object clean)
            {
                clean = null;
                var src = ToDict(v); if (src == null) return false;
                var o = new Dictionary<string, object>();
                foreach (var kv in src)
                {
                    if (o.Count >= max) break;
                    string k = Code(kv.Key, keyMax, "");
                    if (k.Length == 0 || o.ContainsKey(k) || kv.Value == null) continue;
                    o[k] = Clean(Convert.ToString(kv.Value, CultureInfo.InvariantCulture), valMax);
                }
                clean = o; return true;
            }
        }

        static Dictionary<string, object> ToDict(object v)
        {
            if (v is Dictionary<string, object> d) return d;
            if (v is IDictionary id)
            {
                var o = new Dictionary<string, object>();
                foreach (DictionaryEntry kv in id) o[Convert.ToString(kv.Key, CultureInfo.InvariantCulture)] = kv.Value;
                return o;
            }
            return null;
        }

        static bool TryNumber(object v, out double d)
        {
            d = 0;
            switch (v)
            {
                case double x: d = x; return true;
                case float f: d = f; return true;
                case int i: d = i; return true;
                case long l: d = l; return true;
                case short sh: d = sh; return true;
                case decimal m: d = (double)m; return true;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                default: return false;
            }
        }

        // ---------------------------------------------------------------------------------------- builders
        public static Node Str(int max, int min = 0, bool multiline = false) => new StrNode(max, min, multiline, null);
        public static Node CodeStr(int max) => new StrNode(max, 1, false, s => Code(s, max));
        public static Node Lang() => new StrNode(5, 2, false, s => AiLang.Normalize(s));
        public static Node Num(double min = -1e12, double max = 1e12) => new NumNode(min, max, false);
        public static Node Int(long min, long max) => new NumNode(min, max, true);
        public static Node Enum(params string[] vals) => new EnumNode(vals);
        public static Node NumOrStr() => new NumOrStrNode();
        public static Node Arr(Node item, int max) => new ArrNode(item, max);
        public static Node Rec(int max, int keyMax = 32, int valMax = 64) => new RecNode(max, keyMax, valMax);
        public static Node Obj(params (string name, Node node, bool required)[] fields)
        {
            var shape = new Dictionary<string, Field>();
            foreach (var f in fields) shape[f.name] = new Field { Node = f.node, Required = f.required };
            return new ObjNode(shape);
        }
        public static Node Obj(Dictionary<string, Field> shape) => new ObjNode(shape);
        static (string, Node, bool) R(string n, Node node) => (n, node, true);
        static (string, Node, bool) O(string n, Node node) => (n, node, false);

        static Node Opt(Node n, object dflt) => new DefaultNode(n, dflt);
        sealed class DefaultNode : Node
        {
            readonly Node n; readonly object d;
            public DefaultNode(Node n, object d) { this.n = n; this.d = d; }
            public override bool Norm(object v, out object clean) { if (n.Norm(v, out clean)) return true; clean = Fresh(); return true; }
            public object Fresh() => d is Dictionary<string, object> dd ? new Dictionary<string, object>(dd) : d;
        }

        // ---------------------------------------------------------------------------------------- the five schemas (same shapes as hearth-relay/src/templates)
        static readonly Dictionary<string, Node> schemas = new Dictionary<string, Node>
        {
            [AiFn.Ottoline] = Obj(
                R("lang", Lang()), R("question", Str(500, 1, true)),
                R("snapshot", Obj(
                    R("balance", NumOrStr()), R("savings", NumOrStr()),
                    R("loans", Arr(Obj(R("kind", Str(64)), R("amount", NumOrStr()), R("rate", NumOrStr())), 20)),
                    R("investments", Arr(Obj(R("name", Str(64)), R("value", NumOrStr()), R("change_pct", NumOrStr())), 20)),
                    R("policies", Arr(Obj(R("kind", Str(64)), R("pawn", Str(64)), R("coverage", NumOrStr())), 20)),
                    R("recent", Arr(Obj(R("memo", Str(64)), R("amount", NumOrStr())), 20))))),
            [AiFn.Headline] = Obj(
                R("lang", Lang()),
                R("events", Arr(Obj(R("kind", Str(32, 1)), R("asset", Str(48, 1)), R("change_pct", Num(-1000, 1000))), 8))),
            [AiFn.ClaimLetter] = Obj(
                R("lang", Lang()), R("policy", Str(64, 1)), R("pawn", Str(64, 1)), R("incident", Str(120, 1)),
                R("outcome", Enum("approved", "partial", "denied")), R("amount", Num(0, 1e9)), R("reason_code", CodeStr(40)),
                R("form", Obj(O("choices", Opt(Rec(8, 32, 64), new Dictionary<string, object>())), O("free_text", Opt(Str(600, 0, true), ""))))),
            [AiFn.Review] = Obj(
                R("lang", Lang()), R("guest", Str(64, 1)), R("stars", Int(1, 5)),
                R("reasons", Arr(CodeStr(40), 8)), R("venue_kinds", Arr(Str(32, 1), 8))),
            [AiFn.Concierge] = Obj(
                R("lang", Lang()), R("question", Str(300, 1, true)), R("facts", Arr(Str(120, 1), 12))),
        };

        /// <summary>The relay's input shape of <paramref name="fn"/>, repaired (cut, clamped, defaulted). Null when a required value is missing or unusable, or the function is unknown.</summary>
        public static Dictionary<string, object> Normalize(string fn, IDictionary<string, object> fields)
        {
            if (fn == null || fields == null || !schemas.TryGetValue(fn, out var node)) return null;
            return node.Norm(fields, out var clean) ? clean as Dictionary<string, object> : null;
        }
    }

    /// <summary>RimWorld language folder → the language code the relay understands.</summary>
    public static class AiLang
    {
        static readonly string[][] table =
        {
            new[] { "english", "en" }, new[] { "german", "de" }, new[] { "french", "fr" }, new[] { "japanese", "ja" }, new[] { "chinese", "zh" },
            new[] { "russian", "ru" }, new[] { "ukrainian", "uk" }, new[] { "czech", "cs" }, new[] { "korean", "ko" }, new[] { "portuguesebrazilian", "pt-BR" },
            new[] { "portuguese", "pt" }, new[] { "spanish", "es" }, new[] { "italian", "it" }, new[] { "polish", "pl" }, new[] { "turkish", "tr" },
            new[] { "dutch", "nl" }, new[] { "swedish", "sv" }, new[] { "hungarian", "hu" }, new[] { "finnish", "fi" }, new[] { "danish", "da" },
            new[] { "norwegian", "no" }, new[] { "romanian", "ro" }, new[] { "greek", "el" }, new[] { "slovak", "sk" }, new[] { "vietnamese", "vi" },
        };

        /// <summary>RimWorld language folder name ("German (Deutsch)", "PortugueseBrazilian (Português Brasileiro)" …) → code, "en" when unknown.</summary>
        public static string ForFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "en";
            string f = folder.Trim().ToLowerInvariant();
            foreach (var row in table) if (f.StartsWith(row[0], StringComparison.Ordinal)) return row[1];
            return "en";
        }

        /// <summary>A language code in the relay's format (2–3 lowercase letters, optional -XX), "en" when it does not fit.</summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code)) return "en";
            code = code.Trim().Replace('_', '-');
            int dash = code.IndexOf('-');
            string a = dash < 0 ? code : code.Substring(0, dash), b = dash < 0 ? null : code.Substring(dash + 1);
            if (a.Length < 2 || a.Length > 3) return "en";
            foreach (char c in a) if (c < 'a' && !(c >= 'A' && c <= 'Z') || c > 'z') return "en";
            a = a.ToLowerInvariant();
            if (b == null) return a;
            if (b.Length != 2) return a;
            return a + "-" + b.ToUpperInvariant();
        }
    }
}
