using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactBannedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ContactBannedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "contact.banned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        await _repository.UpsertContactAsync(SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp), cancellationToken);
    }
}
