using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ContactUpsertWithBackfillReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;

    protected ContactUpsertWithBackfillReactionBase(ISmartsuppRepository repository) => Repository = repository;

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        var contact = SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp);
        await Repository.UpsertContactAsync(contact, cancellationToken);
        await Repository.BackfillConversationDenormFieldsAsync(contact, cancellationToken);
    }
}
