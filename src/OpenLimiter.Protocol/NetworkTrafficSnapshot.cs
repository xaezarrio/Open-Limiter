namespace OpenLimiter.Protocol;

public sealed record NetworkTrafficSnapshot
{
    public bool IsAvailable { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset SampledAtUtc { get; init; }

    public double SampleIntervalSeconds { get; init; }

    public IReadOnlyList<NetworkTrafficSample> Samples { get; init; } = [];
}
