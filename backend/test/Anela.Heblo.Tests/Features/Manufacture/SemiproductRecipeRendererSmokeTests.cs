using Anela.Heblo.API.PDFPrints;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

/// <summary>
/// Smoke test that verifies the recipe renderer produces valid PDF bytes with and without a barcode.
/// </summary>
public class SemiproductRecipeRendererSmokeTests
{
    private static SemiproductRecipeData BuildData(string? barcodeValue) => new()
    {
        ProductCode = "DEO001001M",
        ProductName = "Deodorant základ s velmi dlouhým názvem, který se nemusí vejít na jeden řádek hlavičky",
        BatchSize = 12000,
        PrintedAt = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
        Mmq = 8000,
        ExpirationMonths = 12,
        BarcodeValue = barcodeValue,
        Ingredients = new List<SemiproductRecipeIngredientLine>
        {
            new() { ProductCode = "SUR001", ProductName = "Bambucké máslo", AmountFullBatch = 6000, AmountHalfBatch = 3000, Percentage = 50, PhaseLabel = "A" },
            new() { ProductCode = "SUR002", ProductName = "Jedlá soda", AmountFullBatch = 6000, AmountHalfBatch = 3000, Percentage = 50, PhaseLabel = "B" },
        },
    };

    [Theory]
    [InlineData("R-DEO001001M")]
    [InlineData("R-ABCDEFGHIJKLMNOPQRSTUVWXYZ12")] // RecipeBarcode.MaxValueLength
    [InlineData(null)]
    public void Render_ReturnsValidPdfBytes(string? barcodeValue)
    {
        var renderer = new QuestPdfSemiproductRecipeRenderer();

        var bytes = renderer.Render(BuildData(barcodeValue));

        bytes.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
