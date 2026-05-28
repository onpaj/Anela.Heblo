using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerRepository : IRepository<MaterialContainer, int>
{
    Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct);
    Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct);
    Task<PagedResult<MaterialContainer>> GetPaginatedAsync(int? lotId, string? materialCode, int page, int pageSize, CancellationToken ct);
}
