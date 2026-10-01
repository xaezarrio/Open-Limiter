using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OpenLimiter.Windows.Connections;

internal static class IpHelperConnectionTable
{
    private const uint ErrorInsufficientBuffer = 122;
    private const uint MaximumTableBytes = 16 * 1024 * 1024;
    private const int TcpOwnerPidAll = 5;
    private const int UdpOwnerPid = 1;

    public static IReadOnlyList<RawConnection> ReadAll()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Network connection discovery requires Windows.");
        }

        var connections = new List<RawConnection>();
        ReadTcp(AddressFamily.InterNetwork, connections);
        ReadTcp(AddressFamily.InterNetworkV6, connections);
        ReadUdp(AddressFamily.InterNetwork, connections);
        ReadUdp(AddressFamily.InterNetworkV6, connections);
        return connections;
    }

    internal static int ConvertPort(uint networkOrderPort) =>
        (ushort)IPAddress.NetworkToHostOrder((short)(networkOrderPort & ushort.MaxValue));

    private static void ReadTcp(AddressFamily family, ICollection<RawConnection> connections)
    {
        using var table = NativeTableBuffer.Load((IntPtr buffer, ref uint size) =>
            GetExtendedTcpTable(buffer, ref size, true, (int)family, TcpOwnerPidAll, 0));
        if (family == AddressFamily.InterNetwork)
        {
            foreach (var row in table.ReadRows<MibTcpRowOwnerPid>())
            {
                var state = ToTcpState(row.State);
                connections.Add(new(
                    ToProcessId(row.OwningPid),
                    NetworkTransportProtocol.Tcp,
                    new(new IPAddress(row.LocalAddress), ConvertPort(row.LocalPort)),
                    state == TcpState.Listen ? null : new(new IPAddress(row.RemoteAddress), ConvertPort(row.RemotePort)),
                    state));
            }
        }
        else
        {
            foreach (var row in table.ReadRows<MibTcp6RowOwnerPid>())
            {
                var state = ToTcpState(row.State);
                connections.Add(new(
                    ToProcessId(row.OwningPid),
                    NetworkTransportProtocol.Tcp,
                    new(new IPAddress(row.LocalAddress, row.LocalScopeId), ConvertPort(row.LocalPort)),
                    state == TcpState.Listen
                        ? null
                        : new(new IPAddress(row.RemoteAddress, row.RemoteScopeId), ConvertPort(row.RemotePort)),
                    state));
            }
        }
    }

    private static void ReadUdp(AddressFamily family, ICollection<RawConnection> connections)
    {
        using var table = NativeTableBuffer.Load((IntPtr buffer, ref uint size) =>
            GetExtendedUdpTable(buffer, ref size, true, (int)family, UdpOwnerPid, 0));
        if (family == AddressFamily.InterNetwork)
        {
            foreach (var row in table.ReadRows<MibUdpRowOwnerPid>())
            {
                connections.Add(new(
                    ToProcessId(row.OwningPid),
                    NetworkTransportProtocol.Udp,
                    new(new IPAddress(row.LocalAddress), ConvertPort(row.LocalPort)),
                    null,
                    null));
            }
        }
        else
        {
            foreach (var row in table.ReadRows<MibUdp6RowOwnerPid>())
            {
                connections.Add(new(
                    ToProcessId(row.OwningPid),
                    NetworkTransportProtocol.Udp,
                    new(new IPAddress(row.LocalAddress, row.LocalScopeId), ConvertPort(row.LocalPort)),
                    null,
                    null));
            }
        }
    }

    private static TcpState ToTcpState(uint value) => value <= (uint)TcpState.DeleteTcb
        ? (TcpState)value
        : TcpState.Unknown;

    private static int ToProcessId(uint value) => value <= int.MaxValue ? (int)value : 0;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(
        IntPtr table,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    internal sealed record RawConnection(
        int ProcessId,
        NetworkTransportProtocol Protocol,
        IPEndPoint LocalEndPoint,
        IPEndPoint? RemoteEndPoint,
        TcpState? TcpState);

    private sealed class NativeTableBuffer : IDisposable
    {
        private readonly IntPtr buffer;
        private readonly uint length;

        private NativeTableBuffer(IntPtr buffer, uint length)
        {
            this.buffer = buffer;
            this.length = length;
        }

        public static NativeTableBuffer Load(NativeTableLoader loader)
        {
            uint size = 0;
            var result = loader(IntPtr.Zero, ref size);
            if (result != ErrorInsufficientBuffer || size < sizeof(uint) || size > MaximumTableBytes)
            {
                throw new Win32Exception((int)result, "Windows did not return a valid network table size.");
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(checked((int)size));
                var allocatedSize = size;
                result = loader(buffer, ref size);
                if (result == 0)
                {
                    return new(buffer, allocatedSize);
                }

                Marshal.FreeHGlobal(buffer);
                if (result != ErrorInsufficientBuffer || size < sizeof(uint) || size > MaximumTableBytes)
                {
                    throw new Win32Exception((int)result, "Windows could not read the network endpoint table.");
                }
            }

            throw new Win32Exception((int)ErrorInsufficientBuffer, "The network endpoint table changed too quickly to read.");
        }

        public IReadOnlyList<T> ReadRows<T>() where T : struct
        {
            var count = checked((uint)Marshal.ReadInt32(buffer));
            var rowSize = Marshal.SizeOf<T>();
            var requiredBytes = checked(sizeof(uint) + ((ulong)count * (uint)rowSize));
            if (requiredBytes > length)
            {
                throw new InvalidDataException("Windows returned a truncated network endpoint table.");
            }

            var rows = new T[count];
            var rowAddress = IntPtr.Add(buffer, sizeof(uint));
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = Marshal.PtrToStructure<T>(rowAddress);
                rowAddress = IntPtr.Add(rowAddress, rowSize);
            }

            return rows;
        }

        public void Dispose() => Marshal.FreeHGlobal(buffer);
    }

    private delegate uint NativeTableLoader(IntPtr table, ref uint size);
}
