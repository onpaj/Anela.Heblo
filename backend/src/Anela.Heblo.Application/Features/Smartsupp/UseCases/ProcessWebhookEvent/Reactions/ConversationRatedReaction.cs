using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationRatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationRatedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.rated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);

        if (ctx.Data.TryGetProperty("rating_value", out var rv) && rv.ValueKind == JsonValueKind.Number)
            conversation.Rating = rv.GetInt32();

        conversation.RatingText = SmartsuppPayloadMapper.TryGetString(ctx.Data, "rating_text");
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
