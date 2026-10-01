namespace OpenLimiter.Core.Models;

public sealed record PolicySchedule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Guid ProfileId { get; init; }

    public ScheduleDays Days { get; init; }

    public int Hour { get; init; }

    public int Minute { get; init; }

    public bool Enabled { get; init; } = true;

    public DateOnly? LastRunLocalDate { get; init; }
}
