using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenLimiter.Core.Parsing;

public static partial class DataRateParser
{
    private static readonly IReadOnlyDictionary<string, decimal> Multipliers = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["bps"] = 1m,
        ["kbps"] = 1_000m,
        ["mbps"] = 1_000_000m,
        ["gbps"] = 1_000_000_000m,
    };

    public static bool TryParse(string? text, out long bitsPerSecond)
    {
        bitsPerSecond = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = RatePattern().Match(text);
        if (!match.Success ||
            !decimal.TryParse(match.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
            !Multipliers.TryGetValue(match.Groups["unit"].Value, out var multiplier))
        {
            return false;
        }

        var result = value * multiplier;
        if (result is <= 0 or > long.MaxValue || decimal.Truncate(result) != result)
        {
            return false;
        }

        bitsPerSecond = decimal.ToInt64(result);
        return true;
    }

    [GeneratedRegex(@"^\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>bps|kbps|mbps|gbps)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RatePattern();
}

