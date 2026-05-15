using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationBotRepliedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationBotRepliedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.bot_replied";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
            await _repository.UpsertConversationAsync(
                SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp), cancellationToken);

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await _repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
