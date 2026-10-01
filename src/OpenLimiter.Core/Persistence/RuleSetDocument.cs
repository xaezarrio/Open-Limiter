using OpenLimiter.Core.Models;

namespace OpenLimiter.Core.Persistence;

public sealed record RuleSetDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required DateTimeOffset ExportedAtUtc { get; init; }

    public required IReadOnlyList<ApplicationRule> Rules { get; init; }
}
