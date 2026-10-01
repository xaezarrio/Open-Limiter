using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLimiter.Core.Persistence;

public sealed class JsonAutomationStore(string filePath) : IAutomationStore
{
    public const int MaximumProfiles = 20;
    public const int MaximumSchedules = 50;
    public const int MaximumDocumentBytes = 512 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public async Task<AutomationConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new();
        }

        var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (content.Length is 0 or > MaximumDocumentBytes)
        {
            throw new InvalidDataException($"Automation document must contain 1 to {MaximumDocumentBytes} bytes.");
        }

        try
        {
            var configuration = JsonSerializer.Deserialize<AutomationConfiguration>(content, SerializerOptions)
                ?? throw new InvalidDataException("Automation document is empty.");
            Validate(configuration);
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Automation document is not valid OpenLimiter JSON.", exception);
        }
    }

    public async Task SaveAsync(AutomationConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration);
        var content = JsonSerializer.SerializeToUtf8Bytes(configuration, SerializerOptions);
        if (content.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException($"Automation document exceeds {MaximumDocumentBytes} bytes.");
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{filePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void Validate(AutomationConfiguration configuration)
    {
        if (configuration.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Automation schema version {configuration.SchemaVersion} is not supported.");
        }
        if (configuration.Profiles is null || configuration.Schedules is null)
        {
            throw new InvalidDataException("Automation document requires profile and schedule collections.");
        }
        if (configuration.Profiles.Any(profile => profile is null) || configuration.Schedules.Any(schedule => schedule is null))
        {
            throw new InvalidDataException("Automation document cannot contain null profiles or schedules.");
        }
        if (configuration.Profiles.Count > MaximumProfiles)
        {
            throw new InvalidDataException($"No more than {MaximumProfiles} profiles are allowed.");
        }
        if (configuration.Schedules.Count > MaximumSchedules)
        {
            throw new InvalidDataException($"No more than {MaximumSchedules} schedules are allowed.");
        }
        if (configuration.Profiles.Any(profile => profile.Rules is null || profile.Rules.Count > RuleSetDocumentSerializer.MaximumRules))
        {
            throw new InvalidDataException($"Each profile requires at most {RuleSetDocumentSerializer.MaximumRules} rules.");
        }
        if (configuration.Profiles.Select(profile => profile.Id).Distinct().Count() != configuration.Profiles.Count ||
            configuration.Schedules.Select(schedule => schedule.Id).Distinct().Count() != configuration.Schedules.Count)
        {
            throw new InvalidDataException("Automation document contains duplicate identifiers.");
        }
    }
}
