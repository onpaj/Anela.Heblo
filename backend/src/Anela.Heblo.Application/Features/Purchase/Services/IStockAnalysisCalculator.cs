using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Interface for calculating stock analysis metrics such as efficiency and recommended order quantity.
/// </summary>
public interface IStockAnalysisCalculator
{
    /// <summary>
    /// Calculates the stock efficiency percentage based on available stock relative to optimal or minimum stock.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <returns>Stock efficiency percentage</returns>
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);

    /// <summary>
    /// Calculates the recommended order quantity needed to reach the target stock level.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="moq">Minimum order quantity as configured for the material</param>
    /// <returns>Recommended order quantity, or null if no order is needed</returns>
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);

    /// <summary>
    /// Analyzes a single material stock snapshot, computing consumption, stockout, efficiency,
    /// severity, and recommended order quantity for the given period.
    /// </summary>
    /// <param name="item">The material stock snapshot to analyze</param>
    /// <param name="fromDate">Start of the analysis period</param>
    /// <param name="toDate">End of the analysis period</param>
    /// <returns>The fully-computed analysis item</returns>
    StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate);

    /// <summary>
    /// Filters analyzed stock items by configured-status and stock-status request filters.
    /// </summary>
    /// <param name="items">Analyzed items to filter</param>
    /// <param name="request">The request carrying the filter criteria</param>
    /// <returns>The filtered item list</returns>
    List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request);

    /// <summary>
    /// Sorts analyzed stock items by the requested sort key and direction.
    /// </summary>
    /// <param name="items">Items to sort</param>
    /// <param name="sortBy">The field to sort by</param>
    /// <param name="descending">Whether to reverse the ascending order</param>
    /// <returns>The sorted item list</returns>
    List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending);
}
