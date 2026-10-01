using OpenLimiter.Core.Versioning;

namespace OpenLimiter.Core.Tests;

public sealed class ComponentVersionTests
{
    [Theory]
    [InlineData("0.4.4", "0.4.4.0")]
    [InlineData("1.2.3.7", "1.2.3.9")]
    public void Matching_release_versions_ignore_the_optional_revision(string expected, string actual)
    {
        Assert.True(ComponentVersion.MatchesRelease(expected, actual));
    }

    [Theory]
    [InlineData("0.4.4", "0.4.3")]
    [InlineData("0.4.4", "0.5.0")]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("0.4.4", null)]
    [InlineData("not-a-version", "0.4.4")]
    public void Different_or_unknown_release_versions_do_not_match(string expected, string? actual)
    {
        Assert.False(ComponentVersion.MatchesRelease(expected, actual));
    }
}
