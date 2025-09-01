using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Xcc.Persistance;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Anela.Heblo.Application.Features.Purchase.Infrastructure;

public class InMemoryPurchaseOrderRepository : EmptyRepository<PurchaseOrder, int>, IPurchaseOrderRepository
{
    private readonly ConcurrentDictionary<int, PurchaseOrder> _orders = new();
    private int _nextId = 1;

    public new async Task<PurchaseOrder> AddAsync(PurchaseOrder entity, CancellationToken cancellationToken = default)
    {
        // Simulate auto-increment ID assignment for in-memory implementation
        var id = Interlocked.Increment(ref _nextId);
        typeof(PurchaseOrder).GetProperty("Id")!.SetValue(entity, id);
        _orders.TryAdd(id, entity);
        return await Task.FromResult(entity);
    }

    public new async Task<PurchaseOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(id, out var order);
        return await Task.FromResult(order);
    }

    public async Task<PurchaseOrder?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(id, out var order);
        return await Task.FromResult(order);
    }

    public async Task<(List<PurchaseOrder> Orders, int TotalCount)> GetPaginatedAsync(
        string? searchTerm,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        int? supplierId,
        bool? activeOrdersOnly,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        var query = _orders.Values.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(x =>
                x.OrderNumber.Contains(searchTerm) ||
                (x.Notes != null && x.Notes.Contains(searchTerm)));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PurchaseOrderStatus>(status, out var statusEnum))
        {
            query = query.Where(x => x.Status == statusEnum);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.OrderDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.OrderDate <= toDate.Value);
        }

        if (supplierId.HasValue)
        {
            // Note: SupplierId filtering is disabled as we now use SupplierName
            // In future, implement supplier name filtering if needed
        }

        if (activeOrdersOnly.HasValue && activeOrdersOnly.Value)
        {
            query = query.Where(x => x.Status != PurchaseOrderStatus.Completed);
        }

        var totalCount = query.Count();

        query = sortBy.ToLowerInvariant() switch
        {
            "ordernumber" => sortDescending ? query.OrderByDescending(x => x.OrderNumber) : query.OrderBy(x => x.OrderNumber),
            "orderdate" => sortDescending ? query.OrderByDescending(x => x.OrderDate) : query.OrderBy(x => x.OrderDate),
            "status" => sortDescending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "totalamount" => sortDescending ? query.OrderByDescending(x => x.TotalAmount) : query.OrderBy(x => x.TotalAmount),
            "createdat" => sortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => sortDescending ? query.OrderByDescending(x => x.OrderDate) : query.OrderBy(x => x.OrderDate)
        };

        var orders = query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return await Task.FromResult((orders, totalCount));
    }

    public async Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        var exists = _orders.Values.Any(x => x.OrderNumber == orderNumber);
        return await Task.FromResult(exists);
    }

    public new async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var removed = _orders.TryRemove(id, out _);
        return await Task.FromResult(removed);
    }

    public new async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // No-op for in-memory implementation
        return await Task.FromResult(1);
    }
}