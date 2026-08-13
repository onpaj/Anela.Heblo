using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    protected ConversationReplyReactionBase(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        Repository = repository;
        _contactEnricher = contactEnricher;
    }

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
        {
            var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
            conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
            await Repository.UpsertConversationAsync(conversation, cancellationToken);
        }

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
