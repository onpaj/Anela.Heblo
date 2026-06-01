using System.Globalization;
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
    // Layout constants — single source of truth for visual tuning.
    private const float BorderThickness = 1.5f;
    private const float BorderPadding = 4f;
    private const float OrderGap = 6f;
    // Column relative widths for both per-order and summary tables.
    private const float KodCol = 2f;
    private const float PopisCol = 8f; // absorbs former Varianta column (current 5 + 3)
    private const float MnozstviCol = 1.5f;
    private const float PoziceCol = 2f;
    private const float CenaCol = 2f;
    private const float StavCol = 2f;

    // Frost badge layout constants
    private const float FrostIconSize = 12f;
    private const float FrostBadgePadding = 3f;
    private const float FrostBadgeBorderThickness = 1.5f;
    private const string DefaultCoolingText = "CHLAZENÁ ZÁSILKA";

    private static readonly byte[] FrostIconBytes = GenerateFrostIcon();

    // Gift badge layout constants (same values as frost badge)
    private const float GiftIconSize = 12f;
    private const float GiftBadgePadding = 3f;
    private const float GiftBadgeBorderThickness = 1.5f;
    private const float GiftBadgePaddingLeft = 4f;

    private static readonly byte[] GiftIconBytes = GenerateGiftIcon();

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

            if (!string.IsNullOrWhiteSpace(_data.ListId))
            {
                page.Header().AlignRight().Text($"ID: {_data.ListId}")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
            }

            page.Content().Column(col =>
            {
                // Title
                col.Item().Text($"Objednávky k expedici – {_data.CarrierDisplayName}")
                    .FontSize(10).Bold();

                col.Item().PaddingBottom(4);

                // Per-order sections
                foreach (var order in _data.Orders)
                {
                    ComposeOrderBlock(col.Item(), order);
                }

                ComposeSummaryPage(col.Item());
            });
        });
    }

    private void ComposeOrderBlock(IContainer container, ExpeditionOrder order)
    {
        container
            .PaddingBottom(OrderGap)
            .Border(BorderThickness)
            .BorderColor(Colors.Grey.Darken2)
            .Padding(BorderPadding)
            .Column(orderCol =>
        {
            // Order heading row: order number on the left, frost badge on the right (if cooled)
            orderCol.Item().Row(headingRow =>
            {
                headingRow.RelativeItem().AlignMiddle().Text(t =>
                {
                    t.Span("Objednávka ").FontSize(10);
                    t.Span(order.Code).Bold().FontSize(10);
                });

                if (order.IsCooled)
                {
                    headingRow.AutoItem()
                        .Border(FrostBadgeBorderThickness)
                        .BorderColor(Colors.Black)
                        .Padding(FrostBadgePadding)
                        .Row(row =>
                        {
                            row.AutoItem().Width(FrostIconSize).Height(FrostIconSize).Image(FrostIconBytes).FitArea();
                            row.AutoItem().PaddingLeft(3).AlignMiddle()
                                .Text(string.IsNullOrWhiteSpace(order.CoolingText) ? DefaultCoolingText : order.CoolingText)
                                .Bold().FontSize(10).FontColor(Colors.Black);
                        });
                }

                if (!string.IsNullOrEmpty(order.GiftBadgeText))
                {
                    headingRow.AutoItem()
                        .PaddingLeft(GiftBadgePaddingLeft)
                        .Border(GiftBadgeBorderThickness)
                        .BorderColor(Colors.Black)
                        .Padding(GiftBadgePadding)
                        .Row(row =>
                        {
                            row.AutoItem().Width(GiftIconSize).Height(GiftIconSize).Image(GiftIconBytes).FitArea();
                            row.AutoItem().PaddingLeft(3).AlignMiddle()
                                .Text(order.GiftBadgeText)
                                .Bold().FontSize(10).FontColor(Colors.Black);
                        });
                }
            });

            // Barcode — 60% of full width
            var barcodeBytes = GenerateBarcode(order.Code);
            orderCol.Item().Height(20).MaxWidth(200).Image(barcodeBytes).FitHeight();

            // Customer info — single right-aligned line
            orderCol.Item().AlignRight().Text(
                $"{order.CustomerName}, {order.Address} {order.Phone}".Trim())
                .FontSize(8);

            // Items table
            BuildItemsTable(orderCol.Item(), order.Items);

            // Notes — shown only when at least one remark is present
            // Appears below items table with PaddingTop(2) separator
            var hasCustomerRemark = !string.IsNullOrWhiteSpace(order.CustomerRemark);
            var hasEshopRemark = !string.IsNullOrWhiteSpace(order.EshopRemark);
            if (hasCustomerRemark || hasEshopRemark)
            {
                orderCol.Item().PaddingTop(2).Column(notesCol =>
                {
                    if (hasCustomerRemark)
                        notesCol.Item().Text($"Poznámka zákazníka: {order.CustomerRemark}")
                            .FontSize(8).Italic().Bold();
                    if (hasEshopRemark)
                        notesCol.Item().Text($"Interní poznámka: {order.EshopRemark}")
                            .FontSize(8).Italic().Bold();
                });
            }

        });
    }

    private void ComposeSummaryPage(IContainer container)
    {
        container.Column(summaryCol =>
        {
            summaryCol.Item().PageBreak();
            summaryCol.Item().Text("Položky objednávek").FontSize(10).Bold();
            summaryCol.Item().PaddingBottom(4);

            var rows = _data.Orders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.ProductCode)
                .Select(g =>
                {
                    var first = g.First();
                    return new SummaryRow
                    {
                        ProductCode = g.Key,
                        Name = first.Name,
                        Variant = first.Variant,
                        WarehousePosition = first.WarehousePosition,
                        Unit = first.Unit,
                        TotalQuantity = g.Sum(i => i.Quantity),
                        StockCount = first.StockCount,
                        IsFromSet = first.IsFromSet,
                    };
                })
                .OrderBy(r => r.WarehousePosition == null)
                .ThenBy(r => r.WarehousePosition)
                .ToList();

            BuildSummaryTable(summaryCol.Item(), rows);
        });
    }

    private void BuildItemsTable(IContainer container, IReadOnlyList<ExpeditionOrderItem> items)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(KodCol);
                columns.RelativeColumn(PopisCol);
                columns.RelativeColumn(MnozstviCol);
                columns.RelativeColumn(CenaCol);
                columns.RelativeColumn(StavCol);
            });

            // Header row
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Kód").Bold();
                header.Cell().Element(HeaderCell).Text("Popis položky").Bold();
                header.Cell().Element(HeaderCellCenter).Text("Množství").Bold();
                header.Cell().Element(HeaderCellCenter).Text("Cena").Bold();
                header.Cell().Element(HeaderCellCenter).Text("Stav skladu").Bold();
            });

            var regularItems = items.Where(i => i.SetName == null).ToList();
            var setGroups = items
                .Where(i => i.SetName != null)
                .GroupBy(i => i.SetName!)
                .ToList();

            foreach (var item in regularItems)
            {
                table.Cell().Element(DataCell).Text(item.ProductCode);
                RenderDescriptionCell(table.Cell().Element(DataCell), item.Name, item.Variant, italic: false);
                table.Cell().Element(CenteredDataCell)
                    .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold();
                table.Cell().Element(CenteredDataCell)
                    .Text(FormatPrice(item.UnitPrice)).FontSize(8);
                table.Cell().Element(CenteredDataCell)
                    .Text(item.StockCount.ToString("0.##"));
            }

            foreach (var group in setGroups)
            {
                // Sub-header spanning all 5 columns
                table.Cell().ColumnSpan(5).Element(SetHeaderCell)
                    .Text($"Sada: {group.Key}").Bold().FontSize(8);

                foreach (var item in group)
                {
                    table.Cell().Element(DataCell).Text(item.ProductCode).Italic();
                    RenderDescriptionCell(table.Cell().Element(DataCell), item.Name, item.Variant, italic: true);
                    table.Cell().Element(CenteredDataCell)
                        .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold().Italic();
                    table.Cell().Element(CenteredDataCell)
                        .Text(FormatPrice(item.UnitPrice)).FontSize(8).Italic();
                    table.Cell().Element(CenteredDataCell)
                        .Text(item.StockCount.ToString("0.##")).Italic();
                }
            }
        });
    }

    private void BuildSummaryTable(IContainer container, IReadOnlyList<SummaryRow> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(KodCol);
                columns.RelativeColumn(PopisCol);
                columns.RelativeColumn(MnozstviCol);
                columns.RelativeColumn(PoziceCol);
                columns.RelativeColumn(StavCol);
            });

            table.Header(header =>
            {
                header.Cell().Element(SummaryHeaderCell).Text("Kód").Bold();
                header.Cell().Element(SummaryHeaderCell).Text("Popis položky").Bold();
                header.Cell().Element(HeaderCellCenter).Text("Množství").Bold();
                header.Cell().Element(HeaderCellCenter).Text("Pozice").Bold();
                header.Cell().Element(HeaderCellCenter).Text("Stav skladu").Bold();
            });

            foreach (var row in rows)
            {
                if (row.IsFromSet)
                {
                    table.Cell().Element(DataCell).Text(row.ProductCode).Italic();
                    RenderDescriptionCell(table.Cell().Element(DataCell), row.Name, row.Variant, italic: true);
                    table.Cell().Element(CenteredDataCell)
                        .Text(FormatAmount(row.TotalQuantity, row.Unit)).FontSize(11).Bold().Italic();
                    table.Cell().Element(CenteredDataCell)
                        .Text(row.WarehousePosition ?? string.Empty).FontSize(8).Italic();
                    table.Cell().Element(CenteredDataCell)
                        .Text(row.StockCount.ToString("0.##")).Italic();
                }
                else
                {
                    table.Cell().Element(DataCell).Text(row.ProductCode);
                    RenderDescriptionCell(table.Cell().Element(DataCell), row.Name, row.Variant, italic: false);
                    table.Cell().Element(CenteredDataCell)
                        .Text(FormatAmount(row.TotalQuantity, row.Unit)).FontSize(11).Bold();
                    table.Cell().Element(CenteredDataCell)
                        .Text(row.WarehousePosition ?? string.Empty).FontSize(8);
                    table.Cell().Element(CenteredDataCell)
                        .Text(row.StockCount.ToString("0.##"));
                }
            }
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

    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

    private static string FormatPrice(decimal price) =>
        price == 0m ? string.Empty : $"{price.ToString("N0", CzechCulture)} Kč";

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

    private static byte[] GenerateFrostIcon()
    {
        const int size = 64;
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = 4f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };

        var cx = size / 2f;
        var cy = size / 2f;
        var spokeLen = size * 0.42f;
        var branchLen = size * 0.18f;
        var branchOffset = size * 0.20f;

        for (var i = 0; i < 6; i++)
        {
            var angle = i * 60.0 * Math.PI / 180.0;
            var ex = cx + (float)(Math.Cos(angle) * spokeLen);
            var ey = cy + (float)(Math.Sin(angle) * spokeLen);
            canvas.DrawLine(cx, cy, ex, ey, paint);

            // Two branch pairs along the spoke
            foreach (var offsetFraction in new[] { branchOffset, spokeLen - branchLen })
            {
                var bx = cx + (float)(Math.Cos(angle) * offsetFraction);
                var by = cy + (float)(Math.Sin(angle) * offsetFraction);
                var leftAngle = angle + 60.0 * Math.PI / 180.0;
                var rightAngle = angle - 60.0 * Math.PI / 180.0;
                canvas.DrawLine(bx, by, bx + (float)(Math.Cos(leftAngle) * branchLen), by + (float)(Math.Sin(leftAngle) * branchLen), paint);
                canvas.DrawLine(bx, by, bx + (float)(Math.Cos(rightAngle) * branchLen), by + (float)(Math.Sin(rightAngle) * branchLen), paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] GenerateGiftIcon()
    {
        const int size = 64;
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = 4f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };

        float cx = size / 2f;

        // Box body
        canvas.DrawRect(6, 30, 52, 28, paint);
        // Lid
        canvas.DrawRect(4, 20, 56, 12, paint);
        // Ribbon — vertical through center
        canvas.DrawLine(cx, 20, cx, 58, paint);
        // Ribbon — horizontal on lid
        canvas.DrawLine(4, 26, 60, 26, paint);
        // Bow — left loop
        canvas.DrawLine(cx, 20, cx - 14, 6, paint);
        canvas.DrawLine(cx - 14, 6, cx - 2, 16, paint);
        // Bow — right loop
        canvas.DrawLine(cx, 20, cx + 14, 6, paint);
        canvas.DrawLine(cx + 14, 6, cx + 2, 16, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Cell style helpers — shared by per-order and summary tables.
    private static IContainer HeaderCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Background(Colors.Grey.Lighten3)
         .Padding(2);

    private static IContainer HeaderCellCenter(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Background(Colors.Grey.Lighten3)
         .Padding(2).AlignCenter();

    private static IContainer SummaryHeaderCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Background(Colors.Grey.Lighten3)
         .Padding(3);

    private static IContainer SetHeaderCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Background(Colors.Grey.Lighten2)
         .Padding(2);

    private static void RenderDescriptionCell(
        IContainer cell,
        string name,
        string? variant,
        bool italic)
    {
        var formattedVariant = FormatVariant(variant);

        cell.Text(text =>
        {
            var nameSpan = text.Span(name);
            if (italic) nameSpan.Italic();

            if (!string.IsNullOrEmpty(formattedVariant))
            {
                text.Line(string.Empty); // forces line break before variant
                var variantSpan = text.Span(formattedVariant);
                if (italic) variantSpan.Italic();
            }
        });
    }

    private static IContainer DataCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2);

    private static IContainer CenteredDataCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Padding(2).AlignCenter().AlignMiddle();

    private sealed class SummaryRow
    {
        public string ProductCode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Variant { get; init; }
        public string? WarehousePosition { get; init; }
        public string? Unit { get; init; }
        public int TotalQuantity { get; init; }
        public decimal StockCount { get; init; }
        public bool IsFromSet { get; init; }
    }
}
