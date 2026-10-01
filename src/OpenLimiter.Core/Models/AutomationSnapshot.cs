namespace OpenLimiter.Core.Models;

public sealed record AutomationSnapshot
{
    public IReadOnlyList<PolicyProfileSummary> Profiles { get; init; } = [];

    public IReadOnlyList<PolicySchedule> Schedules { get; init; } = [];
}
