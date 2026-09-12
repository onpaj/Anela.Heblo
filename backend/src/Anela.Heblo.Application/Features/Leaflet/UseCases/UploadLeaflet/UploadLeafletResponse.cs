using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;

public class UploadLeafletResponse : BaseResponse
{
    public LeafletDocumentSummary? Document { get; set; }
}
