using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using OpenLimiter.Core.Models;
using OpenLimiter.Protocol;
using OpenLimiter.Windows.Service;

namespace OpenLimiter.Service.Tests;

public sealed class PolicyPipeIntegrationTests
{
    [Fact]
    public async Task Authorized_local_client_can_ping_server()
    {
        var currentSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows SID is unavailable.");
        var pipeName = $"OpenLimiter.Tests.{Guid.NewGuid():N}";
        var settings = new ServiceSettings(Path.GetTempPath(), pipeName, currentSid);
        var server = new PolicyPipeServer(
            settings,
            new PingHandler(),
            NullLogger<PolicyPipeServer>.Instance);
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(cancellation.Token);

        var response = await new PolicyServiceClient(pipeName, TimeSpan.FromSeconds(5)).PingAsync();

        Assert.True(response.Succeeded);
        Assert.Equal("test", response.ServiceVersion);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    [Fact]
    public async Task Authorized_local_client_can_send_versioned_rule_set()
    {
        var currentSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows SID is unavailable.");
        var pipeName = $"OpenLimiter.Tests.{Guid.NewGuid():N}";
        var handler = new CaptureReplaceHandler();
        var server = new PolicyPipeServer(
            new ServiceSettings(Path.GetTempPath(), pipeName, currentSid),
            handler,
            NullLogger<PolicyPipeServer>.Instance);
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(cancellation.Token);
        var rule = new ApplicationRule
        {
            Id = Guid.NewGuid(),
            DisplayName = "Browser",
            ExecutablePath = @"C:\Apps\browser.exe",
            BlockedDirections = TrafficDirection.Both,
        };

        var result = await new PolicyServiceClient(pipeName, TimeSpan.FromSeconds(5)).ReplaceRulesAsync([rule]);

        Assert.True(result.Succeeded);
        Assert.Equal(rule, Assert.Single(Assert.IsType<PolicyRequest>(handler.Request).Rules!));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    [Fact]
    public async Task Mutation_can_outlive_the_short_query_timeout()
    {
        var currentSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows SID is unavailable.");
        var pipeName = $"OpenLimiter.Tests.{Guid.NewGuid():N}";
        var server = new PolicyPipeServer(
            new ServiceSettings(Path.GetTempPath(), pipeName, currentSid),
            new DelayedApplyHandler(),
            NullLogger<PolicyPipeServer>.Instance);
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(cancellation.Token);
        var client = new PolicyServiceClient(
            pipeName,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(1));
        var rule = new ApplicationRule
        {
            Id = Guid.NewGuid(),
            DisplayName = "Browser",
            ExecutablePath = @"C:\Apps\browser.exe",
            UploadLimitBitsPerSecond = 1_000_000,
        };

        var result = await client.ApplyAsync(rule);

        Assert.True(result.Succeeded);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    private sealed class PingHandler : IPolicyRequestHandler
    {
        public Task<PolicyResponse> HandleAsync(
            PolicyRequest request,
            string caller,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PolicyResponse
            {
                RequestId = request.RequestId,
                Succeeded = request.Kind == PolicyRequestKind.Ping && !string.IsNullOrWhiteSpace(caller),
                ServiceVersion = "test",
            });
    }

    private sealed class CaptureReplaceHandler : IPolicyRequestHandler
    {
        public PolicyRequest? Request { get; private set; }

        public Task<PolicyResponse> HandleAsync(
            PolicyRequest request,
            string caller,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new PolicyResponse
            {
                RequestId = request.RequestId,
                Succeeded = request.Kind == PolicyRequestKind.ReplaceRules,
                Enforcement = EnforcementResult.Success(["Imported test rule set."]),
            });
        }
    }

    private sealed class DelayedApplyHandler : IPolicyRequestHandler
    {
        public async Task<PolicyResponse> HandleAsync(
            PolicyRequest request,
            string caller,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            return new PolicyResponse
            {
                RequestId = request.RequestId,
                Succeeded = request.Kind == PolicyRequestKind.ApplyRule,
                Enforcement = EnforcementResult.Success(["Applied delayed test rule."]),
            };
        }
    }
}
