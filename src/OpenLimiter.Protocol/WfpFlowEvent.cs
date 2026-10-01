namespace OpenLimiter.Protocol;

public sealed record WfpFlowEvent(
    ulong Sequence,
    ulong ProcessId,
    ulong ApplicationIdHash,
    uint IpVersion,
    byte IpProtocol,
    WfpFlowDirection Direction,
    string? LocalAddress = null,
    ushort LocalPort = 0,
    string? RemoteAddress = null,
    ushort RemotePort = 0,
    Guid? RuleId = null,
    string? RuleDisplayName = null);
