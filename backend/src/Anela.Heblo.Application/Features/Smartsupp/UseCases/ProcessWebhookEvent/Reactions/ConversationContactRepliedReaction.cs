using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationContactRepliedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppWebhookMetrics _metrics;
    private readonly ILogger<ConversationContactRepliedReaction> _logger;

    public ConversationContactRepliedReaction(
        ISmartsuppRepository repository,
        ISmartsuppWebhookMetrics metrics,
        ILogger<ConversationContactRepliedReaction> logger)
    {
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public string EventName => "conversation.contact_replied";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
            await _repository.UpsertConversationAsync(
                SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp, _logger, _metrics), cancellationToken);

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await _repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
