namespace OpenLimiter.Core.Models;

[Flags]
public enum TrafficDirection
{
    None = 0,
    Inbound = 1,
    Outbound = 2,
    Both = Inbound | Outbound,
}

