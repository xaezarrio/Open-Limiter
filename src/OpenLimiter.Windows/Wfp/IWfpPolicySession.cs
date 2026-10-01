namespace OpenLimiter.Windows.Wfp;

public interface IWfpPolicySession : IDisposable
{
    bool IsActive { get; }

    string? Message { get; }

    bool TryActivate();
}
