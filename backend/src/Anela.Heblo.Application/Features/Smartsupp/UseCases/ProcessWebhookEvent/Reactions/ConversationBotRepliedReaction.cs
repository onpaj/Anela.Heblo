using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationBotRepliedReaction : ConversationReplyReactionBase
{
    public ConversationBotRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.bot_replied";
}
