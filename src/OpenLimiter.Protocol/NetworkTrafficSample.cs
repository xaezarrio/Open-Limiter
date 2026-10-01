namespace OpenLimiter.Protocol;

public sealed record NetworkTrafficSample(
    int ProcessId,
    long UploadBytesPerSecond,
    long DownloadBytesPerSecond,
    long TotalUploadBytes,
    long TotalDownloadBytes);
