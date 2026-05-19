using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class GetOrderShipmentLabelsHandlerTests
{
    private readonly Mock<IShipmentClient> _clientMock = new();

    private GetOrderShipmentLabelsHandler CreateHandler() =>
        new(_clientMock.Object, NullLogger<GetOrderShipmentLabelsHandler>.Instance);

    [Fact]
    public async Task Handle_OrderWithSinglePackage_ReturnsLabelDto()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel
                {
                    ShipmentGuid = shipmentGuid,
                    OrderCode = "0001234",
                    PackageName = "Zásilka 1",
                    LabelUrl = "https://example.com/label.pdf",
                    LabelZpl = "^XA^XZ",
                    TrackingNumber = "TRK001",
                    TrackingUrl = "https://carrier.cz/TRK001",
                }
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0001234" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Labels.Should().HaveCount(1);
        var dto = response.Labels[0];
        dto.ShipmentGuid.Should().Be(shipmentGuid);
        dto.PackageName.Should().Be("Zásilka 1");
        dto.LabelUrl.Should().Be("https://example.com/label.pdf");
        dto.LabelZpl.Should().Be("^XA^XZ");
        dto.HasPdf.Should().BeTrue();
        dto.HasZpl.Should().BeTrue();
        dto.TrackingNumber.Should().Be("TRK001");
        dto.TrackingUrl.Should().Be("https://carrier.cz/TRK001");
    }

    [Fact]
    public async Task Handle_OrderWithMultiplePackages_ReturnsAllLabels()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0002345", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0002345", PackageName = "P1", LabelUrl = "https://x.com/1.pdf" },
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0002345", PackageName = "P2", LabelZpl = "^XA^XZ" },
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0002345" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Labels.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_OrderWithLabelUrlOnlyPackage_HasPdfTrueHasZplFalse()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0003456", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0003456", PackageName = "P1", LabelUrl = "https://x.com/1.pdf", LabelZpl = null }
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0003456" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Labels[0].HasPdf.Should().BeTrue();
        response.Labels[0].HasZpl.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoShipmentForOrder_ReturnsNoShipmentFoundError()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0001111", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0001111" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelsNoShipmentFound);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("0001111");
    }

    [Fact]
    public async Task Handle_AllPackagesHaveNullLabels_ReturnsNotGeneratedError()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0002222", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0002222", PackageName = "P1", LabelUrl = null, LabelZpl = null }
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0002222" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelsNotGenerated);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("0002222");
    }

    [Fact]
    public async Task Handle_ShipmentClientThrows_ReturnsInternalServerError()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0003333", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0003333" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_MixedLabels_SomeNullSomePrintable_ReturnsAllPackages()
    {
        // Arrange — one printable package (has LabelUrl) + one non-printable (both null)
        // Per spec: all packages are returned; the kiosk decides what to print.
        var guid = Guid.NewGuid();
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0004444", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = guid, OrderCode = "0004444", PackageName = "P1", LabelUrl = "https://x.com/1.pdf", LabelZpl = null },
                new ShipmentLabel { ShipmentGuid = guid, OrderCode = "0004444", PackageName = "P2", LabelUrl = null, LabelZpl = null },
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0004444" },
            CancellationToken.None);

        // Assert — success because at least one package has a printable label; all packages returned
        response.Success.Should().BeTrue();
        response.Labels.Should().HaveCount(2);
        response.Labels[0].HasPdf.Should().BeTrue();
        response.Labels[1].HasPdf.Should().BeFalse();
        response.Labels[1].HasZpl.Should().BeFalse();
    }
}
