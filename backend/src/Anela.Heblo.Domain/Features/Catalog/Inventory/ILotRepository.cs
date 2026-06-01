using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface ILotRepository : IRepository<Lot, int>
{
    Task<Lot?> GetByIdWithEansAsync(int id, CancellationToken ct);
    Task<PagedResult<Lot>> GetPaginatedAsync(string? materialCode, DateOnly? expirationFrom, DateOnly? expirationTo, int page, int pageSize, CancellationToken ct);
    Task<bool> ExistsAsync(string materialCode, string lotCode, CancellationToken ct);
}
