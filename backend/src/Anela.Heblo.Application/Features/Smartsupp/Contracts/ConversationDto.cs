namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class ConversationDto
{
    public string Id { get; set; } = null!;
    public string? Subject { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAvatarUrl { get; set; }
    public string Status { get; set; } = null!;
    public bool IsUnread { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
