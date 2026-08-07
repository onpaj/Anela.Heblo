using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactCreatedReaction : ContactUpsertWithBackfillReactionBase
{
    public ContactCreatedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "contact.created";
}
