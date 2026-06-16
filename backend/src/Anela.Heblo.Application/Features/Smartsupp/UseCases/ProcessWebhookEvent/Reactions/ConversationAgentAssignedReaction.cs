using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppWebhookMetrics _metrics;
    private readonly ILogger<ConversationAgentAssignedReaction> _logger;

    public ConversationAgentAssignedReaction(
        ISmartsuppRepository repository,
        ISmartsuppWebhookMetrics metrics,
        ILogger<ConversationAgentAssignedReaction> logger)
    {
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp, _logger, _metrics);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
