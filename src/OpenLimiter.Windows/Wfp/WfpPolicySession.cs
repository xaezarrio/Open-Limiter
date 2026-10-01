using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenLimiter.Windows.Driver;

namespace OpenLimiter.Windows.Wfp;

public sealed class WfpPolicySession(IWfpDriverClient driverClient) : IWfpPolicySession
{
    private const uint RpcCAuthnWinnt = 10;
    private const uint SessionFlagDynamic = 0x00000001;
    private const uint ActionCalloutInspection = 0x00006004;

    private static readonly Guid ProviderKey = new("a95745a6-70cf-42fc-aee8-47d778ed1f45");
    private static readonly Guid SubLayerKey = new("098810b7-a34b-43cc-bff1-c9e049601752");
    private static readonly Guid CalloutFlowV4Key = new("353e9d70-8ad0-4475-917d-0b9a57b822f6");
    private static readonly Guid CalloutFlowV6Key = new("c3610b17-8873-47ea-8db2-4d772b5eed13");
    private static readonly Guid FilterFlowV4Key = new("91c33d5b-4208-42b1-8d1e-d36c6bbdcef1");
    private static readonly Guid FilterFlowV6Key = new("a6d1b260-6d7d-48db-8e26-1eeaf160488d");
    private static readonly Guid LayerFlowV4Key = new("af80470a-5596-4c13-9992-539e6fe57967");
    private static readonly Guid LayerFlowV6Key = new("7021d2b3-dfa4-406e-afeb-6afaf7e70efd");

    private SafeWfpEngineHandle? engine;

    public bool IsActive => engine is { IsInvalid: false, IsClosed: false };

    public string? Message { get; private set; }

    public bool TryActivate()
    {
        if (IsActive)
        {
            return true;
        }

        if (nint.Size != 8)
        {
            Message = "The WFP policy session currently supports 64-bit Windows processes only.";
            return false;
        }

        var driver = driverClient.GetStatus();
        if (!driver.IsAvailable)
        {
            Message = driver.Message ?? "The OpenLimiter WFP driver is unavailable.";
            return false;
        }

        using var allocations = new NativeAllocationScope();
        var session = new FwpmSession0
        {
            DisplayData = new(allocations.String("OpenLimiter policy service session"), nint.Zero),
            Flags = SessionFlagDynamic,
            TransactionWaitTimeoutMilliseconds = 5000,
        };

        SafeWfpEngineHandle? candidate = null;
        var transactionOpen = false;
        try
        {
            EnsureSuccess(
                NativeMethods.FwpmEngineOpen0(null, RpcCAuthnWinnt, nint.Zero, ref session, out candidate),
                "open the WFP engine");
            EnsureSuccess(NativeMethods.FwpmTransactionBegin0(candidate, 0), "begin the WFP transaction");
            transactionOpen = true;

            var providerKeyPointer = allocations.Guid(ProviderKey);
            AddProvider(candidate, allocations);
            AddSubLayer(candidate, allocations, providerKeyPointer);
            AddCallout(candidate, allocations, providerKeyPointer, CalloutFlowV4Key, LayerFlowV4Key, "OpenLimiter established-flow IPv4 callout");
            AddCallout(candidate, allocations, providerKeyPointer, CalloutFlowV6Key, LayerFlowV6Key, "OpenLimiter established-flow IPv6 callout");
            AddFilter(candidate, allocations, providerKeyPointer, FilterFlowV4Key, CalloutFlowV4Key, LayerFlowV4Key, "OpenLimiter established-flow IPv4 filter");
            AddFilter(candidate, allocations, providerKeyPointer, FilterFlowV6Key, CalloutFlowV6Key, LayerFlowV6Key, "OpenLimiter established-flow IPv6 filter");

            EnsureSuccess(NativeMethods.FwpmTransactionCommit0(candidate), "commit the WFP transaction");
            transactionOpen = false;
            engine = candidate;
            candidate = null;
            Message = "Dynamic IPv4 and IPv6 flow inspection filters are active.";
            return true;
        }
        catch (WfpPolicyException exception)
        {
            Message = exception.Message;
            return false;
        }
        finally
        {
            if (transactionOpen && candidate is not null && !candidate.IsInvalid)
            {
                NativeMethods.FwpmTransactionAbort0(candidate);
            }

            candidate?.Dispose();
        }
    }

    public void Dispose()
    {
        engine?.Dispose();
        engine = null;
    }

    private static void AddProvider(SafeWfpEngineHandle candidate, NativeAllocationScope allocations)
    {
        var provider = new FwpmProvider0
        {
            ProviderKey = ProviderKey,
            DisplayData = new(allocations.String("OpenLimiter provider"), nint.Zero),
        };
        EnsureSuccess(NativeMethods.FwpmProviderAdd0(candidate, ref provider, nint.Zero), "add the WFP provider");
    }

    private static void AddSubLayer(
        SafeWfpEngineHandle candidate,
        NativeAllocationScope allocations,
        nint providerKeyPointer)
    {
        var subLayer = new FwpmSubLayer0
        {
            SubLayerKey = SubLayerKey,
            DisplayData = new(allocations.String("OpenLimiter flow sublayer"), nint.Zero),
            ProviderKey = providerKeyPointer,
            Weight = 0x100,
        };
        EnsureSuccess(NativeMethods.FwpmSubLayerAdd0(candidate, ref subLayer, nint.Zero), "add the WFP sublayer");
    }

