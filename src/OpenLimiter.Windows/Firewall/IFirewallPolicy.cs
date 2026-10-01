using OpenLimiter.Core.Models;

namespace OpenLimiter.Windows.Firewall;

public interface IFirewallPolicy
{
    void Block(Guid ruleId, string executablePath, TrafficDirection direction);

    void Remove(Guid ruleId);
}

