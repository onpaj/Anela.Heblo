namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppApiClient
{
    Task<SmartsuppSearchResult> SearchConversationsAsync(
        DateTime? updatedAfter,
        string? cursor,
        int size,
        CancellationToken cancellationToken);

    Task<List<SmartsuppMessageData>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken);
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
    public string Status { get; set; } = null!;
    public bool Unread { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAvatarUrl { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public class SmartsuppMessageData
{
    public string Id { get; set; } = null!;
    public string AuthorType { get; set; } = null!;
    public string? AuthorName { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}
