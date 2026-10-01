using OpenLimiter.Core.Models;
using OpenLimiter.Windows.Enforcement;
using OpenLimiter.Windows.Firewall;
using OpenLimiter.Windows.Qos;

namespace OpenLimiter.Windows.Tests;

public sealed class PolicyEnforcerTests
{
    [Fact]
    public async Task Apply_reports_download_limit_as_unsupported()
    {
        var firewall = new FakeFirewallPolicy();
        var qos = new FakeQosPolicy();
        var rule = ValidRule() with { DownloadLimitBitsPerSecond = 2_000_000 };

        var result = await new PolicyEnforcer(firewall, qos).ApplyAsync(rule);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, warning => warning.Contains("WFP packet queues", StringComparison.Ordinal));
        Assert.Equal(1, firewall.RemoveCount);
        Assert.Equal(1, qos.RemoveCount);
        Assert.Equal(0, qos.ApplyCount);
    }

    [Fact]
    public async Task Apply_sends_block_and_upload_to_their_backends()
    {
        var firewall = new FakeFirewallPolicy();
        var qos = new FakeQosPolicy();
        var rule = ValidRule() with
        {
            BlockedDirections = TrafficDirection.Inbound,
            UploadLimitBitsPerSecond = 2_000_000,
        };

        var result = await new PolicyEnforcer(firewall, qos).ApplyAsync(rule);

        Assert.True(result.Succeeded);
        Assert.Equal(TrafficDirection.Inbound, firewall.LastDirection);
        Assert.Equal(2_000_000, qos.LastBitsPerSecond);
    }

    [Fact]
    public async Task Disabled_rule_removes_policies_without_applying_new_ones()
    {
        var firewall = new FakeFirewallPolicy();
        var qos = new FakeQosPolicy();
        var rule = ValidRule() with { Enabled = false, BlockedDirections = TrafficDirection.Both };

        var result = await new PolicyEnforcer(firewall, qos).ApplyAsync(rule);

        Assert.True(result.Succeeded);
        Assert.Equal(1, firewall.RemoveCount);
        Assert.Equal(0, firewall.BlockCount);
        Assert.Equal(1, qos.RemoveCount);
        Assert.Equal(0, qos.ApplyCount);
    }

    private static ApplicationRule ValidRule() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Test host",
        ExecutablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test host path is unavailable."),
    };

    private sealed class FakeFirewallPolicy : IFirewallPolicy
    {
        public int BlockCount { get; private set; }

        public int RemoveCount { get; private set; }

        public TrafficDirection LastDirection { get; private set; }

        public void Block(Guid ruleId, string executablePath, TrafficDirection direction)
        {
            BlockCount++;
            LastDirection = direction;
        }

        public void Remove(Guid ruleId) => RemoveCount++;
    }

    private sealed class FakeQosPolicy : IQosPolicy
    {
        public int ApplyCount { get; private set; }

        public int RemoveCount { get; private set; }

        public long? LastBitsPerSecond { get; private set; }

        public Task ApplyUploadLimitAsync(Guid ruleId, string executablePath, long bitsPerSecond, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastBitsPerSecond = bitsPerSecond;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            return Task.CompletedTask;
        }
    }
}
