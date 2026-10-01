using System.Runtime.InteropServices;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Naming;

namespace OpenLimiter.Windows.Firewall;

public sealed class WindowsFirewallPolicy : IFirewallPolicy
{
    private const int AllProfiles = int.MaxValue;
    private const int ActionBlock = 0;
    private const int DirectionInbound = 1;
    private const int DirectionOutbound = 2;

    public void Block(Guid ruleId, string executablePath, TrafficDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        Remove(ruleId);

        if (direction.HasFlag(TrafficDirection.Inbound))
        {
            AddRule(OwnedPolicyName.Firewall(ruleId, "in"), executablePath, DirectionInbound);
        }

        if (direction.HasFlag(TrafficDirection.Outbound))
        {
            AddRule(OwnedPolicyName.Firewall(ruleId, "out"), executablePath, DirectionOutbound);
        }
    }

    public void Remove(Guid ruleId)
    {
        RemoveIfPresent(OwnedPolicyName.Firewall(ruleId, "in"));
        RemoveIfPresent(OwnedPolicyName.Firewall(ruleId, "out"));
    }

    private static void AddRule(string name, string executablePath, int direction)
    {
        dynamic policy = CreateComObject("HNetCfg.FwPolicy2");
        dynamic rule = CreateComObject("HNetCfg.FWRule");

        try
        {
            rule.Name = name;
            rule.Description = "Managed by OpenLimiter. Remove through OpenLimiter to keep saved rules in sync.";
            rule.ApplicationName = Path.GetFullPath(executablePath);
            rule.Direction = direction;
            rule.Action = ActionBlock;
            rule.Enabled = true;
            rule.Profiles = AllProfiles;
            rule.InterfaceTypes = "All";
            policy.Rules.Add(rule);
        }
        finally
        {
            Marshal.FinalReleaseComObject(rule);
            Marshal.FinalReleaseComObject(policy);
        }
    }

    private static void RemoveIfPresent(string name)
    {
        dynamic policy = CreateComObject("HNetCfg.FwPolicy2");

        try
        {
            try
            {
                policy.Rules.Remove(name);
            }
            catch (COMException exception) when ((uint)exception.HResult is 0x80070002 or 0x80070490)
            {
                // Removing an absent owned rule is idempotent.
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(policy);
        }
    }

    private static object CreateComObject(string programmaticId)
    {
        var type = Type.GetTypeFromProgID(programmaticId)
            ?? throw new PlatformNotSupportedException($"Windows component {programmaticId} is unavailable.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Windows component {programmaticId} could not be created.");
    }
}

