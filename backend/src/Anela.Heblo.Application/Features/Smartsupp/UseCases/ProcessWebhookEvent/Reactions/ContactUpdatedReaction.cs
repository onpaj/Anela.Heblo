using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactUpdatedReaction : ContactUpsertWithBackfillReactionBase
{
    public ContactUpdatedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "contact.updated";
}
