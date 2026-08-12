using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationAgentAssignedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
