using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance.Services;

public class NoOpMonthlyAdCostSourceTests
{
    private readonly NoOpMonthlyAdCostSource _source = new(NullLogger<NoOpMonthlyAdCostSource>.Instance);

    [Fact]
    public async Task GetAsync_ReturnsEmptyList_ForAnyMonthAndVatIds()
    {
        // Arrange
        var month = new YearMonth(2026, 9);
        var vatIds = new[] { "IE9692928F", "CZ26168685" };

        // Act
        var result = await _source.GetAsync(month, vatIds, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
