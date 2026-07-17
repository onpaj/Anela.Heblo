using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public interface IManufacturedProductInventoryRepository : IRepository<ManufacturedProductInventoryItem, int>
{
    Task<ManufacturedProductInventoryItem?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads existing inventory rows (with their logs) for the given product codes, so the write-down
    /// path can merge into a matching product+lot+expiration row and detect already-written orders.
    /// </summary>
    Task<IReadOnlyList<ManufacturedProductInventoryItem>> GetByProductCodesWithLogsAsync(
        IReadOnlyCollection<string> productCodes, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ManufacturedProductInventoryItem> Items, int TotalCount)> GetPagedListAsync(
        ManufacturedInventoryFilter filter, CancellationToken cancellationToken = default);
    Task<Dictionary<string, decimal>> GetTotalAmountByProductCodeAsync(CancellationToken cancellationToken = default);
}
