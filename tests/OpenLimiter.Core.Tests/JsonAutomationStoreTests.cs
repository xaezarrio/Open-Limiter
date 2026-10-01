using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;

namespace OpenLimiter.Core.Tests;

public sealed class JsonAutomationStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"openlimiter-automation-{Guid.NewGuid():N}");

    [Fact]
    public async Task Save_and_load_round_trip_profiles_and_schedules()
    {
        var path = Path.Combine(directory, "automation.json");
        var store = new JsonAutomationStore(path);
        var profile = new PolicyProfile
        {
            Id = Guid.NewGuid(),
            Name = "Work",
            Rules = [],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var schedule = new PolicySchedule
        {
            Id = Guid.NewGuid(),
            Name = "Morning",
            ProfileId = profile.Id,
            Days = ScheduleDays.Weekdays,
            Hour = 9,
        };

        await store.SaveAsync(new() { Profiles = [profile], Schedules = [schedule] });
        var loaded = await store.LoadAsync();

        var loadedProfile = Assert.Single(loaded.Profiles);
        Assert.Equal(profile.Id, loadedProfile.Id);
        Assert.Equal(profile.Name, loadedProfile.Name);
        Assert.Empty(loadedProfile.Rules);
        Assert.Equal(schedule, Assert.Single(loaded.Schedules));
    }

    [Fact]
    public async Task Load_rejects_null_collections()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "automation.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"profiles\":null,\"schedules\":null}");

        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonAutomationStore(path).LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
