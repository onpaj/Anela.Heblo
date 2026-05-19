using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;

public class GetShipmentLabelPdfResponse : BaseResponse
{
    public Stream? PdfStream { get; set; }

    public GetShipmentLabelPdfResponse(Stream pdfStream)
    {
        PdfStream = pdfStream;
    }

    public GetShipmentLabelPdfResponse(ErrorCodes errorCode)
        : base(errorCode)
    {
    }
}
