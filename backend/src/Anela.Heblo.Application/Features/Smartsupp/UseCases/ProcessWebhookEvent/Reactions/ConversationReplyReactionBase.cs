using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;

    protected ConversationReplyReactionBase(ISmartsuppRepository repository) => Repository = repository;

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
            await Repository.UpsertConversationAsync(
                SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp), cancellationToken);

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
