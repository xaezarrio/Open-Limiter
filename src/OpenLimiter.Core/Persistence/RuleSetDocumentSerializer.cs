using System.Text.Json;
using System.Text.Json.Serialization;
using OpenLimiter.Core.Models;

namespace OpenLimiter.Core.Persistence;

public static class RuleSetDocumentSerializer
{
    public const int MaximumDocumentBytes = 60 * 1024;

    public const int MaximumRules = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static byte[] Serialize(IReadOnlyList<ApplicationRule> rules, DateTimeOffset exportedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ValidateRuleCount(rules.Count);

        var content = JsonSerializer.SerializeToUtf8Bytes(new RuleSetDocument
        {
            ExportedAtUtc = exportedAtUtc.ToUniversalTime(),
            Rules = rules,
        }, SerializerOptions);
        if (content.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException($"Rule-set document exceeds {MaximumDocumentBytes} bytes.");
        }

        return content;
    }

    public static RuleSetDocument Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException($"Rule-set document must contain 1 to {MaximumDocumentBytes} bytes.");
        }

        RuleSetDocument document;
        try
        {
            document = JsonSerializer.Deserialize<RuleSetDocument>(content, SerializerOptions)
                ?? throw new InvalidDataException("Rule-set document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Rule-set document is not valid OpenLimiter JSON.", exception);
        }

        if (document.SchemaVersion != RuleSetDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Rule-set schema version {document.SchemaVersion} is not supported.");
        }
        if (document.Rules is null)
        {
            throw new InvalidDataException("Rule-set document has no rules collection.");
        }
        if (document.Rules.Any(rule => rule is null))
        {
            throw new InvalidDataException("Rule-set document contains a null rule.");
        }

        ValidateRuleCount(document.Rules.Count);
        return document;
    }

    private static void ValidateRuleCount(int count)
    {
        if (count > MaximumRules)
        {
            throw new InvalidDataException($"Rule-set document cannot contain more than {MaximumRules} rules.");
        }
    }
}
