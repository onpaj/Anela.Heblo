namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module. Implemented by the
/// Bank module via <c>BankStatementStatisticsSourceAdapter</c>; DI registration
/// lives in <c>BankModule</c>. Mirrors the inversion pattern in
/// <c>docs/architecture/development_guidelines.md</c> ("Cross-Module Communication
/// Example") and the precedent in <see cref="IAnalyticsProductSource"/>.
/// </summary>
public interface IBankStatementStatisticsSource
{
    /// <summary>
    /// Returns daily bank statement statistics in the inclusive range
    /// [<paramref name="startDate"/>, <paramref name="endDate"/>]. Missing dates
    /// are gap-filled with zero-count, zero-total rows. <c>Date</c> values are
    /// tagged <see cref="DateTimeKind.Utc"/>. <c>TotalItemCount</c> is the
    /// per-day sum of <c>BankStatementImport.ItemCount</c>.
    /// </summary>
    Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default);
}
