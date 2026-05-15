using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationOpenedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationOpenedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.opened";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
