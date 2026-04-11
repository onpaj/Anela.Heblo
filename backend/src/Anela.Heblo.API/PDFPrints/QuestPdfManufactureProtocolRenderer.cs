using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.API.PDFPrints;

public class QuestPdfManufactureProtocolRenderer : IManufactureProtocolRenderer
{
    static QuestPdfManufactureProtocolRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(ManufactureProtocolData data)
    {
        return new ManufactureProtocolDocument(data).GeneratePdf();
    }
}
