using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerRepository : IRepository<MaterialContainer, int>
{
    Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct);
    Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        string? materialCode, string? lotCode, string? code, int page, int pageSize, CancellationToken ct);
    Task<string?> GetLastUsedLotCodeForMaterialAsync(string materialCode, CancellationToken ct);
}
