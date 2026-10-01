using System.IO.Pipes;
using System.Security.Principal;
using OpenLimiter.Core.Models;
using OpenLimiter.Protocol;

namespace OpenLimiter.Windows.Service;

public sealed class PolicyServiceClient(
    string pipeName = PolicyServiceProtocol.PipeName,
    TimeSpan? connectionTimeout = null,
    TimeSpan? queryTimeout = null,
    TimeSpan? mutationTimeout = null) : IPolicyServiceClient
{
    private readonly TimeSpan connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(3);
    private readonly TimeSpan queryTimeout = queryTimeout ?? TimeSpan.FromSeconds(10);
    private readonly TimeSpan mutationTimeout = mutationTimeout ?? TimeSpan.FromSeconds(45);

    public Task<PolicyResponse> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.Ping,
        }, queryTimeout, cancellationToken);

    public async Task<NetworkTrafficSnapshot> GetNetworkTrafficAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListNetworkTraffic,
        }, queryTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.NetworkTraffic
            ?? new NetworkTrafficSnapshot { IsAvailable = false, Message = "Policy service returned no network traffic status." };
    }

    public async Task<WfpFlowSnapshot> GetDriverFlowsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListDriverFlows,
        }, queryTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.DriverFlows
            ?? new WfpFlowSnapshot { IsAvailable = false, Message = "Policy service returned no driver flow status." };
    }

    public async Task<IReadOnlyList<ApplicationRule>> ListRulesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListRules,
        }, queryTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.Rules;
    }

    public async Task<EnforcementResult> ApplyAsync(ApplicationRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ApplyRule,
            Rule = rule,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.Enforcement
            ?? EnforcementResult.Failure(["Policy service returned no enforcement result."]);
    }

    public async Task<EnforcementResult> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.RemoveRule,
            RuleId = ruleId,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.Enforcement
            ?? EnforcementResult.Failure(["Policy service returned no enforcement result."]);
    }

    public async Task<EnforcementResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ClearRules,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.Enforcement
            ?? EnforcementResult.Failure(["Policy service returned no enforcement result."]);
    }

    public async Task<EnforcementResult> ReplaceRulesAsync(
        IReadOnlyList<ApplicationRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ReplaceRules,
            Rules = rules,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.Enforcement
            ?? EnforcementResult.Failure(["Policy service returned no enforcement result."]);
    }

    public async Task<AutomationSnapshot> GetAutomationAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListAutomation,
        }, queryTimeout, cancellationToken);
        EnsureSucceeded(response);
        return ToAutomationSnapshot(response);
    }

    public async Task<AutomationSnapshot> SaveProfileAsync(PolicyProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.SaveProfile,
            Profile = profile,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return ToAutomationSnapshot(response);
    }

    public async Task<AutomationSnapshot> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.DeleteProfile,
            ProfileId = profileId,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return ToAutomationSnapshot(response);
    }

    public async Task<EnforcementResult> ActivateProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ActivateProfile,
            ProfileId = profileId,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return response.Enforcement
            ?? EnforcementResult.Failure(["Policy service returned no enforcement result."]);
    }

    public async Task<AutomationSnapshot> SaveScheduleAsync(PolicySchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.SaveSchedule,
            Schedule = schedule,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return ToAutomationSnapshot(response);
    }

    public async Task<AutomationSnapshot> DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.DeleteSchedule,
            ScheduleId = scheduleId,
        }, mutationTimeout, cancellationToken);
        EnsureSucceeded(response);
        return ToAutomationSnapshot(response);
    }

    private static AutomationSnapshot ToAutomationSnapshot(PolicyResponse response) => new()
    {
        Profiles = response.Profiles,
        Schedules = response.Schedules,
    };

    private async Task<PolicyResponse> SendAsync(
        PolicyRequest request,
        TimeSpan responseTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            using (var connectTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeoutSource.CancelAfter(connectionTimeout);
                await pipe.ConnectAsync(connectTimeoutSource.Token);
            }

            using var responseTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseTimeoutSource.CancelAfter(responseTimeout);
            await PipeMessageCodec.WriteAsync(pipe, request, responseTimeoutSource.Token);
            var response = await PipeMessageCodec.ReadAsync<PolicyResponse>(pipe, responseTimeoutSource.Token);

            if (response.Version != PolicyServiceProtocol.Version || response.RequestId != request.RequestId)
            {
                throw new InvalidDataException("Policy service returned a mismatched protocol response.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PolicyServiceUnavailableException("Policy service did not respond before the timeout.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PolicyServiceUnavailableException(
                "Policy service is unavailable or this Windows account is not authorized.",
                exception);
        }
    }

    private static void EnsureSucceeded(PolicyResponse response)
    {
        if (!response.Succeeded)
        {
            throw new InvalidOperationException(response.ErrorMessage ?? "Policy service rejected the request.");
        }
    }
}
