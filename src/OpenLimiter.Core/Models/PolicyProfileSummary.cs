namespace OpenLimiter.Core.Models;

public sealed record PolicyProfileSummary
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public int RuleCount { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
