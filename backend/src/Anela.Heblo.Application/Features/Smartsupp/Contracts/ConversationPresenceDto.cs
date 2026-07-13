namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

/// <summary>
/// An operator currently active in a conversation. Combines native-Smartsupp presence
/// (agent_joined webhook) and Heblo presence (heartbeat from the Heblo chat UI).
/// </summary>
public class ConversationPresenceDto
{
    public string AgentId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    /// <summary>"Smartsupp" or "Heblo".</summary>
    public string Source { get; set; } = null!;

    /// <summary>True when this presence is the requesting operator themselves (so the UI can hide it).</summary>
    public bool IsCurrentUser { get; set; }

    public DateTime EnteredAt { get; set; }
}
