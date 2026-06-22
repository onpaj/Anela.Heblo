namespace Anela.Heblo.Application.Features.KnowledgeBase.Contracts;

public class DocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public Guid? FirstChunkId { get; set; }
}
