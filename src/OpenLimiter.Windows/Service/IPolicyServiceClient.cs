using OpenLimiter.Core.Models;
using OpenLimiter.Protocol;

namespace OpenLimiter.Windows.Service;

public interface IPolicyServiceClient
{
    Task<PolicyResponse> PingAsync(CancellationToken cancellationToken = default);

    Task<NetworkTrafficSnapshot> GetNetworkTrafficAsync(CancellationToken cancellationToken = default);

    Task<WfpFlowSnapshot> GetDriverFlowsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationRule>> ListRulesAsync(CancellationToken cancellationToken = default);

    Task<EnforcementResult> ApplyAsync(ApplicationRule rule, CancellationToken cancellationToken = default);

    Task<EnforcementResult> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default);

    Task<EnforcementResult> ClearAsync(CancellationToken cancellationToken = default);

    Task<EnforcementResult> ReplaceRulesAsync(
        IReadOnlyList<ApplicationRule> rules,
        CancellationToken cancellationToken = default);

    Task<AutomationSnapshot> GetAutomationAsync(CancellationToken cancellationToken = default);

    Task<AutomationSnapshot> SaveProfileAsync(PolicyProfile profile, CancellationToken cancellationToken = default);

    Task<AutomationSnapshot> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<EnforcementResult> ActivateProfileAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<AutomationSnapshot> SaveScheduleAsync(PolicySchedule schedule, CancellationToken cancellationToken = default);

    Task<AutomationSnapshot> DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);
}
