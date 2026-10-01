using System.Text;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;

namespace OpenLimiter.Core.Tests;

public sealed class RuleSetDocumentSerializerTests
{
    [Fact]
    public void Round_trip_preserves_versioned_rule_set()
    {
        var exportedAt = new DateTimeOffset(2026, 9, 26, 1, 2, 3, TimeSpan.Zero);
        var rule = CreateRule();

        var content = RuleSetDocumentSerializer.Serialize([rule], exportedAt);
        var document = RuleSetDocumentSerializer.Deserialize(content);

        Assert.Contains("\"blockedDirections\": \"Both\"", Encoding.UTF8.GetString(content), StringComparison.Ordinal);
        Assert.Equal(RuleSetDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal(exportedAt, document.ExportedAtUtc);
        Assert.Equal(rule, Assert.Single(document.Rules));
    }

    [Fact]
    public void Deserialize_rejects_unknown_schema_and_fields()
    {
        var unknownSchema = Encoding.UTF8.GetBytes("""
            { "schemaVersion": 99, "exportedAtUtc": "2026-09-26T01:02:03Z", "rules": [] }
            """);
        var unknownField = Encoding.UTF8.GetBytes("""
            { "schemaVersion": 1, "exportedAtUtc": "2026-09-26T01:02:03Z", "rules": [], "extra": true }
            """);

        Assert.Throws<InvalidDataException>(() => RuleSetDocumentSerializer.Deserialize(unknownSchema));
        Assert.Throws<InvalidDataException>(() => RuleSetDocumentSerializer.Deserialize(unknownField));
    }

    [Fact]
    public void Deserialize_rejects_null_rule()
    {
        var content = Encoding.UTF8.GetBytes("""
            { "schemaVersion": 1, "exportedAtUtc": "2026-09-26T01:02:03Z", "rules": [null] }
            """);

        Assert.Throws<InvalidDataException>(() => RuleSetDocumentSerializer.Deserialize(content));
    }

    [Fact]
    public void Serialize_rejects_more_than_maximum_rules()
    {
        var rules = Enumerable.Range(0, RuleSetDocumentSerializer.MaximumRules + 1)
            .Select(_ => CreateRule())
            .ToArray();

        Assert.Throws<InvalidDataException>(() => RuleSetDocumentSerializer.Serialize(rules, DateTimeOffset.UtcNow));
    }

    private static ApplicationRule CreateRule() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Browser",
        ExecutablePath = @"C:\Apps\browser.exe",
        BlockedDirections = TrafficDirection.Both,
    };
}
