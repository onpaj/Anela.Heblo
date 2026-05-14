namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentJoinedReaction : ISmartsuppWebhookReaction
{
    public string EventName => "conversation.agent_joined";

    public Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken) => Task.CompletedTask;
}
