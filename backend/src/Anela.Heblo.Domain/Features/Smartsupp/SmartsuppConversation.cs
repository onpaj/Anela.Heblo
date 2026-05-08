namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppConversation
{
    public string Id { get; set; } = null!;
    public string? Subject { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAvatarUrl { get; set; }
    public SmartsuppConversationStatus Status { get; set; }
    public bool IsUnread { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
    public List<SmartsuppMessage> Messages { get; set; } = new();
}
