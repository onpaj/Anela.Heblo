using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryRepository
    : BaseRepository<ManufacturedProductInventoryItem, int>, IManufacturedProductInventoryRepository
{
    public ManufacturedProductInventoryRepository(ApplicationDbContext context) : base(context) { }

    public async Task<ManufacturedProductInventoryItem?> GetByIdWithLogsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.Log)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<ManufacturedProductInventoryItem> Items, int TotalCount)> GetPagedListAsync(
        ManufacturedInventoryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x =>
                x.ProductCode.Contains(filter.Search) ||
                x.ProductName.Contains(filter.Search));

        if (filter.OnlyWithStock)
            query = query.Where(x => x.Amount > 0);

        if (filter.ManufactureOrderId.HasValue)
            query = query.Where(x => x.ManufactureOrderId == filter.ManufactureOrderId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(x => x.Log)
            .OrderBy(x => x.ExpirationDate)
            .ThenBy(x => x.ProductCode)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Dictionary<string, decimal>> GetTotalAmountByProductCodeAsync(
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.Amount > 0)
            .GroupBy(x => x.ProductCode)
            .Select(g => new { ProductCode = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.ProductCode, x => x.Total, cancellationToken);
    }
}
