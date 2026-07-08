namespace Anela.Heblo.Domain.Features.FinancialOverview;

/// <summary>
/// Service for calculating stock value changes using StockToDate data
/// </summary>
public interface IStockValueService
{
    /// <summary>
    /// Gets stock value changes for each warehouse type over the specified time period
    /// </summary>
    /// <param name="startDate">Start date (first day of first month)</param>
    /// <param name="endDate">End date (last day of last month)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Monthly stock value changes by warehouse type</returns>
    Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the stock value change for an arbitrary period: value(periodEndInclusive) - value(periodStart).
    /// Year/Month on the result are taken from periodStart. Used for partial-month (fair-cut) comparison.
    /// </summary>
    Task<MonthlyStockChange> GetStockValueChangeForPeriodAsync(
        DateTime periodStart,
        DateTime periodEndInclusive,
        CancellationToken cancellationToken);
}