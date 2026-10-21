using Anela.Heblo.Application.Features.Manufacture.Contracts;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IProductionActivityAnalyzer
{
    /// <summary>
    /// Determines if a product is in active production based on recent manufacture history.
    /// </summary>
    /// <param name="manufactureHistory">Collection of manufacture records</param>
    /// <param name="dayThreshold">Number of days to look back for active production (default: 30)</param>
    /// <returns>True if product has been manufactured within the threshold period</returns>
    bool IsInActiveProduction(IEnumerable<ProductManufactureRecordSnapshot> manufactureHistory, int dayThreshold = 30);

    /// <summary>
    /// Gets the date of the last production run.
    /// </summary>
    /// <param name="manufactureHistory">Collection of manufacture records</param>
    /// <returns>Date of last production, or null if never manufactured</returns>
    DateTime? GetLastProductionDate(IEnumerable<ProductManufactureRecordSnapshot> manufactureHistory);

    /// <summary>
    /// Calculates the average production frequency in days.
    /// </summary>
    /// <param name="manufactureHistory">Collection of manufacture records</param>
    /// <param name="analysisMonths">Number of months to analyze (default: 12)</param>
    /// <returns>Average days between production runs</returns>
    double CalculateAverageProductionFrequency(IEnumerable<ProductManufactureRecordSnapshot> manufactureHistory, int analysisMonths = 12);
}