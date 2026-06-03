using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Catalog-side adapter that implements the Manufacture-owned IManufactureCatalogSource
/// contract by delegating to the internal ICatalogRepository. DI registration is in
/// CatalogModule.AddCatalogModule(). See ModuleBoundariesTests "Manufacture -> Catalog"
/// rule and its ManufactureCatalogAllowlist for the deliberate CatalogAggregate leak.
/// </summary>
internal sealed class CatalogManufactureCatalogSourceAdapter : IManufactureCatalogSource
{
    private readonly ICatalogRepository _repository;

    public CatalogManufactureCatalogSourceAdapter(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default) =>
        _repository.GetByIdsAsync(ids, cancellationToken);

    public Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);
}
