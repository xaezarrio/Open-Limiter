using OpenLimiter.Core.Models;
using OpenLimiter.Core.Validation;

namespace OpenLimiter.Core.Tests;

public sealed class ApplicationRuleValidatorTests
{
    [Fact]
    public void Validate_rejects_conflicting_outbound_controls()
    {
        var rule = ValidRule() with
        {
            BlockedDirections = TrafficDirection.Outbound,
            UploadLimitBitsPerSecond = 1_000_000,
        };

        var errors = ApplicationRuleValidator.Validate(rule, requireExistingExecutable: false);

        Assert.Contains("Upload cannot be blocked and rate-limited at the same time.", errors);
    }

    [Fact]
    public void Validate_rejects_conflicting_inbound_controls()
    {
        var rule = ValidRule() with
        {
            BlockedDirections = TrafficDirection.Inbound,
            DownloadLimitBitsPerSecond = 1_000_000,
        };

        var errors = ApplicationRuleValidator.Validate(rule, requireExistingExecutable: false);

        Assert.Contains("Download cannot be blocked and rate-limited at the same time.", errors);
    }

    [Fact]
    public void Validate_accepts_a_well_formed_rule()
    {
        var errors = ApplicationRuleValidator.Validate(ValidRule(), requireExistingExecutable: false);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_unknown_direction_flags()
    {
        var errors = ApplicationRuleValidator.Validate(
            ValidRule() with { BlockedDirections = (TrafficDirection)8 },
            requireExistingExecutable: false);

        Assert.Contains("Blocked directions contain an unsupported value.", errors);
    }

    [Fact]
    public void Validate_rejects_unbounded_rate_and_name()
    {
        var errors = ApplicationRuleValidator.Validate(
            ValidRule() with
            {
                DisplayName = new string('x', ApplicationRuleValidator.MaximumDisplayNameLength + 1),
                UploadLimitBitsPerSecond = ApplicationRuleValidator.MaximumRateBitsPerSecond + 1,
            },
            requireExistingExecutable: false);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Validate_rejects_control_characters_in_display_name()
    {
        var errors = ApplicationRuleValidator.Validate(
            ValidRule() with { DisplayName = "Browser\u001b[31m" },
            requireExistingExecutable: false);

        Assert.Contains("Display name cannot contain control characters.", errors);
    }

    private static ApplicationRule ValidRule() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Browser",
        ExecutablePath = @"C:\Program Files\Browser\browser.exe",
        UploadLimitBitsPerSecond = 2_000_000,
    };
}
