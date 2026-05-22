namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppApiClient
{
    Task<SmartsuppSearchResult> SearchConversationsAsync(
        string? cursor,
        int size,
        CancellationToken cancellationToken);

    Task<List<SmartsuppMessageData>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken);

    Task<SmartsuppConversationData?> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken);

    Task<SmartsuppContactData?> GetContactAsync(
        string contactId,
        CancellationToken cancellationToken);

    Task<SmartsuppVisitorData?> GetVisitorAsync(
        string visitorId,
        CancellationToken cancellationToken);

    Task<SmartsuppSentMessageData> SendMessageAsync(
        string conversationId,
        string content,
        string? agentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SmartsuppAgentData>> GetAgentsAsync(CancellationToken cancellationToken);
}

public class SmartsuppSearchResult
{
    public int Total { get; set; }
    public string? After { get; set; }
    public List<SmartsuppConversationData> Items { get; set; } = new();
}

public class SmartsuppConversationData
{
    public string Id { get; set; } = null!;
    public string? ExtId { get; set; }
    public string? Status { get; set; }
    public bool Unread { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ContactId { get; set; }
    public string? VisitorId { get; set; }
    public List<string> AgentIds { get; set; } = new();
    public List<string> AssignedIds { get; set; } = new();
    public string? GroupId { get; set; }
    public int? RatingValue { get; set; }
    public string? RatingText { get; set; }
    public string? Domain { get; set; }
    public string? Referer { get; set; }
    public bool IsOffline { get; set; }
    public bool IsServed { get; set; }
    public string? ChannelType { get; set; }
    public string? ChannelId { get; set; }
    public string? LocationCountry { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationIp { get; set; }
    public string? LocationCode { get; set; }
    public string? VariablesJson { get; set; }
    public string? TagsJson { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAvatarUrl { get; set; }
}

public class SmartsuppMessageData
{
    public string Id { get; set; } = null!;
    public string? ExtId { get; set; }
    public string? Type { get; set; }
    public string? SubType { get; set; }
    public string? Content { get; set; }
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ConversationId { get; set; }
    public string? VisitorId { get; set; }
    public string? AgentId { get; set; }
    public string? TriggerId { get; set; }
    public string? TriggerName { get; set; }
    public string? DeliveryTo { get; set; }
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public bool IsReply { get; set; }
    public bool IsFirstReply { get; set; }
    public bool IsOffline { get; set; }
    public bool IsOfflineReply { get; set; }
    public int? ResponseTime { get; set; }
    public string? PageUrl { get; set; }
    public string? AttachmentsJson { get; set; }
    public string? ChannelType { get; set; }
    public string? ChannelId { get; set; }
    public string? AuthorType { get; set; }
    public string? AuthorName { get; set; }
}

public class SmartsuppVisitorData
{
    public string Id { get; set; } = null!;
    public string? UserAgent { get; set; }
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }
    public int? VisitsCount { get; set; }
}

public class SmartsuppContactData
{
    public string Id { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BannedBy { get; set; }
    public bool GdprApproved { get; set; }
    public string? TagsJson { get; set; }
    public string? PropertiesJson { get; set; }
}

public class SmartsuppSentMessageData
{
    public string Id { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class SmartsuppAgentData
{
    public string Id { get; set; } = null!;
    public string? Name { get; set; }
    public string? Email { get; set; }
}
