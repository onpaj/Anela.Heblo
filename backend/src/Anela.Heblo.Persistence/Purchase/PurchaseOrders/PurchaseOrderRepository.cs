using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Purchase.PurchaseOrders;

public class PurchaseOrderRepository : BaseRepository<PurchaseOrder, int>, IPurchaseOrderRepository
{
    public PurchaseOrderRepository(ApplicationDbContext context) : base(context)
    {
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
        var query = DbSet.AsQueryable();

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

        var totalCount = await query.CountAsync(cancellationToken);

        query = sortBy.ToLowerInvariant() switch
        {
            "ordernumber" => sortDescending ? query.OrderByDescending(x => x.OrderNumber) : query.OrderBy(x => x.OrderNumber),
            "orderdate" => sortDescending ? query.OrderByDescending(x => x.OrderDate) : query.OrderBy(x => x.OrderDate),
            "status" => sortDescending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "totalamount" => sortDescending ? query.OrderByDescending(x => x.Lines.Sum(l => l.LineTotal)) : query.OrderBy(x => x.Lines.Sum(l => l.LineTotal)),
            "createdat" => sortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => sortDescending ? query.OrderByDescending(x => x.OrderDate) : query.OrderBy(x => x.OrderDate)
        };

        var orders = await query
            .Include(x => x.Lines)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (orders, totalCount);
    }

    public async Task<PurchaseOrder?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.Lines)
            .Include(x => x.History)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(x => x.OrderNumber == orderNumber, cancellationToken);
    }

    public async Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(order => order.Status == PurchaseOrderStatus.Draft || order.Status == PurchaseOrderStatus.InTransit)
            .Include(order => order.Lines)
            .SelectMany(order => order.Lines)
            .GroupBy(line => line.MaterialId)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(line => line.Quantity), cancellationToken);
    }

    public async Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(PurchaseOrderStatus status, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(order => order.Status == status)
            .Include(order => order.Lines)
            .ToListAsync(cancellationToken);
    }
}