using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class GetPackagesHandlerTests
{
    private static Package MakePackage(int id, string orderCode = "ORD1", string customer = "Alice",
        string packageNumber = "PKG-1", DateTimeOffset? packedAt = null, string providerCode = "PPL")
        => new()
        {
            Id = id,
            OrderCode = orderCode,
            CustomerName = customer,
            PackageNumber = packageNumber,
            ShippingProviderCode = providerCode,
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = packedAt ?? new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static GetPackagesHandler MakeSut(out Mock<IPackageRepository> repo,
        (List<Package> Items, int TotalCount) result)
    {
        repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetPaginatedAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return new GetPackagesHandler(repo.Object);
    }

    [Fact]
    public async Task Handle_MapsItemsAndPagingFields()
    {
        // Arrange
        var packages = new List<Package> { MakePackage(1), MakePackage(2) };
        var sut = MakeSut(out _, (packages, 5));
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
        var sut = MakeSut(out var repo, (new List<Package>(), 0));
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
        await sut.Handle(request, CancellationToken.None);

        // Assert
        repo.Verify(r => r.GetPaginatedAsync(
            "ORD42", "Bob", null, null,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31),
            3, 10,
            "CustomerName", false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenRepositoryReturnsNoItems()
    {
        var sut = MakeSut(out _, (new List<Package>(), 0));
        var response = await sut.Handle(new GetPackagesRequest(), CancellationToken.None);
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }
}
