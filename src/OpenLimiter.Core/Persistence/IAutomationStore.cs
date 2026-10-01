namespace OpenLimiter.Core.Persistence;

public interface IAutomationStore
{
    Task<AutomationConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AutomationConfiguration configuration, CancellationToken cancellationToken = default);
}
