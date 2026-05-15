using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationClosedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.closed";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = SmartsuppPayloadMapper.TryGetString(ctx.Data, "close_type");
        conversation.ClosedByAgentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        conversation.LastClosedAt = ctx.Timestamp;
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
