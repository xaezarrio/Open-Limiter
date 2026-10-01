using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenLimiter.Core.Parsing;

public static partial class RuleDurationParser
{
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(7);

    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = DurationPattern().Match(text);
        if (!match.Success ||
            !decimal.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var multiplier = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "m" => TimeSpan.TicksPerMinute,
            "h" => TimeSpan.TicksPerHour,
            "d" => TimeSpan.TicksPerDay,
            _ => 0,
        };
        var ticks = value * multiplier;
        if (ticks <= 0 || ticks > MaximumDuration.Ticks || decimal.Truncate(ticks) != ticks)
        {
            return false;
        }

        duration = TimeSpan.FromTicks(decimal.ToInt64(ticks));
        return true;
    }

    [GeneratedRegex(@"^\s*(?<value>\d+)\s*(?<unit>m|h|d)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
