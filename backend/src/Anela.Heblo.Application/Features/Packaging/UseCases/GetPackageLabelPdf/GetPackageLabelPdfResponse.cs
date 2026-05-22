using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfResponse : BaseResponse
{
    public Stream? Content { get; set; }
    public string ContentType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;

    public GetPackageLabelPdfResponse() { }

    public GetPackageLabelPdfResponse(ErrorCodes errorCode) : base(errorCode) { }
}
