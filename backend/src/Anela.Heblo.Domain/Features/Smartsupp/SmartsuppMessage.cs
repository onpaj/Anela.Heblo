namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppMessage
{
    public string Id { get; set; } = null!;
    public string ConversationId { get; set; } = null!;
    public SmartsuppConversation Conversation { get; set; } = null!;
    public SmartsuppMessageAuthorType AuthorType { get; set; }
    public string? AuthorName { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AttachmentsJson { get; set; }
}
