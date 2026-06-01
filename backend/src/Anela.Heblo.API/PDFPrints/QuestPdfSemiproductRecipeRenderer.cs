using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.API.PDFPrints;

public class QuestPdfSemiproductRecipeRenderer : ISemiproductRecipeRenderer
{
    static QuestPdfSemiproductRecipeRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(SemiproductRecipeData data)
    {
        return new SemiproductRecipeDocument(data).GeneratePdf();
    }
}
