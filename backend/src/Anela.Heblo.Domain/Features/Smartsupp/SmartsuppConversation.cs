namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppConversation
{
    public string Id { get; set; } = null!;
    public string? ExtId { get; set; }
    public string? Subject { get; set; }
    public string? ContactId { get; set; }
    public SmartsuppContact? Contact { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAvatarUrl { get; set; }
    public string? VisitorId { get; set; }
    public SmartsuppConversationStatus Status { get; set; }
    public bool IsUnread { get; set; }
    public bool IsOffline { get; set; }
    public bool IsServed { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Domain { get; set; }
    public string? Referer { get; set; }
    public string? LocationCountry { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationIp { get; set; }
    public string? LocationCode { get; set; }
    public string? VariablesJson { get; set; }
    public string? TagsJson { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
    public List<SmartsuppMessage> Messages { get; set; } = new();
}
