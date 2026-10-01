namespace OpenLimiter.Protocol;

public enum WfpFlowDirection : byte
{
    Outbound = 0,
    Inbound = 1,
    Unknown = byte.MaxValue,
}
