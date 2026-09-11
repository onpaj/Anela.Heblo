using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class GetProductPricesHandlerTests
{
    [Fact]
    public async Task returns_each_price_with_its_per_target_sync_status()
    {
        // Arrange
        var repository = new Mock<IProductPriceRepository>();
        repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = "A", PriceWithVat = 190.00m, VatRate = 21m },
            });
        repository
            .Setup(r => r.GetSyncStatesAsync(PriceSyncTarget.Shoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>
            {
                new() { ProductCode = "A", Target = PriceSyncTarget.Shoptet, Status = PriceSyncStatus.InSync },
            });
        repository
            .Setup(r => r.GetSyncStatesAsync(PriceSyncTarget.Flexi, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>
            {
                new()
                {
                    ProductCode = "A", Target = PriceSyncTarget.Flexi,
                    Status = PriceSyncStatus.Conflict, RemoteValueAtConflict = 175.00m,
                },
            });
        var catalog = new Mock<ICatalogRepository>();
        catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>
            {
                new() { ProductCode = "A", ProductName = "Olej na obličej" },
            });
        var handler = new GetProductPricesHandler(repository.Object, catalog.Object);

        // Act
        var response = await handler.Handle(new GetProductPricesRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        var price = response.Prices.Should().ContainSingle().Subject;
        price.ProductName.Should().Be("Olej na obličej");
        price.PriceWithoutVat.Should().Be(157.02m);
        price.ShoptetStatus.Should().Be(PriceSyncStatus.InSync);
        price.FlexiStatus.Should().Be(PriceSyncStatus.Conflict);
        price.FlexiRemoteValue.Should().Be(175.00m);
    }
}
