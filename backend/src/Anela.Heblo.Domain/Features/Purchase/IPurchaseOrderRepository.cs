using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Purchase;

public interface IPurchaseOrderRepository : IRepository<PurchaseOrder, Guid>
{
    Task<(List<PurchaseOrder> Orders, int TotalCount)> GetPaginatedAsync(
        string? searchTerm,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        Guid? supplierId,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrder?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default);
}