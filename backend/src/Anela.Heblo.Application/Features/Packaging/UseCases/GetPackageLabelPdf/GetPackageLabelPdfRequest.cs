using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfRequest : IRequest<GetPackageLabelPdfResponse>
{
    public string OrderCode { get; set; } = null!;

    /// <summary>1-based position of the package within the order's shipment labels.</summary>
    public int PackageNumber { get; set; }
}
