namespace OpenLimiter.Windows.Processes;

public sealed record ProcessGroupSnapshot(
    string Name,
    string? ExecutablePath,
    IReadOnlyList<int> ProcessIds)
{
    public int InstanceCount => ProcessIds.Count;
}

