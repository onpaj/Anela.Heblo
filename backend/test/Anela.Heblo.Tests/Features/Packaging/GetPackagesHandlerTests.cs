using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class GetPackagesHandlerTests
{
    private static Package MakePackage(int id, string orderCode = "ORD1", string customer = "Alice",
        string packageNumber = "PKG-1", DateTimeOffset? packedAt = null, string providerCode = "6",
        string? providerName = null)
        => new()
        {
            Id = id,
            OrderCode = orderCode,
            CustomerName = customer,
            PackageNumber = packageNumber,
            ShippingProviderCode = providerCode,
            ShippingProviderName = providerName,
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = packedAt ?? new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static GetPackagesHandler MakeSut(out Mock<IPackageRepository> repo,
        out Mock<IShippingMethodCatalog> catalog,
        (List<Package> Items, int TotalCount) result)
    {
        repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetPaginatedAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        catalog = new Mock<IShippingMethodCatalog>();
        return new GetPackagesHandler(repo.Object, catalog.Object);
    }

    [Fact]
    public async Task Handle_MapsItemsAndPagingFields()
    {
        // Arrange
        var packages = new List<Package> { MakePackage(1), MakePackage(2) };
        var sut = MakeSut(out _, out _, (packages, 5));
        var request = new GetPackagesRequest { PageNumber = 1, PageSize = 2 };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(2);
        response.Items[0].Id.Should().Be(1);
        response.TotalCount.Should().Be(5);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ForwardsFiltersAndSortToRepository()
    {
        // Arrange
        var sut = MakeSut(out var repo, out _, (new List<Package>(), 0));
        var request = new GetPackagesRequest
        {
            OrderCode = "ORD42",
            CustomerName = "Bob",
            FromDate = new DateTime(2026, 5, 1),
            ToDate = new DateTime(2026, 5, 31),
            SortBy = "CustomerName",
            SortDescending = false,
            PageNumber = 3,
            PageSize = 10,
        };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert — no carrier filter => null codes list
        repo.Verify(r => r.GetPaginatedAsync(
            "ORD42", "Bob", null, (IReadOnlyList<string>?)null,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31),
            3, 10,
            "CustomerName", false,
            It.IsAny<CancellationToken>()), Times.Once);
        response.Success.Should().BeTrue();
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ResolvesCarrierToShippingCodes_AndForwardsToRepository()
    {
        // Arrange
        var sut = MakeSut(out var repo, out var catalog, (new List<Package>(), 0));
        var codes = new[] { "6", "80" };
        catalog.Setup(c => c.GetShippingCodesForCarrier(Carriers.PPL)).Returns(codes);
        var request = new GetPackagesRequest { Carrier = Carriers.PPL };

        // Act
        await sut.Handle(request, CancellationToken.None);

        // Assert
        catalog.Verify(c => c.GetShippingCodesForCarrier(Carriers.PPL), Times.Once);
        repo.Verify(r => r.GetPaginatedAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            codes,
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PopulatesShippingProviderName_FromResolvedCarrier()
    {
        // Arrange — stored code "6" resolves to PPL
        var sut = MakeSut(out _, out var catalog, (new List<Package> { MakePackage(1, providerCode: "6") }, 1));
        catalog.Setup(c => c.ResolveCarrier("6")).Returns(Carriers.PPL);
        var request = new GetPackagesRequest { PageNumber = 1, PageSize = 20 };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Items[0].ShippingProviderCode.Should().Be("6");
        response.Items[0].ShippingProviderName.Should().Be("PPL");
    }

    [Fact]
    public async Task Handle_FallsBackToStoredProviderName_WhenCarrierUnresolved()
    {
        // Arrange — unknown code, catalog returns null; stored name should be preserved
        var sut = MakeSut(out _, out _, (new List<Package> { MakePackage(1, providerCode: "999", providerName: "Legacy PPL") }, 1));

        // Act
        var response = await sut.Handle(new GetPackagesRequest(), CancellationToken.None);

        // Assert
        response.Items[0].ShippingProviderName.Should().Be("Legacy PPL");
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenRepositoryReturnsNoItems()
    {
        var sut = MakeSut(out _, out _, (new List<Package>(), 0));
        var response = await sut.Handle(new GetPackagesRequest(), CancellationToken.None);
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }
}
