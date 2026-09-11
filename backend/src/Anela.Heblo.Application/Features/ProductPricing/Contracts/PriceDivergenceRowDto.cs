namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

/// <summary>One in-scope product's price comparison across Shoptet and Flexi.</summary>
public class PriceDivergenceRowDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Null when the product is absent from the Shoptet retail price list.</summary>
    public decimal? ShoptetPriceWithVat { get; set; }

    /// <summary>Null when the product is absent from the Flexi ERP price read.</summary>
    public decimal? FlexiPriceWithVat { get; set; }
    public decimal? FlexiPriceWithoutVat { get; set; }

    /// <summary>"bezDph", "sDph", or null when Flexi did not expose the price type.</summary>
    public string? FlexiPriceType { get; set; }

    /// <summary>Flexi minus Shoptet, rounded to 2 decimals. Null when either side is missing.</summary>
    public decimal? DifferenceWithVat { get; set; }

    /// <summary>DifferenceWithVat relative to the Shoptet price, in percent, rounded to 2 decimals.
    /// Null when either side is missing or the Shoptet price is zero (undefined ratio).</summary>
    public decimal? DifferencePercent { get; set; }

    public PriceDivergenceKind Kind { get; set; }
}
