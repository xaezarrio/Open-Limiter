using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;

namespace OpenLimiter.Core.Tests;

public sealed class JsonRuleStoreTests
{
    [Fact]
    public async Task Save_and_load_preserves_rules()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenLimiter.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "rules.json");

        try
        {
            var expected = new ApplicationRule
            {
                Id = Guid.NewGuid(),
                DisplayName = "Browser",
                ExecutablePath = @"C:\Apps\browser.exe",
                BlockedDirections = TrafficDirection.Inbound,
                UploadLimitBitsPerSecond = 2_000_000,
            };
            var store = new JsonRuleStore(path);

            await store.SaveAsync([expected]);
            var actual = await store.LoadAsync();

            Assert.Equal([expected], actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Cancelled_save_preserves_previous_rules_and_removes_temporary_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenLimiter.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "rules.json");
        var original = CreateRule("Original");
        var store = new JsonRuleStore(path);

        try
        {
            await store.SaveAsync([original]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.SaveAsync([CreateRule("Replacement")], cancellation.Token));

            Assert.Equal([original], await store.LoadAsync());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ApplicationRule CreateRule(string name) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        ExecutablePath = @"C:\Apps\browser.exe",
        BlockedDirections = TrafficDirection.Inbound,
    };
}
