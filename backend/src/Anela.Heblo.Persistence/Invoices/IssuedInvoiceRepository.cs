using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Invoices;

/// <summary>
/// Repository implementation for IssuedInvoice entity
/// </summary>
public class IssuedInvoiceRepository : BaseRepository<IssuedInvoice, string>, IIssuedInvoiceRepository
{
    private readonly ILogger<IssuedInvoiceRepository> _logger;

    public IssuedInvoiceRepository(ApplicationDbContext context, ILogger<IssuedInvoiceRepository> logger)
        : base(context)
    {
        _logger = logger;
    }

    public override async Task<IssuedInvoice?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.SyncHistory)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date);

        var stats = await query
            .GroupBy(x => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Synced = g.Count(x => x.IsSynced),
                WithErrors = g.Count(x => x.ErrorType.HasValue),
                Critical = g.Count(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired),
                LastSyncTime = g.Max(x => (DateTime?)x.LastSyncTime)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (stats == null)
        {
            return new IssuedInvoiceSyncStats
            {
                TotalInvoices = 0,
                SyncedInvoices = 0,
                UnsyncedInvoices = 0,
                InvoicesWithErrors = 0,
                CriticalErrors = 0,
                LastSyncTime = null
            };
        }

        return new IssuedInvoiceSyncStats
        {
            TotalInvoices = stats.Total,
            SyncedInvoices = stats.Synced,
            UnsyncedInvoices = stats.Total - stats.Synced,
            InvoicesWithErrors = stats.WithErrors,
            CriticalErrors = stats.Critical,
            LastSyncTime = stats.LastSyncTime
        };
    }

    public override async Task<IssuedInvoice> AddAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
    {
        entity.CreationTime = DateTime.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        return await base.AddAsync(entity, cancellationToken);
    }

    public override async Task UpdateAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
    {
        entity.LastModificationTime = DateTime.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        await base.UpdateAsync(entity, cancellationToken);
    }

    public async Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.InvoiceId))
        {
            var invoiceId = filters.InvoiceId.Trim();
            query = query.Where(x => x.Id.Contains(invoiceId));
        }

        if (!string.IsNullOrWhiteSpace(filters.CustomerName))
        {
            var customerName = filters.CustomerName.Trim();
            query = query.Where(x => EF.Functions.ILike(x.CustomerName!, $"%{customerName}%"));
        }

        if (filters.InvoiceDateFrom.HasValue)
        {
            query = query.Where(x => x.InvoiceDate >= filters.InvoiceDateFrom.Value.Date);
        }

        if (filters.InvoiceDateTo.HasValue)
        {
            query = query.Where(x => x.InvoiceDate <= filters.InvoiceDateTo.Value.Date);
        }

        if (filters.ShowOnlyUnsynced)
        {
            query = query.Where(x => !x.IsSynced);
        }
        else if (filters.IsSynced.HasValue)
        {
            query = query.Where(x => x.IsSynced == filters.IsSynced.Value);
        }

        if (filters.ShowOnlyWithErrors)
        {
            query = query.Where(x => x.ErrorType.HasValue);
        }

        query = ApplySorting(query, filters.SortBy, filters.SortDescending);

        List<IssuedInvoice> items;
        int totalCount;
        if (filters.PageSize == 0)
        {
            items = await query.ToListAsync(cancellationToken);
            totalCount = items.Count;
        }
        else
        {
            totalCount = await query.CountAsync(cancellationToken);
            items = await query
                .Skip((filters.PageNumber - 1) * filters.PageSize)
                .Take(filters.PageSize)
                .ToListAsync(cancellationToken);
        }

        var totalPages = filters.PageSize > 0 ? Math.Ceiling((double)totalCount / filters.PageSize) : 1;
        _logger.LogInformation("Retrieved {Count} issued invoices (page {PageNumber}/{TotalPages}, total: {TotalCount})",
            items.Count, filters.PageNumber, totalPages, totalCount);

        return new PaginatedResult<IssuedInvoice>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filters.PageNumber,
            PageSize = filters.PageSize
        };
    }

    public async Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return await DbSet
            .Where(x => x.InvoiceDate >= start && x.InvoiceDate <= end)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<IssuedInvoice> ApplySorting(IQueryable<IssuedInvoice> query, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrEmpty(sortBy))
        {
            return sortDescending
                ? query.OrderByDescending(x => x.InvoiceDate)
                : query.OrderBy(x => x.InvoiceDate);
        }

        return sortBy.ToLower() switch
        {
            "invoicedate" => sortDescending
                ? query.OrderByDescending(x => x.InvoiceDate)
                : query.OrderBy(x => x.InvoiceDate),
            "id" => sortDescending
                ? query.OrderByDescending(x => x.Id)
                : query.OrderBy(x => x.Id),
            "customername" => sortDescending
                ? query.OrderByDescending(x => x.CustomerName ?? string.Empty)
                : query.OrderBy(x => x.CustomerName ?? string.Empty),
            "price" => sortDescending
                ? query.OrderByDescending(x => x.Price)
                : query.OrderBy(x => x.Price),
            "issync" or "issynced" => sortDescending
                ? query.OrderByDescending(x => x.IsSynced)
                : query.OrderBy(x => x.IsSynced),
            "lastsynctime" => sortDescending
                ? query.OrderByDescending(x => x.LastSyncTime ?? DateTime.MinValue)
                : query.OrderBy(x => x.LastSyncTime ?? DateTime.MinValue),
            _ => sortDescending
                ? query.OrderByDescending(x => x.InvoiceDate)
                : query.OrderBy(x => x.InvoiceDate)
        };
    }
}
