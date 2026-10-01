using OpenLimiter.Core.Models;
using OpenLimiter.Core.Validation;

namespace OpenLimiter.Service;

internal static class RuleRestorePlanner
{
    public static RuleRestorePlan Create(IEnumerable<ApplicationRule?> storedRules, DateTimeOffset? nowUtc = null)
    {
        var currentTime = nowUtc ?? DateTimeOffset.UtcNow;
        var rules = new List<ApplicationRule>();
        var warnings = new List<string>();
        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var storedRule in storedRules)
        {
            if (storedRule is null)
            {
                warnings.Add("Skipped a null stored rule.");
                continue;
            }
            if (!storedRule.Enabled)
            {
                continue;
            }
            if (storedRule.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= currentTime)
            {
                warnings.Add($"Skipped expired stored rule {storedRule.Id}.");
                continue;
            }

            var validationErrors = ApplicationRuleValidator.Validate(storedRule, requireExistingExecutable: false);
            if (validationErrors.Count > 0)
            {
                warnings.Add($"Skipped stored rule {storedRule.Id}: {string.Join(" ", validationErrors)}");
                continue;
            }

            ApplicationRule normalizedRule;
            try
            {
                normalizedRule = storedRule with
                {
                    DisplayName = storedRule.DisplayName.Trim(),
                    ExecutablePath = Path.GetFullPath(storedRule.ExecutablePath),
                };
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warnings.Add($"Skipped stored rule {storedRule.Id}: executable path is invalid: {exception.Message}");
                continue;
            }
            if (!executablePaths.Add(normalizedRule.ExecutablePath))
            {
                warnings.Add($"Skipped duplicate stored rule {storedRule.Id} for '{normalizedRule.ExecutablePath}'.");
                continue;
            }

            rules.Add(normalizedRule);
        }

        return new(rules, warnings);
    }
}

internal sealed record RuleRestorePlan(
    IReadOnlyList<ApplicationRule> Rules,
    IReadOnlyList<string> Warnings);
