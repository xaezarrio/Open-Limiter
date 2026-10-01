namespace OpenLimiter.Windows.Processes;

public sealed record ProcessSnapshot(int ProcessId, string Name, string? ExecutablePath);

