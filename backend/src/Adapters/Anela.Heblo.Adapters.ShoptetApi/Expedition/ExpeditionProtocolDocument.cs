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
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Content().Column(col =>
            {
                // Title
                col.Item().Text($"Objednávky k expedici – {_data.CarrierDisplayName}")
                    .FontSize(16).Bold();

                col.Item().PaddingBottom(10);

                // Per-order sections
                foreach (var order in _data.Orders)
                {
                    col.Item().Column(orderCol =>
                    {
                        // Order heading
                        orderCol.Item().Text($"Objednávka {order.Code}")
                            .FontSize(12).Bold();

                        // Barcode
                        var barcodeBytes = GenerateBarcode(order.Code);
                        orderCol.Item().Image(barcodeBytes).FitWidth();

                        // Customer info
                        orderCol.Item().PaddingTop(4).Column(info =>
                        {
                            info.Item().Text(order.CustomerName);
                            info.Item().Text(order.Address);
                            info.Item().Text(order.Phone);
                        });

                        orderCol.Item().PaddingTop(6);

                        // Items table
                        orderCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);  // Kód
                                cols.RelativeColumn(5);  // Popis položky
                                cols.RelativeColumn(1);  // Množství
                                cols.RelativeColumn(2);  // Stav skladu
                                cols.RelativeColumn(2);  // Cena za m.j.
                                cols.RelativeColumn(1);  // Zkompletováno
                            });

                            // Header row
                            table.Header(header =>
                            {
                                header.Cell().Text("Kód").Bold();
                                header.Cell().Text("Popis položky").Bold();
                                header.Cell().Text("Množství").Bold();
                                header.Cell().Text("Stav skladu").Bold();
                                header.Cell().Text("Cena za m.j.").Bold();
                                header.Cell().Text("Zkompletováno").Bold();
                            });

                            foreach (var item in order.Items)
                            {
                                table.Cell().Text(item.ProductCode);
                                table.Cell().Column(itemCol =>
                                {
                                    itemCol.Item().Text(item.Name);
                                    if (!string.IsNullOrEmpty(item.Variant))
                                        itemCol.Item().Text($"Varianta: {item.Variant}").FontSize(9).Italic();
                                    if (!string.IsNullOrEmpty(item.WarehousePosition))
                                        itemCol.Item().Text($"Pozice ve skladu: {item.WarehousePosition}").FontSize(9).Italic();
                                });
                                table.Cell().Text(item.Quantity.ToString());
                                table.Cell().Text(item.StockCount.ToString());
                                table.Cell().Text(item.UnitPrice.ToString("N2"));
                                table.Cell().Text("☐");
                            }
                        });

                        orderCol.Item().PaddingBottom(15);
                    });
                }

                // Summary page
                col.Item().PageBreak();
                col.Item().Text("Souhrn").FontSize(14).Bold();
                col.Item().PaddingBottom(8);

                var aggregated = _data.Orders
                    .SelectMany(o => o.Items)
                    .GroupBy(i => i.ProductCode)
                    .Select(g => new { Code = g.Key, Name = g.First().Name, TotalQty = g.Sum(i => i.Quantity) })
                    .OrderBy(x => x.Code)
                    .ToList();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(6);
                        cols.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Kód").Bold();
                        header.Cell().Text("Název").Bold();
                        header.Cell().Text("Celkové množství").Bold();
                    });

                    foreach (var row in aggregated)
                    {
                        table.Cell().Text(row.Code);
                        table.Cell().Text(row.Name);
                        table.Cell().Text(row.TotalQty.ToString());
                    }
                });
            });
        });
    }

    private static byte[] GenerateBarcode(string text)
    {
        var writer = new BarcodeWriter<SKBitmap>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 300, Height = 80, Margin = 10 },
            Renderer = new SKBitmapRenderer(),
        };

        using var bitmap = writer.Write(text);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
