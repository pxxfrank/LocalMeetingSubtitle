using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class TextCorrectionEngineTests
{
    private readonly TextCorrectionEngine _engine = new();

    [Fact]
    public void WholeTokenRule_DoesNotCorruptLargerWord()
    {
        var rule = TextCorrectionRule.Create("MCP", "MCP-Corrected"); // whole-token by default

        var result = _engine.Apply("MCPX and MCP here", new[] { rule });

        Assert.Equal("MCPX and MCP-Corrected here", result.Text);
    }

    [Fact]
    public void ChinesePhrase_IsReplacedInsideSentence()
    {
        var rule = new TextCorrectionRule { Pattern = "深度求索", Replacement = "DeepSeek", WholeTokenOnly = false };

        var result = _engine.Apply("我们使用深度求索模型", new[] { rule });

        Assert.Equal("我们使用DeepSeek模型", result.Text);
    }

    [Fact]
    public void ChinesePhrase_StandaloneWholeToken_IsReplaced()
    {
        var rule = TextCorrectionRule.Create("深度求索", "DeepSeek");

        Assert.Equal("DeepSeek", _engine.Apply("深度求索", new[] { rule }).Text);
    }

    [Fact]
    public void SpacedAcronym_IsJoined()
    {
        var rule = TextCorrectionRule.Create("M C P", "MCP");

        Assert.Equal("MCP", _engine.Apply("M C P", new[] { rule }).Text);
        Assert.Equal("say MCP now", _engine.Apply("say M C P now", new[] { rule }).Text);
    }

    [Fact]
    public void PriorityOrder_DeterminesChainedResult()
    {
        var lower = new TextCorrectionRule { Pattern = "B", Replacement = "C", Priority = 0 };
        var higher = new TextCorrectionRule { Pattern = "A", Replacement = "B", Priority = 1 };

        // Higher priority runs first: A -> B, then B -> C.
        var result = _engine.Apply("A", new[] { lower, higher }, collectApplied: true);

        Assert.Equal("C", result.Text);
        Assert.Equal(2, result.AppliedRules.Count);
    }

    [Fact]
    public void EqualPriority_KeepsInsertionOrder()
    {
        var first = new TextCorrectionRule { Pattern = "x", Replacement = "1", Priority = 0 };
        var second = new TextCorrectionRule { Pattern = "1", Replacement = "2", Priority = 0 };

        Assert.Equal("2", _engine.Apply("x", new[] { first, second }).Text);
    }

    [Fact]
    public void RegexRule_InvalidPattern_IsIgnored()
    {
        var rule = new TextCorrectionRule { Pattern = "(", Replacement = "", IsRegex = true };

        var result = _engine.Apply("keep (", new[] { rule });

        Assert.Equal("keep (", result.Text);
    }

    [Fact]
    public void RegexRule_ValidPattern_IsApplied()
    {
        var rule = new TextCorrectionRule { Pattern = @"\d+", Replacement = "#", IsRegex = true };

        Assert.Equal("call #", _engine.Apply("call 123", new[] { rule }).Text);
    }

    [Fact]
    public void RunawayProtection_AppliesRuleOnce()
    {
        var grow = new TextCorrectionRule { Pattern = "a", Replacement = "aa", WholeTokenOnly = false };

        Assert.Equal("aa", _engine.Apply("a", new[] { grow }).Text);
    }

    [Fact]
    public void RunawayProtection_CapsReplacementsPerRule()
    {
        var rule = new TextCorrectionRule { Pattern = "x", Replacement = "y", WholeTokenOnly = false };

        var result = _engine.Apply(new string('x', 100), new[] { rule });

        Assert.Equal(64, result.Text.Count(c => c == 'y'));
        Assert.Equal(36, result.Text.Count(c => c == 'x'));
    }

    [Fact]
    public void CaseSensitiveRule_DoesNotMatchDifferentCase()
    {
        var rule = new TextCorrectionRule { Pattern = "MCP", Replacement = "MCP!", CaseSensitive = true };

        Assert.Equal("mcp stays", _engine.Apply("mcp stays", new[] { rule }).Text);
        Assert.Equal("MCP!", _engine.Apply("MCP", new[] { rule }).Text);
    }

    [Fact]
    public void DisabledRules_AreSkipped()
    {
        var rule = new TextCorrectionRule { Pattern = "MCP", Replacement = "MCP!", Enabled = false };

        Assert.Equal("MCP", _engine.Apply("MCP", new[] { rule }).Text);
    }

    [Fact]
    public void EmptyInputOrNoRules_ReturnsInputUnchanged()
    {
        Assert.Equal("", _engine.Apply("", Array.Empty<TextCorrectionRule>()).Text);
        Assert.Equal("MCP", _engine.Apply("MCP", Array.Empty<TextCorrectionRule>()).Text);
    }
}
