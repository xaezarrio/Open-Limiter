using OpenLimiter.Core.Persistence;
using OpenLimiter.Windows.Enforcement;

namespace OpenLimiter.Service;

internal static class PolicyCleanup
{
    public static async Task<IReadOnlyList<string>> RemoveAllAsync(
        IRuleStore ruleStore,
        IPolicyEnforcer enforcer,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var rule in await ruleStore.LoadAsync(cancellationToken))
        {
            var result = await enforcer.RemoveAsync(rule.Id, cancellationToken);
            errors.AddRange(result.Errors);
        }

        return errors;
    }
}
