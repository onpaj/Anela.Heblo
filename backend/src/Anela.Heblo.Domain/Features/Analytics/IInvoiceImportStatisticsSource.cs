namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module. Implemented by the
/// Invoices module via <c>InvoiceImportStatisticsSourceAdapter</c>; DI registration
/// lives in <c>InvoicesModule</c>. Mirrors the inversion pattern in
/// <c>docs/architecture/development_guidelines.md</c> ("Cross-Module Communication
/// Example") and the precedent in <see cref="IAnalyticsProductSource"/>.
/// </summary>
public interface IInvoiceImportStatisticsSource
{
    /// <summary>
    /// Returns daily invoice counts in the inclusive range
    /// [<paramref name="startDate"/>, <paramref name="endDate"/>]. Missing dates
    /// are gap-filled with zero-count rows. <c>Date</c> values are tagged
    /// <see cref="DateTimeKind.Utc"/> and <c>IsBelowThreshold</c> is always
    /// <c>false</c> (the consumer decides thresholds).
    /// </summary>
    Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);
}
