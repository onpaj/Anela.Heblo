namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
    public float[] Embedding { get; set; } = Array.Empty<float>();

    public KnowledgeBaseDocument Document { get; set; } = null!;
}
