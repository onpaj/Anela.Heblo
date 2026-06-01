using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class CannotAllocateFullAmountFilterTests
{
    private readonly CannotAllocateFullAmountFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenMessageMatchesAllocatorFormat_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Cannot allocate full amount for ingredient HYD013: 21361758.000 remaining");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void Transform_ExtractsProductCodeAndRemaining()
    {
        var ex = new InvalidOperationException(
            "Cannot allocate full amount for ingredient HYD013: 21361758.000 remaining");

        var result = _filter.Transform(ex);

        result.Should().Contain("HYD013");
        result.Should().Contain("21361758.000");
        result.Should().Contain("šarže");
    }

    [Fact]
    public void Transform_WhenMessageFormatUnrecognized_ReturnsFallbackMessage()
    {
        var ex = new InvalidOperationException("Cannot allocate full amount for ingredient");

        var result = _filter.Transform(ex);

        result.Should().Contain("ingrediencí");
        result.Should().Contain("šarže");
    }
}
