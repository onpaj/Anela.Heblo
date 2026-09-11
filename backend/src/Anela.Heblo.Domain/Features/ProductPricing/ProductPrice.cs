using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// The master retail price for one product. Heblo owns this value; Shoptet and
/// Flexi are downstream copies of it.
/// </summary>
public class ProductPrice : Entity<string>
{
    public string ProductCode
    {
        get => Id;
        set => Id = value;
    }

    /// <summary>Canonical form. This is the number a human types and rounds.</summary>
    public decimal PriceWithVat { get; set; }

    /// <summary>0, 15 or 21. Sourced from Flexi.</summary>
    public decimal VatRate { get; set; }

    public DateTime ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>Derived for Flexi's <c>cenaZakl</c>, which stores prices excluding VAT.</summary>
    public decimal PriceWithoutVat =>
        Math.Round(PriceWithVat / (1 + VatRate / 100m), 2, MidpointRounding.AwayFromZero);
}
