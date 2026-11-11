using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Catalog-side adapter that implements the Manufacture-owned IManufactureCatalogSource
/// contract by delegating to the internal ICatalogRepository. DI registration is in
/// CatalogModule.AddCatalogModule(). Maps CatalogAggregate to the Manufacture-owned
/// ProductCatalogSnapshot via CatalogAggregateSnapshotMapper.
/// </summary>
internal sealed class CatalogManufactureCatalogSourceAdapter : IManufactureCatalogSource
{
    private readonly ICatalogRepository _repository;

    public CatalogManufactureCatalogSourceAdapter(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductCatalogSnapshot?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var aggregate = await _repository.GetByIdAsync(id, cancellationToken);
        return aggregate == null ? null : CatalogAggregateSnapshotMapper.ToSnapshot(aggregate);
    }

    public async Task<IReadOnlyDictionary<string, ProductCatalogSnapshot>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var aggregates = await _repository.GetByIdsAsync(ids, cancellationToken);
        return aggregates.ToDictionary(kv => kv.Key, kv => CatalogAggregateSnapshotMapper.ToSnapshot(kv.Value));
    }

    public async Task<IEnumerable<ProductCatalogSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = await _repository.GetAllAsync(cancellationToken);
        return aggregates.Select(CatalogAggregateSnapshotMapper.ToSnapshot).ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetProductNameSuffixesAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken = default)
    {
        var products = await _repository.GetByIdsAsync(productCodes, cancellationToken);

        return products
            .Where(p => p.Value.ProductNameSuffix != null)
            .ToDictionary(p => p.Key, p => p.Value.ProductNameSuffix!);
    }

    public Task RefreshPlannedDataAsync(CancellationToken cancellationToken = default) =>
        _repository.RefreshPlannedData(cancellationToken);
}
