namespace OpenLimiter.Windows.Connections;

public sealed record NetworkConnectionGroupSnapshot(
    string ProcessName,
    string? ExecutablePath,
    IReadOnlyList<int> ActiveProcessIds,
    IReadOnlyList<NetworkConnectionSnapshot> Connections)
{
    public int ActiveInstanceCount => ActiveProcessIds.Count;

    public int TcpConnectionCount => Connections.Count(connection =>
        connection.Protocol == NetworkTransportProtocol.Tcp);

    public int UdpEndpointCount => Connections.Count(connection =>
        connection.Protocol == NetworkTransportProtocol.Udp);
}
