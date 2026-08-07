using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ContactUpsertOnlyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;

    protected ContactUpsertOnlyReactionBase(ISmartsuppRepository repository) => Repository = repository;

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        await Repository.UpsertContactAsync(SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp), cancellationToken);
    }
}
