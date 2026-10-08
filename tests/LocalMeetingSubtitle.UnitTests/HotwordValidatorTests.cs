using LocalMeetingSubtitle.Core.Hotwords;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class HotwordValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_IsRejected(string? text)
    {
        var result = HotwordValidator.Validate(text);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void TooLong_IsRejected()
    {
        var result = HotwordValidator.Validate(new string('a', HotwordValidator.MaxLength + 1));

        Assert.False(result.IsValid);
        Assert.Contains("characters", result.Message);
    }

    [Fact]
    public void MaxLength_IsAccepted()
    {
        var result = HotwordValidator.Validate(new string('a', HotwordValidator.MaxLength));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("a\tb")]   // tab
    [InlineData("a\nb")]   // line feed
    [InlineData("a\rb")]   // carriage return
    [InlineData("a\u0001b")] // other control character
    public void ControlCharacters_AreRejected(string text)
    {
        var result = HotwordValidator.Validate(text);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("DeepSeek")]
    [InlineData("深度求索")]
    [InlineData("Atlas 800I")]
    public void ValidText_IsAccepted(string text)
    {
        Assert.True(HotwordValidator.Validate(text).IsValid);
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndTrims()
    {
        Assert.Equal("a b c", HotwordValidator.Normalize("  a \t b   c  "));
        Assert.Equal("", HotwordValidator.Normalize("   "));
    }
}
