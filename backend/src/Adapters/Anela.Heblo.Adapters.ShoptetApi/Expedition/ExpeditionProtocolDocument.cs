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
            page.Margin(1f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(8));

            page.Content().Column(col =>
            {
                // Title
                col.Item().Text($"Objednávky k expedici – {_data.CarrierDisplayName}")
                    .FontSize(10).Bold();

                col.Item().PaddingBottom(4);

                // Per-order sections
                foreach (var order in _data.Orders)
                {
                    col.Item().Column(orderCol =>
                    {
                        // Order heading: "Objednávka " + bold code — 30% larger than body (9 * 1.3 ≈ 12)
                        orderCol.Item().Text(t =>
                        {
                            t.Span("Objednávka ").FontSize(10);
                            t.Span(order.Code).Bold().FontSize(10);
                        });

                        // Barcode — 60% of full width
                        var barcodeBytes = GenerateBarcode(order.Code);
                        orderCol.Item().Height(20).MaxWidth(200).Image(barcodeBytes).FitHeight();

                        // Customer info — single right-aligned line
                        orderCol.Item().AlignRight().Text(
                            $"{order.CustomerName}, {order.Address} {order.Phone}".Trim())
                            .FontSize(8);

                        // Notes — shown only when at least one remark is present
                        var hasCustomerRemark = !string.IsNullOrWhiteSpace(order.CustomerRemark);
                        var hasEshopRemark = !string.IsNullOrWhiteSpace(order.EshopRemark);
                        if (hasCustomerRemark || hasEshopRemark)
                        {
                            orderCol.Item().PaddingTop(2).Column(notesCol =>
                            {
                                if (hasCustomerRemark)
                                    notesCol.Item().Text($"Poznámka zákazníka: {order.CustomerRemark}")
                                        .FontSize(8).Italic();
                                if (hasEshopRemark)
                                    notesCol.Item().Text($"Interní poznámka: {order.EshopRemark}")
                                        .FontSize(8).Italic();
                            });
                        }

                        orderCol.Item().PaddingTop(2);

                        // Items table
                        orderCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);   // Kód
                                cols.RelativeColumn(5);   // Popis položky
                                cols.RelativeColumn(3);   // Varianta
                                cols.RelativeColumn(1.5f); // Množství
                                cols.RelativeColumn(2);   // Pozice
                                cols.RelativeColumn(2);   // Stav skladu
                            });

                            // Header row
                            static IContainer HeaderCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Background(Colors.Grey.Lighten3)
                                 .Padding(2);

                            static IContainer HeaderCellCenter(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Background(Colors.Grey.Lighten3)
                                 .Padding(2).AlignCenter();

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Kód").Bold();
                                header.Cell().Element(HeaderCell).Text("Popis položky").Bold();
                                header.Cell().Element(HeaderCell).Text("Varianta").Bold();
                                header.Cell().Element(HeaderCellCenter).Text("Množství").Bold();
                                header.Cell().Element(HeaderCellCenter).Text("Pozice").Bold();
                                header.Cell().Element(HeaderCellCenter).Text("Stav skladu").Bold();
                            });

                            static IContainer DataCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2);

                            static IContainer CenteredDataCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Padding(2).AlignCenter().AlignMiddle();

                            static IContainer SetHeaderCell(IContainer c) =>
                                c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                 .Background(Colors.Grey.Lighten2)
                                 .Padding(2);

                            var regularItems = order.Items.Where(i => i.SetName == null).ToList();
                            var setGroups = order.Items
                                .Where(i => i.SetName != null)
                                .GroupBy(i => i.SetName!)
                                .ToList();

                            foreach (var item in regularItems)
                            {
                                table.Cell().Element(DataCell).Text(item.ProductCode);
                                table.Cell().Element(DataCell).Text(item.Name);
                                table.Cell().Element(DataCell).Text(FormatVariant(item.Variant)).FontSize(8);
                                table.Cell().Element(CenteredDataCell)
                                    .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold();
                                table.Cell().Element(CenteredDataCell)
                                    .Text(item.WarehousePosition ?? string.Empty).FontSize(8);
                                table.Cell().Element(CenteredDataCell)
                                    .Text(item.StockCount.ToString("0.##"));
                            }

                            foreach (var group in setGroups)
                            {
                                // Sub-header spanning all 6 columns
                                table.Cell().ColumnSpan(6).Element(SetHeaderCell)
                                    .Text($"Sada: {group.Key}").Bold().FontSize(8);

                                foreach (var item in group)
                                {
                                    table.Cell().Element(DataCell).Text(item.ProductCode).Italic();
                                    table.Cell().Element(DataCell).Text(item.Name).Italic();
                                    table.Cell().Element(DataCell).Text(FormatVariant(item.Variant)).FontSize(8).Italic();
                                    table.Cell().Element(CenteredDataCell)
                                        .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold().Italic();
                                    table.Cell().Element(CenteredDataCell)
                                        .Text(item.WarehousePosition ?? string.Empty).FontSize(8).Italic();
                                    table.Cell().Element(CenteredDataCell)
                                        .Text(item.StockCount.ToString("0.##")).Italic();
                                }
                            }
                        });

                        orderCol.Item().PaddingTop(5).PaddingBottom(3)
                            .LineHorizontal(1f).LineColor(Colors.Grey.Darken2);
                    });
                }

                // Summary page
                col.Item().PageBreak();
                col.Item().Text("Položky objednávek").FontSize(10).Bold();
                col.Item().PaddingBottom(4);

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
                            first.IsFromSet,
                        };
                    })
                    .OrderBy(x => x.WarehousePosition == null)
                    .ThenBy(x => x.WarehousePosition)
                    .ToList();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);   // Kód
                        cols.RelativeColumn(5);   // Popis položky
                        cols.RelativeColumn(3);   // Varianta
                        cols.RelativeColumn(1.5f); // Množství
                        cols.RelativeColumn(2);   // Pozice
                        cols.RelativeColumn(2);   // Stav skladu
                    });

                    static IContainer SummaryHeaderCell(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                         .Background(Colors.Grey.Lighten3)
                         .Padding(3);

                    static IContainer SummaryHeaderCellCenter(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                         .Background(Colors.Grey.Lighten3)
                         .Padding(2).AlignCenter();

                    table.Header(header =>
                    {
                        header.Cell().Element(SummaryHeaderCell).Text("Kód").Bold();
                        header.Cell().Element(SummaryHeaderCell).Text("Popis položky").Bold();
                        header.Cell().Element(SummaryHeaderCell).Text("Varianta").Bold();
                        header.Cell().Element(SummaryHeaderCellCenter).Text("Množství").Bold();
                        header.Cell().Element(SummaryHeaderCellCenter).Text("Pozice").Bold();
                        header.Cell().Element(SummaryHeaderCellCenter).Text("Stav skladu").Bold();
                    });

                    static IContainer SummaryDataCell(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2);

                    static IContainer SummaryCenteredDataCell(IContainer c) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                         .Padding(2).AlignCenter().AlignMiddle();

                    foreach (var row in aggregated)
                    {
                        if (row.IsFromSet)
                        {
                            table.Cell().Element(SummaryDataCell).Text(row.Code).Italic();
                            table.Cell().Element(SummaryDataCell).Text(row.Name).Italic();
                            table.Cell().Element(SummaryDataCell).Text(FormatVariant(row.Variant)).FontSize(8).Italic();
                            table.Cell().Element(SummaryCenteredDataCell)
                                .Text(FormatAmount(row.TotalQty, row.Unit)).FontSize(11).Bold().Italic();
                            table.Cell().Element(SummaryCenteredDataCell)
                                .Text(row.WarehousePosition ?? string.Empty).FontSize(8).Italic();
                            table.Cell().Element(SummaryCenteredDataCell)
                                .Text(row.StockCount.ToString("0.##")).Italic();
                        }
                        else
                        {
                            table.Cell().Element(SummaryDataCell).Text(row.Code);
                            table.Cell().Element(SummaryDataCell).Text(row.Name);
                            table.Cell().Element(SummaryDataCell).Text(FormatVariant(row.Variant)).FontSize(8);
                            table.Cell().Element(SummaryCenteredDataCell)
                                .Text(FormatAmount(row.TotalQty, row.Unit)).FontSize(11).Bold();
                            table.Cell().Element(SummaryCenteredDataCell)
                                .Text(row.WarehousePosition ?? string.Empty).FontSize(8);
                            table.Cell().Element(SummaryCenteredDataCell)
                                .Text(row.StockCount.ToString("0.##"));
                        }
                    }
                });
            });
        });
    }

    private static string FormatAmount(int amount, string unit) =>
        string.IsNullOrEmpty(unit) || unit.Equals("ks", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString()
            : $"{amount} {unit}";

    private static string FormatVariant(string? variant)
    {
        if (string.IsNullOrEmpty(variant))
            return string.Empty;
        const string prefix = "Obsah: ";
        return variant.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? variant[prefix.Length..]
            : variant;
    }

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
