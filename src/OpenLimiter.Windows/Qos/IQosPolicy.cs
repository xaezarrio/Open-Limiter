namespace OpenLimiter.Windows.Qos;

public interface IQosPolicy
{
    Task ApplyUploadLimitAsync(Guid ruleId, string executablePath, long bitsPerSecond, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default);
}

