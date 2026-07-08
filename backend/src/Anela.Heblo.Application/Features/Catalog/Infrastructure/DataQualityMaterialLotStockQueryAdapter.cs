using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityMaterialLotStockQueryAdapter : IMaterialLotStockQuery
{
    private readonly ICatalogRepository _catalogRepository;

    public DataQualityMaterialLotStockQueryAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<IReadOnlyList<MaterialLotStockSnapshot>> GetMaterialsWithExpirationAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await _catalogRepository.GetAllAsync(cancellationToken);

        var snapshots = new List<MaterialLotStockSnapshot>();
        foreach (var item in items)
        {
            if (item.Type != ProductType.Material || !item.HasExpiration)
                continue;

            snapshots.Add(new MaterialLotStockSnapshot
            {
                ProductCode = item.ProductCode,
                ErpStock = item.Stock.Erp,
                LotAmounts = item.Stock.Lots.Select(l => l.Amount).ToList(),
            });
        }

        return snapshots;
    }
}
