using OpenLimiter.Core.Persistence;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

internal sealed class RuleExpirationWorker(
    IRuleStore ruleStore,
    IPolicyRequestHandler requestHandler,
    TimeProvider timeProvider,
    ILogger<RuleExpirationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExpireRulesAsync(stoppingToken);
        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExpireRulesAsync(stoppingToken);
        }
    }

    internal async Task ExpireRulesAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var expiredRuleIds = (await ruleStore.LoadAsync(cancellationToken))
            .Where(rule => rule.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= now)
            .OrderBy(rule => rule.ExpiresAtUtc)
            .Select(rule => rule.Id)
            .ToArray();

        foreach (var ruleId in expiredRuleIds)
        {
            var response = await requestHandler.HandleAsync(new()
            {
                RequestId = Guid.NewGuid(),
                Kind = PolicyRequestKind.RemoveRule,
                RuleId = ruleId,
            }, "service\\expiration", cancellationToken);

            if (response.Succeeded)
            {
                logger.LogInformation("Expired and removed policy {RuleId}.", ruleId);
            }
            else
            {
                logger.LogError(
                    "Could not remove expired policy {RuleId}: {ErrorCode} {Message}",
                    ruleId,
                    response.ErrorCode,
                    response.ErrorMessage);
            }
        }
    }
}
