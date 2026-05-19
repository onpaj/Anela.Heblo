using MediatR;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;

public class GetShipmentLabelPdfRequest : IRequest<GetShipmentLabelPdfResponse>
{
    public string OrderCode { get; set; } = null!;
    public Guid ShipmentGuid { get; set; }
    public string PackageName { get; set; } = null!;
}
