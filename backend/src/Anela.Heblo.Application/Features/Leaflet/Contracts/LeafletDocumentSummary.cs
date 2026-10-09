namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

public class LeafletDocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public Guid? FirstChunkId { get; set; }
}
