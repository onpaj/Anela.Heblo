using Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumbers;
using Anela.Heblo.Application.Features.ShipmentLabels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.Packaging;

public class GetOrderTrackingNumbersHandlerTests
{
    private static (GetOrderTrackingNumbersHandler Sut, Mock<IShipmentClient> Client) MakeSut()
    {
        var client = new Mock<IShipmentClient>();
        var sut = new GetOrderTrackingNumbersHandler(
            client.Object, NullLogger<GetOrderTrackingNumbersHandler>.Instance);
        return (sut, client);
    }

    private static ShipmentLabel Label(Guid shipmentGuid, string? trackingNumber) =>
        new() { ShipmentGuid = shipmentGuid, OrderCode = "ORD-1", PackageName = "P", TrackingNumber = trackingNumber };

    [Fact]
    public async Task Handle_ReturnsPerPackageTrackingNumbers_OfLatestActiveShipment()
    {
        var (sut, client) = MakeSut();
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        // GetLabelsByOrderCodeAsync returns oldest-shipment-first; the latest shipment is the new one.
        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShipmentLabel>
            {
                Label(oldGuid, "OLD-TRK"),
                Label(newGuid, "TRK-1"),
                Label(newGuid, "TRK-2"),
            });

        var response = await sut.Handle(new GetOrderTrackingNumbersRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumbers.Should().Equal("TRK-1", "TRK-2");
    }

    [Fact]
    public async Task Handle_SkipsPackagesWithoutTrackingNumber()
    {
        var (sut, client) = MakeSut();
        var guid = Guid.NewGuid();
        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShipmentLabel> { Label(guid, "TRK-1"), Label(guid, null) });

        var response = await sut.Handle(new GetOrderTrackingNumbersRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumbers.Should().Equal("TRK-1");
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoLabels()
    {
        var (sut, client) = MakeSut();
        client.Setup(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await sut.Handle(new GetOrderTrackingNumbersRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumbers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyAndDoesNotThrow_WhenShoptetThrows()
    {
        var (sut, client) = MakeSut();
        client.Setup(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));

        var response = await sut.Handle(new GetOrderTrackingNumbersRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumbers.Should().BeEmpty();
    }
}
