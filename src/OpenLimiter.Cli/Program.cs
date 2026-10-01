using System.Globalization;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Parsing;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Core.Versioning;
using OpenLimiter.Cli;
using OpenLimiter.Protocol;
using OpenLimiter.Windows.Connections;
using OpenLimiter.Windows.Processes;
using OpenLimiter.Windows.Service;

return await OpenLimiterCommand.RunAsync(args);

internal static class OpenLimiterCommand
{
    private static readonly IPolicyServiceClient PolicyService = new PolicyServiceClient();

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "processes" => ListProcesses(args[1..]),
                "instances" => ListProcessInstances(args[1..]),
                "connections" => ListConnections(args[1..]),
                "traffic" => await ListTrafficAsync(args[1..]),
                "service-status" => await ServiceStatusAsync(args[1..]),
                "driver-flows" => await DriverFlowsAsync(args[1..]),
                "rules" => await ListRulesAsync(args[1..]),
                "apply" => await ApplyAsync(args[1..]),
                "pause" => await SetRuleStateAsync(args[1..], enabled: false),
                "resume" => await SetRuleStateAsync(args[1..], enabled: true),
                "remove" => await RemoveAsync(args[1..]),
                "clear" => await ClearAsync(args[1..]),
                "export" => await ExportAsync(args[1..]),
                "import" => await ImportAsync(args[1..]),
                "profiles" => await ListProfilesAsync(args[1..]),
                "profile-save" => await SaveProfileAsync(args[1..]),
                "profile-activate" => await ActivateProfileAsync(args[1..]),
                "profile-delete" => await DeleteProfileAsync(args[1..]),
                "schedules" => await ListSchedulesAsync(args[1..]),
                "schedule-add" => await SaveScheduleAsync(args[1..]),
                "schedule-delete" => await DeleteScheduleAsync(args[1..]),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 2;
        }
        catch (PolicyServiceUnavailableException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("Install or start the OpenLimiter Policy Service, then retry.");
            return 3;
        }
        catch (ArgumentException exception)
        {
            return UsageError(exception.Message);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int ListProcesses(string[] args)
    {
        CommandLineOptions.Parse(args);
        foreach (var group in new ProcessCatalog().GetRunningProcessGroups())
        {
            Console.WriteLine($"{group.InstanceCount,4}x  {group.Name,-28}  {group.ExecutablePath ?? "<protected>"}");
        }

        return 0;
    }

    private static int ListProcessInstances(string[] args)
    {
        CommandLineOptions.Parse(args);
        foreach (var process in new ProcessCatalog().GetRunningProcesses())
        {
            Console.WriteLine($"{process.ProcessId,7}  {process.Name,-28}  {process.ExecutablePath ?? "<protected or exited>"}");
        }

        return 0;
    }

    private static int ListConnections(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "view");
        var view = options.GetValueOrDefault("view")?.ToLowerInvariant() ?? "grouped";
        if (view == "grouped")
        {
            return ListConnectionGroups();
        }
        if (view != "detailed")
        {
            return UsageError("--view must be grouped or detailed.");
        }

        return ListConnectionDetails();
    }

    private static int ListConnectionGroups()
    {
        var groups = new NetworkConnectionCatalog().GetConnectionGroups();
        if (groups.Count == 0)
        {
            Console.WriteLine("No TCP connections or bound UDP endpoints were reported by Windows.");
            return 0;
        }

        foreach (var group in groups)
        {
            Console.WriteLine(
                $"{group.Connections.Count,4} endpoint(s)  {group.ActiveInstanceCount,3} active process(es)  {group.ProcessName}");
            Console.WriteLine($"  TCP={group.TcpConnectionCount}; UDP={group.UdpEndpointCount}; PIDs={string.Join(',', group.ActiveProcessIds)}");
            Console.WriteLine($"  {group.ExecutablePath ?? "<protected or exited>"}");
        }

        return 0;
    }

    private static int ListConnectionDetails()
    {
        var connections = new NetworkConnectionCatalog().GetConnections();
        if (connections.Count == 0)
        {
            Console.WriteLine("No TCP connections or bound UDP endpoints were reported by Windows.");
            return 0;
        }

        foreach (var connection in connections)
        {
            var state = connection.TcpState?.ToString() ?? "Bound";
            var remote = connection.RemoteEndPoint?.ToString() ?? "-";
            Console.WriteLine(
                $"{connection.ProcessId,7}  {connection.ProcessName,-24}  {connection.Protocol,-3}  {state,-12}  {connection.LocalEndPoint,-24}  {remote}");
        }

        return 0;
    }

    private static async Task<int> ListTrafficAsync(string[] args)
    {
        CommandLineOptions.Parse(args);
        var snapshot = await PolicyService.GetNetworkTrafficAsync();
        if (!snapshot.IsAvailable)
        {
            Console.Error.WriteLine(snapshot.Message ?? "Network traffic monitoring is unavailable.");
            return 1;
        }

        var samplesByProcessId = snapshot.Samples.ToDictionary(sample => sample.ProcessId);
        var groups = new ProcessCatalog().GetRunningProcessGroups()
            .Select(group => new
            {
                Group = group,
                Upload = group.ProcessIds.Sum(processId => samplesByProcessId.GetValueOrDefault(processId)?.UploadBytesPerSecond ?? 0),
                Download = group.ProcessIds.Sum(processId => samplesByProcessId.GetValueOrDefault(processId)?.DownloadBytesPerSecond ?? 0),
                TotalUpload = group.ProcessIds.Sum(processId => samplesByProcessId.GetValueOrDefault(processId)?.TotalUploadBytes ?? 0),
                TotalDownload = group.ProcessIds.Sum(processId => samplesByProcessId.GetValueOrDefault(processId)?.TotalDownloadBytes ?? 0),
            })
            .Where(item => item.Upload > 0 || item.Download > 0 || item.TotalUpload > 0 || item.TotalDownload > 0)
            .OrderByDescending(item => item.Upload + item.Download)
            .ThenBy(item => item.Group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (groups.Length == 0)
        {
            Console.WriteLine("No process traffic was measured in the latest sample.");
            return 0;
        }

        foreach (var item in groups)
        {
            Console.WriteLine(
                $"down={FormatByteRate(item.Download),12}  up={FormatByteRate(item.Upload),12}  {item.Group.Name} ({item.Group.InstanceCount} process(es))");
            Console.WriteLine(
                $"  total down={FormatBytes(item.TotalDownload)}; total up={FormatBytes(item.TotalUpload)}; {item.Group.ExecutablePath ?? "<protected>"}");
        }

        return 0;
    }

    private static async Task<int> ListRulesAsync(string[] args)
    {
        CommandLineOptions.Parse(args);
        var rules = await PolicyService.ListRulesAsync();
        if (rules.Count == 0)
        {
            Console.WriteLine("No saved rules. Use 'apply' to create one.");
            return 0;
        }

        foreach (var rule in rules)
        {
            Console.WriteLine($"{rule.Id}  {rule.DisplayName}");
            Console.WriteLine($"  {rule.ExecutablePath}");
            var lifetime = rule.ExpiresAtUtc is { } expiresAtUtc
                ? $"expires={expiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}"
                : "expires=never";
            Console.WriteLine($"  block={rule.BlockedDirections}; upload={FormatRate(rule.UploadLimitBitsPerSecond)}; state={(rule.Enabled ? "active" : "paused")}; {lifetime}");
        }

        return 0;
    }

    private static async Task<int> ServiceStatusAsync(string[] args)
    {
        CommandLineOptions.Parse(args);
        var response = await PolicyService.PingAsync();
        if (!response.Succeeded)
        {
            Console.Error.WriteLine(response.ErrorMessage ?? "Policy service rejected the status request.");
            return 1;
        }

        var cliVersion = typeof(OpenLimiterCommand).Assembly.GetName().Version?.ToString() ?? "unknown";
        var serviceVersion = response.ServiceVersion ?? "unknown";
        var versionsMatch = ComponentVersion.MatchesRelease(cliVersion, serviceVersion);
        Console.WriteLine($"OpenLimiter Policy Service {serviceVersion} is available.");
        if (!versionsMatch)
        {
            Console.Error.WriteLine(
                $"Version mismatch: CLI {cliVersion}, policy service {serviceVersion}. " +
                "Run install-service.ps1 from this release as Administrator.");
        }
        if (response.Driver is { IsAvailable: true } driver)
        {
            Console.WriteLine($"WFP driver API {driver.ApiMajor}.{driver.ApiMinor}; callouts={driver.RegisteredCallouts}; classifications={driver.ClassificationCount}.");
            Console.WriteLine(driver.PolicySessionActive
                ? "Dynamic WFP inspection filters are active."
                : $"Dynamic WFP inspection filters are inactive: {driver.PolicySessionMessage ?? "status not reported"}");
        }
        else
        {
            Console.WriteLine($"WFP driver unavailable: {response.Driver?.Message ?? "status not reported"}");
        }

        return versionsMatch ? 0 : 1;
    }

    private static async Task<int> DriverFlowsAsync(string[] args)
    {
        CommandLineOptions.Parse(args);
        var snapshot = await PolicyService.GetDriverFlowsAsync();
        if (!snapshot.IsAvailable)
        {
            Console.Error.WriteLine($"WFP driver unavailable: {snapshot.Message ?? "status not reported"}");
            return 1;
        }

        if (!snapshot.PolicySessionActive)
        {
            Console.Error.WriteLine($"WFP inspection filters are inactive: {snapshot.Message ?? "status not reported"}");
            return 2;
        }

        if (snapshot.Events.Count == 0)
        {
            Console.WriteLine("No established-flow events are buffered.");
            return 0;
        }

        foreach (var flowEvent in snapshot.Events)
        {
            var rule = flowEvent.RuleId is { } ruleId
                ? $"  rule {flowEvent.RuleDisplayName ?? ruleId.ToString()}"
                : string.Empty;
            var endpoints = flowEvent.LocalAddress is not null && flowEvent.RemoteAddress is not null
                ? $"  {FormatEndpoint(flowEvent.LocalAddress, flowEvent.LocalPort, flowEvent.IpProtocol)} -> {FormatEndpoint(flowEvent.RemoteAddress, flowEvent.RemotePort, flowEvent.IpProtocol)}"
                : string.Empty;
            Console.WriteLine(
                $"{flowEvent.Sequence,8}  PID {flowEvent.ProcessId,7}  {FormatFlowDirection(flowEvent.Direction),8}  {FormatIpProtocol(flowEvent.IpProtocol),7}  IPv{flowEvent.IpVersion}{endpoints}  app {flowEvent.ApplicationIdHash:X16}{rule}");
        }

        return 0;
    }

    private static string FormatFlowDirection(WfpFlowDirection direction) => direction switch
    {
        WfpFlowDirection.Inbound => "inbound",
        WfpFlowDirection.Outbound => "outbound",
        _ => "unknown",
    };

    private static string FormatIpProtocol(byte protocol) => protocol switch
    {
        1 => "ICMP",
        6 => "TCP",
        17 => "UDP",
        58 => "ICMPv6",
        _ => protocol.ToString(),
    };

    private static string FormatEndpoint(string address, ushort port, byte protocol)
    {
        if (protocol is not (6 or 17))
        {
            return address;
        }

        return address.Contains(':', StringComparison.Ordinal)
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
    }

    private static async Task<int> ApplyAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "path", "name", "block", "upload", "duration");
        if (!options.TryGetValue("path", out var executablePath))
        {
            return UsageError("apply requires --path <absolute-exe-path>.");
        }

        var blockedDirections = ParseDirection(options.GetValueOrDefault("block"));
        long? uploadLimit = null;
        if (options.TryGetValue("upload", out var uploadText))
        {
            if (!DataRateParser.TryParse(uploadText, out var parsedRate))
            {
                return UsageError("--upload must use bps, kbps, mbps, or gbps, for example 2.5mbps.");
            }

            uploadLimit = parsedRate;
        }

        if (blockedDirections == TrafficDirection.None && uploadLimit is null)
        {
            return UsageError("apply requires --block in|out|both, --upload <rate>, or both.");
        }

        var fullExecutablePath = Path.GetFullPath(executablePath);
        var existingRule = (await PolicyService.ListRulesAsync()).FirstOrDefault(rule =>
            string.Equals(rule.ExecutablePath, fullExecutablePath, StringComparison.OrdinalIgnoreCase));
        var expiresAtUtc = existingRule?.ExpiresAtUtc;
        if (options.TryGetValue("duration", out var durationText))
        {
            if (string.Equals(durationText, "permanent", StringComparison.OrdinalIgnoreCase))
            {
                expiresAtUtc = null;
            }
            else if (RuleDurationParser.TryParse(durationText, out var duration))
            {
                expiresAtUtc = DateTimeOffset.UtcNow.Add(duration);
            }
            else
            {
                return UsageError("--duration must be 1m..10080m, 1h..168h, 1d..7d, or permanent.");
            }
        }
        var rule = new ApplicationRule
        {
            Id = existingRule?.Id ?? Guid.NewGuid(),
            DisplayName = options.GetValueOrDefault("name") ?? existingRule?.DisplayName ?? Path.GetFileNameWithoutExtension(executablePath),
            ExecutablePath = fullExecutablePath,
            Enabled = existingRule?.Enabled ?? true,
            BlockedDirections = blockedDirections,
            UploadLimitBitsPerSecond = uploadLimit,
            ExpiresAtUtc = expiresAtUtc,
        };

        var result = await PolicyService.ApplyAsync(rule);
        PrintResult(result);
        if (!result.Succeeded)
        {
            return 1;
        }

        Console.WriteLine(existingRule is null
            ? $"Saved rule {rule.Id}."
            : $"Updated existing rule {rule.Id} for this executable.");
        return 0;
    }

    private static async Task<int> ListProfilesAsync(string[] args)
    {
        CommandLineOptions.Parse(args);
        var snapshot = await PolicyService.GetAutomationAsync();
        if (snapshot.Profiles.Count == 0)
        {
            Console.WriteLine("No profiles. Use 'profile-save --name <label>' to capture the current rules.");
            return 0;
        }

        foreach (var profile in snapshot.Profiles)
        {
            Console.WriteLine($"{profile.Id}  {profile.Name}  ({profile.RuleCount} rules, updated {profile.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
        }
        return 0;
    }

    private static async Task<int> SaveProfileAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "name", "id");
        if (!options.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return UsageError("profile-save requires --name <label>.");
        }
        var profileId = options.TryGetValue("id", out var idText) && Guid.TryParse(idText, out var parsedId)
            ? parsedId
            : Guid.NewGuid();
        var rules = await PolicyService.ListRulesAsync();
        var snapshot = await PolicyService.SaveProfileAsync(new()
        {
            Id = profileId,
            Name = name,
            Rules = rules,
        });
        var saved = snapshot.Profiles.Single(profile => profile.Id == profileId);
        Console.WriteLine($"Saved profile {saved.Id} '{saved.Name}' with {saved.RuleCount} rule(s).");
        return 0;
    }

    private static async Task<int> ActivateProfileAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "id");
        if (!options.TryGetValue("id", out var idText) || !Guid.TryParse(idText, out var profileId))
        {
            return UsageError("profile-activate requires --id <profile-guid>.");
        }
        var result = await PolicyService.ActivateProfileAsync(profileId);
        PrintResult(result);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> DeleteProfileAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "id");
        if (!options.TryGetValue("id", out var idText) || !Guid.TryParse(idText, out var profileId))
        {
            return UsageError("profile-delete requires --id <profile-guid>.");
        }
        await PolicyService.DeleteProfileAsync(profileId);
        Console.WriteLine($"Deleted profile {profileId}.");
        return 0;
    }

    private static async Task<int> ListSchedulesAsync(string[] args)
    {
        CommandLineOptions.Parse(args);
        var snapshot = await PolicyService.GetAutomationAsync();
        if (snapshot.Schedules.Count == 0)
        {
            Console.WriteLine("No schedules. Use 'schedule-add' to create one.");
            return 0;
        }

        foreach (var schedule in snapshot.Schedules)
        {
            var profileName = snapshot.Profiles.FirstOrDefault(profile => profile.Id == schedule.ProfileId)?.Name ?? "missing profile";
            Console.WriteLine($"{schedule.Id}  {schedule.Name}: {schedule.Days} at {schedule.Hour:00}:{schedule.Minute:00} -> {profileName}; state={(schedule.Enabled ? "enabled" : "disabled")}");
        }
        return 0;
    }

    private static async Task<int> SaveScheduleAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "name", "profile", "days", "time");
        if (!options.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name) ||
            !options.TryGetValue("profile", out var profileText) || !Guid.TryParse(profileText, out var profileId) ||
            !options.TryGetValue("days", out var daysText) || !ScheduleDaysParser.TryParse(daysText, out var days) ||
            !options.TryGetValue("time", out var timeText) || !TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return UsageError("schedule-add requires --name <label> --profile <guid> --days daily|weekdays|weekend|Mon,Wed --time HH:mm.");
        }

        var schedule = new PolicySchedule
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProfileId = profileId,
            Days = days,
            Hour = time.Hour,
            Minute = time.Minute,
        };
        await PolicyService.SaveScheduleAsync(schedule);
        Console.WriteLine($"Saved schedule {schedule.Id} '{schedule.Name}'.");
        return 0;
    }

    private static async Task<int> DeleteScheduleAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "id");
        if (!options.TryGetValue("id", out var idText) || !Guid.TryParse(idText, out var scheduleId))
        {
            return UsageError("schedule-delete requires --id <schedule-guid>.");
        }
        await PolicyService.DeleteScheduleAsync(scheduleId);
        Console.WriteLine($"Deleted schedule {scheduleId}.");
        return 0;
    }

    private static async Task<int> SetRuleStateAsync(string[] args, bool enabled)
    {
        var command = enabled ? "resume" : "pause";
        var options = CommandLineOptions.Parse(args, "id");
        if (!options.TryGetValue("id", out var idText) || !Guid.TryParse(idText, out var ruleId))
        {
            return UsageError($"{command} requires --id <rule-guid>.");
        }

        var rule = (await PolicyService.ListRulesAsync()).FirstOrDefault(candidate => candidate.Id == ruleId);
        if (rule is null)
        {
            Console.Error.WriteLine($"Saved rule {ruleId} was not found.");
            return 1;
        }

        if (rule.Enabled == enabled)
        {
            Console.WriteLine($"Rule {rule.Id} is already {(enabled ? "active" : "paused")}.");
            return 0;
        }

        var result = await PolicyService.ApplyAsync(rule with { Enabled = enabled });
        PrintResult(result);
        if (!result.Succeeded)
        {
            return 1;
        }

        Console.WriteLine($"{(enabled ? "Resumed" : "Paused")} rule {rule.Id} for {rule.DisplayName}.");
        return 0;
    }

    private static async Task<int> RemoveAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "id");
        if (!options.TryGetValue("id", out var idText) || !Guid.TryParse(idText, out var ruleId))
        {
            return UsageError("remove requires --id <rule-guid>.");
        }

        var result = await PolicyService.RemoveAsync(ruleId);
        PrintResult(result);
        if (!result.Succeeded)
        {
            return 1;
        }

        return 0;
    }

    private static async Task<int> ClearAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "confirm");
        if (!string.Equals(options.GetValueOrDefault("confirm"), "remove-all", StringComparison.Ordinal))
        {
            return UsageError("clear requires --confirm remove-all.");
        }

        var result = await PolicyService.ClearAsync();
        PrintResult(result);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ExportAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "file", "confirm");
        if (!options.TryGetValue("file", out var filePath))
        {
            return UsageError("export requires --file <json-path>.");
        }

        var destination = Path.GetFullPath(filePath);
        if (IsDefaultServiceRuleStore(destination))
        {
            return UsageError("Refusing to overwrite the policy service's internal rules.json. Choose another export path.");
        }
        var overwrite = string.Equals(options.GetValueOrDefault("confirm"), "overwrite", StringComparison.Ordinal);
        if (File.Exists(destination) && !overwrite)
        {
            return UsageError("The export file already exists. Add --confirm overwrite to replace it.");
        }

        var rules = await PolicyService.ListRulesAsync();
        var content = RuleSetDocumentSerializer.Serialize(rules, DateTimeOffset.UtcNow);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{destination}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content);
            File.Move(temporaryPath, destination, overwrite);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        Console.WriteLine($"Exported {rules.Count} rule(s) with schema version {RuleSetDocument.CurrentSchemaVersion} to {destination}.");
        return 0;
    }

    private static async Task<int> ImportAsync(string[] args)
    {
        var options = CommandLineOptions.Parse(args, "file", "confirm");
        if (!options.TryGetValue("file", out var filePath))
        {
            return UsageError("import requires --file <json-path>.");
        }
        if (!string.Equals(options.GetValueOrDefault("confirm"), "replace-all", StringComparison.Ordinal))
        {
            return UsageError("import replaces every saved rule and requires --confirm replace-all.");
        }

        var source = Path.GetFullPath(filePath);
        if (IsDefaultServiceRuleStore(source))
        {
            return UsageError("Refusing to import the policy service's internal rules.json directly. Use a separate export file.");
        }
        var file = new FileInfo(source);
        if (!file.Exists)
        {
            return UsageError($"Import file does not exist: {source}");
        }
        if (file.Length is <= 0 or > RuleSetDocumentSerializer.MaximumDocumentBytes)
        {
            return UsageError($"Import file must contain 1 to {RuleSetDocumentSerializer.MaximumDocumentBytes} bytes.");
        }

        var document = RuleSetDocumentSerializer.Deserialize(await File.ReadAllBytesAsync(source));
        var result = await PolicyService.ReplaceRulesAsync(document.Rules);
        PrintResult(result);
        if (!result.Succeeded)
        {
            return 1;
        }

        Console.WriteLine($"Imported {document.Rules.Count} rule(s) from schema version {document.SchemaVersion}.");
        return 0;
    }

    private static TrafficDirection ParseDirection(string? value) => value?.ToLowerInvariant() switch
    {
        null => TrafficDirection.None,
        "in" => TrafficDirection.Inbound,
        "out" => TrafficDirection.Outbound,
        "both" => TrafficDirection.Both,
        _ => throw new ArgumentException("--block must be in, out, or both."),
    };

    private static void PrintResult(EnforcementResult result)
    {
        foreach (var message in result.Applied)
        {
            Console.WriteLine($"Applied: {message}");
        }

        foreach (var message in result.Warnings)
        {
            Console.WriteLine($"Warning: {message}");
        }

        foreach (var message in result.Errors)
        {
            Console.Error.WriteLine($"Error: {message}");
        }
    }

    private static int UnknownCommand(string command) => UsageError($"Unknown command '{command}'.");

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run with --help for usage.");
        return 2;
    }

    private static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    private static bool IsDefaultServiceRuleStore(string path)
    {
        var defaultStore = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OpenLimiter",
            "rules.json"));
        return string.Equals(path, defaultStore, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRate(long? bitsPerSecond) => bitsPerSecond is null ? "none" : $"{bitsPerSecond} bit/s";

    private static string FormatByteRate(long bytesPerSecond) => $"{FormatBytes(bytesPerSecond)}/s";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        decimal value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static void PrintHelp()
    {
        Console.WriteLine("OpenLimiter command-line preview");
        Console.WriteLine();
        Console.WriteLine("  processes");
        Console.WriteLine("  instances");
        Console.WriteLine("  connections [--view grouped|detailed]");
        Console.WriteLine("  traffic");
        Console.WriteLine("  service-status");
        Console.WriteLine("  driver-flows");
        Console.WriteLine("  rules");
        Console.WriteLine("  apply --path <exe> [--name <label>] [--block in|out|both] [--upload 2.5mbps] [--duration 10m|2h|7d|permanent]");
        Console.WriteLine("  pause --id <rule-guid>");
        Console.WriteLine("  resume --id <rule-guid>");
        Console.WriteLine("  remove --id <rule-guid>");
        Console.WriteLine("  clear --confirm remove-all");
        Console.WriteLine("  export --file <json> [--confirm overwrite]");
        Console.WriteLine("  import --file <json> --confirm replace-all");
        Console.WriteLine("  profiles");
        Console.WriteLine("  profile-save --name <label> [--id <profile-guid>]");
        Console.WriteLine("  profile-activate --id <profile-guid>");
        Console.WriteLine("  profile-delete --id <profile-guid>");
        Console.WriteLine("  schedules");
        Console.WriteLine("  schedule-add --name <label> --profile <guid> --days <set> --time HH:mm");
        Console.WriteLine("  schedule-delete --id <schedule-guid>");
        Console.WriteLine();
        Console.WriteLine("Policy changes are validated and applied by the OpenLimiter Policy Service.");
    }
}
