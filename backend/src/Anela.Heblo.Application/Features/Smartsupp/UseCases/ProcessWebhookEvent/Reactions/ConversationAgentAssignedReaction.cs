using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationAgentAssignedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
