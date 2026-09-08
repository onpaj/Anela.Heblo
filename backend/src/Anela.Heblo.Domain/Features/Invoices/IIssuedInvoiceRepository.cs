using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Invoices;

public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);

    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns daily invoice counts in the inclusive range [<paramref name="startDate"/>, <paramref name="endDate"/>].
    /// Missing dates are gap-filled with zero-count rows. <c>Date</c> values on the result are tagged
    /// <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);

    Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
}
