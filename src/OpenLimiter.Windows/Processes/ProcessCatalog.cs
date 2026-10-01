using System.ComponentModel;
using System.Diagnostics;

namespace OpenLimiter.Windows.Processes;

public sealed class ProcessCatalog
{
    public IReadOnlyList<ProcessGroupSnapshot> GetRunningProcessGroups() =>
        GroupByExecutable(GetRunningProcesses());

    public IReadOnlyList<ProcessSnapshot> GetRunningProcesses()
    {
        var snapshots = new List<ProcessSnapshot>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    snapshots.Add(new(process.Id, process.ProcessName, TryGetExecutablePath(process)));
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and inspection.
                }
            }
        }

        return snapshots
            .OrderBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.ProcessId)
            .ToArray();
    }

    public static IReadOnlyList<ProcessGroupSnapshot> GroupByExecutable(IEnumerable<ProcessSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var materialized = snapshots.ToArray();
        var knownPathsByName = materialized
            .Where(snapshot => snapshot.ExecutablePath is not null)
            .GroupBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(snapshot => snapshot.ExecutablePath!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return materialized
            .GroupBy(
                snapshot => ResolveGroupKey(snapshot, knownPathsByName),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var members = group.OrderBy(snapshot => snapshot.ProcessId).ToArray();
                var identifiedMember = members.FirstOrDefault(snapshot => snapshot.ExecutablePath is not null);
                return new ProcessGroupSnapshot(
                    identifiedMember?.Name ?? members[0].Name,
                    identifiedMember?.ExecutablePath,
                    members.Select(snapshot => snapshot.ProcessId).ToArray());
            })
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveGroupKey(
        ProcessSnapshot snapshot,
        IReadOnlyDictionary<string, string[]> knownPathsByName)
    {
        if (snapshot.ExecutablePath is not null)
        {
            return $"path|{snapshot.ExecutablePath}";
        }

        return knownPathsByName.TryGetValue(snapshot.Name, out var knownPaths) && knownPaths.Length == 1
            ? $"path|{knownPaths[0]}"
            : $"name|{snapshot.Name}";
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
