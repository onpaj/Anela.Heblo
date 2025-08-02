using Anela.Heblo.Application.Features.Catalog;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class CatalogRefreshBackgroundServiceTests
{
    [Fact]
    public void CatalogRepositoryOptions_HasCorrectPriceRefreshIntervals()
    {
        // Arrange
        var options = new CatalogRepositoryOptions();

        // Assert - Verify default intervals for price refresh
        Assert.Equal(TimeSpan.FromMinutes(30), options.EshopPricesRefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(60), options.ErpPricesRefreshInterval);
    }

    [Fact]
    public void CatalogRepositoryOptions_AllowsCustomPriceRefreshIntervals()
    {
        // Arrange
        var options = new CatalogRepositoryOptions
        {
            EshopPricesRefreshInterval = TimeSpan.FromMinutes(15),
            ErpPricesRefreshInterval = TimeSpan.FromMinutes(120)
        };

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(15), options.EshopPricesRefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(120), options.ErpPricesRefreshInterval);
    }
}