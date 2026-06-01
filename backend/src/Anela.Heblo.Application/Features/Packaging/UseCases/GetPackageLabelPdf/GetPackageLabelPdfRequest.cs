using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfRequest : IRequest<GetPackageLabelPdfResponse>
{
    public string OrderCode { get; set; } = null!;
    public string PackageName { get; set; } = null!;
}
