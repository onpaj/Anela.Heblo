using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Pricing.Services;

public class PricingBaselineBuilder : IPricingBaselineBuilder
{
    private const int TrailingSalesMonths = 12;

    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;

    public PricingBaselineBuilder(ICatalogRepository catalogRepository, TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PricingBaselineRow>> BuildAsync(PricingFilterDto filter, CancellationToken ct)
    {
        var products = await _catalogRepository.GetAllAsync(ct) ?? Enumerable.Empty<CatalogAggregate>();
        var now = _timeProvider.GetUtcNow().DateTime;
        var salesFrom = now.AddMonths(-TrailingSalesMonths);

        return ApplyFilters(products, filter)
            .OrderBy(p => p.ProductCode)
            .Select(p => ToBaselineRow(p, salesFrom, now))
            .ToList();
    }

    private static IEnumerable<CatalogAggregate> ApplyFilters(
        IEnumerable<CatalogAggregate> products, PricingFilterDto filter)
    {
        // Mirrors GetProductMarginsHandler.ApplyFilters so the two screens agree on what
        // "all products" means.
        if (!string.IsNullOrWhiteSpace(filter.ProductCode))
            products = products.Where(x => x.ProductCode != null &&
                x.ProductCode.Contains(filter.ProductCode, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.ProductName))
            products = products.Where(x => x.ProductName != null &&
                x.ProductName.Contains(filter.ProductName, StringComparison.OrdinalIgnoreCase));

        products = filter.ProductType.HasValue
            ? products.Where(x => x.Type == filter.ProductType.Value)
            : products.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods);

        return products;
    }

    private static PricingBaselineRow ToBaselineRow(CatalogAggregate product, DateTime salesFrom, DateTime now)
    {
        // Latest month, deliberately not Margins.Averages: the premise of this feature is
        // that costs just rose, and a 13-month average would understate today's cost.
        var latest = product.Margins.MonthlyData
            .OrderByDescending(m => m.Key)
            .Select(m => m.Value)
            .FirstOrDefault();

        var price = product.PriceWithoutVat ?? 0m;
        var hasData = product.PriceWithoutVat is > 0m && latest is not null;

        return new PricingBaselineRow(
            ProductCode: product.ProductCode ?? string.Empty,
            ProductName: product.ProductName ?? string.Empty,
            Price: price,
            MaterialCost: latest?.M0.CostLevel ?? 0m,
            ManufacturingCost: latest?.M1.CostLevel ?? 0m,
            Quantity: product.GetTotalSold(salesFrom, now),
            HasData: hasData);
    }
}
