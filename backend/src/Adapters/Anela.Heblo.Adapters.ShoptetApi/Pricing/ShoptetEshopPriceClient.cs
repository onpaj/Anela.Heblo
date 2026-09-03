using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing;

/// <summary>
/// Catalog-facing e-shop price read. Replaces the former CSV product-export client so
/// the catalog and the price sync observe the same source.
/// </summary>
public class ShoptetEshopPriceClient : IProductPriceEshopClient
{
    private readonly IEshopPriceListClient _priceListClient;
    private readonly IProductVatRateProvider _vatRateProvider;

    public ShoptetEshopPriceClient(
        IEshopPriceListClient priceListClient,
        IProductVatRateProvider vatRateProvider)
    {
        _priceListClient = priceListClient;
        _vatRateProvider = vatRateProvider;
    }

    public async Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken)
    {
        var prices = await _priceListClient.GetPricesWithVatAsync(cancellationToken);
        var vatRates = await _vatRateProvider.GetVatRatesAsync(cancellationToken);

        return prices.Select(entry =>
        {
            var vatRate = vatRates.TryGetValue(entry.Key, out var rate) ? rate : VatRateCalculator.StandardVatRate;

            return new ProductPriceEshop
            {
                ProductCode = entry.Key,
                PriceWithVat = entry.Value,
                PriceWithoutVat = Math.Round(entry.Value / (1 + vatRate / 100m), 2, MidpointRounding.AwayFromZero),
                PurchasePrice = null,
            };
        }).ToList();
    }
}
