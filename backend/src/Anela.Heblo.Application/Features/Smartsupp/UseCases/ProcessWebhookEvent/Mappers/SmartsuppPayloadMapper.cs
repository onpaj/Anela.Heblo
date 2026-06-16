using System.Text.Json;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;

public static class SmartsuppPayloadMapper
{
    private const int SubjectMaxLength = 2000;
    private const int ContactAvatarUrlMaxLength = 2000;
    private const int ContactNameMaxLength = 200;
    private const int ContactEmailMaxLength = 200;
    private const int DomainMaxLength = 200;
    private const int LocationCountryMaxLength = 100;
    private const int LocationCityMaxLength = 100;
    private const int LocationIpMaxLength = 50;
    private const int LocationCodeMaxLength = 10;
    private const int CloseTypeMaxLength = 50;
    private const int ChannelMaxLength = 50;
    private const int RatingTextMaxLength = 1000;
    private const int LastMessagePreviewMaxLength = 200;

    public static SmartsuppConversation MapConversation(JsonElement data, DateTime syncedAt, ILogger logger, ISmartsuppWebhookMetrics metrics)
    {
        var conversationId = data.GetProperty("id").GetString() ?? "";

        var statusStr = TryGetString(data, "status")?.ToLowerInvariant();
        var status = statusStr switch
        {
            "open" => SmartsuppConversationStatus.Open,
            "closed" => SmartsuppConversationStatus.Resolved,
            "pending" => SmartsuppConversationStatus.Pending,
            _ => SmartsuppConversationStatus.Open,
        };

        var lastMessageText = TryGetString(data, "last_message_text");
        var channelRaw = data.TryGetProperty("channel", out var ch) && ch.ValueKind == JsonValueKind.Object
            ? TryGetString(ch, "type")
            : null;

        string? Trunc(string? v, int max, string field) =>
            StringTruncation.Truncate(v, max, field, conversationId, logger, metrics);

        return new SmartsuppConversation
        {
            Id = conversationId,
            ExtId = TryGetString(data, "ext_id"),
            Subject = Trunc(TryGetString(data, "subject"), SubjectMaxLength, "subject"),
            Status = status,
            IsUnread = TryGetBool(data, "unread") ?? false,
            IsOffline = TryGetBool(data, "is_offline") ?? false,
            IsServed = TryGetBool(data, "is_served") ?? false,
            ContactId = TryGetString(data, "contact_id"),
            VisitorId = TryGetString(data, "visitor_id"),
            FinishedAt = ReadOptionalUtc(data, "finished_at"),
            Domain = Trunc(TryGetString(data, "domain"), DomainMaxLength, "domain"),
            Referer = TryGetString(data, "referer"),
            Channel = Trunc(channelRaw, ChannelMaxLength, "channel"),
            LastMessageAt = ReadOptionalUtc(data, "last_message_at"),
            LastMessagePreview = lastMessageText?.Length > LastMessagePreviewMaxLength
                ? lastMessageText[..LastMessagePreviewMaxLength]
                : lastMessageText,
            TagsJson = data.TryGetProperty("tags", out var tags) ? tags.GetRawText() : null,
            VariablesJson = data.TryGetProperty("variables", out var vars) ? vars.GetRawText() : null,
            AssignedAgentIdsJson = data.TryGetProperty("assigned_agent_ids", out var agentIds) ? agentIds.GetRawText() : null,
            Rating = TryGetInt(data, "rating"),
            RatingText = Trunc(TryGetString(data, "rating_text"), RatingTextMaxLength, "rating_text"),
            CloseType = Trunc(TryGetString(data, "close_type"), CloseTypeMaxLength, "close_type"),
            ClosedByAgentId = TryGetString(data, "closed_by_agent_id"),
            LastClosedAt = ReadOptionalUtc(data, "last_closed_at"),
            ContactName = Trunc(TryGetString(data, "contact_name"), ContactNameMaxLength, "contact_name"),
            ContactEmail = Trunc(TryGetString(data, "contact_email"), ContactEmailMaxLength, "contact_email"),
            ContactAvatarUrl = Trunc(TryGetString(data, "contact_avatar_url"), ContactAvatarUrlMaxLength, "contact_avatar_url"),
            LocationCountry = Trunc(TryGetString(data, "location_country"), LocationCountryMaxLength, "location_country"),
            LocationCity = Trunc(TryGetString(data, "location_city"), LocationCityMaxLength, "location_city"),
            LocationIp = Trunc(TryGetString(data, "location_ip"), LocationIpMaxLength, "location_ip"),
            LocationCode = Trunc(TryGetString(data, "location_code"), LocationCodeMaxLength, "location_code"),
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
