using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Flexi.Tests.ProductAttributes;

public class FlexiCoolingParserTests
{
    [Fact]
    public void CatalogAttributes_HasCoolingProperty_DefaultsToNone()
    {
        var attrs = new CatalogAttributes();
        attrs.Cooling.Should().Be(Cooling.None);
    }

    [Fact]
    public void CatalogProperties_HasCoolingProperty_DefaultsToNone()
    {
        var props = new CatalogProperties();
        props.Cooling.Should().Be(Cooling.None);
    }

    [Theory]
    [InlineData("L1", Cooling.L1)]
    [InlineData("L2", Cooling.L2)]
    [InlineData("l1", Cooling.L1)]
    [InlineData("l2", Cooling.L2)]
    [InlineData(" L2 ", Cooling.L2)]
    [InlineData(" l1 ", Cooling.L1)]
    [InlineData("NONE", Cooling.None)]
    public void ParseCooling_WithValidValues_ReturnsParsedEnum(string input, Cooling expected)
    {
        var result = FlexiProductAttributesQueryClient.ParseCooling(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("L3")]
    [InlineData("garbage")]
    public void ParseCooling_WithInvalidOrMissingValues_ReturnsNone(string? input)
    {
        var result = FlexiProductAttributesQueryClient.ParseCooling(input);
        result.Should().Be(Cooling.None);
    }
}
