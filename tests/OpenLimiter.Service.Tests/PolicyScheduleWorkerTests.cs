using OpenLimiter.Core.Models;

namespace OpenLimiter.Service.Tests;

public sealed class PolicyScheduleWorkerTests
{
    [Fact]
    public void IsDue_accepts_a_delayed_run_once_on_a_matching_day()
    {
        var now = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.FromHours(7));
        var schedule = CreateSchedule() with
        {
            Days = ScheduleDays.Thursday,
            Hour = 9,
            Minute = 0,
        };

        Assert.True(PolicyScheduleWorker.IsDue(schedule, now));
        Assert.False(PolicyScheduleWorker.IsDue(schedule with { LastRunLocalDate = new DateOnly(2026, 10, 1) }, now));
    }

    [Fact]
    public void IsDue_rejects_wrong_day_future_time_and_disabled_schedule()
    {
        var now = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.FromHours(7));
        var schedule = CreateSchedule() with
        {
            Days = ScheduleDays.Friday,
            Hour = 9,
        };

        Assert.False(PolicyScheduleWorker.IsDue(schedule, now));
        Assert.False(PolicyScheduleWorker.IsDue(schedule with { Days = ScheduleDays.Thursday }, now));
        Assert.False(PolicyScheduleWorker.IsDue(schedule with { Days = ScheduleDays.Thursday, Hour = 8, Enabled = false }, now));
    }

    private static PolicySchedule CreateSchedule() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Work hours",
        ProfileId = Guid.NewGuid(),
        Days = ScheduleDays.Weekdays,
        Hour = 9,
        Minute = 0,
    };
}
