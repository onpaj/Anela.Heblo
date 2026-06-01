using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Interface for calculating stock severity levels.
/// </summary>
public interface IStockSeverityCalculator
{
    /// <summary>
    /// Determines the severity level of stock based on available stock and configured thresholds.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <param name="isMinConfigured">Whether minimum stock level is configured</param>
    /// <param name="isOptimalConfigured">Whether optimal stock level is configured</param>
    /// <returns>Stock severity level</returns>
    StockSeverity DetermineStockSeverity(
        double availableStock,
        double minStock,
        double optimalStock,
        bool isMinConfigured,
        bool isOptimalConfigured);
}