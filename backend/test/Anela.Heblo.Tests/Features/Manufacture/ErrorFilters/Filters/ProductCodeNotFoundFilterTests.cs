using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ProductCodeNotFoundFilterTests
{
    private readonly ProductCodeNotFoundFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsIdentifyObjectKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Zadaný text 'code:MAS001015T' musí identifikovat objekt [VYR51023]");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsProductCode()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Zadaný text 'code:MAS001015T' musí identifikovat objekt [VYR51023]");

        var result = _filter.Transform(ex);

        result.Should().Be("Produkt s kódem 'MAS001015T' nebyl nalezen v systému Flexi.");
    }

    [Fact]
    public void Transform_WhenCodeNotParseable_ReturnsGenericMessage()
    {
        var ex = new InvalidOperationException("musí identifikovat objekt - no code prefix");

        var result = _filter.Transform(ex);

        result.Should().Be("Produkt s kódem 'neznámý' nebyl nalezen v systému Flexi.");
    }
}
