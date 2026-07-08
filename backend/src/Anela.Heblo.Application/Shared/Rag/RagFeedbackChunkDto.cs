namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>A retrieved chunk surfaced in the feedback browser (from RagInteractionLog.RetrievedChunksJson).</summary>
public class RagFeedbackChunkDto
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Content { get; set; } = string.Empty;
}
