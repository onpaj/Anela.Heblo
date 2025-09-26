using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderRepository : IManufactureOrderRepository
{
    private readonly ApplicationDbContext _context;

    public ManufactureOrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ManufactureOrder>> GetOrdersAsync(
        ManufactureOrderState? state = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? responsiblePerson = null,
        string? orderNumber = null,
        string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ManufactureOrders
            .Include(x => x.SemiProduct)
            .Include(x => x.Products)
            .Include(x => x.Notes)
            .Include(x => x.AuditLog)
            .AsQueryable();

        if (state.HasValue)
        {
            query = query.Where(x => x.State == state.Value);
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(x => x.SemiProductPlannedDate >= dateFrom.Value || x.ProductPlannedDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(x => x.SemiProductPlannedDate <= dateTo.Value || x.ProductPlannedDate <= dateTo.Value);
        }

        if (!string.IsNullOrEmpty(responsiblePerson))
        {
            query = query.Where(x => x.ResponsiblePerson != null && x.ResponsiblePerson.Contains(responsiblePerson));
        }

        if (!string.IsNullOrEmpty(orderNumber))
        {
            query = query.Where(x => x.OrderNumber.Contains(orderNumber));
        }

        if (!string.IsNullOrEmpty(productCode))
        {
            query = query.Where(x =>
                (x.SemiProduct != null && x.SemiProduct.ProductCode.Contains(productCode)) ||
                x.Products.Any(p => p.ProductCode.Contains(productCode)));
        }

        return await query
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<ManufactureOrder?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ManufactureOrders
            .Include(x => x.SemiProduct)
            .Include(x => x.Products)
            .Include(x => x.Notes.OrderByDescending(n => n.CreatedAt))
            .Include(x => x.AuditLog.OrderByDescending(a => a.Timestamp))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ManufactureOrder> AddOrderAsync(ManufactureOrder order, CancellationToken cancellationToken = default)
    {
        _context.ManufactureOrders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<ManufactureOrder> UpdateOrderAsync(ManufactureOrder order, CancellationToken cancellationToken = default)
    {
        _context.ManufactureOrders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task DeleteOrderAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await _context.ManufactureOrders.FindAsync(new object[] { id }, cancellationToken);
        if (order != null)
        {
            _context.ManufactureOrders.Remove(order);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = DateTime.Now.Year;
        var prefix = $"MO-{currentYear}-";

        var lastOrderNumber = await _context.ManufactureOrders
            .Where(x => x.OrderNumber.StartsWith(prefix))
            .OrderByDescending(x => x.OrderNumber)
            .Select(x => x.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int nextSequence = 1;
        if (lastOrderNumber != null)
        {
            var sequencePart = lastOrderNumber.Substring(prefix.Length);
            if (int.TryParse(sequencePart, out int lastSequence))
            {
                nextSequence = lastSequence + 1;
            }
        }

        return $"{prefix}{nextSequence:D3}"; // Format as 001, 002, etc.
    }

    public async Task<List<ManufactureOrder>> GetOrdersForDateRangeAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        return await _context.ManufactureOrders
            .Include(x => x.SemiProduct)
            .Include(x => x.Products)
            .Include(x => x.Notes)
            .Include(x => x.AuditLog)
            .Where(x => (x.SemiProductPlannedDate >= startDate && x.SemiProductPlannedDate <= endDate) ||
                       (x.ProductPlannedDate >= startDate && x.ProductPlannedDate <= endDate))
            .OrderBy(x => x.SemiProductPlannedDate)
            .ThenBy(x => x.ProductPlannedDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ManufactureOrders
            .Where(order => order.State != ManufactureOrderState.Completed && order.State != ManufactureOrderState.Cancelled && order.State != ManufactureOrderState.Draft)
            .Include(order => order.Products)
            .SelectMany(order => order.Products)
            .GroupBy(product => product.ProductCode)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(product => product.PlannedQuantity), cancellationToken);
    }
}