namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public LeafletDocument Document { get; set; } = null!;
}
