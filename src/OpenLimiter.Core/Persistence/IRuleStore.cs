using OpenLimiter.Core.Models;

namespace OpenLimiter.Core.Persistence;

public interface IRuleStore
{
    Task<IReadOnlyList<ApplicationRule>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IEnumerable<ApplicationRule> rules, CancellationToken cancellationToken = default);
}

