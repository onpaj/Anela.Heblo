using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationMessageDeliveryFailedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationMessageDeliveryFailedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.message_delivery_failed";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var msgEl = ctx.GetMessage();
        if (msgEl is null) return;

        var messageId = SmartsuppPayloadMapper.TryGetString(msgEl.Value, "id");
        if (messageId is null) return;

        await _repository.UpdateMessageDeliveryStatusAsync(messageId, "failed", null, cancellationToken);
    }
}
