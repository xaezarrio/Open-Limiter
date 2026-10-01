using OpenLimiter.Core.Models;

namespace OpenLimiter.Core.Persistence;

public sealed record AutomationConfiguration
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<PolicyProfile> Profiles { get; init; } = [];

    public IReadOnlyList<PolicySchedule> Schedules { get; init; } = [];
}
