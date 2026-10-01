using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

public interface IPolicyRequestHandler
{
    Task<PolicyResponse> HandleAsync(
        PolicyRequest request,
        string caller,
        CancellationToken cancellationToken = default);
}
