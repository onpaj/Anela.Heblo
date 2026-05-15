namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public interface ISmartsuppWebhookReaction
{
    string EventName { get; }
    Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken);
}
