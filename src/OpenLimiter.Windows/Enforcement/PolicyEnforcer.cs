using OpenLimiter.Core.Models;
using OpenLimiter.Core.Validation;
using OpenLimiter.Windows.Firewall;
using OpenLimiter.Windows.Qos;

namespace OpenLimiter.Windows.Enforcement;

public sealed class PolicyEnforcer(IFirewallPolicy firewallPolicy, IQosPolicy qosPolicy) : IPolicyEnforcer
{
    public async Task<EnforcementResult> ApplyAsync(ApplicationRule rule, CancellationToken cancellationToken = default)
    {
        var validationErrors = ApplicationRuleValidator.Validate(rule);
        if (validationErrors.Count > 0)
        {
            return EnforcementResult.Failure(validationErrors);
        }

        var applied = new List<string>();
        var warnings = new List<string>();

        try
        {
            firewallPolicy.Remove(rule.Id);
            await qosPolicy.RemoveAsync(rule.Id, cancellationToken);

            if (!rule.Enabled)
            {
                return EnforcementResult.Success(["Removed active policies for the disabled rule."]);
            }

            if (rule.BlockedDirections != TrafficDirection.None)
            {
                firewallPolicy.Block(rule.Id, rule.ExecutablePath, rule.BlockedDirections);
                applied.Add($"Blocked {rule.BlockedDirections.ToString().ToLowerInvariant()} traffic.");
            }

            if (rule.UploadLimitBitsPerSecond is { } uploadLimit)
            {
                await qosPolicy.ApplyUploadLimitAsync(rule.Id, rule.ExecutablePath, uploadLimit, cancellationToken);
                applied.Add($"Limited outbound traffic to {uploadLimit} bit/s.");
            }

            if (rule.DownloadLimitBitsPerSecond is not null)
            {
                warnings.Add("Download limiting requires WFP packet queues that are not implemented yet and was not applied.");
            }

            return EnforcementResult.Success(applied, warnings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return EnforcementResult.Failure([exception.Message], applied, warnings);
        }
    }

    public async Task<EnforcementResult> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        try
        {
            firewallPolicy.Remove(ruleId);
            await qosPolicy.RemoveAsync(ruleId, cancellationToken);
            return EnforcementResult.Success(["Removed firewall and QoS policies owned by this rule."]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return EnforcementResult.Failure([exception.Message]);
        }
    }
}
