using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IEanRepository : IRepository<Ean, int>
{
    Task<Ean?> GetByCodeAsync(string code, CancellationToken ct);
    Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct);
    Task<PagedResult<Ean>> GetPaginatedAsync(int? lotId, string? materialCode, int page, int pageSize, CancellationToken ct);
}
