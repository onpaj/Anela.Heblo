using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using QuestPDF.Fluent;

namespace Anela.Heblo.API.PDFPrints;

public class QuestPdfManufactureProtocolRenderer : IManufactureProtocolRenderer
{
    public byte[] Render(ManufactureProtocolData data)
    {
        return new ManufactureProtocolDocument(data).GeneratePdf();
    }
}
