using OpenLimiter.Core.Models;
using OpenLimiter.Core.Parsing;

namespace OpenLimiter.Core.Tests;

public sealed class ScheduleDaysParserTests
{
    [Theory]
    [InlineData("daily", ScheduleDays.EveryDay)]
    [InlineData("weekdays", ScheduleDays.Weekdays)]
    [InlineData("weekend", ScheduleDays.Weekend)]
    [InlineData("Mon,Wed,Fri", ScheduleDays.Monday | ScheduleDays.Wednesday | ScheduleDays.Friday)]
    public void TryParse_accepts_named_day_sets(string text, ScheduleDays expected)
    {
        Assert.True(ScheduleDaysParser.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Monday")]
    [InlineData("Mon,Funday")]
    public void TryParse_rejects_unknown_day_sets(string? text)
    {
        Assert.False(ScheduleDaysParser.TryParse(text, out _));
    }
}
