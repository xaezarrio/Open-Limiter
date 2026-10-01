using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Core.Validation;
using OpenLimiter.Protocol;
using OpenLimiter.Windows.Driver;
using OpenLimiter.Windows.Enforcement;
using OpenLimiter.Windows.Wfp;

namespace OpenLimiter.Service;

public sealed class PolicyRequestProcessor(
    IRuleStore ruleStore,
    IAutomationStore automationStore,
    IPolicyEnforcer enforcer,
    IWfpDriverClient driverClient,
    IWfpPolicySession wfpPolicySession,
    INetworkTrafficMonitor networkTrafficMonitor,
    TimeProvider timeProvider,
    IAuditLog auditLog,
    ILogger<PolicyRequestProcessor> logger) : IPolicyRequestHandler
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly SemaphoreSlim automationGate = new(1, 1);

    public async Task<PolicyResponse> HandleAsync(
        PolicyRequest request,
        string caller,
        CancellationToken cancellationToken = default)
    {
        var response = await HandleCoreAsync(request, cancellationToken);

        if (!IsAuditedMutation(request.Kind))
        {
            return response;
        }

        try
        {
            await auditLog.WriteAsync(new(
                DateTimeOffset.UtcNow,
                caller,
                request.RequestId,
                request.Kind,
                request.Rule?.Id ?? request.RuleId ?? request.Profile?.Id ?? request.ProfileId ?? request.Schedule?.Id ?? request.ScheduleId,
                response.Succeeded,
                response.ErrorCode), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not write the policy audit entry for request {RequestId}.", request.RequestId);
        }

        return response;
    }

    private static bool IsAuditedMutation(PolicyRequestKind kind) => kind is
        PolicyRequestKind.ApplyRule or
        PolicyRequestKind.RemoveRule or
        PolicyRequestKind.ClearRules or
        PolicyRequestKind.ReplaceRules or
        PolicyRequestKind.SaveProfile or
        PolicyRequestKind.DeleteProfile or
        PolicyRequestKind.ActivateProfile or
        PolicyRequestKind.SaveSchedule or
        PolicyRequestKind.DeleteSchedule or
        PolicyRequestKind.RunSchedule;

    private async Task<PolicyResponse> HandleCoreAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.Version != PolicyServiceProtocol.Version)
        {
            return Reject(request, "protocol_version", "The client protocol version is not supported.");
        }

        if (request.RequestId == Guid.Empty)
        {
            return Reject(request, "invalid_request", "Request ID cannot be empty.");
        }

        try
        {
            return request.Kind switch
            {
                PolicyRequestKind.Ping => Ping(request),
                PolicyRequestKind.ListNetworkTraffic => ListNetworkTraffic(request),
                PolicyRequestKind.ListDriverFlows => await ListDriverFlowsAsync(request, cancellationToken),
                PolicyRequestKind.ListRules => await ListRulesAsync(request, cancellationToken),
                PolicyRequestKind.ApplyRule => await ApplyRuleAsync(request, cancellationToken),
                PolicyRequestKind.RemoveRule => await RemoveRuleAsync(request, cancellationToken),
                PolicyRequestKind.ClearRules => await ClearRulesAsync(request, cancellationToken),
                PolicyRequestKind.ReplaceRules => await ReplaceRulesAsync(request, cancellationToken),
                PolicyRequestKind.ListAutomation => await ListAutomationAsync(request, cancellationToken),
                PolicyRequestKind.SaveProfile => await SaveProfileAsync(request, cancellationToken),
                PolicyRequestKind.DeleteProfile => await DeleteProfileAsync(request, cancellationToken),
                PolicyRequestKind.ActivateProfile => await ActivateProfileAsync(request, cancellationToken),
                PolicyRequestKind.SaveSchedule => await SaveScheduleAsync(request, cancellationToken),
                PolicyRequestKind.DeleteSchedule => await DeleteScheduleAsync(request, cancellationToken),
                PolicyRequestKind.RunSchedule => await RunScheduleAsync(request, cancellationToken),
                _ => Reject(request, "invalid_operation", "The requested operation is not supported."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Policy request {RequestId} failed unexpectedly.", request.RequestId);
            return Reject(request, "internal_error", "The policy service could not complete the request.");
        }
    }

    private async Task<PolicyResponse> ListAutomationAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ListAutomation cannot include a payload.");
        }

        var configuration = await automationStore.LoadAsync(cancellationToken);
        return AutomationSuccess(request, configuration);
    }

    private async Task<PolicyResponse> SaveProfileAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.Profile is not { } profile || request.ProfileId is not null || request.Schedule is not null || request.ScheduleId is not null ||
            request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "SaveProfile requires one profile payload.");
        }
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 64 || profile.Rules is null)
        {
            return Reject(request, "invalid_profile", "Profile requires a non-empty ID, a name up to 64 characters, and a rule collection.");
        }
        if (profile.Rules.Count > RuleSetDocumentSerializer.MaximumRules)
        {
            return Reject(request, "invalid_profile", $"A profile cannot contain more than {RuleSetDocumentSerializer.MaximumRules} rules.");
        }

        var normalizedRules = new List<ApplicationRule>(profile.Rules.Count);
        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profileRule in profile.Rules)
        {
            var normalized = NormalizeRule(profileRule with { Enabled = true, ExpiresAtUtc = null }, out var errors);
            if (normalized is null)
            {
                return Reject(request, "invalid_profile", $"Profile rule {profileRule.Id} is invalid: {string.Join(" ", errors)}");
            }
            if (!executablePaths.Add(normalized.ExecutablePath))
            {
                return Reject(request, "invalid_profile", $"Executable '{normalized.ExecutablePath}' occurs more than once.");
            }
            normalizedRules.Add(normalized);
        }

        await automationGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = await automationStore.LoadAsync(cancellationToken);
            var profiles = configuration.Profiles.Where(item => item.Id != profile.Id).ToList();
            if (profiles.Count >= JsonAutomationStore.MaximumProfiles)
            {
                return Reject(request, "profile_limit", $"No more than {JsonAutomationStore.MaximumProfiles} profiles are allowed.");
            }
            if (profiles.Any(item => string.Equals(item.Name, profile.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Reject(request, "duplicate_profile", "A profile with that name already exists.");
            }

            profiles.Add(profile with
            {
                Name = profile.Name.Trim(),
                Rules = normalizedRules,
                UpdatedAtUtc = timeProvider.GetUtcNow(),
            });
            var updated = configuration with { Profiles = profiles };
            await automationStore.SaveAsync(updated, cancellationToken);
            return AutomationSuccess(request, updated);
        }
        finally
        {
            automationGate.Release();
        }
    }

    private async Task<PolicyResponse> DeleteProfileAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.ProfileId is not { } profileId || profileId == Guid.Empty || request.Profile is not null ||
            request.Schedule is not null || request.ScheduleId is not null || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "DeleteProfile requires one profile ID.");
        }

        await automationGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = await automationStore.LoadAsync(cancellationToken);
            if (configuration.Schedules.Any(schedule => schedule.ProfileId == profileId))
            {
                return Reject(request, "profile_in_use", "Remove schedules that use this profile before deleting it.");
            }
            var profiles = configuration.Profiles.Where(profile => profile.Id != profileId).ToArray();
            if (profiles.Length == configuration.Profiles.Count)
            {
                return Reject(request, "profile_not_found", "Profile was not found.");
            }
            var updated = configuration with { Profiles = profiles };
            await automationStore.SaveAsync(updated, cancellationToken);
            return AutomationSuccess(request, updated);
        }
        finally
        {
            automationGate.Release();
        }
    }

    private async Task<PolicyResponse> ActivateProfileAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.ProfileId is not { } profileId || profileId == Guid.Empty || request.Profile is not null ||
            request.Schedule is not null || request.ScheduleId is not null || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ActivateProfile requires one profile ID.");
        }

        var configuration = await automationStore.LoadAsync(cancellationToken);
        var profile = configuration.Profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is null)
        {
            return Reject(request, "profile_not_found", "Profile was not found.");
        }

        return await ReplaceRulesAsync(request with
        {
            Kind = PolicyRequestKind.ReplaceRules,
            ProfileId = null,
            Rules = profile.Rules,
        }, cancellationToken);
    }

    private async Task<PolicyResponse> SaveScheduleAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.Schedule is not { } schedule || request.Profile is not null || request.ProfileId is not null || request.ScheduleId is not null ||
            request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "SaveSchedule requires one schedule payload.");
        }
        if (schedule.Id == Guid.Empty || string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Trim().Length > 64 ||
            schedule.ProfileId == Guid.Empty || schedule.Days == ScheduleDays.None || (schedule.Days & ~ScheduleDays.EveryDay) != 0 ||
            schedule.Hour is < 0 or > 23 || schedule.Minute is < 0 or > 59)
        {
            return Reject(request, "invalid_schedule", "Schedule requires a valid ID, name, profile, days, and local time.");
        }

        await automationGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = await automationStore.LoadAsync(cancellationToken);
            if (!configuration.Profiles.Any(profile => profile.Id == schedule.ProfileId))
            {
                return Reject(request, "profile_not_found", "Schedule profile was not found.");
            }
            var schedules = configuration.Schedules.Where(item => item.Id != schedule.Id).ToList();
            if (schedules.Count >= JsonAutomationStore.MaximumSchedules)
            {
                return Reject(request, "schedule_limit", $"No more than {JsonAutomationStore.MaximumSchedules} schedules are allowed.");
            }
            if (schedules.Any(item => string.Equals(item.Name, schedule.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Reject(request, "duplicate_schedule", "A schedule with that name already exists.");
            }
            var previous = configuration.Schedules.FirstOrDefault(item => item.Id == schedule.Id);
            schedules.Add(schedule with { Name = schedule.Name.Trim(), LastRunLocalDate = previous?.LastRunLocalDate });
            var updated = configuration with { Schedules = schedules };
            await automationStore.SaveAsync(updated, cancellationToken);
            return AutomationSuccess(request, updated);
        }
        finally
        {
            automationGate.Release();
        }
    }

    private async Task<PolicyResponse> DeleteScheduleAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.ScheduleId is not { } scheduleId || scheduleId == Guid.Empty || request.Profile is not null ||
            request.ProfileId is not null || request.Schedule is not null || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "DeleteSchedule requires one schedule ID.");
        }

        await automationGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = await automationStore.LoadAsync(cancellationToken);
            var schedules = configuration.Schedules.Where(schedule => schedule.Id != scheduleId).ToArray();
            if (schedules.Length == configuration.Schedules.Count)
            {
                return Reject(request, "schedule_not_found", "Schedule was not found.");
            }
            var updated = configuration with { Schedules = schedules };
            await automationStore.SaveAsync(updated, cancellationToken);
            return AutomationSuccess(request, updated);
        }
        finally
        {
            automationGate.Release();
        }
    }

    private async Task<PolicyResponse> RunScheduleAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.ScheduleId is not { } scheduleId || scheduleId == Guid.Empty || request.Profile is not null ||
            request.ProfileId is not null || request.Schedule is not null || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "RunSchedule requires one schedule ID.");
        }

        await automationGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = await automationStore.LoadAsync(cancellationToken);
            var schedule = configuration.Schedules.FirstOrDefault(item => item.Id == scheduleId);
            if (schedule is null || !schedule.Enabled)
            {
                return Reject(request, "schedule_not_found", "Enabled schedule was not found.");
            }
            var profile = configuration.Profiles.FirstOrDefault(item => item.Id == schedule.ProfileId);
            if (profile is null)
            {
                return Reject(request, "profile_not_found", "Schedule profile was not found.");
            }
            var activation = await ReplaceRulesAsync(request with
            {
                Kind = PolicyRequestKind.ReplaceRules,
                ScheduleId = null,
                Rules = profile.Rules,
            }, cancellationToken);
            if (!activation.Succeeded)
            {
                return activation;
            }

            var localDate = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            var schedules = configuration.Schedules
                .Select(item => item.Id == scheduleId ? item with { LastRunLocalDate = localDate } : item)
                .ToArray();
            var updated = configuration with { Schedules = schedules };
            await automationStore.SaveAsync(updated, cancellationToken);
            return activation with
            {
                Profiles = CreateProfileSummaries(updated),
                Schedules = updated.Schedules,
            };
        }
        finally
        {
            automationGate.Release();
        }
    }

    private static bool HasAutomationPayload(PolicyRequest request) =>
        request.Profile is not null || request.ProfileId is not null || request.Schedule is not null || request.ScheduleId is not null;

    private static PolicyResponse AutomationSuccess(PolicyRequest request, AutomationConfiguration configuration) =>
        Success(request) with
        {
            Profiles = CreateProfileSummaries(configuration),
            Schedules = configuration.Schedules,
        };

    private static IReadOnlyList<PolicyProfileSummary> CreateProfileSummaries(AutomationConfiguration configuration) =>
        configuration.Profiles
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new PolicyProfileSummary
            {
                Id = profile.Id,
                Name = profile.Name,
                RuleCount = profile.Rules.Count,
                UpdatedAtUtc = profile.UpdatedAtUtc,
            })
            .ToArray();

    private PolicyResponse ListNetworkTraffic(PolicyRequest request)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ListNetworkTraffic cannot include a rule payload.");
        }

        return Success(request) with { NetworkTraffic = networkTrafficMonitor.GetSnapshot() };
    }

    private async Task<PolicyResponse> ListDriverFlowsAsync(
        PolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ListDriverFlows cannot include a rule payload.");
        }

        var snapshot = driverClient.GetFlowEvents();
        if (snapshot.IsAvailable && snapshot.Events.Count > 0)
        {
            await mutationGate.WaitAsync(cancellationToken);
            try
            {
                var rules = await ruleStore.LoadAsync(cancellationToken);
                snapshot = snapshot with { Events = AssociateRules(snapshot.Events, rules) };
            }
            finally
            {
                mutationGate.Release();
            }
        }

        return Success(request) with
        {
            DriverFlows = snapshot with
            {
                PolicySessionActive = wfpPolicySession.IsActive,
                Message = wfpPolicySession.IsActive ? snapshot.Message : wfpPolicySession.Message ?? snapshot.Message,
            },
        };
    }

    private static IReadOnlyList<WfpFlowEvent> AssociateRules(
        IReadOnlyList<WfpFlowEvent> events,
        IReadOnlyList<ApplicationRule> rules)
    {
        var rulesByHash = new Dictionary<ulong, ApplicationRule>();
        var ambiguousHashes = new HashSet<ulong>();
        foreach (var rule in rules)
        {
            if (!WfpApplicationIdentity.TryGetHash(rule.ExecutablePath, out var hash))
            {
                continue;
            }
            if (!rulesByHash.TryAdd(hash, rule))
            {
                rulesByHash.Remove(hash);
                ambiguousHashes.Add(hash);
            }
        }

        return events.Select(flowEvent =>
        {
            if (ambiguousHashes.Contains(flowEvent.ApplicationIdHash) ||
                !rulesByHash.TryGetValue(flowEvent.ApplicationIdHash, out var rule))
            {
                return flowEvent;
            }

            return flowEvent with
            {
                RuleId = rule.Id,
                RuleDisplayName = rule.DisplayName,
            };
        }).ToArray();
    }

    private PolicyResponse Ping(PolicyRequest request)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "Ping cannot include a rule payload.");
        }

        var driver = driverClient.GetStatus();
        return Success(request) with
        {
            ServiceVersion = typeof(PolicyRequestProcessor).Assembly.GetName().Version?.ToString(),
            Driver = driver with
            {
                PolicySessionActive = wfpPolicySession.IsActive,
                PolicySessionMessage = wfpPolicySession.Message,
            },
        };
    }

    private async Task<PolicyResponse> ListRulesAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ListRules cannot include a rule payload.");
        }

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            return Success(request) with { Rules = await ruleStore.LoadAsync(cancellationToken) };
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<PolicyResponse> ApplyRuleAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ApplyRule requires one rule and no separate rule ID.");
        }

        var normalizedRule = NormalizeRule(request.Rule, out var validationErrors);
        if (normalizedRule is null)
        {
            return Reject(request, "invalid_rule", string.Join(" ", validationErrors));
        }
        if (normalizedRule.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return Reject(request, "invalid_rule", "Rule expiration must be in the future.");
        }

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var rules = (await ruleStore.LoadAsync(cancellationToken)).ToList();
            var previousRule = rules.FirstOrDefault(rule => rule.Id == normalizedRule.Id);
            if (previousRule is null && rules.Count >= RuleSetDocumentSerializer.MaximumRules)
            {
                return Reject(
                    request,
                    "rule_limit",
                    $"No more than {RuleSetDocumentSerializer.MaximumRules} saved rules are allowed.");
            }
            var duplicateExecutable = rules.FirstOrDefault(rule =>
                rule.Id != normalizedRule.Id &&
                string.Equals(rule.ExecutablePath, normalizedRule.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            if (duplicateExecutable is not null)
            {
                return Reject(
                    request,
                    "duplicate_executable",
                    $"Executable already has rule '{duplicateExecutable.Id}'. Update or remove that rule instead.");
            }

            var updatedRules = rules.Where(rule => rule.Id != normalizedRule.Id).ToList();
            updatedRules.Add(normalizedRule);
            if (TryGetRuleSetSizeError(updatedRules, out var sizeError))
            {
                return Reject(request, "rule_set_too_large", sizeError);
            }

            try
            {
                var enforcement = await enforcer.ApplyAsync(normalizedRule, cancellationToken);
                if (!enforcement.Succeeded)
                {
                    var rollback = previousRule is null
                        ? await enforcer.RemoveAsync(normalizedRule.Id, cancellationToken)
                        : await enforcer.ApplyAsync(previousRule, cancellationToken);
                    return EnforcementFailure(request, enforcement, rollback);
                }

                try
                {
                    await ruleStore.SaveAsync(updatedRules, cancellationToken);
                }
                catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
                {
                    var rollback = previousRule is null
                        ? await enforcer.RemoveAsync(normalizedRule.Id, cancellationToken)
                        : await enforcer.ApplyAsync(previousRule, cancellationToken);
                    return PersistenceFailure(request, persistenceException, rollback);
                }

                return Success(request) with { Rules = updatedRules, Enforcement = enforcement };
            }
            catch (OperationCanceledException)
            {
                await RollBackAfterCancellationAsync(
                    request.RequestId,
                    () => previousRule is null
                        ? enforcer.RemoveAsync(normalizedRule.Id, CancellationToken.None)
                        : enforcer.ApplyAsync(previousRule, CancellationToken.None));
                throw;
            }
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<PolicyResponse> RemoveRuleAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.Rules is not null || request.RuleId is not { } ruleId || ruleId == Guid.Empty)
        {
            return Reject(request, "invalid_request", "RemoveRule requires one non-empty rule ID and no rule payload.");
        }

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var rules = (await ruleStore.LoadAsync(cancellationToken)).ToList();
            var previousRule = rules.FirstOrDefault(rule => rule.Id == ruleId);
            try
            {
                var enforcement = await enforcer.RemoveAsync(ruleId, cancellationToken);
                if (!enforcement.Succeeded)
                {
                    var rollback = previousRule is null
                        ? EnforcementResult.Success()
                        : await enforcer.ApplyAsync(previousRule, cancellationToken);
                    return EnforcementFailure(request, enforcement, rollback);
                }

                rules.RemoveAll(rule => rule.Id == ruleId);
                try
                {
                    await ruleStore.SaveAsync(rules, cancellationToken);
                }
                catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
                {
                    var rollback = previousRule is null
                        ? EnforcementResult.Success()
                        : await enforcer.ApplyAsync(previousRule, cancellationToken);
                    return PersistenceFailure(request, persistenceException, rollback);
                }

                return Success(request) with { Rules = rules, Enforcement = enforcement };
            }
            catch (OperationCanceledException)
            {
                await RollBackAfterCancellationAsync(
                    request.RequestId,
                    () => previousRule is null
                        ? Task.FromResult(EnforcementResult.Success())
                        : enforcer.ApplyAsync(previousRule, CancellationToken.None));
                throw;
            }
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<PolicyResponse> ClearRulesAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is not null)
        {
            return Reject(request, "invalid_request", "ClearRules cannot include a rule payload.");
        }

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var rules = (await ruleStore.LoadAsync(cancellationToken)).ToArray();
            var removedRules = new List<ApplicationRule>();
            var messages = new List<string>();

            try
            {
                foreach (var rule in rules)
                {
                    removedRules.Add(rule);
                    var removal = await enforcer.RemoveAsync(rule.Id, cancellationToken);
                    if (!removal.Succeeded)
                    {
                        var rollback = await RestoreRulesAsync(removedRules, cancellationToken);
                        return EnforcementFailure(request, removal, rollback);
                    }

                    messages.AddRange(removal.Applied);
                }

                try
                {
                    await ruleStore.SaveAsync([], cancellationToken);
                }
                catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
                {
                    var rollback = await RestoreRulesAsync(removedRules, cancellationToken);
                    return PersistenceFailure(request, persistenceException, rollback);
                }

                return Success(request) with
                {
                    Enforcement = EnforcementResult.Success(messages.Count == 0
                        ? ["No saved policies required removal."]
                        : messages),
                };
            }
            catch (OperationCanceledException)
            {
                await RollBackAfterCancellationAsync(
                    request.RequestId,
                    () => RestoreRulesAsync(removedRules, CancellationToken.None));
                throw;
            }
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<PolicyResponse> ReplaceRulesAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        if (HasAutomationPayload(request) || request.Rule is not null || request.RuleId is not null || request.Rules is null)
        {
            return Reject(request, "invalid_request", "ReplaceRules requires a rule collection and no singular rule payload.");
        }
        if (request.Rules.Count > RuleSetDocumentSerializer.MaximumRules)
        {
            return Reject(
                request,
                "invalid_rule_set",
                $"A rule set cannot contain more than {RuleSetDocumentSerializer.MaximumRules} rules.");
        }

        var normalizedRules = new List<ApplicationRule>(request.Rules.Count);
        var ids = new HashSet<Guid>();
        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in request.Rules)
        {
            if (rule is null)
            {
                return Reject(request, "invalid_rule_set", "Rule set contains a null rule.");
            }

            var normalizedRule = NormalizeRule(rule, out var validationErrors);
            if (normalizedRule is null)
            {
                return Reject(request, "invalid_rule_set", $"Rule {rule.Id} is invalid: {string.Join(" ", validationErrors)}");
            }
            if (!ids.Add(normalizedRule.Id))
            {
                return Reject(request, "duplicate_rule", $"Rule ID {normalizedRule.Id} occurs more than once.");
            }
            if (!executablePaths.Add(normalizedRule.ExecutablePath))
            {
                return Reject(
                    request,
                    "duplicate_executable",
                    $"Executable '{normalizedRule.ExecutablePath}' occurs more than once.");
            }

            normalizedRules.Add(normalizedRule);
        }
        if (TryGetRuleSetSizeError(normalizedRules, out var sizeError))
        {
            return Reject(request, "rule_set_too_large", sizeError);
        }

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var previousRules = (await ruleStore.LoadAsync(cancellationToken)).ToArray();
            var removedRules = new List<ApplicationRule>();
            var appliedRuleIds = new List<Guid>();
            var messages = new List<string>();
            var warnings = new List<string>();

            try
            {
                foreach (var previousRule in previousRules)
                {
                    removedRules.Add(previousRule);
                    var removal = await enforcer.RemoveAsync(previousRule.Id, cancellationToken);
                    messages.AddRange(removal.Applied);
                    warnings.AddRange(removal.Warnings);
                    if (!removal.Succeeded)
                    {
                        var rollback = await RestoreRulesAsync(removedRules, cancellationToken);
                        return EnforcementFailure(request, removal, rollback);
                    }
                }

                foreach (var normalizedRule in normalizedRules)
                {
                    appliedRuleIds.Add(normalizedRule.Id);
                    var enforcement = await enforcer.ApplyAsync(normalizedRule, cancellationToken);
                    messages.AddRange(enforcement.Applied);
                    warnings.AddRange(enforcement.Warnings);
                    if (!enforcement.Succeeded)
                    {
                        var rollback = await RollBackReplacementAsync(appliedRuleIds, previousRules, cancellationToken);
                        return EnforcementFailure(request, enforcement, rollback);
                    }
                }

                try
                {
                    await ruleStore.SaveAsync(normalizedRules, cancellationToken);
                }
                catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
                {
                    var rollback = await RollBackReplacementAsync(appliedRuleIds, previousRules, cancellationToken);
                    return PersistenceFailure(request, persistenceException, rollback);
                }

                messages.Add($"Replaced the saved policy set with {normalizedRules.Count} rule(s).");
                return Success(request) with
                {
                    Rules = normalizedRules,
                    Enforcement = EnforcementResult.Success(messages, warnings),
                };
            }
            catch (OperationCanceledException)
            {
                try
                {
                    var rollback = removedRules.Count == previousRules.Length
                        ? await RollBackReplacementAsync(appliedRuleIds, previousRules, CancellationToken.None)
                        : await RestoreRulesAsync(removedRules, CancellationToken.None);
                    if (!rollback.Succeeded)
                    {
                        logger.LogCritical(
                            "Rollback after cancellation failed for request {RequestId}: {Errors}",
                            request.RequestId,
                            string.Join(" ", rollback.Errors));
                    }
                }
                catch (Exception rollbackException)
                {
                    logger.LogCritical(
                        rollbackException,
                        "Rollback after cancellation threw for request {RequestId}.",
                        request.RequestId);
                }

                throw;
            }
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<EnforcementResult> RestoreRulesAsync(
        IEnumerable<ApplicationRule> rules,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var rule in rules)
        {
            var restoration = await enforcer.ApplyAsync(rule, cancellationToken);
            errors.AddRange(restoration.Errors);
        }

        return errors.Count == 0
            ? EnforcementResult.Success(["Restored policies removed before the failure."])
            : EnforcementResult.Failure(errors);
    }

    private async Task<EnforcementResult> RollBackReplacementAsync(
        IEnumerable<Guid> appliedRuleIds,
        IEnumerable<ApplicationRule> previousRules,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var ruleId in appliedRuleIds)
        {
            var removal = await enforcer.RemoveAsync(ruleId, cancellationToken);
            errors.AddRange(removal.Errors);
        }

        var restoration = await RestoreRulesAsync(previousRules, cancellationToken);
        errors.AddRange(restoration.Errors);
        return errors.Count == 0
            ? EnforcementResult.Success(["Restored the previous policy set."])
            : EnforcementResult.Failure(errors);
    }

    private static ApplicationRule? NormalizeRule(
        ApplicationRule rule,
        out IReadOnlyList<string> validationErrors)
    {
        var errors = ApplicationRuleValidator.Validate(rule);
        if (errors.Count > 0)
        {
            validationErrors = errors;
            return null;
        }

        try
        {
            var normalizedRule = rule with
            {
                DisplayName = rule.DisplayName.Trim(),
                ExecutablePath = Path.GetFullPath(rule.ExecutablePath),
            };
            validationErrors = ApplicationRuleValidator.Validate(normalizedRule);
            return validationErrors.Count == 0 ? normalizedRule : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            validationErrors = [$"Executable path is invalid: {exception.Message}"];
            return null;
        }
    }

    private static bool TryGetRuleSetSizeError(
        IReadOnlyList<ApplicationRule> rules,
        out string error)
    {
        try
        {
            RuleSetDocumentSerializer.Serialize(rules, DateTimeOffset.UnixEpoch);
            error = string.Empty;
            return false;
        }
        catch (InvalidDataException exception)
        {
            error = exception.Message;
            return true;
        }
    }

    private async Task RollBackAfterCancellationAsync(
        Guid requestId,
        Func<Task<EnforcementResult>> rollbackAction)
    {
        try
        {
            var rollback = await rollbackAction();
            if (!rollback.Succeeded)
            {
                logger.LogCritical(
                    "Rollback after cancellation failed for request {RequestId}: {Errors}",
                    requestId,
                    string.Join(" ", rollback.Errors));
            }
        }
        catch (Exception rollbackException)
        {
            logger.LogCritical(
                rollbackException,
                "Rollback after cancellation threw for request {RequestId}.",
                requestId);
        }
    }

    private static PolicyResponse PersistenceFailure(
        PolicyRequest request,
        Exception exception,
        EnforcementResult rollback)
    {
        var rollbackMessage = rollback.Succeeded
            ? "The policy change was rolled back."
            : $"Rollback also failed: {string.Join(" ", rollback.Errors)}";
        return Reject(
            request,
            "persistence_failed",
            $"The rule store could not be updated: {exception.Message} {rollbackMessage}") with
        {
            Enforcement = rollback,
        };
    }

    private static PolicyResponse EnforcementFailure(
        PolicyRequest request,
        EnforcementResult enforcement,
        EnforcementResult rollback)
    {
        var rollbackMessage = rollback.Succeeded
            ? "The partial policy change was rolled back."
            : $"Rollback also failed: {string.Join(" ", rollback.Errors)}";
        return Success(request) with
        {
            Succeeded = false,
            ErrorCode = "enforcement_failed",
            ErrorMessage = $"{string.Join(" ", enforcement.Errors)} {rollbackMessage}",
            Enforcement = enforcement,
        };
    }

    private static PolicyResponse Success(PolicyRequest request) => new()
    {
        RequestId = request.RequestId,
        Succeeded = true,
    };

    private static PolicyResponse Reject(PolicyRequest request, string code, string message) => new()
    {
        RequestId = request.RequestId,
        Succeeded = false,
        ErrorCode = code,
        ErrorMessage = message,
    };
}
