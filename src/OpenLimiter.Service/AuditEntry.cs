using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Caller,
    Guid RequestId,
    PolicyRequestKind Operation,
    Guid? RuleId,
    bool Succeeded,
    string? ErrorCode);
