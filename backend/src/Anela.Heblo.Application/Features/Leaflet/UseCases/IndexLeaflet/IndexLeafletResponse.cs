namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletResponse
{
    public Guid DocumentId { get; set; }
    public bool WasDuplicate { get; set; }
    public int ChunkCount { get; set; }
}
