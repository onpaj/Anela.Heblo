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

    // Rich context fields — sourced from SmartsuppConversation entity
    public int? Rating { get; set; }
    public string? RatingText { get; set; }
    public string? CloseType { get; set; }
    public string? ClosedByAgentId { get; set; }
    public List<string> AssignedAgentIds { get; set; } = new();
    public string? Channel { get; set; }
    public bool IsServed { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Domain { get; set; }
    public string? Referer { get; set; }
    public string? LocationCountry { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationCode { get; set; }
    public List<string> Tags { get; set; } = new();

    // Phase 1: contact-sourced fields
    public string? ContactPhone { get; set; }
    public string? ContactNote { get; set; }
    public List<string> ContactTags { get; set; } = new();
    public Dictionary<string, string> ContactProperties { get; set; } = new();

    // Phase 1: conversation-sourced fields
    public string? LocationIp { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
    public List<ConversationSummaryDto> OtherConversations { get; set; } = new();
}
