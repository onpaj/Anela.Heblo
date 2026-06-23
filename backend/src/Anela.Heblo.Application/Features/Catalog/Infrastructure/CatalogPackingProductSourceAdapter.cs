using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogPackingProductSourceAdapter : IPackingProductSource
{
    private readonly ICatalogRepository _repository;

    public CatalogPackingProductSourceAdapter(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes, CancellationToken ct = default)
    {
        var items = await _repository.GetByIdsAsync(productCodes, ct);
        return items.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var a = kv.Value;
                return new PackingProductInfo
                {
                    Cooling = a.Properties.Cooling,
                    WeightGrams = a.GrossWeight.HasValue ? (int?)((int)a.GrossWeight.Value)
                                : a.NetWeight.HasValue ? (int?)((int)a.NetWeight.Value)
                                : null,
                    ImageUrl = a.Image,
                };
            });
    }
}
