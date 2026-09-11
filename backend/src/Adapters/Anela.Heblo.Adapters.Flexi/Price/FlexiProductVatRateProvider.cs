using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class FlexiProductVatRateProvider : IProductVatRateProvider
{
    private readonly IProductPriceErpClient _erpClient;

    public FlexiProductVatRateProvider(IProductPriceErpClient erpClient)
    {
        _erpClient = erpClient;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetVatRatesAsync(CancellationToken ct)
    {
        var prices = await _erpClient.GetAllAsync(forceReload: false, ct);

        return prices
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode) && p.PriceWithoutVat > 0)
            .GroupBy(p => p.ProductCode)
            .ToDictionary(
                g => g.Key,
                g => VatRateCalculator.FromPrices(g.First().PriceWithVat, g.First().PriceWithoutVat),
                StringComparer.OrdinalIgnoreCase);
    }
}
