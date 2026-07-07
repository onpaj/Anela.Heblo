using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfRequest : IRequest<GetPackageLabelPdfResponse>
{
    public string OrderCode { get; set; } = null!;

    /// <summary>1-based position of the package within the order's shipment labels.</summary>
    public int PackageNumber { get; set; }

    /// <summary>True when the request is an automated readiness poll (header X-Label-Poll present).
    /// Expected transient 404s are logged at Debug instead of Warning to avoid flooding logs.</summary>
    public bool IsPoll { get; set; }
}
