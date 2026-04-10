using Anela.Heblo.API.PDFPrints;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

/// <summary>
/// Smoke test that verifies the renderer produces valid PDF bytes.
/// Does not test layout details — just confirms the magic bytes are correct.
/// </summary>
public class ManufactureProtocolRendererSmokeTests
{
    [Fact]
    public void Render_ReturnsValidPdfBytes()
    {
        var renderer = new QuestPdfManufactureProtocolRenderer();
        var data = new ManufactureProtocolData
        {
            OrderNumber = "MO-2024-001",
            GeneratedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            ResponsiblePerson = "test@anela.cz",
            PlannedDate = new DateOnly(2024, 6, 15),
            SemiProduct = new ManufactureProtocolSemiProduct
            {
                ProductCode = "SEMI001",
                ProductName = "Testovací polotovar",
                ActualQuantity = 500m,
                LotNumber = "LOT-001",
                ExpirationDate = new DateOnly(2025, 6, 15),
            },
            Products = new List<ManufactureProtocolProduct>
            {
                new ManufactureProtocolProduct
                {
                    ProductCode = "PROD001",
                    ProductName = "Testovací výrobek",
                    ActualQuantity = 100m,
                },
            },
            ErpDocuments = new List<ManufactureProtocolErpDocument>
            {
                new ManufactureProtocolErpDocument
                {
                    DocumentCode = "VYD001",
                    DocumentLabel = "Výdej materiálu pro polotovar",
                    Items = new List<ManufactureErpDocumentItemDto>
                    {
                        new ManufactureErpDocumentItemDto
                        {
                            ProductCode = "MAT001",
                            ProductName = "Materiál A",
                            Amount = 50.0,
                            Unit = "kg",
                            LotNumber = "LOT-MAT-001",
                            ExpirationDate = new DateOnly(2025, 12, 31),
                        },
                    },
                },
            },
            Notes = new List<ManufactureProtocolNote>
            {
                new ManufactureProtocolNote
                {
                    Text = "Testovací poznámka",
                    CreatedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                    CreatedByUser = "user@anela.cz",
                },
            },
        };

        var bytes = renderer.Render(data);

        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x25); // %
        bytes[1].Should().Be(0x50); // P
        bytes[2].Should().Be(0x44); // D
        bytes[3].Should().Be(0x46); // F
    }

    [Fact]
    public void Render_WithMinimalData_ReturnsValidPdfBytes()
    {
        var renderer = new QuestPdfManufactureProtocolRenderer();
        var data = new ManufactureProtocolData
        {
            OrderNumber = "MO-2024-999",
            GeneratedAt = DateTime.UtcNow,
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
        };

        var bytes = renderer.Render(data);

        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x25); // %
        bytes[1].Should().Be(0x50); // P
        bytes[2].Should().Be(0x44); // D
        bytes[3].Should().Be(0x46); // F
    }
}
