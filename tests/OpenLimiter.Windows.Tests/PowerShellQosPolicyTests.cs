using OpenLimiter.Windows.Qos;

namespace OpenLimiter.Windows.Tests;

public sealed class PowerShellQosPolicyTests
{
    [Fact]
    public async Task Removing_an_unknown_QoS_policy_is_idempotent()
    {
        await new PowerShellQosPolicy().RemoveAsync(
            Guid.NewGuid(),
            CancellationToken.None);
    }

    [Fact]
    public async Task PowerShell_failure_is_reported_as_plain_text()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PowerShellQosPolicy.RunPowerShellAsync(
                "Write-Progress -Activity 'OpenLimiter test' -Status 'Working'; Write-Error 'QoS probe failure'; exit 1",
                CancellationToken.None));

        Assert.Contains("QoS probe failure", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIXML", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<Objs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malformed_CliXml_is_not_exposed_to_the_user()
    {
        var message = PowerShellQosPolicy.FormatPowerShellError("#< CLIXML\n<Objs>", 1);

        Assert.Equal("PowerShell exited with code 1 and returned an unreadable error.", message);
    }

    [Fact]
    public async Task Cancellation_stops_policy_process_before_it_can_continue()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"openlimiter-qos-cancel-{Guid.NewGuid():N}.txt");
        var escapedMarkerPath = markerPath.Replace("'", "''", StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PowerShellQosPolicy.RunPowerShellAsync(
                    $"Start-Sleep -Seconds 2; Set-Content -LiteralPath '{escapedMarkerPath}' -Value 'unexpected'",
                    cancellation.Token));

            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            File.Delete(markerPath);
        }
    }
}
