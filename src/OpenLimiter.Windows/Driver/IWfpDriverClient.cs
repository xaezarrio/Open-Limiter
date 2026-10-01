using OpenLimiter.Protocol;

namespace OpenLimiter.Windows.Driver;

public interface IWfpDriverClient
{
    WfpDriverStatus GetStatus();

    WfpFlowSnapshot GetFlowEvents();
}
