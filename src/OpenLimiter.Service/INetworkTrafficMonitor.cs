using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

public interface INetworkTrafficMonitor
{
    NetworkTrafficSnapshot GetSnapshot();
}
