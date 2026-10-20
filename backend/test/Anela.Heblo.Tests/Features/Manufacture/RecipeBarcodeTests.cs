using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class RecipeBarcodeTests
{
    [Theory]
    [InlineData("DEO001001M", "R-DEO001001M")]
    [InlineData("  DEO001001M ", "R-DEO001001M")]
    public void For_PrefixesTrimmedProductCode(string productCode, string expected)
    {
        RecipeBarcode.For(productCode).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DEOŽ01")]
    public void For_ReturnsNull_ForMissingOrNonAsciiCode(string? productCode)
    {
        RecipeBarcode.For(productCode).Should().BeNull();
    }

    [Fact]
    public void For_ReturnsNull_WhenValueExceedsMaxLength()
    {
        var tooLong = new string('A', RecipeBarcode.MaxValueLength - RecipeBarcode.Prefix.Length + 1);
        var longest = new string('A', RecipeBarcode.MaxValueLength - RecipeBarcode.Prefix.Length);

        RecipeBarcode.For(tooLong).Should().BeNull();
        RecipeBarcode.For(longest).Should().HaveLength(RecipeBarcode.MaxValueLength);
    }
}
