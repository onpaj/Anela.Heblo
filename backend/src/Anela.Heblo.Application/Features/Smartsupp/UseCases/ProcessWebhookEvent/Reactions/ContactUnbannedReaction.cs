using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactUnbannedReaction : ContactUpsertOnlyReactionBase
{
    public ContactUnbannedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "contact.unbanned";
}
