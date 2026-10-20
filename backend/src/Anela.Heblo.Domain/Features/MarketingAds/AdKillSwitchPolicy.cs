namespace Anela.Heblo.Domain.Features.MarketingAds;

public static class AdKillSwitchPolicy
{
    /// <summary>
    /// Execution is allowed only when the global row exists and is enabled, and the platform has no
    /// disabling override. A missing global row fails closed.
    /// </summary>
    public static bool IsExecutionEnabled(IEnumerable<AdKillSwitch> switches, string platform)
    {
        var list = switches.ToList();
        var global = list.FirstOrDefault(s => s.Scope == AdKillSwitch.GlobalScope);
        if (global is null || !global.ExecutionEnabled)
        {
            return false;
        }
        var platformOverride = list.FirstOrDefault(s => s.Scope == platform);
        return platformOverride is null || platformOverride.ExecutionEnabled;
    }
}
