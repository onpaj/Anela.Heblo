using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ZeroQuantityItemFilterTests
{
    private readonly ZeroQuantityItemFilter _filter = new();

    [Fact]
    public void CanHandle_WhenArgumentExceptionWithZeroQuantityMessage_ReturnsTrue()
    {
        var ex = new ArgumentException("Item quantity must be greater than zero (Parameter 'item')", "item");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenInvalidOperationExceptionWithSameMessage_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Item quantity must be greater than zero");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenArgumentExceptionWithDifferentMessage_ReturnsFalse()
    {
        var ex = new ArgumentException("Some other argument error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new ArgumentException("Item quantity must be greater than zero (Parameter 'item')", "item");

        var result = _filter.Transform(ex);

        result.Should().Be("Položka výrobní zakázky má nulové množství.");
    }
}
