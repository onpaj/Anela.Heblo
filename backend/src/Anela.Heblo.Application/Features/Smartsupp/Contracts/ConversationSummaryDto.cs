namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class ConversationSummaryDto
{
    public string Id { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public bool IsUnread { get; set; }
}
