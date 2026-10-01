using OpenLimiter.Core.Models;

namespace OpenLimiter.Service.Tests;

public sealed class RuleRestorePlannerTests
{
    [Fact]
    public void Create_skips_invalid_and_duplicate_executable_rules()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.");
        var first = CreateRule(executablePath);
        var duplicate = CreateRule(executablePath.ToUpperInvariant());
        var invalid = CreateRule(executablePath) with { Id = Guid.Empty };

        var plan = RuleRestorePlanner.Create([first, duplicate, invalid]);

        Assert.Equal(first.Id, Assert.Single(plan.Rules).Id);
        Assert.Equal(2, plan.Warnings.Count);
        Assert.Contains(plan.Warnings, warning => warning.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Warnings, warning => warning.Contains("cannot be empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_ignores_disabled_rules_without_warning()
    {
        var rule = CreateRule(Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.")) with
        {
            Enabled = false,
        };

        var plan = RuleRestorePlanner.Create([rule]);

        Assert.Empty(plan.Rules);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Create_skips_null_stored_rule_with_warning()
    {
        var plan = RuleRestorePlanner.Create([null]);

        Assert.Empty(plan.Rules);
        Assert.Contains(plan.Warnings, warning => warning.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_skips_expired_rule_with_warning()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var rule = CreateRule(Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.")) with
        {
            ExpiresAtUtc = now.AddSeconds(-1),
        };

        var plan = RuleRestorePlanner.Create([rule], now);

        Assert.Empty(plan.Rules);
        Assert.Contains(plan.Warnings, warning => warning.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

    private static ApplicationRule CreateRule(string executablePath) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Test process",
        ExecutablePath = executablePath,
        BlockedDirections = TrafficDirection.Inbound,
    };
}
