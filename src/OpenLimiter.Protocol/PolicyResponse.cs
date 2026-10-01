using OpenLimiter.Core.Models;

namespace OpenLimiter.Protocol;

public sealed record PolicyResponse
{
    public int Version { get; init; } = PolicyServiceProtocol.Version;

    public required Guid RequestId { get; init; }

    public required bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<ApplicationRule> Rules { get; init; } = [];

    public EnforcementResult? Enforcement { get; init; }

    public string? ServiceVersion { get; init; }

    public WfpDriverStatus? Driver { get; init; }

    public NetworkTrafficSnapshot? NetworkTraffic { get; init; }

    public WfpFlowSnapshot? DriverFlows { get; init; }

    public IReadOnlyList<PolicyProfileSummary> Profiles { get; init; } = [];

    public IReadOnlyList<PolicySchedule> Schedules { get; init; } = [];
}
