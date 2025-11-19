using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Features.Invoices;

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

    public async Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (isSynced.HasValue)
        {
            query = query.Where(x => x.IsSynced == isSynced.Value);
        }

        return await query
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date)
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return new List<IssuedInvoice>();
        }

        var searchTerm = customerName.Trim().ToLower();

        return await DbSet
            .Where(x => x.CustomerName != null && x.CustomerName.ToLower().Contains(searchTerm))
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.ErrorType != null && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired)
            .OrderByDescending(x => x.LastSyncTime ?? x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => !x.IsSynced || (x.LastSyncTime.HasValue && x.LastSyncTime.Value < beforeDate))
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date);

        var totalInvoices = await query.CountAsync(cancellationToken);
        var syncedInvoices = await query.CountAsync(x => x.IsSynced, cancellationToken);
        var unsyncedInvoices = totalInvoices - syncedInvoices;
        var invoicesWithErrors = await query.CountAsync(x => x.ErrorType.HasValue, cancellationToken);
        var criticalErrors = await query.CountAsync(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired, cancellationToken);

        var lastSyncTime = await query
            .Where(x => x.LastSyncTime.HasValue)
            .MaxAsync(x => (DateTime?)x.LastSyncTime, cancellationToken);

        return new IssuedInvoiceSyncStats
        {
            TotalInvoices = totalInvoices,
            SyncedInvoices = syncedInvoices,
            UnsyncedInvoices = unsyncedInvoices,
            InvoicesWithErrors = invoicesWithErrors,
            CriticalErrors = criticalErrors,
            LastSyncTime = lastSyncTime
        };
    }

    public override async Task<IssuedInvoice> AddAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
    {
        // Set audit fields for new entities
        entity.CreationTime = DateTime.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        return await base.AddAsync(entity, cancellationToken);
    }

    public override async Task UpdateAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
    {
        // Set audit fields for updates
        entity.LastModificationTime = DateTime.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        await base.UpdateAsync(entity, cancellationToken);
    }

    public async Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(filters.InvoiceId))
        {
            var invoiceId = filters.InvoiceId.Trim();
            query = query.Where(x => x.Id.Contains(invoiceId));
        }

        if (!string.IsNullOrWhiteSpace(filters.CustomerName))
        {
            var customerName = filters.CustomerName.Trim().ToLower();
            query = query.Where(x => x.CustomerName != null && x.CustomerName.ToLower().Contains(customerName));
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

        // Apply sorting
        query = ApplySorting(query, filters.SortBy, filters.SortDescending);

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        List<IssuedInvoice> items;
        if (filters.PageSize == 0)
        {
            // PageSize = 0 means return all items without pagination
            items = await query.ToListAsync(cancellationToken);
        }
        else
        {
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