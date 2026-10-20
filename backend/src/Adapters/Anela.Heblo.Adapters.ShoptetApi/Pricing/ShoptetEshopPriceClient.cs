using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing;

/// <summary>
/// Catalog-facing e-shop price read. Replaces the former CSV product-export client so
/// the catalog and the price sync observe the same source.
/// </summary>
public class ShoptetEshopPriceClient : IProductPriceEshopClient
{
    /// <summary>Action windows are calendar days in the e-shop's own time zone.</summary>
    private static readonly TimeZoneInfo EshopTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    private readonly IEshopPriceListClient _priceListClient;
    private readonly IProductVatRateProvider _vatRateProvider;
    private readonly TimeProvider _timeProvider;

    public ShoptetEshopPriceClient(
        IEshopPriceListClient priceListClient,
        IProductVatRateProvider vatRateProvider,
        TimeProvider timeProvider)
    {
        _priceListClient = priceListClient;
        _vatRateProvider = vatRateProvider;
        _timeProvider = timeProvider;
    }

    public async Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken)
    {
        var entries = await _priceListClient.GetPriceListAsync(cancellationToken);
        var vatRates = await _vatRateProvider.GetVatRatesAsync(cancellationToken);

        // Resolved once per snapshot. The catalog refreshes e-shop prices every 30 minutes,
        // so an action starting or ending at midnight is reflected within that interval.
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), EshopTimeZone).DateTime);

        return entries.Select(entry =>
        {
            var vatRate = vatRates.TryGetValue(entry.ProductCode, out var rate) ? rate : VatRateCalculator.StandardVatRate;

            return ProductPriceEshop.FromPriceList(
                entry.ProductCode,
                entry.PriceWithVat,
                entry.ActionPriceWithVat,
                entry.ActionFrom,
                entry.ActionUntil,
                vatRate,
                today);
        }).ToList();
    }
}
