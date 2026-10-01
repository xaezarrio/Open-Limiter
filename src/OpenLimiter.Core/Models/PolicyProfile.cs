namespace OpenLimiter.Core.Models;

public sealed record PolicyProfile
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<ApplicationRule> Rules { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
