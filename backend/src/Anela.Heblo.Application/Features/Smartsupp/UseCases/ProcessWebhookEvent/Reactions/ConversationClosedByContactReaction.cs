using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedByContactReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationClosedByContactReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.closed_by_contact";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = "contact";
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
