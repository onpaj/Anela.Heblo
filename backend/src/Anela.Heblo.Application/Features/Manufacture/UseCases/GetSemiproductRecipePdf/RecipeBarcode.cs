using Anela.Heblo.Application.Shared.Printing;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;

/// <summary>
/// Barcode value printed on the recipe sheet: "R-" + product code (e.g. R-DEO001001M).
/// The prefix tells a recipe scan apart from a product or lot scan.
/// </summary>
public static class RecipeBarcode
{
    public const string Prefix = "R-";

    /// <summary>Longest value that still fits the landscape A4 recipe header beside the product title.</summary>
    public const int MaxValueLength = 30;

    /// <summary>Returns the barcode value, or null when the code is too long or cannot be printed as Code 128.</summary>
    public static string? For(string? productCode)
    {
        var trimmed = productCode?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var value = Prefix + trimmed;
        return value.Length <= MaxValueLength && Code128Encoder.CanEncode(value) ? value : null;
    }
}
