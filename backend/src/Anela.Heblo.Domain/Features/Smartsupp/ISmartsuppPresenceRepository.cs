namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppPresenceRepository
{
    /// <summary>
    /// Insert or refresh a presence row (keyed by ConversationId + AgentId + Source),
    /// bumping LastSeenAt. Executes immediately (no SaveChanges required).
    /// </summary>
    Task UpsertAsync(
        SmartsuppConversationPresence presence,
        CancellationToken cancellationToken);

    /// <summary>Remove a single presence row (e.g. on agent_left or heartbeat leave).</summary>
    Task RemoveAsync(
        string conversationId,
        string agentId,
        SmartsuppPresenceSource source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Return the active presence rows for the given conversations, keyed by conversation id.
    /// A row is active when it was seen after the source-specific threshold
    /// (Heblo heartbeat has a short TTL; Smartsupp join has a long safety-net TTL).
    /// </summary>
    Task<Dictionary<string, List<SmartsuppConversationPresence>>> GetActiveByConversationAsync(
        IReadOnlyCollection<string> conversationIds,
        DateTime hebloThreshold,
        DateTime smartsuppThreshold,
        CancellationToken cancellationToken);

    /// <summary>Delete presence rows that have fallen past their source-specific TTL.</summary>
    Task<int> PurgeExpiredAsync(
        DateTime hebloThreshold,
        DateTime smartsuppThreshold,
        CancellationToken cancellationToken);
}