    private static void AddCallout(
        SafeWfpEngineHandle candidate,
        NativeAllocationScope allocations,
        nint providerKeyPointer,
        Guid calloutKey,
        Guid layerKey,
        string name)
    {
        var callout = new FwpmCallout0
        {
            CalloutKey = calloutKey,
            DisplayData = new(allocations.String(name), nint.Zero),
            ProviderKey = providerKeyPointer,
            ApplicableLayer = layerKey,
        };
        EnsureSuccess(NativeMethods.FwpmCalloutAdd0(candidate, ref callout, nint.Zero, nint.Zero), $"add {name}");
    }

    private static void AddFilter(
        SafeWfpEngineHandle candidate,
        NativeAllocationScope allocations,
        nint providerKeyPointer,
        Guid filterKey,
        Guid calloutKey,
        Guid layerKey,
        string name)
    {
        var filter = new FwpmFilter0
        {
            FilterKey = filterKey,
            DisplayData = new(allocations.String(name), nint.Zero),
            ProviderKey = providerKeyPointer,
            LayerKey = layerKey,
            SubLayerKey = SubLayerKey,
            Action = new(ActionCalloutInspection, calloutKey),
        };
        EnsureSuccess(NativeMethods.FwpmFilterAdd0(candidate, ref filter, nint.Zero, nint.Zero), $"add {name}");
    }

    private static void EnsureSuccess(uint result, string operation)
    {
        if (result == 0)
        {
            return;
        }

        var description = new Win32Exception(unchecked((int)result)).Message;
        throw new WfpPolicyException($"Could not {operation}: 0x{result:X8} ({description})");
    }

    private sealed class WfpPolicyException(string message) : Exception(message);
}

internal sealed class NativeAllocationScope : IDisposable
{
    private readonly List<nint> allocations = [];

    public nint String(string value)
    {
        var pointer = Marshal.StringToHGlobalUni(value);
        allocations.Add(pointer);
        return pointer;
    }

    public nint Guid(Guid value)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        Marshal.StructureToPtr(value, pointer, fDeleteOld: false);
        allocations.Add(pointer);
        return pointer;
    }

    public void Dispose()
    {
        foreach (var pointer in allocations)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}

internal sealed class SafeWfpEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeWfpEngineHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.FwpmEngineClose0(handle) == 0;
}

internal static class NativeMethods
{
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    internal static extern uint FwpmEngineOpen0(
        string? serverName,
        uint authenticationService,
        nint authenticationIdentity,
        ref FwpmSession0 session,
        out SafeWfpEngineHandle engineHandle);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmEngineClose0(nint engineHandle);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmTransactionBegin0(SafeWfpEngineHandle engineHandle, uint flags);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmTransactionCommit0(SafeWfpEngineHandle engineHandle);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmTransactionAbort0(SafeWfpEngineHandle engineHandle);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmProviderAdd0(
        SafeWfpEngineHandle engineHandle,
        ref FwpmProvider0 provider,
        nint securityDescriptor);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmSubLayerAdd0(
        SafeWfpEngineHandle engineHandle,
        ref FwpmSubLayer0 subLayer,
        nint securityDescriptor);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmCalloutAdd0(
        SafeWfpEngineHandle engineHandle,
        ref FwpmCallout0 callout,
        nint securityDescriptor,
        nint calloutId);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmFilterAdd0(
        SafeWfpEngineHandle engineHandle,
        ref FwpmFilter0 filter,
        nint securityDescriptor,
        nint filterId);

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    internal static extern uint FwpmGetAppIdFromFileName0(
        string fileName,
        out nint applicationId);

    [DllImport("fwpuclnt.dll")]
    internal static extern void FwpmFreeMemory0(ref nint memory);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmDisplayData0(nint name, nint description)
{
    public nint Name = name;
    public nint Description = description;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpByteBlob
{
    public uint Size;
    public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpValue0
{
    public uint Type;
    public nint Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmAction0(uint type, Guid calloutKey)
{
    public uint Type = type;
    public Guid CalloutKey = calloutKey;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmSession0
{
    public Guid SessionKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public uint TransactionWaitTimeoutMilliseconds;
    public uint ProcessId;
    public nint Sid;
    public nint Username;
    [MarshalAs(UnmanagedType.Bool)] public bool KernelMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmProvider0
{
    public Guid ProviderKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public FwpByteBlob ProviderData;
    public nint ServiceName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmSubLayer0
{
    public Guid SubLayerKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public nint ProviderKey;
    public FwpByteBlob ProviderData;
    public ushort Weight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmCallout0
{
    public Guid CalloutKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public nint ProviderKey;
    public FwpByteBlob ProviderData;
    public Guid ApplicableLayer;
    public uint CalloutId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmFilter0
{
    public Guid FilterKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public nint ProviderKey;
    public FwpByteBlob ProviderData;
    public Guid LayerKey;
    public Guid SubLayerKey;
    public FwpValue0 Weight;
    public uint NumberOfFilterConditions;
    public nint FilterCondition;
    public FwpmAction0 Action;
    private uint actionUnionAlignmentPadding;
    public Guid Context;
    public nint Reserved;
    public ulong FilterId;
    public FwpValue0 EffectiveWeight;
}
