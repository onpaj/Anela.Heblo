using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.API.PDFPrints;

public class ManufactureProtocolDocument : IDocument
{
    static ManufactureProtocolDocument()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly ManufactureProtocolData _data;

    public ManufactureProtocolDocument(ManufactureProtocolData data)
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
                header.Item().Text(_data.OrderNumber).FontSize(16).Bold();
                header.Item().Text($"Vygenerováno: {_data.GeneratedAt:dd.MM.yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
                header.Item().PaddingTop(4).LineHorizontal(1f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingTop(8).Column(col =>
            {
                // Basic info row
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Odpovědná osoba: ").Bold();
                            t.Span(_data.ResponsiblePerson ?? "—");
                        });
                    });

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Plánované datum: ").Bold();
                            t.Span(_data.PlannedDate.ToString("dd.MM.yyyy"));
                        });
                    });
                });

                col.Item().PaddingTop(10);

                // Semi-product section
                if (_data.SemiProduct != null)
                {
                    col.Item().Text("Polotovar").FontSize(11).Bold();
                    col.Item().PaddingTop(4);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2);   // Kód
                            cols.RelativeColumn(4);   // Název
                            cols.RelativeColumn(2);   // Skutečné množství
                            cols.RelativeColumn(2);   // Šarže
                            cols.RelativeColumn(2);   // Expirace
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Kód").Bold();
                            header.Cell().Element(HeaderCell).Text("Název").Bold();
                            header.Cell().Element(HeaderCell).Text("Skut. množství").Bold();
                            header.Cell().Element(HeaderCell).Text("Šarže").Bold();
                            header.Cell().Element(HeaderCell).Text("Expirace").Bold();
                        });

                        var sp = _data.SemiProduct;
                        table.Cell().Element(DataCell).Text(sp.ProductCode);
                        table.Cell().Element(DataCell).Text(sp.ProductName);
                        table.Cell().Element(DataCell).Text(sp.ActualQuantity?.ToString("0.###") ?? "—");
                        table.Cell().Element(DataCell).Text(sp.LotNumber ?? "—");
                        table.Cell().Element(DataCell).Text(sp.ExpirationDate?.ToString("dd.MM.yyyy") ?? "—");
                    });

                    col.Item().PaddingTop(10);
                }

                // Products table
                col.Item().Text("Výrobky").FontSize(11).Bold();
                col.Item().PaddingTop(4);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);   // Kód
                        cols.RelativeColumn(6);   // Název
                        cols.RelativeColumn(2);   // Skutečné množství
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Kód").Bold();
                        header.Cell().Element(HeaderCell).Text("Název").Bold();
                        header.Cell().Element(HeaderCell).Text("Skut. množství").Bold();
                    });

                    foreach (var product in _data.Products)
                    {
                        table.Cell().Element(DataCell).Text(product.ProductCode);
                        table.Cell().Element(DataCell).Text(product.ProductName);
                        table.Cell().Element(DataCell).Text(product.ActualQuantity?.ToString("0.###") ?? "—");
                    }
                });

                col.Item().PaddingTop(10);

                // ERP documents section
                if (_data.ErpDocuments.Count > 0)
                {
                    col.Item().Text("ABRA Flexi doklady a spotřeba").FontSize(11).Bold();

                    foreach (var erpDoc in _data.ErpDocuments)
                    {
                        col.Item().PaddingTop(8);
                        col.Item().Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(10));
                            t.Span($"{erpDoc.DocumentCode}").Bold();
                            t.Span($"  –  {erpDoc.DocumentLabel}").FontColor(Colors.Grey.Darken1);
                        });

                        col.Item().PaddingTop(4);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);   // Kód
                                cols.RelativeColumn(4);   // Název
                                cols.RelativeColumn(1.5f); // Množství
                                cols.RelativeColumn(1.5f); // Jednotka
                                cols.RelativeColumn(2);   // Šarže
                                cols.RelativeColumn(2);   // Expirace
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Kód").Bold();
                                header.Cell().Element(HeaderCell).Text("Název").Bold();
                                header.Cell().Element(HeaderCell).Text("Množství").Bold();
                                header.Cell().Element(HeaderCell).Text("Jednotka").Bold();
                                header.Cell().Element(HeaderCell).Text("Šarže").Bold();
                                header.Cell().Element(HeaderCell).Text("Expirace").Bold();
                            });

                            foreach (var item in erpDoc.Items)
                            {
                                table.Cell().Element(DataCell).Text(item.ProductCode);
                                table.Cell().Element(DataCell).Text(item.ProductName);
                                table.Cell().Element(DataCell).Text(item.Amount.ToString("0.###"));
                                table.Cell().Element(DataCell).Text(item.Unit ?? "—");
                                table.Cell().Element(DataCell).Text(item.LotNumber ?? "—");
                                table.Cell().Element(DataCell).Text(item.ExpirationDate?.ToString("dd.MM.yyyy") ?? "—");
                            }
                        });
                    }

                    col.Item().PaddingTop(10);
                }

                // Notes section
                if (_data.Notes.Count > 0)
                {
                    col.Item().Text("Poznámky").FontSize(11).Bold();
                    col.Item().PaddingTop(4);

                    foreach (var note in _data.Notes)
                    {
                        col.Item().PaddingTop(4).Column(noteCol =>
                        {
                            noteCol.Item().Text(t =>
                            {
                                t.Span(note.Text);
                            });
                            noteCol.Item().Text(
                                $"{note.CreatedAt:dd.MM.yyyy HH:mm}  –  {note.CreatedByUser}")
                                .FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    }
                }
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
