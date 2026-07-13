using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

/// <summary>
/// A native Smartsupp operator entered a conversation. Records them as present so Heblo operators
/// can tell someone is already working on it. The matching <c>conversation.agent_left</c> clears it.
/// Payload carries only <c>data.conversation_id</c> and <c>data.agent_id</c>.
/// </summary>
public sealed class ConversationAgentJoinedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppPresenceRepository _presenceRepository;
    private readonly ISmartsuppAgentCache _agentCache;

    public ConversationAgentJoinedReaction(
        ISmartsuppPresenceRepository presenceRepository,
        ISmartsuppAgentCache agentCache)
    {
        _presenceRepository = presenceRepository;
        _agentCache = agentCache;
    }

    public string EventName => "conversation.agent_joined";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var conversationId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "conversation_id");
        var agentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(agentId))
            return;

        var agentNames = await _agentCache.GetAgentNamesAsync(cancellationToken);
        var displayName = agentNames.TryGetValue(agentId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : agentId;

        var now = DateTime.SpecifyKind(ctx.Timestamp, DateTimeKind.Unspecified);
        await _presenceRepository.UpsertAsync(
            new SmartsuppConversationPresence
            {
                ConversationId = conversationId,
                AgentId = agentId,
                DisplayName = displayName,
                Source = SmartsuppPresenceSource.Smartsupp,
                EnteredAt = now,
                LastSeenAt = now,
            },
            cancellationToken);
    }
}
