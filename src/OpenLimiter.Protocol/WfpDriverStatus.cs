namespace OpenLimiter.Protocol;

public sealed record WfpDriverStatus
{
    public required bool IsAvailable { get; init; }

    public uint ApiMajor { get; init; }

    public uint ApiMinor { get; init; }

    public uint CapabilityFlags { get; init; }

    public uint RegisteredCallouts { get; init; }

    public ulong ClassificationCount { get; init; }

    public bool PolicySessionActive { get; init; }

    public string? PolicySessionMessage { get; init; }

    public string? Message { get; init; }
}
