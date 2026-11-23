// SPDX-License-Identifier: MIT
// Pure code (no game references) — compiled into Hearth.Core and into the unit tests.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hearth.Ai
{
    /// <summary>
    /// A tiny JSON writer and reader for the AI relay (F05). Written by hand so the unit tests can run without the game and
    /// so no game version depends on a JSON library. Values are: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool, null.
    /// The writer accepts any IDictionary with string keys, any IEnumerable (except string), all number types, bool, string, enums (as text) and null.
    /// </summary>
    public static class AiJson
    {
        // ------------------------------------------------------------------------------------------ writer
        public static string Write(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > 12) { sb.Append("null"); return; }
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case char c: WriteString(sb, c.ToString()); return;
                case Enum e: WriteString(sb, e.ToString()); return;
                case double d: WriteNumber(sb, d); return;
                case float f: WriteNumber(sb, f); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case sbyte _: case byte _: case short _: case ushort _: case int _: case uint _: case long _: case ulong _:
                    sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return;
                case IDictionary dict:
                {
                    sb.Append('{'); bool first = true;
                    foreach (DictionaryEntry kv in dict)
                    {
                        if (!first) sb.Append(','); first = false;
                        WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? "");
                        sb.Append(':'); WriteValue(sb, kv.Value, depth + 1);
                    }
                    sb.Append('}'); return;
                }
                case IEnumerable list:
                {
                    sb.Append('['); bool first = true;
                    foreach (var item in list) { if (!first) sb.Append(','); first = false; WriteValue(sb, item, depth + 1); }
                    sb.Append(']'); return;
                }
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""); return;
            }
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) { sb.Append(((long)d).ToString(CultureInfo.InvariantCulture)); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == 0x2028 || c == 0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else if (char.IsHighSurrogate(c) && !(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))) sb.Append("\\ufffd");      // lone surrogate: never produce broken UTF-8
                        else if (char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(s[i - 1]))) sb.Append("\\ufffd");
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------------------------------ reader
        /// <summary>Parses JSON text. Returns false (value null) on any syntax error, trailing garbage or nesting deeper than 32.</summary>
        public static bool TryParse(string text, out object value)
        {
            value = null;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                int i = 0;
                SkipWs(text, ref i);
                value = ReadValue(text, ref i, 0);
                SkipWs(text, ref i);
                if (i != text.Length) { value = null; return false; }
                return true;
            }
            catch (FormatException) { value = null; return false; }
        }

        public static object ParseOrNull(string text) => TryParse(text, out var v) ? v : null;

        static void SkipWs(string t, ref int i) { while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r' || t[i] == '﻿')) i++; }

        static object ReadValue(string t, ref int i, int depth)
        {
            if (depth > 32) throw new FormatException("too deep");
            if (i >= t.Length) throw new FormatException("eof");
            char c = t[i];
            switch (c)
            {
                case '{':
                {
                    i++; var d = new Dictionary<string, object>(); SkipWs(t, ref i);
                    if (i < t.Length && t[i] == '}') { i++; return d; }
                    while (true)
                    {
                        SkipWs(t, ref i);
                        if (i >= t.Length || t[i] != '"') throw new FormatException("key");
                        string k = ReadString(t, ref i); SkipWs(t, ref i);
                        if (i >= t.Length || t[i] != ':') throw new FormatException(":");
                        i++; SkipWs(t, ref i);
                        d[k] = ReadValue(t, ref i, depth + 1); SkipWs(t, ref i);
                        if (i >= t.Length) throw new FormatException("eof");
                        if (t[i] == ',') { i++; continue; }
                        if (t[i] == '}') { i++; return d; }
                        throw new FormatException("object");
                    }
                }
                case '[':
                {
                    i++; var l = new List<object>(); SkipWs(t, ref i);
                    if (i < t.Length && t[i] == ']') { i++; return l; }
                    while (true)
                    {
                        SkipWs(t, ref i);
                        l.Add(ReadValue(t, ref i, depth + 1)); SkipWs(t, ref i);
                        if (i >= t.Length) throw new FormatException("eof");
                        if (t[i] == ',') { i++; continue; }
                        if (t[i] == ']') { i++; return l; }
                        throw new FormatException("array");
                    }
                }
                case '"': return ReadString(t, ref i);
                case 't': Expect(t, ref i, "true"); return true;
                case 'f': Expect(t, ref i, "false"); return false;
                case 'n': Expect(t, ref i, "null"); return null;
                default: return ReadNumber(t, ref i);
            }
        }

        static void Expect(string t, ref int i, string word)
        {
            if (string.CompareOrdinal(t, i, word, 0, word.Length) != 0) throw new FormatException(word);
            i += word.Length;
        }

        static object ReadNumber(string t, ref int i)
        {
            int s = i;
            while (i < t.Length && "+-0123456789.eE".IndexOf(t[i]) >= 0) i++;
            if (i == s) throw new FormatException("value");
            if (!double.TryParse(t.Substring(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw new FormatException("number");
            return d;
        }

        static string ReadString(string t, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= t.Length) throw new FormatException("string");
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) throw new FormatException("escape");
                char e = t[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > t.Length || !int.TryParse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp)) throw new FormatException("unicode");
                        sb.Append((char)cp); i += 4; break;
                    default: throw new FormatException("escape");
                }
            }
        }

        // ------------------------------------------------------------------------------------------ accessors
        public static object Get(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v : null;
        public static string Str(object o, string key) => Get(o, key) as string;
        public static bool Bool(object o, string key, bool dflt = false) => Get(o, key) is bool b ? b : dflt;
        public static double Num(object o, string key, double dflt = 0) => Get(o, key) is double n ? n : dflt;
        public static List<object> List(object o, string key) => Get(o, key) as List<object>;
    }
}
