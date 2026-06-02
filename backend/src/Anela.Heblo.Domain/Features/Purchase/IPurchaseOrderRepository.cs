using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Purchase;

public interface IPurchaseOrderRepository : IRepository<PurchaseOrder, int>
{
    Task<(List<PurchaseOrder> Orders, int TotalCount)> GetPaginatedAsync(
        string? searchTerm,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        bool? activeOrdersOnly,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrder?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default);

    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(PurchaseOrderStatus status, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PurchaseOrderLine?> GetLineByIdAsync(int lineId, CancellationToken cancellationToken = default);
}