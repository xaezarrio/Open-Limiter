namespace OpenLimiter.Protocol;

public enum PolicyRequestKind
{
    Ping,
    ListNetworkTraffic,
    ListDriverFlows,
    ListRules,
    ApplyRule,
    RemoveRule,
    ClearRules,
    ReplaceRules,
    ListAutomation,
    SaveProfile,
    DeleteProfile,
    ActivateProfile,
    SaveSchedule,
    DeleteSchedule,
    RunSchedule,
}
