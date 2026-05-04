namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex (64 chars)
    public DateTime IngestedAt { get; set; }
    public int WordCount { get; set; }
    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }
    public LeafletDocumentStatus Status { get; set; } = LeafletDocumentStatus.Processing;
    public DateTime? IndexedAt { get; set; }
    public ICollection<LeafletChunk> Chunks { get; set; } = new List<LeafletChunk>();
}
