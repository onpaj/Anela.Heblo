using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Features.Invoices.Contracts;

/// <summary>
/// Repository interface for IssuedInvoice entity
/// Provides specialized query operations beyond basic CRUD
/// </summary>
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    /// <summary>
    /// Gets an invoice with its complete sync history
    /// </summary>
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

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
