using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;
using ZXing.SkiaSharp.Rendering;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ExpeditionProtocolDocument : IDocument
{
    private readonly ExpeditionProtocolData _data;
    public ExpeditionProtocolDocument(ExpeditionProtocolData data)
    {
        _data = data;
    }

    public static byte[] Generate(ExpeditionProtocolData data)
    {
        var doc = new ExpeditionProtocolDocument(data);
        return doc.GeneratePdf();
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

            page.Content().Column(col =>
            {
                // Title
                col.Item().Text($"Objednávky k expedici – {_data.CarrierDisplayName}")
                    .FontSize(12).Bold();

                col.Item().PaddingBottom(6);

                // Per-order sections
                foreach (var order in _data.Orders)
                {
                    col.Item().Column(orderCol =>
                    {
                        // Order heading: "Objednávka " + bold code — 30% larger than body (9 * 1.3 ≈ 12)
                        orderCol.Item().Text(t =>
                        {
                            t.Span("Objednávka ").FontSize(12);
                            t.Span(order.Code).Bold().FontSize(12);
                        });

                        // Barcode — 60% of full width
                        var barcodeBytes = GenerateBarcode(order.Code);
                        orderCol.Item().Height(30).MaxWidth(300).Image(barcodeBytes).FitHeight();

                        // Customer info — single right-aligned line
                        orderCol.Item().AlignRight().Text(
                            $"{order.CustomerName}, {order.Address} {order.Phone}".Trim())
                            .FontSize(9);

                        orderCol.Item().PaddingTop(3);

                        // Items table
                        orderCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);   // Kód
                                cols.RelativeColumn(6);   // Popis položky
                                cols.RelativeColumn(2);   // Množství
                                cols.RelativeColumn(2);   // Stav skladu
                                cols.RelativeColumn(1.5f); // OK
                            });

                            // Header row
                            static IContainer HeaderCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Background(Colors.Grey.Lighten3)
                                 .Padding(3);

                            static IContainer HeaderCellCenter(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Background(Colors.Grey.Lighten3)
                                 .Padding(3).AlignCenter();

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Kód").Bold();
                                header.Cell().Element(HeaderCell).Text("Popis položky").Bold();
                                header.Cell().Element(HeaderCellCenter).Text("Množství").Bold();
                                header.Cell().Element(HeaderCellCenter).Text("Stav skladu").Bold();
                                header.Cell().Element(HeaderCellCenter).Text("OK").Bold();
                            });

                            static IContainer DataCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);

                            static IContainer CenteredDataCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Padding(3).AlignCenter().AlignMiddle();

                            foreach (var item in order.Items)
                            {
                                table.Cell().Element(DataCell).Text(item.ProductCode);

                                table.Cell().Element(DataCell).Column(itemCol =>
                                {
                                    itemCol.Item().Text(item.Name);
                                    if (!string.IsNullOrEmpty(item.Variant))
                                        itemCol.Item().Text($"Varianta: {item.Variant}").FontSize(8).Italic();
                                    if (!string.IsNullOrEmpty(item.WarehousePosition))
                                        itemCol.Item().Text($"Pozice ve skladu: {item.WarehousePosition}").FontSize(8).Italic();
                                });

                                table.Cell().Element(CenteredDataCell)
                                    .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(18).Bold();
                                table.Cell().Element(CenteredDataCell)
                                    .Text(FormatAmount(item.StockCount, item.Unit));
                                table.Cell().Element(CenteredDataCell).Text("☐").FontSize(14);
                            }
                        });

                        orderCol.Item().PaddingTop(8).PaddingBottom(4)
                            .LineHorizontal(1.5f).LineColor(Colors.Grey.Darken2);
                    });
                }

                // Summary page
                col.Item().PageBreak();
                col.Item().Text("Položky objednávek").FontSize(12).Bold();
                col.Item().PaddingBottom(6);

                var aggregated = _data.Orders
                    .SelectMany(o => o.Items)
                    .GroupBy(i => i.ProductCode)
                    .Select(g =>
                    {
                        var first = g.First();
                        return new
                        {
                            Code = g.Key,
                            first.Name,
                            first.Variant,
                            first.WarehousePosition,
                            first.Unit,
                            TotalQty = g.Sum(i => i.Quantity),
                            first.StockCount,
                            first.StockDemand,
                        };
                    })
                    .OrderBy(x => x.WarehousePosition)
                    .ToList();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);   // Kód
                        cols.RelativeColumn(6);   // Popis položky
                        cols.RelativeColumn(2);   // Množství
                        cols.RelativeColumn(2);   // Stav skladu
                    });

                    static IContainer SummaryHeaderCell(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                         .Background(Colors.Grey.Lighten3)
                         .Padding(3);

                    static IContainer SummaryHeaderCellCenter(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                         .Background(Colors.Grey.Lighten3)
                         .Padding(3).AlignCenter();

                    table.Header(header =>
                    {
                        header.Cell().Element(SummaryHeaderCell).Text("Kód").Bold();
                        header.Cell().Element(SummaryHeaderCell).Text("Popis položky").Bold();
                        header.Cell().Element(SummaryHeaderCellCenter).Text("Množství").Bold();
                        header.Cell().Element(SummaryHeaderCellCenter).Text("Stav skladu").Bold();
                    });

                    static IContainer SummaryDataCell(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);

                    static IContainer SummaryCenteredDataCell(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                         .Padding(3).AlignCenter().AlignMiddle();

                    foreach (var row in aggregated)
                    {
                        table.Cell().Element(SummaryDataCell).Text(row.Code);

                        table.Cell().Element(SummaryDataCell).Column(itemCol =>
                        {
                            itemCol.Item().Text(row.Name);
                            if (!string.IsNullOrEmpty(row.Variant))
                                itemCol.Item().Text($"Varianta: {row.Variant}").FontSize(8).Italic();
                            if (!string.IsNullOrEmpty(row.WarehousePosition))
                                itemCol.Item().Text($"Pozice ve skladu: {row.WarehousePosition}").FontSize(8).Italic();
                        });

                        table.Cell().Element(SummaryCenteredDataCell)
                            .Text(FormatAmount(row.TotalQty, row.Unit)).FontSize(18).Bold();
                        table.Cell().Element(SummaryCenteredDataCell)
                            .Text(FormatAmount(row.StockCount, row.Unit));
                    }
                });
            });
        });
    }

    private static string FormatAmount(int amount, string unit) =>
        string.IsNullOrEmpty(unit) ? amount.ToString() : $"{amount} {unit}";

    private static byte[] GenerateBarcode(string text)
    {
        var writer = new BarcodeWriter<SKBitmap>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 500, Height = 80, Margin = 5 },
            Renderer = new SKBitmapRenderer(),
        };

        using var bitmap = writer.Write(text);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
