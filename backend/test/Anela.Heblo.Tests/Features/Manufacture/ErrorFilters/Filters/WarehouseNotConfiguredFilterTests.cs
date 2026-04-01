using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class WarehouseNotConfiguredFilterTests
{
    private readonly WarehouseNotConfiguredFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsWarehouseNotFilledKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Pole 'Sklad' musí být vyplněno. [DoklSklad -1]");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Pole 'Sklad' musí být vyplněno. [DoklSklad -1]");

        var result = _filter.Transform(ex);

        result.Should().Be("Skladový pohyb nemá nastaven sklad.");
    }
}
