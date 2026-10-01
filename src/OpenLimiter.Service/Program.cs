using Microsoft.Extensions.Hosting.WindowsServices;
using OpenLimiter.Core.Persistence;
using OpenLimiter.Windows.Driver;
using OpenLimiter.Windows.Enforcement;
using OpenLimiter.Windows.Firewall;
using OpenLimiter.Windows.Qos;
using OpenLimiter.Windows.Wfp;

namespace OpenLimiter.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--cleanup", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = await CleanupPoliciesAsync();
            return;
        }

        var settings = ServiceSettings.Load(args);
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options => options.ServiceName = "OpenLimiter Policy Service");
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRuleStore>(_ => new JsonRuleStore(settings.RuleFilePath));
        builder.Services.AddSingleton<IAutomationStore>(_ => new JsonAutomationStore(settings.AutomationFilePath));
        builder.Services.AddSingleton<IFirewallPolicy, WindowsFirewallPolicy>();
        builder.Services.AddSingleton<IQosPolicy, PowerShellQosPolicy>();
        builder.Services.AddSingleton<IWfpDriverClient, WfpDriverClient>();
        builder.Services.AddSingleton<IWfpPolicySession, WfpPolicySession>();
        builder.Services.AddSingleton<EtwNetworkTrafficMonitor>();
        builder.Services.AddSingleton<INetworkTrafficMonitor>(provider => provider.GetRequiredService<EtwNetworkTrafficMonitor>());
        builder.Services.AddHostedService(provider => provider.GetRequiredService<EtwNetworkTrafficMonitor>());
        builder.Services.AddSingleton<IPolicyEnforcer, PolicyEnforcer>();
        builder.Services.AddSingleton<IAuditLog, JsonAuditLog>();
        builder.Services.AddSingleton<IPolicyRequestHandler, PolicyRequestProcessor>();
        builder.Services.AddSingleton<PolicyPipeServer>();
        builder.Services.AddHostedService<RuleExpirationWorker>();
        builder.Services.AddHostedService<PolicyScheduleWorker>();
        builder.Services.AddHostedService<PolicyServiceWorker>();

        await builder.Build().RunAsync();
    }

    private static async Task<int> CleanupPoliciesAsync()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OpenLimiter");
        var store = new JsonRuleStore(Path.Combine(dataDirectory, "rules.json"));
        var enforcer = new PolicyEnforcer(new WindowsFirewallPolicy(), new PowerShellQosPolicy());
        var errors = await PolicyCleanup.RemoveAllAsync(store, enforcer);

        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }
        return errors.Count == 0 ? 0 : 1;
    }
}
