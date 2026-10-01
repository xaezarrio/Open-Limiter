namespace OpenLimiter.Protocol;

public sealed record WfpFlowSnapshot
{
    public required bool IsAvailable { get; init; }

    public ulong LatestSequence { get; init; }

    public bool PolicySessionActive { get; init; }

    public IReadOnlyList<WfpFlowEvent> Events { get; init; } = [];

    public string? Message { get; init; }
}
