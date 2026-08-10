using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationContactRepliedReaction : ConversationReplyReactionBase
{
    public ConversationContactRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "conversation.contact_replied";
}
