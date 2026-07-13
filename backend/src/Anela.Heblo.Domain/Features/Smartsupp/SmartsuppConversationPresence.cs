namespace Anela.Heblo.Domain.Features.Smartsupp;

/// <summary>
/// Where a presence signal originated. Smartsupp presence is fed by the
/// conversation.agent_joined / agent_left webhooks (native Smartsupp app);
/// Heblo presence is fed by the heartbeat the Heblo chat UI sends while an
/// operator has the conversation detail open.
/// </summary>
public enum SmartsuppPresenceSource
{
    Smartsupp,
    Heblo,
}

/// <summary>
/// Records that an operator is currently active in a conversation, so other
/// operators can tell whether a colleague is already working on it.
/// Smartsupp's REST API cannot be told an agent is present, so this is tracked
/// entirely inside Heblo and surfaced through the polled conversation responses.
/// </summary>
public class SmartsuppConversationPresence
{
    public long Id { get; set; }

    public string ConversationId { get; set; } = null!;

    /// <summary>
    /// Identity key for the operator. For <see cref="SmartsuppPresenceSource.Smartsupp"/> this is the
    /// Smartsupp agent id; for <see cref="SmartsuppPresenceSource.Heblo"/> it is the operator's mapped
    /// Smartsupp agent id when known, otherwise their Heblo email. Used for self-exclusion and dedup.
    /// </summary>
    public string AgentId { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public SmartsuppPresenceSource Source { get; set; }

    public DateTime EnteredAt { get; set; }

    public DateTime LastSeenAt { get; set; }
}
