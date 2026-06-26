namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentLeftReaction : ISmartsuppWebhookReaction
{
    public string EventName => "conversation.agent_left";

    public Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken) => Task.CompletedTask;
}
