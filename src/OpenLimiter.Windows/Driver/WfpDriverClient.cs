using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenLimiter.Protocol;

namespace OpenLimiter.Windows.Driver;

public sealed class WfpDriverClient : IWfpDriverClient
{
    private const string DevicePath = @"\\.\OpenLimiterWfp";
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const uint IoctlGetCapabilities = 0x00126000;
    private const uint IoctlGetFlowEvents = 0x00126004;
    private const uint ExpectedSize = 32;
    private const uint ExpectedApiMajor = 1;
    private const uint MinimumApiMinor = 3;
    private const uint RequiredCapabilityFlags = 0x0000001F;
    private const uint ExpectedRegisteredCallouts = 2;
    private const int FlowEventBatchSize = 4624;
    private const int FlowEventHeaderSize = 16;
    private const int MaximumFlowEvents = 64;

    internal static int NativeFlowEventSize => Marshal.SizeOf<NativeFlowEvent>();

    internal static int ExpectedFlowEventBatchSize =>
        FlowEventHeaderSize + (MaximumFlowEvents * NativeFlowEventSize);

    public WfpDriverStatus GetStatus()
    {
        using var handle = CreateFile(
            DevicePath,
            GenericRead,
            0,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Unavailable(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        if (!DeviceIoControl(
                handle,
                IoctlGetCapabilities,
                IntPtr.Zero,
                0,
                out var capabilities,
                (uint)Marshal.SizeOf<NativeCapabilities>(),
                out var bytesReturned,
                IntPtr.Zero))
        {
            return Unavailable(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        var compatibilityError = ValidateCapabilities(
            bytesReturned,
            capabilities.Size,
            capabilities.ApiMajor,
            capabilities.ApiMinor,
            capabilities.Flags,
            capabilities.RegisteredCallouts);
        if (compatibilityError is not null)
        {
            return Unavailable(compatibilityError);
        }

        return new()
        {
            IsAvailable = true,
            ApiMajor = capabilities.ApiMajor,
            ApiMinor = capabilities.ApiMinor,
            CapabilityFlags = capabilities.Flags,
            RegisteredCallouts = capabilities.RegisteredCallouts,
            ClassificationCount = capabilities.ClassificationCount,
            Message = "Pass-through driver connected.",
        };
    }

    public WfpFlowSnapshot GetFlowEvents()
    {
        var status = GetStatus();
        if (!status.IsAvailable)
        {
            return UnavailableFlows(status.Message ?? "The WFP driver capability check failed.");
        }

        using var handle = CreateFile(
            DevicePath,
            GenericRead,
            0,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return UnavailableFlows(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        var buffer = Marshal.AllocHGlobal(FlowEventBatchSize);
        try
        {
            if (!DeviceIoControl(
                    handle,
                    IoctlGetFlowEvents,
                    IntPtr.Zero,
                    0,
                    buffer,
                    FlowEventBatchSize,
                    out var bytesReturned,
                    IntPtr.Zero))
            {
                return UnavailableFlows(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            var header = Marshal.PtrToStructure<NativeFlowEventBatchHeader>(buffer);
            if (ExpectedFlowEventBatchSize != FlowEventBatchSize ||
                bytesReturned != FlowEventBatchSize ||
                header.Size != FlowEventBatchSize ||
                header.Count > MaximumFlowEvents)
            {
                return UnavailableFlows("The driver returned an incompatible flow-event structure.");
            }

            var events = new WfpFlowEvent[header.Count];
            var eventSize = Marshal.SizeOf<NativeFlowEvent>();
            for (var index = 0; index < events.Length; index++)
            {
                var nativeEvent = Marshal.PtrToStructure<NativeFlowEvent>(
                    IntPtr.Add(buffer, FlowEventHeaderSize + (index * eventSize)));
                events[index] = new(
                    nativeEvent.Sequence,
                    nativeEvent.ProcessId,
                    nativeEvent.ApplicationIdHash,
                    nativeEvent.IpVersion,
                    nativeEvent.IpProtocol,
                    nativeEvent.Direction switch
                    {
                        0 => WfpFlowDirection.Outbound,
                        1 => WfpFlowDirection.Inbound,
                        _ => WfpFlowDirection.Unknown,
                    },
                    FormatAddress(nativeEvent.IpVersion, nativeEvent.LocalAddress),
                    nativeEvent.LocalPort,
                    FormatAddress(nativeEvent.IpVersion, nativeEvent.RemoteAddress),
                    nativeEvent.RemotePort);
            }

            return new()
            {
                IsAvailable = true,
                LatestSequence = header.LatestSequence,
                Events = events,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static WfpDriverStatus Unavailable(string message) => new()
    {
        IsAvailable = false,
        Message = message,
    };

    private static WfpFlowSnapshot UnavailableFlows(string message) => new()
    {
        IsAvailable = false,
        Message = message,
    };

    internal static string? FormatAddress(uint ipVersion, byte[]? address)
    {
        if (ipVersion == 4 && address is { Length: >= 4 })
        {
            return new IPAddress(address.AsSpan(0, 4)).ToString();
        }
        if (ipVersion == 6 && address is { Length: 16 })
        {
            return new IPAddress(address).ToString();
        }

        return null;
    }

    internal static string? ValidateCapabilities(
        uint bytesReturned,
        uint structureSize,
        uint apiMajor,
        uint apiMinor,
        uint capabilityFlags,
        uint registeredCallouts)
    {
        if (bytesReturned != ExpectedSize || structureSize != ExpectedSize)
        {
            return "The driver returned an incompatible capability structure.";
        }
        if (apiMajor != ExpectedApiMajor || apiMinor < MinimumApiMinor)
        {
            return $"The driver API {apiMajor}.{apiMinor} is incompatible with the required API {ExpectedApiMajor}.{MinimumApiMinor} or newer.";
        }
        if ((capabilityFlags & RequiredCapabilityFlags) != RequiredCapabilityFlags)
        {
            return "The driver does not report every required pass-through telemetry capability.";
        }
        if (registeredCallouts != ExpectedRegisteredCallouts)
        {
            return $"The driver registered {registeredCallouts} callouts; {ExpectedRegisteredCallouts} are required.";
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCapabilities
    {
        public uint Size;
        public uint ApiMajor;
        public uint ApiMinor;
        public uint Flags;
        public uint RegisteredCallouts;
        public uint Reserved;
        public ulong ClassificationCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFlowEventBatchHeader
    {
        public uint Size;
        public uint Count;
        public ulong LatestSequence;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFlowEvent
    {
        public ulong Sequence;
        public ulong ProcessId;
        public ulong ApplicationIdHash;
        public uint IpVersion;
        public byte IpProtocol;
        public byte Direction;
        public ushort Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public ushort LocalPort;
        public ushort RemotePort;
        public uint Reserved2;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        out NativeCapabilities outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
