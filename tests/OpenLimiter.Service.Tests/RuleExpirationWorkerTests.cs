using Microsoft.Extensions.Logging.Abstractions;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service.Tests;

public sealed class RuleExpirationWorkerTests
{
    [Fact]
    public async Task ExpireRules_removes_only_rules_due_at_current_time()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var expired = CreateRule(now.AddSeconds(-1));
        var due = CreateRule(now);
        var future = CreateRule(now.AddMinutes(1));
        var permanent = CreateRule(null);
        var handler = new RecordingHandler();
        var worker = new RuleExpirationWorker(
            new FakeRuleStore(expired, due, future, permanent),
            handler,
            new FixedTimeProvider(now),
            NullLogger<RuleExpirationWorker>.Instance);

        await worker.ExpireRulesAsync();

        Assert.Equal([expired.Id, due.Id], handler.RemovedRuleIds);
        Assert.All(handler.Callers, caller => Assert.Equal("service\\expiration", caller));
    }

    private static ApplicationRule CreateRule(DateTimeOffset? expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Temporary test",
        ExecutablePath = @"C:\Test\app.exe",
        BlockedDirections = TrafficDirection.Outbound,
        ExpiresAtUtc = expiresAtUtc,
    };

    private sealed class FakeRuleStore(params ApplicationRule[] rules) : IRuleStore
    {
        public Task<IReadOnlyList<ApplicationRule>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRule>>(rules);

        public Task SaveAsync(IEnumerable<ApplicationRule> savedRules, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingHandler : IPolicyRequestHandler
    {
        public List<Guid> RemovedRuleIds { get; } = [];

        public List<string> Callers { get; } = [];

        public Task<PolicyResponse> HandleAsync(
            PolicyRequest request,
            string caller,
            CancellationToken cancellationToken = default)
        {
            RemovedRuleIds.Add(Assert.IsType<Guid>(request.RuleId));
            Callers.Add(caller);
            return Task.FromResult(new PolicyResponse
            {
                Version = PolicyServiceProtocol.Version,
                RequestId = request.RequestId,
                Succeeded = true,
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
