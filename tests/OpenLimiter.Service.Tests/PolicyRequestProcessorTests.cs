using Microsoft.Extensions.Logging.Abstractions;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Protocol;
using OpenLimiter.Windows.Driver;
using OpenLimiter.Windows.Enforcement;
using OpenLimiter.Windows.Wfp;

namespace OpenLimiter.Service.Tests;

public sealed class PolicyRequestProcessorTests
{
    [Fact]
    public async Task SaveProfile_stores_reusable_rules_without_temporary_or_paused_state()
    {
        var automation = new FakeAutomationStore();
        var processor = CreateProcessor(new FakeRuleStore(), new FakeEnforcer(), new FakeAuditLog(), automationStore: automation);
        var profile = new PolicyProfile
        {
            Id = Guid.NewGuid(),
            Name = "Gaming",
            Rules = [ValidRule() with { Enabled = false, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) }],
        };

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.SaveProfile,
            Profile = profile,
        }, "desktop\\tester");

        Assert.True(response.Succeeded);
        var storedRule = Assert.Single(Assert.Single(automation.Configuration.Profiles).Rules);
        Assert.True(storedRule.Enabled);
        Assert.Null(storedRule.ExpiresAtUtc);
        Assert.Equal("Gaming", Assert.Single(response.Profiles).Name);
    }

    [Fact]
    public async Task ActivateProfile_transactionally_replaces_the_current_rule_set()
    {
        var currentRule = ValidRule();
        var profileRule = ValidRule() with { Id = Guid.NewGuid() };
        var profile = new PolicyProfile
        {
            Id = Guid.NewGuid(),
            Name = "Focus",
            Rules = [profileRule],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var automation = new FakeAutomationStore(new() { Profiles = [profile] });
        var store = new FakeRuleStore(currentRule);
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog(), automationStore: automation);

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ActivateProfile,
            ProfileId = profile.Id,
        }, "desktop\\tester");

        Assert.True(response.Succeeded);
        Assert.Equal(currentRule.Id, Assert.Single(enforcer.RemovedRuleIds));
        Assert.Equal(profileRule.Id, Assert.Single(enforcer.AppliedRuleIds));
        Assert.Equal(profileRule.Id, Assert.Single(store.Rules).Id);
    }

    [Fact]
    public async Task Apply_rejects_an_expired_temporary_rule()
    {
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(new FakeRuleStore(), enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(
            ApplyRequest(ValidRule() with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }),
            "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("invalid_rule", response.ErrorCode);
        Assert.Empty(enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Apply_enforces_and_persists_rule()
    {
        var store = new FakeRuleStore();
        var enforcer = new FakeEnforcer();
        var audit = new FakeAuditLog();
        var processor = CreateProcessor(store, enforcer, audit);
        var rule = ValidRule();

        var response = await processor.HandleAsync(ApplyRequest(rule), "desktop\\tester");

        Assert.True(response.Succeeded);
        Assert.Equal(rule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal(rule.Id, Assert.Single(enforcer.AppliedRuleIds));
        Assert.True(Assert.Single(audit.Entries).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_updates_enabled_state_without_losing_rule_configuration(bool enabled)
    {
        var previousRule = ValidRule() with
        {
            Enabled = !enabled,
            UploadLimitBitsPerSecond = 2_500_000,
        };
        var updatedRule = previousRule with { Enabled = enabled };
        var store = new FakeRuleStore(previousRule);
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(ApplyRequest(updatedRule), "desktop\\tester");

        Assert.True(response.Succeeded);
        var persistedRule = Assert.Single(store.Rules);
        Assert.Equal(enabled, persistedRule.Enabled);
        Assert.Equal(previousRule.BlockedDirections, persistedRule.BlockedDirections);
        Assert.Equal(previousRule.UploadLimitBitsPerSecond, persistedRule.UploadLimitBitsPerSecond);
        Assert.Equal(enabled, Assert.Single(enforcer.AppliedRules).Enabled);
    }

    [Fact]
    public async Task Apply_rejects_new_rule_when_saved_rule_limit_is_reached()
    {
        var storedRules = Enumerable.Range(0, RuleSetDocumentSerializer.MaximumRules)
            .Select(index => ValidRule() with
            {
                Id = Guid.NewGuid(),
                ExecutablePath = $@"C:\Stored\app-{index}.exe",
            })
            .ToArray();
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(new FakeRuleStore(storedRules), enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(ApplyRequest(ValidRule()), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("rule_limit", response.ErrorCode);
        Assert.Empty(enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Apply_rejects_rule_set_that_would_exceed_safe_document_size()
    {
        var storedRules = Enumerable.Range(0, RuleSetDocumentSerializer.MaximumRules - 1)
            .Select(index => ValidRule() with
            {
                Id = Guid.NewGuid(),
                ExecutablePath = $@"C:\Stored\{new string('x', 700)}-{index}.exe",
            })
            .ToArray();
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(new FakeRuleStore(storedRules), enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(ApplyRequest(ValidRule()), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("rule_set_too_large", response.ErrorCode);
        Assert.Empty(enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Ping_reports_driver_capabilities()
    {
        var processor = CreateProcessor(new FakeRuleStore(), new FakeEnforcer(), new FakeAuditLog());

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.Ping,
        }, "desktop\\tester");

        Assert.True(response.Succeeded);
        var driver = Assert.IsType<WfpDriverStatus>(response.Driver);
        Assert.True(driver.IsAvailable);
        Assert.Equal(2U, driver.RegisteredCallouts);
        Assert.True(driver.PolicySessionActive);
    }

    [Fact]
    public async Task Completed_mutation_is_audited_after_client_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var audit = new FakeAuditLog();
        var rule = ValidRule();
        var processor = CreateProcessor(
            new CancellingRuleStore(cancellation),
            new FakeEnforcer(),
            audit);

        var response = await processor.HandleAsync(
            ApplyRequest(rule),
            "desktop\\tester",
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(response.Succeeded);
        Assert.True(Assert.Single(audit.Entries).Succeeded);
    }

    [Fact]
    public async Task Read_only_requests_do_not_grow_policy_audit()
    {
        var audit = new FakeAuditLog();
        var processor = CreateProcessor(new FakeRuleStore(), new FakeEnforcer(), audit);

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.Ping,
        }, "desktop\\tester");

        Assert.True(response.Succeeded);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task List_driver_flows_reports_bounded_snapshot()
    {
        var processor = CreateProcessor(new FakeRuleStore(), new FakeEnforcer(), new FakeAuditLog());

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListDriverFlows,
        }, "desktop\\tester");

        var snapshot = Assert.IsType<WfpFlowSnapshot>(response.DriverFlows);
        Assert.True(snapshot.PolicySessionActive);
        var flowEvent = Assert.Single(snapshot.Events);
        Assert.Equal(42UL, flowEvent.ProcessId);
        Assert.Equal(6U, flowEvent.IpVersion);
    }

    [Fact]
    public async Task List_network_traffic_returns_monitor_snapshot_without_audit_entry()
    {
        var audit = new FakeAuditLog();
        var traffic = new FakeNetworkTrafficMonitor();
        var processor = CreateProcessor(new FakeRuleStore(), new FakeEnforcer(), audit, trafficMonitor: traffic);

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListNetworkTraffic,
        }, "desktop\\tester");

        Assert.True(response.Succeeded);
        var snapshot = Assert.IsType<NetworkTrafficSnapshot>(response.NetworkTraffic);
        Assert.True(snapshot.IsAvailable);
        Assert.Equal(42, Assert.Single(snapshot.Samples).ProcessId);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task List_driver_flows_associates_application_identity_with_saved_rule()
    {
        var rule = ValidRule();
        Assert.True(WfpApplicationIdentity.TryGetHash(rule.ExecutablePath, out var applicationIdHash));
        var processor = CreateProcessor(
            new FakeRuleStore(rule),
            new FakeEnforcer(),
            new FakeAuditLog(),
            driverClient: new FakeDriverClient(applicationIdHash));

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListDriverFlows,
        }, "desktop\\tester");

        var flowEvent = Assert.Single(Assert.IsType<WfpFlowSnapshot>(response.DriverFlows).Events);
        Assert.Equal(rule.Id, flowEvent.RuleId);
        Assert.Equal(rule.DisplayName, flowEvent.RuleDisplayName);
    }

    [Fact]
    public async Task List_driver_flows_rejects_rule_payload()
    {
        var processor = CreateProcessor(new FakeRuleStore(), new FakeEnforcer(), new FakeAuditLog());

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListDriverFlows,
            Rule = ValidRule(),
        }, "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("invalid_request", response.ErrorCode);
        Assert.Null(response.DriverFlows);
    }

    [Fact]
    public async Task List_driver_flows_reports_inactive_policy_session_separately_from_driver()
    {
        var processor = CreateProcessor(
            new FakeRuleStore(),
            new FakeEnforcer(),
            new FakeAuditLog(),
            new FakePolicySession(isActive: false, "WFP transaction failed for test."));

        var response = await processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ListDriverFlows,
        }, "desktop\\tester");

        var snapshot = Assert.IsType<WfpFlowSnapshot>(response.DriverFlows);
        Assert.True(snapshot.IsAvailable);
        Assert.False(snapshot.PolicySessionActive);
        Assert.Equal("WFP transaction failed for test.", snapshot.Message);
    }

    [Fact]
    public async Task Apply_rolls_back_enforcement_when_persistence_fails()
    {
        var store = new FakeRuleStore { FailSave = true };
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());
        var rule = ValidRule();

        var response = await processor.HandleAsync(ApplyRequest(rule), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("persistence_failed", response.ErrorCode);
        Assert.Equal(rule.Id, Assert.Single(enforcer.RemovedRuleIds));
    }

    [Fact]
    public async Task Apply_rejects_unknown_direction_before_enforcement()
    {
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(new FakeRuleStore(), enforcer, new FakeAuditLog());
        var rule = ValidRule() with { BlockedDirections = (TrafficDirection)16 };

        var response = await processor.HandleAsync(ApplyRequest(rule), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("invalid_rule", response.ErrorCode);
        Assert.Empty(enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Apply_rejects_second_rule_for_same_executable()
    {
        var store = new FakeRuleStore();
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());
        var firstRule = ValidRule();
        var duplicateRule = ValidRule() with
        {
            Id = Guid.NewGuid(),
            ExecutablePath = firstRule.ExecutablePath.ToUpperInvariant(),
        };

        var firstResponse = await processor.HandleAsync(ApplyRequest(firstRule), "desktop\\tester");
        var duplicateResponse = await processor.HandleAsync(ApplyRequest(duplicateRule), "desktop\\tester");

        Assert.True(firstResponse.Succeeded);
        Assert.False(duplicateResponse.Succeeded);
        Assert.Equal("duplicate_executable", duplicateResponse.ErrorCode);
        Assert.Equal(firstRule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal([firstRule.Id], enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Apply_cleans_partial_enforcement_when_backend_fails()
    {
        var enforcer = new FakeEnforcer { FailApply = true };
        var processor = CreateProcessor(new FakeRuleStore(), enforcer, new FakeAuditLog());
        var rule = ValidRule();

        var response = await processor.HandleAsync(ApplyRequest(rule), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("enforcement_failed", response.ErrorCode);
        Assert.Equal(rule.Id, Assert.Single(enforcer.RemovedRuleIds));
    }

    [Fact]
    public async Task Apply_cleans_partial_enforcement_when_request_is_cancelled()
    {
        var rule = ValidRule();
        var store = new FakeRuleStore();
        var enforcer = new FakeEnforcer { CancelApplyRuleId = rule.Id };
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            processor.HandleAsync(ApplyRequest(rule), "desktop\\tester"));

        Assert.Empty(store.Rules);
        Assert.Equal([rule.Id], enforcer.AppliedRuleIds);
        Assert.Equal([rule.Id], enforcer.RemovedRuleIds);
    }

    [Fact]
    public async Task Remove_restores_previous_rule_when_request_is_cancelled()
    {
        var rule = ValidRule();
        var store = new FakeRuleStore(rule);
        var enforcer = new FakeEnforcer { CancelRemoveRuleId = rule.Id };
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.RemoveRule,
            RuleId = rule.Id,
        }, "desktop\\tester"));

        Assert.Equal(rule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal([rule.Id], enforcer.RemovedRuleIds);
        Assert.Equal([rule.Id], enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Clear_restores_removed_rules_when_request_is_cancelled()
    {
        var first = ValidRule();
        var second = ValidRule() with { Id = Guid.NewGuid() };
        var store = new FakeRuleStore(first, second);
        var enforcer = new FakeEnforcer { CancelRemoveRuleId = second.Id };
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.HandleAsync(new()
        {
            RequestId = Guid.NewGuid(),
            Kind = PolicyRequestKind.ClearRules,
        }, "desktop\\tester"));

        Assert.Equal(2, store.Rules.Count);
        Assert.Equal([first.Id, second.Id], enforcer.RemovedRuleIds);
        Assert.Equal([first.Id, second.Id], enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Replace_rules_rejects_duplicate_executable_paths_before_enforcement()
    {
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(new FakeRuleStore(), enforcer, new FakeAuditLog());
        var first = ValidRule();
        var duplicate = ValidRule() with
        {
            ExecutablePath = first.ExecutablePath.ToUpperInvariant(),
        };

        var response = await processor.HandleAsync(ReplaceRequest([first, duplicate]), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("duplicate_executable", response.ErrorCode);
        Assert.Empty(enforcer.AppliedRuleIds);
        Assert.Empty(enforcer.RemovedRuleIds);
    }

    [Fact]
    public async Task Replace_rules_removes_previous_set_and_persists_imported_set()
    {
        var previousRule = ValidRule();
        var importedRule = ValidRule() with { Id = Guid.NewGuid() };
        var store = new FakeRuleStore(previousRule);
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(ReplaceRequest([importedRule]), "desktop\\tester");

        Assert.True(response.Succeeded);
        Assert.Equal(importedRule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal([previousRule.Id], enforcer.RemovedRuleIds);
        Assert.Equal([importedRule.Id], enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Replace_rules_restores_previous_set_when_persistence_fails()
    {
        var previousRule = ValidRule();
        var importedRule = ValidRule() with { Id = Guid.NewGuid() };
        var store = new FakeRuleStore(previousRule) { FailSave = true };
        var enforcer = new FakeEnforcer();
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(ReplaceRequest([importedRule]), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("persistence_failed", response.ErrorCode);
        Assert.Equal(previousRule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal([previousRule.Id, importedRule.Id], enforcer.RemovedRuleIds);
        Assert.Equal([importedRule.Id, previousRule.Id], enforcer.AppliedRuleIds);
    }

    [Fact]
    public async Task Replace_rules_restores_previous_set_when_imported_enforcement_fails()
    {
        var previousRule = ValidRule();
        var importedRule = ValidRule() with { Id = Guid.NewGuid() };
        var store = new FakeRuleStore(previousRule);
        var enforcer = new FakeEnforcer { FailApplyRuleId = importedRule.Id };
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        var response = await processor.HandleAsync(ReplaceRequest([importedRule]), "desktop\\tester");

        Assert.False(response.Succeeded);
        Assert.Equal("enforcement_failed", response.ErrorCode);
        Assert.Equal(previousRule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal([previousRule.Id, importedRule.Id], enforcer.RemovedRuleIds);
        Assert.Equal([importedRule.Id, previousRule.Id], enforcer.AppliedRuleIds);
        Assert.Contains("rolled back", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replace_rules_restores_previous_set_when_request_is_cancelled_mid_enforcement()
    {
        var previousRule = ValidRule();
        var importedRule = ValidRule() with { Id = Guid.NewGuid() };
        var store = new FakeRuleStore(previousRule);
        var enforcer = new FakeEnforcer { CancelApplyRuleId = importedRule.Id };
        var processor = CreateProcessor(store, enforcer, new FakeAuditLog());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            processor.HandleAsync(ReplaceRequest([importedRule]), "desktop\\tester"));

        Assert.Equal(previousRule.Id, Assert.Single(store.Rules).Id);
        Assert.Equal([previousRule.Id, importedRule.Id], enforcer.RemovedRuleIds);
        Assert.Equal([importedRule.Id, previousRule.Id], enforcer.AppliedRuleIds);
    }

    private static PolicyRequestProcessor CreateProcessor(
        IRuleStore store,
        IPolicyEnforcer enforcer,
        IAuditLog auditLog,
        IWfpPolicySession? policySession = null,
        IWfpDriverClient? driverClient = null,
        INetworkTrafficMonitor? trafficMonitor = null,
        IAutomationStore? automationStore = null,
        TimeProvider? timeProvider = null) =>
        new(
            store,
            automationStore ?? new FakeAutomationStore(),
            enforcer,
            driverClient ?? new FakeDriverClient(),
            policySession ?? new FakePolicySession(),
            trafficMonitor ?? new FakeNetworkTrafficMonitor(),
            timeProvider ?? TimeProvider.System,
            auditLog,
            NullLogger<PolicyRequestProcessor>.Instance);

    private static PolicyRequest ApplyRequest(ApplicationRule rule) => new()
    {
        RequestId = Guid.NewGuid(),
        Kind = PolicyRequestKind.ApplyRule,
        Rule = rule,
    };

    private static PolicyRequest ReplaceRequest(IReadOnlyList<ApplicationRule> rules) => new()
    {
        RequestId = Guid.NewGuid(),
        Kind = PolicyRequestKind.ReplaceRules,
        Rules = rules,
    };

    private static ApplicationRule ValidRule() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Test host",
        ExecutablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable."),
        BlockedDirections = TrafficDirection.Inbound,
    };

    private sealed class FakeRuleStore(params ApplicationRule[] rules) : IRuleStore
    {
        public IReadOnlyList<ApplicationRule> Rules { get; private set; } = rules;

        public bool FailSave { get; init; }

        public Task<IReadOnlyList<ApplicationRule>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Rules);

        public Task SaveAsync(IEnumerable<ApplicationRule> rules, CancellationToken cancellationToken = default)
        {
            if (FailSave)
            {
                throw new IOException("Simulated disk failure.");
            }

            Rules = rules.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAutomationStore(AutomationConfiguration? initial = null) : IAutomationStore
    {
        public AutomationConfiguration Configuration { get; private set; } = initial ?? new();

        public Task<AutomationConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Configuration);

        public Task SaveAsync(AutomationConfiguration value, CancellationToken cancellationToken = default)
        {
            Configuration = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnforcer : IPolicyEnforcer
    {
        public List<ApplicationRule> AppliedRules { get; } = [];

        public List<Guid> AppliedRuleIds { get; } = [];

        public List<Guid> RemovedRuleIds { get; } = [];

        public bool FailApply { get; init; }

        public Guid? FailApplyRuleId { get; init; }

        public Guid? CancelApplyRuleId { get; init; }

        public Guid? CancelRemoveRuleId { get; init; }

        public Task<EnforcementResult> ApplyAsync(ApplicationRule rule, CancellationToken cancellationToken = default)
        {
            AppliedRules.Add(rule);
            AppliedRuleIds.Add(rule.Id);
            if (CancelApplyRuleId == rule.Id)
            {
                throw new OperationCanceledException("Simulated cancellation.");
            }

            return Task.FromResult(FailApply || FailApplyRuleId == rule.Id
                ? EnforcementResult.Failure(["Simulated enforcement failure."])
                : EnforcementResult.Success(["Applied test policy."]));
        }

        public Task<EnforcementResult> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
        {
            RemovedRuleIds.Add(ruleId);
            if (CancelRemoveRuleId == ruleId)
            {
                throw new OperationCanceledException("Simulated cancellation.");
            }

            return Task.FromResult(EnforcementResult.Success(["Removed test policy."]));
        }
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNetworkTrafficMonitor : INetworkTrafficMonitor
    {
        public NetworkTrafficSnapshot GetSnapshot() => new()
        {
            IsAvailable = true,
            SampledAtUtc = DateTimeOffset.UtcNow,
            SampleIntervalSeconds = 1,
            Samples = [new(42, 100, 200, 1_000, 2_000)],
        };
    }

    private sealed class FakeDriverClient(ulong applicationIdHash = 0x1234) : IWfpDriverClient
    {
        public WfpDriverStatus GetStatus() => new()
        {
            IsAvailable = true,
            ApiMajor = 1,
            RegisteredCallouts = 2,
        };

        public WfpFlowSnapshot GetFlowEvents() => new()
        {
            IsAvailable = true,
            LatestSequence = 1,
            Events = [new(1, 42, applicationIdHash, 6, 17, WfpFlowDirection.Outbound)],
        };
    }

    private sealed class CancellingRuleStore(CancellationTokenSource cancellation) : IRuleStore
    {
        public Task<IReadOnlyList<ApplicationRule>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRule>>([]);

        public Task SaveAsync(
            IEnumerable<ApplicationRule> rules,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class FakePolicySession(
        bool isActive = true,
        string message = "Test WFP policy session is active.") : IWfpPolicySession
    {
        public bool IsActive => isActive;

        public string? Message => message;

        public bool TryActivate() => isActive;

        public void Dispose()
        {
        }
    }
}
