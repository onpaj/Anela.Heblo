using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationBotRepliedReaction : ConversationReplyReactionBase
{
    public ConversationBotRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "conversation.bot_replied";
}
