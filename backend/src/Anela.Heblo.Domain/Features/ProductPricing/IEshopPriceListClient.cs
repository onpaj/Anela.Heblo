namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Read and write the e-shop's default (retail) price list.</summary>
public interface IEshopPriceListClient
{
    /// <summary>Current prices including VAT, keyed by product code.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetPricesWithVatAsync(CancellationToken ct);

    Task SetPriceWithVatAsync(string productCode, decimal priceWithVat, CancellationToken ct);
}
