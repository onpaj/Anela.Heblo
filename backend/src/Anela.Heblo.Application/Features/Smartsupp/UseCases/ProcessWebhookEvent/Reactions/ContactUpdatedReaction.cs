using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactUpdatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ContactUpdatedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "contact.updated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        var contact = SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp);
        await _repository.UpsertContactAsync(contact, cancellationToken);
        await _repository.BackfillConversationDenormFieldsAsync(contact, cancellationToken);
    }
}
