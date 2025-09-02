using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class CatalogRefreshBackgroundServiceTests
{
    [Fact]
    public void CatalogRepositoryOptions_HasCorrectPriceRefreshIntervals()
    {
        // Arrange
        var options = new DataSourceOptions();

        // Assert - Verify default intervals for price refresh
        options.EshopPricesRefreshInterval.Should().Be(TimeSpan.FromMinutes(30));
        options.ErpPricesRefreshInterval.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void CatalogRepositoryOptions_AllowsCustomPriceRefreshIntervals()
    {
        // Arrange
        var options = new DataSourceOptions
        {
            EshopPricesRefreshInterval = TimeSpan.FromMinutes(15),
            ErpPricesRefreshInterval = TimeSpan.FromMinutes(120)
        };

        // Assert
        options.EshopPricesRefreshInterval.Should().Be(TimeSpan.FromMinutes(15));
        options.ErpPricesRefreshInterval.Should().Be(TimeSpan.FromMinutes(120));
    }
}