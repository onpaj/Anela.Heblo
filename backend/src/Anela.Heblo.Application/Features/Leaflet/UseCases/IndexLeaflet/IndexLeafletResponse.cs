using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletResponse : BaseResponse
{
    public Guid DocumentId { get; set; }
    public bool WasDuplicate { get; set; }
    public int ChunkCount { get; set; }
}
