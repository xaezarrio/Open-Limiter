using OpenLimiter.Windows.Processes;

namespace OpenLimiter.Windows.Tests;

public sealed class ProcessCatalogTests
{
    [Fact]
    public void GroupByExecutable_combines_instances_with_the_same_path()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(30, "chrome", @"C:\Program Files\Chrome\chrome.exe"),
            new ProcessSnapshot(10, "chrome", @"c:\program files\chrome\CHROME.EXE"),
            new ProcessSnapshot(20, "chrome", @"C:\Program Files\Chrome\chrome.exe"),
        };

        var group = Assert.Single(ProcessCatalog.GroupByExecutable(snapshots));

        Assert.Equal("chrome", group.Name);
        Assert.Equal(3, group.InstanceCount);
        Assert.Equal([10, 20, 30], group.ProcessIds);
    }

    [Fact]
    public void GroupByExecutable_keeps_same_name_at_different_paths_separate()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "worker", @"C:\Apps\One\worker.exe"),
            new ProcessSnapshot(20, "worker", @"C:\Apps\Two\worker.exe"),
        };

        var groups = ProcessCatalog.GroupByExecutable(snapshots);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal(1, group.InstanceCount));
    }

    [Fact]
    public void GroupByExecutable_combines_protected_instances_by_name()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "protected-service", null),
            new ProcessSnapshot(20, "Protected-Service", null),
        };

        var group = Assert.Single(ProcessCatalog.GroupByExecutable(snapshots));

        Assert.Null(group.ExecutablePath);
        Assert.Equal(2, group.InstanceCount);
        Assert.Equal([10, 20], group.ProcessIds);
    }

    [Fact]
    public void GroupByExecutable_attaches_hidden_instance_to_one_known_path()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "Bitwarden", null),
            new ProcessSnapshot(20, "Bitwarden", @"C:\Apps\Bitwarden\Bitwarden.exe"),
            new ProcessSnapshot(30, "Bitwarden", @"C:\Apps\Bitwarden\Bitwarden.exe"),
        };

        var group = Assert.Single(ProcessCatalog.GroupByExecutable(snapshots));

        Assert.Equal(@"C:\Apps\Bitwarden\Bitwarden.exe", group.ExecutablePath);
        Assert.Equal(3, group.InstanceCount);
    }

    [Fact]
    public void GroupByExecutable_keeps_hidden_instance_separate_when_path_is_ambiguous()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "worker", null),
            new ProcessSnapshot(20, "worker", @"C:\Apps\One\worker.exe"),
            new ProcessSnapshot(30, "worker", @"C:\Apps\Two\worker.exe"),
        };

        var groups = ProcessCatalog.GroupByExecutable(snapshots);

        Assert.Equal(3, groups.Count);
        Assert.Single(groups, group => group.ExecutablePath is null);
    }
}
