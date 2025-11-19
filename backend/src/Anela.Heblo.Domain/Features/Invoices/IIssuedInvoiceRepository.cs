using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Invoices;

/// <summary>
/// Repository interface for IssuedInvoice entity
/// Provides specialized query operations beyond basic CRUD
/// </summary>
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    /// <summary>
    /// Finds invoices by their synchronization status
    /// </summary>
    /// <param name="isSynced">True for synced invoices, false for unsynced, null for all</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching invoices</returns>
    Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices within a date range
    /// </summary>
    /// <param name="fromDate">Start date (inclusive)</param>
    /// <param name="toDate">End date (inclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching invoices</returns>
    Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices by customer name (partial match)
    /// </summary>
    /// <param name="customerName">Customer name or partial name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching invoices</returns>
    Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices with sync errors (excluding invoice paired errors)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of invoices with critical errors</returns>
    Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an invoice with its complete sync history
    /// </summary>
    /// <param name="id">Invoice ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Invoice with sync history or null if not found</returns>
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invoices that were last synced before a specific date
    /// </summary>
    /// <param name="beforeDate">Date threshold</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of invoices that need re-sync</returns>
    Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync statistics for a date range
    /// </summary>
    /// <param name="fromDate">Start date</param>
    /// <param name="toDate">End date</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync statistics</returns>
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated and filtered list of issued invoices with sorting
    /// </summary>
    /// <param name="filters">Filter criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated result with total count</returns>
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics for invoice synchronization
/// </summary>
public class IssuedInvoiceSyncStats
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate => TotalInvoices > 0 ? (decimal)SyncedInvoices / TotalInvoices * 100 : 0;
}

/// <summary>
/// Filter criteria for issued invoices
/// </summary>
public class IssuedInvoiceFilters
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public string? InvoiceId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime? InvoiceDateFrom { get; set; }
    public DateTime? InvoiceDateTo { get; set; }
    public bool? IsSynced { get; set; }
    public bool ShowOnlyUnsynced { get; set; }
    public bool ShowOnlyWithErrors { get; set; }
}

/// <summary>
/// Paginated result container
/// </summary>
/// <typeparam name="T">Type of items</typeparam>
public class PaginatedResult<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;
}