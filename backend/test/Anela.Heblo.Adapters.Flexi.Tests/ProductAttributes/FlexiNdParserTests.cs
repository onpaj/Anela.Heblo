using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Flexi.Tests.ProductAttributes;

public class FlexiNdParserTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    [InlineData("1")]
    [InlineData("ano")]
    [InlineData("ANO")]
    [InlineData("yes")]
    public void ParseIsNd_WithTruthyValues_ReturnsTrue(string input)
    {
        FlexiProductAttributesQueryClient.ParseIsNd(input).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("ne")]
    [InlineData("garbage")]
    public void ParseIsNd_WithFalsyOrMissingValues_ReturnsFalse(string? input)
    {
        FlexiProductAttributesQueryClient.ParseIsNd(input).Should().BeFalse();
    }
}
