namespace OpenLimiter.Core.Models;

public sealed record EnforcementResult(
    bool Succeeded,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public static EnforcementResult Success(IEnumerable<string>? applied = null, IEnumerable<string>? warnings = null) =>
        new(true, applied?.ToArray() ?? [], warnings?.ToArray() ?? [], []);

    public static EnforcementResult Failure(IEnumerable<string> errors, IEnumerable<string>? applied = null, IEnumerable<string>? warnings = null) =>
        new(false, applied?.ToArray() ?? [], warnings?.ToArray() ?? [], errors.ToArray());
}

