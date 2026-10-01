using OpenLimiter.Core.Models;
using OpenLimiter.Core.Parsing;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

internal sealed class PolicyScheduleWorker(
    IAutomationStore automationStore,
    IPolicyRequestHandler requestHandler,
    TimeProvider timeProvider,
    ILogger<PolicyScheduleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunDueSchedulesAsync(stoppingToken);
        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunDueSchedulesAsync(stoppingToken);
        }
    }

    internal async Task RunDueSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetLocalNow();
        var configuration = await automationStore.LoadAsync(cancellationToken);
        foreach (var schedule in configuration.Schedules.Where(schedule => IsDue(schedule, now)))
        {
            var response = await requestHandler.HandleAsync(new()
            {
                RequestId = Guid.NewGuid(),
                Kind = PolicyRequestKind.RunSchedule,
                ScheduleId = schedule.Id,
            }, "service\\scheduler", cancellationToken);

            if (response.Succeeded)
            {
                logger.LogInformation("Activated scheduled profile for {ScheduleName} ({ScheduleId}).", schedule.Name, schedule.Id);
            }
            else
            {
                logger.LogError(
                    "Scheduled profile activation failed for {ScheduleName} ({ScheduleId}): {ErrorCode} {Message}",
                    schedule.Name,
                    schedule.Id,
                    response.ErrorCode,
                    response.ErrorMessage);
            }
        }
    }

    internal static bool IsDue(PolicySchedule schedule, DateTimeOffset localNow)
    {
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var today = ScheduleDaysParser.FromDayOfWeek(localNow.DayOfWeek);
        return schedule.Enabled &&
            (schedule.Days & today) != 0 &&
            schedule.LastRunLocalDate != localDate &&
            localNow.TimeOfDay >= new TimeSpan(schedule.Hour, schedule.Minute, 0);
    }
}
