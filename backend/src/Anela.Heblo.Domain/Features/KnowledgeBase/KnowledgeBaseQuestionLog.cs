namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseQuestionLog
{
    public Guid Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int TopK { get; set; }
    public int SourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UserId { get; set; }
}
