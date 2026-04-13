using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufactureProtocolHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<IManufactureClient> _flexiMock = new();
    private readonly Mock<IManufactureProtocolRenderer> _rendererMock = new();
    private readonly GetManufactureProtocolHandler _handler;

    private static readonly byte[] PdfMagicBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

    public GetManufactureProtocolHandlerTests()
    {
        _handler = new GetManufactureProtocolHandler(
            _repositoryMock.Object,
            _flexiMock.Object,
            _rendererMock.Object);
    }

    [Fact]
    public async Task Handle_NonCompletedOrder_Throws()
    {
        var order = new ManufactureOrder { Id = 1, State = ManufactureOrderState.Planned, OrderNumber = "MO-2026-001" };
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        Func<Task> act = () => _handler.Handle(new GetManufactureProtocolRequest { Id = 1 }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*completed*");
    }

    [Fact]
    public async Task Handle_OrderNotFound_Throws()
    {
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        Func<Task> act = () => _handler.Handle(new GetManufactureProtocolRequest { Id = 999 }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_CompletedOrder_ReturnsPdfWithCorrectFileName()
    {
        var order = BuildCompletedOrder();
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _flexiMock
            .Setup(x => x.GetErpDocumentItemsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureErpDocumentItem>());

        _rendererMock
            .Setup(r => r.Render(It.IsAny<ManufactureProtocolData>()))
            .Returns(PdfMagicBytes);

        var result = await _handler.Handle(new GetManufactureProtocolRequest { Id = 1 }, CancellationToken.None);

        result.PdfBytes.Should().StartWith(PdfMagicBytes);
        result.FileName.Should().Be("ManufactureProtocol-MO-2026-001.pdf");
    }

    [Fact]
    public async Task Handle_CompletedOrder_FetchesItemsForEachPopulatedFlexiDoc()
    {
        var order = BuildCompletedOrder();
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _flexiMock
            .Setup(x => x.GetErpDocumentItemsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureErpDocumentItem>
            {
                new() { ProductCode = "CHEM-001", Amount = 5.0, LotNumber = "L001", ExpirationDate = new DateOnly(2027, 1, 1) }
            });

        _rendererMock
            .Setup(r => r.Render(It.IsAny<ManufactureProtocolData>()))
            .Returns(PdfMagicBytes);

        await _handler.Handle(new GetManufactureProtocolRequest { Id = 1 }, CancellationToken.None);

        // Order has 4 non-null FlexiDoc codes (MaterialIssueForProduct is null)
        _flexiMock.Verify(
            x => x.GetErpDocumentItemsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public async Task Handle_CompletedOrder_BuildsErpDocumentsWithCorrectLabels()
    {
        var order = BuildCompletedOrder();
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _flexiMock
            .Setup(x => x.GetErpDocumentItemsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureErpDocumentItem>());

        ManufactureProtocolData? capturedData = null;
        _rendererMock
            .Setup(r => r.Render(It.IsAny<ManufactureProtocolData>()))
            .Callback<ManufactureProtocolData>(d => capturedData = d)
            .Returns(PdfMagicBytes);

        await _handler.Handle(new GetManufactureProtocolRequest { Id = 1 }, CancellationToken.None);

        capturedData.Should().NotBeNull();
        capturedData!.ErpDocuments.Should().HaveCount(4);
        capturedData.ErpDocuments.Select(d => d.DocumentCode)
            .Should().Contain(new[] { "V-MAT-001", "V-POL-001", "V-POL-OUT-001", "V-PROD-001" });
    }

    [Fact]
    public async Task Handle_CompletedOrder_MapsOrderDataToProtocolData()
    {
        var order = BuildCompletedOrder();
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _flexiMock
            .Setup(x => x.GetErpDocumentItemsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureErpDocumentItem>());

        ManufactureProtocolData? capturedData = null;
        _rendererMock
            .Setup(r => r.Render(It.IsAny<ManufactureProtocolData>()))
            .Callback<ManufactureProtocolData>(d => capturedData = d)
            .Returns(PdfMagicBytes);

        await _handler.Handle(new GetManufactureProtocolRequest { Id = 1 }, CancellationToken.None);

        capturedData.Should().NotBeNull();
        capturedData!.OrderNumber.Should().Be("MO-2026-001");
        capturedData.ResponsiblePerson.Should().Be("Jana Nováková");
        capturedData.SemiProduct.Should().NotBeNull();
        capturedData.SemiProduct!.ProductCode.Should().Be("POL-001");
        capturedData.SemiProduct.LotNumber.Should().Be("L20260402");
        capturedData.Products.Should().HaveCount(1);
        capturedData.Products[0].ProductCode.Should().Be("PRD-001");
        capturedData.Notes.Should().HaveCount(1);
        capturedData.Notes[0].Text.Should().Be("Test note");
    }

    private static ManufactureOrder BuildCompletedOrder()
    {
        return new ManufactureOrder
        {
            Id = 1,
            OrderNumber = "MO-2026-001",
            State = ManufactureOrderState.Completed,
            CreatedDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            StateChangedAt = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            ResponsiblePerson = "Jana Nováková",
            ManufactureType = ManufactureType.MultiPhase,
            SemiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = "POL-001",
                ProductName = "Polotovar A",
                PlannedQuantity = 10,
                ActualQuantity = 9.8m,
                LotNumber = "L20260402",
                ExpirationDate = new DateOnly(2027, 4, 2),
            },
            Products = new List<ManufactureOrderProduct>
            {
                new()
                {
                    ProductCode = "PRD-001",
                    ProductName = "Krém",
                    PlannedQuantity = 50,
                    ActualQuantity = 48,
                    LotNumber = "L20260402A",
                    ExpirationDate = new DateOnly(2027, 4, 2),
                }
            },
            Notes = new List<ManufactureOrderNote>
            {
                new() { CreatedAt = DateTime.UtcNow, CreatedByUser = "Test User", Text = "Test note" }
            },
            DocMaterialIssueForSemiProduct = "V-MAT-001",
            DocMaterialIssueForSemiProductDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            DocSemiProductReceipt = "V-POL-001",
            DocSemiProductReceiptDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            DocSemiProductIssueForProduct = "V-POL-OUT-001",
            DocSemiProductIssueForProductDate = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            DocMaterialIssueForProduct = null, // Optional — not present in this order
            DocProductReceipt = "V-PROD-001",
            DocProductReceiptDate = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
        };
    }
}
