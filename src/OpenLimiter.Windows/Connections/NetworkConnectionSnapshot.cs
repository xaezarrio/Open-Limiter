using System.Net;
using System.Net.NetworkInformation;

namespace OpenLimiter.Windows.Connections;

public sealed record NetworkConnectionSnapshot(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    NetworkTransportProtocol Protocol,
    IPEndPoint LocalEndPoint,
    IPEndPoint? RemoteEndPoint,
    TcpState? TcpState);
