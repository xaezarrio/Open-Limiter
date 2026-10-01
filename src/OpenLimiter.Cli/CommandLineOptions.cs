namespace OpenLimiter.Cli;

internal sealed class CommandLineOptions
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    private CommandLineOptions()
    {
    }

    public static CommandLineOptions Parse(string[] args, params string[] allowedNames)
    {
        ArgumentNullException.ThrowIfNull(args);
        var allowed = new HashSet<string>(allowedNames, StringComparer.OrdinalIgnoreCase);
        var result = new CommandLineOptions();

        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                args[index].Length == 2 ||
                index + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected --option value near '{args[index]}'.");
            }

            var name = args[index][2..];
            if (!allowed.Contains(name))
            {
                throw new ArgumentException($"Unknown option '--{name}'.");
            }
            if (!result.values.TryAdd(name, args[index + 1]))
            {
                throw new ArgumentException($"Option '--{name}' can only be specified once.");
            }
        }

        return result;
    }

    public bool TryGetValue(string name, out string value) => values.TryGetValue(name, out value!);

    public string? GetValueOrDefault(string name) => values.GetValueOrDefault(name);
}
