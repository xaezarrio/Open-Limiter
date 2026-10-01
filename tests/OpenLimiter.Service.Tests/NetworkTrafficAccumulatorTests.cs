namespace OpenLimiter.Service.Tests;

public sealed class NetworkTrafficAccumulatorTests
{
    [Fact]
    public void TakeSnapshot_reports_directional_rates_and_running_totals()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var accumulator = new NetworkTrafficAccumulator(time);
        accumulator.RecordUpload(42, 1_000);
        accumulator.RecordDownload(42, 3_000);
        time.Advance(TimeSpan.FromSeconds(2));

        var first = Assert.Single(accumulator.TakeSnapshot().Samples);

        Assert.Equal(500, first.UploadBytesPerSecond);
        Assert.Equal(1_500, first.DownloadBytesPerSecond);
        Assert.Equal(1_000, first.TotalUploadBytes);
        Assert.Equal(3_000, first.TotalDownloadBytes);

        time.Advance(TimeSpan.FromSeconds(1));
        var second = Assert.Single(accumulator.TakeSnapshot().Samples);
        Assert.Equal(0, second.UploadBytesPerSecond);
        Assert.Equal(0, second.DownloadBytesPerSecond);
        Assert.Equal(1_000, second.TotalUploadBytes);
        Assert.Equal(3_000, second.TotalDownloadBytes);
    }

    [Fact]
    public void ResetProcess_discards_counters_for_reused_process_id()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var accumulator = new NetworkTrafficAccumulator(time);
        accumulator.RecordUpload(42, 1_000);

        accumulator.ResetProcess(42);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(accumulator.TakeSnapshot().Samples);
    }

    private sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset current = current;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan value) => current += value;
    }
}
