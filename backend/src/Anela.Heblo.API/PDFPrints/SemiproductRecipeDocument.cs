using System.Globalization;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.API.PDFPrints;

public class SemiproductRecipeDocument : IDocument
{
    private readonly SemiproductRecipeData _data;

    public SemiproductRecipeDocument(SemiproductRecipeData data)
    {
        _data = data;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Column(header =>
            {
                header.Item().Text("Receptura polotovaru").FontSize(16).Bold();
                header.Item().Text($"{_data.ProductCode}  –  {_data.ProductName}");
                header.Item().Text(t =>
                {
                    t.Span("Velikost šarže: ").Bold();
                    t.Span(_data.BatchSize.ToString("0.###", CultureInfo.InvariantCulture));
                });
                header.Item().PaddingTop(4).LineHorizontal(1f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingTop(8).Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);     // Kód
                        cols.RelativeColumn(5);     // Název
                        cols.RelativeColumn(2);     // Plná šarže
                        cols.RelativeColumn(2);     // Půl šarže
                        cols.RelativeColumn(1.5f);  // %
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Kód").Bold();
                        header.Cell().Element(HeaderCell).Text("Název").Bold();
                        header.Cell().Element(HeaderCell).Text("Plná šarže").Bold();
                        header.Cell().Element(HeaderCell).Text("Půl šarže").Bold();
                        header.Cell().Element(HeaderCell).Text("%").Bold();
                    });

                    foreach (var ingredient in _data.Ingredients)
                    {
                        table.Cell().Element(DataCell).Text(ingredient.ProductCode);
                        table.Cell().Element(DataCell).Text(ingredient.ProductName);
                        table.Cell().Element(DataCell).Text(ingredient.AmountFullBatch.ToString("0.###", CultureInfo.InvariantCulture));
                        table.Cell().Element(DataCell).Text(ingredient.AmountHalfBatch.ToString("0.###", CultureInfo.InvariantCulture));
                        table.Cell().Element(DataCell).Text(ingredient.Percentage.ToString("0.00", CultureInfo.InvariantCulture) + "%");
                    }
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span("Strana ");
                t.CurrentPageNumber();
                t.Span(" z ");
                t.TotalPages();
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Background(Colors.Grey.Lighten3)
         .Padding(3);

    private static IContainer DataCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
}
