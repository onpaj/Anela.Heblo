using System.Text.Json;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;

public static class SmartsuppPayloadMapper
{
    private const int LastMessagePreviewMaxLength = 200;

    public static SmartsuppConversation MapConversation(JsonElement data, DateTime syncedAt)
    {
        var statusStr = TryGetString(data, "status")?.ToLowerInvariant();
        var status = statusStr switch
        {
            "open" => SmartsuppConversationStatus.Open,
            "closed" => SmartsuppConversationStatus.Resolved,
            "pending" => SmartsuppConversationStatus.Pending,
            _ => SmartsuppConversationStatus.Open,
        };

        var lastMessageText = TryGetString(data, "last_message_text");
        var channel = data.TryGetProperty("channel", out var ch) && ch.ValueKind == JsonValueKind.Object
            ? TryGetString(ch, "type")
            : null;

        return new SmartsuppConversation
        {
            Id = data.GetProperty("id").GetString() ?? "",
            ExtId = TryGetString(data, "ext_id"),
            Subject = TryGetString(data, "subject"),
            Status = status,
            IsUnread = TryGetBool(data, "unread") ?? false,
            IsOffline = TryGetBool(data, "is_offline") ?? false,
            IsServed = TryGetBool(data, "is_served") ?? false,
            ContactId = TryGetString(data, "contact_id"),
            VisitorId = TryGetString(data, "visitor_id"),
            FinishedAt = ReadOptionalUtc(data, "finished_at"),
            Domain = TryGetString(data, "domain"),
            Referer = TryGetString(data, "referer"),
            Channel = channel,
            LastMessageAt = ReadOptionalUtc(data, "last_message_at"),
            LastMessagePreview = lastMessageText?.Length > LastMessagePreviewMaxLength
                ? lastMessageText[..LastMessagePreviewMaxLength]
                : lastMessageText,
            TagsJson = data.TryGetProperty("tags", out var tags) ? tags.GetRawText() : null,
            VariablesJson = data.TryGetProperty("variables", out var vars) ? vars.GetRawText() : null,
            AssignedAgentIdsJson = data.TryGetProperty("assigned_agent_ids", out var agentIds) ? agentIds.GetRawText() : null,
            Rating = TryGetInt(data, "rating"),
            RatingText = TryGetString(data, "rating_text"),
            CloseType = TryGetString(data, "close_type"),
            ClosedByAgentId = TryGetString(data, "closed_by_agent_id"),
            LastClosedAt = ReadOptionalUtc(data, "last_closed_at"),
            ContactName = TryGetString(data, "contact_name"),
            ContactEmail = TryGetString(data, "contact_email"),
            ContactAvatarUrl = TryGetString(data, "contact_avatar_url"),
            LocationCountry = TryGetString(data, "location_country"),
            LocationCity = TryGetString(data, "location_city"),
            LocationIp = TryGetString(data, "location_ip"),
            LocationCode = TryGetString(data, "location_code"),
            CreatedAt = ReadUtc(data, "created_at"),
            UpdatedAt = ReadUtc(data, "updated_at"),
            SyncedAt = syncedAt,
        };
    }

    public static SmartsuppMessage MapMessage(JsonElement data)
    {
        var subType = TryGetString(data, "sub_type");
        var contentText = data.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
            ? TryGetString(content, "text") ?? TryGetString(content, "html")
            : TryGetString(data, "content");

        return new SmartsuppMessage
        {
            Id = data.GetProperty("id").GetString() ?? "",
            ConversationId = data.TryGetProperty("conversation_id", out var cid)
                ? cid.GetString() ?? ""
                : "",
            AuthorType = ParseAuthorType(subType),
            SubType = subType,
            MessageType = TryGetString(data, "type"),
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
            AttachmentsJson = data.TryGetProperty("attachments", out var att) ? att.GetRawText() : null,
            CreatedAt = ReadUtc(data, "created_at"),
            UpdatedAt = ReadUtc(data, "updated_at"),
        };
    }

    public static SmartsuppContact MapContact(JsonElement data, DateTime syncedAt)
    {
        return new SmartsuppContact
        {
            Id = data.GetProperty("id").GetString() ?? "",
            Email = TryGetString(data, "email"),
            Name = TryGetString(data, "name"),
            Phone = TryGetString(data, "phone"),
            Note = TryGetString(data, "note"),
            BannedAt = ReadOptionalUtc(data, "banned_at"),
            BannedBy = TryGetString(data, "banned_by"),
            GdprApproved = TryGetBool(data, "gdpr_approved") ?? false,
            TagsJson = data.TryGetProperty("tags", out var tags) ? tags.GetRawText() : null,
            PropertiesJson = data.TryGetProperty("properties", out var props) ? props.GetRawText() : null,
            CreatedAt = ReadUtc(data, "created_at"),
            UpdatedAt = ReadUtc(data, "updated_at"),
            SyncedAt = syncedAt,
        };
    }

    public static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool? TryGetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : null;

    public static int? TryGetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static DateTime ReadUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : DateTime.UtcNow;

    public static DateTime? ReadOptionalUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : null;

    private static SmartsuppMessageAuthorType ParseAuthorType(string? subType) =>
        subType?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            "contact" => SmartsuppMessageAuthorType.Visitor,
            "system" => SmartsuppMessageAuthorType.System,
            "trigger" => SmartsuppMessageAuthorType.Trigger,
            _ => SmartsuppMessageAuthorType.Visitor,
        };
}
