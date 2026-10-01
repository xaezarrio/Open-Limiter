using System.Collections.Concurrent;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

internal sealed class NetworkTrafficAccumulator(TimeProvider? timeProvider = null)
{
    private readonly ConcurrentDictionary<int, ProcessTrafficCounter> counters = [];
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private DateTimeOffset previousSampleAtUtc = (timeProvider ?? TimeProvider.System).GetUtcNow();

    public void RecordUpload(int processId, int bytes) => Record(processId, bytes, upload: true);

    public void RecordDownload(int processId, int bytes) => Record(processId, bytes, upload: false);

    public void ResetProcess(int processId)
    {
        if (processId > 0)
        {
            counters.TryRemove(processId, out _);
        }
    }

    public NetworkTrafficSnapshot TakeSnapshot()
    {
        var sampledAtUtc = timeProvider.GetUtcNow();
        var elapsedSeconds = Math.Max(0.001, (sampledAtUtc - previousSampleAtUtc).TotalSeconds);
        previousSampleAtUtc = sampledAtUtc;
        var samples = new List<NetworkTrafficSample>(counters.Count);

        foreach (var pair in counters)
        {
            var intervalUpload = Interlocked.Exchange(ref pair.Value.PendingUploadBytes, 0);
            var intervalDownload = Interlocked.Exchange(ref pair.Value.PendingDownloadBytes, 0);
            var totalUpload = Interlocked.Read(ref pair.Value.TotalUploadBytes);
            var totalDownload = Interlocked.Read(ref pair.Value.TotalDownloadBytes);
            samples.Add(new(
                pair.Key,
                Rate(intervalUpload, elapsedSeconds),
                Rate(intervalDownload, elapsedSeconds),
                totalUpload,
                totalDownload));
        }

        return new()
        {
            IsAvailable = true,
            Message = "Measured from Windows kernel network events.",
            SampledAtUtc = sampledAtUtc,
            SampleIntervalSeconds = elapsedSeconds,
            Samples = samples,
        };
    }

    private void Record(int processId, int bytes, bool upload)
    {
        if (processId <= 0 || bytes <= 0)
        {
            return;
        }

        var counter = counters.GetOrAdd(processId, static _ => new());
        if (upload)
        {
            Interlocked.Add(ref counter.PendingUploadBytes, bytes);
            Interlocked.Add(ref counter.TotalUploadBytes, bytes);
        }
        else
        {
            Interlocked.Add(ref counter.PendingDownloadBytes, bytes);
            Interlocked.Add(ref counter.TotalDownloadBytes, bytes);
        }
    }

    private static long Rate(long bytes, double elapsedSeconds) =>
        checked((long)Math.Round(bytes / elapsedSeconds, MidpointRounding.AwayFromZero));

    private sealed class ProcessTrafficCounter
    {
        public long PendingUploadBytes;

        public long PendingDownloadBytes;

        public long TotalUploadBytes;

        public long TotalDownloadBytes;
    }
}
