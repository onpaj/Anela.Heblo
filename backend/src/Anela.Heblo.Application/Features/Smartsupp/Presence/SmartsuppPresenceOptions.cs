namespace Anela.Heblo.Application.Features.Smartsupp.Presence;

public sealed class SmartsuppPresenceOptions
{
    public const string SectionName = "Smartsupp:Presence";

    /// <summary>
    /// How long a Heblo operator's heartbeat stays "active" without a refresh. The UI heartbeats
    /// well inside this window; the TTL only matters if a browser stops beating (tab closed/crashed)
    /// without sending the best-effort leave.
    /// </summary>
    public int HeartbeatTtlSeconds { get; set; } = 90;

    /// <summary>
    /// Safety-net TTL for a native Smartsupp agent_joined row in case the matching agent_left
    /// webhook is never delivered. Normally the row is removed by agent_left long before this.
    /// </summary>
    public int SmartsuppTtlMinutes { get; set; } = 30;
}
