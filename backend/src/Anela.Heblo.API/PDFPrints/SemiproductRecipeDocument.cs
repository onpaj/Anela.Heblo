using System.Globalization;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Anela.Heblo.API.PDFPrints;

public class SemiproductRecipeDocument : IDocument
{
    private readonly SemiproductRecipeData _data;

    private static readonly NumberFormatInfo AmountFormat = new NumberFormatInfo
    {
        NumberGroupSeparator = " ", // non-breaking space
        NumberGroupSizes = [3],
        NumberDecimalSeparator = ",",
        NumberDecimalDigits = 1,
    };

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
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(13));

            page.Header().Column(header =>
            {
                header.Item().Row(row =>
                {
                    row.RelativeItem()
                        .Text($"{_data.ProductCode}  –  {_data.ProductName}")
                        .FontSize(18).Bold();
                    row.ConstantItem(150).Column(rightCol =>
                    {
                        var productCode = _data.ProductCode;
                        rightCol.Item()
                            .Height(32)
                            .Canvas((canvas, size) =>
                            {
                                var hints = new Dictionary<EncodeHintType, object>
                                {
                                    { EncodeHintType.MARGIN, 0 }
                                };
                                var matrix = new MultiFormatWriter()
                                    .encode(productCode, BarcodeFormat.CODE_128, (int)size.Width, (int)size.Height, hints);

                                canvas.Clear(SKColors.White);
                                using var paint = new SKPaint { Color = SKColors.Black };
                                int midRow = matrix.Height / 2;
                                float scaleX = size.Width / matrix.Width;

                                for (int x = 0; x < matrix.Width; x++)
                                {
                                    if (matrix[x, midRow])
                                        canvas.DrawRect(x * scaleX, 0, scaleX, size.Height, paint);
                                }
                            });
                        rightCol.Item()
                            .AlignRight()
                            .PaddingTop(2)
                            .Text($"Tisk: {_data.PrintedAt:dd.MM.yyyy HH:mm}")
                            .FontSize(11)
                            .FontColor(Colors.Grey.Darken1);
                    });
                });
                header.Item().Row(row =>
                {
                    row.AutoItem().Text(t =>
                    {
                        t.Span("Výrobní dávka: ").Bold();
                        t.Span(_data.BatchSize.ToString("N1", AmountFormat) + " g");
                    });
                    if (_data.Mmq.HasValue)
                    {
                        row.AutoItem().PaddingLeft(24).Text(t =>
                        {
                            t.Span("MMQ: ").Bold();
                            t.Span(FormatAmount(_data.Mmq.Value) + " g");
                        });
                    }
                    if (_data.ExpirationMonths.HasValue)
                    {
                        row.AutoItem().PaddingLeft(24).Text(t =>
                        {
                            t.Span("Expirace: ").Bold();
                            t.Span($"{_data.ExpirationMonths}M");
                        });
                    }
                });
                header.Item().PaddingTop(6).LineHorizontal(1f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(1.5f);  // Kód
                        cols.RelativeColumn(6);     // Název
                        cols.RelativeColumn(1.5f);  // %
                        cols.RelativeColumn(2);     // Půl šarže
                        cols.RelativeColumn(2);     // Plná šarže
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Kód").Bold();
                        header.Cell().Element(HeaderCell).Text("Název").Bold();
                        header.Cell().Element(HeaderCell).AlignRight().Text("%").Bold();
                        header.Cell().Element(HeaderCell).AlignRight().Text("Půl šarže").Bold();
                        header.Cell().Element(HeaderCell).AlignRight().Text("Plná šarže").Bold();
                    });

                    string? previousPhase = null;
                    foreach (var ingredient in _data.Ingredients)
                    {
                        if (!string.IsNullOrEmpty(ingredient.PhaseLabel) && ingredient.PhaseLabel != previousPhase)
                        {
                            table.Cell().ColumnSpan(5).Element(PhaseHeaderCell).Text($"Fáze {ingredient.PhaseLabel}").Bold();
                        }
                        previousPhase = ingredient.PhaseLabel;

                        table.Cell().Element(DataCell).Text(ingredient.ProductCode);
                        table.Cell().Element(DataCell).Text(ingredient.ProductName);
                        table.Cell().Element(DataCell).AlignRight().Text(ingredient.Percentage.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                        table.Cell().Element(DataCell).AlignRight().Text(FormatAmount(ingredient.AmountHalfBatch));
                        table.Cell().Element(DataCell).AlignRight().Text(FormatAmount(ingredient.AmountFullBatch));
                    }
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken1));
                t.Span("Strana ");
                t.CurrentPageNumber();
                t.Span(" z ");
                t.TotalPages();
            });
        });
    }

    private static string FormatAmount(double value) =>
        value.ToString("N1", AmountFormat);

    private static IContainer HeaderCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Background(Colors.Grey.Lighten3)
         .Padding(4);

    private static IContainer DataCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).DefaultTextStyle(s => s.Bold());

    private static IContainer PhaseHeaderCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Indigo.Lighten2)
         .Background(Colors.Indigo.Lighten4)
         .Padding(4)
         .DefaultTextStyle(s => s.FontColor(Colors.Indigo.Darken2));
}
