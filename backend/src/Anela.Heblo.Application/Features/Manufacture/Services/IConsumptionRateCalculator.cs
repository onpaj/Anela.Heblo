using Anela.Heblo.Domain.Features.Catalog.Sales;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IConsumptionRateCalculator
{
    /// <summary>
    /// Calculates the daily consumption rate based on sales history for a given time period.
    /// </summary>
    /// <param name="salesHistory">Collection of sales records to analyze</param>
    /// <param name="fromDate">Start date of the analysis period</param>
    /// <param name="toDate">End date of the analysis period</param>
    /// <returns>Daily sales rate (pieces per day)</returns>
    double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, DateTime fromDate, DateTime toDate);

    /// <summary>
    /// Calculates consumption rate from consumed materials history.
    /// </summary>
    /// <param name="consumedHistory">Collection of consumed material records</param>
    /// <param name="fromDate">Start date of the analysis period</param>
    /// <param name="toDate">End date of the analysis period</param>
    /// <returns>Daily consumption rate (pieces per day)</returns>
    double CalculateDailyConsumptionRate(IEnumerable<Domain.Features.Catalog.ConsumedMaterials.ConsumedMaterialRecord> consumedHistory, DateTime fromDate, DateTime toDate);

    /// <summary>
    /// Calculates stock days available based on current stock and consumption rate.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="dailyConsumptionRate">Daily consumption rate</param>
    /// <returns>Number of days the current stock will last</returns>
    double CalculateStockDaysAvailable(decimal availableStock, double dailyConsumptionRate);
}