using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
{
    public ConversationAgentRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "conversation.agent_replied";
}
