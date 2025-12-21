using Anela.Heblo.Domain.Features.Catalog;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogAggregateMarginTests
{
    [Fact]
    public void CatalogAggregate_Should_Initialize_With_Empty_Margins()
    {
        // Arrange & Act
        var catalog = new CatalogAggregate();

        // Assert
        Assert.NotNull(catalog.Margins);
        Assert.NotNull(catalog.Margins.MonthlyData);
        Assert.Empty(catalog.Margins.MonthlyData);
        Assert.NotNull(catalog.Margins.Averages);
    }

    [Fact]
    public void CatalogAggregate_Should_Allow_Margin_Assignment()
    {
        // Arrange
        var catalog = new CatalogAggregate();
        var margins = new MonthlyMarginHistory
        {
            MonthlyData = new Dictionary<DateTime, MarginData>
            {
                { DateTime.Now.AddMonths(-1), new MarginData() }
            }
        };

        // Act
        catalog.Margins = margins;

        // Assert
        Assert.Equal(margins, catalog.Margins);
        Assert.Single(catalog.Margins.MonthlyData);
    }
}