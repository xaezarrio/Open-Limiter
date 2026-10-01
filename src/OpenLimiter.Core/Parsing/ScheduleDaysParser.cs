using OpenLimiter.Core.Models;

namespace OpenLimiter.Core.Parsing;

public static class ScheduleDaysParser
{
    private static readonly IReadOnlyDictionary<string, ScheduleDays> Values =
        new Dictionary<string, ScheduleDays>(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"] = ScheduleDays.Monday,
            ["tue"] = ScheduleDays.Tuesday,
            ["wed"] = ScheduleDays.Wednesday,
            ["thu"] = ScheduleDays.Thursday,
            ["fri"] = ScheduleDays.Friday,
            ["sat"] = ScheduleDays.Saturday,
            ["sun"] = ScheduleDays.Sunday,
        };

    public static bool TryParse(string? text, out ScheduleDays days)
    {
        days = ScheduleDays.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (string.Equals(text.Trim(), "daily", StringComparison.OrdinalIgnoreCase))
        {
            days = ScheduleDays.EveryDay;
            return true;
        }
        if (string.Equals(text.Trim(), "weekdays", StringComparison.OrdinalIgnoreCase))
        {
            days = ScheduleDays.Weekdays;
            return true;
        }
        if (string.Equals(text.Trim(), "weekend", StringComparison.OrdinalIgnoreCase))
        {
            days = ScheduleDays.Weekend;
            return true;
        }

        foreach (var token in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Values.TryGetValue(token, out var value))
            {
                days = ScheduleDays.None;
                return false;
            }

            days |= value;
        }

        return days != ScheduleDays.None;
    }

    public static ScheduleDays FromDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => ScheduleDays.Monday,
        DayOfWeek.Tuesday => ScheduleDays.Tuesday,
        DayOfWeek.Wednesday => ScheduleDays.Wednesday,
        DayOfWeek.Thursday => ScheduleDays.Thursday,
        DayOfWeek.Friday => ScheduleDays.Friday,
        DayOfWeek.Saturday => ScheduleDays.Saturday,
        DayOfWeek.Sunday => ScheduleDays.Sunday,
        _ => ScheduleDays.None,
    };
}
