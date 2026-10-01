using OpenLimiter.Core.Parsing;

namespace OpenLimiter.Core.Tests;

public sealed class RuleDurationParserTests
{
    [Theory]
    [InlineData("10m", 10)]
    [InlineData("2h", 120)]
    [InlineData("1d", 1440)]
    [InlineData(" 7D ", 10080)]
    public void TryParse_accepts_bounded_whole_unit_durations(string text, double expectedMinutes)
    {
        Assert.True(RuleDurationParser.TryParse(text, out var actual));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0m")]
    [InlineData("1.5h")]
    [InlineData("8d")]
    [InlineData("10")]
    [InlineData("forever")]
    public void TryParse_rejects_ambiguous_or_out_of_range_durations(string? text)
    {
        Assert.False(RuleDurationParser.TryParse(text, out _));
    }
}
