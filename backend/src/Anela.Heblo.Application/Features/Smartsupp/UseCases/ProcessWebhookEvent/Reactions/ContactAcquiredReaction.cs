using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactAcquiredReaction : ContactUpsertWithBackfillReactionBase
{
    public ContactAcquiredReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "contact.acquired";
}
