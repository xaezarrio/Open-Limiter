using System.Security.Principal;

namespace OpenLimiter.Service.Tests;

public sealed class ServiceSettingsTests
{
    private static readonly string CurrentUserSid =
        WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("The test process has no Windows user SID.");

    [Fact]
    public void Load_accepts_explicit_safe_settings()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"openlimiter-settings-{Guid.NewGuid():N}");

        var settings = ServiceSettings.Load(
        [
            "--data-directory", dataDirectory,
            "--pipe-name", "OpenLimiter.Test.Safe",
            "--allowed-user-sid", CurrentUserSid,
        ]);

        Assert.Equal(Path.GetFullPath(dataDirectory), settings.DataDirectory);
        Assert.Equal("OpenLimiter.Test.Safe", settings.PipeName);
        Assert.Equal(CurrentUserSid, settings.AllowedUserSid.Value);
    }

    [Fact]
    public void Load_rejects_duplicate_options()
    {
        var exception = Assert.Throws<ArgumentException>(() => ServiceSettings.Load(
        [
            "--allowed-user-sid", CurrentUserSid,
            "--pipe-name", "first",
            "--PIPE-NAME", "second",
        ]));

        Assert.Contains("only be specified once", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_rejects_empty_data_directory(string dataDirectory)
    {
        Assert.Throws<ArgumentException>(() => ServiceSettings.Load(
        [
            "--data-directory", dataDirectory,
            "--allowed-user-sid", CurrentUserSid,
        ]));
    }

    [Fact]
    public void Load_rejects_control_characters_in_pipe_name()
    {
        Assert.Throws<ArgumentException>(() => ServiceSettings.Load(
        [
            "--pipe-name", "OpenLimiter\nInjected",
            "--allowed-user-sid", CurrentUserSid,
        ]));
    }
}
