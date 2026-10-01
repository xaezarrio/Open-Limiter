using OpenLimiter.Core.Parsing;

namespace OpenLimiter.Core.Tests;

public sealed class DataRateParserTests
{
    [Theory]
    [InlineData("800bps", 800)]
    [InlineData("64 kbps", 64_000)]
    [InlineData("2.5mbps", 2_500_000)]
    [InlineData("1Gbps", 1_000_000_000)]
    public void TryParse_accepts_explicit_bit_units(string text, long expected)
    {
        Assert.True(DataRateParser.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0mbps")]
    [InlineData("2 MB/s")]
    [InlineData("fast")]
    public void TryParse_rejects_ambiguous_or_invalid_values(string? text)
    {
        Assert.False(DataRateParser.TryParse(text, out _));
    }
}

