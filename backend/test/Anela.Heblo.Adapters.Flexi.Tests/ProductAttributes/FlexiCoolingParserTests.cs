using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
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
}
