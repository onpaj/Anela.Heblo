using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

/// <summary>
/// A native Smartsupp operator left a conversation — clear their presence row.
/// Payload carries only <c>data.conversation_id</c> and <c>data.agent_id</c>.
/// </summary>
public sealed class ConversationAgentLeftReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppPresenceRepository _presenceRepository;

    public ConversationAgentLeftReaction(ISmartsuppPresenceRepository presenceRepository)
        => _presenceRepository = presenceRepository;

    public string EventName => "conversation.agent_left";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var conversationId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "conversation_id");
        var agentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(agentId))
            return;

        await _presenceRepository.RemoveAsync(
            conversationId, agentId, SmartsuppPresenceSource.Smartsupp, cancellationToken);
    }
}
