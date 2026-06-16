using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationRatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppWebhookMetrics _metrics;
    private readonly ILogger<ConversationRatedReaction> _logger;

    public ConversationRatedReaction(
        ISmartsuppRepository repository,
        ISmartsuppWebhookMetrics metrics,
        ILogger<ConversationRatedReaction> logger)
    {
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public string EventName => "conversation.rated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp, _logger, _metrics);

        if (ctx.Data.TryGetProperty("rating_value", out var rv) && rv.ValueKind == JsonValueKind.Number)
            conversation.Rating = rv.GetInt32();

        conversation.RatingText = SmartsuppPayloadMapper.TryGetString(ctx.Data, "rating_text");
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
