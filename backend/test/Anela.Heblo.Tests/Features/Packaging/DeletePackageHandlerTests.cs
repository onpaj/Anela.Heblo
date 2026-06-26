using Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class DeletePackageHandlerTests
{
    private static (DeletePackageHandler Sut, Mock<IPackageRepository> Repo, Mock<IShipmentClient> Client)
        MakeSut(Package? loaded)
    {
        var repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        var client = new Mock<IShipmentClient>();
        var logger = NullLogger<DeletePackageHandler>.Instance;
        return (new DeletePackageHandler(repo.Object, client.Object, logger), repo, client);
    }

    private static Package SamplePackage(int id = 7) => new()
    {
        Id = id,
        OrderCode = "ORD-1",
        CustomerName = "Alice",
        PackageNumber = "PKG-1",
        ShippingProviderCode = "PPL",
        ShipmentGuid = Guid.NewGuid(),
        PackedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenPackageMissing()
    {
        var (sut, repo, client) = MakeSut(loaded: null);

        var response = await sut.Handle(new DeletePackageRequest { Id = 999 }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PackageNotFound);
        response.Deleted.Should().BeFalse();
        client.Verify(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.DeleteAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallsCancelShipment_AndDeletesRow_OnSuccess()
    {
        var package = SamplePackage();
        var (sut, repo, client) = MakeSut(loaded: package);

        var response = await sut.Handle(new DeletePackageRequest { Id = package.Id }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Deleted.Should().BeTrue();
        client.Verify(c => c.CancelShipmentAsync(package.ShipmentGuid, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.DeleteAsync(package, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_StillDeletesRow_WhenShoptetCancelThrows()
    {
        var package = SamplePackage();
        var (sut, repo, client) = MakeSut(loaded: package);
        client.Setup(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("shoptet 500"));

        var response = await sut.Handle(new DeletePackageRequest { Id = package.Id }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Deleted.Should().BeTrue();
        repo.Verify(r => r.DeleteAsync(package, It.IsAny<CancellationToken>()), Times.Once);
    }
}
