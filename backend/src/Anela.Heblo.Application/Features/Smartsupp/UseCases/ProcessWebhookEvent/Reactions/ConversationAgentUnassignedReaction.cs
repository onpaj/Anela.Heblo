using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentUnassignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationAgentUnassignedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.agent_unassigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        conversation.AssignedAgentIdsJson = null;
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
