using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Persistence.Invoices;

/// <summary>
/// Repository interface for IssuedInvoice entity
/// Provides specialized query operations beyond basic CRUD
/// </summary>
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    /// <summary>
    /// Finds invoices by their synchronization status
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices within a date range
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices by customer name (partial match)
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices with sync errors (excluding invoice paired errors)
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an invoice with its complete sync history
    /// </summary>
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invoices that were last synced before a specific date
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync statistics for a date range
    /// </summary>
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated and filtered list of issued invoices with sorting
    /// </summary>
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invoice headers for a specific date
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
