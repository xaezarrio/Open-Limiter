using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

public sealed class EtwNetworkTrafficMonitor(ILogger<EtwNetworkTrafficMonitor> logger) : BackgroundService, INetworkTrafficMonitor
{
    private const string SessionName = "OpenLimiter.NetworkTraffic";
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private readonly NetworkTrafficAccumulator accumulator = new();
    private readonly object snapshotGate = new();
    private NetworkTrafficSnapshot snapshot = new()
    {
        IsAvailable = false,
        Message = "Network traffic monitoring has not started.",
    };

    public NetworkTrafficSnapshot GetSnapshot()
    {
        lock (snapshotGate)
        {
            return snapshot;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.NetworkTCPIP |
                KernelTraceEventParser.Keywords.Process);
            session.Source.Kernel.TcpIpSend += data => accumulator.RecordUpload(data.ProcessID, data.size);
            session.Source.Kernel.TcpIpRecv += data => accumulator.RecordDownload(data.ProcessID, data.size);
            session.Source.Kernel.UdpIpSend += data => accumulator.RecordUpload(data.ProcessID, data.size);
            session.Source.Kernel.UdpIpRecv += data => accumulator.RecordDownload(data.ProcessID, data.size);
            session.Source.Kernel.ProcessStart += data => accumulator.ResetProcess(data.ProcessID);
            session.Source.Kernel.ProcessStop += data => accumulator.ResetProcess(data.ProcessID);

            using var stopRegistration = stoppingToken.Register(
                static state => { ((TraceEventSession)state!).Stop(); },
                session);
            var processingTask = Task.Run(() => session.Source.Process(), CancellationToken.None);
            using var timer = new PeriodicTimer(SampleInterval);
            SetSnapshot(accumulator.TakeSnapshot());
            logger.LogInformation("ETW network traffic monitoring started.");

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                SetSnapshot(accumulator.TakeSnapshot());
            }

            session.Stop();
            await processingTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ETW network traffic monitoring is unavailable.");
            SetSnapshot(new()
            {
                IsAvailable = false,
                Message = $"Windows ETW network monitoring failed: {exception.Message}",
                SampledAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private void SetSnapshot(NetworkTrafficSnapshot value)
    {
        lock (snapshotGate)
        {
            snapshot = value;
        }
    }
}
