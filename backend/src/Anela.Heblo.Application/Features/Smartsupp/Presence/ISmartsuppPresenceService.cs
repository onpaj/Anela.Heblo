using Anela.Heblo.Application.Features.Smartsupp.Contracts;

namespace Anela.Heblo.Application.Features.Smartsupp.Presence;

public interface ISmartsuppPresenceService
{
    /// <summary>Record/refresh the current Heblo operator as active in the conversation (heartbeat).</summary>
    Task RecordCurrentOperatorAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>Remove the current Heblo operator's presence from the conversation (leave).</summary>
    Task RemoveCurrentOperatorAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Active viewers for the given conversations, keyed by conversation id, deduped per operator,
    /// with <see cref="ConversationPresenceDto.IsCurrentUser"/> resolved against the caller.
    /// </summary>
    Task<Dictionary<string, List<ConversationPresenceDto>>> GetActiveViewersAsync(
        IReadOnlyCollection<string> conversationIds,
        CancellationToken cancellationToken);
}
