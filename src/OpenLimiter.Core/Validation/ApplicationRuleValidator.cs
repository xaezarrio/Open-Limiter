using OpenLimiter.Core.Models;

namespace OpenLimiter.Core.Validation;

public static class ApplicationRuleValidator
{
    public const int MaximumDisplayNameLength = 128;

    public const long MaximumRateBitsPerSecond = 1_000_000_000_000;

    public static IReadOnlyList<string> Validate(ApplicationRule rule, bool requireExistingExecutable = true)
    {
        var errors = new List<string>();

        if (rule.Id == Guid.Empty)
        {
            errors.Add("Rule ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(rule.DisplayName))
        {
            errors.Add("Display name is required.");
        }
        else if (rule.DisplayName.Length > MaximumDisplayNameLength)
        {
            errors.Add($"Display name cannot exceed {MaximumDisplayNameLength} characters.");
        }
        else if (rule.DisplayName.Any(char.IsControl))
        {
            errors.Add("Display name cannot contain control characters.");
        }

        if ((rule.BlockedDirections & ~TrafficDirection.Both) != 0)
        {
            errors.Add("Blocked directions contain an unsupported value.");
        }

        if (string.IsNullOrWhiteSpace(rule.ExecutablePath) || !Path.IsPathFullyQualified(rule.ExecutablePath))
        {
            errors.Add("Executable path must be absolute.");
        }
        else if (!string.Equals(Path.GetExtension(rule.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Executable path must point to an .exe file.");
        }
        else if (requireExistingExecutable && !File.Exists(rule.ExecutablePath))
        {
            errors.Add("Executable file does not exist.");
        }

        ValidateRate(rule.UploadLimitBitsPerSecond, "Upload", errors);
        ValidateRate(rule.DownloadLimitBitsPerSecond, "Download", errors);

        if (rule.BlockedDirections.HasFlag(TrafficDirection.Outbound) && rule.UploadLimitBitsPerSecond is not null)
        {
            errors.Add("Upload cannot be blocked and rate-limited at the same time.");
        }

        if (rule.BlockedDirections.HasFlag(TrafficDirection.Inbound) && rule.DownloadLimitBitsPerSecond is not null)
        {
            errors.Add("Download cannot be blocked and rate-limited at the same time.");
        }

        return errors;
    }

    private static void ValidateRate(long? value, string label, ICollection<string> errors)
    {
        if (value is <= 0)
        {
            errors.Add($"{label} limit must be greater than zero.");
        }
        else if (value > MaximumRateBitsPerSecond)
        {
            errors.Add($"{label} limit cannot exceed {MaximumRateBitsPerSecond} bit/s.");
        }
    }
}
