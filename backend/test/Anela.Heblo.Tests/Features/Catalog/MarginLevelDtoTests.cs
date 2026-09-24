using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class MarginLevelDtoTests
{
    [Fact]
    public void FromDomain_CopiesAllFourFieldsVerbatim()
    {
        // Arrange
        var level = new MarginLevel(percentage: 12.34m, amount: 56.78m, costTotal: 90.12m, costLevel: 3.45m);

        // Act
        var dto = MarginLevelDto.FromDomain(level);

        // Assert
        dto.Percentage.Should().Be(12.34m);
        dto.Amount.Should().Be(56.78m);
        dto.CostTotal.Should().Be(90.12m);
        dto.CostLevel.Should().Be(3.45m);
    }

    [Fact]
    public void FromDomain_ZeroLevel_ProducesZeroDto()
    {
        // Arrange
        var level = MarginLevel.Zero;

        // Act
        var dto = MarginLevelDto.FromDomain(level);

        // Assert
        dto.Percentage.Should().Be(0m);
        dto.Amount.Should().Be(0m);
        dto.CostTotal.Should().Be(0m);
        dto.CostLevel.Should().Be(0m);
    }
}
