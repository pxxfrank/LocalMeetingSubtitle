using LocalMeetingSubtitle.Core.Hotwords;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class ModelHotwordFileTests
{
    [Fact]
    public void ChinesePhrase_IsSplitPerCharacter()
    {
        Assert.Equal("深 度 求 索", ModelHotwordFile.FormatToken("深度求索"));
    }

    [Fact]
    public void LatinWords_StayWhole()
    {
        Assert.Equal("Atlas", ModelHotwordFile.FormatToken("Atlas"));
        Assert.Equal("DeepSeek MCP", ModelHotwordFile.FormatToken("DeepSeek MCP"));
    }

    [Fact]
    public void MixedPhrase_KeepsLatinTokensWholeAndSplitsChinese()
    {
        Assert.Equal("Atlas 800I", ModelHotwordFile.FormatToken("Atlas 800I"));
        Assert.Equal("中 文 ABC 测 试", ModelHotwordFile.FormatToken("中文 ABC 测试"));
    }

    [Fact]
    public void Punctuation_ActsAsSeparator()
    {
        Assert.Equal("DeepSeek MCP", ModelHotwordFile.FormatToken("DeepSeek,MCP"));
    }

    [Fact]
    public void Build_WritesOneFormattedPhrasePerLine()
    {
        var content = ModelHotwordFile.Build(new[] { "深度求索", "Atlas 800I" });

        Assert.Equal("深 度 求 索\nAtlas 800I\n", content);
    }
}
