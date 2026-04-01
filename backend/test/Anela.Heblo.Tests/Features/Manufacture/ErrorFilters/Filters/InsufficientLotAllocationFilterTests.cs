using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotAllocationFilterTests
{
    private readonly InsufficientLotAllocationFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsLotAllocationKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Could not allocate sufficient lots for ingredient 'Demineralizovaná voda' (AKL027). Required: 19087.50, Allocated: 15962.77, Missing: 3124.73");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsIngredientNameAndMissingAmount()
    {
        var ex = new InvalidOperationException(
            "Could not allocate sufficient lots for ingredient 'Demineralizovaná voda' (AKL027). Required: 19087.50, Allocated: 15962.77, Missing: 3124.73");

        var result = _filter.Transform(ex);

        result.Should().Be("Nedostatek šarží pro ingredienci 'Demineralizovaná voda' (chybí: 3124.73).");
    }
}
