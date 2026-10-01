using OpenLimiter.Windows.Processes;

namespace OpenLimiter.Windows.Connections;

public sealed class NetworkConnectionCatalog
{
    public IReadOnlyList<NetworkConnectionSnapshot> GetConnections()
    {
        var processes = new ProcessCatalog().GetRunningProcessGroups()
            .SelectMany(group => group.ProcessIds.Select(processId => (processId, group)))
            .ToDictionary(item => item.processId, item => item.group);

        return IpHelperConnectionTable.ReadAll()
            .Select(connection =>
            {
                processes.TryGetValue(connection.ProcessId, out var process);
                return new NetworkConnectionSnapshot(
                    connection.ProcessId,
                    process?.Name ?? $"PID {connection.ProcessId}",
                    process?.ExecutablePath,
                    connection.Protocol,
                    connection.LocalEndPoint,
                    connection.RemoteEndPoint,
                    connection.TcpState);
            })
            .OrderBy(connection => connection.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.ProcessId)
            .ThenBy(connection => connection.Protocol)
            .ThenBy(connection => connection.LocalEndPoint.Port)
            .ToArray();
    }

    public IReadOnlyList<NetworkConnectionGroupSnapshot> GetConnectionGroups() =>
        GroupConnections(GetConnections());

    internal static IReadOnlyList<NetworkConnectionGroupSnapshot> GroupConnections(
        IEnumerable<NetworkConnectionSnapshot> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        return connections
            .GroupBy(
                connection => connection.ExecutablePath is null
                    ? $"name|{connection.ProcessName}"
                    : $"path|{connection.ExecutablePath}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                var identified = items.FirstOrDefault(item => item.ExecutablePath is not null);
                return new NetworkConnectionGroupSnapshot(
                    identified?.ProcessName ?? items[0].ProcessName,
                    identified?.ExecutablePath,
                    items.Select(item => item.ProcessId).Distinct().Order().ToArray(),
                    items);
            })
            .OrderBy(group => group.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
