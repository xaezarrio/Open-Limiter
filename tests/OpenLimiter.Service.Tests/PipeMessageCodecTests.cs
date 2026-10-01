using System.Buffers.Binary;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service.Tests;

public sealed class PipeMessageCodecTests
{
    [Fact]
    public async Task Round_trip_preserves_request()
    {
        var request = new PolicyRequest
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.Ping,
        };
        await using var stream = new MemoryStream();

        await PipeMessageCodec.WriteAsync(stream, request);
        stream.Position = 0;
        var result = await PipeMessageCodec.ReadAsync<PolicyRequest>(stream);

        Assert.Equal(request, result);
    }

    [Fact]
    public async Task Round_trip_preserves_driver_flow_snapshot()
    {
        var response = new PolicyResponse
        {
            RequestId = Guid.NewGuid(),
            Succeeded = true,
            DriverFlows = new()
            {
                IsAvailable = true,
                LatestSequence = 91,
                Events =
                [
                    new(90, 1204, 0xAABBCCDDEEFF0011, 4, 6, WfpFlowDirection.Outbound,
                        "192.0.2.10", 49152, "198.51.100.20", 443),
                    new(91, 1204, 0xAABBCCDDEEFF0011, 6, 17, WfpFlowDirection.Inbound,
                        "2001:db8::10", 5353, "2001:db8::20", 5353),
                ],
            },
        };
        await using var stream = new MemoryStream();

        await PipeMessageCodec.WriteAsync(stream, response);
        stream.Position = 0;
        var result = await PipeMessageCodec.ReadAsync<PolicyResponse>(stream);

        var snapshot = Assert.IsType<WfpFlowSnapshot>(result.DriverFlows);
        Assert.Equal(91UL, snapshot.LatestSequence);
        Assert.Collection(
            snapshot.Events,
            item =>
            {
                Assert.Equal(4U, item.IpVersion);
                Assert.Equal(WfpFlowDirection.Outbound, item.Direction);
                Assert.Equal("192.0.2.10", item.LocalAddress);
                Assert.Equal((ushort)49152, item.LocalPort);
                Assert.Equal("198.51.100.20", item.RemoteAddress);
                Assert.Equal((ushort)443, item.RemotePort);
            },
            item =>
            {
                Assert.Equal(6U, item.IpVersion);
                Assert.Equal(WfpFlowDirection.Inbound, item.Direction);
                Assert.Equal("2001:db8::10", item.LocalAddress);
                Assert.Equal("2001:db8::20", item.RemoteAddress);
            });
    }

    [Fact]
    public async Task Read_rejects_oversized_frame_before_allocating_payload()
    {
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, PolicyServiceProtocol.MaximumMessageBytes + 1);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PipeMessageCodec.ReadAsync<PolicyRequest>(stream));
    }
}
