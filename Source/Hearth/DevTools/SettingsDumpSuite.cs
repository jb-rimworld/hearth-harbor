// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hearth.Testing;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Writes every registered setting (key, English label/description, category, enum values) as JSON for the translation
    /// tooling (tools/i18n/settings_keyed.py). Explicit only: run as `tools/run_test.sh core.settingsdump <label>`.
    /// Output: $HH_SETTINGS_DUMP or /private/tmp/claude-501/hh_tests/settings_dump.json.
    /// </summary>
    public sealed class SettingsDumpSuite : HearthTestSuite
    {
        public override string Name => "core.settingsdump";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 600;

        static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            string path = Environment.GetEnvironmentVariable("HH_SETTINGS_DUMP");
            if (string.IsNullOrEmpty(path)) path = "/private/tmp/claude-501/hh_tests/settings_dump.json";
            var sb = new StringBuilder("[\n");
            int n = 0;
            foreach (var s in HSettings.All)
            {
                if (n++ > 0) sb.Append(",\n");
                sb.Append("{\"key\":").Append(J(s.Key)).Append(",\"module\":").Append(J(s.Module.ToString()))
                  .Append(",\"category\":").Append(J(s.Category)).Append(",\"catKey\":").Append(J(HSetting.CatKey(s.Category ?? "")))
                  .Append(",\"label\":").Append(J(s.LabelEn)).Append(",\"desc\":").Append(J(s.DescEn))
                  .Append(",\"enumType\":").Append(J(s.EnumType?.Name)).Append(",\"enumNames\":[");
                if (s.EnumType != null)
                {
                    var names = Enum.GetNames(s.EnumType);
                    for (int i = 0; i < names.Length; i++) { if (i > 0) sb.Append(','); sb.Append(J(names[i])); }
                }
                sb.Append("]}");
            }
            sb.Append("\n]\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            t.Check(n > 100, "dumped " + n + " settings to " + path);
            yield return 1;
        }
    }
}
#endif
