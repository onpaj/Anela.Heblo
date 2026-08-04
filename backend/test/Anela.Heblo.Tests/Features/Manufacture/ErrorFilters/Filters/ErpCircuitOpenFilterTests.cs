using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ErpCircuitOpenFilterTests
{
    private readonly ErpCircuitOpenFilter _filter = new();

    [Fact]
    public void CanHandle_WhenExceptionIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenExceptionIsManufactureErpUnavailable_ReturnsTrue()
    {
        var ex = new ManufactureErpUnavailableException("SubmitManufacture", new Exception("circuit open"));

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void Transform_ReturnsCzechUnavailableMessage()
    {
        var ex = new ManufactureErpUnavailableException("SubmitManufacture", new Exception("circuit open"));

        var result = _filter.Transform(ex);

        result.Should().Contain("FlexiBee");
        result.Should().Contain("nedostupný");
    }
}
