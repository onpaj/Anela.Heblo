namespace Anela.Heblo.Domain.Features.ProductPricing;

public static class VatRateCalculator
{
    public const decimal StandardVatRate = 21m;

    /// <summary>Recovers the VAT rate from a price pair, falling back to the standard rate.</summary>
    public static decimal FromPrices(decimal priceWithVat, decimal priceWithoutVat)
    {
        if (priceWithoutVat <= 0)
        {
            return StandardVatRate;
        }

        return Math.Round((priceWithVat / priceWithoutVat - 1) * 100m, 0);
    }
}
