using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityEshopStockSourceAdapter : IDqtEshopStockSource
{
    private readonly IEshopStockClient _inner;
    private readonly CatalogCacheStore _cacheStore;

    public DataQualityEshopStockSourceAdapter(IEshopStockClient inner, CatalogCacheStore cacheStore)
    {
        _inner = inner;
        _cacheStore = cacheStore;
    }

    public async Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken)
    {
        // Codes come from the live supplies list. Names are display-only and come from the hourly
        // product cache; before the first sweep after boot the cache is empty and names stay blank.
        var supplies = await _inner.ListAsync(cancellationToken);
        var namesByCode = _cacheStore.GetEshopProductData()
            .GroupBy(p => p.Code)
            .ToDictionary(g => g.Key, g => FormatName(g.First().Name, g.First().VariantName));

        return supplies
            .Select(s => new DqtEshopStockItem
            {
                Code = s.Code,
                Name = namesByCode.TryGetValue(s.Code, out var name) ? name : string.Empty,
            })
            .ToList();
    }

    private static string FormatName(string name, string? variantName) =>
        string.IsNullOrWhiteSpace(variantName) ? name : $"{name} {variantName}";
}
