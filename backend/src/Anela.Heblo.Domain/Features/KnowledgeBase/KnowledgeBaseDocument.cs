namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex, 64 chars
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}

public enum DocumentStatus
{
    Processing,
    Indexed,
    Failed
}

public enum DocumentType
{
    KnowledgeBase = 0,
    Conversation = 1
}
