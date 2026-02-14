using System.Text.RegularExpressions;

namespace GatherWin.Tests.Helpers;

/// <summary>
/// Tests for MarkdownHelper regex patterns.
/// Note: Full rendering tests would require WPF STA thread and Application context.
/// These tests focus on the regex patterns that drive parsing.
/// </summary>
public class MarkdownHelperTests
{
    // Recreate the patterns from MarkdownHelper since they're private
    private static readonly Regex HeadingPattern = new(
        @"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex InlinePattern = new(
        @"(\*\*(.+?)\*\*)" +
        @"|(__(.+?)__)" +
        @"|(\*(.+?)\*)" +
        @"|(_([^_]+?)_)" +
        @"|(`(.+?)`)" +
        @"|(\[(.+?)\]\((.+?)\))",
        RegexOptions.Compiled);

    [Theory]
    [InlineData("# Heading 1", 1, "Heading 1")]
    [InlineData("## Heading 2", 2, "Heading 2")]
    [InlineData("### Heading 3", 3, "Heading 3")]
    [InlineData("#### Heading 4", 4, "Heading 4")]
    [InlineData("###### Heading 6", 6, "Heading 6")]
    public void HeadingPattern_MatchesHeadings(string input, int expectedLevel, string expectedText)
    {
        var match = HeadingPattern.Match(input);
        Assert.True(match.Success);
        Assert.Equal(expectedLevel, match.Groups[1].Value.Length);
        Assert.Equal(expectedText, match.Groups[2].Value);
    }

    [Theory]
    [InlineData("no heading here")]
    [InlineData("#no space")]
    [InlineData("text # heading")]
    public void HeadingPattern_DoesNotMatchNonHeadings(string input)
    {
        var match = HeadingPattern.Match(input);
        Assert.False(match.Success);
    }

    [Fact]
    public void InlinePattern_MatchesBold()
    {
        var match = InlinePattern.Match("**bold text**");
        Assert.True(match.Success);
        Assert.Equal("bold text", match.Groups[2].Value);
    }

    [Fact]
    public void InlinePattern_MatchesItalic()
    {
        var match = InlinePattern.Match("*italic text*");
        Assert.True(match.Success);
        Assert.Equal("italic text", match.Groups[6].Value);
    }

    [Fact]
    public void InlinePattern_MatchesCode()
    {
        var match = InlinePattern.Match("`code`");
        Assert.True(match.Success);
        Assert.Equal("code", match.Groups[10].Value);
    }

    [Fact]
    public void InlinePattern_MatchesLink()
    {
        var match = InlinePattern.Match("[click here](https://example.com)");
        Assert.True(match.Success);
        Assert.Equal("click here", match.Groups[12].Value);
        Assert.Equal("https://example.com", match.Groups[13].Value);
    }

    [Fact]
    public void InlinePattern_MatchesUnderscoreBold()
    {
        var match = InlinePattern.Match("__bold__");
        Assert.True(match.Success);
        Assert.Equal("bold", match.Groups[4].Value);
    }

    [Fact]
    public void InlinePattern_MatchesMultipleInlines()
    {
        var matches = InlinePattern.Matches("**bold** and *italic* and `code`");
        Assert.Equal(3, matches.Count);
    }
}
