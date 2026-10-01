namespace OpenLimiter.Core.Models;

public sealed record ApplicationRule
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required string ExecutablePath { get; init; }

    public bool Enabled { get; init; } = true;

    public TrafficDirection BlockedDirections { get; init; }

    public long? UploadLimitBitsPerSecond { get; init; }

    public long? DownloadLimitBitsPerSecond { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
