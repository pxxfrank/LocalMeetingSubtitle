using LocalMeetingSubtitle.Core.Hotwords;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class HotwordFileParserTests
{
    [Fact]
    public void ParseTxt_IgnoresCommentsAndBlankLines()
    {
        const string content = "# a comment\n\n   \nDeepSeek\n  MCP  \n";

        var result = HotwordFileParser.ParseTxt(content);

        Assert.Equal(new[] { "DeepSeek", "MCP" }, result.Hotwords.Select(h => h.Text));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ParseTxt_SupportsTrailingScore()
    {
        var result = HotwordFileParser.ParseTxt("MCP:2.5\nplain\n");

        Assert.Equal(2, result.Hotwords.Count);
        Assert.Equal("MCP", result.Hotwords[0].Text);
        Assert.Equal(2.5f, result.Hotwords[0].Score, 3);
        Assert.Equal("plain", result.Hotwords[1].Text);
        Assert.Equal(1.5f, result.Hotwords[1].Score, 3);
    }

    [Fact]
    public void ParseTxt_KeepsColonInsideNonNumericPhrase()
    {
        var result = HotwordFileParser.ParseTxt("a:b\n");

        var hotword = Assert.Single(result.Hotwords);
        Assert.Equal("a:b", hotword.Text);
    }

    [Fact]
    public void ParseTxt_ReportsDuplicateWarnings()
    {
        var result = HotwordFileParser.ParseTxt("foo\nfoo\n");

        Assert.Single(result.Hotwords);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("duplicate", warning);
    }

    [Fact]
    public void ParseTxt_ReportsInvalidWarnings()
    {
        var tooLong = new string('a', 100);

        var result = HotwordFileParser.ParseTxt(tooLong + "\nok\n");

        Assert.Single(result.Hotwords);
        Assert.Equal("ok", result.Hotwords[0].Text);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("skipped", warning);
    }

    [Fact]
    public void ParseCsv_SkipsHeaderRow()
    {
        const string csv = "text,enabled,score\nMCP,true,2.0\nDeepSeek,false,1.2\n";

        var result = HotwordFileParser.ParseCsv(csv);

        Assert.Equal(2, result.Hotwords.Count);
        Assert.Equal("MCP", result.Hotwords[0].Text);
        Assert.True(result.Hotwords[0].Enabled);
        Assert.Equal(2.0f, result.Hotwords[0].Score, 3);
        Assert.Equal("DeepSeek", result.Hotwords[1].Text);
        Assert.False(result.Hotwords[1].Enabled);
    }

    [Fact]
    public void ParseCsv_HandlesQuotedFieldContainingComma()
    {
        const string csv = "text,enabled,score\n\"MCP, Inc\",true,2.0\n";

        var result = HotwordFileParser.ParseCsv(csv);

        var hotword = Assert.Single(result.Hotwords);
        Assert.Equal("MCP, Inc", hotword.Text);
        Assert.Equal(2.0f, hotword.Score, 3);
    }

    [Fact]
    public void ParseCsv_HandlesEscapedQuotes()
    {
        const string csv = "text,enabled,score\n\"say \"\"hi\"\"\",true,1.0\n";

        var result = HotwordFileParser.ParseCsv(csv);

        var hotword = Assert.Single(result.Hotwords);
        Assert.Equal("say \"hi\"", hotword.Text);
    }

    [Fact]
    public void ParseCsv_ReportsDuplicates()
    {
        const string csv = "text,enabled,score\nMCP,true,2.0\nmcp,true,2.0\n";

        var result = HotwordFileParser.ParseCsv(csv);

        Assert.Single(result.Hotwords);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("duplicate", warning);
    }
}
