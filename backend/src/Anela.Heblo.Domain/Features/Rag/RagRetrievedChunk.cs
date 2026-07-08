namespace Anela.Heblo.Domain.Features.Rag;

/// <summary>
/// One retrieved chunk as captured for the eval dataset: the full chunk content plus its
/// similarity score and source document metadata. Serialized into
/// <see cref="RagInteractionLog.RetrievedChunksJson"/> (jsonb).
/// </summary>
public class RagRetrievedChunk
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
