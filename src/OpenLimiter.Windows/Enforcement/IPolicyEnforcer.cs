using OpenLimiter.Core.Models;

namespace OpenLimiter.Windows.Enforcement;

public interface IPolicyEnforcer
{
    Task<EnforcementResult> ApplyAsync(ApplicationRule rule, CancellationToken cancellationToken = default);

    Task<EnforcementResult> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default);
}
