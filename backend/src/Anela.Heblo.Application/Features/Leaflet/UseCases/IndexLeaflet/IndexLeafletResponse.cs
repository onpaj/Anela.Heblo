using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletResponse : BaseResponse
{
    public Guid DocumentId { get; set; }
    public bool WasDuplicate { get; set; }
    public int ChunkCount { get; set; }
    public LeafletDocumentStatus Status { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
}
