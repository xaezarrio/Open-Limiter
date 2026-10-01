namespace OpenLimiter.Core.Versioning;

public static class ComponentVersion
{
    public static bool MatchesRelease(string? expected, string? actual)
    {
        if (!Version.TryParse(expected, out var expectedVersion) ||
            !Version.TryParse(actual, out var actualVersion))
        {
            return false;
        }

        return expectedVersion.Major == actualVersion.Major &&
               expectedVersion.Minor == actualVersion.Minor &&
               NormalizeBuild(expectedVersion.Build) == NormalizeBuild(actualVersion.Build);
    }

    private static int NormalizeBuild(int build) => build < 0 ? 0 : build;
}
