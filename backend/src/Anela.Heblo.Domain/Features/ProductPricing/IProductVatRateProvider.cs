namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>VAT rate per product code, sourced from the ERP.</summary>
public interface IProductVatRateProvider
{
    Task<IReadOnlyDictionary<string, decimal>> GetVatRatesAsync(CancellationToken ct);
}
