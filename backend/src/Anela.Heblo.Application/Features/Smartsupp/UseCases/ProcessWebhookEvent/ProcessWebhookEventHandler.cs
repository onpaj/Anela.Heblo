using System.Text.Json;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventHandler : IRequestHandler<ProcessWebhookEventRequest, ProcessWebhookEventResponse>
{
    private const int LastMessagePreviewMaxLength = 200;

    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<ProcessWebhookEventHandler> _logger;

    public ProcessWebhookEventHandler(
        ISmartsuppRepository repository,
        ILogger<ProcessWebhookEventHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ProcessWebhookEventResponse> Handle(
        ProcessWebhookEventRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.EventName)
        {
            case "conversation.created":
            case "conversation.updated":
            case "conversation.closed":
                await HandleConversationAsync(request.Data, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("smartsupp webhook handled conversation event {Event}", request.EventName);
                return new ProcessWebhookEventResponse { Handled = true };

            case "message.created":
                await HandleMessageAsync(request.Data, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("smartsupp webhook handled message event {Event}", request.EventName);
                return new ProcessWebhookEventResponse { Handled = true };

            default:
                _logger.LogInformation("smartsupp webhook unknown event={Event}", request.EventName);
                return new ProcessWebhookEventResponse { Handled = false, Reason = "unknown event" };
        }
    }

    private async Task HandleConversationAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var id = data.GetProperty("id").GetString() ?? "";
        var statusStr = TryGetString(data, "status")?.ToLowerInvariant();
        var status = statusStr == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        var createdAt = ReadUtc(data, "created_at");
        var updatedAt = ReadUtc(data, "updated_at");
        var lastMessageText = TryGetString(data, "last_message_text");

        var conversation = new SmartsuppConversation
        {
            Id = id,
            ExtId = TryGetString(data, "ext_id"),
            Status = status,
            IsUnread = TryGetBool(data, "unread") ?? false,
            IsOffline = TryGetBool(data, "is_offline") ?? false,
            IsServed = TryGetBool(data, "is_served") ?? false,
            ContactId = TryGetString(data, "contact_id"),
            VisitorId = TryGetString(data, "visitor_id"),
            FinishedAt = ReadOptionalUtc(data, "finished_at"),
            Domain = TryGetString(data, "domain"),
            Referer = TryGetString(data, "referer"),
            LastMessageAt = ReadOptionalUtc(data, "last_message_at"),
            LastMessagePreview = lastMessageText?.Length > LastMessagePreviewMaxLength
                ? lastMessageText[..LastMessagePreviewMaxLength]
                : lastMessageText,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            SyncedAt = DateTime.UtcNow,
        };

        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }

    private async Task HandleMessageAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var conversationId = data.GetProperty("conversation_id").GetString() ?? "";
        var subType = TryGetString(data, "sub_type");
        var contentText = data.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
            ? TryGetString(content, "text")
            : TryGetString(data, "content");

        var message = new SmartsuppMessage
        {
            Id = data.GetProperty("id").GetString() ?? "",
            ConversationId = conversationId,
            AuthorType = ParseAuthorType(subType),
            SubType = subType,
            AuthorName = TryGetString(data, "author_name"),
            Content = contentText,
            TriggerName = TryGetString(data, "trigger_name"),
            TriggerId = TryGetString(data, "trigger_id"),
            PageUrl = TryGetString(data, "page_url"),
            AgentId = TryGetString(data, "agent_id"),
            VisitorId = TryGetString(data, "visitor_id"),
            DeliveryStatus = TryGetString(data, "delivery_status"),
            DeliveredAt = ReadOptionalUtc(data, "delivered_at"),
            IsOffline = TryGetBool(data, "is_offline") ?? false,
            IsReply = TryGetBool(data, "is_reply") ?? false,
            IsFirstReply = TryGetBool(data, "is_first_reply") ?? false,
            ResponseTime = TryGetInt(data, "response_time"),
            CreatedAt = ReadUtc(data, "created_at"),
            UpdatedAt = ReadUtc(data, "updated_at"),
        };

        await _repository.UpsertMessagesAsync(conversationId, new List<SmartsuppMessage> { message }, cancellationToken);
    }

    private static SmartsuppMessageAuthorType ParseAuthorType(string? subType) =>
        subType?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            "contact" => SmartsuppMessageAuthorType.Visitor,
            _ => SmartsuppMessageAuthorType.Visitor,
        };

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? TryGetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : null;

    private static int? TryGetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static DateTime ReadUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : DateTime.UtcNow;

    private static DateTime? ReadOptionalUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : null;
}
