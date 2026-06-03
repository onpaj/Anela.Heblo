using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsCatalogSourceAdapter : ILogisticsCatalogSource
{
    private readonly ICatalogRepository _catalogRepository;

    public LogisticsCatalogSourceAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(item => item.Type == ProductType.Set)
            .Select(item => ToGiftPackageItem(item, fromUtc, toUtc))
            .ToList();
    }

    public async Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(code, cancellationToken);

        if (aggregate is null || aggregate.Type != ProductType.Set)
            return null;

        return ToGiftPackageItem(aggregate, fromUtc, toUtc);
    }

    public async Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(code, cancellationToken);
        return aggregate is null ? null : ToCatalogItem(aggregate);
    }

    private static LogisticsGiftPackageItem ToGiftPackageItem(
        CatalogAggregate aggregate,
        DateTime fromUtc,
        DateTime toUtc) => new()
        {
            ProductCode = aggregate.ProductCode,
            ProductName = aggregate.ProductName,
            Image = aggregate.Image,
            AvailableStock = aggregate.Stock.Available,
            TotalSoldInPeriod = aggregate.GetTotalSold(fromUtc, toUtc),
            StockMinSetup = (int)aggregate.Properties.StockMinSetup,
            OptimalStockDaysSetup = aggregate.Properties.OptimalStockDaysSetup,
        };

    private static LogisticsCatalogItem ToCatalogItem(CatalogAggregate aggregate) => new()
    {
        ProductCode = aggregate.ProductCode,
        Image = aggregate.Image,
        EshopStock = aggregate.Stock.Eshop,
        AvailableStock = aggregate.Stock.Available,
    };
}
