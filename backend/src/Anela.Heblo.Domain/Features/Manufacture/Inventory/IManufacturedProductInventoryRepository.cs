using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public interface IManufacturedProductInventoryRepository : IRepository<ManufacturedProductInventoryItem, int>
{
    Task<ManufacturedProductInventoryItem?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ManufacturedProductInventoryItem> Items, int TotalCount)> GetPagedListAsync(
        ManufacturedInventoryFilter filter, CancellationToken cancellationToken = default);
    Task<Dictionary<string, decimal>> GetTotalAmountByProductCodeAsync(CancellationToken cancellationToken = default);
}
