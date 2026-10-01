using OpenLimiter.Core.Models;

namespace OpenLimiter.Protocol;

public sealed record PolicyRequest
{
    public int Version { get; init; } = PolicyServiceProtocol.Version;

    public required Guid RequestId { get; init; }

    public required PolicyRequestKind Kind { get; init; }

    public ApplicationRule? Rule { get; init; }

    public Guid? RuleId { get; init; }

    public IReadOnlyList<ApplicationRule>? Rules { get; init; }

    public PolicyProfile? Profile { get; init; }

    public PolicySchedule? Schedule { get; init; }

    public Guid? ProfileId { get; init; }

    public Guid? ScheduleId { get; init; }
}
