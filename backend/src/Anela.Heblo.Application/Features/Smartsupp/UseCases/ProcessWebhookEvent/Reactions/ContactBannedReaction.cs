using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactBannedReaction : ContactUpsertOnlyReactionBase
{
    public ContactBannedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "contact.banned";
}
