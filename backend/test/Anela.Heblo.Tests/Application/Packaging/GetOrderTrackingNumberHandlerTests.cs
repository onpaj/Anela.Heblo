using Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.Packaging;

public class GetOrderTrackingNumberHandlerTests
{
    private static (GetOrderTrackingNumberHandler Sut, Mock<IShipmentClient> Client, Mock<IPackageRepository> Repo)
        MakeSut()
    {
        var client = new Mock<IShipmentClient>();
        var repo = new Mock<IPackageRepository>();
        var sut = new GetOrderTrackingNumberHandler(
            client.Object, repo.Object, NullLogger<GetOrderTrackingNumberHandler>.Instance);
        return (sut, client, repo);
    }

    [Fact]
    public async Task Handle_ReturnsAndPersistsTracking_WhenLatestActiveShipmentHasIt()
    {
        var (sut, client, repo) = MakeSut();
        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("126000034", It.IsAny<CancellationToken>()))
            .ReturnsAsync("2421907688");

        var response = await sut.Handle(new GetOrderTrackingNumberRequest { OrderCode = "126000034" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumber.Should().Be("2421907688");
        repo.Verify(r => r.SetTrackingNumberByOrderCodeAsync("126000034", "2421907688", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNullAndDoesNotPersist_WhenNoActiveTrackingYet()
    {
        var (sut, client, repo) = MakeSut();
        client.Setup(c => c.GetLatestActiveTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var response = await sut.Handle(new GetOrderTrackingNumberRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumber.Should().BeNull();
        repo.Verify(r => r.SetTrackingNumberByOrderCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNullAndDoesNotThrow_WhenShoptetThrows()
    {
        var (sut, client, repo) = MakeSut();
        client.Setup(c => c.GetLatestActiveTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));

        var response = await sut.Handle(new GetOrderTrackingNumberRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumber.Should().BeNull();
        repo.Verify(r => r.SetTrackingNumberByOrderCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
