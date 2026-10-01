using System.Runtime.InteropServices;
using OpenLimiter.Protocol;
using OpenLimiter.Windows.Driver;
using OpenLimiter.Windows.Wfp;

namespace OpenLimiter.Windows.Tests;

public sealed class WfpPolicySessionTests
{
    [Fact]
    public void Native_structures_match_x64_wfp_layouts()
    {
        Assert.Equal(16, Marshal.SizeOf<FwpValue0>());
        Assert.Equal(20, Marshal.SizeOf<FwpmAction0>());
        Assert.Equal(72, Marshal.SizeOf<FwpmSession0>());
        Assert.Equal(64, Marshal.SizeOf<FwpmProvider0>());
        Assert.Equal(72, Marshal.SizeOf<FwpmSubLayer0>());
        Assert.Equal(88, Marshal.SizeOf<FwpmCallout0>());
        Assert.Equal(200, Marshal.SizeOf<FwpmFilter0>());
        Assert.Equal(72, WfpDriverClient.NativeFlowEventSize);
        Assert.Equal(4624, WfpDriverClient.ExpectedFlowEventBatchSize);
    }

    [Fact]
    public void Missing_driver_prevents_wfp_engine_mutation()
    {
        using var session = new WfpPolicySession(new UnavailableDriverClient());

        var activated = session.TryActivate();

        Assert.False(activated);
        Assert.False(session.IsActive);
        Assert.Equal("Driver missing for test.", session.Message);
    }

    [Fact]
    public void Application_identity_hash_is_stable_for_executable()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.");

        Assert.True(WfpApplicationIdentity.TryGetHash(executablePath, out var first));
        Assert.True(WfpApplicationIdentity.TryGetHash(executablePath, out var second));
        Assert.NotEqual(0UL, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Application_identity_hash_mixes_original_blob_length()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        var fullLength = WfpApplicationIdentity.HashBytes(bytes, 4);
        var reportedLonger = WfpApplicationIdentity.HashBytes(bytes, 8);

        Assert.NotEqual(fullLength, reportedLonger);
    }

    [Theory]
    [InlineData(31, 32, 1, 2, 0x0F, 2)]
    [InlineData(32, 32, 2, 0, 0x0F, 2)]
    [InlineData(32, 32, 1, 1, 0x0F, 2)]
    [InlineData(32, 32, 1, 2, 0x07, 2)]
    [InlineData(32, 32, 1, 2, 0x0F, 1)]
    public void Driver_capability_validation_rejects_incompatible_contracts(
        uint bytesReturned,
        uint size,
        uint major,
        uint minor,
        uint flags,
        uint callouts)
    {
        Assert.NotNull(WfpDriverClient.ValidateCapabilities(bytesReturned, size, major, minor, flags, callouts));
    }

    [Fact]
    public void Driver_capability_validation_accepts_current_or_newer_minor_contract()
    {
        Assert.Null(WfpDriverClient.ValidateCapabilities(32, 32, 1, 3, 0x1F, 2));
    }

    [Theory]
    [InlineData(4, new byte[] { 127, 0, 0, 1 }, "127.0.0.1")]
    [InlineData(6, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, "::1")]
    public void Driver_endpoint_addresses_are_formatted_by_ip_version(
        uint ipVersion,
        byte[] address,
        string expected)
    {
        Assert.Equal(expected, WfpDriverClient.FormatAddress(ipVersion, address));
    }

    [Fact]
    public void Driver_endpoint_address_rejects_incompatible_length()
    {
        Assert.Null(WfpDriverClient.FormatAddress(6, new byte[4]));
    }

    private sealed class UnavailableDriverClient : IWfpDriverClient
    {
        public WfpDriverStatus GetStatus() => new()
        {
            IsAvailable = false,
            Message = "Driver missing for test.",
        };

        public WfpFlowSnapshot GetFlowEvents() => new()
        {
            IsAvailable = false,
            Message = "Driver missing for test.",
        };
    }
}
