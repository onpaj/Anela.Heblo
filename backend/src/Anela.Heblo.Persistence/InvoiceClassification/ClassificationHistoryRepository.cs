using Microsoft.EntityFrameworkCore;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Persistence.InvoiceClassification;

public class ClassificationHistoryRepository : IClassificationHistoryRepository
{
    private readonly ApplicationDbContext _context;

    public ClassificationHistoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClassificationHistory> AddAsync(ClassificationHistory history)
    {
        _context.ClassificationHistory.Add(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
        int page = 1,
        int pageSize = 20,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? invoiceNumber = null,
        string? companyName = null)
    {
        var query = _context.ClassificationHistory
            .Include(h => h.ClassificationRule)
            .AsQueryable();

        // Apply filters
        if (fromDate.HasValue)
            query = query.Where(h => h.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
        {
            // Include the full end day: toDate is sent as midnight (00:00:00), so we extend to the start of the next day
            var endOfDay = toDate.Value.Date.AddDays(1);
            query = query.Where(h => h.Timestamp < endOfDay);
        }

        if (!string.IsNullOrEmpty(invoiceNumber))
            query = query.Where(h => h.AbraInvoiceId.Contains(invoiceNumber));

        if (!string.IsNullOrEmpty(companyName))
            query = query.Where(h => h.CompanyName.Contains(companyName));

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(h => h.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}