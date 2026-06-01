using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class NegativeStockFilterTests
{
    private readonly NegativeStockFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsNegativeStockKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Není povolen záporný stav skladu.");

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
            "Failed to create consumption stock movement for warehouse 20: Není povolen záporný stav skladu.");

        var result = _filter.Transform(ex);

        result.Should().Be("Operace by způsobila záporný stav skladu.");
    }
}
