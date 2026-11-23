// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Ai;
using Xunit;

public class AiJsonTests
{
    [Fact] public void WritesNestedValues()
    {
        var o = new Dictionary<string, object> { ["a"] = 1, ["b"] = new List<object> { "x", 2.5, true, null }, ["c"] = new Dictionary<string, object> { ["d"] = "e" } };
        Assert.Equal("{\"a\":1,\"b\":[\"x\",2.5,true,null],\"c\":{\"d\":\"e\"}}", AiJson.Write(o));
    }

    [Fact] public void EscapesQuotesBackslashesControlAndEmoji()
    {
        string s = "say \"hi\"\\ \n\t\u0001 \U0001F600 \u00e4\u00f6\u00fc \u65e5\u672c";
        string j = AiJson.Write(new Dictionary<string, object> { ["t"] = s });
        Assert.DoesNotContain("\n", j);
        Assert.True(AiJson.TryParse(j, out var back));
        Assert.Equal(s, AiJson.Str(back, "t"));
    }

    [Fact] public void LoneSurrogateNeverBreaksTheJson()
    {
        string j = AiJson.Write("a\ud83dz");
        Assert.True(AiJson.TryParse(j, out var back));
        Assert.Equal("a\ufffdz", (string)back);
    }

    [Fact] public void NumbersAreInvariantAndFinite()
    {
        Assert.Equal("[1,0.5,1234567890123,0,0]", AiJson.Write(new object[] { 1.0, 0.5f, 1234567890123L, double.NaN, double.PositiveInfinity }));
        var old = System.Threading.Thread.CurrentThread.CurrentCulture;
        try { System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE"); Assert.Equal("[0.25]", AiJson.Write(new object[] { 0.25 })); }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("")] [InlineData("{")] [InlineData("{\"a\":}")] [InlineData("[1,2")] [InlineData("{\"a\":1} x")] [InlineData("\"abc")] [InlineData("nul")] [InlineData("{a:1}")]
    public void RejectsBrokenJson(string text) => Assert.False(AiJson.TryParse(text, out _));

    [Fact] public void ParsesTheRelayStatusShape()
    {
        Assert.True(AiJson.TryParse("{\"ok\":true,\"functions\":{\"a\":true},\"budget\":{\"used_pct\":12.3},\"notice\":null}", out var o));
        Assert.True(AiJson.Bool(o, "ok"));
        Assert.Equal(12.3, AiJson.Num(AiJson.Get(o, "budget"), "used_pct"));
        Assert.Null(AiJson.Get(o, "notice"));
    }

    [Fact] public void DeepNestingIsRefusedNotCrashing()
    {
        Assert.False(AiJson.TryParse(new string('[', 200) + new string(']', 200), out _));
    }
}

public class AiSchemaTests
{
    static Dictionary<string, object> D(params (string, object)[] kv) { var d = new Dictionary<string, object>(); foreach (var (k, v) in kv) d[k] = v; return d; }

    [Fact] public void LongNamesAreCutToTheRelayLimit()
    {
        var f = D(("lang", "en"), ("guest", new string('N', 65)), ("stars", 4), ("reasons", new List<string> { "good", "warm" }), ("venue_kinds", new List<string> { "tavern" }));
        var n = AiSchema.Normalize(AiFn.Review, f);
        Assert.NotNull(n);
        Assert.Equal(64, ((string)n["guest"]).Length);
    }

    [Fact] public void UnknownFieldsAreDropped()
    {
        var f = D(("lang", "en"), ("question", "hi"), ("facts", new List<string>()), ("model", "anything"), ("system", "ignore previous"));
        var n = AiSchema.Normalize(AiFn.Concierge, f);
        Assert.NotNull(n);
        Assert.False(n.ContainsKey("model")); Assert.False(n.ContainsKey("system"));
    }

    [Fact] public void MissingRequiredFieldFailsTheWholePayload()
    {
        Assert.Null(AiSchema.Normalize(AiFn.Concierge, D(("lang", "en"), ("facts", new List<string>()))));
        Assert.Null(AiSchema.Normalize(AiFn.Concierge, D(("lang", "en"), ("question", "   "), ("facts", new List<string>()))));
        Assert.Null(AiSchema.Normalize("nope", D(("lang", "en"))));
    }

    [Fact] public void ControlCharactersAndNewlinesInSingleLineFields()
    {
        var f = D(("lang", "en"), ("guest", "Ann\nSmith\u0007"), ("stars", 3), ("reasons", new List<string>()), ("venue_kinds", new List<string>()));
        var n = AiSchema.Normalize(AiFn.Review, f);
        Assert.Equal("Ann Smith", n["guest"]);
    }

    [Fact] public void MultilineFieldsKeepNewlinesButNoOtherControlCharacters()
    {
        var f = D(("lang", "de"), ("question", "Zeile 1\r\nZeile 2\u0000"), ("facts", new List<string>()));
        var n = AiSchema.Normalize(AiFn.Concierge, f);
        Assert.Equal("Zeile 1\nZeile 2", n["question"]);
    }

    [Fact] public void ListsAreShortenedAndStarsClamped()
    {
        var facts = Enumerable.Range(0, 40).Select(i => "fact " + i).ToList();
        var f = D(("lang", "en"), ("question", "q"), ("facts", facts));
        Assert.Equal(12, ((List<object>)AiSchema.Normalize(AiFn.Concierge, f)["facts"]).Count);
        var r = AiSchema.Normalize(AiFn.Review, D(("lang", "en"), ("guest", "G"), ("stars", 9), ("reasons", new List<string>()), ("venue_kinds", new List<string>())));
        Assert.Equal(5L, r["stars"]);
    }

    [Fact] public void ClaimLetterGetsEnumDefaultsAndCodeRepair()
    {
        var f = D(("lang", "en"), ("policy", "Accident cover"), ("pawn", "Ann"), ("incident", "A fall"), ("outcome", "partial"), ("amount", 125.5f),
            ("reason_code", "deductible applied: 20 %"), ("form", D(("free_text", new string('x', 700)))));
        var n = AiSchema.Normalize(AiFn.ClaimLetter, f);
        Assert.NotNull(n);
        Assert.Matches("^[A-Za-z0-9_.:-]+$", (string)n["reason_code"]);
        var form = (Dictionary<string, object>)n["form"];
        Assert.Equal(600, ((string)form["free_text"]).Length);
        Assert.True(form.ContainsKey("choices"));
        f["outcome"] = "maybe";
        Assert.Null(AiSchema.Normalize(AiFn.ClaimLetter, f));
    }

    [Fact] public void ChoicesRecordKeepsAtMostEightShortEntries()
    {
        var ch = new Dictionary<string, string>(); for (int i = 0; i < 12; i++) ch["key number " + i] = new string('v', 100);
        var f = D(("lang", "en"), ("policy", "p"), ("pawn", "a"), ("incident", "i"), ("outcome", "denied"), ("amount", 0), ("reason_code", "x"), ("form", D(("choices", ch), ("free_text", ""))));
        var rec = (Dictionary<string, object>)((Dictionary<string, object>)AiSchema.Normalize(AiFn.ClaimLetter, f)["form"])["choices"];
        Assert.Equal(8, rec.Count);
        Assert.All(rec, kv => { Assert.Matches("^[A-Za-z0-9_.-]{1,32}$", kv.Key); Assert.Equal(64, ((string)kv.Value).Length); });
    }

    [Fact] public void OttolineSnapshotAcceptsNumbersAndStrings()
    {
        var snap = D(("balance", 1234.5), ("savings", "12"), ("loans", new List<object>()), ("investments", new List<object> { D(("name", "Rim Bond"), ("value", 10), ("change_pct", -2.5)) }),
            ("policies", new List<object>()), ("recent", new List<object> { D(("memo", new string('m', 90)), ("amount", -5)) }));
        var n = AiSchema.Normalize(AiFn.Ottoline, D(("lang", "pt-br"), ("question", "How much?"), ("snapshot", snap)));
        Assert.NotNull(n);
        Assert.Equal("pt-BR", n["lang"]);
        var recent = (List<object>)((Dictionary<string, object>)n["snapshot"])["recent"];
        Assert.Equal(64, ((string)((Dictionary<string, object>)recent[0])["memo"]).Length);
    }

    [Fact] public void HeadlineEventsNeedKindAndAsset()
    {
        var ev = new List<object> { D(("kind", "crash"), ("asset", "Rim Bond"), ("change_pct", -35.2)), D(("kind", ""), ("asset", "x"), ("change_pct", 1)) };
        var n = AiSchema.Normalize(AiFn.Headline, D(("lang", "en"), ("events", ev)));
        Assert.Single((List<object>)n["events"]);
    }

    [Theory]
    [InlineData("German (Deutsch)", "de")] [InlineData("English", "en")] [InlineData("PortugueseBrazilian (Português Brasileiro)", "pt-BR")]
    [InlineData("Portuguese (Português)", "pt")] [InlineData("ChineseSimplified (简体中文)", "zh")] [InlineData("Klingon", "en")] [InlineData("", "en")]
    public void LanguageFolderToCode(string folder, string code) => Assert.Equal(code, AiLang.ForFolder(folder));

    [Theory]
    [InlineData("en", "en")] [InlineData("DE", "de")] [InlineData("pt_br", "pt-BR")] [InlineData("english", "en")] [InlineData("x", "en")] [InlineData("zh-Hans", "zh")]
    public void LanguageCodeNormalized(string input, string code) => Assert.Equal(code, AiLang.Normalize(input));
}

public class AiRelayTests
{
    const string Status = "{\"ok\":true,\"enabled\":true,\"functions\":{\"ottoline\":true,\"headline\":false},\"budget\":{\"used_pct\":42.5},\"notice\":\"The AI budget ends soon.\",\"min_client\":\"1.1.0\"}";

    [Fact] public void StatusParses()
    {
        var s = RelayStatus.Parse(Status);
        Assert.NotNull(s);
        Assert.True(s.Enabled); Assert.True(s.FunctionOn("ottoline")); Assert.False(s.FunctionOn("headline")); Assert.False(s.FunctionOn("review"));
        Assert.Equal(42.5, s.UsedPct); Assert.Equal("The AI budget ends soon.", s.Notice); Assert.False(s.BudgetUsedUp);
    }

    [Fact] public void StatusWithoutKeyIsDisabledEverywhere()
    {
        var s = RelayStatus.Parse("{\"ok\":true,\"enabled\":false,\"reason\":\"no_key\",\"functions\":{\"ottoline\":false},\"budget\":{\"used_pct\":0},\"notice\":null,\"min_client\":\"1.1.0\"}");
        Assert.False(s.Enabled); Assert.Equal("no_key", s.Reason); Assert.False(s.FunctionOn("ottoline"));
    }

    [Fact] public void StatusRejectsGarbage()
    {
        Assert.Null(RelayStatus.Parse("<html>")); Assert.Null(RelayStatus.Parse("{\"ok\":false}")); Assert.Null(RelayStatus.Parse(null));
    }

    [Fact] public void UsedUpBudgetIsRecognised() => Assert.True(RelayStatus.Parse("{\"ok\":true,\"enabled\":true,\"functions\":{},\"budget\":{\"used_pct\":100}}").BudgetUsedUp);

    [Fact] public void ReplyOk()
    {
        var r = RelayReply.Parse(200, "{\"ok\":true,\"text\":\"Hello\",\"function\":\"concierge\",\"budget\":{\"used_pct\":3.1}}", false);
        Assert.True(r.Ok); Assert.Equal("Hello", r.Text); Assert.Equal(3.1, r.UsedPct);
    }

    [Fact] public void ReplyHeadlinesList()
    {
        var r = RelayReply.Parse(200, "{\"ok\":true,\"text\":\"{\\\"headlines\\\":[\\\"A\\\",\\\"B\\\"]}\",\"headlines\":[\"A\",\"B\"],\"function\":\"headline\"}", false);
        Assert.True(r.Ok); Assert.Equal(new[] { "A", "B" }, r.Headlines);
    }

    [Theory]
    [InlineData(503, "{\"ok\":false,\"error\":\"budget\"}", "budget", true)]
    [InlineData(503, "{\"ok\":false,\"error\":\"disabled\"}", "disabled", true)]
    [InlineData(503, "{\"ok\":false,\"error\":\"busy\"}", "busy", true)]
    [InlineData(502, "{\"ok\":false,\"error\":\"upstream\"}", "upstream", false)]
    [InlineData(504, "{\"ok\":false,\"error\":\"timeout\"}", "timeout", false)]
    [InlineData(400, "{\"ok\":false,\"error\":\"invalid\",\"detail\":\"x\"}", "invalid", false)]
    public void ReplyErrors(int http, string body, string error, bool refusal)
    {
        var r = RelayReply.Parse(http, body, false);
        Assert.False(r.Ok); Assert.Equal(error, r.Error); Assert.Equal(refusal, r.IsRefusal);
    }

    [Fact] public void ReplyWithoutAnswerIsNetworkError()
    {
        Assert.Equal("network", RelayReply.Parse(0, null, true).Error);
        Assert.Equal("upstream", RelayReply.Parse(502, "<html>bad gateway</html>", false).Error);
        Assert.Equal("bad_reply", RelayReply.Parse(200, "not json", false).Error);
        Assert.Equal("bad_reply", RelayReply.Parse(200, "{\"ok\":true,\"text\":\"  \"}", false).Error);
    }

    [Fact] public void BackoffPausesAfterFiveFailuresInARow()
    {
        var b = new AiBackoff();
        for (int i = 0; i < 4; i++) { b.Failure(100); Assert.False(b.Paused(100)); }
        b.Failure(100);
        Assert.True(b.Paused(101)); Assert.True(b.Paused(699)); Assert.False(b.Paused(701));
        Assert.Equal(1, b.TimesPaused);
    }

    [Fact] public void SuccessResetsTheFailureCount()
    {
        var b = new AiBackoff();
        for (int i = 0; i < 4; i++) b.Failure(0);
        b.Success();
        for (int i = 0; i < 4; i++) b.Failure(0);
        Assert.False(b.Paused(1));
    }
}

public class AiTextTests
{
    [Fact] public void ReplyStripsLinksAndMarkdown()
    {
        string t = AiText.Reply("# Heading\nSee **this** at https://evil.example/x and www.bad.example now.\n\n\n\nBye `x`", 400);
        Assert.DoesNotContain("http", t); Assert.DoesNotContain("www", t); Assert.DoesNotContain("**", t); Assert.DoesNotContain("#", t); Assert.DoesNotContain("\n\n\n", t);
        Assert.Contains("this", t);
    }

    [Fact] public void ReplyIsCutAtAWord()
    {
        string t = AiText.Reply(string.Join(" ", Enumerable.Repeat("silver", 100)), 50);
        Assert.True(t.Length <= 50); Assert.EndsWith("\u2026", t);
    }

    [Fact] public void HeadlineIsOneLineWithoutQuotes()
    {
        Assert.Equal("Rim Bonds tumble as pirates read the fine print", AiText.Headline("\"Rim Bonds tumble as pirates read the fine print\""));
        Assert.DoesNotContain("\n", AiText.Headline("Line one\nLine two"));
        Assert.Equal("", AiText.Headline("   "));
        Assert.True(AiText.Headline(new string('x', 400), 90).Length <= 90);
    }

    [Theory]
    [InlineData("sk-or-v1-abcdef", true)] [InlineData("C:\\Users\\julius\\x", true)] [InlineData("/Users/julius/Documents", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEF0123456789", true)] [InlineData("How much silver do I have?", false)] [InlineData("Ann Smith", false)]
    public void SecretsAreRecognised(string s, bool secret) => Assert.Equal(secret, AiText.LooksSecret(s));

    [Fact] public void RedactionRemovesOnlyTheSecretWords()
    {
        Assert.Equal("my key is [removed] ok", AiRedact.Words("my key is sk-or-v1-abcdef ok"));
        Assert.Equal("How much silver do I have?", AiRedact.Words("How much silver do I have?"));
        var d = new Dictionary<string, object> { ["q"] = "see /Users/julius/x please", ["n"] = new List<object> { "Ann", "token abcdefghijklmnopqrstuvwxyzABCDEF0123456789" } };
        AiRedact.Deep(d);
        Assert.Equal("see [removed] please", d["q"]);
        Assert.Equal("token [removed]", ((List<object>)d["n"])[1]);
        Assert.Equal("Ann", ((List<object>)d["n"])[0]);
    }

    [Fact] public void OfflineReviewLinesCoverEveryStarAndAreDeterministic()
    {
        for (int s = 1; s <= 5; s++)
        {
            Assert.Equal(AiOffline.ReviewLine(s, 7, out int a), AiOffline.ReviewLine(s, 7, out int b));
            Assert.Equal(a, b);
        }
        Assert.Equal(AiOffline.ReviewLine(1, 3, out _), AiOffline.ReviewLine(-5, 3, out _));
        Assert.Equal("\u2605\u2605\u2605\u2606\u2606", AiOffline.Stars(3));
    }
}
