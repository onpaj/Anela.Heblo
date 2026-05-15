namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppMessage
{
    public string Id { get; set; } = null!;
    public string ConversationId { get; set; } = null!;
    public SmartsuppConversation Conversation { get; set; } = null!;
    public SmartsuppMessageAuthorType AuthorType { get; set; }
    public string? SubType { get; set; }
    public string? MessageType { get; set; }
    public string? AuthorName { get; set; }
    public string? Content { get; set; }
    public string? TriggerName { get; set; }
    public string? TriggerId { get; set; }
    public string? PageUrl { get; set; }
    public string? AgentId { get; set; }
    public string? VisitorId { get; set; }
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public bool IsOffline { get; set; }
    public bool IsReply { get; set; }
    public bool IsFirstReply { get; set; }
    public int? ResponseTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? AttachmentsJson { get; set; }
}
