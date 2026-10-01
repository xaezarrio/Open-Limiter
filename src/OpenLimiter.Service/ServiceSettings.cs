using System.Security.Principal;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

public sealed record ServiceSettings(
    string DataDirectory,
    string PipeName,
    SecurityIdentifier AllowedUserSid)
{
    public string RuleFilePath => Path.Combine(DataDirectory, "rules.json");

    public string AuditFilePath => Path.Combine(DataDirectory, "audit.jsonl");

    public string AutomationFilePath => Path.Combine(DataDirectory, "automation.json");

    public static ServiceSettings Load(string[] args)
    {
        var options = ParseOptions(args);
        var dataDirectory = options.GetValueOrDefault("data-directory")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenLimiter");
        var pipeName = options.GetValueOrDefault("pipe-name") ?? PolicyServiceProtocol.PipeName;
        var allowedSidText = options.GetValueOrDefault("allowed-user-sid");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Data directory cannot be empty.");
        }
        var allowedSidPath = Path.Combine(dataDirectory, "allowed-user.sid");

        if (string.IsNullOrWhiteSpace(allowedSidText) && File.Exists(allowedSidPath))
        {
            allowedSidText = File.ReadAllText(allowedSidPath).Trim();
        }

        if (string.IsNullOrWhiteSpace(allowedSidText) && Environment.UserInteractive)
        {
            allowedSidText = WindowsIdentity.GetCurrent().User?.Value;
        }

        if (string.IsNullOrWhiteSpace(allowedSidText))
        {
            throw new InvalidOperationException(
                $"No authorized user SID is configured. Create '{allowedSidPath}' before starting the service.");
        }

        if (string.IsNullOrWhiteSpace(pipeName) ||
            pipeName.Length > 128 ||
            pipeName.Contains('\\') ||
            pipeName.Any(char.IsControl))
        {
            throw new ArgumentException("Pipe name must contain 1 to 128 printable characters and no backslash.");
        }

        return new(
            Path.GetFullPath(dataDirectory),
            pipeName,
            new SecurityIdentifier(allowedSidText));
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                args[index].Length == 2 ||
                index + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected --option value near '{args[index]}'.");
            }

            var name = args[index][2..];
            if (!name.Equals("data-directory", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("pipe-name", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("allowed-user-sid", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown service option '--{name}'.");
            }

            if (!options.TryAdd(name, args[index + 1]))
            {
                throw new ArgumentException($"Service option '--{name}' can only be specified once.");
            }
        }

        return options;
    }
}
