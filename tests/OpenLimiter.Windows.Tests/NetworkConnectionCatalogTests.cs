using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OpenLimiter.Windows.Connections;

namespace OpenLimiter.Windows.Tests;

public sealed class NetworkConnectionCatalogTests
{
    [Theory]
    [InlineData(0x5000U, 80)]
    [InlineData(0xBB01U, 443)]
    public void ConvertPort_reads_network_byte_order(uint value, int expected) =>
        Assert.Equal(expected, IpHelperConnectionTable.ConvertPort(value));

    [Fact]
    public async Task GetConnections_finds_owned_loopback_tcp_connection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var client = new TcpClient();
            var acceptTask = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var server = await acceptTask;

            NetworkConnectionSnapshot? connection = null;
            for (var attempt = 0; attempt < 10 && connection is null; attempt++)
            {
                connection = new NetworkConnectionCatalog().GetConnections().FirstOrDefault(item =>
                    item.ProcessId == Environment.ProcessId &&
                    item.Protocol == NetworkTransportProtocol.Tcp &&
                    item.LocalEndPoint.Port == port &&
                    item.TcpState == TcpState.Established);
                if (connection is null)
                {
                    await Task.Delay(50);
                }
            }

            Assert.NotNull(connection);
            Assert.Equal(IPAddress.Loopback, connection.LocalEndPoint.Address);
            Assert.NotNull(connection.RemoteEndPoint);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void GetConnections_finds_owned_loopback_udp_endpoint()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;

        var endpoint = new NetworkConnectionCatalog().GetConnections().FirstOrDefault(item =>
            item.ProcessId == Environment.ProcessId &&
            item.Protocol == NetworkTransportProtocol.Udp &&
            item.LocalEndPoint.Port == port);

        Assert.NotNull(endpoint);
        Assert.Equal(IPAddress.Loopback, endpoint.LocalEndPoint.Address);
        Assert.Null(endpoint.RemoteEndPoint);
        Assert.Null(endpoint.TcpState);
    }

    [Fact]
    public void GroupConnections_merges_same_executable_across_process_ids()
    {
        var first = Connection(10, "browser", @"C:\Apps\browser.exe", 50000);
        var second = Connection(11, "browser", @"c:\apps\BROWSER.exe", 50001);

        var group = Assert.Single(NetworkConnectionCatalog.GroupConnections([first, second]));

        Assert.Equal(2, group.ActiveInstanceCount);
        Assert.Equal([10, 11], group.ActiveProcessIds);
        Assert.Equal(2, group.TcpConnectionCount);
    }

    [Fact]
    public void GroupConnections_keeps_same_name_executables_with_different_paths_separate()
    {
        var first = Connection(10, "helper", @"C:\SuiteA\helper.exe", 50000);
        var second = Connection(11, "helper", @"C:\SuiteB\helper.exe", 50001);

        var groups = NetworkConnectionCatalog.GroupConnections([first, second]);

        Assert.Equal(2, groups.Count);
    }

    private static NetworkConnectionSnapshot Connection(
        int processId,
        string name,
        string? path,
        int port) => new(
            processId,
            name,
            path,
            NetworkTransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, port),
            new IPEndPoint(IPAddress.Loopback, 443),
            TcpState.Established);
}
