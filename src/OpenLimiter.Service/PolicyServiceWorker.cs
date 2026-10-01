using OpenLimiter.Core.Persistence;
using OpenLimiter.Windows.Enforcement;
using OpenLimiter.Windows.Wfp;

namespace OpenLimiter.Service;

public sealed class PolicyServiceWorker(
    ServiceSettings settings,
    IRuleStore ruleStore,
    IPolicyEnforcer enforcer,
    IWfpPolicySession wfpPolicySession,
    PolicyPipeServer pipeServer,
    ILogger<PolicyServiceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan WfpRetryInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(settings.DataDirectory);
        using var wfpMaintenanceSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var wfpMaintenanceTask = MaintainWfpPolicySessionAsync(wfpMaintenanceSource.Token);

        try
        {
            await RestoreRulesAsync(stoppingToken);
            await pipeServer.RunAsync(stoppingToken);
        }
        finally
        {
            await wfpMaintenanceSource.CancelAsync();
            try
            {
                await wfpMaintenanceTask;
            }
            catch (OperationCanceledException) when (wfpMaintenanceSource.IsCancellationRequested)
            {
            }
        }
    }

    private async Task MaintainWfpPolicySessionAsync(CancellationToken cancellationToken)
    {
        string? lastFailure = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (wfpPolicySession.TryActivate())
            {
                logger.LogInformation("{Message}", wfpPolicySession.Message);
                return;
            }

            if (!string.Equals(lastFailure, wfpPolicySession.Message, StringComparison.Ordinal))
            {
                logger.LogWarning("WFP flow inspection is inactive: {Message}", wfpPolicySession.Message);
                lastFailure = wfpPolicySession.Message;
            }
            else
            {
                logger.LogDebug("WFP flow inspection remains inactive: {Message}", wfpPolicySession.Message);
            }

            await Task.Delay(WfpRetryInterval, cancellationToken);
        }
    }

    private async Task RestoreRulesAsync(CancellationToken cancellationToken)
    {
        var plan = RuleRestorePlanner.Create(await ruleStore.LoadAsync(cancellationToken));
        foreach (var warning in plan.Warnings)
        {
            logger.LogWarning("{Warning}", warning);
        }

        foreach (var rule in plan.Rules)
        {
            var result = await enforcer.ApplyAsync(rule, cancellationToken);
            if (result.Succeeded)
            {
                logger.LogInformation("Restored policy {RuleId} for {DisplayName}.", rule.Id, rule.DisplayName);
            }
            else
            {
                logger.LogError("Could not restore policy {RuleId}: {Errors}", rule.Id, string.Join(" ", result.Errors));
            }
        }
    }
}
