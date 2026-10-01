using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Windows.Enforcement;

namespace OpenLimiter.Service.Tests;

public sealed class PolicyCleanupTests
{
    [Fact]
    public async Task RemoveAll_attempts_every_saved_rule_and_returns_failures()
    {
        var first = CreateRule();
        var second = CreateRule();
        var enforcer = new FakeEnforcer(second.Id);

        var errors = await PolicyCleanup.RemoveAllAsync(new FakeRuleStore(first, second), enforcer);

        Assert.Equal([first.Id, second.Id], enforcer.RemovedIds);
        Assert.Equal("simulated cleanup failure", Assert.Single(errors));
    }

    private static ApplicationRule CreateRule() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Cleanup test",
        ExecutablePath = @"C:\Test\app.exe",
        BlockedDirections = TrafficDirection.Outbound,
    };

    private sealed class FakeRuleStore(params ApplicationRule[] rules) : IRuleStore
    {
        public Task<IReadOnlyList<ApplicationRule>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRule>>(rules);

        public Task SaveAsync(IEnumerable<ApplicationRule> savedRules, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeEnforcer(Guid failureId) : IPolicyEnforcer
    {
        public List<Guid> RemovedIds { get; } = [];

        public Task<EnforcementResult> ApplyAsync(ApplicationRule rule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EnforcementResult> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
        {
            RemovedIds.Add(ruleId);
            return Task.FromResult(ruleId == failureId
                ? EnforcementResult.Failure(["simulated cleanup failure"])
                : EnforcementResult.Success());
        }
    }
}
