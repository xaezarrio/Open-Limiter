namespace OpenLimiter.Cli.Tests;

public sealed class OpenLimiterCommandTests
{
    [Fact]
    public async Task Unknown_command_returns_usage_exit_code()
    {
        var exitCode = await OpenLimiterCommand.RunAsync(["unknown-command"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Optionless_command_rejects_extra_arguments()
    {
        var exitCode = await OpenLimiterCommand.RunAsync(["processes", "--unexpected", "value"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Connections_rejects_unknown_view_before_enumeration()
    {
        var exitCode = await OpenLimiterCommand.RunAsync(["connections", "--view", "invalid"]);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task Rule_state_command_rejects_invalid_rule_id_before_contacting_service(string command)
    {
        var exitCode = await OpenLimiterCommand.RunAsync([command, "--id", "not-a-guid"]);

        Assert.Equal(2, exitCode);
    }
}
